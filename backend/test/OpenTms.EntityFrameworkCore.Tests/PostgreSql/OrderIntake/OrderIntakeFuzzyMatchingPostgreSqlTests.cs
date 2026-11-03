using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTms.MasterData.Published;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using OpenTms.PostgreSql.ReferenceData;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.Intake;

/// <summary>
/// The fuzzy matching and the Master Data writer of design 72 (build notes 28–32) against real PostgreSQL:
/// <list type="bullet">
/// <item><see cref="TrigramSimilarity"/> gives what pg_trgm's <c>similarity()</c> gives, for raw and normalised names, digits,
/// punctuation and diacritics (the build note's "pg_trgm-identical");</item>
/// <item>tenant isolation (constitution rule 1, Critical): the similar-name reads and the writer of a shared-tier tenant
/// never see, return or reuse another shared-tier tenant's rows in the same host database (the data filter), nor those of
/// a tenant with its own database;</item>
/// <item>the documented parallel duplicate (build note 32): two transactions creating the same unknown transport unit or
/// relation at once — the unique index refuses the second, one row stays, and what the retry resolves against is the
/// first's row.</item>
/// </list>
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class OrderIntakeFuzzyMatchingPostgreSqlTests : ReferenceDataPostgreSqlTestBase
{
    private static string Unique(string prefix) => prefix + " " + Guid.NewGuid().ToString("N")[..8];

    private IMasterDataMatchQuery Match => GetRequiredService<IMasterDataMatchQuery>();

    private IMasterDataIntakeWriter Writer => GetRequiredService<IMasterDataIntakeWriter>();

    /* ---------- pg_trgm ---------- */

    public static IEnumerable<object[]> Pairs()
    {
        var pairs = new (string, string)[]
        {
            ("Jansen Transprot BV", "Jansen Transport B.V."),
            ("jansen transprot", "jansen transport"),
            ("Heinekn Nederland", "Heineken Nederland B.V."),
            ("heinekn nederland", "heineken nederland"),
            ("Bakker Logistiek Holding BV", "Bakker Logistiek Holding"),
            ("Jansen Transport", "Janssen Transport"),
            ("Jansen Transport Venlo", "Jansen Transport Venray"),
            ("Klant Foods Inkoop", "Klant Foods B.V."),
            ("DC Venlo", "dc  venlo!"),
            ("Garage 2000", "Garage 2001"),
            ("A-1 Logistics", "A1 Logistics"),
            ("Müller Logistik GmbH", "Mueller Logistik GmbH"),
            ("Café De Gouden Leeuw", "Cafe de gouden leeuw"),
            ("Europallet", "Europalet"),
            ("Gitterbox", "Europallet"),
            ("x", "x"),
            ("ab", "abc"),
            ("!!!", "abc"),
            ("Nieuwe Klant B.V.", "Klant Foods B.V.")
        };
        return pairs.Select(p => new object[] { p.Item1, p.Item2 });
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task The_trigram_similarity_is_pg_trgms_similarity(string a, string b)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.HostConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT similarity(@a, @b)", connection);
        command.Parameters.AddWithValue("a", a);
        command.Parameters.AddWithValue("b", b);
        var postgres = Convert.ToDouble(await command.ExecuteScalarAsync());

        TrigramSimilarity.Of(a, b).ShouldBe(postgres, 1e-6, $"similarity('{a}', '{b}')");
        TrigramSimilarity.Of(NameNormalizer.Normalize(a), NameNormalizer.Normalize(b)).ShouldBeInRange(0d, 1d);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task The_normalised_trigram_similarity_is_pg_trgms_on_the_normalised_names(string a, string b)
    {
        var left = NameNormalizer.Normalize(a);
        var right = NameNormalizer.Normalize(b);
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.HostConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT similarity(@a, @b)", connection);
        command.Parameters.AddWithValue("a", left);
        command.Parameters.AddWithValue("b", right);
        var postgres = Convert.ToDouble(await command.ExecuteScalarAsync());

        TrigramSimilarity.Of(left, right).ShouldBe(postgres, 1e-6, $"similarity('{left}', '{right}')");
    }

    /* ---------- Tenant isolation ---------- */

    private static Guid OtherSharedTenant => PostgreSqlTestDatabases.TransportSharedTenantId;

    [Fact]
    public async Task A_shared_tier_tenants_similar_name_reads_and_writer_never_reach_another_tenants_rows_in_the_same_database()
    {
        await InRolledBackTransactionAsync(SharedTenant, async () =>
        {
            var name = Unique("Jansen Transport");
            var unitName = Unique("Gitterbox");
            var relation = await Writer.CreateRelationAsync(new IntakeRelationCreateDto { Name = name, SourceReference = "iso " + name });
            var unit = await Writer.CreateReferenceAsync(new IntakeReferenceCreateDto { Kind = MasterDataReferenceKind.TransportUnit, Name = unitName });
            relation.Created.ShouldBeTrue();
            unit.Created.ShouldBeTrue();
            await FlushAsync();
            (await Match.FindSimilarRelationsAsync(new SimilarRelationQuery { Name = name.Replace("Transport", "Transprot") })).Select(r => r.Id).ShouldContain(relation.Id!.Value, "found in its own tenant");

            foreach (var other in new Guid?[] { OtherSharedTenant, null })
            {
                using (CurrentTenant.Change(other))
                {
                    var who = other == null ? "host" : "other shared tenant";
                    (await Match.FindSimilarRelationsAsync(new SimilarRelationQuery { Name = name.Replace("Transport", "Transprot") })).ShouldNotContain(r => r.Id == relation.Id, who);
                    (await Match.FindSimilarRelationsAsync(new SimilarRelationQuery { Name = name })).ShouldNotContain(r => r.Id == relation.Id, who);
                    (await Match.FindSimilarReferencesAsync(new ReferenceNameMatchQuery { Kind = MasterDataReferenceKind.TransportUnit, Names = [unitName] }))
                        .ShouldNotContain(r => r.Id == unit.Id, who);

                    if (other != null)
                    {
                        /* The writer of the other tenant: its own rows, never the first tenant's. */
                        var again = await Writer.CreateRelationAsync(new IntakeRelationCreateDto { Name = name, SourceReference = "iso " + name });
                        again.Created.ShouldBeTrue(who + ": the same reference in another tenant is another row");
                        again.Id.ShouldNotBe(relation.Id);
                        var ownUnit = await Writer.CreateReferenceAsync(new IntakeReferenceCreateDto { Kind = MasterDataReferenceKind.TransportUnit, Name = unitName });
                        (ownUnit.Created, ownUnit.Id == unit.Id).ShouldBe((true, false), who);
                        (await Writer.AddRelationAddressAsync(new IntakeRelationAddressAddDto
                        {
                            RelationId = relation.Id!.Value,
                            Address = new IntakeAddressInputDto { Street = "Kade", HouseNumber = "1", PostalCode = "3011 AA", City = "Rotterdam", CountryCode = "NL" }
                        })).RefusedCode.ShouldBe(IMasterDataIntakeWriter.RefusedNotFound, who);
                        await FlushAsync();
                        (await GetRequiredService<IRepository<Relation, Guid>>().GetAsync(again.Id!.Value)).TenantId.ShouldBe(other);
                    }
                }
            }

            (await GetRequiredService<IRepository<Relation, Guid>>().GetAsync(relation.Id!.Value, includeDetails: true)).Addresses.ShouldBeEmpty("the other tenant changed nothing");
            (await GetRequiredService<IRepository<TransportUnit, Guid>>().CountAsync(u => u.Name == unitName)).ShouldBe(1);
        });
    }

    [Fact]
    public async Task A_tenant_with_its_own_database_is_unreachable_from_the_shared_tier_by_similar_name()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var name = Unique("Heineken Nederland");
            var relation = await Writer.CreateRelationAsync(new IntakeRelationCreateDto { Name = name, SourceReference = "iso " + name });
            await FlushAsync();

            using (CurrentTenant.Change(SharedTenant))
            {
                (await Match.FindSimilarRelationsAsync(new SimilarRelationQuery { Name = name.Replace("Heineken", "Heinekn") })).ShouldNotContain(r => r.Id == relation.Id);
                (await Match.FindSimilarRelationAddressesAsync(new SimilarAddressQuery { Name = name, City = "Amsterdam" })).ShouldNotContain(a => a.RelationId == relation.Id);
            }
        });
    }

    /* ---------- The parallel duplicate (build note 32) ---------- */

    private async Task<(Exception? First, Exception? Second)> ConcurrentlyAsync(Guid tenant, Func<Task> write)
    {
        var firstWritten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Task.Run(async () =>
        {
            using (CurrentTenant.Change(tenant))
            {
                using var unitOfWork = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
                await write();
                firstWritten.SetResult();
                await releaseFirst.Task;
                await unitOfWork.CompleteAsync();
            }
        });
        await firstWritten.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var second = Task.Run(async () =>
        {
            using (CurrentTenant.Change(tenant))
            {
                using var unitOfWork = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
                await write(); /* waits on the unique index for the first transaction */
                await unitOfWork.CompleteAsync();
            }
        });

        await Task.Delay(TimeSpan.FromMilliseconds(750));
        releaseFirst.SetResult();

        Exception? firstError = null, secondError = null;
        try { await first.WaitAsync(TimeSpan.FromSeconds(30)); } catch (Exception ex) { firstError = ex; }
        try { await second.WaitAsync(TimeSpan.FromSeconds(30)); } catch (Exception ex) { secondError = ex; }
        return (firstError, secondError);
    }

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

    [Fact]
    public async Task Two_parallel_intakes_creating_the_same_transport_unit_leave_one_row_and_the_second_fails_for_its_retry()
    {
        var tenant = Guid.NewGuid(); /* a fresh shared-tier tenant: committed rows stay out of every other test's way */
        var name = Unique("Gitterbox");

        var (first, second) = await ConcurrentlyAsync(tenant, () => Writer.CreateReferenceAsync(new IntakeReferenceCreateDto { Kind = MasterDataReferenceKind.TransportUnit, Name = name }));

        first.ShouldBeNull();
        second.ShouldNotBeNull("the unique (TenantId, Name) index refuses the second (build note 32): its job fails and Hangfire retries");
        (second is DbUpdateException || second.InnerException is DbUpdateException || second is Volo.Abp.Data.AbpDbConcurrencyException).ShouldBeTrue(second.ToString());
        var rows = await CommittedAsync(tenant, () => GetRequiredService<IRepository<TransportUnit, Guid>>().GetListAsync(u => u.Name == name));
        var row = rows.ShouldHaveSingleItem();

        /* The retry finds the first's row: the writer returns it, the resolver's exact name read finds it. */
        var retry = await CommittedAsync(tenant, () => Writer.CreateReferenceAsync(new IntakeReferenceCreateDto { Kind = MasterDataReferenceKind.TransportUnit, Name = name }));
        (retry.Created, retry.Id).ShouldBe((false, (Guid?)row.Id));
    }

    [Fact]
    public async Task Two_parallel_intakes_creating_the_same_unknown_customer_leave_one_relation_and_the_retry_resolves_against_it()
    {
        var tenant = Guid.NewGuid();
        var name = Unique("Nieuwe Klant");
        var calls = 0;

        var (first, second) = await ConcurrentlyAsync(tenant, () =>
        {
            /* Two intakes: each its own "{intakeId} {field path}" reference, the same name, so the same derived code. */
            var reference = "intake-" + System.Threading.Interlocked.Increment(ref calls) + " order.customer";
            return Writer.CreateRelationAsync(new IntakeRelationCreateDto { Name = name, SourceReference = reference });
        });

        first.ShouldBeNull();
        second.ShouldNotBeNull("the unique (TenantId, Code) index refuses the second (build note 32)");
        var relations = await CommittedAsync(tenant, () => GetRequiredService<IRepository<Relation, Guid>>().GetListAsync(r => r.Name == name));
        var relation = relations.ShouldHaveSingleItem();

        /* The retried intake resolves the customer by its name before anything is created. */
        var found = await CommittedAsync(tenant, () => Match.FindRelationsAsync(new RelationMatchQuery { Name = name }));
        found.ShouldContain(m => m.Id == relation.Id && m.MatchKind == OpenTms.MasterData.Published.RelationMatchKind.Name);
    }
}
