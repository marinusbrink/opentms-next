using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.EntityFrameworkCore.Platform.Tenants;
using OpenTms.MasterData.Relations;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.Auditing;
using OpenTms.Platform.Tenants;
using OpenTms.Platform.TestData;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Xunit;
using Xunit.Abstractions;

namespace OpenTms.PostgreSql.TestData;

/// <summary>
/// "Reset to dataset" against real PostgreSQL with the real module contributors (docs/designs/65-tenant-test-datasets.md):
/// data in every module, a rerun wipes and regenerates to the same counts (idempotent, constitution rule 3), soft-deleted
/// rows go too, the hard delete works on aggregates with owned types and cascading children (transport orders and order
/// templates), no entity history is written, and — the mandatory negative test — resetting one shared-tier tenant leaves
/// the other shared-tier tenant in the same host database untouched (rule 1). One test runs the whole path for real:
/// app service → queued arguments → <see cref="TestDatasetResetJob"/> with the Average profile.
/// The small-profile tests run the job's orchestration (wipe descending, seed, generate ascending, in the tenant's
/// context) with a <see cref="TestDatasetProfile"/> of their own, because the job only knows the two fixed profiles.
/// Risk class: Critical — a host action that deletes a tenant's data.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class TestDatasetResetPostgreSqlTests : OpenTmsTestBase<TestDatasetPostgreSqlTestModule>
{
    private static readonly TestDatasetProfile Tiny = new(
        "Tiny", Seed: 4_242, Relations: 100, MinPerEntity: 3, MaxPerEntity: 6, Orders: 30, OrderDaysBack: 10, OrderDaysAhead: 5);

    /// <summary>Rows that a run may add per run by design (events waiting in the outbox), not business data.</summary>
    private static readonly string[] NotBusinessData = { "Outbox", "Inbox" };

    private static Guid TenantA => TestDatasetTestDatabases.TenantAId;

    private static Guid TenantB => TestDatasetTestDatabases.TenantBId;

    private readonly ITestOutputHelper _output;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public TestDatasetResetPostgreSqlTests(ITestOutputHelper output)
    {
        _output = output;
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
    }

    /* ── Helpers ────────────────────────────────────────────────────────────────────────────── */

    /// <summary>The job's orchestration (TestDatasetResetJob.ExecuteAsync) for a profile of the test's choosing.</summary>
    private async Task RunResetAsync(Guid tenantId, TestDatasetProfile profile, bool generate = true)
    {
        var stopwatch = Stopwatch.StartNew();
        using var scope = ServiceProvider.CreateScope();
        var services = scope.ServiceProvider;
        var contributors = services.GetRequiredService<IEnumerable<ITestDatasetContributor>>().OrderBy(c => c.Order).ToList();
        contributors.Count.ShouldBe(5);
        var addresses = await services.GetRequiredService<TestAddressRegister>().LoadAsync();
        var context = new TestDatasetContext(
            tenantId, profile, new TestAddressBook(addresses, new Random(profile.Seed)), services.GetRequiredService<IClock>().Now);

        using (_currentTenant.Change(tenantId))
        {
            foreach (var contributor in Enumerable.Reverse(contributors))
            {
                await contributor.WipeAsync(context);
            }

            if (generate)
            {
                await services.GetRequiredService<IDataSeeder>().SeedAsync(new DataSeedContext(tenantId));
                foreach (var contributor in contributors)
                {
                    await contributor.GenerateAsync(context);
                }
            }
        }

        _output.WriteLine($"{(generate ? "reset" : "wipe")} of {tenantId} ({profile.Name}) took {stopwatch.Elapsed.TotalSeconds:0.0}s");
    }

    private static Dictionary<string, long> Business(IReadOnlyDictionary<string, long> counts) =>
        counts.Where(c => !NotBusinessData.Any(n => c.Key.Contains(n, StringComparison.Ordinal)))
            .ToDictionary(c => c.Key, c => c.Value);

    private sealed record Snapshot(
        IReadOnlyDictionary<string, long> Counts, List<Guid> Relations, List<Guid> Orders, List<Guid> Trips, List<Guid> Legs);

    private static async Task<Snapshot> SnapshotAsync(Guid tenantId) => new(
        await TenantTables.CountAsync(tenantId),
        await TenantTables.IdsAsync("masterdata.Relations", tenantId),
        await TenantTables.IdsAsync("orders.TransportOrders", tenantId),
        await TenantTables.IdsAsync("planning.TransportTrips", tenantId),
        await TenantTables.IdsAsync("planning.Legs", tenantId));

    private static void ShouldBeIdentical(Snapshot after, Snapshot before)
    {
        var changed = before.Counts.Keys.Union(after.Counts.Keys)
            .Where(k => before.Counts.GetValueOrDefault(k) != after.Counts.GetValueOrDefault(k))
            .Select(k => $"{k}: {before.Counts.GetValueOrDefault(k)} -> {after.Counts.GetValueOrDefault(k)}")
            .ToList();
        changed.ShouldBeEmpty("the other tenant's rows changed");
        after.Relations.ShouldBe(before.Relations);
        after.Orders.ShouldBe(before.Orders);
        after.Trips.ShouldBe(before.Trips);
        after.Legs.ShouldBe(before.Legs);
    }

    private static void ShouldHaveDataInEveryModule(IReadOnlyDictionary<string, long> counts, TestDatasetProfile profile)
    {
        counts["masterdata.Relations"].ShouldBe(profile.Relations);
        foreach (var table in new[]
                 {
                     "masterdata.RelationAddresses", "masterdata.RelationContactPersons", "masterdata.RelationBankAccounts",
                     "masterdata.Vehicles", "masterdata.VehicleTypes", "masterdata.Drivers", "masterdata.TransportUnits",
                     "masterdata.Products", "masterdata.GeographicalRegions", "masterdata.RelationCategories",
                     "financial.TariffsPriceLists", "financial.TariffsPriceListRules", "financial.TariffsDiscountsOrSurcharges",
                     "financial.TariffsAdditionalServices", "financial.InvoicingRelationFinancialSettings",
                     "orders.TransportOrderTemplates", "orders.TransportOrderStops", "orders.TransportOrderLines",
                     "planning.Legs", "planning.TransportTrips", "planning.TripStops",
                     "platform.NumberSequenceCounters"
                 })
        {
            counts.ShouldContainKey(table);
            counts[table].ShouldBeGreaterThan(0, $"{table} has no generated rows");
        }

        counts["orders.TransportOrders"].ShouldBe(profile.Orders);
    }

    /* ── Generation and idempotency ─────────────────────────────────────────────────────────── */

    [Fact]
    public async Task A_reset_generates_in_every_module_and_a_rerun_wipes_and_regenerates_the_same_counts()
    {
        await RunResetAsync(TenantA, Tiny);
        var first = await SnapshotAsync(TenantA);
        ShouldHaveDataInEveryModule(first.Counts, Tiny);

        await RunResetAsync(TenantA, Tiny);
        var second = await SnapshotAsync(TenantA);

        var a = Business(first.Counts);
        var b = Business(second.Counts);
        var differences = a.Keys.Union(b.Keys)
            .Where(k => a.GetValueOrDefault(k) != b.GetValueOrDefault(k))
            .Select(k => $"{k}: {a.GetValueOrDefault(k)} -> {b.GetValueOrDefault(k)}")
            .ToList();
        differences.ShouldBeEmpty("a rerun of the same dataset converges to the same rows");

        /* New records, not the old ones kept: the first run's data was wiped. */
        second.Relations.Intersect(first.Relations).ShouldBeEmpty();
        second.Orders.Intersect(first.Orders).ShouldBeEmpty();
        second.Trips.Intersect(first.Trips).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_tenants_own_queries_see_exactly_its_generated_rows()
    {
        await RunResetAsync(TenantA, Tiny);
        await RunResetAsync(TenantB, Tiny with { Relations = 60, Orders = 12, Seed = 77 });

        using (_currentTenant.Change(TenantA))
        {
            var relations = await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<Relation, Guid>>().GetCountAsync());
            var orders = await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<TransportOrder, Guid>>().GetCountAsync());
            relations.ShouldBe(Tiny.Relations);
            orders.ShouldBe(Tiny.Orders);
        }

        using (_currentTenant.Change(TenantB))
        {
            (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<Relation, Guid>>().GetCountAsync())).ShouldBe(60);
        }
    }

    [Fact]
    public async Task Soft_deleted_rows_are_wiped_too()
    {
        await RunResetAsync(TenantA, Tiny);
        var relationId = (await TenantTables.IdsAsync("masterdata.Relations", TenantA)).First();
        var orderId = (await TenantTables.IdsAsync("orders.TransportOrders", TenantA)).First();
        var templateId = (await TenantTables.IdsAsync("orders.TransportOrderTemplates", TenantA)).First();
        using (_currentTenant.Change(TenantA))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                await GetRequiredService<IRepository<Relation, Guid>>().DeleteAsync(relationId, autoSave: true);
                await GetRequiredService<IRepository<TransportOrder, Guid>>().DeleteAsync(orderId, autoSave: true);
                await GetRequiredService<IRepository<TransportOrderTemplate, Guid>>().DeleteAsync(templateId, autoSave: true);
            });
        }

        (await TenantTables.CountByIdAsync("masterdata.Relations", relationId)).ShouldBe(1, "soft delete keeps the row");
        (await TenantTables.CountAsync(TenantA, "deleted")).Values.Sum().ShouldBeGreaterThanOrEqualTo(3);

        await RunResetAsync(TenantA, Tiny);

        (await TenantTables.CountByIdAsync("masterdata.Relations", relationId)).ShouldBe(0);
        (await TenantTables.CountByIdAsync("orders.TransportOrders", orderId)).ShouldBe(0);
        (await TenantTables.CountByIdAsync("orders.TransportOrderTemplates", templateId)).ShouldBe(0);
        var deleted = (await TenantTables.CountAsync(TenantA, "deleted")).Where(c => c.Value > 0).Select(c => $"{c.Key}: {c.Value}").ToList();
        deleted.ShouldBeEmpty("no soft-deleted row survives a reset");
    }

    [Fact]
    public async Task The_wipe_removes_every_generated_row_including_owned_and_child_rows()
    {
        await RunResetAsync(TenantA, Tiny);
        (await TenantTables.CountAsync(TenantA))["orders.TransportOrders"].ShouldBe(Tiny.Orders);

        await RunResetAsync(TenantA, Tiny, generate: false);

        var after = Business(await TenantTables.CountAsync(TenantA));
        var seeded = TestDatasetTestDatabases.SeededCountsOfA;
        var leftOver = after.Where(c => c.Value > seeded.GetValueOrDefault(c.Key))
            .Select(c => $"{c.Key}: {c.Value} (seeded {seeded.GetValueOrDefault(c.Key)})")
            .ToList();
        leftOver.ShouldBeEmpty("the wipe leaves nothing beyond what the seed wrote");

        foreach (var table in new[]
                 {
                     "masterdata.Relations", "masterdata.RelationAddresses", "masterdata.RelationContactPersons", "masterdata.Vehicles",
                     "masterdata.Drivers", "financial.TariffsPriceLists", "financial.TariffsPriceListRules", "financial.TariffsRateLines",
                     "financial.InvoicingRelationFinancialSettings",
                     "orders.TransportOrders", "orders.TransportOrderStops", "orders.TransportOrderLines", "orders.TransportOrderReferences",
                     "orders.TransportOrderTemplates", "orders.TransportOrderTemplateStops", "orders.TransportOrderTemplateLines",
                     "planning.Legs", "planning.TransportTrips", "planning.TripStops", "planning.TripStopLegs", "planning.ViaDepotRules",
                     "platform.NumberSequenceCounters", "platform.DerivedValueStates"
                 })
        {
            after.GetValueOrDefault(table).ShouldBe(0, table);
        }

        /* Users, roles and settings stay (design Q1). */
        after["public.AbpUsers"].ShouldBe(seeded["public.AbpUsers"]);
        after["public.AbpRoles"].ShouldBe(seeded["public.AbpRoles"]);
        after["masterdata.Countries"].ShouldBe(seeded["masterdata.Countries"]);
    }

    /* ── Tenant isolation (mandatory negative test) ─────────────────────────────────────────── */

    [Fact]
    public async Task Resetting_one_shared_tier_tenant_leaves_the_other_tenant_in_the_same_database_untouched()
    {
        /* Both tenants live in the host database (no connection string): the delete must be scoped by the tenant filter. */
        await WithUnitOfWorkAsync(async () =>
        {
            var tenants = GetRequiredService<ITenantRepository>();
            (await tenants.GetAsync(TenantA, includeDetails: true)).ConnectionStrings.ShouldBeEmpty();
            (await tenants.GetAsync(TenantB, includeDetails: true)).ConnectionStrings.ShouldBeEmpty();
        });

        await RunResetAsync(TenantB, Tiny with { Seed = 99 });
        var before = await SnapshotAsync(TenantB);
        ShouldHaveDataInEveryModule(before.Counts, Tiny);

        await RunResetAsync(TenantA, Tiny);
        ShouldBeIdentical(await SnapshotAsync(TenantB), before);

        await RunResetAsync(TenantA, Tiny, generate: false);
        ShouldBeIdentical(await SnapshotAsync(TenantB), before);

        await RunResetAsync(TenantA, Tiny);
        ShouldBeIdentical(await SnapshotAsync(TenantB), before);
    }

    /* ── Entity history ─────────────────────────────────────────────────────────────────────── */

    [Fact]
    public async Task A_reset_writes_no_entity_history()
    {
        /* The functional logbook would record these types inside an audit scope; the run must not open one. */
        FunctionalLogbookEntitySelector.IsSelected(typeof(Relation)).ShouldBeTrue();
        FunctionalLogbookEntitySelector.IsSelected(typeof(TransportOrder)).ShouldBeTrue();

        var before = await TenantTables.CountAsync(TenantA);

        await RunResetAsync(TenantA, Tiny);

        var after = await TenantTables.CountAsync(TenantA);
        foreach (var table in new[] { "public.AbpEntityChanges", "public.AbpEntityPropertyChanges", "public.AbpAuditLogs", "public.AbpAuditLogActions" })
        {
            after.GetValueOrDefault(table).ShouldBe(before.GetValueOrDefault(table), table);
        }
    }

    /* ── End to end: app service → job, Average profile ─────────────────────────────────────── */

    [Fact]
    public async Task The_real_job_resets_a_shared_tier_tenant_to_the_average_dataset_and_leaves_the_other_tenant_alone()
    {
        var jobs = GetRequiredService<FakeBackgroundJobManager>();
        var stateWriter = GetRequiredService<TenantStateWriter>();
        await RunResetAsync(TenantB, Tiny with { Seed = 5 });
        var bystander = await SnapshotAsync(TenantB);
        await stateWriter.SetDatasetResetStateAsync(TenantA, TestDatasetResetState.Succeeded);
        var history = await TenantTables.CountAsync(TenantA);
        jobs.Clear();

        await GetRequiredService<ITenantAdministrationAppService>().ResetToDatasetAsync(
            TenantA, new TenantResetToDatasetDto { Dataset = TestDatasets.Average, ConfirmationName = "dataset-a" });
        var args = jobs.JobsOf<TestDatasetResetArgs>().ShouldHaveSingleItem();
        (await stateWriter.FindAsync(TenantA))!.GetProperty<string?>(TenantProperties.DatasetResetState).ShouldBe(TestDatasetResetState.Queued);

        var stopwatch = Stopwatch.StartNew();
        await GetRequiredService<TestDatasetResetJob>().ExecuteAsync(args);
        _output.WriteLine($"Average reset through the job took {stopwatch.Elapsed.TotalSeconds:0.0}s");

        var tenant = (await stateWriter.FindAsync(TenantA))!;
        tenant.GetProperty<string?>(TenantProperties.DatasetResetError).ShouldBeNull();
        tenant.GetProperty<string?>(TenantProperties.DatasetResetState).ShouldBe(TestDatasetResetState.Succeeded);
        tenant.GetProperty<Guid?>(TenantProperties.DatasetResetRequestId).ShouldBe(args.RequestId);

        var counts = await TenantTables.CountAsync(TenantA);
        ShouldHaveDataInEveryModule(counts, TestDatasetProfile.Average);
        counts["masterdata.Vehicles"].ShouldBeGreaterThanOrEqualTo(TestDatasetProfile.Average.MinPerEntity);
        foreach (var table in new[] { "public.AbpEntityChanges", "public.AbpAuditLogs" })
        {
            counts.GetValueOrDefault(table).ShouldBe(history.GetValueOrDefault(table), table);
        }

        ShouldBeIdentical(await SnapshotAsync(TenantB), bystander);

        /* A redelivery of the finished request changes nothing. */
        var relations = await TenantTables.IdsAsync("masterdata.Relations", TenantA);
        await GetRequiredService<TestDatasetResetJob>().ExecuteAsync(args);
        (await TenantTables.IdsAsync("masterdata.Relations", TenantA)).ShouldBe(relations);
    }

    [Fact]
    public async Task A_rolled_back_request_leaves_the_tenant_state_and_queues_nothing()
    {
        var jobs = GetRequiredService<FakeBackgroundJobManager>();
        var stateWriter = GetRequiredService<TenantStateWriter>();
        await stateWriter.SetDatasetResetStateAsync(TenantA, TestDatasetResetState.Succeeded);
        var before = (await stateWriter.FindAsync(TenantA))!;
        jobs.Clear();

        using (_unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            await GetRequiredService<ITenantAdministrationAppService>().ResetToDatasetAsync(
                TenantA, new TenantResetToDatasetDto { Dataset = TestDatasets.Large, ConfirmationName = "dataset-a" });
            /* disposed without CompleteAsync */
        }

        jobs.Jobs.ShouldBeEmpty();
        var after = (await stateWriter.FindAsync(TenantA))!;
        after.GetProperty<string?>(TenantProperties.DatasetResetState).ShouldBe(TestDatasetResetState.Succeeded);
        after.GetProperty<string?>(TenantProperties.DatasetName).ShouldBe(before.GetProperty<string?>(TenantProperties.DatasetName));
    }
    [Fact]
    public async Task Two_simultaneous_requests_for_the_same_tenant_queue_one_reset()
    {
        /* The guard "no reset while one is Queued" is check-then-set; the tenant's concurrency stamp must stop the second. */
        var jobs = GetRequiredService<FakeBackgroundJobManager>();
        var stateWriter = GetRequiredService<TenantStateWriter>();
        await stateWriter.SetDatasetResetStateAsync(TenantA, TestDatasetResetState.Succeeded);
        jobs.Clear();

        async Task<Exception?> RequestAsync(string dataset)
        {
            await Task.Yield();
            try
            {
                using var scope = ServiceProvider.CreateScope();
                var unitOfWorkManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
                using var unitOfWork = unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true);
                await scope.ServiceProvider.GetRequiredService<ITenantAdministrationAppService>().ResetToDatasetAsync(
                    TenantA, new TenantResetToDatasetDto { Dataset = dataset, ConfirmationName = "dataset-a" });
                await unitOfWork.CompleteAsync();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var results = await Task.WhenAll(RequestAsync(TestDatasets.Average), RequestAsync(TestDatasets.Large));
        _output.WriteLine(string.Join(" | ", results.Select(r => r?.GetType().Name ?? "ok")));

        jobs.JobsOf<TestDatasetResetArgs>().Count.ShouldBe(1, "two resets of one tenant must never run side by side");
        results.Count(r => r == null).ShouldBe(1);
        var queued = jobs.JobsOf<TestDatasetResetArgs>().Single().Dataset;
        (await stateWriter.FindAsync(TenantA))!.GetProperty<string?>(TenantProperties.DatasetName).ShouldBe(queued);

        await stateWriter.SetDatasetResetStateAsync(TenantA, TestDatasetResetState.Succeeded);
    }
}
