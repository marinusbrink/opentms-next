using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Relations;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.TestDoubles;
using OpenTms.TransportRun;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// Shared plumbing for the Planning &amp; Execution service tests on SQLite (design 59): the Master Data rows the trips
/// refer to (relations with geocoded addresses, vehicle types, vehicles, drivers) and the Orders master data (waypoint
/// types, a transport unit, the order type) are created through the real services in the same tenant; orders are real
/// Orders rows confirmed through the Orders service (SQLite delivers the facts in process, so Planning's handler
/// generates the legs); routing and CO₂ come from <see cref="TestRoutingWorld"/>; derived-value requests are recorded
/// (<see cref="RecordedDerivedValueRequests"/>) since SQLite runs no transactions.
/// </summary>
public abstract class PlanningAppServiceTestBase : OpenTmsTestBase<PlanningTestModule>
{
    protected static readonly DateOnly Day = new(2026, 10, 26);

    protected static readonly TimeSpan Cet = TimeSpan.FromHours(2);

    protected Volo.Abp.MultiTenancy.ICurrentTenant CurrentTenant { get; }

    protected Guid TenantA { get; } = Guid.NewGuid();

    protected Guid TenantB { get; } = Guid.NewGuid();

    protected TestRoutingWorld World { get; }

    protected RecordedDerivedValueRequests Requests { get; }

    protected ITransportTripAppService Trips { get; }

    protected ILegAppService Legs { get; }

    protected IRepository<Leg, Guid> LegRepository { get; }

    protected IRepository<TransportTrip, Guid> TripRepository { get; }

    protected TransportRunFixture Run => GetRequiredService<TransportRunFixture>();

    protected ITransportOrderAppService Orders => GetRequiredService<ITransportOrderAppService>();

    protected OrderLegsHandler Handler => GetRequiredService<OrderLegsHandler>();

    /// <summary>The Orders master data of one tenant (no foreign keys from Planning; design 56).</summary>
    protected sealed record OrdersMasterData(Guid Load, Guid Unload, Guid DepotIn, Guid DepotOut, Guid Pallet, Guid OrderTypeId);

    private readonly Dictionary<Guid, OrdersMasterData> _ordersMasterData = new();

    protected PlanningAppServiceTestBase()
    {
        CurrentTenant = GetRequiredService<Volo.Abp.MultiTenancy.ICurrentTenant>();
        World = GetRequiredService<TestRoutingWorld>();
        Requests = GetRequiredService<RecordedDerivedValueRequests>();
        Trips = GetRequiredService<ITransportTripAppService>();
        Legs = GetRequiredService<ILegAppService>();
        LegRepository = GetRequiredService<IRepository<Leg, Guid>>();
        TripRepository = GetRequiredService<IRepository<TransportTrip, Guid>>();
    }

    protected IDisposable InTenantA() => CurrentTenant.Change(TenantA);

    protected IDisposable InTenantB() => CurrentTenant.Change(TenantB);

    protected static DateTimeOffset At(int hour, int minute = 0, int day = 26) => new(2026, 10, day, hour, minute, 0, Cet);

    protected static OpenTms.Platform.ReferenceData.LocalizedNameDto Names(string en, string? nl = null) => TransportRunFixture.Names(en, nl);

    protected static OpenTms.Platform.ReferenceData.ArchivableListRequestDto List(int start = 0, int end = 100, string? search = null, string? sortBy = null, string? sortDir = null, bool includeArchived = false)
        => new() { StartRow = start, EndRow = end, WildcardSearch = search, SortBy = sortBy, SortDir = sortDir, IncludeArchived = includeArchived };

    protected static OpenTms.Platform.Grid.GridSelectionDto Explicit(params Guid[] ids) => new() { Mode = "Explicit", ExplicitIds = [.. ids] };

    protected static OpenTms.Platform.ReferenceData.NumberSequenceFormatDto Numbering(string? prefix = "X", bool year = false, bool month = false, string separator = "-", int digits = 5)
        => new() { Prefix = prefix, IncludeYear = year, IncludeMonth = month, Separator = separator, Digits = digits };

    /* Orders master data of the current tenant */

    private OrdersMasterData Current => _ordersMasterData.TryGetValue(CurrentTenant.Id ?? Guid.Empty, out var md)
        ? md
        : throw new InvalidOperationException("Call PlaceAsync, TripTypeAsync or OrdersMasterDataAsync first: the Orders master data of this tenant is not built yet.");

    protected Guid Load => Current.Load;

    protected Guid Unload => Current.Unload;

    protected Guid DepotIn => Current.DepotIn;

    protected Guid DepotOut => Current.DepotOut;

    protected Guid Pallet => Current.Pallet;

    protected Guid OrderTypeId => Current.OrderTypeId;

    /// <summary>Builds the tenant's Orders master data once: LOAD/UNLOAD, the depot pair (DEPOTIN whose successor is DEPOTOUT), a pallet unit, the order type.</summary>
    protected async Task<OrdersMasterData> OrdersMasterDataAsync()
    {
        var tenant = CurrentTenant.Id ?? Guid.Empty;
        if (_ordersMasterData.TryGetValue(tenant, out var existing))
        {
            return existing;
        }

        await Run.CountryAsync();
        await Run.CurrencyAsync("EUR");
        var load = await Run.WaypointTypeAsync("LOAD", "Load", "Laden", WaypointActivity.Load, 30);
        var unload = await Run.WaypointTypeAsync("UNLOAD", "Unload", "Lossen", WaypointActivity.Unload, 30);
        var depotOut = await Run.WaypointTypeAsync("DEPOTOUT", "Depot out", "Depot uit", WaypointActivity.Load, 15);
        var depotIn = await Run.WaypointTypeAsync("DEPOTIN", "Depot in", "Depot in", WaypointActivity.Unload, 15, successorId: depotOut);
        var pallet = await Run.TransportUnitAsync("Pallet");
        var orderType = await Run.OrderTypeAsync(TransportOrderRequiredFields.None, TransportRunFixture.NewSuffix(), load, unload);
        var md = new OrdersMasterData(load, unload, depotIn, depotOut, pallet, orderType);
        _ordersMasterData[tenant] = md;
        return md;
    }

    /* Master Data fixtures */

    protected async Task<Guid> CountryAsync()
    {
        await OrdersMasterDataAsync();
        return await Run.CountryAsync();
    }

    /// <summary>A relation with one geocoded address; returns the relation and address ids.</summary>
    protected async Task<(Guid RelationId, Guid AddressId)> PlaceAsync(string code, string name, double latitude, double longitude, RelationRoles roles = RelationRoles.Customer | RelationRoles.Depot, string city = "Amsterdam")
    {
        await OrdersMasterDataAsync();
        var place = await Run.PlaceAsync(code, name, roles, latitude, longitude, city);
        return (place.RelationId, place.AddressId);
    }

    protected async Task<Guid> CharacteristicAsync(string code)
        => (await GetRequiredService<ITransportCharacteristicsAppService>().CreateAsync(new TransportCharacteristicsCreateDto { Code = code, Name = code })).Id;

    protected Task<Guid> VehicleTypeAsync(string name, VehicleTypeKind kind, double? maxEpq = null, double? maxLoadingMeters = null, params Guid[] characteristics)
        => Run.VehicleTypeAsync(name, kind, maxEpq, maxLoadingMeters, null, characteristics);

    protected Task<Guid> VehicleAsync(string name, Guid typeId, Guid ownerRelationId, double? grossKg = 18_000, double? maxPayloadKg = null, EuroClass? euroClass = EuroClass.Euro6, params Guid[] characteristics)
        => Run.VehicleAsync(name, typeId, ownerRelationId, grossKg, maxPayloadKg, euroClass, null, null, characteristics);

    protected async Task<Guid> CombinationAsync(string name, params Guid[] vehicleIds)
        => (await GetRequiredService<IVehicleCombinationAppService>().CreateAsync(new VehicleCombinationCreateDto
        {
            Name = name,
            Members = vehicleIds.Select((id, i) => new VehicleCombinationMemberInputDto { VehicleId = id, Position = i + 1 }).ToList()
        })).Id;

    protected Task<Guid> DriverAsync(string code, string firstName, string lastName) => Run.DriverAsync(code, firstName, lastName);

    protected async Task<Guid> TripTypeAsync(string code = "STD", TripExecution execution = TripExecution.OwnFleet)
    {
        await OrdersMasterDataAsync();
        return await Run.TripTypeAsync(code, execution);
    }

    /* Orders (real rows through the Orders service) */

    /// <summary>A stop of an order line at a place; the window is given as instants of the day, stored as the local times of the address.</summary>
    protected static TransportOrderStopInputDto StopAt((Guid RelationId, Guid AddressId) place, Guid waypointTypeId, DateOnly date,
        DateTimeOffset? windowFrom = null, DateTimeOffset? windowTill = null, int? plannedDurationMin = 30, string? reference = null)
        => TransportRunFixture.Stop(waypointTypeId, place.RelationId, place.AddressId, date, reference, null,
            windowFrom == null ? null : TimeOnly.FromTimeSpan(windowFrom.Value.TimeOfDay), windowTill == null ? null : TimeOnly.FromTimeSpan(windowTill.Value.TimeOfDay), plannedDurationMin);

    protected TransportOrderLineInputDto LineOf(decimal quantity, double? weightKg, params TransportOrderStopInputDto[] stops) => new()
    {
        TransportUnitId = Pallet,
        Quantity = quantity,
        WeightKg = weightKg,
        LoadingMeters = (double)quantity * 0.4,
        Epq = (double)quantity,
        Stops = stops.ToList()
    };

    /// <summary>A Draft order of the customer with the given lines, dated <see cref="Day"/>, in the current tenant.</summary>
    protected Task<TransportOrderDto> OrderAsync(Guid customerRelationId, params TransportOrderLineInputDto[] lines) => OrderAsync(customerRelationId, null, lines);

    /// <summary>A Draft order with a "Tevens trip" request (Q9), honoured by Planning on the confirm.</summary>
    protected async Task<TransportOrderDto> OrderAsync(Guid customerRelationId, TransportOrderTripRequestInputDto? tripRequest, params TransportOrderLineInputDto[] lines)
    {
        await OrdersMasterDataAsync();
        return await Orders.CreateAsync(new TransportOrderCreateDto
        {
            TransportOrderTypeId = OrderTypeId,
            CustomerRelationId = customerRelationId,
            OrderDate = Day,
            Lines = lines.ToList(),
            TripRequest = tripRequest
        });
    }

    /// <summary>Confirms the order: Planning reacts to the fact in process (legs, and the trip when requested). Returns the order id.</summary>
    protected async Task<Guid> ConfirmOrderAsync(TransportOrderDto order)
    {
        await Orders.ConfirmAsync(order.Id);
        return order.Id;
    }

    /// <summary>A confirmed order of the customer with the given lines.</summary>
    protected async Task<Guid> ConfirmedOrderAsync(Guid customerRelationId, params TransportOrderLineInputDto[] lines)
        => await ConfirmOrderAsync(await OrderAsync(customerRelationId, lines));

    protected static List<TransportOrderLineInputDto> LinesOf(TransportOrderDto dto)
        => dto.Lines.Select(l => new TransportOrderLineInputDto
        {
            Id = l.Id, TransportUnitId = l.TransportUnitId, ProductId = l.ProductId, Quantity = l.Quantity, WeightKg = l.WeightKg, LoadingMeters = l.LoadingMeters, Epq = l.Epq, Remarks = l.Remarks,
            Stops = l.Stops.Select(s => new TransportOrderStopInputDto
            {
                Id = s.Id, WaypointTypeId = s.WaypointTypeId, RelationId = s.RelationId, RelationAddressId = s.RelationAddressId, Date = s.Date,
                TimeSlotId = s.TimeSlotId, WindowFrom = s.WindowFrom, WindowTill = s.WindowTill, Reference = s.Reference, Remarks = s.Remarks, PlannedDurationMin = s.PlannedDurationMin
            }).ToList()
        }).ToList();

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
