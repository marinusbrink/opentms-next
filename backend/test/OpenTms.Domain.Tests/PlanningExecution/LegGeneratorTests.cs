using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.PlanningExecution.ViaDepots;
using Shouldly;
using Xunit;

namespace OpenTms.PlanningExecution;

/// <summary>
/// LegGenerator corner cases (design 59 "Generation"; orders.md §4 scenarios 18–28 as the design corrects them). Pure
/// domain logic: no database. Risk class: Critical (leg generation idempotency and via-depot splitting; the PostgreSQL
/// tests cover the same against the unique index).
/// </summary>
public class LegGeneratorTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly Guid Load = Guid.NewGuid();
    private static readonly Guid Unload = Guid.NewGuid();
    private static readonly Guid DepotIn = Guid.NewGuid();
    private static readonly Guid DepotOut = Guid.NewGuid();
    private static readonly Guid Unit = Guid.NewGuid();

    private readonly LegGenerator _generator = new();

    private static LegGenerationStop Stop(Guid stopId, Guid relationId, Guid waypointType, string name, DateOnly date, params Guid[] regions) => new()
    {
        Snapshot = new LegEndSnapshot(stopId, waypointType, relationId, Guid.NewGuid(), name, name + " city", "NL", 52.0, 5.0, "Europe/Amsterdam", date, null, null, null, 30),
        RegionIds = regions.ToList()
    };

    private static LegGenerationInput Order(Guid orderId, params LegGenerationLine[] lines) => new()
    {
        OrderId = orderId,
        CustomerRelationId = Customer,
        Lines = lines.ToList()
    };

    private static LegGenerationLine Line(Guid lineId, decimal quantity, params LegGenerationStop[] stops) => new()
    {
        LineId = lineId,
        Quantities = new LegQuantities(quantity, 1000, null, 2.4, 1, null, Unit, null, null, []),
        Stops = stops.ToList()
    };

    private static DepotLocation Depot(Guid relationId, Guid addressId) => new(relationId, addressId, "Depot", "Utrecht", "NL", 52.1, 5.1, "Europe/Amsterdam");

    private LegGenerationResult Generate(LegGenerationInput input, IReadOnlyList<Leg> existing, IReadOnlyList<ViaDepotRule>? rules = null, DepotLocation? depot = null, Func<Guid, Guid?>? successorOf = null)
        => _generator.Generate(input, rules ?? [], _ => depot, successorOf ?? (_ => null), existing, Tenant, Guid.NewGuid);

    [Fact]
    public void One_leg_per_consecutive_stop_pair_with_the_lines_quantities_and_positions()
    {
        var a = Stop(Guid.NewGuid(), Guid.NewGuid(), Load, "A", new DateOnly(2026, 10, 26));
        var b = Stop(Guid.NewGuid(), Guid.NewGuid(), Unload, "B", new DateOnly(2026, 10, 26));
        var c = Stop(Guid.NewGuid(), Guid.NewGuid(), Unload, "C", new DateOnly(2026, 10, 27));
        var order = Order(Guid.NewGuid(), Line(Guid.NewGuid(), 10, a, b, c));

        var result = Generate(order, []);

        result.Created.Count.ShouldBe(2);
        result.Created.Select(l => l.Position).ShouldBe([1, 2]);
        result.Created[0].OriginStopId.ShouldBe(a.Snapshot.StopId);
        result.Created[0].DestinationStopId.ShouldBe(b.Snapshot.StopId);
        result.Created[1].OriginStopId.ShouldBe(b.Snapshot.StopId);
        result.Created[1].DestinationStopId.ShouldBe(c.Snapshot.StopId);
        result.Created.ShouldAllBe(l => l.Quantity == 10 && l.WeightKg == 1000 && l.TenantId == Tenant && l.CustomerRelationId == Customer && l.Source == LegSource.Generated);
        result.Created[0].OriginName.ShouldBe("A");
        result.Created[0].DestinationPlannedDate.ShouldBe(new DateOnly(2026, 10, 26));
        result.Created[1].DestinationPlannedDate.ShouldBe(new DateOnly(2026, 10, 27));
    }

    [Fact]
    public void The_same_facts_twice_change_nothing_and_a_single_stop_line_has_no_legs()
    {
        var a = Stop(Guid.NewGuid(), Guid.NewGuid(), Load, "A", new DateOnly(2026, 10, 26));
        var b = Stop(Guid.NewGuid(), Guid.NewGuid(), Unload, "B", new DateOnly(2026, 10, 26));
        var order = Order(Guid.NewGuid(), Line(Guid.NewGuid(), 10, a, b), Line(Guid.NewGuid(), 1, a));

        var first = Generate(order, []);
        first.Created.Count.ShouldBe(1);

        var second = Generate(order, first.Created);
        second.Created.ShouldBeEmpty();
        second.Updated.ShouldBeEmpty();
        second.Deleted.ShouldBeEmpty();
        second.Unassigned.ShouldBeEmpty();
        second.Unchanged.ShouldHaveSingleItem().Id.ShouldBe(first.Created[0].Id);
    }

    [Fact]
    public void A_changed_stop_updates_an_unassigned_leg_in_place_and_takes_an_assigned_leg_off_its_trip()
    {
        var a = Stop(Guid.NewGuid(), Guid.NewGuid(), Load, "A", new DateOnly(2026, 10, 26));
        var b = Stop(Guid.NewGuid(), Guid.NewGuid(), Unload, "B", new DateOnly(2026, 10, 26));
        var c = Stop(Guid.NewGuid(), Guid.NewGuid(), Unload, "C", new DateOnly(2026, 10, 27));
        var lineId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var legs = Generate(Order(orderId, Line(lineId, 10, a, b, c)), []).Created;
        var tripId = Guid.NewGuid();
        legs[1].AssignTo(tripId, 1);

        var b2 = Stop(b.Snapshot.StopId, b.Snapshot.RelationId, Unload, "B (renamed)", new DateOnly(2026, 10, 28));
        var result = Generate(Order(orderId, Line(lineId, 10, a, b2, c)), legs);

        result.Created.ShouldBeEmpty();
        result.Deleted.ShouldBeEmpty();
        result.Updated.Count.ShouldBe(2, "both legs touch stop B");
        legs[0].DestinationName.ShouldBe("B (renamed)");
        legs[0].TripId.ShouldBeNull();
        legs[1].TripId.ShouldBeNull("the assigned leg left its trip");
        result.Unassigned.ShouldHaveSingleItem().ShouldBe((legs[1], tripId));
        legs[1].OriginPlannedDate.ShouldBe(new DateOnly(2026, 10, 28));
    }

    [Fact]
    public void A_quantity_change_keeps_an_assigned_leg_on_its_trip()
    {
        var a = Stop(Guid.NewGuid(), Guid.NewGuid(), Load, "A", new DateOnly(2026, 10, 26));
        var b = Stop(Guid.NewGuid(), Guid.NewGuid(), Unload, "B", new DateOnly(2026, 10, 26));
        var lineId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var legs = Generate(Order(orderId, Line(lineId, 10, a, b)), []).Created;
        legs[0].AssignTo(Guid.NewGuid(), 1);

        var result = Generate(Order(orderId, Line(lineId, 12, a, b)), legs);

        result.Updated.ShouldHaveSingleItem().Quantity.ShouldBe(12);
        result.Unassigned.ShouldBeEmpty();
        legs[0].TripId.ShouldNotBeNull();
    }

    [Fact]
    public void A_removed_stop_deletes_its_leg_after_unassigning_it_and_a_removed_line_deletes_all_its_legs()
    {
        var a = Stop(Guid.NewGuid(), Guid.NewGuid(), Load, "A", new DateOnly(2026, 10, 26));
        var b = Stop(Guid.NewGuid(), Guid.NewGuid(), Unload, "B", new DateOnly(2026, 10, 26));
        var c = Stop(Guid.NewGuid(), Guid.NewGuid(), Unload, "C", new DateOnly(2026, 10, 27));
        var line1 = Guid.NewGuid();
        var line2 = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var legs = Generate(Order(orderId, Line(line1, 10, a, b, c), Line(line2, 5, a, c)), []).Created;
        legs.Count.ShouldBe(3);
        var tripId = Guid.NewGuid();
        legs.Single(l => l.OrderLineId == line1 && l.Position == 2).AssignTo(tripId, 1);

        var result = Generate(Order(orderId, Line(line1, 10, a, b)), legs);

        result.Deleted.Count.ShouldBe(2, "B→C of line 1 and the whole line 2");
        result.Unassigned.ShouldHaveSingleItem().TripId.ShouldBe(tripId);
        result.Unchanged.ShouldHaveSingleItem().DestinationStopId.ShouldBe(b.Snapshot.StopId);
    }

    [Fact]
    public void A_cancelled_order_loses_every_leg()
    {
        var a = Stop(Guid.NewGuid(), Guid.NewGuid(), Load, "A", new DateOnly(2026, 10, 26));
        var b = Stop(Guid.NewGuid(), Guid.NewGuid(), Unload, "B", new DateOnly(2026, 10, 26));
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var legs = Generate(Order(orderId, Line(lineId, 10, a, b)), []).Created;
        legs[0].AssignTo(Guid.NewGuid(), 1);

        var cancelled = new LegGenerationInput { OrderId = orderId, CustomerRelationId = Customer, IsCancelled = true, Lines = [Line(lineId, 10, a, b)] };
        var result = Generate(cancelled, legs);

        result.Deleted.ShouldHaveSingleItem();
        result.Unassigned.ShouldHaveSingleItem();
        result.Created.ShouldBeEmpty();
    }

    [Fact]
    public void A_customer_via_depot_rule_splits_the_pair_at_the_depot_with_the_arrival_and_departure_types_and_the_overnight_date()
    {
        var depotRelation = Guid.NewGuid();
        var depotAddress = Guid.NewGuid();
        var rule = new ViaDepotRule(Guid.NewGuid(), Tenant, ViaDepotScope.Customer, Customer, null, depotRelation, depotAddress, DepotIn);
        rule.SetWaypointTypes(DepotIn, DepotOut);
        rule.SetOvernight(true);
        var a = Stop(Guid.NewGuid(), Guid.NewGuid(), Load, "A", new DateOnly(2026, 10, 26));
        var b = Stop(Guid.NewGuid(), Guid.NewGuid(), Unload, "B", new DateOnly(2026, 10, 28));
        var lineId = Guid.NewGuid();
        var order = Order(Guid.NewGuid(), Line(lineId, 10, a, b));

        var result = Generate(order, [], [rule], Depot(depotRelation, depotAddress));

        result.Created.Count.ShouldBe(2);
        var toDepot = result.Created[0];
        var fromDepot = result.Created[1];
        toDepot.Source.ShouldBe(LegSource.ViaDepot);
        toDepot.IsViaLeg.ShouldBeTrue();
        toDepot.ViaDepotRuleId.ShouldBe(rule.Id);
        toDepot.OriginStopId.ShouldBe(a.Snapshot.StopId);
        toDepot.DestinationRelationId.ShouldBe(depotRelation);
        toDepot.DestinationRelationAddressId.ShouldBe(depotAddress);
        toDepot.DestinationWaypointTypeId.ShouldBe(DepotIn);
        toDepot.DestinationPlannedDate.ShouldBe(new DateOnly(2026, 10, 26));
        toDepot.DestinationName.ShouldBe("Depot");
        fromDepot.OriginStopId.ShouldBe(toDepot.DestinationStopId, "one synthetic depot stop joins the two legs");
        fromDepot.OriginWaypointTypeId.ShouldBe(DepotOut);
        fromDepot.OriginPlannedDate.ShouldBe(new DateOnly(2026, 10, 27), "overnight: the onward leg departs the next day");
        fromDepot.DestinationStopId.ShouldBe(b.Snapshot.StopId);
        fromDepot.Position.ShouldBe(2);

        /* Regeneration finds the same via legs again (the synthetic stop id is stable). */
        var again = Generate(order, result.Created, [rule], Depot(depotRelation, depotAddress));
        again.Created.ShouldBeEmpty();
        again.Deleted.ShouldBeEmpty();
        again.Unchanged.Count.ShouldBe(2);
    }

    [Fact]
    public void A_rule_without_departure_type_departs_the_depot_with_the_arrival_types_successor_else_the_arrival_type()
    {
        var depotRelation = Guid.NewGuid();
        var depotAddress = Guid.NewGuid();
        var rule = new ViaDepotRule(Guid.NewGuid(), Tenant, ViaDepotScope.Customer, Customer, null, depotRelation, depotAddress, DepotIn);
        var a = Stop(Guid.NewGuid(), Guid.NewGuid(), Load, "A", new DateOnly(2026, 10, 26));
        var b = Stop(Guid.NewGuid(), Guid.NewGuid(), Unload, "B", new DateOnly(2026, 10, 26));
        var order = Order(Guid.NewGuid(), Line(Guid.NewGuid(), 10, a, b));

        /* Orders' waypoint type DEPOTIN names DEPOTOUT as its successor (design 59 "Generation"). */
        var withSuccessor = Generate(order, [], [rule], Depot(depotRelation, depotAddress), id => id == DepotIn ? DepotOut : null);
        withSuccessor.Created[0].DestinationWaypointTypeId.ShouldBe(DepotIn);
        withSuccessor.Created[1].OriginWaypointTypeId.ShouldBe(DepotOut);

        /* No successor known: the arrival type itself. */
        var withoutSuccessor = Generate(order, [], [rule], Depot(depotRelation, depotAddress), _ => null);
        withoutSuccessor.Created[1].OriginWaypointTypeId.ShouldBe(DepotIn);

        /* A rule with its own departure type is not touched by the successor. */
        rule.SetWaypointTypes(DepotIn, DepotOut);
        var explicitDeparture = Generate(order, [], [rule], Depot(depotRelation, depotAddress), _ => Guid.NewGuid());
        explicitDeparture.Created[1].OriginWaypointTypeId.ShouldBe(DepotOut);
    }

    [Fact]
    public void Rules_match_in_relation_then_customer_then_region_order_and_a_depot_equal_to_a_stop_does_not_split()
    {
        var region = Guid.NewGuid();
        var relationDepot = (Relation: Guid.NewGuid(), Address: Guid.NewGuid());
        var regionDepot = (Relation: Guid.NewGuid(), Address: Guid.NewGuid());
        var aRelation = Guid.NewGuid();
        var byRelation = new ViaDepotRule(Guid.NewGuid(), Tenant, ViaDepotScope.Relation, aRelation, null, relationDepot.Relation, relationDepot.Address, DepotIn);
        var byRegion = new ViaDepotRule(Guid.NewGuid(), Tenant, ViaDepotScope.Region, null, region, regionDepot.Relation, regionDepot.Address, DepotIn);
        var depots = new Dictionary<Guid, DepotLocation>
        {
            [relationDepot.Address] = Depot(relationDepot.Relation, relationDepot.Address),
            [regionDepot.Address] = Depot(regionDepot.Relation, regionDepot.Address)
        };

        var a = Stop(Guid.NewGuid(), aRelation, Load, "A", new DateOnly(2026, 10, 26), region);
        var b = Stop(Guid.NewGuid(), Guid.NewGuid(), Unload, "B", new DateOnly(2026, 10, 26));
        var relationWins = _generator.Generate(Order(Guid.NewGuid(), Line(Guid.NewGuid(), 1, a, b)), [byRegion, byRelation], id => depots.GetValueOrDefault(id), _ => null, [], Tenant, Guid.NewGuid);
        relationWins.Created.Count.ShouldBe(2);
        relationWins.Created[0].DestinationRelationId.ShouldBe(relationDepot.Relation);

        var regionOnly = _generator.Generate(Order(Guid.NewGuid(), Line(Guid.NewGuid(), 1, Stop(Guid.NewGuid(), Guid.NewGuid(), Load, "X", new DateOnly(2026, 10, 26), region), b)), [byRegion, byRelation], id => depots.GetValueOrDefault(id), _ => null, [], Tenant, Guid.NewGuid);
        regionOnly.Created[0].DestinationRelationId.ShouldBe(regionDepot.Relation);

        /* The stop at the depot itself: no split. */
        var atDepot = Stop(Guid.NewGuid(), regionDepot.Relation, Load, "Depot", new DateOnly(2026, 10, 26), region);
        atDepot = new LegGenerationStop { Snapshot = atDepot.Snapshot with { RelationAddressId = regionDepot.Address }, RegionIds = atDepot.RegionIds };
        var noSplit = _generator.Generate(Order(Guid.NewGuid(), Line(Guid.NewGuid(), 1, atDepot, b)), [byRegion], id => depots.GetValueOrDefault(id), _ => null, [], Tenant, Guid.NewGuid);
        noSplit.Created.ShouldHaveSingleItem().Source.ShouldBe(LegSource.Generated);

        /* An archived rule never applies; a rule whose depot address is unknown neither. */
        var inRegion = new LegGenerationStop { Snapshot = a.Snapshot with { RelationId = Guid.NewGuid() }, RegionIds = [region] };
        byRegion.SetArchived(true);
        _generator.Generate(Order(Guid.NewGuid(), Line(Guid.NewGuid(), 1, inRegion, b)), [byRegion], id => depots.GetValueOrDefault(id), _ => null, [], Tenant, Guid.NewGuid)
            .Created.ShouldHaveSingleItem();
        byRegion.SetArchived(false);
        _generator.Generate(Order(Guid.NewGuid(), Line(Guid.NewGuid(), 1, inRegion, b)), [byRegion], _ => null, _ => null, [], Tenant, Guid.NewGuid)
            .Created.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_line_the_planner_reshaped_keeps_its_legs_and_only_refreshes_the_stop_snapshots()
    {
        var a = Stop(Guid.NewGuid(), Guid.NewGuid(), Load, "A", new DateOnly(2026, 10, 26));
        var b = Stop(Guid.NewGuid(), Guid.NewGuid(), Unload, "B", new DateOnly(2026, 10, 26));
        var lineId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var legs = Generate(Order(orderId, Line(lineId, 10, a, b)), []).Created;
        var original = legs[0];
        var split = new Leg(Guid.NewGuid(), Tenant, orderId, lineId, Customer, 1, LegSource.Split,
            original.Origin with { StopId = Guid.NewGuid() }, original.Destination with { StopId = Guid.NewGuid() }, original.Quantities.Scale(0.4m));
        split.MarkSplitFrom(original.Id);
        original.SetQuantities(original.Quantities.Scale(0.6m));
        var tripId = Guid.NewGuid();
        split.AssignTo(tripId, 1);

        var b2 = Stop(b.Snapshot.StopId, b.Snapshot.RelationId, Unload, "B moved", new DateOnly(2026, 10, 27));
        var result = Generate(Order(orderId, Line(lineId, 10, a, b2)), [original, split]);

        result.Created.ShouldBeEmpty();
        result.Deleted.ShouldBeEmpty();
        original.DestinationName.ShouldBe("B moved");
        original.Quantity.ShouldBe(6, "the planner's split quantities are not overwritten");
        split.Quantity.ShouldBe(4);
        split.DestinationName.ShouldBe("B", "the split leg's synthetic stop is not an order stop");
        split.TripId.ShouldBe(tripId);
        result.Updated.ShouldHaveSingleItem().Id.ShouldBe(original.Id);
    }

    [Fact]
    public void A_return_leg_follows_a_changed_order_stop_but_keeps_its_swapped_activities_and_its_date_order()
    {
        var day = new DateOnly(2026, 10, 26);
        var a = Stop(Guid.NewGuid(), Guid.NewGuid(), Load, "A", day);
        var b = Stop(Guid.NewGuid(), Guid.NewGuid(), Unload, "B", day.AddDays(1));
        var lineId = Guid.NewGuid();
        var order = Order(Guid.NewGuid(), Line(lineId, 10, a, b));
        var original = Generate(order, []).Created.Single();
        var returnLeg = new LegAssignmentService(new TripStopComposer()).CreateReturnLeg(original, null, [], [original], Guid.NewGuid);

        /* trips.md 22: loaded where the original unloads (B), unloaded where it loaded (A), on or after B's date. */
        (returnLeg.OriginStopId, returnLeg.OriginWaypointTypeId, returnLeg.DestinationStopId, returnLeg.DestinationWaypointTypeId)
            .ShouldBe((b.Snapshot.StopId, Load, a.Snapshot.StopId, Unload));
        returnLeg.DestinationPlannedDate.ShouldBe(day.AddDays(1));
        var tripId = Guid.NewGuid();
        returnLeg.AssignTo(tripId, 2);

        /* Unchanged facts: the return leg stays as it is, on its trip. */
        var same = Generate(order, [original, returnLeg]);
        same.Unchanged.ShouldContain(returnLeg);
        same.Unassigned.ShouldBeEmpty();

        /* The order really moves B (the return leg's origin): the place and date follow, the activity stays "load",
         * and the destination date (A) is kept on or after the new origin date. */
        var moved = new LegGenerationStop { Snapshot = b.Snapshot with { Name = "B moved", PlannedDate = day.AddDays(3) }, RegionIds = b.RegionIds };
        var changedOrder = Order(order.OrderId, Line(lineId, 10, a, moved));
        var changed = Generate(changedOrder, [original, returnLeg]);
        changed.Updated.ShouldContain(returnLeg);
        changed.Unassigned.ShouldContain(u => u.Leg == returnLeg);
        returnLeg.OriginName.ShouldBe("B moved");
        returnLeg.OriginPlannedDate.ShouldBe(day.AddDays(3));
        returnLeg.OriginWaypointTypeId.ShouldBe(Load, "the return leg loads where the original unloads");
        returnLeg.DestinationWaypointTypeId.ShouldBe(Unload, "the return leg unloads where the original loaded");
        returnLeg.DestinationPlannedDate.ShouldBe(day.AddDays(3), "a return leg never arrives before it departs");

        /* The same moved facts again: nothing changes any more (the stored source key is the order stop's). */
        returnLeg.AssignTo(tripId, 2);
        var again = Generate(changedOrder, [original, returnLeg]);
        again.Unchanged.ShouldContain(returnLeg);
        again.Unassigned.ShouldBeEmpty();
        returnLeg.TripId.ShouldBe(tripId);
    }

    [Fact]
    public void Synthetic_stop_ids_are_deterministic_and_distinct_per_kind()
    {
        var x = Guid.NewGuid();
        var y = Guid.NewGuid();
        Leg.SyntheticStopId("via", x, y).ShouldBe(Leg.SyntheticStopId("via", x, y));
        Leg.SyntheticStopId("via", x, y).ShouldNotBe(Leg.SyntheticStopId("via", y, x));
        Leg.SyntheticStopId("via", x, y).ShouldNotBe(Leg.SyntheticStopId("hub", x, y));
    }

    [Fact]
    public void A_leg_window_check_uses_both_planned_dates_against_departure_and_arrival_plus_one()
    {
        var a = Stop(Guid.NewGuid(), Guid.NewGuid(), Load, "A", new DateOnly(2026, 10, 26));
        var b = Stop(Guid.NewGuid(), Guid.NewGuid(), Unload, "B", new DateOnly(2026, 10, 28));
        var leg = Generate(Order(Guid.NewGuid(), Line(Guid.NewGuid(), 10, a, b)), []).Created[0];

        leg.IsWithinWindow(new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 27)).ShouldBeTrue("arrival + 1 day is still inside");
        leg.IsWithinWindow(new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 28)).ShouldBeFalse("the origin date lies before the departure");
        leg.IsWithinWindow(new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 25)).ShouldBeFalse();
    }

    [Fact]
    public void The_editable_fields_refuse_a_planned_date_outside_the_stops_window()
    {
        var from = new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.FromHours(2));
        var till = new DateTimeOffset(2026, 10, 27, 17, 0, 0, TimeSpan.FromHours(2));
        var a = new LegGenerationStop { Snapshot = new LegEndSnapshot(Guid.NewGuid(), Load, Guid.NewGuid(), null, "A", null, "NL", 52, 5, "Europe/Amsterdam", new DateOnly(2026, 10, 26), from, till, null, null) };
        var b = Stop(Guid.NewGuid(), Guid.NewGuid(), Unload, "B", new DateOnly(2026, 10, 28));
        var leg = Generate(Order(Guid.NewGuid(), Line(Guid.NewGuid(), 10, a, b)), []).Created[0];

        leg.SetEditable("ref-1", null, "note", new DateOnly(2026, 10, 27), null);
        leg.OriginPlannedDate.ShouldBe(new DateOnly(2026, 10, 27));
        leg.OriginReference.ShouldBe("ref-1");
        leg.Remarks.ShouldBe("note");

        Should.Throw<Volo.Abp.BusinessException>(() => leg.SetEditable(null, null, null, new DateOnly(2026, 10, 28), null))
            .Code.ShouldBe(PlanningExecutionErrorCodes.LegDateOutsideStopWindow);
    }

    [Fact]
    public void The_planners_edits_are_no_order_change_the_leg_keeps_them_and_its_trip_but_a_changed_stop_still_unassigns()
    {
        var day = new DateOnly(2026, 10, 26);
        var a = Stop(Guid.NewGuid(), Guid.NewGuid(), Load, "A", day);
        var b = Stop(Guid.NewGuid(), Guid.NewGuid(), Unload, "B", day);
        var lineId = Guid.NewGuid();
        var order = Order(Guid.NewGuid(), Line(lineId, 10, a, b));
        var leg = Generate(order, []).Created.Single();
        var tripId = Guid.NewGuid();
        leg.AssignTo(tripId, 1);

        var from = new DateTimeOffset(2026, 10, 26, 7, 0, 0, TimeSpan.FromHours(1));
        var till = new DateTimeOffset(2026, 10, 26, 9, 30, 0, TimeSpan.FromHours(1));
        leg.SetEditable("PLANNER-REF", "DEST-REF", "note", null, null, new LegWindow(from, till), new LegWindow(null, till));

        /* Regenerated from the same order facts (e.g. after a quantity-only save): unchanged, the edits and the trip stay. */
        var again = Generate(order, [leg]);
        again.Unchanged.ShouldContain(leg);
        again.Unassigned.ShouldBeEmpty();
        leg.TripId.ShouldBe(tripId);
        (leg.OriginReference, leg.DestinationReference, leg.OriginWindowFrom, leg.OriginWindowTill, leg.DestinationWindowTill).ShouldBe(("PLANNER-REF", "DEST-REF", from, till, till));

        /* A quantity change updates the leg in place and keeps the planner's ends. */
        var more = Order(order.OrderId, Line(lineId, 12, a, b));
        var updated = Generate(more, [leg]);
        updated.Updated.ShouldContain(leg);
        updated.Unassigned.ShouldBeEmpty();
        leg.Quantity.ShouldBe(12);
        leg.OriginReference.ShouldBe("PLANNER-REF");

        /* The order's destination stop really changes: only that end follows the order, and the leg leaves its trip. */
        var moved = new LegGenerationStop { Snapshot = b.Snapshot with { Reference = "ORDER-REF", PlannedDate = day.AddDays(1) }, RegionIds = b.RegionIds };
        var changed = Generate(Order(order.OrderId, Line(lineId, 12, a, moved)), [leg]);
        changed.Unassigned.ShouldHaveSingleItem().Leg.ShouldBe(leg);
        leg.TripId.ShouldBeNull();
        leg.DestinationReference.ShouldBe("ORDER-REF");
        leg.DestinationPlannedDate.ShouldBe(day.AddDays(1));
        leg.OriginReference.ShouldBe("PLANNER-REF", "the origin did not change at the order: the planner's edit stays");
    }

    [Fact]
    public void A_leg_without_a_stored_source_key_compares_its_current_values_as_before()
    {
        var a = Stop(Guid.NewGuid(), Guid.NewGuid(), Load, "A", new DateOnly(2026, 10, 26));
        var b = Stop(Guid.NewGuid(), Guid.NewGuid(), Unload, "B", new DateOnly(2026, 10, 26));
        var leg = Generate(Order(Guid.NewGuid(), Line(Guid.NewGuid(), 10, a, b)), []).Created.Single();

        leg.OriginSourceKey.ShouldBe(Leg.SourceKey(a.Snapshot));
        leg.DestinationSourceKey.ShouldBe(Leg.SourceKey(b.Snapshot));
        Leg.SourceKey(a.Snapshot with { Reference = "X" }).ShouldNotBe(Leg.SourceKey(a.Snapshot));
        Leg.SourceKey(a.Snapshot with { }).ShouldBe(Leg.SourceKey(a.Snapshot), "deterministic");

        leg.InheritSourceKeys(null, null);
        leg.OriginDiffersFrom(a.Snapshot).ShouldBeFalse();
        leg.OriginDiffersFrom(b.Snapshot).ShouldBeTrue();
    }

    [Fact]
    public void A_planned_window_must_not_end_before_it_starts_and_the_planned_date_must_lie_within_the_new_window()
    {
        var a = Stop(Guid.NewGuid(), Guid.NewGuid(), Load, "A", new DateOnly(2026, 10, 26));
        var b = Stop(Guid.NewGuid(), Guid.NewGuid(), Unload, "B", new DateOnly(2026, 10, 26));
        var leg = Generate(Order(Guid.NewGuid(), Line(Guid.NewGuid(), 10, a, b)), []).Created.Single();
        var zone = TimeSpan.FromHours(1);

        Should.Throw<Volo.Abp.BusinessException>(() => leg.SetEditable(null, null, null, null, null,
                new LegWindow(new DateTimeOffset(2026, 10, 26, 10, 0, 0, zone), new DateTimeOffset(2026, 10, 26, 8, 0, 0, zone))))
            .Code.ShouldBe(PlanningExecutionErrorCodes.InvalidLegWindow);
        Should.Throw<Volo.Abp.BusinessException>(() => leg.SetEditable(null, null, null, null, null,
                new LegWindow(new DateTimeOffset(2026, 10, 28, 8, 0, 0, zone), new DateTimeOffset(2026, 10, 28, 10, 0, 0, zone))))
            .Code.ShouldBe(PlanningExecutionErrorCodes.LegDateOutsideStopWindow, "the planned date (the 26th) lies before the new window");
        leg.OriginWindowFrom.ShouldBeNull("a refused edit changes nothing");

        leg.SetEditable(null, null, null, new DateOnly(2026, 10, 28), null,
            new LegWindow(new DateTimeOffset(2026, 10, 28, 8, 0, 0, zone), new DateTimeOffset(2026, 10, 28, 10, 0, 0, zone)));
        leg.OriginPlannedDate.ShouldBe(new DateOnly(2026, 10, 28));
        leg.OriginWindowFrom.ShouldBe(new DateTimeOffset(2026, 10, 28, 8, 0, 0, zone));

        /* Both ends null clear the window (the whole day); a missing window leaves it alone. */
        leg.SetEditable(null, null, "remark only", null, null);
        leg.OriginWindowFrom.ShouldNotBeNull();
        leg.SetEditable(null, null, null, null, null, new LegWindow(null, null));
        (leg.OriginWindowFrom, leg.OriginWindowTill).ShouldBe((null, null));
    }
}
