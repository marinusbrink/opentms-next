using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Fakes;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using OpenTms.Orders.TransportOrders;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using MasterDataNames = OpenTms.MasterData.LocalizedNameDto;

namespace OpenTms.EntityFrameworkCore.Orders;

/// <summary>
/// Shared plumbing for the transport order service tests on SQLite (design 58): the Master Data rows an order refers to
/// (country, customer and stop relations with addresses, a transport unit, a product) created through the Master Data
/// services in the same tenant, the Orders master data (waypoint types, a time slot, the order type) through the Orders
/// services, and the fakes of Financial's and Planning's published contracts (Fakes/OrdersFakes.cs). Today is the date
/// the services use.
/// </summary>
public abstract class TransportOrderTestBase<TModule> : OpenTmsTestBase<TModule> where TModule : IAbpModule
{
    protected ICurrentTenant CurrentTenant { get; }

    protected Guid TenantA { get; } = Guid.NewGuid();

    protected Guid TenantB { get; } = Guid.NewGuid();

    protected ITransportOrderAppService Orders => GetRequiredService<ITransportOrderAppService>();

    protected FakeFinancialReferences Financial => GetRequiredService<FakeFinancialReferences>();

    protected FakeRateLines RateLines => GetRequiredService<FakeRateLines>();

    protected FakeLegs Legs => GetRequiredService<FakeLegs>();

    protected FakeTrips Trips => GetRequiredService<FakeTrips>();

    protected RecordingDerivedValueScheduler DerivedValues => GetRequiredService<RecordingDerivedValueScheduler>();

    protected OrdersEventRecorder Events => GetRequiredService<OrdersEventRecorder>();

    protected DateOnly Today { get; }

    protected TransportOrderTestBase()
    {
        CurrentTenant = GetRequiredService<ICurrentTenant>();
        Today = GetRequiredService<OrdersClock>().Today();
    }

    protected IDisposable InTenantA() => CurrentTenant.Change(TenantA);

    protected IDisposable InTenantB() => CurrentTenant.Change(TenantB);

    protected static CultureScope UiCulture(string culture) => new(culture);

    /// <summary>The Master Data and Orders rows of one tenant; created in the current tenant.</summary>
    protected sealed class Fixture
    {
        public Guid CountryId { get; init; }
        public Guid CustomerId { get; init; }
        public Guid CustomerAddressId { get; init; }
        public Guid DepotId { get; init; }
        public Guid DepotAddressId { get; init; }
        public Guid ShopId { get; init; }
        public Guid ShopAddressId { get; init; }
        public Guid CarrierId { get; init; }
        public Guid OwnCompanyId { get; init; }
        public Guid TransportUnitId { get; init; }
        public Guid ProductId { get; init; }
        public Guid LoadTypeId { get; init; }
        public Guid UnloadTypeId { get; init; }
        public Guid TimeSlotId { get; init; }
        public Guid OrderTypeId { get; init; }
        public Guid VatId { get; init; }
        public Guid PriceListId { get; init; }
    }

    protected async Task<Fixture> FixtureAsync(TransportOrderRequiredFields required = TransportOrderRequiredFields.None, bool withOwnCompany = true)
    {
        var country = await GetRequiredService<ICountryAppService>().CreateAsync(new CountryCreateDto
        {
            IsoAlpha2 = "NL", IsoAlpha3 = "NLD", Names = new MasterDataNames { ["en"] = "Netherlands", ["nl"] = "Nederland" }, PrimaryTimeZoneId = "Europe/Amsterdam"
        });
        var customer = await RelationAsync("KLANT", "Klant Foods B.V.", RelationRoles.Customer, country.Id, "Dierensteinweg", "30", "2991 XJ", "Barendrecht");
        var depot = await RelationAsync("DEPOT", "DC Venlo", RelationRoles.Customer | RelationRoles.Depot, country.Id, "Celsiusweg", "12", "5928 PR", "Venlo");
        var shop = await RelationAsync("SHOP", "Supermarkt Utrecht", RelationRoles.Customer, country.Id, "Vredenburg", "40", "3511 BD", "Utrecht");
        var carrier = await RelationAsync("CARRIER", "Charter Transport", RelationRoles.Carrier, country.Id, "Edisonstraat", "8", "5928 PG", "Venlo");
        Guid ownId = Guid.Empty;
        if (withOwnCompany)
        {
            ownId = (await RelationAsync("ME", "Voorbeeld Transport", RelationRoles.OwnCompany | RelationRoles.Carrier, country.Id, "Waalhaven", "45", "3089 JH", "Rotterdam")).Id;
        }

        var unit = await GetRequiredService<ITransportUnitAppService>().CreateAsync(new TransportUnitCreateDto { Name = "Europallet" });
        var product = await GetRequiredService<IProductAppService>().CreateAsync(new ProductCreateDto { Name = "Zuivel" });

        var waypointTypes = GetRequiredService<IWaypointTypeAppService>();
        var load = await waypointTypes.CreateAsync(new WaypointTypeCreateDto { Code = "LOAD", Names = OrdersNames("Load", "Laden"), Activity = WaypointActivity.Load, DefaultDurationMinutes = 30 });
        var unload = await waypointTypes.CreateAsync(new WaypointTypeCreateDto { Code = "UNLOAD", Names = OrdersNames("Unload", "Lossen"), Activity = WaypointActivity.Unload, DefaultDurationMinutes = 20 });
        var slot = await GetRequiredService<ITimeSlotAppService>().CreateAsync(new TimeSlotCreateDto { Code = "MORNING", Names = OrdersNames("Morning", "Ochtend"), StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(12, 0) });
        var type = await GetRequiredService<ITransportOrderTypeAppService>().CreateAsync(new TransportOrderTypeCreateDto
        {
            Code = "STD", Names = OrdersNames("Standard", "Standaard"), Numbering = new() { Prefix = "TO", IncludeYear = true, Separator = "-", Digits = 5 },
            RequiredFields = required,
            DefaultWaypoints = [new() { WaypointTypeId = load.Id, Position = 1 }, new() { WaypointTypeId = unload.Id, Position = 2 }]
        });

        return new Fixture
        {
            CountryId = country.Id,
            CustomerId = customer.Id, CustomerAddressId = customer.Addresses.Single().Id!.Value,
            DepotId = depot.Id, DepotAddressId = depot.Addresses.Single().Id!.Value,
            ShopId = shop.Id, ShopAddressId = shop.Addresses.Single().Id!.Value,
            CarrierId = carrier.Id, OwnCompanyId = ownId,
            TransportUnitId = unit.Id, ProductId = product.Id,
            LoadTypeId = load.Id, UnloadTypeId = unload.Id, TimeSlotId = slot.Id, OrderTypeId = type.Id,
            VatId = Financial.AddVat(), PriceListId = Financial.AddPriceList()
        };
    }

    protected Task<RelationDto> RelationAsync(string code, string name, RelationRoles roles, Guid countryId, string street, string number, string postalCode, string city)
        => GetRequiredService<IRelationAppService>().CreateAsync(new RelationCreateDto
        {
            Code = code, Name = name, Roles = roles,
            Addresses = [new RelationAddressDto { CountryId = countryId, Street = street, HouseNumber = number, PostalCode = postalCode, City = city, Latitude = 51.9, Longitude = 4.5 }]
        });

    protected static MasterDataNames Names(string en, string nl) => new() { ["en"] = en, ["nl"] = nl };

    protected static OpenTms.Platform.ReferenceData.LocalizedNameDto Names(string en) => new() { ["en"] = en };

    protected static OpenTms.Platform.ReferenceData.LocalizedNameDto OrdersNames(string en, string nl) => new() { ["en"] = en, ["nl"] = nl };

    protected static TransportOrderStopInputDto Stop(Guid waypointTypeId, Guid relationId, Guid addressId, DateOnly date, string? reference = null, Guid? timeSlotId = null, Guid? id = null)
        => new() { Id = id, WaypointTypeId = waypointTypeId, RelationId = relationId, RelationAddressId = addressId, Date = date, Reference = reference, TimeSlotId = timeSlotId, PlannedDurationMin = 30 };

    protected TransportOrderLineInputDto Line(Fixture f, decimal quantity = 2, double? weightKg = 100, bool withStops = true, Guid? id = null, DateOnly? date = null)
    {
        var day = date ?? Today;
        return new TransportOrderLineInputDto
        {
            Id = id, TransportUnitId = f.TransportUnitId, ProductId = f.ProductId, Quantity = quantity, WeightKg = weightKg, Remarks = "Gekoeld houden",
            Stops = withStops ? [Stop(f.LoadTypeId, f.DepotId, f.DepotAddressId, day, "LAAD-1", f.TimeSlotId), Stop(f.UnloadTypeId, f.ShopId, f.ShopAddressId, day.AddDays(1), "LOS-1")] : null
        };
    }

    protected TransportOrderCreateDto CreateDto(Fixture f, bool confirm = false, int lines = 1, string? customerReference = "PO-1", DateOnly? orderDate = null)
        => new()
        {
            TransportOrderTypeId = f.OrderTypeId,
            CustomerRelationId = f.CustomerId,
            OrderDate = orderDate ?? Today,
            CustomerReference = customerReference,
            FinancialTerms = new TransportOrderFinancialTermsInputDto { CurrencyCode = "EUR", VatPercentageId = f.VatId, PaymentTermDays = 30, PriceListIds = [f.PriceListId] },
            Lines = Enumerable.Range(0, lines).Select(_ => Line(f)).ToList(),
            References = [new TransportOrderReferenceDto { Type = "PO", Value = "123" }],
            Confirm = confirm
        };

    protected static TransportOrderUpdateDto HeaderUpdate(TransportOrderDto dto, Action<TransportOrderUpdateDto>? change = null)
    {
        var update = new TransportOrderUpdateDto
        {
            TransportOrderTypeId = dto.TransportOrderTypeId, CustomerRelationId = dto.CustomerRelationId, CustomerContactPersonId = dto.CustomerContactPersonId,
            OrderDate = dto.OrderDate, CustomerReference = dto.CustomerReference, OrganizationUnitId = dto.OrganizationUnitId,
            InternalRemarks = dto.InternalRemarks, InvoicingRemarks = dto.InvoicingRemarks, ConcurrencyStamp = dto.ConcurrencyStamp
        };
        change?.Invoke(update);
        return update;
    }

    protected static List<TransportOrderLineInputDto> LinesOf(TransportOrderDto dto)
        => dto.Lines.Select(l => new TransportOrderLineInputDto
        {
            Id = l.Id, TransportUnitId = l.TransportUnitId, ProductId = l.ProductId, Quantity = l.Quantity, WeightKg = l.WeightKg, Remarks = l.Remarks,
            Stops = l.Stops.Select(s => new TransportOrderStopInputDto
            {
                Id = s.Id, WaypointTypeId = s.WaypointTypeId, RelationId = s.RelationId, RelationAddressId = s.RelationAddressId, Date = s.Date,
                TimeSlotId = s.TimeSlotId, WindowFrom = s.WindowFrom, WindowTill = s.WindowTill, Reference = s.Reference, Remarks = s.Remarks, PlannedDurationMin = s.PlannedDurationMin
            }).ToList()
        }).ToList();
}
