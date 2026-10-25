using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData.Fleet;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.MasterData.Relations;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// TransportTripAppService on SQLite (design 59 "Lifecycle and commands", "Stop composition"; trips.md 3, 7–15, 20–28,
/// 46–49, 56): numbering, header rules, vehicles and drivers, assignment with both composition modes, manual stops,
/// renumbering, the status machine with every refusal, the four leg operations, the list and tenant isolation.
/// Risk class: Critical.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class TransportTripAppServiceTests : PlanningAppServiceTestBase
{
    private async Task<(Guid Type, (Guid, Guid) Depot, (Guid, Guid) CustomerA, (Guid, Guid) CustomerB, Guid Customer)> FixturesAsync()
    {
        var type = await TripTypeAsync();
        var depot = await NamedPlaceAsync("DEPOT", "Depot Utrecht", 52.09, 5.12, RelationRoles.Depot | RelationRoles.OwnCompany, "Utrecht");
        var a = await NamedPlaceAsync("CUSTA", "Customer A", 52.37, 4.90, RelationRoles.Customer, "Amsterdam");
        var b = await NamedPlaceAsync("CUSTB", "Customer B", 51.92, 4.48, RelationRoles.Customer, "Rotterdam");
        return (type, depot, a, b, a.RelationId);
    }

    /// <summary>The order's stops carry the relation names and cities of the fixtures (the legs snapshot them).</summary>
    private readonly Dictionary<Guid, (string Name, string City, double Lat, double Lon)> _places = new();

    private async Task<(Guid RelationId, Guid AddressId)> NamedPlaceAsync(string code, string name, double lat, double lon, RelationRoles roles, string city)
    {
        var place = await PlaceAsync(code, name, lat, lon, roles, city);
        _places[place.RelationId] = (name, city, lat, lon);
        return place;
    }

    private PricingLineDto Line((Guid, Guid) from, (Guid, Guid) to, decimal quantity = 10, int fromHour = 8, int toHour = 12, int day = 26, double? weightKg = 1000)
    {
        var f = _places[from.Item1];
        var t = _places[to.Item1];
        return LineOf(quantity, weightKg,
            StopAt(from, Load, new DateOnly(2026, 10, day), f.Name, f.Lat, f.Lon, At(fromHour, 0, day), At(fromHour + 2, 0, day), city: f.City),
            StopAt(to, Unload, new DateOnly(2026, 10, day), t.Name, t.Lat, t.Lon, At(toHour, 0, day), At(toHour + 2, 0, day), city: t.City));
    }

    [Fact]
    public async Task Create_draws_a_number_from_the_types_sequence_takes_the_types_defaults_and_requests_a_calculation()
    {
        using var _ = InTenantA();
        var (type, depot, _, _, _) = await FixturesAsync();
        var carrier = await PlaceAsync("CARR", "Carrier BV", 52, 5, RelationRoles.Carrier);

        var trip = await Trips.CreateAsync(new TransportTripCreateDto
        {
            TransportTripTypeId = type,
            PlannedDeparture = At(6),
            PlannedArrival = At(18),
            Name = "Monday run",
            CarrierRelationId = carrier.RelationId,
            DepartureRelationId = depot.Item1,
            DepartureAddressId = depot.Item2,
            Attributes = new Dictionary<string, string> { ["Colour"] = "blue" }
        });

        trip.Number.ShouldBe("TR-2026-00001");
        trip.TripDate.ShouldBe(Day);
        trip.Status.ShouldBe(TransportTripStatus.Planned);
        trip.Execution.ShouldBe(TripExecution.OwnFleet, "the type's default");
        trip.CarrierName.ShouldBe("Carrier BV");
        trip.DepartureRelationName.ShouldBe("Depot Utrecht");
        trip.Attributes.ShouldBe(new Dictionary<string, string> { ["Colour"] = "blue" });
        trip.Stops.ShouldBeEmpty();
        trip.TransportTripTypeName.ShouldBe("STD");

        var second = await CreateTripAsync(type);
        second.Number.ShouldBe("TR-2026-00002");
        Requests.For(PlanningExecutionApplicationModule.TripTargetType, trip.Id).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Create_refuses_unknown_type_relations_vehicles_and_drivers()
    {
        using var _ = InTenantA();
        var (type, _, _, _, _) = await FixturesAsync();

        (await Should.ThrowAsync<BusinessException>(() => CreateTripAsync(Guid.NewGuid()))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => Trips.CreateAsync(new TransportTripCreateDto { TransportTripTypeId = type, PlannedDeparture = At(6), CarrierRelationId = Guid.NewGuid() })))
            .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => Trips.CreateAsync(new TransportTripCreateDto { TransportTripTypeId = type, PlannedDeparture = At(6), VehicleIds = [Guid.NewGuid()] })))
            .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => Trips.CreateAsync(new TransportTripCreateDto { TransportTripTypeId = type, PlannedDeparture = At(6), DriverIds = [Guid.NewGuid()] })))
            .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => Trips.CreateAsync(new TransportTripCreateDto { TransportTripTypeId = type, PlannedDeparture = At(6), PlannedArrival = At(5) })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.ArrivalBeforeDeparture);
    }

    [Fact]
    public async Task Vehicles_drivers_and_carrier_commands_name_the_master_data_rows_and_a_combination_lists_its_members()
    {
        using var _ = InTenantA();
        var (type, depot, _, _, _) = await FixturesAsync();
        var tractorType = await VehicleTypeAsync("Tractor", VehicleTypeKind.Tractor);
        var trailerType = await VehicleTypeAsync("Trailer", VehicleTypeKind.Trailer, maxEpq: 33, maxLoadingMeters: 13.6);
        var tractor = await VehicleAsync("Truck 01", tractorType, depot.Item1);
        var trailer = await VehicleAsync("Trailer 01", trailerType, depot.Item1, grossKg: 24_000, maxPayloadKg: 24_000, euroClass: null);
        var combination = await CombinationAsync("Combi 1", tractor, trailer);
        var driver = await DriverAsync("D1", "Jan", "Jansen");
        var trip = await CreateTripAsync(type);

        trip = await Trips.SetVehiclesAsync(trip.Id, new TripVehiclesDto { VehicleIds = [trailer, tractor] });
        trip.Vehicles.Select(v => v.Name).ShouldBe(["Trailer 01", "Truck 01"]);
        trip.Vehicles.Select(v => v.Position).ShouldBe([1, 2]);

        trip = await Trips.SetVehiclesAsync(trip.Id, new TripVehiclesDto { VehicleCombinationId = combination });
        trip.VehicleCombinationName.ShouldBe("Combi 1");
        trip.Vehicles.Select(v => v.Name).ShouldBe(["Truck 01", "Trailer 01"], "the combination's members in position order");

        (await Should.ThrowAsync<BusinessException>(() => Trips.SetVehiclesAsync(trip.Id, new TripVehiclesDto { VehicleCombinationId = combination, VehicleIds = [tractor] })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.VehicleSourceConflict);

        trip = await Trips.SetDriversAsync(trip.Id, new TripDriversDto { DriverIds = [driver] });
        trip.Drivers.ShouldHaveSingleItem().Name.ShouldBe("Jan Jansen");

        var carrier = await PlaceAsync("CARR", "Carrier BV", 52, 5, RelationRoles.Carrier);
        trip = await Trips.SetCarrierAsync(trip.Id, new TripCarrierDto { CarrierRelationId = carrier.RelationId });
        trip.CarrierName.ShouldBe("Carrier BV");
        trip = await Trips.SetCarrierAsync(trip.Id, new TripCarrierDto());
        trip.CarrierRelationId.ShouldBeNull();
        trip.CarrierName.ShouldBeNull();

        var rows = await Trips.GetListAsync(TripList(search: "combi"));
        rows.Rows.ShouldHaveSingleItem().Vehicles.ShouldBe(["Truck 01", "Trailer 01"]);
    }

    [Fact]
    public async Task Update_changes_the_header_keeps_the_number_and_refuses_a_date_outside_the_numbering_period_and_a_stale_stamp()
    {
        using var _ = InTenantA();
        var (type, depot, _, _, _) = await FixturesAsync();
        var trip = await CreateTripAsync(type);

        var update = UpdateFrom(trip);
        update.Name = "Renamed";
        update.PlannedDeparture = At(7);
        update.PlannedArrival = At(19);
        update.ArrivalRelationId = depot.Item1;
        update.ArrivalAddressId = depot.Item2;
        update.Remarks = "Take the ring road";
        update.ActualDistanceM = 123_000;
        update.TripDate = new DateOnly(2026, 11, 2);
        update.Attributes["Door"] = "7";
        var updated = await Trips.UpdateAsync(trip.Id, update);

        updated.Number.ShouldBe(trip.Number);
        updated.Name.ShouldBe("Renamed");
        updated.TripDate.ShouldBe(new DateOnly(2026, 11, 2), "the same year under a yearly numbering");
        updated.ArrivalRelationName.ShouldBe("Depot Utrecht");
        updated.ActualDistanceM.ShouldBe(123_000);
        updated.Attributes.ShouldContainKeyAndValue("Door", "7");

        var otherYear = UpdateFrom(updated);
        otherYear.TripDate = new DateOnly(2027, 1, 5);
        (await Should.ThrowAsync<BusinessException>(() => Trips.UpdateAsync(trip.Id, otherYear))).Code.ShouldBe(PlanningExecutionErrorCodes.TripDateLocked);

        var stale = UpdateFrom(updated);
        stale.ConcurrencyStamp = trip.ConcurrencyStamp;
        stale.Name = "Stale";
        await Should.ThrowAsync<Volo.Abp.Data.AbpDbConcurrencyException>(() => Trips.UpdateAsync(trip.Id, stale));
    }

    [Fact]
    public async Task Assigning_legs_in_merge_mode_shares_stops_moving_them_rebuilds_both_trips_and_unassigning_returns_them_to_the_unplanned_list()
    {
        using var _ = InTenantA();
        var (type, depot, a, b, _) = await FixturesAsync();
        var order1 = await ConfirmOrderAsync(Order(a.Item1, Line(depot, a), Line(depot, b, toHour: 14)));
        var order2 = await ConfirmOrderAsync(Order(b.Item1, Line(depot, b, quantity: 4, toHour: 15)));
        var legs1 = await LegIdsOfOrderAsync(order1);
        var legs2 = await LegIdsOfOrderAsync(order2);
        var trip = await CreateTripAsync(type, At(6), At(18));
        var generated = legs1.Concat(legs2).ToHashSet();
        Requests.All.Count(r => r.TargetType == PlanningExecutionApplicationModule.LegTargetType && generated.Contains(r.TargetId)).ShouldBe(3, "every generated leg is routed");

        trip = await Trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = legs1.Concat(legs2).ToList() });

        trip.Stops.Count.ShouldBe(3, "three loads at the depot share one stop; two unloads at B share one");
        trip.Stops.Select(s => s.Position).ShouldBe([1, 2, 3]);
        trip.Stops[0].Name.ShouldBe("Depot Utrecht");
        trip.Stops[0].Legs.Count.ShouldBe(3);
        trip.Stops[0].Legs.ShouldAllBe(l => l.Role == StopLegRole.Origin);
        trip.Stops[0].PlannedDurationMin.ShouldBe(30);
        trip.Stops[1].Name.ShouldBe("Customer A");
        trip.Stops[2].Name.ShouldBe("Customer B");
        trip.Stops[2].Legs.Count.ShouldBe(2);
        trip.Stops[2].Legs.Select(l => l.CustomerName).Distinct().Count().ShouldBe(2);
        (await Legs.GetListAsync(LegList(tripId: trip.Id))).Rows.Select(l => l.TripPosition).ShouldBe([1, 2, 3]);
        (await Legs.GetListAsync(LegList())).Rows.ShouldBeEmpty("nothing is unplanned");

        var summary = await Trips.GetSummaryAsync(trip.Id);
        summary.LegCount.ShouldBe(3);
        summary.OrderCount.ShouldBe(2);
        summary.TotalQuantity.ShouldBe(24);

        /* Move order 2's leg to another trip: B's stop stays for order 1's leg, the target gets its pair. */
        var other = await CreateTripAsync(type, At(6), At(18));
        trip = await Trips.MoveLegsAsync(trip.Id, new MoveLegsDto { LegIds = legs2, ToTripId = other.Id });
        trip.Stops.Count.ShouldBe(3);
        trip.Stops[2].Legs.ShouldHaveSingleItem();
        other = await Trips.GetAsync(other.Id);
        other.Stops.Count.ShouldBe(2);
        other.Stops.Select(s => s.Name).ShouldBe(["Depot Utrecht", "Customer B"]);
        Requests.For(PlanningExecutionApplicationModule.TripTargetType, other.Id).ShouldNotBeEmpty();

        /* Unassign everything from the first trip. */
        trip = await Trips.UnassignLegsAsync(trip.Id, new UnassignLegsDto { LegIds = legs1 });
        trip.Stops.ShouldBeEmpty();
        var unplanned = await Legs.GetListAsync(LegList());
        unplanned.Rows.Select(l => l.Id).ShouldBe(legs1, ignoreOrder: true);
        unplanned.Rows.ShouldAllBe(l => l.TripId == null && l.TripPosition == null);
        (await Legs.GetListAsync(LegList(orderId: order2))).Rows.ShouldHaveSingleItem().TripNumber.ShouldBe(other.Number);
    }

    [Fact]
    public async Task Shuttle_mode_gives_every_leg_its_own_pair_and_the_first_last_stop_rule_sets_departure_and_arrival()
    {
        using var _ = InTenantA();
        var (type, depot, a, _, _) = await FixturesAsync();
        var order = await ConfirmOrderAsync(Order(a.Item1, Line(depot, a), Line(depot, a, toHour: 15)));
        var trip = await CreateTripAsync(type, At(6), At(18), StopCompositionMode.OneStopPairPerLeg, FirstLastStopRule.Both);

        trip = await Trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = await LegIdsOfOrderAsync(order) });

        trip.Stops.Count.ShouldBe(4);
        trip.Stops.Select(s => s.Name).ShouldBe(["Depot Utrecht", "Customer A", "Depot Utrecht", "Customer A"]);
        trip.DepartureRelationId.ShouldBe(depot.Item1);
        trip.DepartureAddressId.ShouldBe(depot.Item2);
        trip.ArrivalRelationId.ShouldBe(a.Item1);
    }

    [Fact]
    public async Task Assignment_refusals_other_trip_outside_window_unassignable_trip_cancelled_order_and_unknown_leg()
    {
        using var _ = InTenantA();
        var (type, depot, a, _, _) = await FixturesAsync();
        var order = await ConfirmOrderAsync(Order(a.Item1, Line(depot, a)));
        var legs = await LegIdsOfOrderAsync(order);
        var trip = await CreateTripAsync(type, At(6), At(18));
        var other = await CreateTripAsync(type, At(6), At(18));
        await Trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = legs });

        (await Should.ThrowAsync<BusinessException>(() => Trips.AssignLegsAsync(other.Id, new AssignLegsDto { LegIds = legs })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.LegOnOtherTrip);
        (await Should.ThrowAsync<BusinessException>(() => Trips.UnassignLegsAsync(other.Id, new UnassignLegsDto { LegIds = legs })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.LegNotOnTrip);
        (await Should.ThrowAsync<BusinessException>(() => Trips.MoveLegsAsync(trip.Id, new MoveLegsDto { LegIds = legs, ToTripId = trip.Id })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.SameTrip);
        await Should.ThrowAsync<EntityNotFoundException>(() => Trips.AssignLegsAsync(other.Id, new AssignLegsDto { LegIds = [Guid.NewGuid()] }));

        /* Outside the window: refused, unless the planner confirms. */
        await Trips.UnassignLegsAsync(trip.Id, new UnassignLegsDto { LegIds = legs });
        var tomorrow = await CreateTripAsync(type, At(6, 0, 28), At(18, 0, 28));
        var outside = await Should.ThrowAsync<BusinessException>(() => Trips.AssignLegsAsync(tomorrow.Id, new AssignLegsDto { LegIds = legs }));
        outside.Code.ShouldBe(PlanningExecutionErrorCodes.LegOutsideTripWindow);
        (await Trips.AssignLegsAsync(tomorrow.Id, new AssignLegsDto { LegIds = legs, AllowOutsideWindow = true })).Stops.Count.ShouldBe(2);

        /* A cancelled trip takes nothing; a cancelled order's legs cannot be planned. */
        await Trips.UnassignLegsAsync(tomorrow.Id, new UnassignLegsDto { LegIds = legs });
        await Trips.CancelAsync(other.Id);
        (await Should.ThrowAsync<BusinessException>(() => Trips.AssignLegsAsync(other.Id, new AssignLegsDto { LegIds = legs })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.TripNotAssignable);
        World.OrderReferences[(CurrentTenant.Id, order)].Status = global::OpenTms.Orders.TransportOrders.TransportOrderStatus.Cancelled;
        (await Should.ThrowAsync<BusinessException>(() => Trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = legs })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.OrderCancelled);
    }

    [Fact]
    public async Task Manual_stops_are_added_moved_and_removed_with_renumbering_and_survive_the_composition()
    {
        using var _ = InTenantA();
        var (type, depot, a, _, _) = await FixturesAsync();
        var fuel = await PlaceAsync("FUEL", "Fuel station", 52.2, 5.0, RelationRoles.Depot, "Hilversum");
        var order = await ConfirmOrderAsync(Order(a.Item1, Line(depot, a)));
        var trip = await CreateTripAsync(type, At(6), At(18));
        trip = await Trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = await LegIdsOfOrderAsync(order) });

        trip = await Trips.AddStopAsync(trip.Id, new AddManualStopDto { WaypointTypeId = Guid.NewGuid(), RelationId = fuel.RelationId, Position = 2, PlannedDurationMin = 15, Remarks = "Refuel" });
        trip.Stops.Select(s => s.Name).ShouldBe(["Depot Utrecht", "Fuel station", "Customer A"]);
        var fuelStop = trip.Stops[1];
        fuelStop.IsManual.ShouldBeTrue();
        fuelStop.City.ShouldBe("Hilversum");
        fuelStop.Latitude.ShouldBe(52.2);
        fuelStop.Remarks.ShouldBe("Refuel");

        trip = await Trips.UpdateStopAsync(trip.Id, fuelStop.Id, new UpdateStopDto { Position = 3 });
        trip.Stops.Select(s => s.Name).ShouldBe(["Depot Utrecht", "Customer A", "Fuel station"]);
        trip.Stops.Select(s => s.Position).ShouldBe([1, 2, 3]);

        (await Should.ThrowAsync<BusinessException>(() => Trips.DeleteStopAsync(trip.Id, trip.Stops[0].Id))).Code.ShouldBe(PlanningExecutionErrorCodes.NotManualStop);
        (await Should.ThrowAsync<BusinessException>(() => Trips.UpdateStopAsync(trip.Id, fuelStop.Id, new UpdateStopDto { Position = 9 }))).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidPosition);

        trip = await Trips.RecordStopActualsAsync(trip.Id, fuelStop.Id, new StopActualsDto { ActualArrival = At(13), CumulativeDistanceM = 80_000 });
        trip.Stops[2].ActualArrival.ShouldBe(At(13));
        trip.Stops[2].ActualCumulativeDistanceM.ShouldBe(80_000);
        (await Should.ThrowAsync<BusinessException>(() => Trips.UpdateStopAsync(trip.Id, fuelStop.Id, new UpdateStopDto { Position = 1 }))).Code.ShouldBe(PlanningExecutionErrorCodes.StopHasActuals);
        (await Should.ThrowAsync<BusinessException>(() => Trips.DeleteStopAsync(trip.Id, fuelStop.Id))).Code.ShouldBe(PlanningExecutionErrorCodes.StopHasActuals);

        var second = await Trips.AddStopAsync(trip.Id, new AddManualStopDto { WaypointTypeId = Guid.NewGuid(), RelationId = fuel.RelationId });
        second.Stops.Count.ShouldBe(4);
        trip = await Trips.DeleteStopAsync(trip.Id, second.Stops[3].Id);
        trip.Stops.Count.ShouldBe(3);

        /* The composition after an unassign keeps the manual stop and renumbers. */
        trip = await Trips.UnassignLegsAsync(trip.Id, new UnassignLegsDto { LegIds = await LegIdsOfOrderAsync(order) });
        trip.Stops.ShouldHaveSingleItem().Name.ShouldBe("Fuel station");
        trip.Stops[0].Position.ShouldBe(1);
    }

    [Fact]
    public async Task The_status_machine_start_complete_cancel_archive_delete_with_every_refusal()
    {
        using var _ = InTenantA();
        var (type, depot, a, _, _) = await FixturesAsync();
        var order = await ConfirmOrderAsync(Order(a.Item1, Line(depot, a)));
        var legs = await LegIdsOfOrderAsync(order);
        var trip = await CreateTripAsync(type, At(6), At(18));
        trip = await Trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = legs });

        (await Should.ThrowAsync<BusinessException>(() => Trips.CompleteAsync(trip.Id, new TripTimestampDto()))).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidTransition);
        (await Should.ThrowAsync<BusinessException>(() => Trips.ArchiveAsync(trip.Id))).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidTransition);

        trip = await Trips.StartAsync(trip.Id, new TripTimestampDto { At = At(6, 10) });
        trip.Status.ShouldBe(TransportTripStatus.InProgress);
        trip.ActualDeparture.ShouldBe(At(6, 10));
        (await Should.ThrowAsync<BusinessException>(() => Trips.StartAsync(trip.Id, new TripTimestampDto()))).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidTransition);
        (await Should.ThrowAsync<BusinessException>(() => Trips.DeleteAsync(trip.Id))).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidTransition);

        trip = await Trips.CompleteAsync(trip.Id, new TripTimestampDto { At = At(17) });
        trip.Status.ShouldBe(TransportTripStatus.Completed);
        trip.ActualArrival.ShouldBe(At(17));
        trip.Stops.ShouldAllBe(s => s.ActualArrival == At(17) && s.ActualDeparture == At(17));
        (await Should.ThrowAsync<BusinessException>(() => Trips.CancelAsync(trip.Id))).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidTransition);
        (await Should.ThrowAsync<BusinessException>(() => Trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = legs }))).Code.ShouldBe(PlanningExecutionErrorCodes.TripNotAssignable);

        trip = await Trips.ArchiveAsync(trip.Id);
        trip.IsArchived.ShouldBeTrue();
        (await Should.ThrowAsync<BusinessException>(() => Trips.SetDriversAsync(trip.Id, new TripDriversDto()))).Code.ShouldBe(PlanningExecutionErrorCodes.TripArchived);
        (await Trips.GetListAsync(TripList())).Rows.ShouldNotContain(r => r.Id == trip.Id, "archived trips are hidden by default");
        (await Trips.GetListAsync(TripList(archived: true))).Rows.ShouldContain(r => r.Id == trip.Id);
        trip = await Trips.UnarchiveAsync(trip.Id);
        trip.IsArchived.ShouldBeFalse();

        /* Cancel a planned trip with legs: the legs are unassigned; delete a planned trip likewise. */
        var cancelled = await CreateTripAsync(type, At(6), At(18));
        var order2 = await ConfirmOrderAsync(Order(a.Item1, Line(depot, a)));
        await Trips.AssignLegsAsync(cancelled.Id, new AssignLegsDto { LegIds = await LegIdsOfOrderAsync(order2) });
        cancelled = await Trips.CancelAsync(cancelled.Id);
        cancelled.Status.ShouldBe(TransportTripStatus.Cancelled);
        cancelled.Stops.ShouldBeEmpty();
        (await LegsOfOrderAsync(order2)).ShouldAllBe(l => l.TripId == null);
        (await Should.ThrowAsync<BusinessException>(() => Trips.DeleteAsync(cancelled.Id))).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidTransition);

        var deletable = await CreateTripAsync(type, At(6), At(18));
        await Trips.AssignLegsAsync(deletable.Id, new AssignLegsDto { LegIds = await LegIdsOfOrderAsync(order2) });
        await Trips.DeleteAsync(deletable.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => Trips.GetAsync(deletable.Id));
        (await LegsOfOrderAsync(order2)).ShouldAllBe(l => l.TripId == null);
    }

    [Fact]
    public async Task Split_return_carry_out_separately_and_outsource_origin_reshape_the_legs()
    {
        using var _ = InTenantA();
        var (type, depot, a, _, _) = await FixturesAsync();
        var hub = await PlaceAsync("HUB", "Hub Breda", 51.59, 4.78, RelationRoles.Depot, "Breda");
        var order = await ConfirmOrderAsync(Order(a.Item1, Line(depot, a, quantity: 10, weightKg: 1000)));
        var legId = (await LegIdsOfOrderAsync(order)).Single();
        var trip = await CreateTripAsync(type, At(6), At(18));
        var shuttle = await CreateTripAsync(type, At(6), At(18));
        trip = await Trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = [legId] });

        /* 20–21: split 60/40, the clone lands on the other trip; amounts outside the leg are refused. */
        (await Should.ThrowAsync<BusinessException>(() => Trips.SplitLineAsync(trip.Id, new SplitLineDto { LegId = legId, Quantity = 10 }))).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidSplit);
        var clone = await Trips.SplitLineAsync(trip.Id, new SplitLineDto { LegId = legId, Quantity = 4, ToTripId = shuttle.Id });
        clone.Quantity.ShouldBe(4);
        clone.WeightKg.ShouldBe(400);
        clone.Source.ShouldBe(LegSource.Split);
        clone.SplitFromLegId.ShouldBe(legId);
        clone.TripId.ShouldBe(shuttle.Id);
        clone.OrderLineId.ShouldBe((await Legs.GetAsync(legId)).OrderLineId);
        var original = await Legs.GetAsync(legId);
        original.Quantity.ShouldBe(6);
        original.WeightKg.ShouldBe(600);
        (await Trips.GetAsync(shuttle.Id)).Stops.Count.ShouldBe(2);

        /* 22: a return leg mirrors the original on the same trip; a second one is refused. */
        var back = await Trips.ReturnLegAsync(trip.Id, new LegReferenceDto { LegId = legId });
        back.Source.ShouldBe(LegSource.Return);
        back.Origin.RelationId.ShouldBe(a.Item1);
        back.Destination.RelationId.ShouldBe(depot.Item1);
        back.TripId.ShouldBe(trip.Id);
        back.Position.ShouldBe(2);
        (await Should.ThrowAsync<BusinessException>(() => Trips.ReturnLegAsync(trip.Id, new LegReferenceDto { LegId = legId }))).Code.ShouldBe(PlanningExecutionErrorCodes.ReturnLegExists);
        trip = await Trips.GetAsync(trip.Id);
        trip.Stops.Count.ShouldBe(4, "Load depot, Unload A, Load A, Unload depot (different activities, no merge)");

        /* 23: carry out separately. */
        trip = await Trips.CarryOutSeparatelyAsync(trip.Id, new LegReferenceDto { LegId = back.Id });
        trip.Stops.Count.ShouldBe(4);
        trip.Stops.Skip(2).ShouldAllBe(s => s.Legs.Single().LegId == back.Id && s.Legs.Single().IsCarriedOutSeparately);

        /* 24: outsource the origin activity of the original leg to a hub trip. */
        var hubTrip = await CreateTripAsync(type, At(6), At(18));
        var hubLeg = await Trips.OutsourceOriginAsync(trip.Id, new OutsourceOriginDto { LegId = legId, HubRelationId = hub.RelationId, ToTripId = hubTrip.Id });
        hubLeg.Source.ShouldBe(LegSource.Outsourced);
        hubLeg.Origin.RelationId.ShouldBe(depot.Item1);
        hubLeg.Destination.RelationId.ShouldBe(hub.RelationId);
        hubLeg.Destination.City.ShouldBe("Breda");
        hubLeg.TripId.ShouldBe(hubTrip.Id);
        original = await Legs.GetAsync(legId);
        original.Origin.RelationId.ShouldBe(hub.RelationId);
        original.Origin.StopId.ShouldBe(hubLeg.Destination.StopId, "the hub joins the two legs");
        original.Source.ShouldBe(LegSource.Outsourced);
        original.TripId.ShouldBe(trip.Id, "the original stays on its trip");
        trip = await Trips.GetAsync(trip.Id);
        trip.Stops.Select(s => s.Name).ShouldContain("Hub Breda");
        (await Should.ThrowAsync<BusinessException>(() => Trips.OutsourceOriginAsync(trip.Id, new OutsourceOriginDto { LegId = legId, HubRelationId = hub.RelationId, ToTripId = trip.Id }))).Code.ShouldBe(PlanningExecutionErrorCodes.SameTrip);

        /* The reshaped line survives a regeneration of the order. */
        await Handler.HandleEventAsync(new global::OpenTms.Orders.Events.TransportOrderStopsChangedEto { OrderId = order });
        (await LegsOfOrderAsync(order)).Count.ShouldBe(4);
        (await Legs.GetAsync(clone.Id)).Quantity.ShouldBe(4);
    }

    [Fact]
    public async Task The_list_filters_by_view_status_date_carrier_and_search_hides_empty_trips_and_sorts()
    {
        using var _ = InTenantA();
        var (type, depot, a, _, _) = await FixturesAsync();
        var carrier = await PlaceAsync("CARR", "Carrier BV", 52, 5, RelationRoles.Carrier);
        var order = await ConfirmOrderAsync(Order(a.Item1, Line(depot, a)));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var planned = await CreateTripAsync(type, new DateTimeOffset(today.ToDateTime(new TimeOnly(6, 0)), TimeSpan.Zero), name: "Alpha");
        await Trips.AssignLegsAsync(planned.Id, new AssignLegsDto { LegIds = await LegIdsOfOrderAsync(order), AllowOutsideWindow = true });
        var empty = await CreateTripAsync(type, At(6, 0, 20), name: "Beta");
        await Trips.SetCarrierAsync(empty.Id, new TripCarrierDto { CarrierRelationId = carrier.RelationId });
        var cancelled = await CreateTripAsync(type, At(6, 0, 21), name: "Gamma");
        await Trips.CancelAsync(cancelled.Id);

        (await Trips.GetListAsync(TripList())).Rows.Count.ShouldBe(3);
        (await Trips.GetListAsync(TripList(view: "today"))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Alpha");
        (await Trips.GetListAsync(TripList(view: "open"))).Rows.Select(r => r.Name).ShouldBe(["Beta", "Alpha"], "by planned departure");
        (await Trips.GetListAsync(TripList(status: [TransportTripStatus.Cancelled]))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Gamma");
        (await Trips.GetListAsync(TripList(hideEmpty: true))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Alpha");
        (await Trips.GetListAsync(TripList(carrierRelationId: carrier.RelationId))).Rows.ShouldHaveSingleItem().CarrierName.ShouldBe("Carrier BV");
        (await Trips.GetListAsync(TripList(dateFrom: new DateOnly(2026, 10, 21), dateTo: new DateOnly(2026, 10, 21)))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Gamma");
        (await Trips.GetListAsync(TripList(search: "carrier"))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Beta");
        (await Trips.GetListAsync(TripList(search: "amsterdam"))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Alpha", "stop cities are searchable");
        (await Trips.GetListAsync(TripList(search: "customer"))).Rows.ShouldHaveSingleItem().Customers.ShouldHaveSingleItem();
        (await Trips.GetListAsync(TripList(sortBy: "name", sortDir: "desc"))).Rows.Select(r => r.Name).ShouldBe(["Gamma", "Beta", "Alpha"]);
        (await Trips.GetListAsync(TripList(sortBy: "number"))).Rows.Select(r => r.Number).ShouldBe(["TR-2026-00001", "TR-2026-00002", "TR-2026-00003"]);
        (await Should.ThrowAsync<BusinessException>(() => Trips.GetListAsync(TripList(sortBy: "nope")))).Code.ShouldBe(ReferenceDataErrorCodes.UnknownSortColumn);

        var row = (await Trips.GetListAsync(TripList(view: "today"))).Rows.Single();
        row.StopCount.ShouldBe(2);
        row.LegCount.ShouldBe(1);
        row.FirstStopCity.ShouldBe("Utrecht");
        row.LastStopCity.ShouldBe("Amsterdam");
        row.OrderNumbers.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Another_tenant_sees_no_trips_legs_or_stops_and_cannot_touch_them()
    {
        Guid tripId;
        List<Guid> legs;
        using (InTenantA())
        {
            var (type, depot, a, _, _) = await FixturesAsync();
            var order = await ConfirmOrderAsync(Order(a.Item1, Line(depot, a)));
            legs = await LegIdsOfOrderAsync(order);
            var trip = await CreateTripAsync(type, At(6), At(18));
            await Trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = legs });
            tripId = trip.Id;
        }

        using var _ = InTenantB();
        (await Trips.GetListAsync(TripList())).Rows.ShouldBeEmpty();
        (await Legs.GetListAsync(LegList())).Rows.ShouldBeEmpty();
        (await Legs.GetListAsync(LegList(tripId: tripId))).Rows.ShouldBeEmpty();
        await Should.ThrowAsync<EntityNotFoundException>(() => Trips.GetAsync(tripId));
        await Should.ThrowAsync<EntityNotFoundException>(() => Trips.GetSummaryAsync(tripId));
        await Should.ThrowAsync<EntityNotFoundException>(() => Legs.GetAsync(legs[0]));
        await Should.ThrowAsync<EntityNotFoundException>(() => Trips.CancelAsync(tripId));
        var typeB = await TripTypeAsync();
        var tripB = await CreateTripAsync(typeB, At(6), At(18));
        await Should.ThrowAsync<EntityNotFoundException>(() => Trips.AssignLegsAsync(tripB.Id, new AssignLegsDto { LegIds = legs, AllowOutsideWindow = true }));
        (await GetRequiredService<OpenTms.PlanningExecution.Published.ILegReader>().GetByTripAsync(tripId)).ShouldBeEmpty();
        (await GetRequiredService<OpenTms.PlanningExecution.Published.ITransportTripLookup>().GetAsync([tripId])).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_published_readers_return_the_stored_values_of_the_tenants_legs_and_trips()
    {
        using var _ = InTenantA();
        var (type, depot, a, _, _) = await FixturesAsync();
        var tractorType = await VehicleTypeAsync("Tractor", VehicleTypeKind.Tractor);
        var tractor = await VehicleAsync("Truck 07", tractorType, depot.Item1);
        var driver = await DriverAsync("D7", "Piet", "Pietersen");
        var order = await ConfirmOrderAsync(Order(a.Item1, Line(depot, a)));
        var trip = await CreateTripAsync(type, At(6), At(18));
        await Trips.SetVehiclesAsync(trip.Id, new TripVehiclesDto { VehicleIds = [tractor] });
        await Trips.SetDriversAsync(trip.Id, new TripDriversDto { DriverIds = [driver] });
        await Trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = await LegIdsOfOrderAsync(order) });

        var legs = await GetRequiredService<OpenTms.PlanningExecution.Published.ILegReader>().GetByOrderAsync(order);
        var leg = legs.ShouldHaveSingleItem();
        leg.TripNumber.ShouldBe(trip.Number);
        leg.TripStatus.ShouldBe(TransportTripStatus.Planned);
        leg.Origin.RelationId.ShouldBe(depot.Item1);
        leg.Destination.City.ShouldBe("Amsterdam");
        leg.Quantity.ShouldBe(10);
        (await GetRequiredService<OpenTms.PlanningExecution.Published.ILegReader>().GetByTripAsync(trip.Id)).ShouldHaveSingleItem().Id.ShouldBe(leg.Id);
        (await GetRequiredService<OpenTms.PlanningExecution.Published.ILegReader>().GetAsync([leg.Id, Guid.NewGuid()])).ShouldHaveSingleItem();

        var reference = (await GetRequiredService<OpenTms.PlanningExecution.Published.ITransportTripLookup>().GetAsync([trip.Id])).ShouldHaveSingleItem();
        reference.Number.ShouldBe(trip.Number);
        reference.VehicleNames.ShouldBe(["Truck 07"]);
        reference.DriverNames.ShouldBe(["Piet Pietersen"]);
        reference.PlannedDeparture.ShouldBe(At(6));
    }
}
