using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.DerivedValues;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Relations;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Availability;
using OpenTms.PlanningExecution.Planboard;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.RecordUpdates;
using Volo.Abp.Identity;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// Shared plumbing of the Planboard tests (design 68) on SQLite: a fleet with every vehicle kind, default drivers on a
/// tractor, an own company and a charter carrier, the Planboard and availability services, the record channel's captured
/// notices, and the trip calculator run in place of the design-49 job (SQLite records the requests instead of running it).
/// </summary>
public abstract class PlanboardTestBase : PlanningAppServiceTestBase
{
    protected IPlanboardAppService Planboard => GetRequiredService<IPlanboardAppService>();

    protected IResourceAvailabilityAppService Availability => GetRequiredService<IResourceAvailabilityAppService>();

    protected CapturingRecordUpdateTransport Sent => GetRequiredService<CapturingRecordUpdateTransport>();

    protected sealed record FleetSet(
        Guid Type,
        (Guid RelationId, Guid AddressId) Depot,
        (Guid RelationId, Guid AddressId) CustomerA,
        (Guid RelationId, Guid AddressId) CustomerB,
        Guid OwnCompany,
        Guid Carrier,
        Guid Tractor,
        Guid CharterTractor,
        Guid Rigid,
        Guid Van,
        Guid Trailer1,
        Guid Trailer2,
        Guid Driver1,
        Guid Driver2,
        Guid Driver3,
        Guid Driver4,
        Guid DefaultDriver);

    /// <summary>
    /// The fleet of the current tenant: the own company is the depot (Utrecht); the tractor "Truck 01" belongs to it and has
    /// the default driver "D9"; "Charter 01" belongs to the carrier and has no default drivers.
    /// </summary>
    protected async Task<FleetSet> FleetAsync()
    {
        var type = await TripTypeAsync();
        var depot = await PlaceAsync("DEPOT", "Depot Utrecht", 52.09, 5.12, RelationRoles.Depot | RelationRoles.OwnCompany, "Utrecht");
        var a = await PlaceAsync("CUSTA", "Customer A", 52.37, 4.90, RelationRoles.Customer, "Amsterdam");
        var b = await PlaceAsync("CUSTB", "Customer B", 51.92, 4.48, RelationRoles.Customer, "Rotterdam");
        var carrier = await PlaceAsync("CARR", "Charter Carrier", 51.37, 6.17, RelationRoles.Carrier, "Venlo");

        var tractorType = await VehicleTypeAsync("Tractor", VehicleTypeKind.Tractor);
        var rigidType = await VehicleTypeAsync("Rigid", VehicleTypeKind.Rigid, maxEpq: 18, maxLoadingMeters: 7.2);
        var vanType = await VehicleTypeAsync("Van", VehicleTypeKind.Van, maxEpq: 4, maxLoadingMeters: 2.4);
        var trailerType = await VehicleTypeAsync("Trailer", VehicleTypeKind.Trailer, maxEpq: 33, maxLoadingMeters: 13.6);

        var driver1 = await DriverAsync("D1", "Anna", "Aalders");
        var driver2 = await DriverAsync("D2", "Bram", "Bakker");
        var driver3 = await DriverAsync("D3", "Cor", "Claassen");
        var driver4 = await DriverAsync("D4", "Dirk", "Dekker");
        var defaultDriver = await DriverAsync("D9", "Fleur", "Visser");

        var tractor = (await GetRequiredService<IVehicleAppService>().CreateAsync(new VehicleCreateDto
        {
            Name = "Truck 01", VehicleTypeId = tractorType, OwnerRelationId = depot.RelationId, GrossVehicleWeightKg = 18_000, EuroClass = EuroClass.Euro6,
            FuelType = FuelType.Diesel, DefaultDrivers = [new VehicleDefaultDriverDto { DriverId = defaultDriver, SortOrder = 1 }]
        })).Id;
        var charter = await VehicleAsync("Charter 01", tractorType, carrier.RelationId);
        var rigid = await VehicleAsync("Box 01", rigidType, depot.RelationId);
        var van = await VehicleAsync("Van 01", vanType, depot.RelationId);
        var trailer1 = await VehicleAsync("Trailer 01", trailerType, depot.RelationId, grossKg: 24_000, maxPayloadKg: 24_000, euroClass: null);
        var trailer2 = await VehicleAsync("Trailer 02", trailerType, depot.RelationId, grossKg: 24_000, maxPayloadKg: 24_000, euroClass: null);

        return new FleetSet(type, depot, a, b, depot.RelationId, carrier.RelationId, tractor, charter, rigid, van, trailer1, trailer2,
            driver1, driver2, driver3, driver4, defaultDriver);
    }

    /// <summary>A trip with the given resources, created through the trip service (as the Transport app does).</summary>
    protected Task<TransportTripDto> TripWithAsync(Guid type, DateTimeOffset departure, DateTimeOffset? arrival, IEnumerable<Guid>? vehicles = null, IEnumerable<Guid>? drivers = null,
        Guid? carrier = null, Guid? combination = null, Guid? organizationUnitId = null)
        => Trips.CreateAsync(new TransportTripCreateDto
        {
            TransportTripTypeId = type,
            PlannedDeparture = departure,
            PlannedArrival = arrival,
            VehicleIds = vehicles?.ToList() ?? [],
            DriverIds = drivers?.ToList() ?? [],
            CarrierRelationId = carrier,
            VehicleCombinationId = combination,
            OrganizationUnitId = organizationUnitId
        });

    /// <summary>A line from → to on one day with an origin window of two hours from <paramref name="fromHour"/>.</summary>
    protected TransportOrderLineInputDto Line((Guid, Guid) from, (Guid, Guid) to, decimal quantity = 10, int fromHour = 8, int toHour = 12, int day = 26, double? weightKg = 1000)
        => LineOf(quantity, weightKg,
            StopAt(from, Load, new DateOnly(2026, 10, day), At(fromHour, 0, day), At(fromHour + 2, 0, day)),
            StopAt(to, Unload, new DateOnly(2026, 10, day), At(toHour, 0, day), At(toHour + 2, 0, day)));

    /// <summary>Runs the trip's calculation as the design-49 job would (SQLite only records the request).</summary>
    protected Task CalculateAsync(Guid tripId, long version = 1)
        => WithUnitOfWorkAsync(() => GetRequiredService<TripCalculator>().CalculateAsync(
            new DerivedValueCalculationContext(PlanningExecutionApplicationModule.TripTargetType, tripId, version, default)));

    protected async Task<Guid> OrganizationUnitAsync(string name)
    {
        var id = Guid.NewGuid();
        await WithUnitOfWorkAsync(() => GetRequiredService<OrganizationUnitManager>().CreateAsync(new OrganizationUnit(id, name, null, CurrentTenant.Id)));
        return id;
    }

    /// <summary>
    /// A wall-clock time in Amsterdam from 25 October 2026 03:00 on (CET, +01:00 — the base's <c>At</c> uses +02:00, which
    /// is an hour off local time after the autumn change; availability and working patterns are local).
    /// </summary>
    protected static DateTimeOffset Local(int hour, int minute = 0, int day = 26) => new(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(1));

    /// <summary>The notices of one record captured so far.</summary>
    protected IReadOnlyList<RecordUpdate> NoticesOf(Guid id) => Sent.For(id);

    /// <summary>Instants of a Monday some weeks ahead of the real clock (availability fan-out only looks from yesterday on).</summary>
    protected static DateTimeOffset Future(int hour, int dayOffset = 0, int minute = 0)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 + 21).AddDays(dayOffset);
        var local = monday.ToDateTime(new TimeOnly(hour, minute));
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    protected static PlanboardSchedulerRequestDto Window(PlanboardResourceMode mode, DateTimeOffset from, DateTimeOffset to, int take = 50, string? search = null, params Guid[] units)
        => new() { Mode = mode, From = from, To = to, Take = take, Search = search, OrganizationUnitIds = [.. units] };
}
