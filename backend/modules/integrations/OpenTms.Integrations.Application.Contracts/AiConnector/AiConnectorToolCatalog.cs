using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Threading.Tasks;
using OpenTms.Financial.Permissions;
using OpenTms.MasterData.Permissions;
using OpenTms.Orders.Permissions;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Reporting.Features;

namespace OpenTms.Integrations.AiConnector;

/// <summary>
/// One tool of the connector: its name and text for the model, its typed input, what it needs and how it is called.
/// <see cref="Permissions"/> all have to be granted. <see cref="FeatureRequirements"/> mirror the
/// <c>[RequiresFeature]</c> attributes of the service the tool calls: every entry has to hold, and an entry holds when one
/// of its features is on. The services enforce these themselves; here they decide what <c>tools/list</c> shows.
/// </summary>
public sealed class AiConnectorToolDefinition
{
    private readonly Lazy<JsonElement> _inputSchema;

    public AiConnectorToolDefinition(
        string name,
        string title,
        string description,
        Type inputType,
        IReadOnlyList<string> permissions,
        IReadOnlyList<IReadOnlyList<string>> featureRequirements,
        Func<IAiConnectorToolService, object, Task<object>> invoke)
    {
        Name = name;
        Title = title;
        Description = description;
        InputType = inputType;
        Permissions = permissions;
        FeatureRequirements = featureRequirements;
        Invoke = invoke;
        _inputSchema = new Lazy<JsonElement>(() => AiConnectorToolSchema.For(inputType));
    }

    public string Name { get; }

    public string Title { get; }

    public string Description { get; }

    public Type InputType { get; }

    public IReadOnlyList<string> Permissions { get; }

    public IReadOnlyList<IReadOnlyList<string>> FeatureRequirements { get; }

    public Func<IAiConnectorToolService, object, Task<object>> Invoke { get; }

    public JsonElement InputSchema => _inputSchema.Value;
}

/// <summary>The nine tools of design 74 "Tool contract" and the two of design 75, all read-only.</summary>
public static class AiConnectorToolCatalog
{
    public const string SearchOrders = "search_orders";
    public const string GetOrder = "get_order";
    public const string SearchTrips = "search_trips";
    public const string GetTrip = "get_trip";
    public const string GetLegs = "get_legs";
    public const string SearchRelations = "search_relations";
    public const string SearchVehicles = "search_vehicles";
    public const string GetOrderRates = "get_order_rates";
    public const string GetCustomerRevenue = "get_customer_revenue";
    public const string ListMetrics = "list_metrics";
    public const string QueryMetrics = "query_metrics";

    /// <summary>The fixed text every revenue answer carries (design 74 "get_customer_revenue").</summary>
    public const string RevenueDefinition =
        "Calculated revenue stored on transport orders, not invoiced revenue. Orders count by order date, with status Open, " +
        "ReadyForInvoicing, Invoiced or Archived. Draft and Cancelled orders do not count; ExcludedDraftCount and " +
        "ExcludedCancelledCount state how many there were. An order whose revenue is not calculated yet is in no total: " +
        "OrdersWithoutAmount counts those, so a total is only complete when that count is zero.";

    private static readonly string[][] TransportOrders =
        [[AppFeatures.Transport], [AppFeatures.TransportApp.Orders]];

    private static readonly string[][] TransportOrPlanboardTrips =
        [[AppFeatures.Transport, AppFeatures.Planboard], [AppFeatures.TransportApp.Trips, AppFeatures.Planboard]];

    /* Design 75: the feature only; which metric this user may ask is IMetricQuery's check, per metric. */
    private static readonly string[][] Metrics = [[ReportingFeatures.Metrics]];

    public static readonly IReadOnlyList<AiConnectorToolDefinition> All =
    [
        Define<SearchOrdersInput, AiToolList<AiOrderRow>>(
            SearchOrders, "Search transport orders",
            "Lists transport orders of this organisation, newest first, with customer, first and last stop, freight counts and progress. Amounts are not included; use get_order_rates.",
            [OrdersPermissions.TransportOrders.Default], TransportOrders,
            (tools, input) => tools.SearchOrdersAsync(input)),
        Define<GetOrderInput, AiOrderDetail>(
            GetOrder, "Get a transport order",
            "One transport order with its references, default stops and freight lines. Amounts are not included; use get_order_rates.",
            [OrdersPermissions.TransportOrders.Default], TransportOrders,
            (tools, input) => tools.GetOrderAsync(input)),
        Define<SearchTripsInput, AiToolList<AiTripRow>>(
            SearchTrips, "Search trips",
            "Lists trips of this organisation by trip date, with carrier, vehicles, customers, order numbers, times, distances and warnings. Distances in metres, durations in seconds.",
            [PlanningExecutionPermissions.TransportTrips.Default], TransportOrPlanboardTrips,
            (tools, input) => tools.SearchTripsAsync(input)),
        Define<GetTripInput, AiTripDetail>(
            GetTrip, "Get a trip",
            "One trip with its vehicles, stops and totals. Distances in metres, durations in seconds.",
            [PlanningExecutionPermissions.TransportTrips.Default], TransportOrPlanboardTrips,
            (tools, input) => tools.GetTripAsync(input)),
        Define<GetLegsInput, AiToolList<AiLegRow>>(
            GetLegs, "List the legs of an order or trip",
            "The legs of one order or one trip: origin, destination, freight, distance, duration and estimated CO2. Give orderId or tripId.",
            [PlanningExecutionPermissions.Legs.Default], TransportOrPlanboardTrips,
            (tools, input) => tools.GetLegsAsync(input)),
        Define<SearchRelationsInput, AiToolList<AiRelationRow>>(
            SearchRelations, "Search relations",
            "Lists relations (customers, carriers, depots) with code, name, city, roles and categories, archived ones included. Use it to find the id other tools ask for.",
            [MasterDataPermissions.Relations.Default],
            [[AppFeatures.MasterData.Default], [AppFeatures.MasterData.General.Default], [AppFeatures.MasterData.General.Relations.Default], [AppFeatures.MasterData.General.Relations.RelationsList]],
            (tools, input) => tools.SearchRelationsAsync(input)),
        Define<SearchVehiclesInput, AiToolList<AiVehicleRow>>(
            SearchVehicles, "Search vehicles",
            "Lists vehicles with licence plate, type, fuel, euro class, owner and home depot, archived ones included.",
            [MasterDataPermissions.Vehicles.Default],
            [[AppFeatures.MasterData.Default], [AppFeatures.MasterData.General.Default], [AppFeatures.MasterData.General.Fleet.Default], [AppFeatures.MasterData.General.Fleet.Vehicles]],
            (tools, input) => tools.SearchVehiclesAsync(input)),
        Define<GetOrderRatesInput, AiOrderRates>(
            GetOrderRates, "Get the rates of an order",
            "The stored rate lines and totals of one order. State says whether they are up to date. Amounts are stored values in the given currency; do not add them across currencies.",
            [FinancialPermissions.RateLines.Default], [[AppFeatures.Transport]],
            (tools, input) => tools.GetOrderRatesAsync(input)),
        Define<GetCustomerRevenueInput, AiCustomerRevenue>(
            GetCustomerRevenue, "Get the revenue of a customer",
            "The revenue of one customer in a period, per currency, added up by the database. Report the figures with their definition, never as invoiced revenue. " + RevenueDefinition,
            [FinancialPermissions.RateLines.Default, OrdersPermissions.TransportOrders.Default], TransportOrders,
            (tools, input) => tools.GetCustomerRevenueAsync(input)),
        Define<ListMetricsInput, AiMetricList>(
            ListMetrics, "List the metrics",
            "The numbers this user may ask with query_metrics: each metric with its unit, its exact definition, its breakdowns and its filters. factsReady says whether the numbers are prepared yet. Start here for any question about totals, counts or trends.",
            [], Metrics,
            (tools, input) => tools.ListMetricsAsync(input)),
        Define<QueryMetricsInput, AiMetricAnswer>(
            QueryMetrics, "Query the metrics",
            "Answers numbers from a fixed list: choose metrics of one kind (order or trip), a period, breakdowns and filters. The database adds up, so never add rows of different currencies yourself. Report a figure with its definition, its period and what coverage left out; with warning mixed_currencies an amount is not a clean total. Any other status than ok is a refusal: tell the user what it says, never guess a number.",
            [], Metrics,
            (tools, input) => tools.QueryMetricsAsync(input)),
    ];

    public static AiConnectorToolDefinition? Find(string name) => All.FirstOrDefault(tool => tool.Name == name);

    private static AiConnectorToolDefinition Define<TInput, TResult>(
        string name,
        string title,
        string description,
        string[] permissions,
        string[][] featureRequirements,
        Func<IAiConnectorToolService, TInput, Task<TResult>> call)
        where TInput : class
        where TResult : class
        => new(name, title, description, typeof(TInput), permissions, featureRequirements,
            async (tools, input) => await call(tools, (TInput)input));
}

/// <summary>
/// The JSON Schema of a tool's input, from its typed DTO: limits and descriptions come from the DTO's attributes. An
/// optional field is simply left out, so its schema names the plain type: not every AI tool accepts a union with null.
/// </summary>
internal static class AiConnectorToolSchema
{
    public static JsonElement For(Type inputType)
    {
        var node = AiConnectorJson.Options.GetJsonSchemaAsNode(inputType, new JsonSchemaExporterOptions
        {
            TransformSchemaNode = (context, schema) =>
            {
                if (schema is JsonObject property && context.PropertyInfo?.AttributeProvider is { } attributes)
                {
                    Annotate(property, attributes);
                }

                if (schema is JsonObject value)
                {
                    DropNull(value);
                }

                return schema;
            }
        });
        return JsonSerializer.SerializeToElement(node);
    }

    private static void Annotate(JsonObject property, ICustomAttributeProvider attributes)
    {
        foreach (var attribute in attributes.GetCustomAttributes(true))
        {
            switch (attribute)
            {
                case DescriptionAttribute description:
                    property["description"] = description.Description;
                    break;
                case MaxLengthAttribute maxLength:
                    property["maxLength"] = maxLength.Length;
                    break;
                case RangeAttribute range:
                    property["minimum"] = JsonValue.Create(Convert.ToInt32(range.Minimum));
                    property["maximum"] = JsonValue.Create(Convert.ToInt32(range.Maximum));
                    break;
            }
        }
    }

    private static void DropNull(JsonObject schema)
    {
        if (schema["type"] is JsonArray types)
        {
            var named = types.Select(t => t?.GetValue<string>()).Where(t => t != "null").ToList();
            schema["type"] = named.Count == 1 ? named[0] : new JsonArray(named.Select(t => JsonValue.Create(t)).ToArray<JsonNode?>());
        }

        if (schema["enum"] is JsonArray values)
        {
            schema["enum"] = new JsonArray(values.Where(v => v != null).Select(v => v!.DeepClone()).ToArray());
            schema["type"] ??= "string";
        }
    }
}
