using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.Financial.Tariffs;
using OpenTms.HostIntegration;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Relations;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using Shouldly;
using Volo.Abp.Security.Claims;
using Xunit;
using static OpenTms.AiConnector.AiConnectorHarness;

namespace OpenTms.AiConnector;

/// <summary>
/// Design 74 "Field allowlists" (risk class High), with real orders, trips, legs, relations, vehicles and rate lines on the real host: every left-out field of the design's table is seeded with a marker value,
/// the source service is shown to return the marker (so the test cannot pass by seeding nothing), and the serialized result of each tool over <c>/mcp</c> contains no marker and only the fields the design's
/// "Fields returned" table names. The tool DTOs' names are guarded by the architecture test beside it.
/// </summary>
[Collection(HostIntegrationCollection.Name)]
public class FieldAllowlistHostTests
{
    private static readonly string[] TextMarkers =
    [
        Marker.Prefix, "marker-relation@", "marker.example", Marker.RelationPhone, Marker.RelationKvk, Marker.RelationVat, Marker.Vin,
        Marker.TripRequestAmount, Marker.RateDescriptionKey, "Financial:RateLine",
        "765432.19", "654321.87", "543219.76", "432198.65", "51.987654", "4.876543", "52.123987", "5.987123", "876543.21", "111111.11",
    ];

    /* The order's stored revenue: every tool but get_customer_revenue and query_metrics is to stay silent about it. */
    private const string OrderRevenueText = "987654.32";

    private static readonly IReadOnlyDictionary<string, string[]> AllowedFields = new Dictionary<string, string[]>
    {
        [ToolNames.SearchOrders] = ["total", "rows", "id", "number", "status", "orderDate", "customerRelationId", "customerName", "customerReference", "transportOrderTypeName", "source", "firstStopName", "firstStopCity", "firstStopDate", "lastStopName", "lastStopCity", "lastStopDate", "lineCount", "stopCount", "tripNumbers", "executionProgress", "co2Kg", "hasTripRequest"],
        [ToolNames.GetOrder] = ["id", "number", "status", "orderDate", "customerRelationId", "customerName", "customerReference", "transportOrderTypeName", "source", "firstStopName", "firstStopCity", "firstStopDate", "lastStopName", "lastStopCity", "lastStopDate", "lineCount", "stopCount", "tripNumbers", "executionProgress", "co2Kg", "references", "type", "value", "defaultStops", "position", "relationName", "waypointTypeName", "windowFrom", "windowTill", "lines", "productName", "transportUnitName", "quantity", "colli", "weightKg", "volumeM3", "loadingMeters", "temperatureName", "vehicleTypeName", "requiredTransportCharacteristicNames", "shipperRelationName"],
        [ToolNames.SearchTrips] = ["total", "rows", "id", "number", "status", "execution", "tripDate", "transportTripTypeName", "carrierRelationId", "carrierName", "customers", "orderNumbers", "vehicles", "firstStopCity", "lastStopCity", "plannedDeparture", "plannedArrival", "actualDeparture", "actualArrival", "legCount", "stopCount", "loadFactor", "loadingMeters", "plannedDistanceM", "actualDistanceM", "plannedDurationS", "co2Kg", "warnings", "code"],
        [ToolNames.GetTrip] = ["id", "number", "status", "execution", "tripDate", "transportTripTypeName", "carrierRelationId", "carrierName", "vehicleCombinationName", "vehicles", "name", "licencePlate", "plannedDeparture", "plannedArrival", "actualDeparture", "actualArrival", "loadFactor", "loadingMeters", "plannedDistanceM", "actualDistanceM", "plannedDurationS", "co2Kg", "warnings", "code", "stops", "position", "relationName", "city", "estimatedArrival", "estimatedDeparture", "legCount", "stopCount", "orderCount", "totalQuantity", "totalWeightKg"],
        [ToolNames.GetLegs] = ["total", "rows", "id", "orderId", "orderNumber", "tripId", "tripNumber", "tripStatus", "position", "customerName", "origin", "destination", "name", "city", "countryCode", "plannedDate", "windowFrom", "windowTill", "quantity", "weightKg", "volumeM3", "loadingMeters", "distanceM", "durationS", "estimatedCo2Kg", "isViaLeg"],
        [ToolNames.SearchRelations] = ["total", "rows", "id", "code", "name", "city", "countryCode", "roles", "categoryNames", "isBlocked", "isArchived"],
        [ToolNames.SearchVehicles] = ["total", "rows", "id", "name", "kind", "licencePlate", "registrationCountryCode", "vehicleTypeName", "fuelType", "euroClass", "ownerName", "homeDepotName", "combinationName", "isArchived"],
        [ToolNames.GetCustomerRevenue] = ["customerRelationId", "dateFrom", "dateTo", "definition", "totals", "currencyCode", "revenueAmount", "orderCount", "ordersWithoutAmount", "excludedDraftCount", "excludedCancelledCount"],
        [ToolNames.GetOrderRates] = ["currencyCode", "state", "totals", "linesAmount", "discountsAndSurchargesAmount", "additionalServicesAmount", "total", "hasFixedPrice", "lines", "side", "kind", "additionalServiceName", "discountOrSurchargeName", "priceListCode", "quantity", "unitPrice", "amount", "isManual", "tripNumber"],
    };

    private readonly AiConnectorHarness _h;

    public FieldAllowlistHostTests(HostIntegrationFixture host)
    {
        _h = new AiConnectorHarness(host);
    }

    private static IEnumerable<string> FieldNames(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;
                    foreach (var nested in FieldNames(property.Value))
                    {
                        yield return nested;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in FieldNames(item))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    private async Task<(ConnectorWorld World, string Token)> ArrangeAsync()
    {
        var world = await _h.WorldOfAsync(_h.TenantA, "TNTAAA");
        await _h.ReadyAsync(_h.TenantA);
        var user = await _h.CreateUserAsync(_h.TenantA, AllToolPermissions);
        return (world, (await _h.ConnectAsync(user)).AccessToken!);
    }

    private static IEnumerable<(string Tool, object Args)> ToolCalls(ConnectorWorld w) =>
    [
        (ToolNames.SearchOrders, new Dictionary<string, object?> { ["customerRelationId"] = w.CustomerId }),
        (ToolNames.GetOrder, new Dictionary<string, object?> { ["id"] = w.OrderId }),
        (ToolNames.SearchTrips, new Dictionary<string, object?> { ["carrierRelationId"] = w.CarrierId }),
        (ToolNames.GetTrip, new Dictionary<string, object?> { ["id"] = w.TripId }),
        (ToolNames.GetLegs, new Dictionary<string, object?> { ["orderId"] = w.OrderId }),
        (ToolNames.GetLegs, new Dictionary<string, object?> { ["tripId"] = w.TripId }),
        (ToolNames.SearchRelations, new Dictionary<string, object?> { ["text"] = "Marked", ["take"] = 50 }),
        (ToolNames.SearchVehicles, new Dictionary<string, object?> { ["take"] = 50 }),
        (ToolNames.GetOrderRates, new Dictionary<string, object?> { ["orderId"] = w.OrderId }),
        (ToolNames.GetCustomerRevenue, new Dictionary<string, object?> { ["customerRelationId"] = w.CustomerId, ["dateFrom"] = "2026-01-01", ["dateTo"] = "2026-12-31" }),
    ];

    [Fact]
    public async Task The_source_services_do_return_every_marker_so_the_test_below_means_something()
    {
        var world = await _h.WorldOfAsync(_h.TenantA, "TNTAAA");
        var admin = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(AbpClaimTypes.UserId, world.CreatorUserId.ToString()),
            new Claim(AbpClaimTypes.TenantId, _h.TenantA.ToString()),
            new Claim(AbpClaimTypes.Role, "admin"),
        ], "Test"));

        var order = JsonSerializer.Serialize(await _h.InScopeAsync(_h.TenantA, services => services.GetRequiredService<ITransportOrderAppService>().GetAsync(world.OrderId), admin));
        foreach (var marker in new[]
                 {
                     Marker.InternalRemarks, Marker.InvoicingRemarks, Marker.OrderRemark, Marker.OrderAttribute, Marker.ReferenceDescription, Marker.TripRequestAmount,
                     Marker.LineRemarks, Marker.LineInternalRemarks, Marker.LineSpecialAgreements, Marker.LineShipperInstructions, Marker.LinePinCode, Marker.LineBarcode,
                     Marker.LineContainerNumber, Marker.LineInvoiceReference, Marker.LineAttachedDocuments, Marker.LineAttribute, "987654.32", "876543.21", "111111.11",
                     world.ContactPersonId.ToString(),
                 })
        {
            order.ShouldContain(marker, Case.Insensitive, "order: " + marker);
        }

        var trip = JsonSerializer.Serialize(await _h.InScopeAsync(_h.TenantA, services => services.GetRequiredService<ITransportTripAppService>().GetAsync(world.TripId), admin));
        foreach (var marker in new[] { Marker.TripName, Marker.TripRemarks, Marker.TripDriverInstructions, Marker.TripAttribute, Marker.TripWarningData, Marker.DriverLastName, Marker.StopRemarks, "765432.19", "654321.87", "51.987654", "4.876543" })
        {
            trip.ShouldContain(marker, Case.Insensitive, "trip: " + marker);
        }

        var legs = JsonSerializer.Serialize(await _h.InScopeAsync(_h.TenantA, services => services.GetRequiredService<ILegAppService>().GetListAsync(new LegListRequestDto { StartRow = 0, EndRow = 10, OrderId = world.OrderId }), admin));
        foreach (var marker in new[] { Marker.LegRemarks, Marker.LegOriginReference, Marker.LegDestinationReference, "543219.76", "432198.65", "52.123987", "5.987123" })
        {
            legs.ShouldContain(marker, Case.Insensitive, "legs: " + marker);
        }

        var relations = JsonSerializer.Serialize(await _h.InScopeAsync(_h.TenantA, services => services.GetRequiredService<IRelationAppService>().GetListAsync(new RelationListRequestDto { StartRow = 0, EndRow = 100, IncludeArchived = true }), admin));
        foreach (var marker in new[] { Marker.RelationEmail, Marker.RelationPhone, Marker.RelationFullName, Marker.RelationKvk, Marker.RelationVat })
        {
            relations.ShouldContain(marker, Case.Insensitive, "relations: " + marker);
        }

        var vehicles = JsonSerializer.Serialize(await _h.InScopeAsync(_h.TenantA, services => services.GetRequiredService<IVehicleAppService>().GetListAsync(new VehicleListRequestDto { StartRow = 0, EndRow = 100, IncludeArchived = true }), admin));
        vehicles.ShouldContain(Marker.Vin, Case.Insensitive);

        var rates = JsonSerializer.Serialize(await _h.InScopeAsync(_h.TenantA, services => services.GetRequiredService<IRateLineAppService>().GetAsync(new RateLineGetRequestDto { OrderId = world.OrderId }), admin));
        rates.ShouldContain(Marker.RateDescription);
        rates.ShouldContain(Marker.RateDescriptionKey);
    }

    [Fact]
    public async Task No_tool_result_contains_a_marker_of_a_left_out_field()
    {
        var (world, token) = await ArrangeAsync();
        foreach (var (tool, args) in ToolCalls(world))
        {
            var answer = await _h.CallToolAsync(token, tool, args);
            answer.IsError.ShouldBeFalse(tool + ": " + answer.Text);
            foreach (var marker in TextMarkers)
            {
                answer.Text.ShouldNotContain(marker, Case.Insensitive, tool + " leaks " + marker);
            }

            answer.Text.ShouldNotContain(world.ContactPersonId.ToString(), Case.Insensitive, tool + " leaks the contact person");
            answer.Text.ShouldNotContain(world.CreatorUserId.ToString(), Case.Insensitive, tool + " leaks the creator");
            if (tool != ToolNames.GetCustomerRevenue)
            {
                answer.Text.ShouldNotContain(OrderRevenueText, Case.Sensitive, tool + " leaks the order's revenue");
            }

            /* The structured form says the same. */
            var structured = answer.Result.GetProperty("structuredContent").GetRawText();
            foreach (var marker in TextMarkers)
            {
                structured.ShouldNotContain(marker, Case.Insensitive, tool + " (structured) leaks " + marker);
            }
        }
    }

    [Fact]
    public async Task Every_tool_result_holds_only_the_fields_the_design_names()
    {
        var (world, token) = await ArrangeAsync();
        var seen = new HashSet<string>();
        foreach (var (tool, args) in ToolCalls(world))
        {
            var answer = await _h.CallToolAsync(token, tool, args);
            answer.IsError.ShouldBeFalse(tool + ": " + answer.Text);
            var names = FieldNames(answer.Json).Distinct().ToList();
            names.ShouldNotBeEmpty(tool);
            var extra = names.Except(AllowedFields[tool]).ToList();
            extra.ShouldBeEmpty(tool + " returns fields the design does not name");
            seen.Add(tool);
        }

        seen.Count.ShouldBe(AllowedFields.Count, "every tool of design 74 was looked at");
    }

    [Fact]
    public async Task The_money_of_an_order_comes_only_from_the_two_money_tools()
    {
        var (world, token) = await ArrangeAsync();
        var rates = await _h.ToolJsonAsync(token, ToolNames.GetOrderRates, new Dictionary<string, object?> { ["orderId"] = world.OrderId });
        rates.GetProperty("lines").GetArrayLength().ShouldBeGreaterThanOrEqualTo(2);
        rates.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("amount").GetDecimal()).ShouldContain(world.UnitPrice * 2);

        var revenue = await _h.ToolJsonAsync(token, ToolNames.GetCustomerRevenue, new Dictionary<string, object?> { ["customerRelationId"] = world.CustomerId, ["dateFrom"] = "2026-01-01", ["dateTo"] = "2026-12-31" });
        revenue.GetProperty("totals").EnumerateArray().Single().GetProperty("revenueAmount").GetDecimal().ShouldBe(Marker.OrderRevenue);
    }
}
