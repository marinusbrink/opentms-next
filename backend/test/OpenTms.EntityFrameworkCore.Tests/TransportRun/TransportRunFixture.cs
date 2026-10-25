using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Tariffs;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.NumberSequences;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using MasterDataNames = OpenTms.MasterData.LocalizedNameDto;
using ReferenceNames = OpenTms.Platform.ReferenceData.LocalizedNameDto;

namespace OpenTms.TransportRun;

/// <summary>
/// The ids of one tenant's transport-run world: the Master Data rows an order refers to, the Orders, Planning and
/// Financial master data, built by <see cref="TransportRunFixture.BuildAsync"/> through the real application services.
/// Codes carry a per-build suffix so committed rows (PostgreSQL) never clash with a later build in the same tenant.
/// </summary>
public sealed class TransportRunWorld
{
    public string Suffix { get; init; } = string.Empty;
    public Guid CountryId { get; init; }
    public Guid CustomerId { get; init; }
    public Guid CustomerAddressId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public Guid DepotId { get; init; }
    public Guid DepotAddressId { get; init; }
    public Guid ShopId { get; init; }
    public Guid ShopAddressId { get; init; }
    public Guid CarrierId { get; init; }
    public Guid CarrierAddressId { get; init; }
    public Guid OwnCompanyId { get; init; }
    public Guid TransportUnitId { get; init; }
    public Guid ProductId { get; init; }
    public Guid LoadTypeId { get; init; }
    public Guid UnloadTypeId { get; init; }
    public Guid TimeSlotId { get; init; }
    public Guid OrderTypeId { get; init; }
    public Guid TripTypeId { get; init; }
    public Guid VatId { get; init; }

    /// <summary>The customer's revenue list: one lane depot → shop at <see cref="TransportRunFixture.RevenueUnitPrice"/> per unit.</summary>
    public Guid PriceListId { get; init; }

    /// <summary>The carrier's cost list: one lane depot → shop at <see cref="TransportRunFixture.CostUnitPrice"/> per unit.</summary>
    public Guid CostPriceListId { get; init; }
}

public sealed class TransportRunWorldOptions
{
    public TransportOrderRequiredFields RequiredFields { get; init; } = TransportOrderRequiredFields.None;

    public bool WithOwnCompany { get; init; } = true;

    /// <summary>The relation financial settings of the customer (revenue list, VAT, payment term) and the carrier (cost list).</summary>
    public bool WithRelationSettings { get; init; } = true;

    /// <summary>Suffixes the names whose uniqueness Master Data enforces (transport unit, product): for rows that commit.</summary>
    public bool UniqueNames { get; init; }
}

/// <summary>A geocoded Master Data relation with one address, as the stops and trips refer to it.</summary>
public sealed record Place(Guid RelationId, Guid AddressId, string Name, string City, double Latitude, double Longitude);

/// <summary>
/// The one world builder of the transport-run tests (designs 56–60) for SQLite and PostgreSQL alike: it creates the rows
/// a scenario needs through the production application services of Master Data, Orders, Planning &amp; Execution and
/// Financial in the current tenant and unit of work, and offers the few direct writes a test needs for stored values no
/// service exposes (routed distances, allocations, rate lines of a given version). Nothing here replaces a service.
/// </summary>
public class TransportRunFixture : ITransientDependency
{
    public const decimal RevenueUnitPrice = 10m;
    public const decimal CostUnitPrice = 8m;

    /* Places: the depot in Venlo, the shop in Utrecht, the customer in Barendrecht, the carrier in Venlo, the own company in Rotterdam. */
    public static readonly (double Lat, double Lon) Venlo = (51.3704, 6.1724);
    public static readonly (double Lat, double Lon) Utrecht = (52.0907, 5.1214);
    public static readonly (double Lat, double Lon) Barendrecht = (51.8564, 4.5342);
    public static readonly (double Lat, double Lon) Rotterdam = (51.9244, 4.4777);

    private readonly ICurrentTenant _currentTenant;
    private readonly IGuidGenerator _guids;
    private readonly IClock _clock;
    private readonly ICountryAppService _countries;
    private readonly ICurrencyAppService _currencies;
    private readonly IRepository<Country, Guid> _countryRepository;
    private readonly IRepository<Currency, Guid> _currencyRepository;
    private readonly IRelationAppService _relations;
    private readonly ITransportUnitAppService _units;
    private readonly IRepository<TransportUnit, Guid> _unitRepository;
    private readonly IProductAppService _products;
    private readonly IVehicleTypeAppService _vehicleTypes;
    private readonly IVehicleAppService _vehicles;
    private readonly IDriverAppService _drivers;
    private readonly IWaypointTypeAppService _waypointTypes;
    private readonly IRepository<WaypointType, Guid> _waypointTypeRepository;
    private readonly ITimeSlotAppService _timeSlots;
    private readonly IRepository<TimeSlot, Guid> _timeSlotRepository;
    private readonly ITransportOrderTypeAppService _orderTypes;
    private readonly IRepository<TransportOrderType, Guid> _orderTypeRepository;
    private readonly ITransportTripTypeAppService _tripTypes;
    private readonly IRepository<TransportTripType, Guid> _tripTypeRepository;
    private readonly IVatPercentageAppService _vat;
    private readonly IPriceListAppService _priceLists;
    private readonly IDiscountOrSurchargeAppService _discounts;
    private readonly IRelationFinancialSettingsAppService _relationSettings;
    private readonly ITransportOrderAppService _orders;
    private readonly IRepository<TransportOrder, Guid> _orderRepository;
    private readonly ITransportTripAppService _trips;
    private readonly IRepository<Leg, Guid> _legs;
    private readonly IRepository<TransportTrip, Guid> _tripRepository;
    private readonly IRepository<RateLine, Guid> _rateLines;
    private readonly OrdersClock _ordersClock;

    public TransportRunFixture(
        ICurrentTenant currentTenant,
        IGuidGenerator guids,
        IClock clock,
        ICountryAppService countries,
        ICurrencyAppService currencies,
        IRepository<Country, Guid> countryRepository,
        IRepository<Currency, Guid> currencyRepository,
        IRelationAppService relations,
        ITransportUnitAppService units,
        IRepository<TransportUnit, Guid> unitRepository,
        IProductAppService products,
        IVehicleTypeAppService vehicleTypes,
        IVehicleAppService vehicles,
        IDriverAppService drivers,
        IWaypointTypeAppService waypointTypes,
        IRepository<WaypointType, Guid> waypointTypeRepository,
        ITimeSlotAppService timeSlots,
        IRepository<TimeSlot, Guid> timeSlotRepository,
        ITransportOrderTypeAppService orderTypes,
        IRepository<TransportOrderType, Guid> orderTypeRepository,
        ITransportTripTypeAppService tripTypes,
        IRepository<TransportTripType, Guid> tripTypeRepository,
        IVatPercentageAppService vat,
        IPriceListAppService priceLists,
        IDiscountOrSurchargeAppService discounts,
        IRelationFinancialSettingsAppService relationSettings,
        ITransportOrderAppService orders,
        IRepository<TransportOrder, Guid> orderRepository,
        ITransportTripAppService trips,
        IRepository<Leg, Guid> legs,
        IRepository<TransportTrip, Guid> tripRepository,
        IRepository<RateLine, Guid> rateLines,
        OrdersClock ordersClock)
    {
        _currentTenant = currentTenant;
        _guids = guids;
        _clock = clock;
        _countries = countries;
        _currencies = currencies;
        _countryRepository = countryRepository;
        _currencyRepository = currencyRepository;
        _relations = relations;
        _units = units;
        _unitRepository = unitRepository;
        _products = products;
        _vehicleTypes = vehicleTypes;
        _vehicles = vehicles;
        _drivers = drivers;
        _waypointTypes = waypointTypes;
        _waypointTypeRepository = waypointTypeRepository;
        _timeSlots = timeSlots;
        _timeSlotRepository = timeSlotRepository;
        _orderTypes = orderTypes;
        _orderTypeRepository = orderTypeRepository;
        _tripTypes = tripTypes;
        _tripTypeRepository = tripTypeRepository;
        _vat = vat;
        _priceLists = priceLists;
        _discounts = discounts;
        _relationSettings = relationSettings;
        _orders = orders;
        _orderRepository = orderRepository;
        _trips = trips;
        _legs = legs;
        _tripRepository = tripRepository;
        _rateLines = rateLines;
        _ordersClock = ordersClock;
    }

    /// <summary>Today in the tenant's time zone, the date the Orders services use.</summary>
    public DateOnly Today => _ordersClock.Today();

    public static string NewSuffix() => Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    // ── the world ──────────────────────────────────────────────────────────────

    /// <summary>Builds the standard world of one tenant in the current tenant and unit of work.</summary>
    public async Task<TransportRunWorld> BuildAsync(TransportRunWorldOptions? options = null)
    {
        options ??= new TransportRunWorldOptions();
        var suffix = NewSuffix();

        var countryId = await CountryAsync();
        await CurrencyAsync("EUR");
        var customer = await PlaceAsync("KL" + suffix, "Klant Foods B.V.", RelationRoles.Customer, Barendrecht.Lat, Barendrecht.Lon, "Barendrecht", "Dierensteinweg", "30", "2991 XJ", countryId);
        var depot = await PlaceAsync("DP" + suffix, "DC Venlo", RelationRoles.Customer | RelationRoles.Depot, Venlo.Lat, Venlo.Lon, "Venlo", "Celsiusweg", "12", "5928 PR", countryId);
        var shop = await PlaceAsync("SH" + suffix, "Supermarkt Utrecht", RelationRoles.Customer, Utrecht.Lat, Utrecht.Lon, "Utrecht", "Vredenburg", "40", "3511 BD", countryId);
        var carrier = await PlaceAsync("CA" + suffix, "Charter Transport", RelationRoles.Carrier, Venlo.Lat, Venlo.Lon, "Venlo", "Edisonstraat", "8", "5928 PG", countryId);
        var ownCompanyId = Guid.Empty;
        if (options.WithOwnCompany)
        {
            ownCompanyId = (await PlaceAsync("ME" + suffix, "Voorbeeld Transport", RelationRoles.OwnCompany | RelationRoles.Carrier, Rotterdam.Lat, Rotterdam.Lon, "Rotterdam", "Waalhaven", "45", "3089 JH", countryId)).RelationId;
        }

        var unit = await _units.CreateAsync(new TransportUnitCreateDto { Name = options.UniqueNames ? "Europallet " + suffix : "Europallet" });
        var product = await _products.CreateAsync(new ProductCreateDto { Name = options.UniqueNames ? "Zuivel " + suffix : "Zuivel" });

        var load = await WaypointTypeAsync("LOAD", "Load", "Laden", WaypointActivity.Load, 30);
        var unload = await WaypointTypeAsync("UNLOAD", "Unload", "Lossen", WaypointActivity.Unload, 20);
        var slot = await TimeSlotAsync();
        var orderType = await OrderTypeAsync(options.RequiredFields, suffix, load, unload);
        var tripType = await TripTypeAsync();

        var vat = await _vat.CreateAsync(new VatPercentageCreateDto
        {
            Code = "VAT" + suffix, Names = Names("High", "Hoog"), CountryCode = "NL",
            Rates = [new VatPercentageRateInputDto { ValidFrom = new DateOnly(2000, 1, 1), Percentage = 21m }]
        });
        var revenueList = await PriceListAsync("PL" + suffix, RateSide.Revenue, depot.RelationId, shop.RelationId, RevenueUnitPrice, name: "Tarieven 2026");
        var costList = await PriceListAsync("CL" + suffix, RateSide.Cost, depot.RelationId, shop.RelationId, CostUnitPrice, name: "Inkooptarieven 2026");

        if (options.WithRelationSettings)
        {
            await _relationSettings.UpsertByRelationAsync(customer.RelationId, new RelationFinancialSettingsUpsertDto
            {
                RevenuePriceListIds = [revenueList], VatPercentageId = vat.Id, PaymentTermDays = 30, CurrencyCode = "EUR"
            });
            await _relationSettings.UpsertByRelationAsync(carrier.RelationId, new RelationFinancialSettingsUpsertDto { CostPriceListIds = [costList], CurrencyCode = "EUR" });
        }

        return new TransportRunWorld
        {
            Suffix = suffix,
            CountryId = countryId,
            CustomerId = customer.RelationId, CustomerAddressId = customer.AddressId, CustomerName = customer.Name,
            DepotId = depot.RelationId, DepotAddressId = depot.AddressId,
            ShopId = shop.RelationId, ShopAddressId = shop.AddressId,
            CarrierId = carrier.RelationId, CarrierAddressId = carrier.AddressId,
            OwnCompanyId = ownCompanyId,
            TransportUnitId = unit.Id, ProductId = product.Id,
            LoadTypeId = load, UnloadTypeId = unload, TimeSlotId = slot, OrderTypeId = orderType, TripTypeId = tripType,
            VatId = vat.Id, PriceListId = revenueList, CostPriceListId = costList
        };
    }

    // ── Master Data ────────────────────────────────────────────────────────────

    /// <summary>The Netherlands: the seeded row (PostgreSQL) or a new one (SQLite).</summary>
    public async Task<Guid> CountryAsync()
    {
        var existing = await _countryRepository.FindAsync(c => c.IsoAlpha2 == "NL");
        if (existing != null)
        {
            return existing.Id;
        }

        return (await _countries.CreateAsync(new CountryCreateDto
        {
            IsoAlpha2 = "NL", IsoAlpha3 = "NLD", Names = new MasterDataNames { ["en"] = "Netherlands", ["nl"] = "Nederland" }, PrimaryTimeZoneId = "Europe/Amsterdam"
        })).Id;
    }

    public async Task CurrencyAsync(string isoCode)
    {
        if (await _currencyRepository.FindAsync(c => c.IsoCode == isoCode) != null)
        {
            return;
        }

        await _currencies.CreateAsync(new CurrencyCreateDto { IsoCode = isoCode, Names = new MasterDataNames { ["en"] = isoCode }, IsActive = true, IsoNumeric = 978, MinorUnits = 2 });
    }

    /// <summary>A relation with one geocoded address in the Netherlands (Europe/Amsterdam).</summary>
    public async Task<Place> PlaceAsync(string code, string name, RelationRoles roles, double latitude, double longitude, string city = "Amsterdam",
        string street = "Main street", string houseNumber = "1", string postalCode = "1000 AA", Guid? countryId = null)
    {
        var relation = await _relations.CreateAsync(new RelationCreateDto
        {
            Code = code, Name = name, Roles = roles,
            Addresses =
            [
                new RelationAddressDto
                {
                    CountryId = countryId ?? await CountryAsync(), Street = street, HouseNumber = houseNumber, PostalCode = postalCode, City = city,
                    Latitude = latitude, Longitude = longitude, TimeZoneId = "Europe/Amsterdam"
                }
            ]
        });
        var address = (await _relations.GetAddressesAsync(relation.Id)).Single();
        return new Place(relation.Id, address.Id!.Value, name, city, latitude, longitude);
    }

    /// <summary>The transport unit with this name: the existing row or a new one.</summary>
    public async Task<Guid> TransportUnitAsync(string name)
    {
        var existing = await _unitRepository.FindAsync(u => u.Name == name);
        return existing?.Id ?? (await _units.CreateAsync(new TransportUnitCreateDto { Name = name })).Id;
    }

    public async Task<Guid> VehicleTypeAsync(string name, VehicleTypeKind kind, double? maxEpq = null, double? maxLoadingMeters = null, decimal? costPricePerKm = null, params Guid[] characteristics)
        => (await _vehicleTypes.CreateAsync(new VehicleTypeCreateDto
        {
            Name = name, Kind = kind, MaxEpq = maxEpq, MaxLoadingMeters = maxLoadingMeters, CostPricePerKm = costPricePerKm, TransportCharacteristicsIds = characteristics.ToList()
        })).Id;

    public async Task<Guid> VehicleAsync(string name, Guid typeId, Guid ownerRelationId, double? grossKg = 18_000, double? maxPayloadKg = null, EuroClass? euroClass = EuroClass.Euro6,
        string? licencePlate = null, decimal? costPricePerKm = null, params Guid[] characteristics)
        => (await _vehicles.CreateAsync(new VehicleCreateDto
        {
            Name = name, VehicleTypeId = typeId, OwnerRelationId = ownerRelationId, GrossVehicleWeightKg = grossKg, MaxPayloadKg = maxPayloadKg, EuroClass = euroClass,
            FuelType = FuelType.Diesel, LicencePlate = licencePlate, CostPricePerKm = costPricePerKm, TransportCharacteristicIds = characteristics.ToList()
        })).Id;

    public async Task<Guid> DriverAsync(string code, string firstName, string lastName)
        => (await _drivers.CreateAsync(new DriverCreateDto { Code = code, FirstName = firstName, LastName = lastName })).Id;

    // ── Orders master data ─────────────────────────────────────────────────────

    /// <summary>The waypoint type with this code: the seeded row (PostgreSQL) or a new one (SQLite).</summary>
    public async Task<Guid> WaypointTypeAsync(string code, string en, string nl, WaypointActivity activity, int? durationMinutes = null, Guid? successorId = null)
    {
        var existing = await _waypointTypeRepository.FindAsync(w => w.Code == code);
        if (existing != null)
        {
            return existing.Id;
        }

        return (await _waypointTypes.CreateAsync(new WaypointTypeCreateDto
        {
            Code = code, Names = Names(en, nl), Activity = activity, DefaultDurationMinutes = durationMinutes, SuccessiveWaypointTypeId = successorId
        })).Id;
    }

    public async Task<Guid> TimeSlotAsync(string code = "MORNING")
    {
        var existing = await _timeSlotRepository.FindAsync(t => t.Code == code);
        if (existing != null)
        {
            return existing.Id;
        }

        return (await _timeSlots.CreateAsync(new TimeSlotCreateDto { Code = code, Names = Names("Morning", "Ochtend"), StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(12, 0) })).Id;
    }

    /// <summary>The order type: the seeded STD when nothing is required, else a type of its own with the required fields.</summary>
    public async Task<Guid> OrderTypeAsync(TransportOrderRequiredFields required, string suffix, Guid loadTypeId, Guid unloadTypeId)
    {
        if (required == TransportOrderRequiredFields.None)
        {
            var standard = await _orderTypeRepository.FindAsync(t => t.Code == "STD");
            if (standard != null)
            {
                return standard.Id;
            }
        }

        var code = required == TransportOrderRequiredFields.None ? "STD" : "REQ" + suffix;
        return (await _orderTypes.CreateAsync(new TransportOrderTypeCreateDto
        {
            Code = code, Names = Names("Standard", "Standaard"), Numbering = new() { Prefix = "TO", IncludeYear = true, Separator = "-", Digits = 5 },
            RequiredFields = required,
            DefaultWaypoints = [new() { WaypointTypeId = loadTypeId, Position = 1 }, new() { WaypointTypeId = unloadTypeId, Position = 2 }]
        })).Id;
    }

    // ── Planning master data ───────────────────────────────────────────────────

    /// <summary>The trip type STD: the seeded default (PostgreSQL) or a new one (SQLite).</summary>
    public async Task<Guid> TripTypeAsync(string code = "STD", TripExecution execution = TripExecution.OwnFleet)
    {
        var existing = await _tripTypeRepository.FindAsync(t => t.Code == code);
        if (existing != null)
        {
            return existing.Id;
        }

        return (await _tripTypes.CreateAsync(new TransportTripTypeCreateDto
        {
            Code = code, Names = Names(code, code), Numbering = new() { Prefix = "TR", IncludeYear = true, Separator = "-", Digits = 5 }, DefaultExecution = execution
        })).Id;
    }

    // ── Financial master data ──────────────────────────────────────────────────

    /// <summary>A price list with one lane rule from → to at <paramref name="unitPrice"/> per unit of the basis.</summary>
    public async Task<Guid> PriceListAsync(string code, RateSide side, Guid fromRelationId, Guid toRelationId, decimal unitPrice, QuantitySource basis = QuantitySource.LineQuantity, string? name = null)
    {
        var list = await _priceLists.CreateAsync(new PriceListCreateDto { Code = code, Name = name ?? "List " + code, Side = side, CurrencyCode = "EUR", Basis = basis, ValidFrom = new DateOnly(2020, 1, 1) });
        await _priceLists.CreateRuleAsync(list.Id, new PriceListRuleInputDto
        {
            FromRelationId = fromRelationId, ToRelationId = toRelationId,
            Brackets = [new PriceListBracketInputDto { UpperBound = null, UnitPrices = [unitPrice] }]
        });
        return list.Id;
    }

    public async Task<Guid> DiscountOrSurchargeAsync(string name, decimal percentage = 5m)
        => (await _discounts.CreateAsync(new DiscountOrSurchargeCreateDto
        {
            Code = "DS" + NewSuffix(), Name = name, ValueKind = DiscountOrSurchargeValueKind.Percentage,
            Values = [new DiscountOrSurchargeValueInputDto { ValidFrom = new DateOnly(2020, 1, 1), Value = percentage }]
        })).Id;

    /// <summary>Stores a calculated revenue line of the given version on the order (a price list line anchored to the first line), as the calculator would.</summary>
    public async Task StoreRevenueAsync(TransportOrderDto order, decimal amount, long version = 1)
    {
        var existing = await _rateLines.GetListAsync(l => l.AnchorOrderId == order.Id && l.Side == RateSide.Revenue);
        if (existing.Count > 0)
        {
            await _rateLines.DeleteManyAsync(existing, autoSave: true);
        }

        var lineId = order.Lines.OrderBy(l => l.Position).First().Id!.Value;
        var line = RateLine.Calculated(_guids.Create(), _currentTenant.Id, RateSide.Revenue, RateLineAnchor.OrderLine(order.Id, lineId), RateLineKind.PriceList, "EUR",
            "Financial:RateLine.PriceList", 1m, QuantitySource.LineQuantity, amount, amount);
        line.SetCalculation(Now, version);
        line.Position = 1;
        await _rateLines.InsertAsync(line, autoSave: true);
    }

    /// <summary>Stores a cost line of the given version on the trip, as the trip calculator would.</summary>
    public async Task StoreTripCostAsync(Guid tripId, decimal amount, long version = 1)
    {
        var line = RateLine.Calculated(_guids.Create(), _currentTenant.Id, RateSide.Cost, RateLineAnchor.Trip(tripId), RateLineKind.OwnFleetDistance, "EUR",
            TransportRunDescriptionKey, 1m, QuantitySource.TripDistanceKm, amount, amount);
        line.SetCalculation(Now, version);
        line.Position = 1;
        await _rateLines.InsertAsync(line, autoSave: true);
    }

    private const string TransportRunDescriptionKey = "Financial:RateLine.OwnFleetDistance";

    // ── orders ─────────────────────────────────────────────────────────────────

    public static TransportOrderStopInputDto Stop(Guid waypointTypeId, Guid relationId, Guid addressId, DateOnly date, string? reference = null, Guid? timeSlotId = null,
        TimeOnly? windowFrom = null, TimeOnly? windowTill = null, int? plannedDurationMin = 30, Guid? id = null)
        => new() { Id = id, WaypointTypeId = waypointTypeId, RelationId = relationId, RelationAddressId = addressId, Date = date, Reference = reference, TimeSlotId = timeSlotId, WindowFrom = windowFrom, WindowTill = windowTill, PlannedDurationMin = plannedDurationMin };

    /// <summary>A line of the standard world: depot (load, morning slot) today → shop (unload) tomorrow.</summary>
    public TransportOrderLineInputDto Line(TransportRunWorld w, decimal quantity = 2, double? weightKg = 100, bool withStops = true, Guid? id = null, DateOnly? date = null)
    {
        var day = date ?? Today;
        return new TransportOrderLineInputDto
        {
            Id = id, TransportUnitId = w.TransportUnitId, ProductId = w.ProductId, Quantity = quantity, WeightKg = weightKg, Remarks = "Gekoeld houden",
            Stops = withStops ? [Stop(w.LoadTypeId, w.DepotId, w.DepotAddressId, day, "LAAD-1", w.TimeSlotId), Stop(w.UnloadTypeId, w.ShopId, w.ShopAddressId, day.AddDays(1), "LOS-1")] : null
        };
    }

    /// <summary>The create DTO of the standard world; the terms carry the customer's lists and VAT.</summary>
    public TransportOrderCreateDto CreateDto(TransportRunWorld w, bool confirm = false, int lines = 1, string? customerReference = "PO-1", DateOnly? orderDate = null, bool withTerms = true)
        => new()
        {
            TransportOrderTypeId = w.OrderTypeId,
            CustomerRelationId = w.CustomerId,
            OrderDate = orderDate ?? Today,
            CustomerReference = customerReference,
            FinancialTerms = withTerms ? new TransportOrderFinancialTermsInputDto { CurrencyCode = "EUR", VatPercentageId = w.VatId, PaymentTermDays = 30, PriceListIds = [w.PriceListId] } : null,
            Lines = Enumerable.Range(0, lines).Select(_ => Line(w)).ToList(),
            References = [new TransportOrderReferenceEntryDto { Type = "PO", Value = "123" }],
            Confirm = confirm
        };

    public Task<TransportOrderDto> CreateOrderAsync(TransportRunWorld w, bool confirm = true, int lines = 1, string? customerReference = "PO-1", DateOnly? orderDate = null)
        => _orders.CreateAsync(CreateDto(w, confirm, lines, customerReference, orderDate));

    /// <summary>Cancels the order as a fact in Orders' table without the command's events (the window before Planning reacts).</summary>
    public async Task CancelWithoutEventsAsync(Guid orderId)
    {
        var order = await _orderRepository.GetAsync(orderId, includeDetails: false);
        order.Cancel();
        await _orderRepository.UpdateAsync(order, autoSave: true);
    }

    /// <summary>Removes the order row (soft delete) without the command's events: the published readers then know it no longer.</summary>
    public async Task RemoveWithoutEventsAsync(Guid orderId)
    {
        var order = await _orderRepository.GetAsync(orderId, includeDetails: false);
        await _orderRepository.DeleteAsync(order, autoSave: true);
    }

    /// <summary>Marks the order's trip request as handled with the given trip id, as Orders does on TransportTripCreated.</summary>
    public async Task RecordTripAsync(Guid orderId, Guid tripId)
    {
        var order = await _orderRepository.GetAsync(orderId, includeDetails: false);
        order.RecordTrip(tripId);
        await _orderRepository.UpdateAsync(order, autoSave: true);
    }

    // ── planning ───────────────────────────────────────────────────────────────

    public Task<List<Leg>> LegsOfAsync(Guid orderId) => _legs.GetListAsync(l => l.OrderId == orderId);

    public async Task<List<Guid>> LegIdsOfAsync(Guid orderId) => (await LegsOfAsync(orderId)).OrderBy(l => l.OrderLineId).ThenBy(l => l.Position).Select(l => l.Id).ToList();

    /// <summary>A trip of the given type departing at <paramref name="departure"/> (default: today 06:00 CET), with every unplanned leg of <paramref name="orderIds"/> assigned.</summary>
    public async Task<TransportTripDto> PlanAsync(Guid tripTypeId, IReadOnlyCollection<Guid> orderIds, TransportTripStatus status = TransportTripStatus.Planned,
        DateTimeOffset? departure = null, DateTimeOffset? arrival = null, Guid? carrierRelationId = null, string? name = null, bool allowOutsideWindow = true)
    {
        var start = departure ?? new DateTimeOffset(Today.ToDateTime(new TimeOnly(6, 0)), TimeSpan.FromHours(2));
        var trip = await _trips.CreateAsync(new TransportTripCreateDto
        {
            TransportTripTypeId = tripTypeId, PlannedDeparture = start, PlannedArrival = arrival ?? start.AddHours(12), CarrierRelationId = carrierRelationId, Name = name,
            Execution = carrierRelationId == null ? null : TripExecution.Subcontracted
        });
        var legIds = new List<Guid>();
        foreach (var orderId in orderIds)
        {
            legIds.AddRange((await LegsOfAsync(orderId)).Where(l => l.TripId == null).OrderBy(l => l.OrderLineId).ThenBy(l => l.Position).Select(l => l.Id));
        }

        if (legIds.Count > 0)
        {
            trip = await _trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = legIds, AllowOutsideWindow = allowOutsideWindow });
        }

        if (status is TransportTripStatus.InProgress or TransportTripStatus.Completed)
        {
            trip = await _trips.StartAsync(trip.Id, new TripTimestampDto { At = start.AddMinutes(10) });
        }

        if (status == TransportTripStatus.Completed)
        {
            trip = await _trips.CompleteAsync(trip.Id, new TripTimestampDto { At = start.AddHours(10) });
        }

        return trip;
    }

    /// <summary>Writes the stored values of a leg the calculators would (route, CO₂, allocation), straight into Planning's table.</summary>
    public async Task SetLegValuesAsync(Guid legId, int? distanceM = null, decimal? co2Kg = null, decimal? allocatedCost = null, decimal? allocatedRevenue = null, long version = 1)
    {
        var leg = await _legs.GetAsync(legId);
        if (distanceM != null)
        {
            leg.SetRoute(distanceM.Value, distanceM.Value / 20, null, "Test", Now);
        }

        if (co2Kg != null)
        {
            leg.SetCo2(co2Kg, "Test", "Test");
        }

        if (allocatedCost != null || allocatedRevenue != null)
        {
            leg.SetAllocation(allocatedCost, allocatedRevenue, version, Now);
        }

        await _legs.UpdateAsync(leg, autoSave: true);
    }

    public Task<TransportTrip> TripAsync(Guid tripId) => _tripRepository.GetAsync(tripId);

    // ── helpers ────────────────────────────────────────────────────────────────

    private DateTimeOffset Now => new(_clock.Now.ToUniversalTime(), TimeSpan.Zero);

    public static ReferenceNames Names(string en, string? nl = null)
    {
        var names = new ReferenceNames { ["en"] = en };
        if (nl != null)
        {
            names["nl"] = nl;
        }

        return names;
    }

    /// <summary>The planned departure of a day at an hour in CET/CEST (+02:00 in the test calendar).</summary>
    public static DateTimeOffset At(DateOnly day, int hour, int minute = 0) => new(day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(2));
}
