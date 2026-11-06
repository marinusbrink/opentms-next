using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.Financial.Tariffs;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Relations;
using OpenTms.Orders.EntityFrameworkCore;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.TransportRun;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using Volo.Abp.Timing;

namespace OpenTms.AiConnector;

/// <summary>Every field design 74 leaves out, seeded with a value that can be found again. The allowlist test proves none of them leaves.</summary>
public static class Marker
{
    public const string Prefix = "MARKER";

    public const string InternalRemarks = Prefix + "-order-internal-remarks";
    public const string InvoicingRemarks = Prefix + "-order-invoicing-remarks";
    public const string OrderRemark = Prefix + "-order-remark";
    public const string OrderAttribute = Prefix + "-order-attribute";
    public const string ReferenceDescription = Prefix + "-reference-description";
    public const string TripRequestAmount = "918273.64";

    public const string LineRemarks = Prefix + "-line-remarks";
    public const string LineInternalRemarks = Prefix + "-line-internal";
    public const string LineSpecialAgreements = Prefix + "-line-special";
    public const string LineShipperInstructions = Prefix + "-line-shipper";
    public const string LinePinCode = Prefix + "-pin";
    public const string LineBarcode = Prefix + "-barcode";
    public const string LineContainerNumber = Prefix + "-container";
    public const string LineInvoiceReference = Prefix + "-invoice-ref";
    public const string LineAttachedDocuments = Prefix + "-attached";
    public const string LineAttribute = Prefix + "-line-attribute";

    public const decimal OrderRevenue = 987654.32m;
    public const decimal OrderCost = 876543.21m;
    public const decimal OrderMargin = 111111.11m;

    public const string TripName = Prefix + "-trip-name";
    public const string TripRemarks = Prefix + "-trip-remarks";
    public const string TripDriverInstructions = Prefix + "-driver-instructions";
    public const string TripAttribute = Prefix + "-trip-attribute";
    public const string TripWarningData = Prefix + "-warning-data";
    public const string DriverLastName = Prefix + "DriverName";
    public const decimal TripCost = 765432.19m;
    public const decimal TripRevenue = 654321.87m;

    public const string StopRemarks = Prefix + "-stop-remarks";
    public const double StopLatitude = 51.987654;
    public const double StopLongitude = 4.876543;

    public const string LegRemarks = Prefix + "-leg-remarks";
    public const string LegOriginReference = Prefix + "-leg-origin-ref";
    public const string LegDestinationReference = Prefix + "-leg-destination-ref";
    public const double LegLatitude = 52.123987;
    public const double LegLongitude = 5.987123;
    public const decimal LegCost = 543219.76m;
    public const decimal LegRevenue = 432198.65m;

    public const string RelationEmail = "marker-relation@marker.example";
    public const string RelationPhone = "0612398745";
    public const string RelationFullName = Prefix + "-relation-full-name";
    public const string RelationKvk = "98127345";
    public const string RelationVat = "NL998127345B01";
    public const string ContactLastName = Prefix + "Contact";

    public const string Vin = "WMARKER1N00000075";

    public const string RateDescription = Prefix + "-rate-description";
    public const string RateDescriptionKey = "Financial:RateLine.PriceList";
}

/// <summary>The ids of one tenant's data: real rows through the real services and, for what only a calculator writes, straight in the tables.</summary>
public sealed class ConnectorWorld
{
    public required string Tag { get; init; }
    public required Guid TenantId { get; init; }
    public required TransportRunWorld Base { get; init; }
    public required Guid CustomerId { get; init; }
    public required string CustomerName { get; init; }
    public required Guid CarrierId { get; init; }
    public required string CarrierName { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid OrderLineId { get; init; }
    public required Guid TripId { get; init; }
    public required Guid LegId { get; init; }
    public required Guid VehicleId { get; init; }
    public required string VehicleName { get; init; }
    public required Guid VehicleTypeId { get; init; }
    public required Guid MarkerRelationId { get; init; }
    public required Guid CreatorUserId { get; init; }
    public required Guid ContactPersonId { get; init; }
    public required decimal UnitPrice { get; init; }
    public required string RateDescription { get; init; }
    public required DateOnly Day { get; init; }

    public string LegOriginName => "Origin " + Tag;

    public IEnumerable<string> Ids => new[] { CustomerId, CarrierId, OrderId, TripId, LegId, VehicleId, MarkerRelationId }.Select(id => id.ToString());
}

public static class ConnectorWorldBuilder
{
    private static readonly Dictionary<Guid, ConnectorWorld> Worlds = [];

    /// <summary>Each tenant's world, built once for the run: reads only ever look at it, so the isolation and allowlist tests share it.</summary>
    public static async Task<ConnectorWorld> WorldOfAsync(this AiConnectorHarness harness, Guid tenant, string tag)
    {
        if (Worlds.TryGetValue(tenant, out var existing))
        {
            return existing;
        }

        await harness.EnsureFeaturesOnAsync(tenant);
        var admin = await harness.CreateUserAsync(tenant);
        await harness.InScopeAsync(tenant, async services =>
        {
            var users = services.GetRequiredService<IdentityUserManager>();
            var user = await users.GetByIdAsync(admin.Id);
            (await users.AddToRoleAsync(user, "admin")).Succeeded.ShouldBeTrue();
        });

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(AbpClaimTypes.UserId, admin.Id.ToString()),
            new Claim(AbpClaimTypes.TenantId, tenant.ToString()),
            new Claim(AbpClaimTypes.UserName, admin.Email),
            new Claim(AbpClaimTypes.Role, "admin"),
        ], "Test"));

        ConnectorWorld world;
        try
        {
            world = await harness.InScopeAsync(tenant, services => BuildAsync(services, tenant, tag, admin.Id), principal);
        }
        catch (Volo.Abp.BusinessException exception)
        {
            throw new InvalidOperationException("The world of " + tag + " could not be built: " + exception.Code + " " + string.Join(", ", exception.Data.Keys.Cast<object>().Select(k => k + "=" + exception.Data[k])), exception);
        }

        /* Only a calculator or a screen outside these services writes them: straight in the tables, as those leave them. */
        await harness.ExecuteAsync(tenant, "UPDATE orders.\"TransportOrders\" SET \"Attributes\" = @a::jsonb WHERE \"Id\" = @id", ("a", "{\"note\":\"" + Marker.OrderAttribute + "\"}"), ("id", world.OrderId));
        await harness.ExecuteAsync(tenant, "UPDATE orders.\"TransportOrderLines\" SET \"Attributes\" = @a::jsonb WHERE \"Id\" = @id", ("a", "{\"note\":\"" + Marker.LineAttribute + "\"}"), ("id", world.OrderLineId));
        await harness.ExecuteAsync(tenant, "UPDATE planning.\"TransportTrips\" SET \"Attributes\" = @a::jsonb WHERE \"Id\" = @id", ("a", "{\"note\":\"" + Marker.TripAttribute + "\"}"), ("id", world.TripId));
        Worlds[tenant] = world;
        return world;
    }

    private static async Task<ConnectorWorld> BuildAsync(IServiceProvider services, Guid tenant, string tag, Guid creatorId)
    {
        var run = ActivatorUtilities.CreateInstance<TransportRunFixture>(services);
        var relations = services.GetRequiredService<IRelationAppService>();
        var unitPrice = 100m + Math.Abs(tag.GetHashCode() % 800);
        var day = new DateOnly(2026, 5, 4);

        var w = await run.BuildAsync(new TransportRunWorldOptions { UniqueNames = true });
        var country = await run.CountryAsync();
        var customer = await run.PlaceAsync("CU" + tag, "Customer " + tag, RelationRoles.Customer, 52.1, 5.1, "City " + tag);
        var carrier = await run.PlaceAsync("CR" + tag, "Carrier " + tag, RelationRoles.Carrier, 52.2, 5.2, "Carrier City " + tag);

        /* A relation with everything that must stay out of search_relations. */
        var marked = await relations.CreateAsync(new RelationCreateDto
        {
            Code = "MK" + tag,
            Name = "Marked " + tag,
            FullName = Marker.RelationFullName,
            Roles = RelationRoles.Customer,
            IsBlocked = true,
            BlockReason = RelationBlockReason.Dispute,
            Email = Marker.RelationEmail,
            Phone = Marker.RelationPhone,
            ChamberOfCommerceNumber = Marker.RelationKvk,
            VatNumber = Marker.RelationVat,
            Addresses =
            [
                new RelationAddressDto { CountryId = country, Street = "Dorpsstraat", HouseNumber = "1", PostalCode = "1000 AA", City = "Dorpskern", Latitude = 52.3, Longitude = 5.3, TimeZoneId = "Europe/Amsterdam" }
            ],
            ContactPersons = [new RelationContactPersonDto { FirstName = "Mark", LastName = Marker.ContactLastName, Email = "contact@marker.example", Role = default }],
        });
        var contactId = await services.GetRequiredService<IRepository<Relation, Guid>>().WithDetailsAsync(r => r.ContactPersons)
            .ContinueWith(t => t.Result.Where(r => r.Id == marked.Id).SelectMany(r => r.ContactPersons).Select(c => c.Id).First());

        /* Vehicles and a driver of its name. */
        var vehicleType = await run.VehicleTypeAsync("Type " + tag, VehicleTypeKind.Tractor);
        var vehicleName = "Vehicle " + tag;
        var vehicle = await services.GetRequiredService<IVehicleAppService>().CreateAsync(new VehicleCreateDto
        {
            Name = vehicleName, VehicleTypeId = vehicleType, OwnerRelationId = carrier.RelationId, Vin = Marker.Vin, LicencePlate = "AB-" + tag[..2] + "-12", RegistrationCountryId = country, FuelType = FuelType.Diesel, EuroClass = EuroClass.Euro6,
        });
        var driver = await run.DriverAsync("D" + tag, "Dri", Marker.DriverLastName);

        /* An order through the service, with every left-out field the service takes. */
        var line = run.Line(w);
        line.Remarks = Marker.LineRemarks;
        line.InternalRemarks = Marker.LineInternalRemarks;
        line.SpecialAgreements = Marker.LineSpecialAgreements;
        line.ShipperInstructions = Marker.LineShipperInstructions;
        line.PinCode = Marker.LinePinCode;
        line.Barcode = Marker.LineBarcode;
        line.ContainerNumber = Marker.LineContainerNumber;
        line.InvoiceReference = Marker.LineInvoiceReference;
        line.AttachedDocuments = Marker.LineAttachedDocuments;
        line.PaymentInstruction = PaymentInstruction.NotFree;

        var orders = services.GetRequiredService<ITransportOrderAppService>();
        var created = await orders.CreateAsync(new TransportOrderCreateDto
        {
            TransportOrderTypeId = w.OrderTypeId,
            CustomerRelationId = marked.Id,
            CustomerContactPersonId = contactId,
            OrderDate = day,
            CustomerReference = "PO " + tag,
            InternalRemarks = Marker.InternalRemarks,
            InvoicingRemarks = Marker.InvoicingRemarks,
            FinancialTerms = new TransportOrderFinancialTermsInputDto { CurrencyCode = "EUR", VatPercentageId = w.VatId, PaymentTermDays = 37, PriceListIds = [w.PriceListId] },
            DefaultStops = [new TransportOrderDefaultStopInputDto { WaypointTypeId = w.LoadTypeId, RelationId = w.DepotId, RelationAddressId = w.DepotAddressId, DayOffset = 0 }],
            Lines = [line],
            References = [new TransportOrderReferenceEntryDto { Type = "PO", Value = "VALUE-" + tag, Description = Marker.ReferenceDescription }],
            TripRequest = new TransportOrderTripRequestInputDto
            {
                TransportTripTypeId = w.TripTypeId,
                DepartureRelationId = w.DepotId,
                DepartureAddressId = w.DepotAddressId,
                ArrivalRelationId = w.ShopId,
                ArrivalAddressId = w.ShopAddressId,
                PlannedDeparture = TransportRunFixture.At(day, 6),
                PlannedArrival = TransportRunFixture.At(day, 18),
                AgreedPurchaseAmount = decimal.Parse(Marker.TripRequestAmount, System.Globalization.CultureInfo.InvariantCulture),
                AgreedPurchaseCurrencyCode = "EUR",
            },
            Confirm = true,
        });
        var lineId = created.Lines.Single().Id!.Value;

        var orderRepository = services.GetRequiredService<IRepository<TransportOrder, Guid>>();
        var order = await orderRepository.GetAsync(created.Id, includeDetails: true);
        order.Remarks.Add(new TransportOrderRemark(Guid.NewGuid(), tenant, order.Id, OrderRemarkKind.Revision, Marker.OrderRemark, DateTime.Now, creatorId));
        order.WriteSummary(new OrderSummary(Marker.OrderRevenue, Marker.OrderCost, Marker.OrderMargin, 12.5m, ExecutionProgress.Planned, 1, 2, "First " + tag, "First City " + tag, day, "Last " + tag, "Last City " + tag, day, "T-" + tag), DateTime.Now);
        await orderRepository.UpdateAsync(order, autoSave: true);

        /* A trip through the service, then what only planners and calculators write. */
        var trip = await services.GetRequiredService<ITransportTripAppService>().CreateAsync(new TransportTripCreateDto
        {
            TransportTripTypeId = w.TripTypeId,
            TripDate = day,
            Name = Marker.TripName,
            CarrierRelationId = carrier.RelationId,
            CarrierContactPersonId = null,
            Execution = TripExecution.Subcontracted,
            PlannedDeparture = TransportRunFixture.At(day, 6),
            PlannedArrival = TransportRunFixture.At(day, 18),
        });
        var tripRepository = services.GetRequiredService<IRepository<TransportTrip, Guid>>();
        var tripRow = await tripRepository.GetAsync(trip.Id, includeDetails: true);
        tripRow.SetRemarks(Marker.TripRemarks);
        tripRow.SetDriverInstructions(Marker.TripDriverInstructions);
        tripRow.SetVehicles(null, [vehicle.Id]);
        tripRow.SetDrivers([driver]);
        tripRow.SetMoney(Marker.TripCost, Marker.TripRevenue);
        tripRow.SetWarnings([new TripWarning(TripWarningCode.LegOutsideTripWindow, null, null, Marker.TripWarningData)]);
        var stop = new TripStop(Guid.NewGuid(), tenant, trip.Id, 1, w.LoadTypeId, w.DepotId, w.DepotAddressId, "Stop " + tag, "Stop City " + tag, Marker.StopLatitude, Marker.StopLongitude, "Europe/Amsterdam", false);
        stop.SetRemarks(Marker.StopRemarks);
        tripRow.Stops.Add(stop);
        var stop2 = new TripStop(Guid.NewGuid(), tenant, trip.Id, 2, w.UnloadTypeId, w.ShopId, w.ShopAddressId, "Stop2 " + tag, "Stop City2 " + tag, Marker.StopLatitude, Marker.StopLongitude, "Europe/Amsterdam", false);
        tripRow.Stops.Add(stop2);
        await tripRepository.UpdateAsync(tripRow, autoSave: true);

        /* A leg on the order and the trip. */
        LegEndSnapshot End(string name, string reference, Guid relation, Guid address) => new(
            Guid.NewGuid(), w.LoadTypeId, relation, address, name, "Leg City " + tag, "NL", Marker.LegLatitude, Marker.LegLongitude, "Europe/Amsterdam", day, null, null, reference, 30);
        var leg = new Leg(Guid.NewGuid(), tenant, order.Id, lineId, marked.Id, 1, LegSource.Generated,
            End("Origin " + tag, Marker.LegOriginReference, w.DepotId, w.DepotAddressId),
            End("Destination " + tag, Marker.LegDestinationReference, w.ShopId, w.ShopAddressId),
            new LegQuantities(2, 100, null, null, null, null, w.TransportUnitId, w.ProductId, null, []));
        leg.AssignTo(trip.Id, 1);
        leg.SetEditable(Marker.LegOriginReference, Marker.LegDestinationReference, Marker.LegRemarks, null, null);
        leg.SetAllocation(Marker.LegCost, Marker.LegRevenue, 1, DateTimeOffset.UtcNow);
        leg.SetRoute(12_345, 678, null, "Test", DateTimeOffset.UtcNow);
        await services.GetRequiredService<IRepository<Leg, Guid>>().InsertAsync(leg, autoSave: true);

        /* Rate lines: one calculated with its key, one manual with a text. */
        var rateRepository = services.GetRequiredService<IRepository<RateLine, Guid>>();
        var calculated = RateLine.Calculated(Guid.NewGuid(), tenant, RateSide.Revenue, RateLineAnchor.OrderLine(order.Id, lineId), RateLineKind.PriceList, "EUR",
            Marker.RateDescriptionKey, 2m, QuantitySource.LineQuantity, unitPrice, unitPrice * 2);
        calculated.SetCalculation(DateTimeOffset.UtcNow, 1);
        calculated.Position = 1;
        await rateRepository.InsertAsync(calculated, autoSave: true);
        var manual = RateLine.Manual(Guid.NewGuid(), tenant, RateSide.Revenue, RateLineAnchor.Order(order.Id), RateLineKind.Manual, "EUR", Marker.RateDescription, 1m, QuantitySource.LineQuantity, 1.5m, null);
        manual.Position = 2;
        await rateRepository.InsertAsync(manual, autoSave: true);

        return new ConnectorWorld
        {
            Tag = tag,
            TenantId = tenant,
            Base = w,
            CustomerId = marked.Id,
            CustomerName = "Marked " + tag,
            CarrierId = carrier.RelationId,
            CarrierName = carrier.Name,
            OrderId = order.Id,
            OrderLineId = lineId,
            TripId = trip.Id,
            LegId = leg.Id,
            VehicleId = vehicle.Id,
            VehicleName = vehicleName,
            VehicleTypeId = vehicleType,
            MarkerRelationId = marked.Id,
            CreatorUserId = creatorId,
            ContactPersonId = contactId,
            UnitPrice = unitPrice,
            RateDescription = Marker.RateDescription,
            Day = day,
        };
    }

    /// <summary>An order straight in Orders' table, with the status and stored revenue a test needs: no outbox event, no number.</summary>
    public static async Task<Guid> SeedOrderAsync(
        IServiceProvider services,
        Guid tenant,
        Guid customerId,
        DateOnly date,
        TransportOrderStatus status,
        string currency,
        decimal? revenue,
        decimal? cost = null,
        decimal? margin = null,
        decimal? co2 = null)
    {
        var typeId = await services.GetRequiredService<IRepository<TransportOrderType, Guid>>().GetQueryableAsync().ContinueWith(t => t.Result.Select(x => x.Id).First());
        var order = new TransportOrder(
            Guid.NewGuid(),
            tenant,
            new OrderHeaderInput(typeId, customerId, null, "Customer", date, null, null, null, null),
            new FinancialTermsInput(currency, null, null, [], MultipleMatches.All, [], null, false),
            OrderSource.Screen,
            Guid.NewGuid);
        order.WriteSummary(new OrderSummary(revenue, cost, margin, co2, ExecutionProgress.Unplanned, 0, 0, null, null, null, null, null, null, null), DateTime.Now);
        await services.GetRequiredService<IRepository<TransportOrder, Guid>>().InsertAsync(order, autoSave: true);
        var context = await services.GetRequiredService<IDbContextProvider<OrdersDbContext>>().GetDbContextAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE orders.\"TransportOrders\" SET \"Status\" = {(int)status} WHERE \"Id\" = {order.Id}");
        return order.Id;
    }
}
