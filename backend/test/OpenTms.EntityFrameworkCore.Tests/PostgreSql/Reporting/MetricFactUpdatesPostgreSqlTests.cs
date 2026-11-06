using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using OpenTms.Features;
using OpenTms.Orders.Events;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.Events;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.DerivedValues;
using OpenTms.Reporting.Facts;
using OpenTms.Reporting.Features;
using OpenTms.Reporting.Metrics;
using OpenTms.Reporting.Published;
using OpenTms.TestDoubles;
using OpenTms.TransportRun;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.Reporting;

/// <summary>
/// Design 75 "Fact updates" (risk class High) and the writing half of "Tenant isolation" (Critical) on PostgreSQL through the real outbox: a change, a status
/// change and a delete each reach the fact; a trip whose route never worked still gets one; a duplicate event changes nothing; a newer save discards an older run;
/// a lost update and a lost delete are repaired by the daily runner; and a job and a handler write only under the raising tenant, in its own database and, on the
/// shared tier, under its own tenant id. The rows commit, in the committing tests' own tenants.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class MetricFactUpdatesPostgreSqlTests : ReportingPostgreSqlTestBase
{
    private static Guid OwnDatabase => PostgreSqlTestDatabases.TransportTenantAId;

    private static Guid SharedTier => PostgreSqlTestDatabases.TransportSharedTenantId;

    private TransportRunFixture Run => GetRequiredService<TransportRunFixture>();

    private ITransportOrderAppService Orders => GetRequiredService<ITransportOrderAppService>();

    private ITransportTripAppService Trips => GetRequiredService<ITransportTripAppService>();

    private TestRoutingWorld Routing => GetRequiredService<TestRoutingWorld>();

    private async Task<TransportRunWorld> WorldAsync(Guid tenant)
        => await CommittedAsync(tenant, () => Run.BuildAsync(new TransportRunWorldOptions { UniqueNames = true }));

    private async Task<TransportOrderDto> ConfirmedOrderAsync(Guid tenant, TransportRunWorld world)
        => await CommittedAsync(tenant, () => Run.CreateOrderAsync(world, customerReference: "RPT-" + Guid.NewGuid().ToString("N")[..8]));

    private async Task<Guid> TripAsync(Guid tenant, TransportRunWorld world)
        => (await CommittedAsync(tenant, () => Run.PlanAsync(world.TripTypeId, []))).Id;

    private static long SqlCount(string database, string sql)
    {
        using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    /* ---------- Orders ---------- */

    [Fact]
    public async Task A_new_order_a_status_change_a_recalculation_and_a_delete_each_reach_the_fact_through_the_outbox()
    {
        using var features = MetricFeaturesOn();
        var world = await WorldAsync(OwnDatabase);
        var order = await ConfirmedOrderAsync(OwnDatabase, world);
        await DriveAsync(OwnDatabase);

        var fact = (await OrderFactAsync(OwnDatabase, order.Id)).ShouldNotBeNull("the order's recalculation reached Reporting");
        fact.Status.ShouldBe(TransportOrderStatus.Open);
        fact.CustomerRelationId.ShouldBe(world.CustomerId);
        fact.CurrencyCode.ShouldBe("EUR");
        fact.TenantId.ShouldBe(OwnDatabase);
        var firstRevenue = fact.RevenueAmount;

        /* A stored value changes: Financial's new total flows through the order's summary to the fact. */
        await CommittedAsync(OwnDatabase, async () =>
        {
            var repository = GetRequiredService<IRepository<TransportOrder, Guid>>();
            var row = await repository.GetAsync(order.Id, includeDetails: false);
            row.WriteSummary(new OrderSummary(4_321m, 21m, 4_300m, 7m, ExecutionProgress.Unplanned, 1, 2, null, null, null, null, null, null, null), DateTime.Now);
            await repository.UpdateAsync(row, autoSave: true);
            await GetRequiredService<IDistributedEventBus>().PublishAsync(new TransportOrderRecalculatedEto { OrderId = order.Id, Version = 99 });
        });
        await DriveAsync(OwnDatabase);
        fact = (await OrderFactAsync(OwnDatabase, order.Id)).ShouldNotBeNull();
        fact.RevenueAmount.ShouldBe(4_321m);
        fact.CostOfRevenueAmount.ShouldBe(21m);
        fact.MarginAmount.ShouldBe(4_300m);
        fact.Co2Kg.ShouldBe(7m);
        fact.RevenueAmount.ShouldNotBe(firstRevenue);

        /* A status change. */
        await CommittedAsync(OwnDatabase, () => Orders.CancelAsync(order.Id));
        await DriveAsync(OwnDatabase);
        (await OrderFactAsync(OwnDatabase, order.Id)).ShouldNotBeNull().Status.ShouldBe(TransportOrderStatus.Cancelled, "a cancelled order keeps its fact, with its status");

        /* The other delete path: the order is gone, so the fact goes. */
        var second = await ConfirmedOrderAsync(OwnDatabase, world);
        await DriveAsync(OwnDatabase);
        (await OrderFactAsync(OwnDatabase, second.Id)).ShouldNotBeNull();
        await CommittedAsync(OwnDatabase, () => Orders.DeleteAsync(second.Id));
        await DriveAsync(OwnDatabase);
        (await OrderFactAsync(OwnDatabase, second.Id)).ShouldBeNull("the calculator finds no order and deletes the fact");
    }

    /* ---------- Trips ---------- */

    [Fact]
    public async Task A_new_trip_a_change_a_status_change_and_a_delete_each_reach_the_fact_through_the_outbox()
    {
        using var features = MetricFeaturesOn();
        var world = await WorldAsync(OwnDatabase);
        var tripId = await TripAsync(OwnDatabase, world);
        await DriveAsync(OwnDatabase);

        var fact = (await TripFactAsync(OwnDatabase, tripId)).ShouldNotBeNull("the created event reaches Reporting");
        fact.Status.ShouldBe(TransportTripStatus.Planned);
        fact.TransportTripTypeId.ShouldBe(world.TripTypeId);

        await CommittedAsync(OwnDatabase, async () =>
        {
            var trip = await Trips.GetAsync(tripId);
            await Trips.StartAsync(tripId, new TripTimestampDto { At = trip.PlannedDeparture });
        });
        await DriveAsync(OwnDatabase);
        (await TripFactAsync(OwnDatabase, tripId)).ShouldNotBeNull().Status.ShouldBe(TransportTripStatus.InProgress);

        var second = await TripAsync(OwnDatabase, world);
        await DriveAsync(OwnDatabase);
        (await TripFactAsync(OwnDatabase, second)).ShouldNotBeNull();
        await CommittedAsync(OwnDatabase, () => Trips.DeleteAsync(second));
        await DriveAsync(OwnDatabase);
        (await TripFactAsync(OwnDatabase, second)).ShouldBeNull("TransportTripDeletedEto removes the fact");
    }

    [Fact]
    public async Task A_trip_without_a_working_route_still_gets_a_fact()
    {
        using var features = MetricFeaturesOn();
        var world = await WorldAsync(OwnDatabase);
        Routing.RoutingThrows = true;
        try
        {
            var tripId = await TripAsync(OwnDatabase, world);
            /* Only the fact's job runs: the recalculated event of a trip never fires while routing fails. */
            await DriveFactsAsync(OwnDatabase);
            var fact = (await TripFactAsync(OwnDatabase, tripId)).ShouldNotBeNull("TransportTripCreatedEto alone is enough");
            fact.PlannedDistanceM.ShouldBeNull();
        }
        finally
        {
            Routing.RoutingThrows = false;
        }
    }

    /* ---------- Idempotence and version guard ---------- */

    [Fact]
    public async Task A_duplicate_event_changes_nothing_at_all()
    {
        using var features = MetricFeaturesOn();
        var world = await WorldAsync(OwnDatabase);
        var order = await ConfirmedOrderAsync(OwnDatabase, world);
        await DriveAsync(OwnDatabase);
        var before = (await OrderFactAsync(OwnDatabase, order.Id)).ShouldNotBeNull();
        var countBefore = await CommittedAsync(OwnDatabase, () => OrderFacts.CountAsync(), transactional: false);

        var handlers = GetRequiredService<ReportingFactEventHandlers>();
        using (AsBackgroundProcess())
        {
            for (var i = 0; i < 2; i++)
            {
                await CommittedAsync(OwnDatabase, () => handlers.HandleEventAsync(new TransportOrderRecalculatedEto { OrderId = order.Id, Version = 1 }));
            }
        }

        (await DriveFactsAsync(OwnDatabase)).ShouldBeGreaterThanOrEqualTo(1);

        var after = (await OrderFactAsync(OwnDatabase, order.Id)).ShouldNotBeNull();
        after.FactUpdatedAt.ShouldBe(before.FactUpdatedAt, "a fact only moves when a value changed");
        after.RevenueAmount.ShouldBe(before.RevenueAmount);
        (await CommittedAsync(OwnDatabase, () => OrderFacts.CountAsync(), transactional: false)).ShouldBe(countBefore, "still one fact per order");
    }

    [Fact]
    public async Task A_newer_save_discards_an_older_run()
    {
        using var features = MetricFeaturesOn();
        var world = await WorldAsync(OwnDatabase);
        var order = await ConfirmedOrderAsync(OwnDatabase, world);
        await DriveAsync(OwnDatabase);
        var before = (await OrderFactAsync(OwnDatabase, order.Id)).ShouldNotBeNull();

        var scheduler = GetRequiredService<IDerivedValueScheduler>();
        var older = await CommittedAsync(OwnDatabase, () => scheduler.RequestAsync(ReportingFactConsts.OrderFactTargetType, order.Id, "Test"));
        await CommittedAsync(OwnDatabase, async () =>
        {
            var repository = GetRequiredService<IRepository<TransportOrder, Guid>>();
            var row = await repository.GetAsync(order.Id, includeDetails: false);
            row.WriteSummary(new OrderSummary(777m, null, null, null, ExecutionProgress.Unplanned, 1, 2, null, null, null, null, null, null, null), DateTime.Now);
            await repository.UpdateAsync(row, autoSave: true);
        });
        var newer = await CommittedAsync(OwnDatabase, () => scheduler.RequestAsync(ReportingFactConsts.OrderFactTargetType, order.Id, "Test"));
        newer.ShouldBeGreaterThan(older);

        var job = GetRequiredService<DerivedValueCalculationJob>();
        using (AsBackgroundProcess())
        {
            await job.ExecuteAsync(new DerivedValueCalculationArgs { TenantId = OwnDatabase, TargetType = ReportingFactConsts.OrderFactTargetType, TargetId = order.Id, Version = older });
        }

        (await OrderFactAsync(OwnDatabase, order.Id)).ShouldNotBeNull().RevenueAmount.ShouldBe(before.RevenueAmount, "the older run was discarded");

        using (AsBackgroundProcess())
        {
            await job.ExecuteAsync(new DerivedValueCalculationArgs { TenantId = OwnDatabase, TargetType = ReportingFactConsts.OrderFactTargetType, TargetId = order.Id, Version = newer });
        }

        (await OrderFactAsync(OwnDatabase, order.Id)).ShouldNotBeNull().RevenueAmount.ShouldBe(777m);
    }

    /* ---------- Freshness ---------- */

    /// <summary>
    /// Design 75 "Freshness" (risk class Medium), as far as a test process can: save to fact through the real outbox and job, driven at once. What production adds is the wait for the
    /// dispatcher's signal and a Hangfire worker under its real load, which only the staging run measures. This holds the part the code owns to the budget's 60 seconds.
    /// </summary>
    [Fact]
    public async Task From_a_save_to_the_fact_stays_far_within_60_seconds_when_the_outbox_and_the_jobs_run_at_once()
    {
        using var features = MetricFeaturesOn();
        var world = await WorldAsync(OwnDatabase);
        var times = new List<TimeSpan>();
        for (var i = 0; i < 10; i++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var order = await ConfirmedOrderAsync(OwnDatabase, world);
            await DriveAsync(OwnDatabase);
            (await OrderFactAsync(OwnDatabase, order.Id)).ShouldNotBeNull();
            watch.Stop();
            times.Add(watch.Elapsed);
        }

        times.Sort();
        times[(int)Math.Ceiling(times.Count * 0.95) - 1].ShouldBeLessThan(TimeSpan.FromSeconds(60));
    }

    /* ---------- The repair ---------- */

    [Fact]
    public async Task The_daily_repair_catches_a_lost_update_and_a_lost_delete_of_orders_and_trips()
    {
        using var features = MetricFeaturesOn();
        var world = await WorldAsync(OwnDatabase);
        var changed = await ConfirmedOrderAsync(OwnDatabase, world);
        var deleted = await ConfirmedOrderAsync(OwnDatabase, world);
        var changedTrip = await TripAsync(OwnDatabase, world);
        var deletedTrip = await TripAsync(OwnDatabase, world);
        await DriveAsync(OwnDatabase);
        (await OrderFactAsync(OwnDatabase, deleted.Id)).ShouldNotBeNull();
        (await TripFactAsync(OwnDatabase, deletedTrip)).ShouldNotBeNull();
        await CommittedAsync(OwnDatabase, () => State.SetBackfillCompletedAtAsync(DateTime.Now));

        /* The rows change straight in the tables: no event reaches Reporting, as when delivery was lost. */
        await CommittedAsync(OwnDatabase, async () =>
        {
            var orders = GetRequiredService<IRepository<TransportOrder, Guid>>();
            var row = await orders.GetAsync(changed.Id, includeDetails: false);
            row.WriteSummary(new OrderSummary(1_234.5m, null, null, null, ExecutionProgress.Unplanned, 1, 2, null, null, null, null, null, null, null), DateTime.Now);
            await orders.UpdateAsync(row, autoSave: true);
            await Run.RemoveWithoutEventsAsync(deleted.Id);

            var trips = GetRequiredService<IRepository<TransportTrip, Guid>>();
            var trip = await trips.GetAsync(changedTrip, includeDetails: false);
            trip.SetMoney(321m, 654m);
            await trips.UpdateAsync(trip, autoSave: true);
            var gone = await trips.GetAsync(deletedTrip, includeDetails: false);
            await trips.DeleteAsync(gone, autoSave: true);
        });
        await DriveFactsAsync(OwnDatabase);
        (await OrderFactAsync(OwnDatabase, changed.Id)).ShouldNotBeNull().RevenueAmount.ShouldNotBe(1_234.5m, "the update was lost");
        (await OrderFactAsync(OwnDatabase, deleted.Id)).ShouldNotBeNull("the delete was lost");

        var runner = GetRequiredService<ReportingFactRepairRunner>();
        using (AsBackgroundProcess())
        using (CurrentTenant.Change(OwnDatabase))
        {
            await runner.RunForCurrentTenantAsync();
        }

        await DriveFactsAsync(OwnDatabase);

        (await OrderFactAsync(OwnDatabase, changed.Id)).ShouldNotBeNull().RevenueAmount.ShouldBe(1_234.5m);
        (await OrderFactAsync(OwnDatabase, deleted.Id)).ShouldBeNull();
        var repairedTrip = (await TripFactAsync(OwnDatabase, changedTrip)).ShouldNotBeNull();
        repairedTrip.CostAmount.ShouldBe(321m);
        repairedTrip.RevenueAmount.ShouldBe(654m);
        (await TripFactAsync(OwnDatabase, deletedTrip)).ShouldBeNull();
    }

    [Fact]
    public async Task A_tenant_is_due_for_repair_24_hours_after_its_last_run_whatever_a_restart_did_to_the_timer()
    {
        using var features = MetricFeaturesOn();
        await CommittedAsync(OwnDatabase, () => State.SetBackfillCompletedAtAsync(DateTime.Now));
        var runner = GetRequiredService<ReportingFactRepairRunner>();

        await CommittedAsync(OwnDatabase, () => State.SetRepairLastRunAtAsync(DateTime.Now.AddHours(-23)));
        using (AsBackgroundProcess())
        using (CurrentTenant.Change(OwnDatabase))
        {
            (await runner.RunIfDueForCurrentTenantAsync()).ShouldBeFalse("23 hours after the last run");
        }

        await CommittedAsync(OwnDatabase, () => State.SetRepairLastRunAtAsync(DateTime.Now.AddHours(-25)));
        using (AsBackgroundProcess())
        using (CurrentTenant.Change(OwnDatabase))
        {
            (await runner.RunIfDueForCurrentTenantAsync()).ShouldBeTrue("25 hours after the last run");
            (await runner.RunIfDueForCurrentTenantAsync()).ShouldBeFalse("the run stored its time");
        }
    }

    /* ---------- The feature ---------- */

    [Fact]
    public async Task Without_the_feature_the_handlers_skip_and_the_calculators_write_nothing()
    {
        /* No features overridden: Reporting.Metrics is off by default. */
        var world = await WorldAsync(OwnDatabase);
        var order = await ConfirmedOrderAsync(OwnDatabase, world);
        var tripId = await TripAsync(OwnDatabase, world);
        await DriveAsync(OwnDatabase);
        (await OrderFactAsync(OwnDatabase, order.Id)).ShouldBeNull();
        (await TripFactAsync(OwnDatabase, tripId)).ShouldBeNull();
        (await CommittedAsync(OwnDatabase, () => GetRequiredService<IDerivedValueStateStore>().FindAsync(ReportingFactConsts.OrderFactTargetType, order.Id), transactional: false))
            .ShouldBeNull("the handlers asked for nothing");

        /* A job requested just before the feature went off. */
        var job = GetRequiredService<DerivedValueCalculationJob>();
        long version;
        using (MetricFeaturesOn())
        {
            version = await CommittedAsync(OwnDatabase, () => GetRequiredService<IDerivedValueScheduler>().RequestAsync(ReportingFactConsts.OrderFactTargetType, order.Id, "Test"));
        }

        using (AsBackgroundProcess())
        {
            await job.ExecuteAsync(new DerivedValueCalculationArgs { TenantId = OwnDatabase, TargetType = ReportingFactConsts.OrderFactTargetType, TargetId = order.Id, Version = version });
        }

        (await OrderFactAsync(OwnDatabase, order.Id)).ShouldBeNull("the calculators also check the feature");
    }

    /* ---------- Tenant isolation of the writes ---------- */

    [Fact]
    public async Task A_handler_and_a_job_write_only_under_the_raising_tenant_on_both_tiers()
    {
        using var features = MetricFeaturesOn();
        var ownWorld = await WorldAsync(OwnDatabase);
        var sharedWorld = await WorldAsync(SharedTier);
        var ownOrder = await ConfirmedOrderAsync(OwnDatabase, ownWorld);
        var sharedOrder = await ConfirmedOrderAsync(SharedTier, sharedWorld);
        var ownTrip = await TripAsync(OwnDatabase, ownWorld);
        var sharedTrip = await TripAsync(SharedTier, sharedWorld);

        await DriveAsync(OwnDatabase);
        await DriveAsync(SharedTier);

        (await OrderFactAsync(OwnDatabase, ownOrder.Id)).ShouldNotBeNull().TenantId.ShouldBe(OwnDatabase);
        (await OrderFactAsync(SharedTier, sharedOrder.Id)).ShouldNotBeNull().TenantId.ShouldBe(SharedTier);
        (await TripFactAsync(OwnDatabase, ownTrip)).ShouldNotBeNull().TenantId.ShouldBe(OwnDatabase);
        (await TripFactAsync(SharedTier, sharedTrip)).ShouldNotBeNull().TenantId.ShouldBe(SharedTier);

        (await OrderFactAsync(SharedTier, ownOrder.Id)).ShouldBeNull();
        (await OrderFactAsync(OwnDatabase, sharedOrder.Id)).ShouldBeNull();
        (await TripFactAsync(SharedTier, ownTrip)).ShouldBeNull();
        (await TripFactAsync(OwnDatabase, sharedTrip)).ShouldBeNull();

        /* In the databases themselves: the own-database tenant's rows are not in the host database, the shared tenant's not in the other. */
        var host = PostgreSqlTestDatabases.HostDatabase;
        var own = PostgreSqlTestDatabases.TransportTenantADatabase;
        SqlCount(host, $"SELECT count(*) FROM reporting.\"OrderFacts\" WHERE \"Id\" = '{ownOrder.Id}'").ShouldBe(0);
        SqlCount(own, $"SELECT count(*) FROM reporting.\"OrderFacts\" WHERE \"Id\" = '{ownOrder.Id}'").ShouldBe(1);
        SqlCount(host, $"SELECT count(*) FROM reporting.\"OrderFacts\" WHERE \"Id\" = '{sharedOrder.Id}' AND \"TenantId\" = '{SharedTier}'").ShouldBe(1);
        SqlCount(own, $"SELECT count(*) FROM reporting.\"OrderFacts\" WHERE \"Id\" = '{sharedOrder.Id}'").ShouldBe(0);
        SqlCount(host, $"SELECT count(*) FROM reporting.\"TripFacts\" WHERE \"Id\" = '{ownTrip}'").ShouldBe(0);
        SqlCount(own, $"SELECT count(*) FROM reporting.\"TripFacts\" WHERE \"Id\" = '{sharedTrip}'").ShouldBe(0);
    }

    [Fact]
    public async Task A_job_of_one_tenant_naming_a_record_of_another_finds_no_source_and_writes_nothing()
    {
        using var features = MetricFeaturesOn();
        var sharedWorld = await WorldAsync(SharedTier);
        var sharedOrder = await ConfirmedOrderAsync(SharedTier, sharedWorld);
        await DriveAsync(SharedTier);
        (await OrderFactAsync(SharedTier, sharedOrder.Id)).ShouldNotBeNull();

        var job = GetRequiredService<DerivedValueCalculationJob>();
        var version = await CommittedAsync(OwnDatabase, () => GetRequiredService<IDerivedValueScheduler>().RequestAsync(ReportingFactConsts.OrderFactTargetType, sharedOrder.Id, "Test"));
        using (AsBackgroundProcess())
        {
            await job.ExecuteAsync(new DerivedValueCalculationArgs { TenantId = OwnDatabase, TargetType = ReportingFactConsts.OrderFactTargetType, TargetId = sharedOrder.Id, Version = version });
        }

        (await OrderFactAsync(OwnDatabase, sharedOrder.Id)).ShouldBeNull();
        (await OrderFactAsync(SharedTier, sharedOrder.Id)).ShouldNotBeNull("the other tenant's fact stays");
    }

    [Fact]
    public async Task The_shared_tier_tenants_filter_keeps_their_facts_apart_in_one_database_for_each_grain()
    {
        using var features = MetricFeaturesOn();
        var day = new DateOnly(2026, 11, 2);
        var customer = Guid.NewGuid();
        var mine = PostgreSqlTestDatabases.SharedTenantId;
        var other = PostgreSqlTestDatabases.TransportSharedTenantId;
        var carrier = Guid.NewGuid();
        var unit = Guid.NewGuid();

        await InRolledBackTransactionAsync(null, async () =>
        {
            using (CurrentTenant.Change(mine))
            {
                await CalculateOrderFactAsync(await SeedOrderAsync(customer, await OrderTypeOfTenantAsync(), day, revenue: 11m, organizationUnitId: unit));
                await CalculateTripFactAsync(await SeedTripAsync(await TripTypeOfTenantAsync(), day, carrierId: carrier, cost: 22m, organizationUnitId: unit));
                await State.SetBackfillCompletedAtAsync(DateTime.Now);
                await FlushAsync();
            }

            using (CurrentTenant.Change(other))
            {
                await CalculateOrderFactAsync(await SeedOrderAsync(customer, await OrderTypeOfTenantAsync(), day, revenue: 1_000m, organizationUnitId: unit));
                await CalculateTripFactAsync(await SeedTripAsync(await TripTypeOfTenantAsync(), day, carrierId: carrier, cost: 2_000m, organizationUnitId: unit));
                await State.SetBackfillCompletedAtAsync(DateTime.Now);
                await FlushAsync();
            }

            using (CurrentTenant.Change(mine))
            {
                (await QueryAsync(Ask(day, day, [MetricKeys.OrderRevenue]))).Rows!.Single().Values[MetricKeys.OrderRevenue].ShouldBe(11m);
                (await QueryAsync(Ask(day, day, [MetricKeys.TripCost]))).Rows!.Single().Values[MetricKeys.TripCost].ShouldBe(22m);
                (await QueryAsync(Ask(day, day, [MetricKeys.OrderCount], [MetricBreakdowns.Customer]))).Rows!.Single().Values[MetricKeys.OrderCount].ShouldBe(1m);
                (await QueryAsync(Ask(day, day, [MetricKeys.TripCount], [MetricBreakdowns.Carrier]))).Rows!.Single().Values[MetricKeys.TripCount].ShouldBe(1m);
                (await OrderFacts.CountAsync()).ShouldBe(1);
                (await TripFacts.CountAsync()).ShouldBe(1);
            }

            /* Clearing one tenant's facts leaves the other's. */
            using (CurrentTenant.Change(other))
            {
                await State.ClearFactsAsync();
                await FlushAsync();
                (await OrderFacts.CountAsync()).ShouldBe(0);
            }

            using (CurrentTenant.Change(mine))
            {
                (await OrderFacts.CountAsync()).ShouldBe(1, "a shared-tier tenant clears only its own rows");
                (await TripFacts.CountAsync()).ShouldBe(1);
            }
        });
    }
}
