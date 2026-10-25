using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.PlanningExecution.TransportTrips;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace OpenTms.PlanningExecution;

/// <summary>
/// The TransportTrip aggregate's own rules (design 56 "Status machines", design 59 "TransportTrip"): every transition and
/// refusal, vehicles and drivers bounds, attributes bounds, manual stops. Risk class: Critical (status machine).
/// </summary>
public class TransportTripRuleTests
{
    private static readonly DateTimeOffset Departure = new(2026, 10, 26, 6, 0, 0, TimeSpan.FromHours(2));

    private static TransportTrip Trip()
        => new(Guid.NewGuid(), Guid.NewGuid(), "TR-2026-00001", Guid.NewGuid(), new DateOnly(2026, 10, 26), TripExecution.OwnFleet,
            StopCompositionMode.MergeByLocationAndActivity, FirstLastStopRule.None, Departure);

    private static string Code(Action action) => Should.Throw<BusinessException>(action).Code!;

    [Fact]
    public void Planned_starts_then_completes_and_fills_the_missing_actuals()
    {
        var trip = Trip();
        var stop = trip.AddManualStop(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Fuel", null, null, null, null, 10, null);
        trip.Status.ShouldBe(TransportTripStatus.Planned);
        trip.IsAssignable.ShouldBeTrue();

        trip.Start(Departure.AddMinutes(5));
        trip.Status.ShouldBe(TransportTripStatus.InProgress);
        trip.ActualDeparture.ShouldBe(Departure.AddMinutes(5));
        trip.IsAssignable.ShouldBeTrue("an in-progress trip still takes legs");

        var done = Departure.AddHours(8);
        trip.Complete(done);
        trip.Status.ShouldBe(TransportTripStatus.Completed);
        trip.ActualArrival.ShouldBe(done);
        stop.ActualArrival.ShouldBe(done);
        stop.ActualDeparture.ShouldBe(done);
        trip.IsAssignable.ShouldBeFalse();
        trip.IsFinal.ShouldBeTrue();
    }

    [Fact]
    public void Every_other_transition_is_refused_with_InvalidTransition()
    {
        var planned = Trip();
        Code(() => planned.Complete(Departure)).ShouldBe(PlanningExecutionErrorCodes.InvalidTransition);
        Code(() => planned.Archive()).ShouldBe(PlanningExecutionErrorCodes.InvalidTransition);
        Code(() => planned.Unarchive()).ShouldBe(PlanningExecutionErrorCodes.InvalidTransition);

        var inProgress = Trip();
        inProgress.Start(Departure);
        Code(() => inProgress.Start(Departure)).ShouldBe(PlanningExecutionErrorCodes.InvalidTransition);
        Code(() => inProgress.EnsureDeletable()).ShouldBe(PlanningExecutionErrorCodes.InvalidTransition);

        var completed = Trip();
        completed.Start(Departure);
        completed.Complete(Departure.AddHours(1));
        Code(() => completed.Start(Departure)).ShouldBe(PlanningExecutionErrorCodes.InvalidTransition);
        Code(() => completed.Complete(Departure)).ShouldBe(PlanningExecutionErrorCodes.InvalidTransition);
        Code(() => completed.Cancel()).ShouldBe(PlanningExecutionErrorCodes.InvalidTransition);
        Code(() => completed.EnsureAssignable()).ShouldBe(PlanningExecutionErrorCodes.TripNotAssignable);

        var cancelled = Trip();
        cancelled.Cancel();
        cancelled.Status.ShouldBe(TransportTripStatus.Cancelled);
        Code(() => cancelled.Cancel()).ShouldBe(PlanningExecutionErrorCodes.InvalidTransition);
        Code(() => cancelled.Start(Departure)).ShouldBe(PlanningExecutionErrorCodes.InvalidTransition);
        Code(() => cancelled.EnsureAssignable()).ShouldBe(PlanningExecutionErrorCodes.TripNotAssignable);

        var inProgressCancel = Trip();
        inProgressCancel.Start(Departure);
        inProgressCancel.Cancel();
        inProgressCancel.Status.ShouldBe(TransportTripStatus.Cancelled);
    }

    [Fact]
    public void Archiving_needs_a_final_status_and_an_archived_trip_is_read_only_until_unarchived()
    {
        var trip = Trip();
        trip.Cancel();
        trip.Archive();
        trip.IsArchived.ShouldBeTrue();
        Code(() => trip.Archive()).ShouldBe(PlanningExecutionErrorCodes.InvalidTransition);
        Code(() => trip.SetCarrier(Guid.NewGuid(), null)).ShouldBe(PlanningExecutionErrorCodes.TripArchived);
        Code(() => trip.SetDrivers([Guid.NewGuid()])).ShouldBe(PlanningExecutionErrorCodes.TripArchived);
        Code(() => trip.EnsureAssignable()).ShouldBe(PlanningExecutionErrorCodes.TripNotAssignable);
        Code(() => trip.EnsureDeletable()).ShouldBe(PlanningExecutionErrorCodes.InvalidTransition);

        trip.Unarchive();
        trip.IsArchived.ShouldBeFalse();
        trip.SetDrivers([Guid.NewGuid()]);
    }

    [Fact]
    public void Vehicles_are_a_combination_or_up_to_three_ids_and_drivers_at_most_three()
    {
        var trip = Trip();
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToList();

        Code(() => trip.SetVehicles(null, ids)).ShouldBe(PlanningExecutionErrorCodes.TooManyVehicles);
        Code(() => trip.SetVehicles(Guid.NewGuid(), ids.Take(1).ToList())).ShouldBe(PlanningExecutionErrorCodes.VehicleSourceConflict);
        Code(() => trip.SetDrivers(ids)).ShouldBe(PlanningExecutionErrorCodes.TooManyDrivers);

        trip.SetVehicles(null, ids.Take(3).ToList());
        trip.Vehicles.OrderBy(v => v.Position).Select(v => v.VehicleId).ShouldBe(ids.Take(3));
        trip.SetVehicles(null, [ids[2], ids[0]]);
        trip.Vehicles.OrderBy(v => v.Position).Select(v => v.VehicleId).ShouldBe([ids[2], ids[0]]);
        trip.SetVehicles(ids[3], []);
        trip.Vehicles.ShouldBeEmpty();
        trip.VehicleCombinationId.ShouldBe(ids[3]);

        trip.SetDrivers([ids[0], ids[0], ids[1]]);
        trip.Drivers.Count.ShouldBe(2, "duplicates collapse");
    }

    [Fact]
    public void Attributes_are_bounded_and_the_header_refuses_an_arrival_before_the_departure()
    {
        var trip = Trip();
        var tooMany = Enumerable.Range(0, PlanningExecutionConsts.MaxAttributes + 1).ToDictionary(i => "k" + i, _ => "v");
        Code(() => trip.SetAttributes(tooMany)).ShouldBe(PlanningExecutionErrorCodes.InvalidAttributes);
        Code(() => trip.SetAttributes(new Dictionary<string, string> { [new string('k', PlanningExecutionConsts.MaxAttributeKeyLength + 1)] = "v" })).ShouldBe(PlanningExecutionErrorCodes.InvalidAttributes);
        Code(() => trip.SetAttributes(new Dictionary<string, string> { ["k"] = new string('v', PlanningExecutionConsts.MaxAttributeValueLength + 1) })).ShouldBe(PlanningExecutionErrorCodes.InvalidAttributes);
        trip.SetAttributes(new Dictionary<string, string> { [" Colour "] = "red" });
        trip.Attributes.ShouldBe(new Dictionary<string, string> { ["Colour"] = "red" });

        Code(() => trip.SetHeader("x", trip.TripDate, Departure, Departure.AddHours(-1), null, null, null, null, null, null, null, TripExecution.OwnFleet,
            StopCompositionMode.MergeByLocationAndActivity, FirstLastStopRule.None, null)).ShouldBe(PlanningExecutionErrorCodes.ArrivalBeforeDeparture);
    }

    [Fact]
    public void Manual_stops_insert_move_and_remove_with_renumbering_and_the_actuals_guard()
    {
        var trip = Trip();
        var a = trip.AddManualStop(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), null, "A", null, null, null, null, null, null);
        var b = trip.AddManualStop(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), null, "B", null, null, null, null, null, null);
        var c = trip.AddManualStop(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), null, "C", null, null, null, null, null, null);
        trip.OrderedStops.Select(s => s.Name).ShouldBe(["C", "A", "B"]);

        trip.MoveStop(b.Id, 1);
        trip.OrderedStops.Select(s => s.Name).ShouldBe(["B", "C", "A"]);
        trip.OrderedStops.Select(s => s.Position).ShouldBe([1, 2, 3]);
        Code(() => trip.MoveStop(b.Id, 4)).ShouldBe(PlanningExecutionErrorCodes.InvalidPosition);

        trip.RecordStopActual(c.Id, Departure, null, 1000);
        Code(() => trip.MoveStop(c.Id, 1)).ShouldBe(PlanningExecutionErrorCodes.StopHasActuals);
        Code(() => trip.RemoveManualStop(c.Id)).ShouldBe(PlanningExecutionErrorCodes.StopHasActuals);

        trip.RemoveManualStop(a.Id);
        trip.OrderedStops.Select(s => s.Name).ShouldBe(["B", "C"]);
        trip.OrderedStops.Select(s => s.Position).ShouldBe([1, 2]);
        Should.Throw<Volo.Abp.Domain.Entities.EntityNotFoundException>(() => trip.GetStop(a.Id));
    }

    [Fact]
    public void Warnings_deduplicate_and_the_calculator_values_round_to_their_scales()
    {
        var trip = Trip();
        var legId = Guid.NewGuid();
        trip.AddWarning(new TripWarning(TripWarningCode.LegChangedAfterPlanning, legId));
        trip.AddWarning(new TripWarning(TripWarningCode.LegChangedAfterPlanning, legId));
        trip.Warnings.ShouldHaveSingleItem();

        trip.SetLoad(0.123456m, 13.6);
        trip.LoadFactor.ShouldBe(0.1235m);
        trip.SetCo2(12.34567m);
        trip.Co2Kg.ShouldBe(12.346m);
        trip.SetMoney(100.005m, 10.004m);
        trip.CostAmount.ShouldBe(100.01m);
        trip.RevenueAmount.ShouldBe(10.00m);

        var json = TripWarning.ToJson(trip.Warnings);
        TripWarning.FromJson(json).ShouldBe(trip.Warnings);
        TripWarning.FromJson(null).ShouldBeEmpty();
    }
}
