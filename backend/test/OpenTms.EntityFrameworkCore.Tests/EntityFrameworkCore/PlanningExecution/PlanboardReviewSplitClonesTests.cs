using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.PlanningExecution.ViaDepots;
using Shouldly;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// Design 74 §7, what the clones of a split of an order line carry over from their originals: carried out separately (before the
/// assignment, so the composer sees it) and, on a generator-owned line, the planner's references, remarks, dates and windows when the
/// places match. Risk class: High (the order chain, planning); the quantities are Critical (revenue and cost follow them), proven on
/// PostgreSQL in <see cref="OpenTms.PostgreSql.PlanningExecution.PlanboardReviewPostgreSqlTests"/>.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class PlanboardReviewSplitClonesTests : PlanboardReviewTestBase
{
    private static DateOnly NextDay => Day.AddDays(1);

    /// <summary>A → B → C, the first leg with every field a planner can edit changed through <c>PUT leg/{id}</c>.</summary>
    private async Task<(FleetSet F, Guid OrderId, LegDto Edited, LegDto Other)> EditedLineAsync(string code)
    {
        var f = await FleetAsync();
        var c = await PlaceAsync(code, "Cee", 52.5, 5.5);
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, LineOf(10, 1000, StopAt(f.CustomerA, Load, Day), StopAt(f.CustomerB, Unload, Day), StopAt(c, Unload, Day)));
        var legs = await LegRowsAsync(orderId);
        var edited = await Legs.UpdateAsync(legs[0].Id, new LegUpdateDto
        {
            OriginReference = "OREF", DestinationReference = "DREF", Remarks = "ring first", OriginPlannedDate = NextDay, DestinationPlannedDate = NextDay,
            OriginWindow = new LegTimeWindowDto { From = At(8, 0, 27).AddSeconds(30), Till = At(10, 0, 27) },
            DestinationWindow = new LegTimeWindowDto { From = At(12, 0, 27), Till = At(14, 0, 27) },
            ConcurrencyStamp = legs[0].ConcurrencyStamp
        });
        return (f, orderId, edited, legs[1]);
    }

    private static void ShouldCarryTheEdits(LegDto clone, LegDto original)
    {
        clone.Remarks.ShouldBe(original.Remarks);
        clone.Origin.Reference.ShouldBe(original.Origin.Reference);
        clone.Origin.PlannedDate.ShouldBe(original.Origin.PlannedDate);
        clone.Origin.WindowFrom.ShouldBe(original.Origin.WindowFrom);
        clone.Origin.WindowTill.ShouldBe(original.Origin.WindowTill);
        clone.Destination.Reference.ShouldBe(original.Destination.Reference);
        clone.Destination.PlannedDate.ShouldBe(original.Destination.PlannedDate);
        clone.Destination.WindowFrom.ShouldBe(original.Destination.WindowFrom);
        clone.Destination.WindowTill.ShouldBe(original.Destination.WindowTill);
    }

    /* Carried out separately. */

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_clone_of_a_leg_carried_out_separately_gets_its_own_stop_pair_on_the_trip(bool reshaped)
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var c = await PlaceAsync("SEP" + (reshaped ? "R" : "G"), "Cee", 52.5, 5.5);
        var x = await PlaceAsync("SEX" + (reshaped ? "R" : "G"), "Hub", 52.3, 5.3);
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, LineOf(10, 1000, StopAt(f.CustomerA, Load, Day), StopAt(f.CustomerB, Unload, Day), StopAt(c, Unload, Day)));
        var legs = await LegRowsAsync(orderId);
        if (reshaped)
        {
            await Legs.SplitAtAddressAsync(legs[1].Id, new LegSplitAtAddressDto { RelationId = x.RelationId, RelationAddressId = x.AddressId, ArrivalWaypointTypeId = DepotIn, DepartureWaypointTypeId = DepotOut });
            legs = await LegRowsAsync(orderId);
        }

        /* A leg of another order shares the stops: a clone without the flag would merge into them. */
        var sharing = (await LegRowsAsync(await ConfirmedOrderAsync(f.CustomerA.RelationId, LineOf(1, 10, StopAt(f.CustomerA, Load, Day), StopAt(f.CustomerB, Unload, Day))))).Single();
        var first = legs.Single(l => l.Position == 1);
        var trip = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), first.Id, sharing.Id);
        trip.Stops.Count.ShouldBe(2, "the control: the two legs share their stops");
        trip = await Trips.CarryOutSeparatelyAsync(trip.Id, new LegReferenceDto { LegId = first.Id });
        trip.Stops.Count.ShouldBe(4);

        var result = await Legs.SplitOrderLineAsync(legs.Single(l => l.Position == legs.Max(p => p.Position)).Id, new LegSplitOrderLineDto { Quantity = 3 });

        var clone = result.NewLegs.Single(l => l.Position == 1);
        clone.TripId.ShouldBe(trip.Id);
        clone.IsCarriedOutSeparately.ShouldBeTrue();
        var stops = (await Trips.GetAsync(trip.Id)).Stops;
        stops.Count.ShouldBe(6, "the shared pair, the original's own pair and the clone's own pair");
        var own = stops.Where(s => s.Legs.Any(l => l.LegId == clone.Id)).ToList();
        own.Count.ShouldBe(2);
        own.ShouldAllBe(s => s.Legs.Count == 1 && s.Legs[0].IsCarriedOutSeparately);
        stops.Where(s => s.Legs.Any(l => l.LegId == sharing.Id)).ShouldAllBe(s => s.Legs.Count == 1, "the other order's stops stay its own");

        var before = (await LegRowsAsync(orderId)).Select(Fingerprint).ToList();
        await RedeliverLinesChangedAsync(orderId);
        (await LegRowsAsync(orderId)).Select(Fingerprint).ShouldBe(before);
        (await Trips.GetAsync(trip.Id)).Stops.Count.ShouldBe(6);
    }

    [Fact]
    public async Task A_clone_of_a_leg_not_carried_out_separately_still_merges_into_shared_stops()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var c = await PlaceAsync("MRG", "Cee", 52.5, 5.5);
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, LineOf(10, 1000, StopAt(f.CustomerA, Load, Day), StopAt(f.CustomerB, Unload, Day), StopAt(c, Unload, Day)));
        var legs = await LegRowsAsync(orderId);
        var trip = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), legs[0].Id);

        var result = await Legs.SplitOrderLineAsync(legs[1].Id, new LegSplitOrderLineDto { Quantity = 3 });

        var clone = result.NewLegs.Single(l => l.Position == 1);
        clone.TripId.ShouldBe(trip.Id);
        clone.IsCarriedOutSeparately.ShouldBeFalse();
        (await Trips.GetAsync(trip.Id)).Stops.Count.ShouldBe(2);
    }

    /// <summary>
    /// <see cref="Leg.Unassign"/> clears the flag: carried out separately is a fact about a leg on a trip. The chosen leg's clone stays
    /// unplanned, so it has no stops to be separate from, and a later assignment must merge by the trip's rule.
    /// </summary>
    [Fact]
    public async Task The_chosen_legs_clone_stays_unplanned_and_is_not_carried_out_separately()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.CustomerA, f.CustomerB));
        var leg = (await LegRowsAsync(orderId)).Single();
        var trip = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), leg.Id);
        await Trips.CarryOutSeparatelyAsync(trip.Id, new LegReferenceDto { LegId = leg.Id });

        var result = await Legs.SplitOrderLineAsync(leg.Id, new LegSplitOrderLineDto { Quantity = 4 });

        var clone = result.NewLegs.Single();
        clone.TripId.ShouldBeNull();
        clone.IsCarriedOutSeparately.ShouldBeFalse("an unplanned leg is not carried out separately, as after Leg.Unassign");
    }

    /* The planner's edits on a generator-owned line. */

    [Fact]
    public async Task A_generator_owned_clone_keeps_the_originals_edits_and_a_later_delivery_leaves_it_on_its_trip()
    {
        using var _ = InTenantA();
        var (f, orderId, edited, other) = await EditedLineAsync("EDT");
        var trip = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18, 0, 28)), edited.Id);

        var result = await Legs.SplitOrderLineAsync(other.Id, new LegSplitOrderLineDto { Quantity = 3 });

        var clone = result.NewLegs.Single(l => l.Position == 1);
        ShouldCarryTheEdits(clone, edited);
        clone.Source.ShouldBe(LegSource.Generated, "the line stays the generator's");
        clone.TripId.ShouldBe(trip.Id);
        var untouched = result.NewLegs.Single(l => l.Position == 2);
        untouched.Remarks.ShouldBeNull("the other clone's original has no edits");
        result.RemainingLegs.Single(l => l.Id == edited.Id).Remarks.ShouldBe("ring first");

        var before = (await LegRowsAsync(orderId)).Select(Fingerprint).ToList();
        var requests = TripRequests(trip.Id);

        await RedeliverLinesChangedAsync(orderId);
        await RedeliverLinesChangedAsync(orderId);

        var after = await LegRowsAsync(orderId);
        after.Select(Fingerprint).ShouldBe(before, "the later delivery changes nothing");
        TripRequests(trip.Id).ShouldBe(requests);
        var stored = after.Single(l => l.Id == clone.Id);
        stored.TripId.ShouldBe(trip.Id);
        ShouldCarryTheEdits(stored, edited);
        (await Trips.GetAsync(trip.Id)).Warnings.ShouldBeEmpty("no leg left the trip");

        /* Quantities: every leg of a line carries the line's, and both lines add up to the total. */
        after.Where(l => l.OrderLineId == clone.OrderLineId).ShouldAllBe(l => l.Quantity == 3 && l.WeightKg == 300);
        after.Where(l => l.OrderLineId == edited.OrderLineId).ShouldAllBe(l => l.Quantity == 7 && l.WeightKg == 700);
        (await Orders.GetAsync(orderId)).Lines.Sum(l => l.Quantity).ShouldBe(10);
    }

    [Fact]
    public async Task The_chosen_legs_clone_takes_the_edits_too_and_the_clone_goes_on_following_the_lines_quantity()
    {
        using var _ = InTenantA();
        var (f, orderId, edited, other) = await EditedLineAsync("CHO");
        var trip = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18, 0, 28)), other.Id);

        var result = await Legs.SplitOrderLineAsync(edited.Id, new LegSplitOrderLineDto { Quantity = 4 });

        var chosen = result.NewLegs.Single(l => l.Position == 1);
        chosen.TripId.ShouldBeNull("the chosen leg's clone stays unplanned");
        ShouldCarryTheEdits(chosen, edited);
        var onTrip = result.NewLegs.Single(l => l.Position == 2);
        onTrip.TripId.ShouldBe(trip.Id);

        var line = LinesOf(await Orders.GetAsync(orderId)).Single(l => l.Id == result.NewLineId);
        line.Quantity = 5;
        await Orders.UpdateLineAsync(orderId, result.NewLineId, line);

        var after = await LegRowsAsync(orderId);
        after.Where(l => l.OrderLineId == result.NewLineId).ShouldAllBe(l => l.Quantity == 5);
        after.Single(l => l.Id == onTrip.Id).TripId.ShouldBe(trip.Id, "a quantity is no change of the stop");
        ShouldCarryTheEdits(after.Single(l => l.Id == chosen.Id), edited);
        after.Where(l => l.OrderLineId == edited.OrderLineId).ShouldAllBe(l => l.Quantity == 6, "the old line is left alone");
    }

    [Fact]
    public async Task A_clone_whose_places_differ_from_its_original_does_not_take_the_edits()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.CustomerA, f.CustomerB));
        var leg = (await LegRowsAsync(orderId)).Single();
        await Legs.UpdateAsync(leg.Id, new LegUpdateDto { Remarks = "ring first", ConcurrencyStamp = leg.ConcurrencyStamp });

        /* A via-depot rule arrives after the leg was generated: the new line's route is another one. */
        await GetRequiredService<IViaDepotRuleAppService>().CreateAsync(new ViaDepotRuleCreateDto
        {
            Scope = ViaDepotScope.Customer, RelationId = f.CustomerA.RelationId, StopWaypointTypeId = Load,
            DepotRelationId = f.Depot.RelationId, DepotAddressId = f.Depot.AddressId, ArrivalWaypointTypeId = DepotIn
        });

        var result = await Legs.SplitOrderLineAsync(leg.Id, new LegSplitOrderLineDto { Quantity = 4 });

        result.NewLegs.Count.ShouldBe(2);
        result.NewLegs.ShouldAllBe(l => l.Remarks == null && l.Quantity == 4);
        (await Orders.GetAsync(orderId)).Lines.Sum(l => l.Quantity).ShouldBe(10);
    }
}
