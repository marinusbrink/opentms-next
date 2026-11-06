using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Events;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.Planboard;
using OpenTms.PlanningExecution.TransportTrips;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// Design 74 §8 on SQLite: dragging an order onto a trip: the preview, the move, the stuck legs, the refusals and <c>create-trip</c>
/// with an order; the per-trip <c>LegUnassigned</c> facts of <c>TripOperations.AssignAsync</c>. Risk class: High (planning).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class PlanboardReviewAssignOrderTests : PlanboardReviewTestBase
{
    private sealed record Scene(FleetSet Fleet, Guid OrderId, List<LegDto> Legs, TransportTripDto T1, TransportTripDto T2, TransportTripDto Target);

    /// <summary>An order of three legs: the first on T1, the second on T2, the third unplanned; an empty target trip.</summary>
    private async Task<Scene> SceneAsync()
    {
        var f = await FleetAsync();
        var c = await PlaceAsync("C", "Cee", 52.5, 5.5);
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId,
            LineOf(10, 1000, StopAt(f.CustomerA, Load, Day), StopAt(f.CustomerB, Unload, Day), StopAt(c, Unload, Day)),
            LineOf(4, 100, StopAt(f.CustomerA, Load, Day), StopAt(c, Unload, Day)));
        var legs = await LegRowsAsync(orderId);
        legs.Count.ShouldBe(3);
        var t1 = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), legs[0].Id);
        var t2 = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), legs[1].Id);
        var target = await TripWithAsync(f.Type, At(6), At(18));
        return new Scene(f, orderId, legs, t1, t2, target);
    }

    private async Task<PlanboardAssignOrderResultDto> AssignAsync(Guid tripId, Guid orderId, bool allowOutsideWindow = true)
        => await Planboard.AssignOrderAsync(tripId, new PlanboardAssignOrderDto { OrderId = orderId, ConcurrencyStamp = (await Trips.GetAsync(tripId)).ConcurrencyStamp, AllowOutsideWindow = allowOutsideWindow });

    [Fact]
    public async Task The_preview_lists_the_legs_the_trips_they_leave_and_moves_nothing()
    {
        using var _ = InTenantA();
        var s = await SceneAsync();
        var before = (await LegRowsAsync(s.OrderId)).Select(Fingerprint).ToList();

        var preview = await Planboard.GetAssignOrderPreviewAsync(s.Target.Id, new PlanboardAssignOrderPreviewRequestDto { OrderId = s.OrderId });

        preview.Legs.Select(l => l.Id).ShouldBe(s.Legs.Select(l => l.Id), ignoreOrder: true);
        preview.MovableCount.ShouldBe(3);
        preview.Stuck.ShouldBeEmpty();
        preview.PulledFrom.Select(p => (p.TripId, p.Number, p.LegCount)).ShouldBe([(s.T1.Id, s.T1.Number, 1), (s.T2.Id, s.T2.Number, 1)], ignoreOrder: true);
        var named = preview.Legs.Single(l => l.Id == s.Legs[0].Id);
        named.OriginName.ShouldNotBeNullOrEmpty();
        named.DestinationName.ShouldNotBeNullOrEmpty();
        named.TripNumber.ShouldBe(s.T1.Number);
        (await LegRowsAsync(s.OrderId)).Select(Fingerprint).ShouldBe(before);
    }

    [Fact]
    public async Task The_preview_of_an_order_of_only_unplanned_legs_needs_no_confirmation()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.CustomerA, f.CustomerB));
        var target = await TripWithAsync(f.Type, At(6), At(18));
        var preview = await Planboard.GetAssignOrderPreviewAsync(target.Id, new PlanboardAssignOrderPreviewRequestDto { OrderId = orderId });
        preview.PulledFrom.ShouldBeEmpty();
        preview.MovableCount.ShouldBe(1);
    }

    [Fact]
    public async Task The_preview_of_an_unknown_trip_is_not_found()
    {
        using var _ = InTenantA();
        var s = await SceneAsync();
        await Should.ThrowAsync<EntityNotFoundException>(() => Planboard.GetAssignOrderPreviewAsync(Guid.NewGuid(), new PlanboardAssignOrderPreviewRequestDto { OrderId = s.OrderId }));
    }

    [Fact]
    public async Task Movable_legs_move_and_the_old_trips_are_recomposed_and_recalculated()
    {
        using var _ = InTenantA();
        var s = await SceneAsync();
        var (requests1, requests2) = (TripRequests(s.T1.Id), TripRequests(s.T2.Id));

        var result = await AssignAsync(s.Target.Id, s.OrderId);

        result.Moved.Select(l => l.Id).ShouldBe(s.Legs.Select(l => l.Id), ignoreOrder: true);
        result.Stuck.ShouldBeEmpty();
        result.PulledFrom.Select(p => p.TripId).ShouldBe([s.T1.Id, s.T2.Id], ignoreOrder: true);
        result.Id.ShouldBe(s.Target.Id);
        result.ConcurrencyStamp.ShouldNotBe(s.Target.ConcurrencyStamp);
        (await LegRowsAsync(s.OrderId)).ShouldAllBe(l => l.TripId == s.Target.Id);
        (await Trips.GetAsync(s.T1.Id)).Stops.ShouldBeEmpty();
        (await Trips.GetAsync(s.T2.Id)).Stops.ShouldBeEmpty();
        (await Trips.GetAsync(s.Target.Id)).Stops.SelectMany(t => t.Legs).Select(l => l.LegId).Distinct().Count().ShouldBe(3);
        TripRequests(s.T1.Id).ShouldBeGreaterThan(requests1);
        TripRequests(s.T2.Id).ShouldBeGreaterThan(requests2);
    }

    [Fact]
    public async Task Each_old_trip_hears_LegUnassigned_only_for_its_own_legs()
    {
        using var _ = InTenantA();
        var s = await SceneAsync();
        Events.Clear();

        await AssignAsync(s.Target.Id, s.OrderId);

        var facts = Events.Of<LegUnassignedEto>();
        facts.Where(e => e.TripId == s.T1.Id).Select(e => e.LegId).ShouldBe([s.Legs[0].Id]);
        facts.Where(e => e.TripId == s.T2.Id).Select(e => e.LegId).ShouldBe([s.Legs[1].Id]);
        facts.ShouldNotContain(e => e.LegId == s.Legs[2].Id, "the third leg was on no trip");
        facts.ShouldNotContain(e => e.TripId == s.Target.Id);
    }

    [Fact]
    public async Task Stuck_legs_stay_and_are_reported_with_either_reason()
    {
        using var _ = InTenantA();
        var s = await SceneAsync();
        await Trips.RecordStopActualsAsync(s.T1.Id, (await Trips.GetAsync(s.T1.Id)).Stops[0].Id, new StopActualsDto { ActualArrival = At(7) });
        await Trips.StartAsync(s.T2.Id, new TripTimestampDto { At = At(6) });
        await Trips.CompleteAsync(s.T2.Id, new TripTimestampDto { At = At(17) });

        var preview = await Planboard.GetAssignOrderPreviewAsync(s.Target.Id, new PlanboardAssignOrderPreviewRequestDto { OrderId = s.OrderId });
        preview.MovableCount.ShouldBe(1);
        preview.PulledFrom.ShouldBeEmpty("a stuck leg leaves no trip, so no confirmation is due");
        preview.Stuck.Single(l => l.Id == s.Legs[0].Id).StuckReason.ShouldBe(PlanboardStuckReason.HasActuals);
        preview.Stuck.Single(l => l.Id == s.Legs[1].Id).StuckReason.ShouldBe(PlanboardStuckReason.TripFinal);

        var result = await AssignAsync(s.Target.Id, s.OrderId);

        result.Moved.ShouldHaveSingleItem().Id.ShouldBe(s.Legs[2].Id);
        result.Stuck.Select(l => l.Id).ShouldBe([s.Legs[0].Id, s.Legs[1].Id], ignoreOrder: true);
        var legs = await LegRowsAsync(s.OrderId);
        legs.Single(l => l.Id == s.Legs[0].Id).TripId.ShouldBe(s.T1.Id);
        legs.Single(l => l.Id == s.Legs[1].Id).TripId.ShouldBe(s.T2.Id);
        legs.Single(l => l.Id == s.Legs[2].Id).TripId.ShouldBe(s.Target.Id);
    }

    [Fact]
    public async Task A_leg_already_on_this_trip_is_not_moved()
    {
        using var _ = InTenantA();
        var s = await SceneAsync();
        var preview = await Planboard.GetAssignOrderPreviewAsync(s.T1.Id, new PlanboardAssignOrderPreviewRequestDto { OrderId = s.OrderId });
        preview.Legs.Count.ShouldBe(3);
        preview.MovableCount.ShouldBe(2);
        preview.PulledFrom.ShouldHaveSingleItem().TripId.ShouldBe(s.T2.Id);

        Events.Clear();
        var result = await AssignAsync(s.T1.Id, s.OrderId);

        result.Moved.Select(l => l.Id).ShouldBe([s.Legs[1].Id, s.Legs[2].Id], ignoreOrder: true);
        Events.Of<LegUnassignedEto>().ShouldNotContain(e => e.LegId == s.Legs[0].Id);
        (await LegRowsAsync(s.OrderId)).ShouldAllBe(l => l.TripId == s.T1.Id);
    }

    [Fact]
    public async Task No_movable_leg_is_a_business_error_saying_why()
    {
        using var _ = InTenantA();
        var s = await SceneAsync();

        await AssignAsync(s.Target.Id, s.OrderId);
        (await Should.ThrowAsync<BusinessException>(() => AssignAsync(s.Target.Id, s.OrderId))).Code.ShouldBe(PlanningExecutionErrorCodes.OrderAlreadyOnTrip);

        var draft = await OrderAsync(s.Fleet.CustomerA.RelationId, Line(s.Fleet.CustomerA, s.Fleet.CustomerB));
        (await Should.ThrowAsync<BusinessException>(() => AssignAsync(s.Target.Id, draft.Id))).Code.ShouldBe(PlanningExecutionErrorCodes.OrderHasNoLegs);

        await Trips.RecordStopActualsAsync(s.Target.Id, (await Trips.GetAsync(s.Target.Id)).Stops[0].Id, new StopActualsDto { ActualArrival = At(7) });
        foreach (var stop in (await Trips.GetAsync(s.Target.Id)).Stops)
        {
            await Trips.RecordStopActualsAsync(s.Target.Id, stop.Id, new StopActualsDto { ActualArrival = At(7) });
        }

        var other = await TripWithAsync(s.Fleet.Type, At(6), At(18));
        (await Should.ThrowAsync<BusinessException>(() => AssignAsync(other.Id, s.OrderId))).Code.ShouldBe(PlanningExecutionErrorCodes.OrderLegsStuck);
    }

    [Fact]
    public async Task A_stale_stamp_is_a_conflict_and_nothing_moves()
    {
        using var _ = InTenantA();
        var s = await SceneAsync();
        var stale = s.Target.ConcurrencyStamp;
        await Planboard.SetFieldAsync(s.Target.Id, new PlanboardUpdateFieldDto { Field = PlanboardTripField.Remarks, Value = "moved on", ConcurrencyStamp = stale });
        var before = (await LegRowsAsync(s.OrderId)).Select(l => l.TripId).ToList();

        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Planboard.AssignOrderAsync(s.Target.Id, new PlanboardAssignOrderDto { OrderId = s.OrderId, ConcurrencyStamp = stale, AllowOutsideWindow = true }));

        (await LegRowsAsync(s.OrderId)).Select(l => l.TripId).ShouldBe(before);
    }

    [Fact]
    public async Task The_target_trip_must_accept_legs_and_the_window_check_holds_without_the_override()
    {
        using var _ = InTenantA();
        var s = await SceneAsync();
        var elsewhere = await TripWithAsync(s.Fleet.Type, At(6, 0, 28), At(18, 0, 28));
        (await Should.ThrowAsync<BusinessException>(() => AssignAsync(elsewhere.Id, s.OrderId, allowOutsideWindow: false))).Code.ShouldBe(PlanningExecutionErrorCodes.LegOutsideTripWindow);

        await Trips.StartAsync(s.Target.Id, new TripTimestampDto { At = At(6) });
        await Trips.CompleteAsync(s.Target.Id, new TripTimestampDto { At = At(17) });
        (await Should.ThrowAsync<BusinessException>(() => AssignAsync(s.Target.Id, s.OrderId))).Code.ShouldBe(PlanningExecutionErrorCodes.TripNotAssignable);
    }

    [Fact]
    public async Task Create_trip_with_an_order_moves_its_legs_and_reports_them()
    {
        using var _ = InTenantA();
        var s = await SceneAsync();

        var created = await Planboard.CreateTripAsync(new PlanboardCreateTripDto { PlannedDeparture = At(9), TransportTripTypeId = s.Fleet.Type, OrderId = s.OrderId });

        created.Moved.Count.ShouldBe(3);
        created.Stuck.ShouldBeEmpty();
        created.PulledFrom.Count.ShouldBe(2);
        var trip = await Trips.GetAsync(created.Id);
        trip.PlannedArrival.ShouldBe(At(11), "design 74 §10: the departure plus two hours until a route says otherwise");
        (await LegRowsAsync(s.OrderId)).ShouldAllBe(l => l.TripId == created.Id);
        (await Trips.GetAsync(s.T1.Id)).Stops.ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_trip_refuses_an_order_together_with_legs_and_an_order_without_a_movable_leg_creates_no_trip()
    {
        using var _ = InTenantA();
        var s = await SceneAsync();
        var refused = await Should.ThrowAsync<BusinessException>(() => Planboard.CreateTripAsync(new PlanboardCreateTripDto { PlannedDeparture = At(9), TransportTripTypeId = s.Fleet.Type, OrderId = s.OrderId, LegIds = [s.Legs[2].Id] }));
        refused.Code.ShouldBe(PlanningExecutionErrorCodes.CreateTripOrderOrLegs);

        var count = await TripRepository.GetCountAsync();
        var draft = await OrderAsync(s.Fleet.CustomerA.RelationId, Line(s.Fleet.CustomerA, s.Fleet.CustomerB));
        (await Should.ThrowAsync<BusinessException>(() => Planboard.CreateTripAsync(new PlanboardCreateTripDto { PlannedDeparture = At(9), TransportTripTypeId = s.Fleet.Type, OrderId = draft.Id })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.OrderHasNoLegs);
        (await TripRepository.GetCountAsync()).ShouldBe(count);
    }

    [Fact]
    public async Task Another_tenant_reaches_neither_the_trip_nor_the_order()
    {
        Guid orderId, tripId;
        string stamp;
        using (InTenantA())
        {
            var s = await SceneAsync();
            (orderId, tripId, stamp) = (s.OrderId, s.Target.Id, s.Target.ConcurrencyStamp);
        }

        using (InTenantB())
        {
            var f = await FleetAsync();
            var own = await TripWithAsync(f.Type, At(6), At(18));
            await Should.ThrowAsync<EntityNotFoundException>(() => Planboard.GetAssignOrderPreviewAsync(tripId, new PlanboardAssignOrderPreviewRequestDto { OrderId = orderId }));
            await Should.ThrowAsync<EntityNotFoundException>(() => Planboard.AssignOrderAsync(tripId, new PlanboardAssignOrderDto { OrderId = orderId, ConcurrencyStamp = stamp }));
            var preview = await Planboard.GetAssignOrderPreviewAsync(own.Id, new PlanboardAssignOrderPreviewRequestDto { OrderId = orderId });
            preview.Legs.ShouldBeEmpty();
            preview.MovableCount.ShouldBe(0);
            (await Should.ThrowAsync<BusinessException>(() => AssignAsync(own.Id, orderId))).Code.ShouldBe(PlanningExecutionErrorCodes.OrderHasNoLegs);
        }

        using (InTenantA())
        {
            (await Trips.GetAsync(tripId)).Stops.ShouldBeEmpty();
        }
    }
}
