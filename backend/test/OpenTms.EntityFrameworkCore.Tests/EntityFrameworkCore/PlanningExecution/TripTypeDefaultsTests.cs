using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Relations;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.Localization;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// Design 61 "Trip type defaults" on SQLite. The server fill (point 2) on both paths that create a trip — the trips service
/// (<c>POST /api/planning/transport-trip</c>) and the order's trip request on Confirm (<see cref="OrderLegsHandler"/>) —
/// group by group: an empty group takes the type's, a given value always wins, the "only when it is the type's" rules for
/// the carrier's contact and the departure/arrival address, the planned departure never filled, a vanished, archived or
/// other-tenant default skipped without an error, and the caller's input never changed. The type's validation on save
/// (point 1: every id in the tenant, the structural rules, another relation's address, another tenant's ids) and the
/// names on its detail. The order's trip request always names its departure and arrival (Orders requires both), so the
/// departure/arrival fill runs through the trips service only; on the order path those tests assert that the request's
/// places win. The PO's rules of 2026-10-29: a type's default contact must be its carrier's (refused on save, skipped in the
/// fill when it no longer is), and a carrier filled from the type makes the trip Subcontracted unless an execution is given.
/// Risk class: Critical (the fill, tenant isolation of the fill), High (the type's validation and isolation).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class TripTypeDefaultsTests : PlanningAppServiceTestBase
{
    public enum CreatePath
    {
        AppService,
        OrderConfirm
    }

    private const string NotFound = "(not found)";

    private ITransportTripTypeAppService TripTypes => GetRequiredService<ITransportTripTypeAppService>();

    private IRepository<TransportTripType, Guid> TypeRepository => GetRequiredService<IRepository<TransportTripType, Guid>>();

    private sealed record Carrier(Guid RelationId, Guid AddressId, Guid ContactId);

    private sealed record World(
        (Guid RelationId, Guid AddressId) Depot,
        (Guid RelationId, Guid AddressId) Hub,
        (Guid RelationId, Guid AddressId) Yard,
        (Guid RelationId, Guid AddressId) Customer,
        Carrier Carrier,
        Carrier OtherCarrier,
        Guid Tractor,
        Guid Trailer,
        Guid Van,
        Guid Combination,
        Guid Driver1,
        Guid Driver2,
        Guid Driver3);

    /// <summary>The Master Data of the current tenant every test draws from: four places, two carriers with a contact each, three vehicles and a combination, three drivers.</summary>
    private async Task<World> WorldAsync()
    {
        var depot = await PlaceAsync("DEPOT", "Depot Utrecht", 52.09, 5.12, RelationRoles.Depot | RelationRoles.OwnCompany, "Utrecht");
        var hub = await PlaceAsync("HUB", "Hub Venlo", 51.37, 6.17, RelationRoles.Depot, "Venlo");
        var yard = await PlaceAsync("YARD", "Yard Breda", 51.59, 4.78, RelationRoles.Depot, "Breda");
        var customer = await PlaceAsync("CUST", "Customer Amsterdam", 52.37, 4.90, RelationRoles.Customer, "Amsterdam");
        var carrier = await CarrierAsync("CARR1", "Carrier One BV", "Kees", "Visser");
        var otherCarrier = await CarrierAsync("CARR2", "Carrier Two BV", "Anna", "Bakker");
        var tractorType = await VehicleTypeAsync("Tractor", VehicleTypeKind.Tractor);
        var trailerType = await VehicleTypeAsync("Trailer", VehicleTypeKind.Trailer, maxEpq: 33, maxLoadingMeters: 13.6);
        var vanType = await VehicleTypeAsync("Van", VehicleTypeKind.Rigid, maxEpq: 8);
        var tractor = await Run.VehicleAsync("Truck 01", tractorType, depot.RelationId);
        var trailer = await Run.VehicleAsync("Trailer 01", trailerType, depot.RelationId, grossKg: 24_000, euroClass: null);
        var van = await Run.VehicleAsync("Van 01", vanType, depot.RelationId, grossKg: 3_500);
        var combination = await CombinationAsync("Combi 1", tractor, trailer);
        var driver1 = await DriverAsync("D1", "Jan", "Jansen");
        var driver2 = await DriverAsync("D2", "Piet", "Peters");
        var driver3 = await DriverAsync("D3", "Klaas", "Klaassen");
        return new World(depot, hub, yard, customer, carrier, otherCarrier, tractor, trailer, van, combination, driver1, driver2, driver3);
    }

    private async Task<Carrier> CarrierAsync(string code, string name, string firstName, string lastName)
    {
        var relations = GetRequiredService<IRelationAppService>();
        var created = await relations.CreateAsync(new RelationCreateDto
        {
            Code = code,
            Name = name,
            Roles = RelationRoles.Carrier,
            Addresses =
            [
                new RelationAddressDto
                {
                    CountryId = await CountryAsync(), Street = "Carrier road", HouseNumber = "5", PostalCode = "3500 AA", City = "Utrecht",
                    Latitude = 52.1, Longitude = 5.1, TimeZoneId = "Europe/Amsterdam"
                }
            ],
            ContactPersons = [new RelationContactPersonDto { FirstName = firstName, LastName = lastName, Role = ContactPersonRole.Planning }]
        });
        var detail = await relations.GetAsync(created.Id);
        return new Carrier(created.Id, detail.Addresses.Single().Id!.Value, detail.ContactPersons.Single().Id!.Value);
    }

    private static TransportTripTypeCreateDto NewType(string code, Action<TransportTripTypeCreateDto>? defaults = null)
    {
        var input = new TransportTripTypeCreateDto { Code = code, Names = Names(code), Numbering = Numbering(code) };
        defaults?.Invoke(input);
        return input;
    }

    private Task<TransportTripTypeDto> TypeAsync(string code, Action<TransportTripTypeCreateDto>? defaults = null) => TripTypes.CreateAsync(NewType(code, defaults));

    private static TransportTripTypeUpdateDto UpdateOf(TransportTripTypeDto dto) => new()
    {
        Code = dto.Code,
        Names = dto.Names,
        Description = dto.Description,
        DefaultExecution = dto.DefaultExecution,
        SendToFleetManagementSystem = dto.SendToFleetManagementSystem,
        Numbering = dto.Numbering,
        IsArchived = dto.IsArchived,
        DefaultCarrierRelationId = dto.DefaultCarrierRelationId,
        DefaultCarrierContactPersonId = dto.DefaultCarrierContactPersonId,
        DefaultVehicleCombinationId = dto.DefaultVehicleCombinationId,
        DefaultVehicleIds = dto.DefaultVehicleIds.ToList(),
        DefaultDriverIds = dto.DefaultDriverIds.ToList(),
        DefaultDepartureRelationId = dto.DefaultDepartureRelationId,
        DefaultDepartureAddressId = dto.DefaultDepartureAddressId,
        DefaultDepartureTime = dto.DefaultDepartureTime,
        DefaultArrivalRelationId = dto.DefaultArrivalRelationId,
        DefaultArrivalAddressId = dto.DefaultArrivalAddressId,
        DefaultStopCompositionMode = dto.DefaultStopCompositionMode,
        DefaultFirstLastStopRule = dto.DefaultFirstLastStopRule,
        ConcurrencyStamp = dto.ConcurrencyStamp
    };

    /// <summary>Every default a type can carry, with vehicles (not a combination); the order of vehicles and drivers is not their creation order.</summary>
    private static void EveryDefault(TransportTripTypeCreateDto t, World w)
    {
        t.DefaultExecution = TripExecution.Subcontracted;
        t.DefaultCarrierRelationId = w.Carrier.RelationId;
        t.DefaultCarrierContactPersonId = w.Carrier.ContactId;
        t.DefaultVehicleIds = [w.Van, w.Tractor];
        t.DefaultDriverIds = [w.Driver2, w.Driver1];
        t.DefaultDepartureRelationId = w.Depot.RelationId;
        t.DefaultDepartureAddressId = w.Depot.AddressId;
        t.DefaultDepartureTime = new TimeOnly(5, 30);
        t.DefaultArrivalRelationId = w.Hub.RelationId;
        t.DefaultArrivalAddressId = w.Hub.AddressId;
        t.DefaultStopCompositionMode = StopCompositionMode.OneStopPairPerLeg;
        t.DefaultFirstLastStopRule = FirstLastStopRule.Both;
    }

    /// <summary>Changes a stored type's defaults past the service's validation: Master Data that vanished since the save, or ids of another tenant.</summary>
    private Task RewriteAsync(Guid typeId, Action<TransportTripType> change) => WithUnitOfWorkAsync(async () =>
    {
        var type = await TypeRepository.GetAsync(typeId);
        change(type);
        await TypeRepository.UpdateAsync(type);
    });

    private TransportOrderLineInputDto Line((Guid, Guid) from, (Guid, Guid) to)
        => LineOf(10, 1000, StopAt(from, Load, Day, At(8), At(10)), StopAt(to, Unload, Day, At(12), At(14)));

    /// <summary>
    /// Creates a trip through the chosen path. The order path posts the input as the order's trip request (its departure and
    /// arrival default to the yard: Orders requires both), confirms the order and reads the trip Planning created; the
    /// request must be honoured and stay as posted (the fill never writes back into the order).
    /// </summary>
    private async Task<TransportTripDto> CreateViaAsync(CreatePath path, World w, TransportTripCreateDto input)
    {
        if (path == CreatePath.AppService)
        {
            return await Trips.CreateAsync(input);
        }

        var order = await OrderAsync(w.Customer.RelationId, new TransportOrderTripRequestInputDto
        {
            TransportTripTypeId = input.TransportTripTypeId,
            DepartureRelationId = input.DepartureRelationId ?? w.Yard.RelationId,
            DepartureAddressId = input.DepartureAddressId ?? w.Yard.AddressId,
            ArrivalRelationId = input.ArrivalRelationId ?? w.Yard.RelationId,
            ArrivalAddressId = input.ArrivalAddressId ?? w.Yard.AddressId,
            PlannedDeparture = input.PlannedDeparture,
            PlannedArrival = input.PlannedArrival,
            CarrierRelationId = input.CarrierRelationId,
            CarrierContactPersonId = input.CarrierContactPersonId,
            VehicleCombinationId = input.VehicleCombinationId,
            VehicleIds = input.VehicleIds.ToList(),
            DriverIds = input.DriverIds.ToList()
        }, Line(w.Yard, w.Customer));

        var orderId = await ConfirmOrderAsync(order);

        var request = (await Orders.GetAsync(orderId)).TripRequest.ShouldNotBeNull();
        request.WarningCode.ShouldBeNull($"the request is honoured (field {request.WarningField})");
        request.TripId.ShouldNotBeNull();
        request.CarrierRelationId.ShouldBe(input.CarrierRelationId, "the fill never writes back into the order's request");
        request.CarrierContactPersonId.ShouldBe(input.CarrierContactPersonId);
        request.VehicleCombinationId.ShouldBe(input.VehicleCombinationId);
        request.VehicleIds.ShouldBe(input.VehicleIds);
        request.DriverIds.ShouldBe(input.DriverIds);
        var trip = (await TripRepository.GetListAsync(t => t.CreatedFromOrderId == orderId)).ShouldHaveSingleItem();
        request.TripId.ShouldBe(trip.Id);
        return await Trips.GetAsync(trip.Id);
    }

    private static List<Guid> VehicleIdsOf(TransportTripDto trip) => trip.Vehicles.OrderBy(v => v.Position).Select(v => v.VehicleId).ToList();

    private static List<Guid> DriverIdsOf(TransportTripDto trip) => trip.Drivers.OrderBy(d => d.Position).Select(d => d.DriverId).ToList();

    /* ── The server fill (Critical) ─────────────────────────────────────────────────────────────────────────── */

    [Theory]
    [InlineData(CreatePath.AppService)]
    [InlineData(CreatePath.OrderConfirm)]
    public async Task An_empty_trip_takes_every_default_of_its_type_but_never_its_planned_departure(CreatePath path)
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var type = await TypeAsync("FULL", t => EveryDefault(t, w));
        var input = NewTrip(type.Id, At(9));
        input.StopCompositionMode = null;
        input.FirstLastStopRule = null;

        var trip = await CreateViaAsync(path, w, input);

        trip.TransportTripTypeId.ShouldBe(type.Id);
        trip.CarrierRelationId.ShouldBe(w.Carrier.RelationId);
        trip.CarrierName.ShouldBe("Carrier One BV");
        trip.CarrierContactPersonId.ShouldBe(w.Carrier.ContactId);
        trip.VehicleCombinationId.ShouldBeNull();
        VehicleIdsOf(trip).ShouldBe([w.Van, w.Tractor], "the type's order, not the creation order");
        DriverIdsOf(trip).ShouldBe([w.Driver2, w.Driver1]);
        trip.StopCompositionMode.ShouldBe(StopCompositionMode.OneStopPairPerLeg);
        trip.FirstLastStopRule.ShouldBe(FirstLastStopRule.Both);
        trip.Execution.ShouldBe(TripExecution.Subcontracted, "the type's execution");
        trip.PlannedDeparture.ShouldBe(At(9), "the type's departure time is for the screens; the server never fills the planned departure");
        trip.TripDate.ShouldBe(Day);

        if (path == CreatePath.AppService)
        {
            (trip.DepartureRelationId, trip.DepartureAddressId).ShouldBe(((Guid?)w.Depot.RelationId, (Guid?)w.Depot.AddressId));
            (trip.ArrivalRelationId, trip.ArrivalAddressId).ShouldBe(((Guid?)w.Hub.RelationId, (Guid?)w.Hub.AddressId));

            /* The fill works on a copy: the caller's input is unchanged. */
            input.CarrierRelationId.ShouldBeNull();
            input.CarrierContactPersonId.ShouldBeNull();
            input.VehicleCombinationId.ShouldBeNull();
            input.VehicleIds.ShouldBeEmpty();
            input.DriverIds.ShouldBeEmpty();
            input.DepartureRelationId.ShouldBeNull();
            input.DepartureAddressId.ShouldBeNull();
            input.ArrivalRelationId.ShouldBeNull();
            input.ArrivalAddressId.ShouldBeNull();
            input.StopCompositionMode.ShouldBeNull();
            input.FirstLastStopRule.ShouldBeNull();
            input.Execution.ShouldBeNull();
        }
        else
        {
            (trip.DepartureRelationId, trip.DepartureAddressId).ShouldBe(((Guid?)w.Yard.RelationId, (Guid?)w.Yard.AddressId), "the request's departure wins over the type's");
            /* The type's first/last stop rule (Both) is in force on the order's trip: once its legs are on it, the arrival follows
             * the last stop (the customer), not the type's hub; the departure is the yard either way (request and first stop). */
            (trip.ArrivalRelationId, trip.ArrivalAddressId).ShouldBe(((Guid?)w.Customer.RelationId, (Guid?)w.Customer.AddressId), "the type's rule moved the arrival onto the last stop");
            trip.CreatedFromOrderId.ShouldNotBeNull();
            trip.Stops.ShouldNotBeEmpty("the order's legs are on the filled trip");
        }
    }

    [Theory]
    [InlineData(CreatePath.AppService)]
    [InlineData(CreatePath.OrderConfirm)]
    public async Task A_combination_default_fills_a_trip_without_vehicles(CreatePath path)
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var type = await TypeAsync("COMBI", t => t.DefaultVehicleCombinationId = w.Combination);

        var trip = await CreateViaAsync(path, w, NewTrip(type.Id, At(6)));

        trip.VehicleCombinationId.ShouldBe(w.Combination);
        trip.VehicleCombinationName.ShouldBe("Combi 1");
        VehicleIdsOf(trip).ShouldBe([w.Tractor, w.Trailer], "the combination's members");
    }

    [Theory]
    [InlineData(CreatePath.AppService)]
    [InlineData(CreatePath.OrderConfirm)]
    public async Task Given_values_always_win_over_the_types_defaults(CreatePath path)
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var type = await TypeAsync("FULL", t =>
        {
            EveryDefault(t, w);
            /* On the order path the legs go on the trip at once; with Both the departure and arrival would follow the stops and
             * hide whether the request's places won. */
            if (path == CreatePath.OrderConfirm)
            {
                t.DefaultFirstLastStopRule = FirstLastStopRule.None;
            }
        });
        var input = NewTrip(type.Id, At(7));
        input.CarrierRelationId = w.OtherCarrier.RelationId;
        input.CarrierContactPersonId = w.OtherCarrier.ContactId;
        input.VehicleIds = [w.Trailer];
        input.DriverIds = [w.Driver3];
        input.DepartureRelationId = w.Hub.RelationId;
        input.DepartureAddressId = w.Hub.AddressId;
        input.ArrivalRelationId = w.Depot.RelationId;
        input.ArrivalAddressId = w.Depot.AddressId;
        /* The enums' own defaults are values too: given, they beat the type's OneStopPairPerLeg / Both. */
        input.StopCompositionMode = StopCompositionMode.MergeByLocationAndActivity;
        input.FirstLastStopRule = FirstLastStopRule.None;
        input.Execution = TripExecution.OwnFleet;

        var trip = await CreateViaAsync(path, w, input);

        trip.CarrierRelationId.ShouldBe(w.OtherCarrier.RelationId);
        trip.CarrierContactPersonId.ShouldBe(w.OtherCarrier.ContactId);
        VehicleIdsOf(trip).ShouldBe([w.Trailer]);
        trip.VehicleCombinationId.ShouldBeNull();
        DriverIdsOf(trip).ShouldBe([w.Driver3]);
        (trip.DepartureRelationId, trip.DepartureAddressId).ShouldBe(((Guid?)w.Hub.RelationId, (Guid?)w.Hub.AddressId));
        (trip.ArrivalRelationId, trip.ArrivalAddressId).ShouldBe(((Guid?)w.Depot.RelationId, (Guid?)w.Depot.AddressId));
        if (path == CreatePath.AppService)
        {
            trip.StopCompositionMode.ShouldBe(StopCompositionMode.MergeByLocationAndActivity);
            trip.FirstLastStopRule.ShouldBe(FirstLastStopRule.None);
            trip.Execution.ShouldBe(TripExecution.OwnFleet);
        }
        else
        {
            /* The order's request carries no composition: the type's. A request with a carrier is subcontracted. */
            trip.StopCompositionMode.ShouldBe(StopCompositionMode.OneStopPairPerLeg);
            trip.FirstLastStopRule.ShouldBe(FirstLastStopRule.None);
            trip.Execution.ShouldBe(TripExecution.Subcontracted);
        }
    }

    [Theory]
    [InlineData(CreatePath.AppService)]
    [InlineData(CreatePath.OrderConfirm)]
    public async Task A_given_combination_keeps_the_types_vehicles_out_and_given_vehicles_keep_the_types_combination_out(CreatePath path)
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var withVehicles = await TypeAsync("VEH", t => t.DefaultVehicleIds = [w.Van]);
        var withCombination = await TypeAsync("COMBI", t => t.DefaultVehicleCombinationId = w.Combination);

        var combined = NewTrip(withVehicles.Id, At(6));
        combined.VehicleCombinationId = w.Combination;
        var first = await CreateViaAsync(path, w, combined);
        first.VehicleCombinationId.ShouldBe(w.Combination);
        VehicleIdsOf(first).ShouldNotContain(w.Van, "a given combination fills the vehicle group");

        var own = NewTrip(withCombination.Id, At(6));
        own.VehicleIds = [w.Van];
        var second = await CreateViaAsync(path, w, own);
        second.VehicleCombinationId.ShouldBeNull("given vehicles fill the vehicle group");
        VehicleIdsOf(second).ShouldBe([w.Van]);
    }

    [Theory]
    [InlineData(CreatePath.AppService)]
    [InlineData(CreatePath.OrderConfirm)]
    public async Task A_given_carrier_without_contact_gets_the_types_contact_only_when_it_is_the_types_carrier(CreatePath path)
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var type = await TypeAsync("CARR", t =>
        {
            t.DefaultCarrierRelationId = w.Carrier.RelationId;
            t.DefaultCarrierContactPersonId = w.Carrier.ContactId;
        });

        var same = NewTrip(type.Id, At(6));
        same.CarrierRelationId = w.Carrier.RelationId;
        var sameTrip = await CreateViaAsync(path, w, same);
        sameTrip.CarrierRelationId.ShouldBe(w.Carrier.RelationId);
        sameTrip.CarrierContactPersonId.ShouldBe(w.Carrier.ContactId, "the type's carrier given without a contact gets the type's contact");
        same.CarrierContactPersonId.ShouldBeNull("the caller's input is not changed");

        var other = NewTrip(type.Id, At(6));
        other.CarrierRelationId = w.OtherCarrier.RelationId;
        var otherTrip = await CreateViaAsync(path, w, other);
        otherTrip.CarrierRelationId.ShouldBe(w.OtherCarrier.RelationId);
        otherTrip.CarrierContactPersonId.ShouldBeNull("another carrier never gets the type's carrier's contact");
    }

    [Fact]
    public async Task A_given_departure_or_arrival_relation_without_address_gets_the_types_address_only_when_it_is_the_types_relation()
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var type = await TypeAsync("PLACES", t =>
        {
            t.DefaultDepartureRelationId = w.Depot.RelationId;
            t.DefaultDepartureAddressId = w.Depot.AddressId;
            t.DefaultArrivalRelationId = w.Hub.RelationId;
            t.DefaultArrivalAddressId = w.Hub.AddressId;
        });

        var ownRelations = NewTrip(type.Id, At(6));
        ownRelations.DepartureRelationId = w.Depot.RelationId;
        ownRelations.ArrivalRelationId = w.Hub.RelationId;
        var first = await Trips.CreateAsync(ownRelations);
        (first.DepartureRelationId, first.DepartureAddressId).ShouldBe(((Guid?)w.Depot.RelationId, (Guid?)w.Depot.AddressId));
        (first.ArrivalRelationId, first.ArrivalAddressId).ShouldBe(((Guid?)w.Hub.RelationId, (Guid?)w.Hub.AddressId));
        ownRelations.DepartureAddressId.ShouldBeNull();
        ownRelations.ArrivalAddressId.ShouldBeNull();

        var otherRelations = NewTrip(type.Id, At(6));
        otherRelations.DepartureRelationId = w.Yard.RelationId;
        otherRelations.ArrivalRelationId = w.Customer.RelationId;
        var second = await Trips.CreateAsync(otherRelations);
        (second.DepartureRelationId, second.DepartureAddressId).ShouldBe(((Guid?)w.Yard.RelationId, (Guid?)null), "another relation never gets the type's address");
        (second.ArrivalRelationId, second.ArrivalAddressId).ShouldBe(((Guid?)w.Customer.RelationId, (Guid?)null));

        /* One group given, the other empty: each group on its own. */
        var mixed = NewTrip(type.Id, At(6));
        mixed.DepartureRelationId = w.Yard.RelationId;
        mixed.DepartureAddressId = w.Yard.AddressId;
        var third = await Trips.CreateAsync(mixed);
        (third.DepartureRelationId, third.DepartureAddressId).ShouldBe(((Guid?)w.Yard.RelationId, (Guid?)w.Yard.AddressId));
        (third.ArrivalRelationId, third.ArrivalAddressId).ShouldBe(((Guid?)w.Hub.RelationId, (Guid?)w.Hub.AddressId));
    }

    [Theory]
    [InlineData(CreatePath.AppService)]
    [InlineData(CreatePath.OrderConfirm)]
    public async Task Archived_defaults_are_skipped_without_an_error_and_a_skipped_carrier_skips_its_contact(CreatePath path)
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var type = await TypeAsync("ARCH", t =>
        {
            t.DefaultCarrierRelationId = w.Carrier.RelationId;
            t.DefaultCarrierContactPersonId = w.Carrier.ContactId;
            t.DefaultVehicleIds = [w.Van, w.Tractor, w.Trailer];
            t.DefaultDriverIds = [w.Driver1, w.Driver2];
            t.DefaultDepartureRelationId = w.Hub.RelationId;
            t.DefaultDepartureAddressId = w.Hub.AddressId;
            t.DefaultArrivalRelationId = w.Depot.RelationId;
            t.DefaultArrivalAddressId = w.Depot.AddressId;
        });
        var combined = await TypeAsync("ARCHCOMBI", t => t.DefaultVehicleCombinationId = w.Combination);
        await GetRequiredService<IRelationAppService>().ArchiveAsync(w.Carrier.RelationId);
        await GetRequiredService<IRelationAppService>().ArchiveAsync(w.Hub.RelationId);
        await GetRequiredService<IVehicleAppService>().ArchiveAsync(w.Van);
        await GetRequiredService<IDriverAppService>().ArchiveAsync(w.Driver1);
        await GetRequiredService<IVehicleCombinationAppService>().ArchiveAsync(w.Combination);

        var trip = await CreateViaAsync(path, w, NewTrip(type.Id, At(6)));

        trip.CarrierRelationId.ShouldBeNull("an archived carrier is skipped");
        trip.CarrierContactPersonId.ShouldBeNull("and its contact with it, although the contact itself still exists");
        VehicleIdsOf(trip).ShouldBe([w.Tractor, w.Trailer], "the archived vehicle is skipped, the rest keep their order");
        DriverIdsOf(trip).ShouldBe([w.Driver2]);
        if (path == CreatePath.AppService)
        {
            trip.DepartureRelationId.ShouldBeNull("an archived departure relation is skipped, its address with it");
            trip.DepartureAddressId.ShouldBeNull();
            (trip.ArrivalRelationId, trip.ArrivalAddressId).ShouldBe(((Guid?)w.Depot.RelationId, (Guid?)w.Depot.AddressId), "the other groups still fill");
        }

        var combinationTrip = await CreateViaAsync(path, w, NewTrip(combined.Id, At(6)));
        combinationTrip.VehicleCombinationId.ShouldBeNull("an archived combination is skipped");
        combinationTrip.Vehicles.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(CreatePath.AppService)]
    [InlineData(CreatePath.OrderConfirm)]
    public async Task Deleted_defaults_are_skipped_without_an_error(CreatePath path)
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var deletedCarrier = await TypeAsync("DELCARR", t =>
        {
            t.DefaultCarrierRelationId = w.Carrier.RelationId;
            t.DefaultCarrierContactPersonId = w.Carrier.ContactId;
        });
        await GetRequiredService<IRelationAppService>().DeleteAsync(w.Carrier.RelationId);

        var first = await CreateViaAsync(path, w, NewTrip(deletedCarrier.Id, At(6)));
        first.CarrierRelationId.ShouldBeNull("a deleted carrier is skipped");
        first.CarrierContactPersonId.ShouldBeNull();

        /* Rows the lookup no longer returns, written past the validation as if deleted since the save. */
        var gone = await TypeAsync("GONE");
        await RewriteAsync(gone.Id, t =>
        {
            t.SetDefaultCarrier(w.OtherCarrier.RelationId, Guid.NewGuid());
            t.SetDefaultVehicles(null, [Guid.NewGuid(), w.Van]);
            t.SetDefaultDrivers([w.Driver1, Guid.NewGuid()]);
            t.SetDefaultDeparture(Guid.NewGuid(), Guid.NewGuid(), null);
            t.SetDefaultArrival(w.Hub.RelationId, Guid.NewGuid());
        });
        var goneCombination = await TypeAsync("GONECOMBI");
        await RewriteAsync(goneCombination.Id, t => t.SetDefaultVehicles(Guid.NewGuid(), null));

        var second = await CreateViaAsync(path, w, NewTrip(gone.Id, At(6)));
        second.CarrierRelationId.ShouldBe(w.OtherCarrier.RelationId, "the carrier still exists");
        second.CarrierContactPersonId.ShouldBeNull("its deleted contact is skipped");
        VehicleIdsOf(second).ShouldBe([w.Van]);
        DriverIdsOf(second).ShouldBe([w.Driver1]);
        if (path == CreatePath.AppService)
        {
            second.DepartureRelationId.ShouldBeNull();
            second.DepartureAddressId.ShouldBeNull();
            (second.ArrivalRelationId, second.ArrivalAddressId).ShouldBe(((Guid?)w.Hub.RelationId, (Guid?)null), "the relation fills, its deleted address is skipped");
        }

        var third = await CreateViaAsync(path, w, NewTrip(goneCombination.Id, At(6)));
        third.VehicleCombinationId.ShouldBeNull();
        third.Vehicles.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_address_no_longer_of_its_relation_is_skipped_also_when_the_relation_is_given()
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var type = await TypeAsync("MOVED");
        await RewriteAsync(type.Id, t =>
        {
            t.SetDefaultDeparture(w.Depot.RelationId, w.Hub.AddressId, null);
            t.SetDefaultArrival(w.Hub.RelationId, w.Depot.AddressId);
        });

        var empty = await Trips.CreateAsync(NewTrip(type.Id, At(6)));
        (empty.DepartureRelationId, empty.DepartureAddressId).ShouldBe(((Guid?)w.Depot.RelationId, (Guid?)null));
        (empty.ArrivalRelationId, empty.ArrivalAddressId).ShouldBe(((Guid?)w.Hub.RelationId, (Guid?)null));

        var given = NewTrip(type.Id, At(6));
        given.DepartureRelationId = w.Depot.RelationId;
        given.ArrivalRelationId = w.Hub.RelationId;
        var trip = await Trips.CreateAsync(given);
        trip.DepartureAddressId.ShouldBeNull("the type's address belongs to another relation now");
        trip.ArrivalAddressId.ShouldBeNull();
    }

    [Theory]
    [InlineData(CreatePath.AppService)]
    [InlineData(CreatePath.OrderConfirm)]
    public async Task A_trip_in_another_tenant_never_gets_this_tenants_defaults(CreatePath path)
    {
        World a;
        Guid typeOfA;
        using (InTenantA())
        {
            a = await WorldAsync();
            typeOfA = (await TypeAsync("FULL", t => EveryDefault(t, a))).Id;
        }

        using var _ = InTenantB();
        var b = await WorldAsync();
        var typeOfB = await TypeAsync("FOREIGN");
        await RewriteAsync(typeOfB.Id, t =>
        {
            t.SetDefaultCarrier(a.Carrier.RelationId, a.Carrier.ContactId);
            t.SetDefaultVehicles(null, [a.Van, a.Tractor]);
            t.SetDefaultDrivers([a.Driver1]);
            t.SetDefaultDeparture(a.Depot.RelationId, a.Depot.AddressId, null);
            t.SetDefaultArrival(a.Hub.RelationId, a.Hub.AddressId);
        });

        var trip = await CreateViaAsync(path, b, NewTrip(typeOfB.Id, At(6)));

        trip.CarrierRelationId.ShouldBeNull("tenant A's carrier does not exist for tenant B");
        trip.CarrierContactPersonId.ShouldBeNull();
        trip.Vehicles.ShouldBeEmpty();
        trip.Drivers.ShouldBeEmpty();
        if (path == CreatePath.AppService)
        {
            trip.DepartureRelationId.ShouldBeNull();
            trip.DepartureAddressId.ShouldBeNull();
            trip.ArrivalRelationId.ShouldBeNull();
            trip.ArrivalAddressId.ShouldBeNull();

            /* Tenant A's type does not exist for tenant B either. */
            var error = await Should.ThrowAsync<BusinessException>(() => Trips.CreateAsync(NewTrip(typeOfA, At(6))));
            error.Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
            error.Data["field"].ShouldBe("transportTripTypeId");

            /* A's carrier given with B's type of the same carrier id: still no contact of A, still refused. */
            var given = NewTrip(typeOfB.Id, At(6));
            given.CarrierRelationId = a.Carrier.RelationId;
            (await Should.ThrowAsync<BusinessException>(() => Trips.CreateAsync(given))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        }
    }

    /* ── The contact must be the carrier's; a carrier from the type subcontracts (PO 2026-10-29, Critical) ──── */

    [Theory]
    [InlineData(CreatePath.AppService)]
    [InlineData(CreatePath.OrderConfirm)]
    public async Task A_types_contact_no_longer_of_its_carrier_is_skipped_and_the_carrier_still_fills(CreatePath path)
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var type = await TypeAsync("MOVED");
        /* Stored past the validation, as if the contact belongs to another relation since the save. */
        await RewriteAsync(type.Id, t => t.SetDefaultCarrier(w.Carrier.RelationId, w.OtherCarrier.ContactId));

        var empty = await CreateViaAsync(path, w, NewTrip(type.Id, At(6)));
        empty.CarrierRelationId.ShouldBe(w.Carrier.RelationId, "the carrier still fills");
        empty.CarrierContactPersonId.ShouldBeNull("the type's contact is not the carrier's");
        empty.Execution.ShouldBe(TripExecution.Subcontracted, "the carrier came from the type");

        var sameCarrier = NewTrip(type.Id, At(6));
        sameCarrier.CarrierRelationId = w.Carrier.RelationId;
        var sameTrip = await CreateViaAsync(path, w, sameCarrier);
        sameTrip.CarrierRelationId.ShouldBe(w.Carrier.RelationId);
        sameTrip.CarrierContactPersonId.ShouldBeNull("the type's carrier given without a contact gets the type's contact only when it is that carrier's");

        /* The type's contact belongs to the other carrier: given that carrier, it still is not the type's carrier. */
        var contactsCarrier = NewTrip(type.Id, At(6));
        contactsCarrier.CarrierRelationId = w.OtherCarrier.RelationId;
        var otherTrip = await CreateViaAsync(path, w, contactsCarrier);
        otherTrip.CarrierRelationId.ShouldBe(w.OtherCarrier.RelationId);
        otherTrip.CarrierContactPersonId.ShouldBeNull("a carrier other than the type's never gets the type's contact, even its own");
    }

    [Theory]
    [InlineData(CreatePath.AppService)]
    [InlineData(CreatePath.OrderConfirm)]
    public async Task A_contact_of_another_tenant_on_the_type_is_skipped_while_the_own_carrier_fills(CreatePath path)
    {
        World a;
        using (InTenantA())
        {
            a = await WorldAsync();
        }

        using var _ = InTenantB();
        var b = await WorldAsync();
        var type = await TypeAsync("FOREIGNCONTACT");
        await RewriteAsync(type.Id, t => t.SetDefaultCarrier(b.Carrier.RelationId, a.Carrier.ContactId));

        var empty = await CreateViaAsync(path, b, NewTrip(type.Id, At(6)));
        empty.CarrierRelationId.ShouldBe(b.Carrier.RelationId);
        empty.CarrierContactPersonId.ShouldBeNull("tenant A's contact does not exist for tenant B");
        empty.Execution.ShouldBe(TripExecution.Subcontracted);

        var given = NewTrip(type.Id, At(6));
        given.CarrierRelationId = b.Carrier.RelationId;
        (await CreateViaAsync(path, b, given)).CarrierContactPersonId.ShouldBeNull();
    }

    [Theory]
    [InlineData(CreatePath.AppService)]
    [InlineData(CreatePath.OrderConfirm)]
    public async Task A_carrier_from_the_type_subcontracts_the_trip_even_when_the_types_execution_is_own_fleet(CreatePath path)
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var type = await TypeAsync("OWNCARR", t =>
        {
            t.DefaultExecution = TripExecution.OwnFleet;
            t.DefaultCarrierRelationId = w.Carrier.RelationId;
            t.DefaultCarrierContactPersonId = w.Carrier.ContactId;
        });
        var input = NewTrip(type.Id, At(6));

        var trip = await CreateViaAsync(path, w, input);

        trip.CarrierRelationId.ShouldBe(w.Carrier.RelationId);
        trip.CarrierContactPersonId.ShouldBe(w.Carrier.ContactId);
        trip.Execution.ShouldBe(TripExecution.Subcontracted, "a carrier filled from the type makes the trip subcontracted (PO 2026-10-29)");
        input.Execution.ShouldBeNull("the caller's input is not changed");
    }

    [Fact]
    public async Task A_given_execution_always_wins_also_when_the_carrier_comes_from_the_type()
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var type = await TypeAsync("SUBCARR", t =>
        {
            t.DefaultExecution = TripExecution.Subcontracted;
            t.DefaultCarrierRelationId = w.Carrier.RelationId;
            t.DefaultCarrierContactPersonId = w.Carrier.ContactId;
        });
        var input = NewTrip(type.Id, At(6));
        input.Execution = TripExecution.OwnFleet;

        var trip = await Trips.CreateAsync(input);

        trip.CarrierRelationId.ShouldBe(w.Carrier.RelationId, "the carrier still fills");
        trip.Execution.ShouldBe(TripExecution.OwnFleet, "a given execution always wins");
    }

    [Theory]
    [InlineData(CreatePath.AppService)]
    [InlineData(CreatePath.OrderConfirm)]
    public async Task A_carrier_given_by_the_caller_keeps_the_types_execution(CreatePath path)
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var type = await TypeAsync("OWNCARR", t =>
        {
            t.DefaultExecution = TripExecution.OwnFleet;
            t.DefaultCarrierRelationId = w.Carrier.RelationId;
            t.DefaultCarrierContactPersonId = w.Carrier.ContactId;
        });

        var typesCarrier = NewTrip(type.Id, At(6));
        typesCarrier.CarrierRelationId = w.Carrier.RelationId;
        var otherCarrier = NewTrip(type.Id, At(6));
        otherCarrier.CarrierRelationId = w.OtherCarrier.RelationId;
        otherCarrier.CarrierContactPersonId = w.OtherCarrier.ContactId;

        foreach (var input in new[] { typesCarrier, otherCarrier })
        {
            var trip = await CreateViaAsync(path, w, input);
            trip.CarrierRelationId.ShouldBe(input.CarrierRelationId);
            /* The trips service: the carrier did not come from the type, so the type's execution (unchanged rule). The order's
             * request has no execution of its own: a request with a carrier is subcontracted there (as before), and that
             * given value wins over the type's. */
            trip.Execution.ShouldBe(path == CreatePath.AppService ? TripExecution.OwnFleet : TripExecution.Subcontracted);
        }
    }

    [Theory]
    [InlineData(CreatePath.AppService)]
    [InlineData(CreatePath.OrderConfirm)]
    public async Task A_type_carrier_that_is_skipped_does_not_subcontract_the_trip(CreatePath path)
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var archived = await TypeAsync("ARCHCARR", t =>
        {
            t.DefaultExecution = TripExecution.OwnFleet;
            t.DefaultCarrierRelationId = w.Carrier.RelationId;
            t.DefaultCarrierContactPersonId = w.Carrier.ContactId;
        });
        var gone = await TypeAsync("GONECARR");
        await RewriteAsync(gone.Id, t => t.SetDefaultCarrier(Guid.NewGuid(), null));
        var none = await TypeAsync("NOCARR", t => t.DefaultExecution = TripExecution.OwnFleet);
        await GetRequiredService<IRelationAppService>().ArchiveAsync(w.Carrier.RelationId);

        foreach (var type in new[] { archived.Id, gone.Id, none.Id })
        {
            var trip = await CreateViaAsync(path, w, NewTrip(type, At(6)));
            trip.CarrierRelationId.ShouldBeNull();
            trip.Execution.ShouldBe(TripExecution.OwnFleet, "no carrier came from the type: the type's execution");
        }
    }

    [Fact]
    public async Task The_fill_copies_every_field_of_the_input_and_never_changes_the_input()
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var defaults = GetRequiredService<TripTypeDefaults>();
        var input = new TransportTripCreateDto
        {
            TransportTripTypeId = Guid.NewGuid(),
            TripDate = Day,
            Name = "Monday run",
            Execution = TripExecution.Subcontracted,
            StopCompositionMode = StopCompositionMode.OneStopPairPerLeg,
            FirstLastStopRule = FirstLastStopRule.ArrivalIsLastStop,
            CarrierRelationId = Guid.NewGuid(),
            CarrierContactPersonId = Guid.NewGuid(),
            DepartureRelationId = Guid.NewGuid(),
            DepartureAddressId = Guid.NewGuid(),
            ArrivalRelationId = Guid.NewGuid(),
            ArrivalAddressId = Guid.NewGuid(),
            PlannedDeparture = At(6),
            PlannedArrival = At(18),
            VehicleCombinationId = Guid.NewGuid(),
            VehicleIds = [Guid.NewGuid()],
            DriverIds = [Guid.NewGuid()],
            Remarks = "Call ahead",
            DriverInstructions = "Gate 4",
            OrganizationUnitId = Guid.NewGuid(),
            Attributes = new Dictionary<string, string> { ["Colour"] = "blue" }
        };
        var properties = typeof(TransportTripCreateDto).GetProperties();
        foreach (var property in properties)
        {
            /* A new input field must be set here, so the copy below is checked for it too. */
            IsEmpty(property.GetValue(input)).ShouldBeFalse($"the sample sets {property.Name}");
        }

        var noDefaults = new TransportTripType(Guid.NewGuid(), TenantA, "BARE", LocalizedName.Create("en", "Bare"), NumberSequenceFormat.Create("B", false, false, "-", 5));
        var copy = await defaults.FillAsync(input, noDefaults);

        copy.ShouldNotBeSameAs(input);
        foreach (var property in properties)
        {
            var original = property.GetValue(input);
            var copied = property.GetValue(copy);
            if (original is IEnumerable sequence and not string)
            {
                copied.ShouldNotBeSameAs(original, $"{property.Name} is a copy, not the caller's list");
                ((IEnumerable)copied!).Cast<object>().ShouldBe(sequence.Cast<object>(), $"{property.Name} is copied");
            }
            else
            {
                copied.ShouldBe(original, $"{property.Name} is copied");
            }
        }

        /* A type with defaults and an empty input: the copy is filled, the input stays empty. */
        var full = new TransportTripType(Guid.NewGuid(), TenantA, "FULL", LocalizedName.Create("en", "Full"), NumberSequenceFormat.Create("F", false, false, "-", 5));
        full.SetDefaultCarrier(w.Carrier.RelationId, w.Carrier.ContactId);
        full.SetDefaultVehicles(null, [w.Van]);
        full.SetDefaultDrivers([w.Driver1]);
        full.SetDefaultDeparture(w.Depot.RelationId, w.Depot.AddressId, new TimeOnly(5, 0));
        full.SetDefaultArrival(w.Hub.RelationId, w.Hub.AddressId);
        var empty = NewTrip(full.Id, At(6));
        empty.StopCompositionMode = null;
        empty.FirstLastStopRule = null;
        var vehicles = empty.VehicleIds;
        var drivers = empty.DriverIds;

        var filled = await defaults.FillAsync(empty, full);

        filled.CarrierRelationId.ShouldBe(w.Carrier.RelationId);
        filled.VehicleIds.ShouldBe([w.Van]);
        filled.DriverIds.ShouldBe([w.Driver1]);
        filled.DepartureAddressId.ShouldBe(w.Depot.AddressId);
        filled.PlannedDeparture.ShouldBe(At(6));
        empty.CarrierRelationId.ShouldBeNull();
        empty.CarrierContactPersonId.ShouldBeNull();
        empty.DepartureRelationId.ShouldBeNull();
        empty.ArrivalRelationId.ShouldBeNull();
        empty.Execution.ShouldBeNull();
        empty.StopCompositionMode.ShouldBeNull();
        empty.FirstLastStopRule.ShouldBeNull();
        empty.VehicleIds.ShouldBeSameAs(vehicles);
        empty.VehicleIds.ShouldBeEmpty();
        empty.DriverIds.ShouldBeSameAs(drivers);
        empty.DriverIds.ShouldBeEmpty();
    }

    private static bool IsEmpty(object? value) => value switch
    {
        null => true,
        Guid g => g == Guid.Empty,
        string s => s.Length == 0,
        IEnumerable e => !e.Cast<object>().Any(),
        _ => false
    };

    /* ── The type's validation and isolation on save (High) ──────────────────────────────────────────────────── */

    [Theory]
    [InlineData("defaultCarrierRelationId")]
    [InlineData("defaultCarrierContactPersonId")]
    [InlineData("defaultVehicleCombinationId")]
    [InlineData("defaultVehicleIds")]
    [InlineData("defaultDriverIds")]
    [InlineData("defaultDepartureRelationId")]
    [InlineData("defaultDepartureAddressId")]
    [InlineData("defaultArrivalRelationId")]
    [InlineData("defaultArrivalAddressId")]
    public async Task Saving_a_type_refuses_a_default_the_tenant_does_not_have(string field)
    {
        using var _ = InTenantA();
        var w = await WorldAsync();
        var unknown = Guid.NewGuid();
        void Apply(TransportTripTypeCreateDto t)
        {
            switch (field)
            {
                case "defaultCarrierRelationId": t.DefaultCarrierRelationId = unknown; break;
                case "defaultCarrierContactPersonId": t.DefaultCarrierRelationId = w.Carrier.RelationId; t.DefaultCarrierContactPersonId = unknown; break;
                case "defaultVehicleCombinationId": t.DefaultVehicleCombinationId = unknown; break;
                case "defaultVehicleIds": t.DefaultVehicleIds = [w.Van, unknown]; break;
                case "defaultDriverIds": t.DefaultDriverIds = [w.Driver1, unknown]; break;
                case "defaultDepartureRelationId": t.DefaultDepartureRelationId = unknown; break;
                case "defaultDepartureAddressId": t.DefaultDepartureRelationId = w.Depot.RelationId; t.DefaultDepartureAddressId = unknown; break;
                case "defaultArrivalRelationId": t.DefaultArrivalRelationId = unknown; break;
                case "defaultArrivalAddressId": t.DefaultArrivalRelationId = w.Hub.RelationId; t.DefaultArrivalAddressId = unknown; break;
                default: throw new ArgumentOutOfRangeException(nameof(field));
            }
        }

        var create = NewType("BAD");
        Apply(create);
        var refused = await Should.ThrowAsync<BusinessException>(() => TripTypes.CreateAsync(create));
        refused.Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        refused.Data["field"].ShouldBe(field);
        refused.Data["value"].ShouldBe(unknown);

        var existing = await TypeAsync("OK");
        var update = UpdateOf(existing);
        Apply(update);
        var refusedUpdate = await Should.ThrowAsync<BusinessException>(() => TripTypes.UpdateAsync(existing.Id, update));
        refusedUpdate.Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        refusedUpdate.Data["field"].ShouldBe(field);

        (await TripTypes.GetListAsync(List())).Rows.Select(r => r.Code).ShouldBe(["OK"], "the refused create wrote nothing");
        var stored = await TripTypes.GetAsync(existing.Id);
        stored.DefaultCarrierRelationId.ShouldBeNull("the refused update wrote nothing");
        stored.DefaultVehicleIds.ShouldBeEmpty();
        stored.DefaultDriverIds.ShouldBeEmpty();
        stored.DefaultDepartureRelationId.ShouldBeNull();
        stored.DefaultArrivalRelationId.ShouldBeNull();
    }

    [Fact]
    public async Task An_address_of_another_relation_is_refused_on_departure_and_arrival()
    {
        using var _ = InTenantA();
        var w = await WorldAsync();

        var departure = await Should.ThrowAsync<BusinessException>(() => TypeAsync("DEP", t =>
        {
            t.DefaultDepartureRelationId = w.Depot.RelationId;
            t.DefaultDepartureAddressId = w.Hub.AddressId;
        }));
        departure.Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        departure.Data["field"].ShouldBe("defaultDepartureAddressId");

        var arrival = await Should.ThrowAsync<BusinessException>(() => TypeAsync("ARR", t =>
        {
            t.DefaultArrivalRelationId = w.Hub.RelationId;
            t.DefaultArrivalAddressId = w.Carrier.AddressId;
        }));
        arrival.Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        arrival.Data["field"].ShouldBe("defaultArrivalAddressId");
    }

    [Fact]
    public async Task A_default_contact_of_another_relation_than_the_carrier_is_refused_on_create_and_update_and_nothing_is_written()
    {
        using var _ = InTenantA();
        var w = await WorldAsync();

        var create = await Should.ThrowAsync<BusinessException>(() => TypeAsync("WRONG", t =>
        {
            t.DefaultCarrierRelationId = w.Carrier.RelationId;
            t.DefaultCarrierContactPersonId = w.OtherCarrier.ContactId;
        }));
        create.Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        create.Data["field"].ShouldBe("defaultCarrierContactPersonId");
        create.Data["value"].ShouldBe(w.OtherCarrier.ContactId);
        (await TripTypes.GetListAsync(List())).TotalCount.ShouldBe(0, "the refused create wrote nothing");

        var existing = await TypeAsync("OK", t =>
        {
            t.DefaultCarrierRelationId = w.Carrier.RelationId;
            t.DefaultCarrierContactPersonId = w.Carrier.ContactId;
        });

        /* Another carrier's contact on the same carrier. */
        var otherContact = UpdateOf(existing);
        otherContact.DefaultCarrierContactPersonId = w.OtherCarrier.ContactId;
        var refused = await Should.ThrowAsync<BusinessException>(() => TripTypes.UpdateAsync(existing.Id, otherContact));
        refused.Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        refused.Data["field"].ShouldBe("defaultCarrierContactPersonId");
        refused.Data["value"].ShouldBe(w.OtherCarrier.ContactId);

        /* The carrier switched, the old carrier's contact kept: no longer the carrier's. */
        var switched = UpdateOf(existing);
        switched.DefaultCarrierRelationId = w.OtherCarrier.RelationId;
        var refusedSwitch = await Should.ThrowAsync<BusinessException>(() => TripTypes.UpdateAsync(existing.Id, switched));
        refusedSwitch.Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        refusedSwitch.Data["field"].ShouldBe("defaultCarrierContactPersonId");
        refusedSwitch.Data["value"].ShouldBe(w.Carrier.ContactId);

        var stored = await TripTypes.GetAsync(existing.Id);
        (stored.DefaultCarrierRelationId, stored.DefaultCarrierContactPersonId).ShouldBe(((Guid?)w.Carrier.RelationId, (Guid?)w.Carrier.ContactId), "the refused updates wrote nothing");

        /* The matching pair is accepted. */
        var matching = UpdateOf(stored);
        matching.DefaultCarrierRelationId = w.OtherCarrier.RelationId;
        matching.DefaultCarrierContactPersonId = w.OtherCarrier.ContactId;
        var updated = await TripTypes.UpdateAsync(existing.Id, matching);
        (updated.DefaultCarrierRelationId, updated.DefaultCarrierContactPersonId).ShouldBe(((Guid?)w.OtherCarrier.RelationId, (Guid?)w.OtherCarrier.ContactId));
    }

    [Fact]
    public async Task The_structural_rules_are_refused_on_save_and_duplicates_are_dropped()
    {
        using var _ = InTenantA();
        var w = await WorldAsync();

        var contact = await Should.ThrowAsync<BusinessException>(() => TypeAsync("C", t => t.DefaultCarrierContactPersonId = w.Carrier.ContactId));
        contact.Code.ShouldBe(PlanningExecutionErrorCodes.ContactWithoutCarrier);
        contact.Data["field"].ShouldBe("defaultCarrierContactPersonId");

        var departure = await Should.ThrowAsync<BusinessException>(() => TypeAsync("D", t => t.DefaultDepartureAddressId = w.Depot.AddressId));
        departure.Code.ShouldBe(PlanningExecutionErrorCodes.AddressWithoutRelation);
        departure.Data["field"].ShouldBe("defaultDepartureAddressId");

        var arrival = await Should.ThrowAsync<BusinessException>(() => TypeAsync("A", t => t.DefaultArrivalAddressId = w.Hub.AddressId));
        arrival.Code.ShouldBe(PlanningExecutionErrorCodes.AddressWithoutRelation);
        arrival.Data["field"].ShouldBe("defaultArrivalAddressId");

        (await Should.ThrowAsync<BusinessException>(() => TypeAsync("V", t =>
        {
            t.DefaultVehicleCombinationId = w.Combination;
            t.DefaultVehicleIds = [w.Van];
        }))).Code.ShouldBe(PlanningExecutionErrorCodes.VehicleSourceConflict);

        /* More than three is refused at the API's boundary already (MaxLength on the input). */
        await Should.ThrowAsync<AbpValidationException>(() => TypeAsync("V4", t => t.DefaultVehicleIds = [w.Van, w.Tractor, w.Trailer, Guid.NewGuid()]));
        await Should.ThrowAsync<AbpValidationException>(() => TypeAsync("D4", t => t.DefaultDriverIds = [w.Driver1, w.Driver2, w.Driver3, Guid.NewGuid()]));

        (await TripTypes.GetListAsync(List())).TotalCount.ShouldBe(0, "nothing refused was written");

        var deduplicated = await TypeAsync("DUP", t =>
        {
            t.DefaultVehicleIds = [w.Trailer, w.Trailer, w.Van];
            t.DefaultDriverIds = [w.Driver3, w.Driver1, w.Driver3];
        });
        deduplicated.DefaultVehicleIds.ShouldBe([w.Trailer, w.Van]);
        deduplicated.DefaultDriverIds.ShouldBe([w.Driver3, w.Driver1]);
    }

    [Fact]
    public async Task The_detail_carries_the_defaults_with_their_names_and_an_update_can_clear_them()
    {
        using var _ = InTenantA();
        using var culture = new CultureScope("en");
        var w = await WorldAsync();

        var created = await TypeAsync("FULL", t => EveryDefault(t, w));
        var detail = await TripTypes.GetAsync(created.Id);

        detail.DefaultCarrierRelationId.ShouldBe(w.Carrier.RelationId);
        detail.DefaultCarrierRelationName.ShouldBe("Carrier One BV");
        detail.DefaultCarrierContactPersonId.ShouldBe(w.Carrier.ContactId);
        detail.DefaultCarrierContactPersonName.ShouldBe("Kees Visser");
        detail.DefaultVehicleCombinationId.ShouldBeNull();
        detail.DefaultVehicleCombinationName.ShouldBeNull();
        detail.DefaultVehicleIds.ShouldBe([w.Van, w.Tractor]);
        detail.DefaultVehicles.Select(v => (v.Id, v.Name, v.LicencePlate, v.Kind, v.VehicleTypeName)).ShouldBe(
        [
            (w.Van, "Van 01", (string?)null, (VehicleTypeKind?)VehicleTypeKind.Rigid, (string?)"Van"),
            (w.Tractor, "Truck 01", null, VehicleTypeKind.Tractor, "Tractor")
        ]);
        detail.DefaultDriverIds.ShouldBe([w.Driver2, w.Driver1]);
        detail.DefaultDrivers.Select(d => (d.Id, d.Name)).ShouldBe([(w.Driver2, "Piet Peters"), (w.Driver1, "Jan Jansen")]);
        detail.DefaultDepartureRelationName.ShouldBe("Depot Utrecht");
        detail.DefaultDepartureAddressId.ShouldBe(w.Depot.AddressId);
        detail.DefaultDepartureAddressLabel.ShouldBe("Main street 1, 1000 AA Utrecht");
        detail.DefaultDepartureTime.ShouldBe(new TimeOnly(5, 30));
        detail.DefaultArrivalRelationName.ShouldBe("Hub Venlo");
        detail.DefaultArrivalAddressLabel.ShouldBe("Main street 1, 1000 AA Venlo");
        detail.DefaultStopCompositionMode.ShouldBe(StopCompositionMode.OneStopPairPerLeg);
        detail.DefaultFirstLastStopRule.ShouldBe(FirstLastStopRule.Both);
        detail.DefaultExecution.ShouldBe(TripExecution.Subcontracted);

        var combined = await TypeAsync("COMBI", t => t.DefaultVehicleCombinationId = w.Combination);
        (await TripTypes.GetAsync(combined.Id)).DefaultVehicleCombinationName.ShouldBe("Combi 1");

        /* An archived default is still named (it still exists); the fill skips it, the editor shows it. */
        await GetRequiredService<IRelationAppService>().ArchiveAsync(w.Carrier.RelationId);
        (await TripTypes.GetAsync(created.Id)).DefaultCarrierRelationName.ShouldBe("Carrier One BV");

        var clear = UpdateOf(detail);
        clear.DefaultCarrierRelationId = null;
        clear.DefaultCarrierContactPersonId = null;
        clear.DefaultVehicleIds = [];
        clear.DefaultDriverIds = [];
        clear.DefaultDepartureRelationId = null;
        clear.DefaultDepartureAddressId = null;
        clear.DefaultDepartureTime = null;
        clear.DefaultArrivalRelationId = null;
        clear.DefaultArrivalAddressId = null;
        clear.DefaultStopCompositionMode = StopCompositionMode.MergeByLocationAndActivity;
        clear.DefaultFirstLastStopRule = FirstLastStopRule.None;
        var cleared = await TripTypes.UpdateAsync(created.Id, clear);
        cleared.DefaultCarrierRelationId.ShouldBeNull();
        cleared.DefaultCarrierRelationName.ShouldBeNull();
        cleared.DefaultCarrierContactPersonName.ShouldBeNull();
        cleared.DefaultVehicles.ShouldBeEmpty();
        cleared.DefaultDrivers.ShouldBeEmpty();
        cleared.DefaultDepartureRelationName.ShouldBeNull();
        cleared.DefaultDepartureAddressLabel.ShouldBeNull();
        cleared.DefaultDepartureTime.ShouldBeNull();
        cleared.DefaultArrivalAddressLabel.ShouldBeNull();
        cleared.DefaultStopCompositionMode.ShouldBe(StopCompositionMode.MergeByLocationAndActivity);
        cleared.DefaultFirstLastStopRule.ShouldBe(FirstLastStopRule.None);
    }

    [Fact]
    public async Task The_detail_names_a_default_the_lookup_no_longer_returns_not_found()
    {
        using var _ = InTenantA();
        using var culture = new CultureScope("en");
        var w = await WorldAsync();
        var type = await TypeAsync("GONE");
        var (vehicle, driver) = (Guid.NewGuid(), Guid.NewGuid());
        await RewriteAsync(type.Id, t =>
        {
            t.SetDefaultCarrier(Guid.NewGuid(), Guid.NewGuid());
            t.SetDefaultVehicles(null, [vehicle, w.Van]);
            t.SetDefaultDrivers([driver]);
            t.SetDefaultDeparture(Guid.NewGuid(), Guid.NewGuid(), null);
            t.SetDefaultArrival(w.Hub.RelationId, Guid.NewGuid());
        });
        var combined = await TypeAsync("GONECOMBI");
        await RewriteAsync(combined.Id, t => t.SetDefaultVehicles(Guid.NewGuid(), null));

        var detail = await TripTypes.GetAsync(type.Id);

        detail.DefaultCarrierRelationName.ShouldBe(NotFound);
        detail.DefaultCarrierContactPersonName.ShouldBe(NotFound);
        detail.DefaultVehicles.Select(v => (v.Id, v.Name, v.Kind)).ShouldBe([(vehicle, NotFound, (VehicleTypeKind?)null), (w.Van, "Van 01", VehicleTypeKind.Rigid)]);
        detail.DefaultDrivers.ShouldHaveSingleItem().Name.ShouldBe(NotFound);
        detail.DefaultDepartureRelationName.ShouldBe(NotFound);
        detail.DefaultDepartureAddressLabel.ShouldBe(NotFound);
        detail.DefaultArrivalRelationName.ShouldBe("Hub Venlo");
        detail.DefaultArrivalAddressLabel.ShouldBe(NotFound);
        (await TripTypes.GetAsync(combined.Id)).DefaultVehicleCombinationName.ShouldBe(NotFound);
    }

    [Fact]
    public async Task Another_tenants_master_data_is_refused_on_save_and_never_named_on_the_detail()
    {
        World a;
        using (InTenantA())
        {
            a = await WorldAsync();
        }

        using var _ = InTenantB();
        using var culture = new CultureScope("en");
        var b = await WorldAsync();

        var cases = new List<(string Field, Guid Value, Action<TransportTripTypeCreateDto> Apply)>
        {
            ("defaultCarrierRelationId", a.Carrier.RelationId, t => t.DefaultCarrierRelationId = a.Carrier.RelationId),
            ("defaultCarrierContactPersonId", a.Carrier.ContactId, t => { t.DefaultCarrierRelationId = b.Carrier.RelationId; t.DefaultCarrierContactPersonId = a.Carrier.ContactId; }),
            ("defaultVehicleCombinationId", a.Combination, t => t.DefaultVehicleCombinationId = a.Combination),
            ("defaultVehicleIds", a.Van, t => t.DefaultVehicleIds = [b.Van, a.Van]),
            ("defaultDriverIds", a.Driver1, t => t.DefaultDriverIds = [a.Driver1]),
            ("defaultDepartureRelationId", a.Depot.RelationId, t => t.DefaultDepartureRelationId = a.Depot.RelationId),
            ("defaultDepartureAddressId", a.Depot.AddressId, t => { t.DefaultDepartureRelationId = b.Depot.RelationId; t.DefaultDepartureAddressId = a.Depot.AddressId; }),
            ("defaultArrivalRelationId", a.Hub.RelationId, t => t.DefaultArrivalRelationId = a.Hub.RelationId),
            ("defaultArrivalAddressId", a.Hub.AddressId, t => { t.DefaultArrivalRelationId = b.Hub.RelationId; t.DefaultArrivalAddressId = a.Hub.AddressId; })
        };
        foreach (var (field, value, apply) in cases)
        {
            var error = await Should.ThrowAsync<BusinessException>(() => TypeAsync("X" + field.Length, apply));
            error.Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound, field);
            error.Data["field"].ShouldBe(field);
            error.Data["value"].ShouldBe(value);
        }

        (await TripTypes.GetListAsync(List())).TotalCount.ShouldBe(0);

        /* Stored past the validation, tenant A's ids are never named for tenant B. */
        var type = await TypeAsync("FOREIGN");
        await RewriteAsync(type.Id, t =>
        {
            t.SetDefaultCarrier(a.Carrier.RelationId, a.Carrier.ContactId);
            t.SetDefaultVehicles(null, [a.Van]);
            t.SetDefaultDrivers([a.Driver1]);
            t.SetDefaultDeparture(a.Depot.RelationId, a.Depot.AddressId, null);
            t.SetDefaultArrival(a.Hub.RelationId, a.Hub.AddressId);
        });
        var detail = await TripTypes.GetAsync(type.Id);
        detail.DefaultCarrierRelationName.ShouldBe(NotFound);
        detail.DefaultCarrierContactPersonName.ShouldBe(NotFound);
        detail.DefaultVehicles.ShouldHaveSingleItem().Name.ShouldBe(NotFound);
        detail.DefaultVehicles.Single().LicencePlate.ShouldBeNull();
        detail.DefaultDrivers.ShouldHaveSingleItem().Name.ShouldBe(NotFound);
        detail.DefaultDepartureRelationName.ShouldBe(NotFound);
        detail.DefaultDepartureAddressLabel.ShouldBe(NotFound);
        detail.DefaultArrivalRelationName.ShouldBe(NotFound);
        detail.DefaultArrivalAddressLabel.ShouldBe(NotFound);
    }
}
