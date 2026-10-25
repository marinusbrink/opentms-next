using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Fakes;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Relations;
using OpenTms.Orders.Events;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using Volo.Abp.Domain.Repositories;
using MasterDataNames = OpenTms.MasterData.LocalizedNameDto;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// Shared plumbing for the Planning &amp; Execution service tests on SQLite (design 59): the Master Data rows the trips
/// refer to (relations with geocoded addresses, vehicle types, vehicles, drivers) are created through the Master Data
/// services in the same tenant; the Orders facts come from the fake pricing reader (<see cref="PlanningFakeWorld"/>);
/// derived-value requests are recorded (<see cref="RecordedDerivedValueRequests"/>) since SQLite runs no transactions.
/// </summary>
public abstract class PlanningAppServiceTestBase : ReferenceDataAppServiceTestBase
{
    /// <summary>Orders waypoint type ids (no foreign key; design 56).</summary>
    protected static readonly Guid Load = Guid.Parse("11111111-1111-1111-1111-111111111111");

    protected static readonly Guid Unload = Guid.Parse("22222222-2222-2222-2222-222222222222");

    protected static readonly Guid DepotIn = Guid.Parse("33333333-3333-3333-3333-333333333333");

    protected static readonly Guid DepotOut = Guid.Parse("44444444-4444-4444-4444-444444444444");

    protected static readonly Guid Pallet = Guid.Parse("55555555-5555-5555-5555-555555555555");

    protected static readonly DateOnly Day = new(2026, 10, 26);

    protected static readonly TimeSpan Cet = TimeSpan.FromHours(2);

    protected PlanningFakeWorld World { get; }

    protected RecordedDerivedValueRequests Requests { get; }

    protected ITransportTripAppService Trips { get; }

    protected ILegAppService Legs { get; }

    protected IRepository<Leg, Guid> LegRepository { get; }

    protected IRepository<TransportTrip, Guid> TripRepository { get; }

    private readonly Dictionary<Guid, Guid> _countries = new();

    protected PlanningAppServiceTestBase()
    {
        World = GetRequiredService<PlanningFakeWorld>();
        Requests = GetRequiredService<RecordedDerivedValueRequests>();
        Trips = GetRequiredService<ITransportTripAppService>();
        Legs = GetRequiredService<ILegAppService>();
        LegRepository = GetRequiredService<IRepository<Leg, Guid>>();
        TripRepository = GetRequiredService<IRepository<TransportTrip, Guid>>();
    }

    protected static DateTimeOffset At(int hour, int minute = 0, int day = 26) => new(2026, 10, day, hour, minute, 0, Cet);

    /* Master Data fixtures */

    protected async Task<Guid> CountryAsync()
    {
        var tenant = CurrentTenant.Id ?? Guid.Empty;
        if (_countries.TryGetValue(tenant, out var id))
        {
            return id;
        }

        var country = await GetRequiredService<ICountryAppService>().CreateAsync(new CountryCreateDto
        {
            IsoAlpha2 = "NL",
            IsoAlpha3 = "NLD",
            Names = new MasterDataNames { ["en"] = "Netherlands", ["nl"] = "Nederland" },
            PrimaryTimeZoneId = "Europe/Amsterdam"
        });
        _countries[tenant] = country.Id;
        return country.Id;
    }

    /// <summary>A relation with one geocoded address; returns the relation and address ids.</summary>
    protected async Task<(Guid RelationId, Guid AddressId)> PlaceAsync(string code, string name, double latitude, double longitude, RelationRoles roles = RelationRoles.Customer | RelationRoles.Depot, string city = "Amsterdam")
    {
        var relations = GetRequiredService<IRelationAppService>();
        var relation = await relations.CreateAsync(new RelationCreateDto
        {
            Code = code,
            Name = name,
            Roles = roles,
            Addresses =
            [
                new RelationAddressDto
                {
                    CountryId = await CountryAsync(),
                    Street = "Main street",
                    HouseNumber = "1",
                    PostalCode = "1000AA",
                    City = city,
                    Latitude = latitude,
                    Longitude = longitude,
                    TimeZoneId = "Europe/Amsterdam"
                }
            ]
        });
        var address = (await relations.GetAddressesAsync(relation.Id)).Single();
        return (relation.Id, address.Id!.Value);
    }

    protected async Task<Guid> CharacteristicAsync(string code)
        => (await GetRequiredService<ITransportCharacteristicsAppService>().CreateAsync(new TransportCharacteristicsCreateDto { Code = code, Name = code })).Id;

    protected async Task<Guid> VehicleTypeAsync(string name, VehicleTypeKind kind, double? maxEpq = null, double? maxLoadingMeters = null, params Guid[] characteristics)
        => (await GetRequiredService<IVehicleTypeAppService>().CreateAsync(new VehicleTypeCreateDto
        {
            Name = name,
            Kind = kind,
            MaxEpq = maxEpq,
            MaxLoadingMeters = maxLoadingMeters,
            TransportCharacteristicsIds = characteristics.ToList()
        })).Id;

    protected async Task<Guid> VehicleAsync(string name, Guid typeId, Guid ownerRelationId, double? grossKg = 18_000, double? maxPayloadKg = null, EuroClass? euroClass = EuroClass.Euro6, params Guid[] characteristics)
        => (await GetRequiredService<IVehicleAppService>().CreateAsync(new VehicleCreateDto
        {
            Name = name,
            VehicleTypeId = typeId,
            OwnerRelationId = ownerRelationId,
            GrossVehicleWeightKg = grossKg,
            MaxPayloadKg = maxPayloadKg,
            EuroClass = euroClass,
            FuelType = FuelType.Diesel,
            TransportCharacteristicIds = characteristics.ToList()
        })).Id;

    protected async Task<Guid> CombinationAsync(string name, params Guid[] vehicleIds)
        => (await GetRequiredService<IVehicleCombinationAppService>().CreateAsync(new VehicleCombinationCreateDto
        {
            Name = name,
            Members = vehicleIds.Select((id, i) => new VehicleCombinationMemberInputDto { VehicleId = id, Position = i + 1 }).ToList()
        })).Id;

    protected async Task<Guid> DriverAsync(string code, string firstName, string lastName)
        => (await GetRequiredService<IDriverAppService>().CreateAsync(new DriverCreateDto { Code = code, FirstName = firstName, LastName = lastName })).Id;

    protected async Task<Guid> TripTypeAsync(string code = "STD", TripExecution execution = TripExecution.OwnFleet)
        => (await GetRequiredService<ITransportTripTypeAppService>().CreateAsync(new TransportTripTypeCreateDto
        {
            Code = code,
            Names = Names(code),
            Numbering = Numbering("TR", year: true),
            DefaultExecution = execution
        })).Id;

    /* Orders facts (the fake pricing reader) */

    protected static PricingStopDto StopAt((Guid RelationId, Guid AddressId) place, Guid waypointTypeId, DateOnly date, string name, double latitude, double longitude,
        DateTimeOffset? windowFrom = null, DateTimeOffset? windowTill = null, int? plannedDurationMin = 30, string? city = null, params Guid[] regions) => new()
    {
        StopId = Guid.NewGuid(),
        RelationId = place.RelationId,
        RelationAddressId = place.AddressId,
        WaypointTypeId = waypointTypeId,
        Date = date,
        WindowFrom = windowFrom,
        WindowTill = windowTill,
        PlannedDurationMin = plannedDurationMin,
        Latitude = latitude,
        Longitude = longitude,
        TimeZoneId = "Europe/Amsterdam",
        AddressName = name,
        City = city ?? name,
        CountryCode = "NL",
        GeographicalRegionIds = regions.ToList()
    };

    protected static PricingLineDto LineOf(decimal quantity, double? weightKg, params PricingStopDto[] stops) => new()
    {
        LineId = Guid.NewGuid(),
        Position = 1,
        TransportUnitId = Pallet,
        Quantity = quantity,
        WeightKg = weightKg,
        LoadingMeters = (double)quantity * 0.4,
        Epq = (double)quantity,
        Stops = stops.Select((s, i) => { s.Position = i + 1; return s; }).ToList()
    };

    /// <summary>Registers an Open order with the fake reader for the current tenant.</summary>
    protected PricingSnapshotDto Order(Guid customerRelationId, params PricingLineDto[] lines)
    {
        var snapshot = new PricingSnapshotDto
        {
            OrderId = Guid.NewGuid(),
            CustomerRelationId = customerRelationId,
            OrderDate = Day,
            Status = (int)TransportOrderStatus.Open,
            Lines = lines.Select((l, i) => { l.Position = i + 1; return l; }).ToList()
        };
        World.Register(CurrentTenant.Id, snapshot, customerName: "Customer " + customerRelationId.ToString("N"));
        return snapshot;
    }

    protected OrderLegsHandler Handler => GetRequiredService<OrderLegsHandler>();

    /// <summary>Registers the order and lets Planning react to its confirmation (legs, and the trip when requested).</summary>
    protected async Task<Guid> ConfirmOrderAsync(PricingSnapshotDto snapshot)
    {
        await Handler.HandleEventAsync(new TransportOrderConfirmedEto { OrderId = snapshot.OrderId!.Value });
        return snapshot.OrderId.Value;
    }

    protected Task<List<Leg>> LegsOfOrderAsync(Guid orderId) => LegRepository.GetListAsync(l => l.OrderId == orderId);

    protected async Task<List<Guid>> LegIdsOfOrderAsync(Guid orderId) => (await LegsOfOrderAsync(orderId)).OrderBy(l => l.OrderLineId).ThenBy(l => l.Position).Select(l => l.Id).ToList();

    /* Trips */

    protected static TransportTripCreateDto NewTrip(Guid typeId, DateTimeOffset departure, DateTimeOffset? arrival = null, StopCompositionMode mode = StopCompositionMode.MergeByLocationAndActivity,
        FirstLastStopRule rule = FirstLastStopRule.None, string? name = null) => new()
    {
        TransportTripTypeId = typeId,
        PlannedDeparture = departure,
        PlannedArrival = arrival,
        StopCompositionMode = mode,
        FirstLastStopRule = rule,
        Name = name
    };

    protected Task<TransportTripDto> CreateTripAsync(Guid typeId, DateTimeOffset? departure = null, DateTimeOffset? arrival = null, StopCompositionMode mode = StopCompositionMode.MergeByLocationAndActivity,
        FirstLastStopRule rule = FirstLastStopRule.None, string? name = null)
        => Trips.CreateAsync(NewTrip(typeId, departure ?? At(6), arrival, mode, rule, name));

    protected static TransportTripUpdateDto UpdateFrom(TransportTripDto dto) => new()
    {
        Name = dto.Name,
        TripDate = dto.TripDate,
        Execution = dto.Execution,
        StopCompositionMode = dto.StopCompositionMode,
        FirstLastStopRule = dto.FirstLastStopRule,
        DepartureRelationId = dto.DepartureRelationId,
        DepartureAddressId = dto.DepartureAddressId,
        ArrivalRelationId = dto.ArrivalRelationId,
        ArrivalAddressId = dto.ArrivalAddressId,
        PlannedDeparture = dto.PlannedDeparture,
        PlannedArrival = dto.PlannedArrival,
        ActualDistanceM = dto.ActualDistanceM,
        Remarks = dto.Remarks,
        DriverInstructions = dto.DriverInstructions,
        OrganizationUnitId = dto.OrganizationUnitId,
        Attributes = new Dictionary<string, string>(dto.Attributes),
        ConcurrencyStamp = dto.ConcurrencyStamp
    };

    protected static TransportTripListRequestDto TripList(string? search = null, string? view = null, bool? archived = null, bool hideEmpty = false, string? sortBy = null, string? sortDir = null,
        List<TransportTripStatus>? status = null, DateOnly? dateFrom = null, DateOnly? dateTo = null, Guid? carrierRelationId = null)
        => new() { StartRow = 0, EndRow = 100, WildcardSearch = search, View = view, IsArchived = archived, HideEmpty = hideEmpty, SortBy = sortBy, SortDir = sortDir, Status = status, DateFrom = dateFrom, DateTo = dateTo, CarrierRelationId = carrierRelationId };

    protected static LegListRequestDto LegList(Guid? orderId = null, Guid? tripId = null, DateOnly? dateFrom = null, DateOnly? dateTo = null, Guid? customerRelationId = null, string? search = null, string? sortBy = null)
        => new() { StartRow = 0, EndRow = 100, OrderId = orderId, TripId = tripId, DateFrom = dateFrom, DateTo = dateTo, CustomerRelationId = customerRelationId, WildcardSearch = search, SortBy = sortBy };
}
