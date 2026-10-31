using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData.Relations;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.Planboard;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.TestDoubles;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// Design 68 §5 and §7 on SQLite: stop optimisation (queued with 202 semantics, refused with 409 when nothing can move; the
/// job honours pickup-before-delivery, keeps the fixed first and last stop and the driven prefix, uses the great-circle
/// heuristic when neither VROOM nor routing is configured, is idempotent, notifies after the commit); the unplanned list's
/// groups (D18); split-line and return-leg on an unplanned leg; the 100-id bound of the list refetch; and negative
/// tenant-isolation tests. Risk class: High (planning).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class PlanboardStopsAndLegsTests : PlanboardTestBase
{
    private IPlanboardStopOptimizationAppService Optimizer => GetRequiredService<IPlanboardStopOptimizationAppService>();

    private RecordingBackgroundJobManager Jobs => GetRequiredService<RecordingBackgroundJobManager>();

    private async Task RunQueuedOptimisationsAsync()
    {
        foreach (var args in Jobs.Drain<PlanboardStopOptimizationArgs>())
        {
            await GetRequiredService<PlanboardStopOptimizationJob>().ExecuteAsync(args);
        }
    }

    private async Task<(Guid RelationId, Guid AddressId)> AtLongitude(string code, double longitude)
        => await PlaceAsync(code, "Place " + code, 52.0, longitude, RelationRoles.Customer | RelationRoles.Depot, "City " + code);

    private Task<TransportTripDto> ManualStopAsync(Guid tripId, (Guid RelationId, Guid AddressId) place, int? position = null)
        => Trips.AddStopAsync(tripId, new AddManualStopDto { WaypointTypeId = Guid.NewGuid(), RelationId = place.RelationId, RelationAddressId = place.AddressId, Position = position, PlannedDurationMin = 10 });

    /* Stop optimisation */

    [Fact]
    public async Task The_job_reorders_the_free_stops_by_the_great_circle_heuristic_keeping_the_manual_first_and_last_stop()
    {
        using var _ = InTenantA();
        var type = await TripTypeAsync();
        var first = await AtLongitude("W40", 4.0);
        var east55 = await AtLongitude("E55", 5.5);
        var mid45 = await AtLongitude("M45", 4.5);
        var mid50 = await AtLongitude("M50", 5.0);
        var last = await AtLongitude("E60", 6.0);
        var trip = await CreateTripAsync(type, At(6), At(18));
        foreach (var place in new[] { first, east55, mid45, mid50, last })
        {
            trip = await ManualStopAsync(trip.Id, place);
        }

        trip.Stops.OrderBy(s => s.Position).Select(s => s.Longitude).ShouldBe([4.0, 5.5, 4.5, 5.0, 6.0]);

        var queued = await Optimizer.OptimizeStopsAsync(trip.Id);
        queued.TripId.ShouldBe(trip.Id);
        queued.JobId.ShouldNotBeNullOrEmpty();
        var args = Jobs.Of<PlanboardStopOptimizationArgs>().Last(a => a.TripId == trip.Id);
        args.TenantId.ShouldBe(TenantA, "the job runs in the requesting tenant");
        var noticesBefore = NoticesOf(trip.Id).Count;

        await RunQueuedOptimisationsAsync();

        var optimised = await Trips.GetAsync(trip.Id);
        optimised.Stops.OrderBy(s => s.Position).Select(s => s.Longitude).ShouldBe([4.0, 4.5, 5.0, 5.5, 6.0], "first and last stay; the free stops follow the line");
        optimised.Stops.OrderBy(s => s.Position).Select(s => s.Position).ShouldBe([1, 2, 3, 4, 5]);
        Requests.ReasonsFor(PlanningExecutionApplicationModule.TripTargetType, trip.Id).ShouldContain(nameof(TripChangeKind.Stops));
        NoticesOf(trip.Id).Count.ShouldBe(noticesBefore + 1, "one notice after the job's commit");

        /* Idempotent: a second run finds the optimised order and changes nothing. */
        await Optimizer.OptimizeStopsAsync(trip.Id);
        await RunQueuedOptimisationsAsync();
        var again = await Trips.GetAsync(trip.Id);
        again.ConcurrencyStamp.ShouldBe(optimised.ConcurrencyStamp);
        again.Stops.OrderBy(s => s.Position).Select(s => s.Longitude).ShouldBe([4.0, 4.5, 5.0, 5.5, 6.0]);
    }

    [Fact]
    public async Task The_job_puts_a_legs_pickup_before_its_delivery_even_when_the_shortest_order_would_not()
    {
        using var _ = InTenantA();
        var type = await TripTypeAsync();
        var first = await AtLongitude("W40", 4.0);
        var pickup = await AtLongitude("E55", 5.5);
        var delivery = await AtLongitude("W45", 4.5);
        var mid = await AtLongitude("M50", 5.0);
        var last = await AtLongitude("E60", 6.0);
        var order = await ConfirmedOrderAsync(pickup.RelationId, LineOf(5, 500,
            StopAt(pickup, Load, Day, At(8), At(10)), StopAt(delivery, Unload, Day, At(12), At(14))));
        var trip = await CreateTripAsync(type, At(6), At(18));
        trip = await Trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = await LegIdsOfOrderAsync(order) });
        trip = await ManualStopAsync(trip.Id, first, 1);
        trip = await ManualStopAsync(trip.Id, mid, 2);
        trip = await ManualStopAsync(trip.Id, last);
        trip.Stops.Count.ShouldBe(5);

        await Optimizer.OptimizeStopsAsync(trip.Id);
        await RunQueuedOptimisationsAsync();

        var stops = (await Trips.GetAsync(trip.Id)).Stops.OrderBy(s => s.Position).ToList();
        stops.First().Longitude.ShouldBe(4.0);
        stops.Last().Longitude.ShouldBe(6.0);
        var pickupPosition = stops.Single(s => s.Longitude == 5.5).Position;
        var deliveryPosition = stops.Single(s => s.Longitude == 4.5).Position;
        pickupPosition.ShouldBeLessThan(deliveryPosition, "pickup before delivery of the same leg");
    }

    [Fact]
    public async Task A_stop_with_actuals_and_every_stop_before_it_stay_and_nothing_to_move_is_refused_with_409()
    {
        using var _ = InTenantA();
        var type = await TripTypeAsync();
        var a = await AtLongitude("A", 4.0);
        var b = await AtLongitude("B", 6.0);
        var c = await AtLongitude("C", 5.0);
        var d = await AtLongitude("D", 4.5);
        var trip = await CreateTripAsync(type, At(6), At(18));
        foreach (var place in new[] { a, b, c, d })
        {
            trip = await ManualStopAsync(trip.Id, place);
        }

        /* Only the first and the last stop: nothing is free. */
        var two = await CreateTripAsync(type, At(6), At(18));
        two = await ManualStopAsync(two.Id, a);
        two = await ManualStopAsync(two.Id, b);
        (await Should.ThrowAsync<BusinessException>(() => Optimizer.OptimizeStopsAsync(two.Id))).Code.ShouldBe(PlanningExecutionErrorCodes.StopsNotOptimizable);

        /* Driven up to the second stop: B is history; C (free) alone cannot move. */
        await Trips.StartAsync(trip.Id, new TripTimestampDto { At = At(6) });
        trip = await Trips.RecordStopActualsAsync(trip.Id, trip.Stops.Single(s => s.Position == 2).Id, new StopActualsDto { ActualArrival = At(8) });
        (await Should.ThrowAsync<BusinessException>(() => Optimizer.OptimizeStopsAsync(trip.Id))).Code.ShouldBe(PlanningExecutionErrorCodes.StopsNotOptimizable);

        var completed = await Trips.CompleteAsync(trip.Id, new TripTimestampDto { At = At(17) });
        (await Should.ThrowAsync<BusinessException>(() => Optimizer.OptimizeStopsAsync(completed.Id))).Code.ShouldBe(PlanningExecutionErrorCodes.TripNotAssignable);
        Jobs.Of<PlanboardStopOptimizationArgs>().ShouldNotContain(j => j.TripId == trip.Id || j.TripId == two.Id, "nothing was queued");
    }

    [Fact]
    public async Task The_job_skips_a_trip_that_no_longer_takes_changes_or_is_gone()
    {
        using var _ = InTenantA();
        var type = await TripTypeAsync();
        var trip = await CreateTripAsync(type, At(6), At(18));
        foreach (var lon in new[] { 4.0, 5.5, 4.5, 6.0 })
        {
            trip = await ManualStopAsync(trip.Id, await AtLongitude("L" + lon.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), lon));
        }

        await Optimizer.OptimizeStopsAsync(trip.Id);
        var cancelled = await Trips.CancelAsync(trip.Id);
        await RunQueuedOptimisationsAsync();
        (await Trips.GetAsync(trip.Id)).ConcurrencyStamp.ShouldBe(cancelled.ConcurrencyStamp, "a cancelled trip is left alone");

        await GetRequiredService<PlanboardStopOptimizationJob>().ExecuteAsync(new PlanboardStopOptimizationArgs { TenantId = TenantA, TripId = Guid.NewGuid() });
    }

    [Fact]
    public async Task Negative_tenant_isolation_optimisation_and_its_job_never_reach_another_tenants_trip()
    {
        TransportTripDto trip;
        using (InTenantA())
        {
            var type = await TripTypeAsync();
            trip = await CreateTripAsync(type, At(6), At(18));
            foreach (var lon in new[] { 4.0, 5.5, 4.5, 5.0, 6.0 })
            {
                trip = await ManualStopAsync(trip.Id, await AtLongitude("L" + lon.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), lon));
            }
        }

        using (InTenantB())
        {
            await Should.ThrowAsync<EntityNotFoundException>(() => Optimizer.OptimizeStopsAsync(trip.Id));
        }

        /* A job whose arguments name tenant B finds no such trip and changes nothing in tenant A. */
        await GetRequiredService<PlanboardStopOptimizationJob>().ExecuteAsync(new PlanboardStopOptimizationArgs { TenantId = TenantB, TripId = trip.Id });
        using (InTenantA())
        {
            var untouched = await Trips.GetAsync(trip.Id);
            untouched.ConcurrencyStamp.ShouldBe(trip.ConcurrencyStamp);
            untouched.Stops.OrderBy(s => s.Position).Select(s => s.Longitude).ShouldBe([4.0, 5.5, 4.5, 5.0, 6.0]);
        }
    }

    /* The unplanned list's groups (D18) */

    [Fact]
    public async Task The_groups_count_the_unplanned_legs_per_origin_date_customer_and_city_and_a_group_key_filters_the_list()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.Depot, f.CustomerA), Line(f.Depot, f.CustomerB));
        await ConfirmedOrderAsync(f.CustomerB.RelationId, Line(f.CustomerB, f.CustomerA, fromHour: 8, toHour: 12, day: 27));

        var byDate = await Legs.GetGroupsAsync(new LegListRequestDto { StartRow = 0, EndRow = 100, GroupBy = LegGroupings.OriginDate });
        byDate.Rows.Select(g => (g.Key, g.Count)).ShouldBe([("2026-10-26", 2L), ("2026-10-27", 1L)]);
        byDate.FilteredCount.ShouldBe(2);

        var byCustomer = await Legs.GetGroupsAsync(new LegListRequestDto { StartRow = 0, EndRow = 100, GroupBy = "Customer" });
        byCustomer.Rows.Select(g => (g.Label, g.Count)).ShouldBe([("Customer A", 2L), ("Customer B", 1L)], ignoreOrder: true);

        var byOriginCity = await Legs.GetGroupsAsync(new LegListRequestDto { StartRow = 0, EndRow = 100, GroupBy = LegGroupings.OriginCity });
        byOriginCity.Rows.Select(g => (g.Key, g.Count)).ShouldBe([("Rotterdam", 1L), ("Utrecht", 2L)]);
        var paged = await Legs.GetGroupsAsync(new LegListRequestDto { StartRow = 1, EndRow = 2, GroupBy = LegGroupings.DestinationCity });
        paged.Rows.ShouldHaveSingleItem();
        paged.FilteredCount.ShouldBe(2);

        var inGroup = await Legs.GetListAsync(new LegListRequestDto { StartRow = 0, EndRow = 100, GroupBy = LegGroupings.OriginDate, GroupKey = "2026-10-27" });
        inGroup.Rows.ShouldHaveSingleItem().CustomerRelationId.ShouldBe(f.CustomerB.RelationId);
        var customerKey = byCustomer.Rows.Single(g => g.Label == "Customer A").Key;
        (await Legs.GetListAsync(new LegListRequestDto { StartRow = 0, EndRow = 100, GroupBy = LegGroupings.Customer, GroupKey = customerKey })).Rows.Count.ShouldBe(2);
        (await Legs.GetListAsync(new LegListRequestDto { StartRow = 0, EndRow = 100, GroupBy = LegGroupings.OriginDate, GroupKey = "not-a-date" })).Rows.ShouldBeEmpty();

        (await Should.ThrowAsync<BusinessException>(() => Legs.GetGroupsAsync(new LegListRequestDto { StartRow = 0, EndRow = 100, GroupBy = "orderNumber" })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.UnknownGroupBy);

        using (InTenantB())
        {
            (await Legs.GetGroupsAsync(new LegListRequestDto { StartRow = 0, EndRow = 100, GroupBy = LegGroupings.OriginDate })).Rows.ShouldBeEmpty("negative tenant isolation");
            (await Legs.GetListAsync(new LegListRequestDto { StartRow = 0, EndRow = 100, GroupBy = LegGroupings.Customer, GroupKey = customerKey })).Rows.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task The_list_refetch_by_ids_returns_those_legs_and_trips_and_refuses_more_than_one_hundred()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var order = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.Depot, f.CustomerA), Line(f.Depot, f.CustomerB));
        var legs = await LegIdsOfOrderAsync(order);
        var trip = await TripWithAsync(f.Type, At(6), At(18), [f.Tractor]);

        (await Legs.GetListAsync(new LegListRequestDto { StartRow = 0, EndRow = 100, Ids = [legs[1]] })).Rows.ShouldHaveSingleItem().Id.ShouldBe(legs[1]);
        var tooMany = Enumerable.Range(0, PlanningExecutionConsts.MaxIdsPerQuery + 1).Select(_ => Guid.NewGuid()).ToList();
        (await Should.ThrowAsync<BusinessException>(() => Legs.GetListAsync(new LegListRequestDto { StartRow = 0, EndRow = 100, Ids = tooMany }))).Code.ShouldBe(PlanningExecutionErrorCodes.TooManyIds);

        var tripList = TripList();
        tripList.Ids = [trip.Id];
        (await Trips.GetListAsync(tripList)).Rows.ShouldHaveSingleItem().Id.ShouldBe(trip.Id);
        tripList.Ids = tooMany;
        (await Should.ThrowAsync<BusinessException>(() => Trips.GetListAsync(tripList))).Code.ShouldBe(PlanningExecutionErrorCodes.TooManyIds);

        using (InTenantB())
        {
            (await Legs.GetListAsync(new LegListRequestDto { StartRow = 0, EndRow = 100, Ids = legs })).Rows.ShouldBeEmpty("negative tenant isolation");
            var other = TripList();
            other.Ids = [trip.Id];
            (await Trips.GetListAsync(other)).Rows.ShouldBeEmpty();
        }
    }

    /* Split line and return leg on an unplanned leg (§7) */

    [Fact]
    public async Task Split_line_moves_part_of_an_unplanned_legs_quantity_to_a_new_unplanned_leg_and_is_refused_on_a_trip()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var order = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.Depot, f.CustomerA, quantity: 10));
        var leg = (await LegIdsOfOrderAsync(order)).Single();

        var split = await Legs.SplitLineAsync(leg, new LegSplitLineDto { Quantity = 4 });

        split.Id.ShouldNotBe(leg);
        split.Quantity.ShouldBe(4);
        split.TripId.ShouldBeNull();
        split.OrderLineId.ShouldBe((await LegRepository.GetAsync(leg)).OrderLineId);
        (await LegRepository.GetAsync(leg)).Quantity.ShouldBe(6);
        (await Legs.GetListAsync(LegList(orderId: order))).Rows.Sum(l => l.Quantity).ShouldBe(10, "no quantity is lost");
        Requests.ReasonsFor(PlanningExecutionApplicationModule.LegTargetType, split.Id).ShouldNotBeEmpty("the new leg is routed");
        NoticesOf(split.Id).Last().Hints!.Removed.ShouldBeFalse("a new unplanned leg");
        NoticesOf(leg).ShouldNotBeEmpty();

        await Should.ThrowAsync<BusinessException>(() => Legs.SplitLineAsync(leg, new LegSplitLineDto { Quantity = 6 }));
        await Should.ThrowAsync<BusinessException>(() => Legs.SplitLineAsync(leg, new LegSplitLineDto { Quantity = 0 }));

        var trip = await TripWithAsync(f.Type, At(6), At(18));
        await Trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = [leg] });
        (await Should.ThrowAsync<BusinessException>(() => Legs.SplitLineAsync(leg, new LegSplitLineDto { Quantity = 1 }))).Code.ShouldBe(PlanningExecutionErrorCodes.LegOnTrip);
        (await Should.ThrowAsync<BusinessException>(() => Legs.ReturnLegAsync(leg))).Code.ShouldBe(PlanningExecutionErrorCodes.LegOnTrip);
    }

    [Fact]
    public async Task Return_leg_mirrors_an_unplanned_leg_once()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var order = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.Depot, f.CustomerA));
        var leg = await LegRepository.GetAsync((await LegIdsOfOrderAsync(order)).Single());

        var mirrored = await Legs.ReturnLegAsync(leg.Id);

        mirrored.Id.ShouldNotBe(leg.Id);
        mirrored.TripId.ShouldBeNull();
        mirrored.Origin.RelationId.ShouldBe(leg.DestinationRelationId);
        mirrored.Destination.RelationId.ShouldBe(leg.OriginRelationId);
        mirrored.OrderId.ShouldBe(order);
        (await LegsOfOrderAsync(order)).Count.ShouldBe(2);
        NoticesOf(mirrored.Id).ShouldNotBeEmpty();

        await Should.ThrowAsync<BusinessException>(() => Legs.ReturnLegAsync(leg.Id), "the mirrored leg exists");

        using (InTenantB())
        {
            await Should.ThrowAsync<EntityNotFoundException>(() => Legs.ReturnLegAsync(leg.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => Legs.SplitLineAsync(leg.Id, new LegSplitLineDto { Quantity = 1 }));
        }
    }
}
