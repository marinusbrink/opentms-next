using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.EntityFrameworkCore;
using OpenTms.Features;
using OpenTms.Integrations.Features;
using OpenTms.Integrations.OrderIntake;
using OpenTms.Integrations.OrderIntake.Profiles;
using OpenTms.Orders.Intake;
using OpenTms.Orders.Published;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.Features;
using OpenTms.PostgreSql.ReferenceData;
using Shouldly;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.Intake;

using DocumentFormat = OpenTms.Integrations.OrderIntake.DocumentFormat;

/// <summary>
/// The design 72 review fixes that only real PostgreSQL can show (transactions, savepoints, ILIKE, concurrency):
/// <list type="bullet">
/// <item>M2: the review list and the Received list search with ILIKE, their own % and _ matched literally, and read no jsonb
/// column.</item>
/// <item>B1 / minor 2: the set-based use stamps (ExecuteUpdate) write at most every five minutes and leave the concurrency
/// stamp alone.</item>
/// <item>Minor 7: a duplicate insert inside a transaction answers false and the transaction stays usable (savepoint); two
/// concurrent recordings of one message store it once and the loser goes on in its own transaction.</item>
/// <item>M1: an unexpected exception while submitting fails the job with nothing half-written (the first document's intake
/// is rolled back with the second's failure), records SubmitFailed in a fresh transaction and leaves the documents Pending,
/// so the retry processes both.</item>
/// </list>
/// Risk class: High (order chain, jobs); Critical where a broken transaction could commit half an order chain.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class OrderIntakeReviewFixesPostgreSqlTests : ReferenceDataPostgreSqlTestBase
{
    private static Guid Committing => PostgreSqlTestDatabases.TransportTenantAId;

    private static IDisposable IntakeOn()
    {
        var all = new[]
        {
            TestFeatureValueProvider.Override(IntegrationsFeatures.OrderIntake, "true"),
            TestFeatureValueProvider.Override(IntegrationsFeatures.OrderIntakeAiMapping, "false"),
            TestFeatureValueProvider.Override(AppFeatures.Integrations, "true")
        };
        return new All(all);
    }

    private sealed class All(IDisposable[] all) : IDisposable
    {
        public void Dispose()
        {
            foreach (var d in all.Reverse())
            {
                d.Dispose();
            }
        }
    }

    private static string Unique(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..10];

    private async Task<T> CommittedAsync<T>(Guid tenant, Func<Task<T>> action)
    {
        using (CurrentTenant.Change(tenant))
        {
            using var unitOfWork = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
            var result = await action();
            await unitOfWork.CompleteAsync();
            return result;
        }
    }

    private Task CommittedAsync(Guid tenant, Func<Task> action) => CommittedAsync(tenant, async () =>
    {
        await action();
        return true;
    });

    /* ---------- M2: ILIKE search, projection ---------- */

    [Fact]
    public async Task The_review_list_searches_with_ilike_matches_wildcards_literally_and_reads_no_jsonb()
    {
        using var features = IntakeOn();
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var tag = Unique("zz");
            var repository = GetRequiredService<IRepository<OrderIntake, Guid>>();
            foreach (var subject in new[] { tag + " 50% korting", tag + " A_B Venlo", tag + " AxB Venlo" })
            {
                await repository.InsertAsync(new OrderIntake(Guid.NewGuid(), TenantA, Guid.NewGuid(), 0, "Api", OrderSource.Api, "{\"customerReference\":\"R\",\"lines\":[]}",
                    new OrderIntakeChannelSettings("orders@acme.nl", subject, "order.json", 0.9m, false, false, null, null, null)));
            }

            await FlushAsync();
            var review = GetRequiredService<IOrderIntakeAppService>();

            async Task<List<string?>> SearchAsync(string term)
                => (await review.GetListAsync(new OrderIntakeListRequestDto { WildcardSearch = term })).Rows.Select(r => r.Subject).ToList();

            (await SearchAsync(tag + " 50%")).ShouldBe([tag + " 50% korting"]);
            (await SearchAsync("% korting")).ShouldBe([tag + " 50% korting"], "% is literal: '% korting' is not 'anything korting'");
            (await SearchAsync(tag + " a_b")).ShouldBe([tag + " A_B Venlo"], "_ is literal and the match ignores case");
            (await SearchAsync(tag.ToUpperInvariant() + " AXB")).ShouldBe([tag + " AxB Venlo"]);
            (await SearchAsync(tag)).Count.ShouldBe(3);

            using var capture = EfCommandCapture.Begin();
            await Task.Delay(EfCommandCapture.LoggingCacheTime);
            (await SearchAsync("b venlo")).Count.ShouldBe(2);
            var reads = capture.On("OrderIntakes");
            reads.ShouldNotBeEmpty("control: the list query was captured");
            reads.ShouldContain(sql => sql.Contains("ILIKE"), "the trigram indexes serve ILIKE on the raw columns");
            foreach (var sql in reads)
            {
                sql.ShouldNotContain("lower(");
                sql.ShouldNotContain("\"Payload\"");
                sql.ShouldNotContain("\"Resolution\"");
                sql.ShouldNotContain("\"ReviewedPayload\"");
            }
        });
    }

    [Fact]
    public async Task The_received_list_searches_with_ilike_matches_wildcards_literally_and_reads_no_jsonb()
    {
        using var features = IntakeOn();
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var tag = Unique("yy");
            var messages = GetRequiredService<IRepository<InboundMessage, Guid>>();
            var source = Guid.NewGuid();
            foreach (var (subject, from) in new[] { (tag + " 50% korting", "a@acme.nl"), (tag + " A_B Venlo", "b@acme.nl"), (tag + " AxB Venlo", "c@other_x.nl") })
            {
                await messages.InsertAsync(new InboundMessage(Guid.NewGuid(), TenantA, InboundChannel.Gmail, source, Unique("ext"), from, subject, DateTime.UtcNow, 10));
            }

            await FlushAsync();
            var service = GetRequiredService<IInboundMessageAppService>();

            async Task<List<string?>> SearchAsync(string term)
                => (await service.GetListAsync(new InboundMessageListRequestDto { WildcardSearch = term, SourceId = source })).Rows.Select(r => r.Subject).ToList();

            (await SearchAsync("% korting")).ShouldBe([tag + " 50% korting"]);
            (await SearchAsync("A_B")).ShouldBe([tag + " A_B Venlo"]);
            (await SearchAsync("OTHER_X")).ShouldBe([tag + " AxB Venlo"], "the sender, case-insensitively, _ literal");
            (await SearchAsync(tag)).Count.ShouldBe(3);

            using var capture = EfCommandCapture.Begin();
            await Task.Delay(EfCommandCapture.LoggingCacheTime);
            (await SearchAsync("venlo")).Count.ShouldBe(2);
            capture.On("InboundMessages").ShouldContain(sql => sql.Contains("ILIKE"));
            foreach (var sql in capture.Commands)
            {
                sql.ShouldNotContain("\"Mapping\"");
                sql.ShouldNotContain("\"Parameters\"");
            }
        });
    }

    /* ---------- B1 / minor 2: the set-based use stamps ---------- */

    [Fact]
    public async Task The_use_stamps_are_set_based_throttled_and_leave_the_concurrency_stamp_alone()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var client = new ApiClient(Guid.NewGuid(), TenantA, "Use stamp", "otms_" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant(), "abcd", DateTime.UtcNow, null, null, false);
            await GetRequiredService<IRepository<ApiClient, Guid>>().InsertAsync(client);
            var profile = new ImportProfile(Guid.NewGuid(), TenantA, "Use stamp", "x@acme.nl", DocumentFormat.Csv, OrderIntakeConsts.TransportOrderTarget,
                new ImportProfileRules { Fields = [new FieldRule { Target = "order.customerReference", Source = new FieldSource { Kind = FieldSourceKind.Constant, Value = "x" } }] },
                ["Ref"], ProfileOrigin.Ai, "claude-opus-5-5", Guid.NewGuid());
            await GetRequiredService<IRepository<ImportProfile, Guid>>().InsertAsync(profile);
            await FlushAsync();

            var interval = OrderIntakeConsts.UsageStampInterval;
            var t0 = new DateTime(2026, 11, 3, 12, 0, 0);
            var clients = GetRequiredService<IApiClientRepository>();
            var profiles = GetRequiredService<IImportProfileRepository>();

            (await clients.RecordUseAsync(client.Id, t0, t0 - interval)).ShouldBeTrue();
            (await clients.RecordUseAsync(client.Id, t0.AddMinutes(4), t0.AddMinutes(4) - interval)).ShouldBeFalse();
            (await clients.RecordUseAsync(client.Id, t0.AddMinutes(6), t0.AddMinutes(6) - interval)).ShouldBeTrue();
            (await profiles.RecordUseAsync(profile.Id, t0, t0 - interval)).ShouldBeTrue();
            (await profiles.RecordUseAsync(profile.Id, t0.AddMinutes(1), t0.AddMinutes(1) - interval)).ShouldBeFalse();

            using (CurrentTenant.Change(SharedTenant))
            {
                (await clients.RecordUseAsync(client.Id, t0.AddDays(1), t0.AddDays(1) - interval)).ShouldBeFalse("another tenant reaches nothing");
                (await profiles.RecordUseAsync(profile.Id, t0.AddDays(1), t0.AddDays(1) - interval)).ShouldBeFalse();
            }

            var context = await GetRequiredService<Volo.Abp.EntityFrameworkCore.IDbContextProvider<OpenTms.Integrations.EntityFrameworkCore.IntegrationsDbContext>>().GetDbContextAsync();
            context.ChangeTracker.Clear();
            var storedClient = await GetRequiredService<IRepository<ApiClient, Guid>>().GetAsync(client.Id);
            (storedClient.LastUsedAt, storedClient.ConcurrencyStamp).ShouldBe(((DateTime?)t0.AddMinutes(6), client.ConcurrencyStamp));
            var storedProfile = await GetRequiredService<IRepository<ImportProfile, Guid>>().GetAsync(profile.Id);
            (storedProfile.LastUsedAt, storedProfile.ConcurrencyStamp).ShouldBe(((DateTime?)t0, profile.ConcurrencyStamp));
        });
    }

    /* ---------- Minor 7: duplicate-safe recording ---------- */

    [Fact]
    public async Task A_duplicate_insert_inside_a_transaction_answers_false_and_the_transaction_stays_usable()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var messages = GetRequiredService<IInboundMessageRepository>();
            var source = Guid.NewGuid();
            InboundMessage Message(string externalId) => new(Guid.NewGuid(), TenantA, InboundChannel.Api, source, externalId, null, "dup", DateTime.UtcNow, 10);

            (await messages.TryInsertAsync(Message("dup-1"))).ShouldBeTrue();
            (await messages.TryInsertAsync(Message("dup-1"))).ShouldBeFalse("the unique index refused it");

            /* Without the savepoint PostgreSQL would answer 25P02 (current transaction is aborted) from here on. */
            (await messages.TryInsertAsync(Message("dup-2"))).ShouldBeTrue();
            (await messages.CountAsync(m => m.SourceId == source)).ShouldBe(2);
        });
    }

    private static InboundMessageRecording Recording(Guid source, string externalId) => new()
    {
        Channel = InboundChannel.Api,
        SourceId = source,
        ExternalId = externalId,
        ReceivedAt = DateTime.UtcNow,
        RawContent = Encoding.UTF8.GetBytes("raw " + externalId),
        Documents =
        {
            new InboundDocumentRecording
            {
                Kind = InboundDocumentKind.ApiFile, FileName = "orders.csv", ContentType = "text/csv",
                Content = Encoding.UTF8.GetBytes("Ref;Van\nR-1;1234AB\n"), Format = DocumentFormat.Csv
            }
        }
    };

    [Fact]
    public async Task Two_concurrent_recordings_of_one_message_store_it_once_and_the_loser_goes_on_in_its_transaction()
    {
        using var features = IntakeOn();
        /* The tenant's blob container exists already (any earlier message created it); see the cold-container test below. */
        await CommittedAsync(Committing, () => GetRequiredService<IInboundMessageRecorder>().RecordAsync(Recording(Guid.NewGuid(), Unique("warm"))));
        await ConcurrentDuplicateAsync(Committing);
    }

    [Fact]
    public async Task Two_concurrent_recordings_of_a_tenants_very_first_message_store_it_once_and_the_tenant_records_on()
    {
        /* A shared-tier tenant that has never stored a blob: both recordings create the "opentms-inbound" container row. */
        using var features = IntakeOn();
        var tenant = Guid.NewGuid();
        await ConcurrentDuplicateAsync(tenant);

        /* Every later message of the tenant still records and its documents still read back. */
        var next = await CommittedAsync(tenant, () => GetRequiredService<IInboundMessageRecorder>().RecordAsync(Recording(Guid.NewGuid(), Unique("after"))));
        next.IsNew.ShouldBeTrue();
        await CommittedAsync(tenant, async () =>
        {
            var document = (await GetRequiredService<IRepository<InboundDocument, Guid>>().GetListAsync(d => d.InboundMessageId == next.InboundMessageId)).Single();
            (await GetRequiredService<Volo.Abp.BlobStoring.IBlobContainer<InboundDocumentContainer>>().GetAllBytesOrNullAsync(document.BlobName)).ShouldNotBeNull();
        });
    }

    [Fact]
    public async Task Two_overlapping_recordings_of_different_messages_in_one_tenant_both_succeed()
    {
        /* Two API calls or two instances' mailbox syncs of one tenant at the same time (design 72 review B1: concurrent submits
         * must both succeed). Each recording stores blobs in the tenant's one "opentms-inbound" container. */
        using var features = IntakeOn();
        await CommittedAsync(Committing, () => GetRequiredService<IInboundMessageRecorder>().RecordAsync(Recording(Guid.NewGuid(), Unique("warm"))));
        var firstInserted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Task.Run(async () =>
        {
            using (CurrentTenant.Change(Committing))
            {
                using var unitOfWork = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
                var result = await GetRequiredService<IInboundMessageRecorder>().RecordAsync(Recording(Guid.NewGuid(), Unique("overlap-a")));
                firstInserted.SetResult();
                await releaseFirst.Task;
                await unitOfWork.CompleteAsync();
                return result;
            }
        });
        await firstInserted.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var second = Task.Run(async () =>
        {
            using (CurrentTenant.Change(Committing))
            {
                using var unitOfWork = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
                var result = await GetRequiredService<IInboundMessageRecorder>().RecordAsync(Recording(Guid.NewGuid(), Unique("overlap-b")));
                await unitOfWork.CompleteAsync();
                return result;
            }
        });

        await Task.Delay(TimeSpan.FromMilliseconds(750));
        releaseFirst.SetResult();

        (await first.WaitAsync(TimeSpan.FromSeconds(30))).IsNew.ShouldBeTrue();
        (await second.WaitAsync(TimeSpan.FromSeconds(30))).IsNew.ShouldBeTrue("a different message of the same tenant must not conflict with the first");
    }

    private async Task ConcurrentDuplicateAsync(Guid tenant)
    {
        var Committing = tenant;
        var source = Guid.NewGuid();
        var externalId = Unique("conc");
        var firstInserted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Task.Run(async () =>
        {
            using (CurrentTenant.Change(Committing))
            {
                using var unitOfWork = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
                var result = await GetRequiredService<IInboundMessageRecorder>().RecordAsync(Recording(source, externalId));
                firstInserted.SetResult();
                await releaseFirst.Task;
                await unitOfWork.CompleteAsync();
                return result;
            }
        });
        await firstInserted.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var second = Task.Run(async () =>
        {
            using (CurrentTenant.Change(Committing))
            {
                using var unitOfWork = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
                var recorder = GetRequiredService<IInboundMessageRecorder>();
                var duplicate = await recorder.RecordAsync(Recording(source, externalId)); /* waits on the unique index */
                var other = await recorder.RecordAsync(Recording(source, externalId + "-other"));
                await unitOfWork.CompleteAsync();
                return (duplicate, other);
            }
        });

        await Task.Delay(TimeSpan.FromMilliseconds(750));
        second.IsCompleted.ShouldBeFalse("the second insert waits for the first transaction");
        releaseFirst.SetResult();

        var won = await first.WaitAsync(TimeSpan.FromSeconds(30));
        var (lost, other) = await second.WaitAsync(TimeSpan.FromSeconds(30));

        won.IsNew.ShouldBeTrue();
        lost.IsNew.ShouldBeFalse("the concurrent duplicate is answered as not new");
        lost.InboundMessageId.ShouldBe(won.InboundMessageId);
        other.IsNew.ShouldBeTrue("the loser's transaction stayed usable and committed its next message");

        await CommittedAsync(Committing, async () =>
        {
            var messages = GetRequiredService<IRepository<InboundMessage, Guid>>();
            (await messages.CountAsync(m => m.SourceId == source && m.ExternalId == externalId)).ShouldBe(1);
            (await messages.CountAsync(m => m.SourceId == source && m.ExternalId == externalId + "-other")).ShouldBe(1);
            (await GetRequiredService<IRepository<InboundDocument, Guid>>().CountAsync(d => d.InboundMessageId == won.InboundMessageId))
                .ShouldBe(1, "the loser wrote no documents for the winner's message");
        });
    }

    /* ---------- M1: an unexpected submit failure ---------- */

    /// <summary>Orders as it is, except that one call fails with something that is not a refusal (a dropped connection, a bug).</summary>
    private sealed class FailingOrderIntakeService(IOrderIntakeService inner, int failOnCall) : IOrderIntakeService
    {
        private int _calls;

        public Task<OrderIntakeSubmissionResultDto> SubmitAsync(OrderIntakeSubmissionDto input, CancellationToken cancellationToken = default)
            => Interlocked.Increment(ref _calls) == failOnCall
                ? throw new InvalidOperationException("The connection was lost.")
                : inner.SubmitAsync(input, cancellationToken);

        public Task<OrderIntakeDto?> GetReviewedPayloadAsync(Guid intakeId, CancellationToken cancellationToken = default)
            => inner.GetReviewedPayloadAsync(intakeId, cancellationToken);

        public Task<IntakeLearningContextDto> GetLearningContextAsync(CancellationToken cancellationToken = default)
            => inner.GetLearningContextAsync(cancellationToken);
    }

    private static InboundDocumentRecording ApiJson(string externalId, string reference) => new()
    {
        Kind = InboundDocumentKind.ApiJson,
        FileName = "order.json",
        ContentType = "application/json",
        Format = DocumentFormat.Json,
        Content = JsonSerializer.SerializeToUtf8Bytes(new IntakeOrderRequestDto
        {
            ExternalId = externalId,
            Order = new OrderIntakeDto
            {
                CustomerReference = reference,
                Customer = new OrderIntakeCustomerDto { Name = "Onbekend " + reference },
                Lines = [new OrderIntakeLineDto { TransportUnitName = "Europallet", Stops = [new OrderIntakeStopDto { Role = IntakeStopRole.Pickup }, new OrderIntakeStopDto { Role = IntakeStopRole.Delivery }] }]
            }
        }, IntakeJson.Options)
    };

    [Fact]
    public async Task An_unexpected_submit_failure_fails_the_job_with_nothing_half_written_and_the_retry_processes_everything()
    {
        using var features = IntakeOn();
        var externalId = Unique("m1");
        var recorded = await CommittedAsync(Committing, () => GetRequiredService<IInboundMessageRecorder>().RecordAsync(new InboundMessageRecording
        {
            Channel = InboundChannel.Api,
            SourceId = Guid.NewGuid(),
            ExternalId = externalId,
            ReceivedAt = DateTime.UtcNow,
            Documents = { ApiJson(externalId, externalId + "-1"), ApiJson(externalId, externalId + "-2") }
        }));

        async Task<(List<InboundDocument> Documents, int Intakes)> StateAsync() => await CommittedAsync(Committing, async () =>
        {
            var documents = await GetRequiredService<IRepository<InboundDocument, Guid>>()
                .GetListAsync(d => d.InboundMessageId == recorded.InboundMessageId, includeDetails: true);
            var ids = documents.Select(d => d.Id).ToList();
            var intakes = await GetRequiredService<IRepository<OrderIntake, Guid>>().CountAsync(i => ids.Contains(i.SourceDocumentId));
            return (documents.OrderBy(d => d.CreationTime).ThenBy(d => d.Id).ToList(), intakes);
        });

        var failing = ActivatorUtilities.CreateInstance<InboundMessageProcessor>(
            GetRequiredService<IServiceProvider>(), new FailingOrderIntakeService(GetRequiredService<IOrderIntakeService>(), failOnCall: 2));
        using (CurrentTenant.Change(Committing))
        {
            var thrown = await Should.ThrowAsync<IntakeSubmitException>(() => failing.ProcessAsync(recorded.InboundMessageId));
            thrown.InnerException.ShouldBeOfType<InvalidOperationException>();
            thrown.IntakeIndex.ShouldBe(0);
        }

        var (documents, intakes) = await StateAsync();
        intakes.ShouldBe(0, "the first document's intake was rolled back with the second's failure: nothing half-written");
        documents.Count.ShouldBe(2);
        documents.ShouldAllBe(d => d.Status == InboundDocumentStatus.Pending, "still Pending, so Hangfire's retry processes them");
        documents.SelectMany(d => d.Steps).ShouldNotContain(s => s.Step == IntakeStep.Submitted, "the rolled-back steps are gone");
        var failedSteps = documents.SelectMany(d => d.Steps).Where(s => s.MessageKey == IntakeStepKeys.SubmitFailed).ToList();
        var failedStep = failedSteps.ShouldHaveSingleItem("recorded once, in a fresh transaction");
        (failedStep.Step, failedStep.Outcome).ShouldBe((IntakeStep.Failed, StepOutcome.Error));

        using (CurrentTenant.Change(Committing))
        {
            await GetRequiredService<IInboundMessageProcessor>().ProcessAsync(recorded.InboundMessageId);
        }

        (documents, intakes) = await StateAsync();
        intakes.ShouldBe(2, "the retry submitted both documents");
        documents.ShouldAllBe(d => d.Status == InboundDocumentStatus.NeedsReview);
    }
}
