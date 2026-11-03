using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using OpenTms.Features;
using OpenTms.Integrations.Features;
using OpenTms.Integrations.OrderIntake;
using OpenTms.Integrations.OrderIntake.Ai;
using OpenTms.Integrations.OrderIntake.Profiles;
using OpenTms.Orders.Events;
using OpenTms.Orders.Intake;
using OpenTms.Platform.Features;
using OpenTms.TestDoubles;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Integrations.Intake;

using DocumentFormat = OpenTms.Integrations.OrderIntake.DocumentFormat;
using IntegrationsErrorCodes = OpenTms.Integrations.IntegrationsErrorCodes;

/// <summary>
/// The import pipeline of design 72 end to end on SQLite, with a scripted language model and the real Orders intake
/// service behind it: recording (idempotent on the external id), the AI step's verification ("first time right": equal →
/// Proposed profile and the executor's output; different → one repair round; still different → extraction capped at 0.6;
/// refusal or schema error → NeedsAttention; AI off → NeedsAttention without a call; transient errors retried twice),
/// metering, the learned profile reading the next document of the layout without AI, reprocessing, the intake API's JSON
/// path (idempotent on ExternalId, the active-client checks), the Orders facts handled twice with one effect, and profile
/// activation and correction. SQLite delivers the distributed events in process (no outbox here).
/// Risk class: High — order chain and external integration; Critical for the API client checks (authorization).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class IntakePipelineTests : OpenTmsEntityFrameworkCoreTestBase
{
    private const string Csv = "Ref;Van;Naar;Datum\nR-1;1234AB;5678CD;04-11-2026\n";
    private const string Sender = "orders@acme.nl";

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly ICurrentTenant _currentTenant;

    public IntakePipelineTests()
    {
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    private ScriptedLanguageModelClient Ai => GetRequiredService<ScriptedLanguageModelClient>();

    private IInboundMessageRecorder Recorder => GetRequiredService<IInboundMessageRecorder>();

    private IInboundMessageProcessor Processor => GetRequiredService<IInboundMessageProcessor>();

    private static IDisposable IntakeOn(bool ai = true)
    {
        var a = TestFeatureValueProvider.Override(IntegrationsFeatures.OrderIntake, "true");
        var b = TestFeatureValueProvider.Override(IntegrationsFeatures.OrderIntakeAiMapping, ai ? "true" : "false");
        var c = TestFeatureValueProvider.Override(AppFeatures.Integrations, "true");
        return new Both(a, b, c);
    }

    private sealed class Both(params IDisposable[] all) : IDisposable
    {
        public void Dispose()
        {
            foreach (var d in all.Reverse())
            {
                d.Dispose();
            }
        }
    }

    private Task<InboundMessageRecordResult> RecordCsvAsync(string externalId, string csv = Csv, string from = Sender, Guid? source = null)
        => WithUnitOfWorkAsync(() => Recorder.RecordAsync(new InboundMessageRecording
        {
            Channel = InboundChannel.Gmail,
            SourceId = source ?? Guid.Empty,
            ExternalId = externalId,
            From = from,
            Subject = "Order R-1",
            ReceivedAt = DateTime.UtcNow,
            RawContent = Encoding.UTF8.GetBytes("From: " + from + "\r\n\r\nzie bijlage"),
            Documents =
            {
                new InboundDocumentRecording
                {
                    Kind = InboundDocumentKind.Attachment, FileName = "orders.csv", ContentType = "text/csv",
                    Content = Encoding.UTF8.GetBytes(csv), Format = DocumentFormat.Csv
                }
            }
        }));

    private Task<List<InboundDocument>> DocumentsAsync(Guid messageId) => WithUnitOfWorkAsync(() =>
        GetRequiredService<IRepository<InboundDocument, Guid>>().GetListAsync(d => d.InboundMessageId == messageId, includeDetails: true));

    private Task<List<ImportProfile>> ProfilesAsync() => WithUnitOfWorkAsync(() => GetRequiredService<IRepository<ImportProfile, Guid>>().GetListAsync());

    private Task<List<AiUsageRecord>> UsageAsync() => WithUnitOfWorkAsync(async () =>
    {
        using (_currentTenant.Change(null))
        {
            return await GetRequiredService<IRepository<AiUsageRecord, Guid>>().GetListAsync();
        }
    });

    /* ---------- The AI's answers ---------- */

    private static LearnedField F(string path, string value, string source = "line 2") => new() { Path = path, Value = value, Source = source };

    /// <summary>The model's reading of <see cref="Csv"/>.</summary>
    private static List<LearnedIntake> ModelIntakes() =>
    [
        new LearnedIntake
        {
            Fields =
            [
                F("order.customerReference", "R-1", "A2"),
                F("lines[0].stops[0].address.postalCode", "1234AB", "B2"),
                F("lines[0].stops[0].date", "04-11-2026", "D2"),
                F("lines[0].stops[1].address.postalCode", "5678CD", "C2"),
                F("lines[0].stops[1].date", "2026-11-04", "D2")
            ]
        }
    ];

    private static FieldRule Rule(string target, string column, params FieldTransform[] transforms)
        => new() { Target = target, Source = new FieldSource { Kind = FieldSourceKind.Column, Value = column }, Transforms = transforms.ToList() };

    /// <summary>Rules that read exactly <see cref="ModelIntakes"/>; <paramref name="pickupColumn"/> "Naar" reads the wrong postcode.</summary>
    private static string RulesJson(string pickupColumn = "Van") => ImportProfileRules.ToJson(new ImportProfileRules
    {
        HeaderRow = 1,
        Orders = new OrderSplitRule { Scope = new OrderScope { Kind = OrderScopeKind.RowGroup, GroupBy = ["Ref"] } },
        Fields =
        [
            Rule("order.customerReference", "Ref"),
            Rule("stop[Pickup].address.postalCode", pickupColumn),
            Rule("stop[Delivery].address.postalCode", "Naar"),
            Rule("stop[Pickup].date", "Datum", new FieldTransform { Kind = FieldTransformKind.Date, Format = "dd-MM-yyyy" }),
            Rule("stop[Delivery].date", "Datum", new FieldTransform { Kind = FieldTransformKind.Date, Format = "dd-MM-yyyy" })
        ]
    });

    private static LearnedDocument Order(string rulesJson, decimal confidence = 0.95m) => new()
    {
        Index = 1, IsOrder = true, Reason = "a transport order", Confidence = confidence, Intakes = ModelIntakes(), ProfileJson = rulesJson,
        Signature = ["Ref", "Van", "Naar", "Datum"]
    };

    /* ---------- Verification ---------- */

    [Fact]
    public async Task A_verified_profile_is_stored_proposed_and_the_executors_output_is_submitted()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        Ai.Answer(Order(RulesJson()));
        var recorded = await RecordCsvAsync("gmail-1");

        await Processor.ProcessAsync(recorded.InboundMessageId);

        Ai.Requests.Count.ShouldBe(1, "one call per message");
        var request = Ai.Requests[0];
        request.ModelId.ShouldBe(OrderIntakeConsts.DefaultLearningModel);
        request.CacheSystemText.ShouldBeTrue();
        request.JsonSchemaName.ShouldBe(IntakeLearningPrompt.SchemaName);
        var text = string.Join("\n", request.Messages.SelectMany(m => m.Parts).Select(p => p.Text));
        text.ShouldContain("<document index=\"1\"");
        text.ShouldContain("R-1");

        var document = (await DocumentsAsync(recorded.InboundMessageId)).ShouldHaveSingleItem();
        document.MappingMethod.ShouldBe(MappingMethod.AiVerifiedProfile);
        document.Steps.Select(s => s.Step).ShouldContain(IntakeStep.ProfileVerified);
        document.Steps.Select(s => s.Step).ShouldContain(IntakeStep.Submitted);
        document.Status.ShouldBe(InboundDocumentStatus.NeedsReview, "nothing resolves without master data; the intake waits in Orders' queue");
        document.Mapping!.Intakes.ShouldHaveSingleItem().Provenance["order.customerReference"].Ref.ShouldBe("A2", "the executor's provenance, not the model's");

        var profile = (await ProfilesAsync()).ShouldHaveSingleItem();
        profile.Status.ShouldBe(ImportProfileStatus.Proposed);
        profile.Origin.ShouldBe(ProfileOrigin.Ai);
        profile.SenderKey.ShouldBe(Sender);
        profile.Format.ShouldBe(DocumentFormat.Csv);
        profile.SourceDocumentId.ShouldBe(document.Id);
        document.ImportProfileId.ShouldBe(profile.Id);

        var usage = (await UsageAsync()).Where(u => u.TenantId == _tenantA).ToList().ShouldHaveSingleItem("every call is metered in the host database");
        usage.Agent.ShouldBe(OrderIntakeConsts.LearnAgent);
        usage.InputTokens.ShouldBe(1000);
        usage.InboundDocumentId.ShouldBe(document.Id);
        var aiStep = document.Steps.Single(s => s.Step == IntakeStep.AiCompleted);
        (aiStep.InputTokens, aiStep.OutputTokens).ShouldBe((1000, 200));
    }

    [Fact]
    public async Task The_next_document_of_a_learned_layout_is_read_by_the_profile_without_any_AI_call()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        Ai.Answer(Order(RulesJson()));
        await Processor.ProcessAsync((await RecordCsvAsync("gmail-1")).InboundMessageId);

        var second = await RecordCsvAsync("gmail-2", "Ref;Van;Naar;Datum\nR-2;1111AA;2222BB;05-11-2026\n");
        await Processor.ProcessAsync(second.InboundMessageId);

        Ai.Requests.Count.ShouldBe(1, "a known layout costs nothing (design 72: tokens no longer burned)");
        var document = (await DocumentsAsync(second.InboundMessageId)).ShouldHaveSingleItem();
        document.MappingMethod.ShouldBe(MappingMethod.Profile);
        document.Steps.Select(s => s.Step).ShouldContain(IntakeStep.ProfileMatched);
        document.Mapping!.Intakes.ShouldHaveSingleItem().Payload.ToJsonString().ShouldContain("R-2");
    }

    [Fact]
    public async Task Different_rules_get_one_repair_round_and_are_stored_when_the_repair_is_equal()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        Ai.Answer(Order(RulesJson(pickupColumn: "Naar")));
        Ai.Answer(Order(RulesJson()));
        var recorded = await RecordCsvAsync("gmail-1");

        await Processor.ProcessAsync(recorded.InboundMessageId);

        Ai.Requests.Count.ShouldBe(2);
        var repair = Ai.Requests[1];
        repair.Messages.Count.ShouldBe(3, "the same conversation: the question, the model's answer, the differences");
        repair.Messages[1].Role.ShouldBe(LanguageModelRole.Assistant);
        string.Join("\n", repair.Messages[2].Parts.Select(p => p.Text)).ShouldContain("lines[0].stops[0].address.postalCode");

        var document = (await DocumentsAsync(recorded.InboundMessageId)).ShouldHaveSingleItem();
        document.MappingMethod.ShouldBe(MappingMethod.AiVerifiedProfile);
        (await ProfilesAsync()).ShouldHaveSingleItem().Status.ShouldBe(ImportProfileStatus.Proposed);
        (await UsageAsync()).Count(u => u.TenantId == _tenantA).ShouldBe(2, "the repair call is metered too");
    }

    [Fact]
    public async Task Rules_still_different_after_the_repair_give_no_profile_and_the_models_values_capped_at_0_6()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        Ai.Answer(Order(RulesJson(pickupColumn: "Naar")));
        Ai.Answer(Order(RulesJson(pickupColumn: "Naar")));
        var recorded = await RecordCsvAsync("gmail-1");

        await Processor.ProcessAsync(recorded.InboundMessageId);

        Ai.Requests.Count.ShouldBe(2, "exactly one repair round");
        var document = (await DocumentsAsync(recorded.InboundMessageId)).ShouldHaveSingleItem();
        document.MappingMethod.ShouldBe(MappingMethod.AiExtraction);
        document.Steps.Select(s => s.Step).ShouldContain(IntakeStep.ProfileVerificationFailed);
        document.Mapping!.Intakes.ShouldHaveSingleItem().Confidence.ShouldBeLessThanOrEqualTo(0.6m);
        (await ProfilesAsync()).ShouldBeEmpty("an unverified profile is never stored");
        document.Status.ShouldBe(InboundDocumentStatus.NeedsReview, "the model's values still go to the review");
    }

    [Fact]
    public async Task A_refusal_leaves_the_document_needing_attention()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        Ai.Refuse();
        var recorded = await RecordCsvAsync("gmail-1");

        await Processor.ProcessAsync(recorded.InboundMessageId);

        var document = (await DocumentsAsync(recorded.InboundMessageId)).ShouldHaveSingleItem();
        document.Status.ShouldBe(InboundDocumentStatus.NeedsAttention);
        document.Steps.ShouldContain(s => s.Step == IntakeStep.AiFailed && s.Parameters["reason"] == "refusal");
        (await ProfilesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_answer_outside_the_schema_leaves_the_document_needing_attention()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        Ai.AnswerOutsideTheSchema();
        var recorded = await RecordCsvAsync("gmail-1");

        await Processor.ProcessAsync(recorded.InboundMessageId);

        var document = (await DocumentsAsync(recorded.InboundMessageId)).ShouldHaveSingleItem();
        document.Status.ShouldBe(InboundDocumentStatus.NeedsAttention);
        document.Steps.ShouldContain(s => s.Step == IntakeStep.AiFailed && s.Parameters["reason"] == "schema");
    }

    [Fact]
    public async Task Without_the_AI_feature_nothing_is_sent_and_the_document_needs_attention()
    {
        using var features = IntakeOn(ai: false);
        using var tenant = _currentTenant.Change(_tenantA);
        var recorded = await RecordCsvAsync("gmail-1");

        await Processor.ProcessAsync(recorded.InboundMessageId);

        Ai.Requests.ShouldBeEmpty("AI mapping is off: no document content leaves the tenant");
        var document = (await DocumentsAsync(recorded.InboundMessageId)).ShouldHaveSingleItem();
        document.Status.ShouldBe(InboundDocumentStatus.NeedsAttention);
        document.Steps.Select(s => s.Step).ShouldContain(IntakeStep.ProfileNotFound);
    }

    [Fact]
    public async Task Without_the_intake_feature_nothing_runs()
    {
        using var features = TestFeatureValueProvider.Override(IntegrationsFeatures.OrderIntake, "false");
        using var tenant = _currentTenant.Change(_tenantA);
        var recorded = await RecordCsvAsync("gmail-1");

        await Processor.ProcessAsync(recorded.InboundMessageId);

        Ai.Requests.ShouldBeEmpty();
        (await DocumentsAsync(recorded.InboundMessageId)).ShouldHaveSingleItem().Status.ShouldBe(InboundDocumentStatus.Pending);
    }

    [Fact]
    public async Task A_document_that_is_not_an_order_is_NotAnOrder()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        Ai.Answer(new LearnedDocument { Index = 1, IsOrder = false, Reason = "newsletter", Confidence = 0.9m });
        var recorded = await RecordCsvAsync("gmail-1");

        await Processor.ProcessAsync(recorded.InboundMessageId);

        (await DocumentsAsync(recorded.InboundMessageId)).ShouldHaveSingleItem().Status.ShouldBe(InboundDocumentStatus.NotAnOrder);
        (await ProfilesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Transient_AI_failures_are_retried_twice_and_the_third_leaves_the_document_needing_attention()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var recorded = await RecordCsvAsync("gmail-1");

        Ai.Fail(transient: true);
        (await Should.ThrowAsync<IntakeAiRetryException>(() => Processor.ProcessAsync(recorded.InboundMessageId))).Attempt.ShouldBe(1);
        (await DocumentsAsync(recorded.InboundMessageId)).Single().Status.ShouldBe(InboundDocumentStatus.Pending, "the job retries it");

        Ai.Fail(transient: true);
        (await Should.ThrowAsync<IntakeAiRetryException>(() => Processor.ProcessAsync(recorded.InboundMessageId))).Attempt.ShouldBe(2);

        Ai.Fail(transient: true);
        await Processor.ProcessAsync(recorded.InboundMessageId);

        var document = (await DocumentsAsync(recorded.InboundMessageId)).Single();
        document.Status.ShouldBe(InboundDocumentStatus.NeedsAttention);
        document.Steps.Count(s => s.Step == IntakeStep.AiFailed).ShouldBe(3);
    }

    [Fact]
    public async Task A_non_transient_AI_failure_is_not_retried()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var recorded = await RecordCsvAsync("gmail-1");
        Ai.Fail(transient: false);

        await Processor.ProcessAsync(recorded.InboundMessageId);

        (await DocumentsAsync(recorded.InboundMessageId)).Single().Status.ShouldBe(InboundDocumentStatus.NeedsAttention);
    }

    /* ---------- Idempotency ---------- */

    [Fact]
    public async Task The_same_mail_twice_is_stored_once()
    {
        using var tenant = _currentTenant.Change(_tenantA);
        var source = Guid.NewGuid();

        var first = await RecordCsvAsync("gmail-msg-7", source: source);
        var second = await RecordCsvAsync("gmail-msg-7", source: source);
        var otherMailbox = await RecordCsvAsync("gmail-msg-7", source: Guid.NewGuid());

        first.IsNew.ShouldBeTrue();
        second.IsNew.ShouldBeFalse();
        second.InboundMessageId.ShouldBe(first.InboundMessageId);
        otherMailbox.IsNew.ShouldBeTrue("the key is (channel, source, external id)");
        (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<InboundMessage, Guid>>().CountAsync(m => m.ExternalId == "gmail-msg-7"))).ShouldBe(2);
        (await DocumentsAsync(first.InboundMessageId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Processing_a_message_twice_calls_the_AI_once_and_creates_one_intake()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        Ai.Answer(Order(RulesJson()));
        var recorded = await RecordCsvAsync("gmail-1");

        await Processor.ProcessAsync(recorded.InboundMessageId);
        await Processor.ProcessAsync(recorded.InboundMessageId);

        Ai.Requests.Count.ShouldBe(1, "only Pending documents run");
        (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<OrderIntake, Guid>>().CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task Reprocessing_a_document_reads_it_again_but_Orders_keeps_one_intake()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        Ai.Answer(Order(RulesJson()));
        var recorded = await RecordCsvAsync("gmail-1");
        await Processor.ProcessAsync(recorded.InboundMessageId);
        var document = (await DocumentsAsync(recorded.InboundMessageId)).Single();
        var intakeId = document.Steps.First(s => s.Step == IntakeStep.Submitted).IntakeId;

        await GetRequiredService<IInboundMessageAppService>().ReprocessDocumentAsync(document.Id);
        await Processor.ProcessAsync(recorded.InboundMessageId);

        var again = (await DocumentsAsync(recorded.InboundMessageId)).Single();
        again.Steps.Select(s => s.Step).ShouldContain(IntakeStep.Reprocessed);
        again.MappingMethod.ShouldBe(MappingMethod.Profile, "the profile learned the first time reads it now");
        again.Steps.Where(s => s.Step == IntakeStep.Submitted).Select(s => s.IntakeId).Distinct().ShouldBe([intakeId], "Orders answers the intake it holds");
        (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<OrderIntake, Guid>>().CountAsync())).ShouldBe(1);
        Ai.Requests.Count.ShouldBe(1);
    }

    /* ---------- The intake API ---------- */

    private async Task<ApiClient> ApiClientAsync(bool active = true, string clientId = "otms_AAAAAAAAAAAAAAAAAAAAAAAA")
    {
        var client = new ApiClient(Guid.NewGuid(), _currentTenant.Id, "ACME ERP", clientId, "abcd", DateTime.UtcNow, null, null, autoConfirm: false);
        if (!active)
        {
            client.Deactivate();
        }

        return await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<ApiClient, Guid>>().InsertAsync(client, autoSave: true));
    }

    /// <summary>The principal of an API client's token (a user id too, because the test authorization requires a signed-in caller).</summary>
    private IDisposable AsApiClient(Guid apiClientId, string? tokenClientId = "otms_AAAAAAAAAAAAAAAAAAAAAAAA")
    {
        var claims = new List<Claim>
        {
            new(AbpClaimTypes.UserId, Guid.NewGuid().ToString()),
            new(OrderIntakeConsts.ApiClientIdProperty, apiClientId.ToString())
        };
        if (_currentTenant.Id != null)
        {
            claims.Add(new Claim(AbpClaimTypes.TenantId, _currentTenant.Id.Value.ToString()));
        }
        if (tokenClientId != null)
        {
            claims.Add(new Claim(AbpClaimTypes.ClientId, tokenClientId));
        }

        return GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")));
    }

    private static IntakeOrderRequestDto ApiOrder(string externalId) => new()
    {
        ExternalId = externalId,
        Order = new OrderIntakeDto
        {
            CustomerReference = "ERP-1",
            Lines =
            [
                new OrderIntakeLineDto
                {
                    TransportUnitName = "Europallet",
                    Stops =
                    [
                        new OrderIntakeStopDto { Role = IntakeStopRole.Pickup, Date = "2026-11-04", Address = new OrderIntakeAddressDto { PostalCode = "1234AB", HouseNumber = "1" } },
                        new OrderIntakeStopDto { Role = IntakeStopRole.Delivery, Date = "2026-11-05", Address = new OrderIntakeAddressDto { PostalCode = "5678CD", HouseNumber = "2" } }
                    ]
                }
            ]
        }
    };

    [Fact]
    public async Task The_API_records_the_JSON_order_processes_it_in_the_request_and_answers_the_same_ExternalId_with_the_stored_result()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var client = await ApiClientAsync();
        using var principal = AsApiClient(client.Id);
        var api = GetRequiredService<IIntakeApiAppService>();

        var first = await api.SubmitOrderAsync(ApiOrder("erp-42"));
        var again = await api.SubmitOrderAsync(ApiOrder("erp-42"));

        Ai.Requests.ShouldBeEmpty("the canonical model needs no AI");
        first.Intakes.ShouldHaveSingleItem().Status.ShouldBe(OrderIntakeStatus.NeedsReview);
        first.Intakes[0].Unresolved.ShouldContain(IntakeFieldPaths.Customer);
        again.InboundMessageId.ShouldBe(first.InboundMessageId);
        again.Intakes.Single().IntakeId.ShouldBe(first.Intakes[0].IntakeId);
        (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<OrderIntake, Guid>>().CountAsync())).ShouldBe(1, "a retry after a timeout creates nothing");
        (await api.GetByExternalIdAsync("erp-42"))!.InboundMessageId.ShouldBe(first.InboundMessageId);
        (await api.GetByExternalIdAsync("unknown")).ShouldBeNull();

        var document = (await DocumentsAsync(first.InboundMessageId)).Single();
        document.MappingMethod.ShouldBe(MappingMethod.Canonical);
        document.SenderKey.ShouldBe("api:" + client.ClientId.ToLowerInvariant());
    }

    [Fact]
    public async Task An_inactive_or_unknown_client_and_a_token_of_another_client_are_refused()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var api = GetRequiredService<IIntakeApiAppService>();

        var inactive = await ApiClientAsync(active: false, clientId: "otms_BBBBBBBBBBBBBBBBBBBBBBBB");
        using (AsApiClient(inactive.Id, "otms_BBBBBBBBBBBBBBBBBBBBBBBB"))
        {
            (await Should.ThrowAsync<BusinessException>(() => api.SubmitOrderAsync(ApiOrder("x")))).Code.ShouldBe(IntegrationsErrorCodes.ApiClientInactive);
        }

        using (AsApiClient(Guid.NewGuid()))
        {
            (await Should.ThrowAsync<BusinessException>(() => api.SubmitOrderAsync(ApiOrder("x")))).Code.ShouldBe(IntegrationsErrorCodes.ApiClientInactive);
        }

        var active = await ApiClientAsync(clientId: "otms_CCCCCCCCCCCCCCCCCCCCCCCC");
        using (AsApiClient(active.Id, tokenClientId: "otms_DDDDDDDDDDDDDDDDDDDDDDDD"))
        {
            (await Should.ThrowAsync<BusinessException>(() => api.SubmitOrderAsync(ApiOrder("x")))).Code
                .ShouldBe(IntegrationsErrorCodes.ApiClientInactive, "build note 11: the token's client_id must be the client's own");
        }

        (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<InboundMessage, Guid>>().CountAsync())).ShouldBe(0, "nothing is recorded for a refused call");
    }

    [Fact]
    public async Task A_client_of_tenant_A_reaches_nothing_from_tenant_B()
    {
        using var features = IntakeOn();
        Guid clientOfA;
        using (_currentTenant.Change(_tenantA))
        {
            clientOfA = (await ApiClientAsync()).Id;
        }

        using (_currentTenant.Change(_tenantB))
        using (AsApiClient(clientOfA))
        {
            (await Should.ThrowAsync<BusinessException>(() => GetRequiredService<IIntakeApiAppService>().SubmitOrderAsync(ApiOrder("x")))).Code
                .ShouldBe(IntegrationsErrorCodes.ApiClientInactive, "the client is not one of tenant B's");
        }

        using (_currentTenant.Change(null))
        using (AsApiClient(clientOfA))
        {
            await Should.ThrowAsync<BusinessException>(() => GetRequiredService<IIntakeApiAppService>().SubmitOrderAsync(ApiOrder("x")));
        }
    }

    [Fact]
    public async Task The_intake_API_is_gated_by_the_intake_feature()
    {
        using var off = TestFeatureValueProvider.Override(IntegrationsFeatures.OrderIntake, "false");
        using var tenant = _currentTenant.Change(_tenantA);
        var client = await ApiClientAsync();
        using var principal = AsApiClient(client.Id);

        await Should.ThrowAsync<AbpAuthorizationException>(() => GetRequiredService<IIntakeApiAppService>().SubmitOrderAsync(ApiOrder("x")));
    }

    [Fact]
    public async Task An_oversized_or_unsupported_document_is_refused_before_anything_is_stored()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var client = await ApiClientAsync();
        using var principal = AsApiClient(client.Id);
        var api = GetRequiredService<IIntakeApiAppService>();

        var big = new Volo.Abp.Content.RemoteStreamContent(new System.IO.MemoryStream(new byte[1]), "a.pdf", "application/pdf", OrderIntakeConsts.MaxDocumentBytes + 1);
        (await Should.ThrowAsync<BusinessException>(() => api.SubmitDocumentAsync("doc-1", big))).Code.ShouldBe(IntegrationsErrorCodes.DocumentTooLarge);

        var exe = new Volo.Abp.Content.RemoteStreamContent(new System.IO.MemoryStream([1, 2]), "setup.exe", "application/x-msdownload");
        (await Should.ThrowAsync<BusinessException>(() => api.SubmitDocumentAsync("doc-2", exe))).Code.ShouldBe(IntegrationsErrorCodes.UnsupportedFormat);

        (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<InboundMessage, Guid>>().CountAsync())).ShouldBe(0);
    }

    /* ---------- Orders' facts and the profile counters ---------- */

    private async Task<(InboundDocument Document, ImportProfile Profile)> MatchedDocumentAsync()
    {
        Ai.Answer(Order(RulesJson()));
        var recorded = await RecordCsvAsync("gmail-" + Guid.NewGuid().ToString("N")[..6]);
        await Processor.ProcessAsync(recorded.InboundMessageId);
        var document = (await DocumentsAsync(recorded.InboundMessageId)).Single();
        return (document, (await ProfilesAsync()).Single(p => p.Id == document.ImportProfileId));
    }

    private Task HandleAsync(Func<OrderIntakeEventHandlers, Task> handle)
        => WithUnitOfWorkAsync(() => handle(GetRequiredService<OrderIntakeEventHandlers>()));

    [Fact]
    public async Task Each_Orders_fact_handled_twice_records_one_step_and_counts_once()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var (document, profile) = await MatchedDocumentAsync();
        var intakeId = document.Steps.First(s => s.Step == IntakeStep.NeedsReview).IntakeId!.Value;
        var completed = new OrderIntakeCompletedEto { IntakeId = intakeId, SourceDocumentId = document.Id, IntakeIndex = 0, TransportOrderId = Guid.NewGuid(), Confirmed = false, Corrected = false };

        await HandleAsync(h => h.HandleEventAsync(completed));
        await HandleAsync(h => h.HandleEventAsync(completed));

        var after = (await DocumentsAsync(document.InboundMessageId)).Single();
        after.Steps.Count(s => s.Step == IntakeStep.Resolved).ShouldBe(1);
        after.Steps.Count(s => s.Step == IntakeStep.OrderCreated).ShouldBe(1);
        after.Status.ShouldBe(InboundDocumentStatus.Completed);
        (await ProfilesAsync()).Single(p => p.Id == profile.Id).ConfirmedCount.ShouldBe(1, "one confirmation, not two");

        var confirmed = new IntakeOrderConfirmedEto { IntakeId = intakeId, SourceDocumentId = document.Id, TransportOrderId = completed.TransportOrderId, EditedAfterIntake = false };
        await HandleAsync(h => h.HandleEventAsync(confirmed));
        await HandleAsync(h => h.HandleEventAsync(confirmed));
        (await DocumentsAsync(document.InboundMessageId)).Single().Steps.Count(s => s.Step == IntakeStep.OrderConfirmed).ShouldBe(1);
        (await ProfilesAsync()).Single(p => p.Id == profile.Id).ConfirmedCount.ShouldBe(2);

        var rejected = new OrderIntakeRejectedEto { IntakeId = Guid.NewGuid(), SourceDocumentId = document.Id, IntakeIndex = 0 };
        await HandleAsync(h => h.HandleEventAsync(rejected));
        await HandleAsync(h => h.HandleEventAsync(rejected));
        (await DocumentsAsync(document.InboundMessageId)).Single().Steps.Count(s => s.Step == IntakeStep.Rejected).ShouldBe(1);
    }

    [Fact]
    public async Task A_profile_becomes_active_at_the_threshold_and_a_correction_sends_it_back_and_queues_a_repair()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var (document, profile) = await MatchedDocumentAsync();
        var jobs = GetRequiredService<RecordingBackgroundJobManager>();
        jobs.Clear();

        for (var i = 0; i < OrderIntakeConsts.DefaultProfileActivationThreshold; i++)
        {
            await HandleAsync(h => h.HandleEventAsync(new IntakeOrderConfirmedEto
            {
                IntakeId = Guid.NewGuid(), SourceDocumentId = document.Id, TransportOrderId = Guid.NewGuid(), EditedAfterIntake = false
            }));
        }

        var active = (await ProfilesAsync()).Single(p => p.Id == profile.Id);
        active.ConfirmedCount.ShouldBe(3);
        active.Status.ShouldBe(ImportProfileStatus.Active, "three confirmations without corrections (tenant setting default 3)");

        await HandleAsync(h => h.HandleEventAsync(new IntakeOrderConfirmedEto
        {
            IntakeId = Guid.NewGuid(), SourceDocumentId = document.Id, TransportOrderId = Guid.NewGuid(), EditedAfterIntake = true
        }));

        var corrected = (await ProfilesAsync()).Single(p => p.Id == profile.Id);
        corrected.CorrectedCount.ShouldBe(1);
        corrected.Status.ShouldBe(ImportProfileStatus.Proposed, "build note 9: an edited Draft is a correction");
        jobs.Of<ImportProfileRepairArgs>().ShouldHaveSingleItem().ImportProfileId.ShouldBe(profile.Id);
    }

    [Fact]
    public async Task Without_the_AI_feature_a_correction_queues_no_repair()
    {
        using var features = IntakeOn();
        using var tenant = _currentTenant.Change(_tenantA);
        var (document, _) = await MatchedDocumentAsync();
        var jobs = GetRequiredService<RecordingBackgroundJobManager>();
        jobs.Clear();

        using (TestFeatureValueProvider.Override(IntegrationsFeatures.OrderIntakeAiMapping, "false"))
        {
            await HandleAsync(h => h.HandleEventAsync(new IntakeOrderConfirmedEto
            {
                IntakeId = Guid.NewGuid(), SourceDocumentId = document.Id, TransportOrderId = Guid.NewGuid(), EditedAfterIntake = true
            }));
        }

        jobs.Of<ImportProfileRepairArgs>().ShouldBeEmpty();
    }

    /* ---------- Tenant isolation of the inbound store ---------- */

    [Fact]
    public async Task Tenant_B_reaches_none_of_tenant_As_messages_documents_originals_or_profiles()
    {
        using var features = IntakeOn();
        Guid messageId;
        Guid documentId;
        Guid profileId;
        using (_currentTenant.Change(_tenantA))
        {
            var (document, profile) = await MatchedDocumentAsync();
            (messageId, documentId, profileId) = (document.InboundMessageId, document.Id, profile.Id);
        }

        using (_currentTenant.Change(_tenantB))
        {
            var messages = GetRequiredService<IInboundMessageAppService>();
            (await messages.GetListAsync(new InboundMessageListRequestDto())).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(() => messages.GetAsync(messageId));
            await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(() => messages.GetRawContentAsync(messageId));
            await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(() => messages.GetDocumentContentAsync(documentId));
            await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(() => messages.ReprocessDocumentAsync(documentId));

            var profiles = GetRequiredService<IImportProfileAppService>();
            (await profiles.GetListAsync(new ImportProfileListRequestDto())).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(() => profiles.GetAsync(profileId));
            await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(() => profiles.ActivateAsync(profileId));
            await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(() => profiles.RetireAsync(profileId));

            /* The pipeline itself, run in B for A's message, finds nothing and touches nothing. */
            await Processor.ProcessAsync(messageId);
        }

        using (_currentTenant.Change(_tenantA))
        {
            (await GetRequiredService<IInboundMessageAppService>().GetAsync(messageId)).Documents.ShouldHaveSingleItem().Id.ShouldBe(documentId);
            (await GetRequiredService<IImportProfileAppService>().GetAsync(profileId)).Status.ShouldBe(ImportProfileStatus.Proposed);
        }
    }
}
