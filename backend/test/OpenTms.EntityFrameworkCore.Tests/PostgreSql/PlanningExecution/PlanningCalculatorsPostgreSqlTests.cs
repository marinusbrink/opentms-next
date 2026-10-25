using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Fakes;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.DerivedValues;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.PlanningExecution;

/// <summary>
/// The two Planning calculators run by the real design-49 job against PostgreSQL (design 59 "Calculators"): the leg
/// route (distance, speed-corrected duration, toll, the great-circle CO₂ of an unplanned leg), the trip (stop times and
/// cumulative distances, load, CO₂ with the per-leg allocation, cost and revenue allocation, warnings, the recalculated
/// event in the tenant's outbox), a superseded version that is skipped, a failing provider that keeps the old values and
/// is retried, and a provider without a route that warns. The rows commit (the job runs its own transaction) under fresh
/// ids in tenant A's database. Risk class: Critical.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PlanningCalculatorsPostgreSqlTests : PlanningPostgreSqlTestBase
{
    private static readonly AbpUnitOfWorkOptions Transactional = new() { IsTransactional = true };

    private DerivedValueCalculationJob Job => GetRequiredService<DerivedValueCalculationJob>();

    private IDerivedValueStateStore States => GetRequiredService<IDerivedValueStateStore>();

    private async Task<T> CommittedAsync<T>(Func<Task<T>> action)
    {
        using (CurrentTenant.Change(TenantA))
        using (var unitOfWork = UnitOfWorkManager.Begin(Transactional, requiresNew: true))
        {
            var result = await action();
            await unitOfWork.CompleteAsync();
            return result;
        }
    }

    private Task CommittedAsync(Func<Task> action) => CommittedAsync(async () => { await action(); return 0; });

    private async Task<long> RequestedVersionAsync(string targetType, Guid id)
        => await CommittedAsync(async () => (await States.FindAsync(targetType, id)).ShouldNotBeNull().RequestedVersion);

    private Task RunAsync(string targetType, Guid id, long version)
        => Job.ExecuteAsync(new DerivedValueCalculationArgs { TenantId = TenantA, TargetType = targetType, TargetId = id, Version = version });

    private async Task<TransportTrip> TripAsync(Guid id) => await CommittedAsync(() => Operations.GetWithDetailsAsync(id));

    private async Task<List<Leg>> LegsAsync(Guid orderId) => await CommittedAsync(async () => (await LegRepository.GetListAsync(l => l.OrderId == orderId)).OrderBy(l => l.Position).ToList());

    private sealed record Setup(Guid OrderId, Guid TripId, List<Leg> Legs);

    private async Task<Setup> PlanOneOrderAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        return await CommittedAsync(async () =>
        {
            var depot = await PlaceAsync("DEP" + suffix, "Depot " + suffix, 52.0907, 5.1214, "Utrecht");
            var a = await PlaceAsync("A" + suffix, "Customer A " + suffix, 52.3676, 4.9041);
            var b = await PlaceAsync("B" + suffix, "Customer B " + suffix, 51.9244, 4.4777, "Rotterdam");
            var orderId = RegisterOrder(TenantA, a.RelationId,
                LineOf(10, 8000, StopAt(depot.RelationId, depot.AddressId, Load, "Depot", 52.0907, 5.1214, windowFrom: At(6), windowTill: At(9)),
                    StopAt(a.RelationId, a.AddressId, Unload, "Customer A", 52.3676, 4.9041, windowFrom: At(7), windowTill: At(12))),
                LineOf(4, 2000, StopAt(depot.RelationId, depot.AddressId, Load, "Depot", 52.0907, 5.1214),
                    StopAt(b.RelationId, b.AddressId, Unload, "Customer B", 51.9244, 4.4777, windowFrom: At(16), windowTill: At(17), city: "Rotterdam")));
            var legs = await ConfirmAsync(orderId);
            legs.Count.ShouldBe(2);
            World.OrderTotals[(TenantA, orderId)] = new RateLineTotalsDto { AnchorId = orderId, TotalAmount = 1_000m, CalculationVersion = 1 };

            var trip = await CreateTripAsync(await SeededTripTypeAsync(), At(6), At(18));
            World.TripTotals[(TenantA, trip.Id)] = new RateLineTotalsDto { AnchorId = trip.Id, TotalAmount = 400m, CalculationVersion = 1 };
            await Operations.AssignAsync(trip, legs, allowOutsideWindow: false, afterStopId: null, moving: false);
            return new Setup(orderId, trip.Id, legs);
        });
    }

    [Fact]
    public async Task The_leg_route_calculator_stores_distance_speed_corrected_duration_toll_and_the_fallback_co2_of_an_unplanned_leg()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var orderId = await CommittedAsync(async () =>
        {
            var a = await PlaceAsync("RA" + suffix, "A", 52.0, 5.0);
            var b = await PlaceAsync("RB" + suffix, "B", 52.5, 5.5);
            var id = RegisterOrder(TenantA, a.RelationId, LineOf(2, 1500, StopAt(a.RelationId, a.AddressId, Load, "A", 52.0, 5.0), StopAt(b.RelationId, b.AddressId, Unload, "B", 52.5, 5.5)));
            (await ConfirmAsync(id)).ShouldHaveSingleItem();
            return id;
        });
        var leg = (await LegsAsync(orderId)).Single();
        leg.DistanceM.ShouldBeNull("reads never calculate; the job does");
        var version = await RequestedVersionAsync(PlanningExecutionApplicationModule.LegTargetType, leg.Id);

        World.RoutingAvailable = true;
        World.RoutingThrows = false;
        await RunAsync(PlanningExecutionApplicationModule.LegTargetType, leg.Id, version);

        var routed = (await LegsAsync(orderId)).Single();
        var expectedDistance = (int)Math.Round(PlanningFakeWorld.GreatCircleM(new(52.0, 5.0), new(52.5, 5.5)) * World.RoadFactor);
        routed.DistanceM.ShouldBe(expectedDistance);
        routed.DurationS.ShouldBe((int)Math.Round(Math.Round(expectedDistance / World.SpeedMps) * 1.2, MidpointRounding.AwayFromZero), "the default speed correction of 120 %");
        routed.TollDistanceM.ShouldBe(expectedDistance / 2);
        routed.RouteProvider.ShouldBe("Fake");
        routed.RoutedAt.ShouldNotBeNull();
        routed.EstimatedCo2Kg.ShouldNotBeNull("an unplanned leg gets the great-circle estimate");
        routed.Co2Provider.ShouldBe("GreatCircleCpi");
        World.RouteCalls.ShouldContain(c => c.TenantId == TenantA && c.Vehicle != null && c.Vehicle.GrossWeightKg == 40_000, "no trip: the default 40 t vehicle");

        (await CommittedAsync(async () => (await States.FindAsync(PlanningExecutionApplicationModule.LegTargetType, leg.Id))!.Status)).ShouldBe(DerivedValueStatus.UpToDate);
    }

    [Fact]
    public async Task The_trip_calculator_writes_stop_times_distances_load_co2_allocation_warnings_and_publishes_the_fact()
    {
        var setup = await PlanOneOrderAsync();
        World.RoutingAvailable = true;
        World.RoutingThrows = false;

        /* Route the legs first (their distances drive the allocation), then the trip. */
        foreach (var leg in setup.Legs)
        {
            await RunAsync(PlanningExecutionApplicationModule.LegTargetType, leg.Id, await RequestedVersionAsync(PlanningExecutionApplicationModule.LegTargetType, leg.Id));
        }

        var version = await RequestedVersionAsync(PlanningExecutionApplicationModule.TripTargetType, setup.TripId);
        await RunAsync(PlanningExecutionApplicationModule.TripTargetType, setup.TripId, version);

        var trip = await TripAsync(setup.TripId);
        var legs = await LegsAsync(setup.OrderId);
        var stops = trip.OrderedStops.ToList();
        stops.Count.ShouldBe(3, "one depot stop for two loads, two unloads");
        stops[0].PlannedArrival.ShouldBe(trip.PlannedDeparture, "the first stop is where the trip departs");
        stops[0].PlannedDeparture.ShouldBe(trip.PlannedDeparture.AddMinutes(30));
        stops[0].PlannedCumulativeDistanceM.ShouldBe(0);
        stops[1].PlannedArrival.ShouldNotBeNull();
        stops[1].PlannedArrival!.Value.ShouldBeGreaterThan(stops[0].PlannedDeparture!.Value);
        stops[1].PlannedCumulativeDistanceM.ShouldNotBeNull().ShouldBeGreaterThan(0);
        stops[2].PlannedCumulativeDistanceM.ShouldNotBeNull().ShouldBeGreaterThan(stops[1].PlannedCumulativeDistanceM!.Value);
        stops[1].EstimatedArrival.ShouldBe(stops[1].PlannedArrival, "estimated equals planned until the ingest run");
        trip.PlannedDistanceM.ShouldBe(stops[2].PlannedCumulativeDistanceM);
        trip.PlannedDurationS.ShouldNotBeNull().ShouldBeGreaterThan(0);
        trip.PlannedArrival.ShouldBe(At(18), "no arrival rule and no arrival address: the planned arrival stays");

        trip.LoadFactor.ShouldBeNull("no vehicles (design 59 assumption 2)");
        trip.LoadingMeters.ShouldBe(5.6);
        trip.Co2Kg.ShouldNotBeNull();
        trip.Co2Kg!.Value.ShouldBeGreaterThan(0);
        legs.ShouldAllBe(l => l.EstimatedCo2Kg != null && l.Co2FactorSet == "Fake");
        legs.Sum(l => l.EstimatedCo2Kg!.Value).ShouldBe(trip.Co2Kg.Value);

        /* AD-20: trip cost by km share, order revenue by km share; the sums are exact. */
        trip.CostAmount.ShouldBe(400m);
        legs.Sum(l => l.AllocatedCostAmount!.Value).ShouldBe(400m);
        legs.Sum(l => l.AllocatedRevenueAmount!.Value).ShouldBe(1_000m, "the trip holds every leg of the order");
        trip.RevenueAmount.ShouldBe(1_000m);
        legs.ShouldAllBe(l => l.AllocationVersion == version && l.AllocatedAt != null);
        var longer = legs.OrderByDescending(l => l.DistanceM).First();
        longer.AllocatedCostAmount!.Value.ShouldBeGreaterThan(legs.Min(l => l.AllocatedCostAmount!.Value));

        trip.Warnings.ShouldContain(w => w.Code == TripWarningCode.EtaOutsideStopWindow, "Customer B's window of 16:00 is far from the morning arrival");
        trip.Warnings.ShouldNotContain(w => w.Code == TripWarningCode.NoCostPriceSource);
        trip.Warnings.ShouldNotContain(w => w.Code == TripWarningCode.AddressNotGeocoded);
        trip.Warnings.ShouldNotContain(w => w.Code == TripWarningCode.RoutingNotConfigured);
        trip.CalculatedAt.ShouldNotBeNull();
        World.MatrixCalls.ShouldContain(c => c.TenantId == TenantA && c.Points == 3);

        (await PostgreSqlTestDatabases.CountOutboxRowsAsync(PostgreSqlTestDatabases.TenantADatabase, setup.TripId.ToString())).ShouldBeGreaterThan(0, "the recalculated fact sits in the tenant's outbox");
        (await PostgreSqlTestDatabases.CountOutboxRowsAsync(PostgreSqlTestDatabases.HostDatabase, setup.TripId.ToString())).ShouldBe(0);
    }

    [Fact]
    public async Task A_superseded_version_is_skipped_a_failing_provider_keeps_the_old_values_and_is_retried_and_no_route_warns()
    {
        var setup = await PlanOneOrderAsync();
        World.RoutingAvailable = true;
        World.RoutingThrows = false;
        var first = await RequestedVersionAsync(PlanningExecutionApplicationModule.TripTargetType, setup.TripId);
        await RunAsync(PlanningExecutionApplicationModule.TripTargetType, setup.TripId, first);
        var calculated = (await TripAsync(setup.TripId)).CalculatedAt.ShouldNotBeNull();

        /* A newer save arrived: the older job is skipped and writes nothing. */
        var second = await CommittedAsync(() => Operations.RequestRecalculationAsync(setup.TripId, "Test"));
        await RunAsync(PlanningExecutionApplicationModule.TripTargetType, setup.TripId, first);
        (await TripAsync(setup.TripId)).CalculatedAt.ShouldBe(calculated, "the superseded version changed nothing");
        (await CommittedAsync(async () => (await States.FindAsync(PlanningExecutionApplicationModule.TripTargetType, setup.TripId))!.CalculatedVersion)).ShouldBe(first);

        /* The provider throws: the job fails, the stored values stay, the state says Failed; the retry succeeds. */
        World.RoutingThrows = true;
        try
        {
            await Should.ThrowAsync<InvalidOperationException>(() => RunAsync(PlanningExecutionApplicationModule.TripTargetType, setup.TripId, second));
        }
        finally
        {
            World.RoutingThrows = false;
        }

        (await TripAsync(setup.TripId)).CalculatedAt.ShouldBe(calculated);
        var state = await CommittedAsync(async () => (await States.FindAsync(PlanningExecutionApplicationModule.TripTargetType, setup.TripId))!);
        state.Status.ShouldBe(DerivedValueStatus.Failed);
        state.LastErrorCode.ShouldBe(nameof(InvalidOperationException));

        await RunAsync(PlanningExecutionApplicationModule.TripTargetType, setup.TripId, second);
        var retried = await TripAsync(setup.TripId);
        retried.CalculatedAt!.Value.ShouldBeGreaterThan(calculated);
        (await CommittedAsync(async () => (await States.FindAsync(PlanningExecutionApplicationModule.TripTargetType, setup.TripId))!.Status)).ShouldBe(DerivedValueStatus.UpToDate);

        /* No route from the provider: the stop times stay, the trip warns. */
        World.RoutingAvailable = false;
        try
        {
            var third = await CommittedAsync(() => Operations.RequestRecalculationAsync(setup.TripId, "Test"));
            await RunAsync(PlanningExecutionApplicationModule.TripTargetType, setup.TripId, third);
        }
        finally
        {
            World.RoutingAvailable = true;
        }

        var unrouted = await TripAsync(setup.TripId);
        unrouted.Warnings.ShouldContain(w => w.Code == TripWarningCode.RoutingNotConfigured);
        unrouted.OrderedStops.First().PlannedArrival.ShouldBe(retried.OrderedStops.First().PlannedArrival, "the old times are kept");
    }
}
