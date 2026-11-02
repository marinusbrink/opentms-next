using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.Planboard;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// Design 68 "API contract — commands" on SQLite: every narrow command of the Planboard — reschedule (stop times shift,
/// final trips refused), change resource (D11: a tractor fills only empty slots), add driver (max three, a duplicate is a
/// no-op), set field, create from a cell (type defaults, then the row's resource, legs assigned in the same unit of work),
/// assign/unassign, complete (both actuals stamped, only missing stop actuals filled) — each refusing a stale concurrency
/// stamp with 409, notifying the trip with the merged hints of both states after the commit and nothing on a rollback,
/// asking the trips it overlapped to recalculate, and none of them reaching another tenant's trip.
/// Risk class: High (planning).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class PlanboardCommandTests : PlanboardTestBase
{
    private async Task<(FleetSet F, TransportTripDto Trip, Guid Order)> PlannedTripAsync(params Guid[] vehicles)
    {
        var f = await FleetAsync();
        var order = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.Depot, f.CustomerA), Line(f.Depot, f.CustomerB, fromHour: 9, toHour: 14));
        var trip = await TripWithAsync(f.Type, At(6), At(18), vehicles.Length == 0 ? [f.Tractor] : vehicles, [f.Driver1]);
        trip = await Trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = await LegIdsOfOrderAsync(order) });
        return (f, trip, order);
    }

    /* Reschedule */

    [Fact]
    public async Task Reschedule_moves_the_trip_keeps_its_length_shifts_every_stop_and_notifies_both_days_after_the_commit()
    {
        using var _ = InTenantA();
        var (f, trip, _) = await PlannedTripAsync();
        await CalculateAsync(trip.Id);
        trip = await Trips.GetAsync(trip.Id);
        trip.Stops.ShouldContain(s => s.PlannedArrival != null, "the calculation stored the stop times");
        var before = trip.Stops.ToDictionary(s => s.Id, s => (s.PlannedArrival, s.PlannedDeparture));
        var noticesBefore = NoticesOf(trip.Id).Count;

        var change = await Planboard.RescheduleAsync(trip.Id, new PlanboardRescheduleDto { PlannedDeparture = At(7, 30, 27), ConcurrencyStamp = trip.ConcurrencyStamp });

        change.Id.ShouldBe(trip.Id);
        change.ConcurrencyStamp.ShouldNotBe(trip.ConcurrencyStamp);
        var moved = await Trips.GetAsync(trip.Id);
        moved.PlannedDeparture.ShouldBe(At(7, 30, 27));
        moved.PlannedArrival.ShouldBe(At(19, 30, 27), "the arrival keeps its distance to the departure");
        moved.TripDate.ShouldBe(new DateOnly(2026, 10, 27));
        moved.ConcurrencyStamp.ShouldBe(change.ConcurrencyStamp);
        var delta = TimeSpan.FromHours(25.5);
        foreach (var stop in moved.Stops)
        {
            stop.PlannedArrival.ShouldBe(before[stop.Id].PlannedArrival + delta, stop.Name);
            stop.PlannedDeparture.ShouldBe(before[stop.Id].PlannedDeparture + delta, stop.Name);
        }

        Requests.ReasonsFor(PlanningExecutionApplicationModule.TripTargetType, trip.Id).ShouldContain(nameof(TripChangeKind.Header));
        var notice = NoticesOf(trip.Id).Skip(noticesBefore).ShouldHaveSingleItem("one notice per command, after the commit");
        notice.TenantId.ShouldBe(TenantA);
        notice.Hints.ShouldNotBeNull();
        notice.Hints.From.ShouldBe(new DateOnly(2026, 10, 26), "the day the trip left");
        notice.Hints.Till.ShouldBe(new DateOnly(2026, 10, 27), "the day it arrived on");
        notice.Hints.ResourceIds!.ShouldBe([f.Tractor, f.Driver1], ignoreOrder: true);
        notice.Hints.Removed.ShouldBeFalse();

        var explicitArrival = await Planboard.RescheduleAsync(trip.Id, new PlanboardRescheduleDto { PlannedDeparture = At(8, 0, 27), PlannedArrival = At(10, 0, 27), ConcurrencyStamp = change.ConcurrencyStamp });
        (await Trips.GetAsync(trip.Id)).PlannedArrival.ShouldBe(At(10, 0, 27));
        (await Should.ThrowAsync<BusinessException>(() => Planboard.RescheduleAsync(trip.Id, new PlanboardRescheduleDto { PlannedDeparture = At(8, 0, 27), PlannedArrival = At(7, 0, 27), ConcurrencyStamp = explicitArrival.ConcurrencyStamp })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.ArrivalBeforeDeparture);
        (await Should.ThrowAsync<BusinessException>(() => Planboard.RescheduleAsync(trip.Id, new PlanboardRescheduleDto { PlannedDeparture = new DateTimeOffset(2027, 1, 4, 8, 0, 0, Cet), ConcurrencyStamp = explicitArrival.ConcurrencyStamp })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.TripDateLocked, "the date may not leave the numbering period");
    }

    [Fact]
    public async Task Reschedule_refuses_a_completed_or_cancelled_trip_and_a_stale_stamp_and_a_refusal_notifies_nobody()
    {
        using var _ = InTenantA();
        var (_, trip, _) = await PlannedTripAsync();
        var stale = trip.ConcurrencyStamp;
        var current = (await Planboard.SetFieldAsync(trip.Id, new PlanboardUpdateFieldDto { Field = PlanboardTripField.Name, Value = "Touched", ConcurrencyStamp = stale })).ConcurrencyStamp;

        var noticesBefore = NoticesOf(trip.Id).Count;
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Planboard.RescheduleAsync(trip.Id, new PlanboardRescheduleDto { PlannedDeparture = At(8), ConcurrencyStamp = stale }));
        (await Trips.GetAsync(trip.Id)).PlannedDeparture.ShouldBe(At(6), "nothing changed");

        await Trips.StartAsync(trip.Id, new TripTimestampDto { At = At(6) });
        var completed = await Trips.CompleteAsync(trip.Id, new TripTimestampDto { At = At(17) });
        noticesBefore = NoticesOf(trip.Id).Count;
        (await Should.ThrowAsync<BusinessException>(() => Planboard.RescheduleAsync(trip.Id, new PlanboardRescheduleDto { PlannedDeparture = At(8), ConcurrencyStamp = completed.ConcurrencyStamp })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.InvalidTransition);
        NoticesOf(trip.Id).Count.ShouldBe(noticesBefore, "the load noticed the trip, but the refused unit of work never committed: nothing is sent");

        var f2 = await TripWithAsync(trip.TransportTripTypeId, At(6), At(8));
        var cancelled = await Trips.CancelAsync(f2.Id);
        (await Should.ThrowAsync<BusinessException>(() => Planboard.RescheduleAsync(f2.Id, new PlanboardRescheduleDto { PlannedDeparture = At(9), ConcurrencyStamp = cancelled.ConcurrencyStamp })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.InvalidTransition);
        current.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Every_command_refuses_a_stale_concurrency_stamp()
    {
        using var _ = InTenantA();
        var (f, trip, order) = await PlannedTripAsync();
        const string stale = "not-the-stamp";
        var legs = await LegIdsOfOrderAsync(order);

        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Planboard.RescheduleAsync(trip.Id, new PlanboardRescheduleDto { PlannedDeparture = At(7), ConcurrencyStamp = stale }));
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Planboard.ChangeResourceAsync(trip.Id, new PlanboardChangeResourceDto { Mode = PlanboardResourceMode.Driver, ToResourceId = f.Driver2, ConcurrencyStamp = stale }));
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Planboard.AddDriverAsync(trip.Id, new PlanboardAddDriverDto { DriverId = f.Driver2, ConcurrencyStamp = stale }));
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Planboard.SetFieldAsync(trip.Id, new PlanboardUpdateFieldDto { Field = PlanboardTripField.Remarks, Value = "x", ConcurrencyStamp = stale }));
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Planboard.UnassignLegsAsync(trip.Id, new PlanboardLegsDto { LegIds = legs, ConcurrencyStamp = stale }));
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Planboard.AssignLegsAsync(trip.Id, new PlanboardLegsDto { LegIds = legs, ConcurrencyStamp = stale }));
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Planboard.CompleteAsync(trip.Id, new PlanboardCompleteDto { ActualDeparture = At(6), ActualArrival = At(9), ConcurrencyStamp = stale }));

        var unchanged = await Trips.GetAsync(trip.Id);
        unchanged.ConcurrencyStamp.ShouldBe(trip.ConcurrencyStamp);
        unchanged.Drivers.Select(d => d.DriverId).ShouldBe([f.Driver1]);
        unchanged.Remarks.ShouldBeNull();
    }

    /* Change resource (D11) */

    [Fact]
    public async Task A_tractor_put_on_a_trip_without_drivers_and_carrier_brings_its_default_driver_and_an_own_vehicle_no_carrier()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var trip = await TripWithAsync(f.Type, At(6), At(18), [f.Trailer1]);

        await Planboard.ChangeResourceAsync(trip.Id, new PlanboardChangeResourceDto { Mode = PlanboardResourceMode.Tractor, ToResourceId = f.Tractor, ConcurrencyStamp = trip.ConcurrencyStamp });

        var changed = await Trips.GetAsync(trip.Id);
        changed.Vehicles.Select(v => v.VehicleId).ShouldBe([f.Tractor, f.Trailer1], "the motor vehicle goes before the trailers");
        changed.Drivers.Select(d => d.DriverId).ShouldBe([f.DefaultDriver], "the empty driver slot takes the tractor's default driver");
        changed.CarrierRelationId.ShouldBeNull("the tractor belongs to the own company: no carrier");

        var notice = NoticesOf(trip.Id).Last();
        notice.Hints!.ResourceIds!.ShouldBe([f.Trailer1, f.Tractor, f.DefaultDriver], ignoreOrder: true);
        Requests.ReasonsFor(PlanningExecutionApplicationModule.TripTargetType, trip.Id).ShouldContain(nameof(TripChangeKind.Vehicles));
    }

    [Fact]
    public async Task A_tractor_never_overwrites_set_drivers_or_a_set_carrier_and_a_charter_tractor_fills_only_the_empty_carrier()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();

        /* Drivers set, no carrier: the charter tractor keeps the driver and brings its owner as carrier. */
        var withDriver = await TripWithAsync(f.Type, At(6), At(18), [f.Tractor], [f.Driver1]);
        await Planboard.ChangeResourceAsync(withDriver.Id, new PlanboardChangeResourceDto
        {
            Mode = PlanboardResourceMode.Tractor, FromResourceId = f.Tractor, ToResourceId = f.CharterTractor, ConcurrencyStamp = withDriver.ConcurrencyStamp
        });
        var a = await Trips.GetAsync(withDriver.Id);
        a.Vehicles.Select(v => v.VehicleId).ShouldBe([f.CharterTractor]);
        a.Drivers.Select(d => d.DriverId).ShouldBe([f.Driver1]);
        a.CarrierRelationId.ShouldBe(f.Carrier);

        /* Carrier set, no drivers: the own tractor brings its default driver but leaves the carrier. */
        var other = await PlaceAsync("CARR2", "Other Carrier", 52.5, 6.1, OpenTms.MasterData.Relations.RelationRoles.Carrier, "Zwolle");
        var withCarrier = await TripWithAsync(f.Type, At(6), At(18), carrier: other.RelationId);
        await Planboard.ChangeResourceAsync(withCarrier.Id, new PlanboardChangeResourceDto { Mode = PlanboardResourceMode.Tractor, ToResourceId = f.Tractor, ConcurrencyStamp = withCarrier.ConcurrencyStamp });
        var b = await Trips.GetAsync(withCarrier.Id);
        b.CarrierRelationId.ShouldBe(other.RelationId, "a set carrier is never overwritten");
        b.Drivers.Select(d => d.DriverId).ShouldBe([f.DefaultDriver]);

        /* Both set: nothing but the vehicle changes. */
        var full = await TripWithAsync(f.Type, At(6), At(18), [f.Rigid], [f.Driver2], carrier: other.RelationId);
        await Planboard.ChangeResourceAsync(full.Id, new PlanboardChangeResourceDto { Mode = PlanboardResourceMode.Tractor, ToResourceId = f.CharterTractor, ConcurrencyStamp = full.ConcurrencyStamp });
        var c = await Trips.GetAsync(full.Id);
        c.Vehicles.Select(v => v.VehicleId).ShouldBe([f.CharterTractor], "a tractor replaces the trip's motor vehicle");
        c.Drivers.Select(d => d.DriverId).ShouldBe([f.Driver2]);
        c.CarrierRelationId.ShouldBe(other.RelationId);
    }

    [Fact]
    public async Task Trailers_and_drivers_take_or_replace_positions_and_a_wrong_kind_or_absent_resource_is_refused()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var trip = await TripWithAsync(f.Type, At(6), At(18), [f.Tractor, f.Trailer1], [f.Driver1, f.Driver2]);
        var stamp = trip.ConcurrencyStamp;

        stamp = (await Planboard.ChangeResourceAsync(trip.Id, new PlanboardChangeResourceDto { Mode = PlanboardResourceMode.Trailer, ToResourceId = f.Trailer2, ConcurrencyStamp = stamp })).ConcurrencyStamp;
        (await Trips.GetAsync(trip.Id)).Vehicles.Select(v => v.VehicleId).ShouldBe([f.Tractor, f.Trailer1, f.Trailer2], "a trailer takes the next free position");

        stamp = (await Planboard.ChangeResourceAsync(trip.Id, new PlanboardChangeResourceDto { Mode = PlanboardResourceMode.Driver, FromResourceId = f.Driver1, ToResourceId = f.Driver3, ConcurrencyStamp = stamp })).ConcurrencyStamp;
        var afterDriver = await Trips.GetAsync(trip.Id);
        afterDriver.Drivers.Select(d => d.DriverId).ShouldBe([f.Driver3, f.Driver2], "the driver's position is replaced");
        NoticesOf(trip.Id).Last().Hints!.ResourceIds!.ShouldContain(f.Driver1, "the row the trip left hears about it");
        NoticesOf(trip.Id).Last().Hints!.ResourceIds!.ShouldContain(f.Driver3);

        (await Should.ThrowAsync<BusinessException>(() => Planboard.ChangeResourceAsync(trip.Id, new PlanboardChangeResourceDto { Mode = PlanboardResourceMode.Trailer, ToResourceId = f.Rigid, ConcurrencyStamp = stamp })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.ResourceKindMismatch);
        (await Should.ThrowAsync<BusinessException>(() => Planboard.ChangeResourceAsync(trip.Id, new PlanboardChangeResourceDto { Mode = PlanboardResourceMode.Tractor, ToResourceId = f.Trailer1, ConcurrencyStamp = stamp })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.ResourceKindMismatch);
        (await Should.ThrowAsync<BusinessException>(() => Planboard.ChangeResourceAsync(trip.Id, new PlanboardChangeResourceDto { Mode = PlanboardResourceMode.Driver, FromResourceId = f.Driver4, ToResourceId = f.Driver1, ConcurrencyStamp = stamp })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.ResourceNotOnTrip);
        (await Should.ThrowAsync<BusinessException>(() => Planboard.ChangeResourceAsync(trip.Id, new PlanboardChangeResourceDto { Mode = PlanboardResourceMode.Tractor, ToResourceId = Guid.NewGuid(), ConcurrencyStamp = stamp })))
            .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => Planboard.ChangeResourceAsync(trip.Id, new PlanboardChangeResourceDto { Mode = PlanboardResourceMode.Driver, ToResourceId = Guid.NewGuid(), ConcurrencyStamp = stamp })))
            .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
    }

    [Fact]
    public async Task Replacing_a_vehicle_of_a_combination_trip_keeps_the_members_as_loose_vehicles()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var combination = await CombinationAsync("Combi 1", f.Tractor, f.Trailer1);
        var trip = await TripWithAsync(f.Type, At(6), At(18), combination: combination, drivers: [f.Driver1]);

        await Planboard.ChangeResourceAsync(trip.Id, new PlanboardChangeResourceDto { Mode = PlanboardResourceMode.Trailer, FromResourceId = f.Trailer1, ToResourceId = f.Trailer2, ConcurrencyStamp = trip.ConcurrencyStamp });

        var changed = await Trips.GetAsync(trip.Id);
        changed.VehicleCombinationId.ShouldBeNull();
        changed.Vehicles.Select(v => v.VehicleId).ShouldBe([f.Tractor, f.Trailer2]);
    }

    /* Add driver, set field */

    [Fact]
    public async Task Add_driver_takes_the_next_position_up_to_three_and_a_driver_already_on_the_trip_is_a_no_op()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var trip = await TripWithAsync(f.Type, At(6), At(18), [f.Tractor], [f.Driver1]);

        var second = await Planboard.AddDriverAsync(trip.Id, new PlanboardAddDriverDto { DriverId = f.Driver2, ConcurrencyStamp = trip.ConcurrencyStamp });
        var third = await Planboard.AddDriverAsync(trip.Id, new PlanboardAddDriverDto { DriverId = f.Driver3, ConcurrencyStamp = second.ConcurrencyStamp });
        (await Trips.GetAsync(trip.Id)).Drivers.Select(d => (d.Position, d.DriverId)).ShouldBe([(1, f.Driver1), (2, f.Driver2), (3, f.Driver3)]);

        var notices = NoticesOf(trip.Id).Count;
        var duplicate = await Planboard.AddDriverAsync(trip.Id, new PlanboardAddDriverDto { DriverId = f.Driver2, ConcurrencyStamp = third.ConcurrencyStamp });
        duplicate.ConcurrencyStamp.ShouldBe(third.ConcurrencyStamp, "nothing was saved");
        (await Trips.GetAsync(trip.Id)).Drivers.Count.ShouldBe(3);

        (await Should.ThrowAsync<BusinessException>(() => Planboard.AddDriverAsync(trip.Id, new PlanboardAddDriverDto { DriverId = f.Driver4, ConcurrencyStamp = third.ConcurrencyStamp })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.TooManyDrivers);
        (await Should.ThrowAsync<BusinessException>(() => Planboard.AddDriverAsync(trip.Id, new PlanboardAddDriverDto { DriverId = Guid.NewGuid(), ConcurrencyStamp = third.ConcurrencyStamp })))
            .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        notices.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Set_field_edits_the_name_the_remarks_and_the_driver_instructions_one_at_a_time()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var trip = await TripWithAsync(f.Type, At(6), At(18), [f.Tractor]);

        var stamp = (await Planboard.SetFieldAsync(trip.Id, new PlanboardUpdateFieldDto { Field = PlanboardTripField.Name, Value = "Night run", ConcurrencyStamp = trip.ConcurrencyStamp })).ConcurrencyStamp;
        stamp = (await Planboard.SetFieldAsync(trip.Id, new PlanboardUpdateFieldDto { Field = PlanboardTripField.Remarks, Value = "Gate 4", ConcurrencyStamp = stamp })).ConcurrencyStamp;
        stamp = (await Planboard.SetFieldAsync(trip.Id, new PlanboardUpdateFieldDto { Field = PlanboardTripField.DriverInstructions, Value = "Call ahead", ConcurrencyStamp = stamp })).ConcurrencyStamp;

        var changed = await Trips.GetAsync(trip.Id);
        changed.Name.ShouldBe("Night run");
        changed.Remarks.ShouldBe("Gate 4");
        changed.DriverInstructions.ShouldBe("Call ahead");
        changed.ConcurrencyStamp.ShouldBe(stamp);

        (await Should.ThrowAsync<BusinessException>(() => Planboard.SetFieldAsync(trip.Id, new PlanboardUpdateFieldDto { Field = (PlanboardTripField)9, Value = "x", ConcurrencyStamp = stamp })))
            .Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
    }

    /* Create from a cell */

    [Fact]
    public async Task A_trip_created_on_a_tractor_row_takes_the_types_defaults_first_and_the_row_tractor_in_its_slot()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var type = (await GetRequiredService<ITransportTripTypeAppService>().CreateAsync(new TransportTripTypeCreateDto
        {
            Code = "DEF", Names = Names("Defaults"), Numbering = Numbering("DF", year: true),
            DefaultVehicleIds = [f.Rigid, f.Trailer1], DefaultDriverIds = [f.Driver2]
        })).Id;

        var created = await Planboard.CreateTripAsync(new PlanboardCreateTripDto { TransportTripTypeId = type, Mode = PlanboardResourceMode.Tractor, ResourceId = f.Tractor, PlannedDeparture = At(9) });

        var trip = await Trips.GetAsync(created.Id);
        trip.TransportTripTypeId.ShouldBe(type);
        trip.PlannedDeparture.ShouldBe(At(9));
        trip.Vehicles.Select(v => v.VehicleId).ShouldBe([f.Tractor, f.Trailer1], "the row's tractor replaces the type's motor vehicle; the type's trailer stays");
        trip.Drivers.Select(d => d.DriverId).ShouldBe([f.Driver2], "the type filled the driver slot, so the tractor's default driver does not");
        trip.ConcurrencyStamp.ShouldBe(created.ConcurrencyStamp);
        Requests.ReasonsFor(PlanningExecutionApplicationModule.TripTargetType, trip.Id).ShouldContain("Created");
        NoticesOf(trip.Id).ShouldNotBeEmpty("the new trip is announced after the commit");

        var onDriverRow = await Planboard.CreateTripAsync(new PlanboardCreateTripDto { TransportTripTypeId = type, Mode = PlanboardResourceMode.Driver, ResourceId = f.Driver3, PlannedDeparture = At(10) });
        (await Trips.GetAsync(onDriverRow.Id)).Drivers.Select(d => d.DriverId).ShouldBe([f.Driver3, f.Driver2], "the row's driver first, the type's after");

        var onTrailerRow = await Planboard.CreateTripAsync(new PlanboardCreateTripDto { TransportTripTypeId = type, Mode = PlanboardResourceMode.Trailer, ResourceId = f.Trailer2, PlannedDeparture = At(11) });
        (await Trips.GetAsync(onTrailerRow.Id)).Vehicles.Select(v => v.VehicleId).ShouldBe([f.Rigid, f.Trailer1, f.Trailer2]);

        var withoutDefaults = await Planboard.CreateTripAsync(new PlanboardCreateTripDto { TransportTripTypeId = f.Type, Mode = PlanboardResourceMode.Tractor, ResourceId = f.Tractor, PlannedDeparture = At(12) });
        (await Trips.GetAsync(withoutDefaults.Id)).Drivers.Select(d => d.DriverId).ShouldBe([f.DefaultDriver], "an empty driver slot takes the tractor's default driver (D11)");

        (await Should.ThrowAsync<BusinessException>(() => Planboard.CreateTripAsync(new PlanboardCreateTripDto { TransportTripTypeId = type, ResourceId = f.Tractor, PlannedDeparture = At(9) })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.ResourceKindMismatch, "a resource needs its mode");
        (await Should.ThrowAsync<BusinessException>(() => Planboard.CreateTripAsync(new PlanboardCreateTripDto { TransportTripTypeId = type, Mode = PlanboardResourceMode.Tractor, ResourceId = f.Trailer2, PlannedDeparture = At(9) })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.ResourceKindMismatch);
    }

    [Fact]
    public async Task A_trip_created_from_a_leg_drop_assigns_the_legs_in_the_same_unit_of_work_and_without_a_type_takes_the_tenants_first()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var order = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.Depot, f.CustomerA));
        var legs = await LegIdsOfOrderAsync(order);

        var created = await Planboard.CreateTripAsync(new PlanboardCreateTripDto { Mode = PlanboardResourceMode.Driver, ResourceId = f.Driver1, PlannedDeparture = At(5), LegIds = legs });

        var trip = await Trips.GetAsync(created.Id);
        trip.TransportTripTypeId.ShouldBe(f.Type, "the tenant's only active type");
        trip.Stops.Count.ShouldBe(2);
        (await LegRepository.GetListAsync(l => legs.Contains(l.Id))).ShouldAllBe(l => l.TripId == created.Id);
        foreach (var leg in legs)
        {
            NoticesOf(leg).Last().Hints!.Removed.ShouldBeTrue("the leg left the unplanned list");
        }
    }

    [Fact]
    public async Task Without_any_active_trip_type_a_trip_cannot_be_created_from_a_cell()
    {
        using var _ = InTenantB();
        await OrdersMasterDataAsync();
        (await Should.ThrowAsync<BusinessException>(() => Planboard.CreateTripAsync(new PlanboardCreateTripDto { PlannedDeparture = At(5) })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.NoTripType);
    }

    /* Assign, unassign, complete */

    [Fact]
    public async Task Assign_and_unassign_through_the_planboard_move_legs_and_notify_trip_and_legs()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var order = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.Depot, f.CustomerA));
        var legs = await LegIdsOfOrderAsync(order);
        var trip = await TripWithAsync(f.Type, At(6), At(18), [f.Tractor]);

        var assigned = await Planboard.AssignLegsAsync(trip.Id, new PlanboardLegsDto { LegIds = legs, ConcurrencyStamp = trip.ConcurrencyStamp });
        (await Trips.GetAsync(trip.Id)).Stops.Count.ShouldBe(2);
        (await Legs.GetListAsync(LegList())).Rows.ShouldBeEmpty();
        NoticesOf(legs[0]).Last().Hints!.Removed.ShouldBeTrue();

        await Planboard.UnassignLegsAsync(trip.Id, new PlanboardLegsDto { LegIds = legs, ConcurrencyStamp = assigned.ConcurrencyStamp });
        (await Trips.GetAsync(trip.Id)).Stops.ShouldBeEmpty();
        (await Legs.GetListAsync(LegList())).Rows.Select(l => l.Id).ShouldBe(legs, ignoreOrder: true);
        NoticesOf(legs[0]).Last().Hints!.Removed.ShouldBeFalse("back on the unplanned list");
        NoticesOf(legs[0]).Last().Hints!.From.ShouldBe(Day);
    }

    [Fact]
    public async Task Complete_stamps_both_actuals_fills_only_the_missing_stop_actuals_and_refuses_a_planned_trip()
    {
        using var _ = InTenantA();
        var (_, trip, _) = await PlannedTripAsync();
        (await Should.ThrowAsync<BusinessException>(() => Planboard.CompleteAsync(trip.Id, new PlanboardCompleteDto { ActualDeparture = At(6), ActualArrival = At(17), ConcurrencyStamp = trip.ConcurrencyStamp })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.InvalidTransition, "only a running trip is completed");

        await Trips.StartAsync(trip.Id, new TripTimestampDto { At = At(6, 10) });
        var first = trip.Stops.OrderBy(s => s.Position).First();
        var running = await Trips.RecordStopActualsAsync(trip.Id, first.Id, new StopActualsDto { ActualArrival = At(7, 55), ActualDeparture = At(8, 40) });
        (await Should.ThrowAsync<BusinessException>(() => Planboard.CompleteAsync(trip.Id, new PlanboardCompleteDto { ActualDeparture = At(17), ActualArrival = At(6), ConcurrencyStamp = running.ConcurrencyStamp })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.ArrivalBeforeDeparture);

        var completed = await Planboard.CompleteAsync(trip.Id, new PlanboardCompleteDto { ActualDeparture = At(6, 0), ActualArrival = At(17, 30), ConcurrencyStamp = running.ConcurrencyStamp });

        var done = await Trips.GetAsync(trip.Id);
        done.Status.ShouldBe(TransportTripStatus.Completed);
        done.ActualDeparture.ShouldBe(At(6, 0), "the stated departure replaces the recorded start");
        done.ActualArrival.ShouldBe(At(17, 30));
        var stops = done.Stops.OrderBy(s => s.Position).ToList();
        stops[0].ActualArrival.ShouldBe(At(7, 55), "a recorded actual stays");
        stops[0].ActualDeparture.ShouldBe(At(8, 40));
        stops.Skip(1).ShouldAllBe(s => s.ActualArrival == At(17, 30) && s.ActualDeparture == At(17, 30), "missing actuals take the arrival");
        done.ConcurrencyStamp.ShouldBe(completed.ConcurrencyStamp);
        Requests.ReasonsFor(PlanningExecutionApplicationModule.TripTargetType, trip.Id).ShouldContain("Completed");
    }

    [Fact]
    public async Task Archiving_or_deleting_a_trip_tells_the_type_group_the_trip_left_the_set()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var archived = await TripWithAsync(f.Type, At(6), At(18), [f.Tractor]);
        await Trips.CancelAsync(archived.Id);
        await Trips.ArchiveAsync(archived.Id);
        NoticesOf(archived.Id).Last().Hints!.Removed.ShouldBeTrue("archived: off the Scheduler");

        await Trips.UnarchiveAsync(archived.Id);
        NoticesOf(archived.Id).Last().Hints!.Removed.ShouldBeFalse();

        var deleted = await TripWithAsync(f.Type, At(6), At(18), [f.Rigid]);
        await Trips.DeleteAsync(deleted.Id);
        var last = NoticesOf(deleted.Id).Last();
        last.Hints!.Removed.ShouldBeTrue("deleted");
        last.Hints.ResourceIds!.ShouldContain(f.Rigid, "the row it left hears about it");
    }

    /* Fan-out and isolation */

    [Fact]
    public async Task A_command_that_moves_a_trip_asks_the_trips_sharing_its_resources_before_and_after_to_recalculate()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var trip = await TripWithAsync(f.Type, At(6), At(10), [f.Tractor], [f.Driver1]);
        var overlappedBefore = await TripWithAsync(f.Type, At(8), At(9), [f.Rigid], [f.Driver1]);
        var overlappedAfter = await TripWithAsync(f.Type, At(15), At(16), [f.Tractor]);
        var unrelated = await TripWithAsync(f.Type, At(15), At(16), [f.Van], [f.Driver4]);

        await Planboard.RescheduleAsync(trip.Id, new PlanboardRescheduleDto { PlannedDeparture = At(14), ConcurrencyStamp = trip.ConcurrencyStamp });

        Requests.ReasonsFor(PlanningExecutionApplicationModule.TripTargetType, overlappedBefore.Id).ShouldContain("OverlappingHeader", "the old double booking clears");
        Requests.ReasonsFor(PlanningExecutionApplicationModule.TripTargetType, overlappedAfter.Id).ShouldContain("OverlappingHeader", "the new double booking appears");
        Requests.ReasonsFor(PlanningExecutionApplicationModule.TripTargetType, unrelated.Id).ShouldNotContain("OverlappingHeader");
    }

    [Fact]
    public async Task Negative_tenant_isolation_no_command_reaches_another_tenants_trip()
    {
        FleetSet f;
        TransportTripDto trip;
        Guid leg;
        using (InTenantA())
        {
            f = await FleetAsync();
            var order = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.Depot, f.CustomerA));
            leg = (await LegIdsOfOrderAsync(order))[0];
            trip = await TripWithAsync(f.Type, At(6), At(18), [f.Tractor], [f.Driver1]);
        }

        using (InTenantB())
        {
            await TripTypeAsync();
            var stamp = trip.ConcurrencyStamp;
            await Should.ThrowAsync<EntityNotFoundException>(() => Planboard.RescheduleAsync(trip.Id, new PlanboardRescheduleDto { PlannedDeparture = At(8), ConcurrencyStamp = stamp }));
            await Should.ThrowAsync<EntityNotFoundException>(() => Planboard.ChangeResourceAsync(trip.Id, new PlanboardChangeResourceDto { Mode = PlanboardResourceMode.Driver, ToResourceId = f.Driver2, ConcurrencyStamp = stamp }));
            await Should.ThrowAsync<EntityNotFoundException>(() => Planboard.AddDriverAsync(trip.Id, new PlanboardAddDriverDto { DriverId = f.Driver2, ConcurrencyStamp = stamp }));
            await Should.ThrowAsync<EntityNotFoundException>(() => Planboard.SetFieldAsync(trip.Id, new PlanboardUpdateFieldDto { Field = PlanboardTripField.Name, Value = "x", ConcurrencyStamp = stamp }));
            await Should.ThrowAsync<EntityNotFoundException>(() => Planboard.AssignLegsAsync(trip.Id, new PlanboardLegsDto { LegIds = [leg], ConcurrencyStamp = stamp }));
            await Should.ThrowAsync<EntityNotFoundException>(() => Planboard.CompleteAsync(trip.Id, new PlanboardCompleteDto { ActualDeparture = At(6), ActualArrival = At(9), ConcurrencyStamp = stamp }));

            /* A trip of tenant B cannot take tenant A's legs or resources either. */
            var own = await CreateTripAsync(await TripTypeAsync(), At(6), At(18));
            await Should.ThrowAsync<Exception>(() => Planboard.AssignLegsAsync(own.Id, new PlanboardLegsDto { LegIds = [leg], ConcurrencyStamp = own.ConcurrencyStamp }));
            (await Should.ThrowAsync<BusinessException>(() => Planboard.ChangeResourceAsync(own.Id, new PlanboardChangeResourceDto { Mode = PlanboardResourceMode.Tractor, ToResourceId = f.Tractor, ConcurrencyStamp = own.ConcurrencyStamp })))
                .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
            (await Should.ThrowAsync<BusinessException>(() => Planboard.CreateTripAsync(new PlanboardCreateTripDto { Mode = PlanboardResourceMode.Driver, ResourceId = f.Driver1, PlannedDeparture = At(5) })))
                .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        }

        using (InTenantA())
        {
            var untouched = await Trips.GetAsync(trip.Id);
            untouched.ConcurrencyStamp.ShouldBe(trip.ConcurrencyStamp);
            untouched.Drivers.Select(d => d.DriverId).ShouldBe([f.Driver1]);
            (await LegRepository.GetAsync(leg)).TripId.ShouldBeNull();
        }
    }
}
