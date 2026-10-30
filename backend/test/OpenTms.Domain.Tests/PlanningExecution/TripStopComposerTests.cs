using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using Shouldly;
using Xunit;

namespace OpenTms.PlanningExecution;

/// <summary>
/// TripStopComposer (design 59 "Stop composition"; trips.md 7–15, 23, 28) as pure domain logic: both modes, manual stops,
/// emptied stops, the first/last stop rule, deterministic order and renumbering. Risk class: Critical.
/// </summary>
public class TripStopComposerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Load = Guid.NewGuid();
    private static readonly Guid Unload = Guid.NewGuid();
    private static readonly Guid Unit = Guid.NewGuid();
    private readonly TripStopComposer _composer = new();

    private static (Guid RelationId, Guid AddressId) Place() => (Guid.NewGuid(), Guid.NewGuid());

    private static LegEndSnapshot End((Guid RelationId, Guid AddressId) place, Guid waypointType, string name, int hour, int day = 26)
        => new(Guid.NewGuid(), waypointType, place.RelationId, place.AddressId, name, name, "NL", 52, 5, "Europe/Amsterdam", new DateOnly(2026, 10, day),
            new DateTimeOffset(2026, 10, day, hour, 0, 0, TimeSpan.FromHours(2)), new DateTimeOffset(2026, 10, day, hour + 2, 0, 0, TimeSpan.FromHours(2)), null, 30);

    private static Leg LegBetween(TransportTrip trip, int tripPosition, LegEndSnapshot origin, LegEndSnapshot destination)
    {
        var leg = new Leg(Guid.NewGuid(), Tenant, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, LegSource.Generated, origin, destination,
            new LegQuantities(1, 100, null, null, null, null, Unit, null, null, []));
        leg.AssignTo(trip.Id, tripPosition);
        return leg;
    }

    private static TransportTrip Trip(StopCompositionMode mode, FirstLastStopRule rule = FirstLastStopRule.None)
        => new(Guid.NewGuid(), Tenant, "TR-1", Guid.NewGuid(), new DateOnly(2026, 10, 26), TripExecution.OwnFleet, mode, rule, new DateTimeOffset(2026, 10, 26, 6, 0, 0, TimeSpan.FromHours(2)));

    [Fact]
    public void Merge_mode_shares_a_stop_for_the_same_address_and_activity_and_orders_stops_by_window_then_assignment()
    {
        var trip = Trip(StopCompositionMode.MergeByLocationAndActivity);
        var depot = Place();
        var customerA = Place();
        var customerB = Place();
        var leg1 = LegBetween(trip, 1, End(depot, Load, "Depot", 8), End(customerA, Unload, "A", 12));
        var leg2 = LegBetween(trip, 2, End(depot, Load, "Depot", 8), End(customerB, Unload, "B", 14));

        _composer.Compose(trip, [leg1, leg2], Guid.NewGuid);

        trip.Stops.Count.ShouldBe(3, "two loads at the depot share one stop");
        var stops = trip.OrderedStops.ToList();
        stops.Select(s => s.Position).ShouldBe([1, 2, 3]);
        stops[0].Name.ShouldBe("Depot");
        stops[0].Legs.Count.ShouldBe(2);
        stops[0].PlannedDurationMin.ShouldBe(30);
        stops[1].Name.ShouldBe("A");
        stops[2].Name.ShouldBe("B");
        stops.ShouldAllBe(s => !s.IsManual);
        stops[1].Legs.ShouldHaveSingleItem().Role.ShouldBe(StopLegRole.Destination);
    }

    [Fact]
    public void Merge_mode_never_reuses_a_stop_with_actuals_or_a_different_activity()
    {
        var trip = Trip(StopCompositionMode.MergeByLocationAndActivity);
        var depot = Place();
        var customer = Place();
        var leg1 = LegBetween(trip, 1, End(depot, Load, "Depot", 8), End(customer, Unload, "A", 12));
        _composer.Compose(trip, [leg1], Guid.NewGuid);
        trip.OrderedStops.First().RecordActuals(new DateTimeOffset(2026, 10, 26, 8, 5, 0, TimeSpan.FromHours(2)), null, null);

        var leg2 = LegBetween(trip, 2, End(depot, Load, "Depot", 9), End(depot, Unload, "Depot unload", 9));
        _composer.Compose(trip, [leg1, leg2], Guid.NewGuid);

        trip.Stops.Count.ShouldBe(4, "a visited stop is never reused; unload at the depot is another activity");
        var stops = trip.OrderedStops.ToList();
        stops[0].HasActuals.ShouldBeTrue();
        stops[0].Legs.ShouldHaveSingleItem().LegId.ShouldBe(leg1.Id);
    }

    [Fact]
    public void Shuttle_mode_gives_every_leg_its_own_stop_pair()
    {
        var trip = Trip(StopCompositionMode.OneStopPairPerLeg);
        var depot = Place();
        var customer = Place();
        var leg1 = LegBetween(trip, 1, End(depot, Load, "Depot", 8), End(customer, Unload, "A", 10));
        var leg2 = LegBetween(trip, 2, End(depot, Load, "Depot", 11), End(customer, Unload, "A", 13));

        _composer.Compose(trip, [leg1, leg2], Guid.NewGuid);

        trip.Stops.Count.ShouldBe(4);
        trip.OrderedStops.Select(s => s.Name).ShouldBe(["Depot", "A", "Depot", "A"]);
    }

    [Fact]
    public void Unassigning_shrinks_or_removes_stops_but_manual_stops_survive_and_positions_close_the_gaps()
    {
        var trip = Trip(StopCompositionMode.MergeByLocationAndActivity);
        var depot = Place();
        var a = Place();
        var b = Place();
        var leg1 = LegBetween(trip, 1, End(depot, Load, "Depot", 8), End(a, Unload, "A", 12));
        var leg2 = LegBetween(trip, 2, End(depot, Load, "Depot", 8), End(b, Unload, "B", 14));
        _composer.Compose(trip, [leg1, leg2], Guid.NewGuid);
        var fuel = Place();
        trip.AddManualStop(Guid.NewGuid(), 2, Guid.NewGuid(), fuel.RelationId, fuel.AddressId, "Fuel", "Utrecht", 52, 5, null, 15, null);
        trip.OrderedStops.Select(s => s.Name).ShouldBe(["Depot", "Fuel", "A", "B"]);

        leg2.Unassign();
        _composer.Compose(trip, [leg1], Guid.NewGuid);

        trip.OrderedStops.Select(s => s.Name).ShouldBe(["Depot", "Fuel", "A"]);
        trip.OrderedStops.Select(s => s.Position).ShouldBe([1, 2, 3]);
        trip.OrderedStops.First().Legs.ShouldHaveSingleItem().LegId.ShouldBe(leg1.Id);
        trip.OrderedStops.Single(s => s.IsManual).Name.ShouldBe("Fuel");
    }

    [Fact]
    public void The_first_and_last_stop_rule_sets_departure_and_arrival_and_new_stops_can_follow_a_given_stop()
    {
        var trip = Trip(StopCompositionMode.MergeByLocationAndActivity, FirstLastStopRule.Both);
        var depot = Place();
        var a = Place();
        var b = Place();
        var leg1 = LegBetween(trip, 1, End(depot, Load, "Depot", 8), End(a, Unload, "A", 12));
        _composer.Compose(trip, [leg1], Guid.NewGuid);
        trip.DepartureRelationId.ShouldBe(depot.RelationId);
        trip.DepartureAddressId.ShouldBe(depot.AddressId);
        trip.ArrivalRelationId.ShouldBe(a.RelationId);

        var depotStop = trip.OrderedStops.First();
        var leg2 = LegBetween(trip, 2, End(b, Load, "B", 9), End(a, Unload, "A", 13));
        _composer.Compose(trip, [leg1, leg2], Guid.NewGuid, afterStopId: depotStop.Id);

        trip.OrderedStops.Select(s => s.Name).ShouldBe(["Depot", "B", "A"], "the new origin stop follows the depot; A is shared");
        trip.ArrivalRelationId.ShouldBe(a.RelationId);
        trip.OrderedStops.Last().Legs.Count.ShouldBe(2);
    }

    [Fact]
    public void A_leg_carried_out_separately_gets_its_own_pair_at_the_end_and_is_not_merged_with()
    {
        var trip = Trip(StopCompositionMode.MergeByLocationAndActivity);
        var depot = Place();
        var a = Place();
        var leg1 = LegBetween(trip, 1, End(depot, Load, "Depot", 8), End(a, Unload, "A", 12));
        var leg2 = LegBetween(trip, 2, End(depot, Load, "Depot", 8), End(a, Unload, "A", 12));
        _composer.Compose(trip, [leg1, leg2], Guid.NewGuid);
        trip.Stops.Count.ShouldBe(2);

        leg2.SetCarriedOutSeparately(true);
        foreach (var stop in trip.Stops)
        {
            stop.RemoveLeg(leg2.Id);
        }

        _composer.Compose(trip, [leg1, leg2], Guid.NewGuid);

        trip.Stops.Count.ShouldBe(4);
        trip.OrderedStops.Skip(2).ShouldAllBe(s => s.Legs.Single().LegId == leg2.Id);

        /* A third leg does not join the separate pair. */
        var leg3 = LegBetween(trip, 3, End(depot, Load, "Depot", 8), End(a, Unload, "A", 12));
        _composer.Compose(trip, [leg1, leg2, leg3], Guid.NewGuid);
        trip.Stops.Count.ShouldBe(4);
        trip.OrderedStops.First().Legs.Select(l => l.LegId).ShouldBe([leg1.Id, leg3.Id], ignoreOrder: true);
    }

    [Fact]
    public void Composing_twice_with_the_same_legs_changes_nothing()
    {
        var trip = Trip(StopCompositionMode.MergeByLocationAndActivity);
        var leg1 = LegBetween(trip, 1, End(Place(), Load, "X", 8), End(Place(), Unload, "Y", 12));
        _composer.Compose(trip, [leg1], Guid.NewGuid);
        var ids = trip.OrderedStops.Select(s => s.Id).ToList();

        _composer.Compose(trip, [leg1], Guid.NewGuid);

        trip.OrderedStops.Select(s => s.Id).ShouldBe(ids);
    }

    [Fact]
    public void More_than_the_maximum_stops_is_refused()
    {
        var trip = Trip(StopCompositionMode.OneStopPairPerLeg);
        var legs = Enumerable.Range(0, PlanningExecutionConsts.MaxStopsPerTrip / 2 + 1)
            .Select(i => LegBetween(trip, i + 1, End(Place(), Load, "L" + i, 8), End(Place(), Unload, "U" + i, 12)))
            .ToList();

        Should.Throw<Volo.Abp.BusinessException>(() => _composer.Compose(trip, legs, Guid.NewGuid)).Code.ShouldBe(PlanningExecutionErrorCodes.TooManyStops);
    }
}
