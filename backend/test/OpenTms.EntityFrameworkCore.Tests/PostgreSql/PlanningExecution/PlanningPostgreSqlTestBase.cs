using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Fakes;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Relations;
using OpenTms.Orders.Events;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.PostgreSql.ReferenceData;
using Volo.Abp.Domain.Repositories;
using MasterDataNames = OpenTms.MasterData.LocalizedNameDto;

namespace OpenTms.PostgreSql.PlanningExecution;

/// <summary>
/// Shared plumbing for the Planning &amp; Execution tests against real PostgreSQL (design 59): the seeded default trip
/// type of each tenant database, Master Data places created in the same transaction, the Orders facts from the fake
/// pricing reader, legs generated through the real handler. Every helper runs inside the caller's unit of work.
/// </summary>
public abstract class PlanningPostgreSqlTestBase : ReferenceDataPostgreSqlTestBase
{
    protected static readonly Guid Load = Guid.Parse("11111111-1111-1111-1111-111111111111");

    protected static readonly Guid Unload = Guid.Parse("22222222-2222-2222-2222-222222222222");

    protected static readonly Guid DepotIn = Guid.Parse("33333333-3333-3333-3333-333333333333");

    protected static readonly Guid Pallet = Guid.Parse("55555555-5555-5555-5555-555555555555");

    protected static readonly DateOnly Day = new(2026, 10, 26);

    protected PlanningFakeWorld World => GetRequiredService<PlanningFakeWorld>();

    protected OrderLegsHandler Handler => GetRequiredService<OrderLegsHandler>();

    protected TripOperations Operations => GetRequiredService<TripOperations>();

    protected IRepository<Leg, Guid> LegRepository => GetRequiredService<IRepository<Leg, Guid>>();

    protected IRepository<TransportTrip, Guid> TripRepository => GetRequiredService<IRepository<TransportTrip, Guid>>();

    protected static DateTimeOffset At(int hour, int minute = 0, int day = 26) => new(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(2));

    /// <summary>The default trip type the DbMigrator seeded in the current tenant.</summary>
    protected async Task<Guid> SeededTripTypeAsync()
        => (await GetRequiredService<IRepository<TransportTripType, Guid>>().GetListAsync()).OrderBy(t => t.IsDefault ? 0 : 1).First().Id;

    protected async Task<(Guid RelationId, Guid AddressId)> PlaceAsync(string code, string name, double latitude, double longitude, string city = "Amsterdam")
    {
        var countries = GetRequiredService<ICountryAppService>();
        var country = (await countries.GetListAsync(new OpenTms.MasterData.MasterDataListRequestDto { StartRow = 0, EndRow = 10, WildcardSearch = "NL" })).Rows.FirstOrDefault(c => c.IsoAlpha2 == "NL");
        var countryId = country?.Id ?? (await countries.CreateAsync(new CountryCreateDto
        {
            IsoAlpha2 = "NL", IsoAlpha3 = "NLD", Names = new MasterDataNames { ["en"] = "Netherlands" }, PrimaryTimeZoneId = "Europe/Amsterdam"
        })).Id;
        var relations = GetRequiredService<IRelationAppService>();
        var relation = await relations.CreateAsync(new RelationCreateDto
        {
            Code = code,
            Name = name,
            Roles = RelationRoles.Customer | RelationRoles.Depot,
            Addresses = [new RelationAddressDto { CountryId = countryId, Street = "Street", PostalCode = "1000AA", City = city, Latitude = latitude, Longitude = longitude, TimeZoneId = "Europe/Amsterdam" }]
        });
        await FlushAsync();
        return (relation.Id, (await relations.GetAddressesAsync(relation.Id)).Single().Id!.Value);
    }

    protected static PricingStopDto StopAt(Guid relationId, Guid addressId, Guid waypointTypeId, string name, double latitude, double longitude, DateOnly? date = null,
        DateTimeOffset? windowFrom = null, DateTimeOffset? windowTill = null, string? city = null, params Guid[] regions) => new()
    {
        StopId = Guid.NewGuid(),
        RelationId = relationId,
        RelationAddressId = addressId,
        WaypointTypeId = waypointTypeId,
        Date = date ?? Day,
        WindowFrom = windowFrom,
        WindowTill = windowTill,
        PlannedDurationMin = 30,
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

    /// <summary>Registers an Open order with the fake reader for the tenant and returns its id.</summary>
    protected Guid RegisterOrder(Guid? tenantId, Guid customerRelationId, params PricingLineDto[] lines)
    {
        var snapshot = new PricingSnapshotDto
        {
            OrderId = Guid.NewGuid(),
            CustomerRelationId = customerRelationId,
            OrderDate = Day,
            Status = (int)TransportOrderStatus.Open,
            Lines = lines.Select((l, i) => { l.Position = i + 1; return l; }).ToList()
        };
        World.Register(tenantId, snapshot, customerName: "Customer " + customerRelationId.ToString("N"));
        return snapshot.OrderId!.Value;
    }

    protected async Task<List<Leg>> ConfirmAsync(Guid orderId)
    {
        await Handler.HandleEventAsync(new TransportOrderConfirmedEto { OrderId = orderId });
        await FlushAsync();
        return (await LegRepository.GetListAsync(l => l.OrderId == orderId)).OrderBy(l => l.OrderLineId).ThenBy(l => l.Position).ToList();
    }

    protected Task<TransportTrip> CreateTripAsync(Guid typeId, DateTimeOffset? departure = null, DateTimeOffset? arrival = null, StopCompositionMode mode = StopCompositionMode.MergeByLocationAndActivity)
        => Operations.CreateAsync(new TransportTripCreateDto
        {
            TransportTripTypeId = typeId,
            PlannedDeparture = departure ?? At(6),
            PlannedArrival = arrival ?? At(18),
            StopCompositionMode = mode
        }, null, null, null);
}
