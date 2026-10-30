using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// Design 61 "Order page review" point 5 with build notes 4 and 7 on SQLite: <c>split-at-address</c> turns A→B into A→X and
/// X→B (<c>LegSource.SplitAtAddress</c>, <c>splitFromLegId</c>, the synthetic stop X, the departure activity defaulting to the
/// arrival's successor, the date to the origin date, positions, quantities, the leg soft-deleted, routes requested), the
/// line counts as reshaped (an order save keeps the halves); <c>remove-split</c> on either half restores the leg with its own
/// id and source and regenerates a line that is all the generator's again; every refusal (LegOnTrip, LegNotSplitAtAddress,
/// SplitAtAddressNested, SplitAddressIsLegEnd, SplitDateOutsideLeg, OrderCancelled, an unknown address) leaves the legs as
/// they were; another tenant cannot reach the leg nor lend its address. Risk class: High (planning), Critical for the
/// tenant-isolation cases.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class LegSplitAtAddressTests : PlanningAppServiceTestBase
{
    private sealed record World((Guid RelationId, Guid AddressId) A, (Guid RelationId, Guid AddressId) B, (Guid RelationId, Guid AddressId) C, (Guid RelationId, Guid AddressId) X, (Guid RelationId, Guid AddressId) Y);

    private async Task<World> WorldAsync()
    {
        var a = await PlaceAsync("A" + TransportRun.TransportRunFixture.NewSuffix(), "Alpha", 52.0, 5.0);
        var b = await PlaceAsync("B" + TransportRun.TransportRunFixture.NewSuffix(), "Beta", 52.1, 5.1);
        var c = await PlaceAsync("C" + TransportRun.TransportRunFixture.NewSuffix(), "Gamma", 52.2, 5.2);
        var x = await PlaceAsync("X" + TransportRun.TransportRunFixture.NewSuffix(), "Xray hub", 52.05, 5.05);
        var y = await PlaceAsync("Y" + TransportRun.TransportRunFixture.NewSuffix(), "Yankee hub", 52.07, 5.07);
        return new World(a, b, c, x, y);
    }

    /// <summary>A confirmed order of one line A (load, Day) → B (unload, Day+1) → C (unload, Day+2): two generated legs.</summary>
    private async Task<(Guid OrderId, LegDto First, LegDto Second)> OrderAsync(World w, decimal quantity = 4)
    {
        var orderId = await ConfirmedOrderAsync(w.A.RelationId, LineOf(quantity, 400, StopAt(w.A, Load, Day), StopAt(w.B, Unload, Day.AddDays(1)), StopAt(w.C, Unload, Day.AddDays(2))));
        var legs = (await Legs.GetListAsync(LegList(orderId: orderId))).Rows.OrderBy(l => l.Position).ToList();
        legs.Count.ShouldBe(2, "one leg per consecutive stop pair");
        return (orderId, legs[0], legs[1]);
    }

    private static LegSplitAtAddressDto At((Guid RelationId, Guid AddressId) place, Guid arrivalType, Guid? departureType = null, DateOnly? date = null)
        => new() { RelationId = place.RelationId, RelationAddressId = place.AddressId, ArrivalWaypointTypeId = arrivalType, DepartureWaypointTypeId = departureType, Date = date };

    private async Task<string> RefusalAsync(Func<Task> action) => (await Should.ThrowAsync<BusinessException>(action)).Code!;

    private async Task<List<(Guid Id, int Position, LegSource Source)>> ShapeAsync(Guid orderId)
        => (await LegsOfOrderAsync(orderId)).OrderBy(l => l.Position).Select(l => (l.Id, l.Position, l.Source)).ToList();

    [Fact]
    public async Task Split_turns_A_B_into_A_X_and_X_B_at_a_synthetic_stop_with_the_successor_activity_and_the_origin_date()
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var (orderId, leg, next) = await OrderAsync(w);

        var halves = await Legs.SplitAtAddressAsync(leg.Id, At(w.X, DepotIn));

        halves.Count.ShouldBe(2);
        var (first, second) = (halves[0], halves[1]);
        var stopX = Leg.SyntheticStopId("split-at", leg.Id, w.X.AddressId);
        foreach (var half in halves)
        {
            half.Source.ShouldBe(LegSource.SplitAtAddress);
            half.SplitFromLegId.ShouldBe(leg.Id);
            half.OrderId.ShouldBe(orderId);
            half.OrderLineId.ShouldBe(leg.OrderLineId);
            half.Quantity.ShouldBe(leg.Quantity, "the quantities are copied");
            half.WeightKg.ShouldBe(leg.WeightKg);
            half.TripId.ShouldBeNull();
            half.IsViaLeg.ShouldBeFalse();
        }

        (first.Origin.StopId, first.Origin.RelationId, first.Destination.StopId, first.Destination.RelationAddressId).ShouldBe((leg.Origin.StopId, w.A.RelationId, stopX, w.X.AddressId));
        (second.Origin.StopId, second.Origin.RelationAddressId, second.Destination.StopId, second.Destination.RelationId).ShouldBe((stopX, w.X.AddressId, leg.Destination.StopId, w.B.RelationId));
        first.Destination.WaypointTypeId.ShouldBe(DepotIn);
        second.Origin.WaypointTypeId.ShouldBe(DepotOut, "the departure defaults to the arrival activity's successor (design 51)");
        first.Destination.PlannedDate.ShouldBe(leg.Origin.PlannedDate, "the date defaults to the leg's origin date");
        second.Origin.PlannedDate.ShouldBe(leg.Origin.PlannedDate);
        first.Destination.Name.ShouldBe("Xray hub");
        (first.Position, second.Position).ShouldBe((1, 2));

        (await ShapeAsync(orderId)).ShouldBe([(first.Id, 1, LegSource.SplitAtAddress), (second.Id, 2, LegSource.SplitAtAddress), (next.Id, 3, LegSource.Generated)], "the line's later leg moved one position up");
        await Should.ThrowAsync<EntityNotFoundException>(() => Legs.GetAsync(leg.Id));
        using (GetRequiredService<IDataFilter>().Disable<ISoftDelete>())
        {
            (await LegRepository.GetAsync(leg.Id)).IsDeleted.ShouldBeTrue("the split leg is soft-deleted, kept for remove-split");
        }

        Requests.For(PlanningExecutionApplicationModule.LegTargetType, first.Id).ShouldNotBeEmpty("the new legs' routes are requested");
        Requests.For(PlanningExecutionApplicationModule.LegTargetType, second.Id).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Split_takes_the_posted_departure_activity_and_date_and_without_a_successor_repeats_the_arrival_activity()
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var (_, leg, next) = await OrderAsync(w);

        var posted = await Legs.SplitAtAddressAsync(leg.Id, At(w.X, Unload, Load, Day.AddDays(1)));
        posted[0].Destination.WaypointTypeId.ShouldBe(Unload);
        posted[1].Origin.WaypointTypeId.ShouldBe(Load);
        posted[0].Destination.PlannedDate.ShouldBe(Day.AddDays(1), "a date within the leg (Day → Day+1)");

        var noSuccessor = await Legs.SplitAtAddressAsync(next.Id, At(w.Y, Unload));
        noSuccessor[1].Origin.WaypointTypeId.ShouldBe(Unload, "no successor: the arrival activity");
    }

    [Fact]
    public async Task The_split_line_counts_as_reshaped_an_order_save_keeps_the_halves()
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var (orderId, leg, next) = await OrderAsync(w);
        var halves = await Legs.SplitAtAddressAsync(leg.Id, At(w.X, DepotIn));

        var order = await Orders.GetAsync(orderId);
        var line = LinesOf(order).Single();
        line.Remarks = "Changed by the clerk";
        line.Quantity = 6;
        await Orders.UpdateLineAsync(orderId, line.Id!.Value, line);

        var after = await ShapeAsync(orderId);
        after.Select(l => l.Id).ShouldBe([halves[0].Id, halves[1].Id, next.Id], "regeneration keeps a reshaped line's legs");
        after.Take(2).ShouldAllBe(l => l.Source == LegSource.SplitAtAddress);
    }

    [Fact]
    public async Task Remove_split_on_either_half_restores_the_leg_with_its_own_id_source_ends_and_position_and_regenerates_the_line()
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var (orderId, leg, next) = await OrderAsync(w);
        var halves = await Legs.SplitAtAddressAsync(leg.Id, At(w.X, DepotIn));

        /* The clerk changes the quantity while the line is reshaped; the restored line is regenerated from the order. */
        var order = await Orders.GetAsync(orderId);
        var line = LinesOf(order).Single();
        line.Quantity = 7;
        await Orders.UpdateLineAsync(orderId, line.Id!.Value, line);
        var before = Requests.For(PlanningExecutionApplicationModule.LegTargetType, leg.Id).Count;

        var restored = await Legs.UndoSplitAtAddressAsync(halves[1].Id);

        restored.Id.ShouldBe(leg.Id, "the leg the split replaced comes back with its own id");
        restored.Source.ShouldBe(LegSource.Generated);
        restored.SplitFromLegId.ShouldBeNull();
        (restored.Origin.StopId, restored.Destination.StopId).ShouldBe((leg.Origin.StopId, leg.Destination.StopId));
        (restored.Origin.RelationId, restored.Destination.RelationId).ShouldBe((w.A.RelationId, w.B.RelationId));
        restored.Position.ShouldBe(1);
        restored.Quantity.ShouldBe(7, "all legs are the generator's again: the line is regenerated and follows the order");
        (await ShapeAsync(orderId)).ShouldBe([(leg.Id, 1, LegSource.Generated), (next.Id, 2, LegSource.Generated)]);
        await Should.ThrowAsync<EntityNotFoundException>(() => Legs.GetAsync(halves[0].Id));
        await Should.ThrowAsync<EntityNotFoundException>(() => Legs.GetAsync(halves[1].Id));
        Requests.For(PlanningExecutionApplicationModule.LegTargetType, leg.Id).Count.ShouldBeGreaterThan(before, "the restored leg's route is requested");

        /* Split again at the same place and remove through the first half. */
        var again = await Legs.SplitAtAddressAsync(leg.Id, At(w.X, DepotIn));
        (await Legs.UndoSplitAtAddressAsync(again[0].Id)).Id.ShouldBe(leg.Id);
        (await ShapeAsync(orderId)).Select(l => l.Id).ShouldBe([leg.Id, next.Id]);
    }

    [Fact]
    public async Task A_nested_split_must_be_removed_first_then_both_come_back_in_order()
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var (orderId, leg, next) = await OrderAsync(w);
        var outer = await Legs.SplitAtAddressAsync(leg.Id, At(w.X, DepotIn));
        var inner = await Legs.SplitAtAddressAsync(outer[1].Id, At(w.Y, DepotIn));
        (await ShapeAsync(orderId)).Select(l => l.Position).ShouldBe([1, 2, 3, 4]);

        (await RefusalAsync(() => Legs.UndoSplitAtAddressAsync(outer[0].Id))).ShouldBe(PlanningExecutionErrorCodes.SplitAtAddressNested);

        var x2b = await Legs.UndoSplitAtAddressAsync(inner[0].Id);
        x2b.Id.ShouldBe(outer[1].Id);
        x2b.Source.ShouldBe(LegSource.SplitAtAddress, "the inner split restores the outer half with its own source");
        (await ShapeAsync(orderId)).ShouldBe([(outer[0].Id, 1, LegSource.SplitAtAddress), (outer[1].Id, 2, LegSource.SplitAtAddress), (next.Id, 3, LegSource.Generated)]);

        (await Legs.UndoSplitAtAddressAsync(outer[0].Id)).Id.ShouldBe(leg.Id);
        (await ShapeAsync(orderId)).ShouldBe([(leg.Id, 1, LegSource.Generated), (next.Id, 2, LegSource.Generated)]);
    }

    [Fact]
    public async Task The_refusals_leave_the_legs_as_they_were()
    {
        using var _ = InTenantA();
        var type = await TripTypeAsync();
        var w = await WorldAsync();
        var (orderId, leg, next) = await OrderAsync(w);
        var shape = await ShapeAsync(orderId);

        (await RefusalAsync(() => Legs.SplitAtAddressAsync(leg.Id, At(w.A, DepotIn)))).ShouldBe(PlanningExecutionErrorCodes.SplitAddressIsLegEnd);
        (await RefusalAsync(() => Legs.SplitAtAddressAsync(leg.Id, At(w.B, DepotIn)))).ShouldBe(PlanningExecutionErrorCodes.SplitAddressIsLegEnd);
        (await RefusalAsync(() => Legs.SplitAtAddressAsync(leg.Id, At(w.X, DepotIn, date: Day.AddDays(-1))))).ShouldBe(PlanningExecutionErrorCodes.SplitDateOutsideLeg);
        var late = await Should.ThrowAsync<BusinessException>(() => Legs.SplitAtAddressAsync(leg.Id, At(w.X, DepotIn, date: Day.AddDays(2))));
        late.Code.ShouldBe(PlanningExecutionErrorCodes.SplitDateOutsideLeg);
        (late.Data["legId"], late.Data["from"], late.Data["till"]).ShouldBe((leg.Id, "2026-10-26", "2026-10-27"));
        (await RefusalAsync(() => Legs.SplitAtAddressAsync(leg.Id, At((w.X.RelationId, Guid.NewGuid()), DepotIn)))).ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await RefusalAsync(() => Legs.SplitAtAddressAsync(leg.Id, At((w.Y.RelationId, w.X.AddressId), DepotIn)))).ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound, "the address must be one of the relation's");
        (await RefusalAsync(() => Legs.SplitAtAddressAsync(leg.Id, At(w.X, Guid.NewGuid())))).ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await RefusalAsync(() => Legs.SplitAtAddressAsync(leg.Id, At(w.X, DepotIn, Guid.NewGuid())))).ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await RefusalAsync(() => Legs.UndoSplitAtAddressAsync(leg.Id))).ShouldBe(PlanningExecutionErrorCodes.LegNotSplitAtAddress);
        await Should.ThrowAsync<EntityNotFoundException>(() => Legs.SplitAtAddressAsync(Guid.NewGuid(), At(w.X, DepotIn)));
        (await ShapeAsync(orderId)).ShouldBe(shape);

        /* On a trip: unassign first. */
        var trip = await CreateTripAsync(type, At(6), At(18, 0, 28));
        await Trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = [next.Id] });
        var onTrip = await Should.ThrowAsync<BusinessException>(() => Legs.SplitAtAddressAsync(next.Id, At(w.X, DepotIn)));
        onTrip.Code.ShouldBe(PlanningExecutionErrorCodes.LegOnTrip);
        onTrip.Data["tripId"].ShouldBe(trip.Id);

        /* A half on a trip blocks the removal. */
        var halves = await Legs.SplitAtAddressAsync(leg.Id, At(w.X, DepotIn));
        await Trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = [halves[0].Id] });
        (await RefusalAsync(() => Legs.UndoSplitAtAddressAsync(halves[1].Id))).ShouldBe(PlanningExecutionErrorCodes.LegOnTrip);
        (await LegsOfOrderAsync(orderId)).Select(l => l.Id).ShouldBe([halves[0].Id, halves[1].Id, next.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task A_cancelled_orders_legs_are_neither_split_nor_restored()
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var (orderId, leg, next) = await OrderAsync(w);
        var halves = await Legs.SplitAtAddressAsync(leg.Id, At(w.X, DepotIn));

        await Run.CancelWithoutEventsAsync(orderId);

        (await RefusalAsync(() => Legs.SplitAtAddressAsync(next.Id, At(w.Y, DepotIn)))).ShouldBe(PlanningExecutionErrorCodes.OrderCancelled);
        (await RefusalAsync(() => Legs.UndoSplitAtAddressAsync(halves[0].Id))).ShouldBe(PlanningExecutionErrorCodes.OrderCancelled);
    }

    [Fact]
    public async Task Another_tenant_can_neither_split_nor_restore_the_leg_nor_lend_its_address()
    {
        Guid legId;
        Guid halfId;
        (Guid RelationId, Guid AddressId) tenantAPlace;
        using (InTenantA())
        {
            var w = await WorldAsync();
            var (_, leg, next) = await OrderAsync(w);
            legId = next.Id;
            halfId = (await Legs.SplitAtAddressAsync(leg.Id, At(w.X, DepotIn)))[0].Id;
            tenantAPlace = w.Y;
        }

        Guid tenantBLeg;
        using (InTenantB())
        {
            var w = await WorldAsync();
            await Should.ThrowAsync<EntityNotFoundException>(() => Legs.SplitAtAddressAsync(legId, At(w.X, DepotIn)));
            await Should.ThrowAsync<EntityNotFoundException>(() => Legs.UndoSplitAtAddressAsync(halfId));
            var (_, own, _) = await OrderAsync(w);
            tenantBLeg = own.Id;

            /* Tenant A's address is unknown in tenant B. */
            (await RefusalAsync(() => Legs.SplitAtAddressAsync(tenantBLeg, At(tenantAPlace, DepotIn)))).ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        }

        using (InTenantA())
        {
            (await Legs.GetAsync(legId)).Source.ShouldBe(LegSource.Generated, "tenant B's attempts changed nothing");
            (await Legs.GetAsync(halfId)).Source.ShouldBe(LegSource.SplitAtAddress);
            await Should.ThrowAsync<EntityNotFoundException>(() => Legs.GetAsync(tenantBLeg));
        }
    }
}
