using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTms.Financial.Permissions;
using OpenTms.HostIntegration;
using OpenTms.Integrations.AiConnector;
using OpenTms.Integrations.Permissions;
using OpenTms.Orders.Permissions;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.Features;
using OpenTms.Reporting.Facts;
using OpenTms.Reporting.Features;
using OpenTms.Reporting.Metrics;
using OpenTms.Reporting.Published;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;
using static OpenTms.AiConnector.AiConnectorHarness;

namespace OpenTms.AiConnector;

/// <summary>
/// Design 75 on the real host: <c>list_metrics</c> and <c>query_metrics</c> over <c>/mcp</c> with real grants and stored feature values. "Tenant isolation" (Critical): each tool, each grain and a group query return only the
/// tenant's facts, for tenants with their own database and a shared-tier pair, and a filter id of another tenant gives zero rows and no name. "Money behind permissions" (Critical): each money metric refused without
/// <c>Financial.RateLines</c>, absent from <c>list_metrics</c>, refused in a mix with an allowed metric, no refusal revealing a value; <c>IMetricQuery</c> called directly checks for itself. Also the equal answer to
/// <c>get_customer_revenue</c>, <c>mixed_currencies</c>, the refusal statuses, the 2-second timeout over HTTP, and logs without a filter's value. The rest of the design's rows are in <c>PostgreSql/Reporting/</c>.
/// </summary>
[Collection(HostIntegrationCollection.Name)]
public class McpMetricsHostTests
{
    private static readonly string[] MoneyMetrics = MetricCatalog.All.Where(m => m.IsMoney).Select(m => m.Key).ToArray();

    private static readonly string[] MetricUserPermissions =
    [
        IntegrationsPermissions.AiConnector.Use, OrdersPermissions.TransportOrders.Default, PlanningExecutionPermissions.TransportTrips.Default, FinancialPermissions.RateLines.Default,
    ];

    private static readonly Dictionary<Guid, MetricWorld> Worlds = [];

    private readonly AiConnectorHarness _h;

    public McpMetricsHostTests(HostIntegrationFixture host)
    {
        _h = new AiConnectorHarness(host);
    }

    private sealed record MetricWorld(ConnectorWorld World, Guid Customer, Guid Carrier, Guid TripType, Guid OrderType, Guid Unit, decimal Revenue, decimal TripCost);

    private string Tag(Guid tenant)
        => tenant == _h.TenantA ? "TNTAAA" : tenant == _h.TenantB ? "TNTBBB" : tenant == _h.TenantC ? "TNTCCC" : "TNTDDD";

    private static readonly DateOnly Day = new(2026, 5, 4);

    /// <summary>Facts from the real calculators over the world's real order and trip, plus a mix of stored facts; the backfill marked finished, as its runner leaves it.</summary>
    private async Task<MetricWorld> FactsOfAsync(Guid tenant)
    {
        if (Worlds.TryGetValue(tenant, out var existing))
        {
            await _h.ReadyAsync(tenant);
            return existing;
        }

        var world = await _h.WorldOfAsync(tenant, Tag(tenant));
        await _h.ReadyAsync(tenant);
        var revenue = 1_000m + Math.Abs(Tag(tenant).GetHashCode() % 1000);
        var tripCost = 2_000m + Math.Abs(Tag(tenant).GetHashCode() % 1000);
        var unit = Guid.NewGuid();
        await _h.InScopeAsync(tenant, async services =>
        {
            await services.GetRequiredService<OrderFactCalculator>().CalculateAsync(new DerivedValueCalculationContext(ReportingFactConsts.OrderFactTargetType, world.OrderId, 1, CancellationToken.None));
            await services.GetRequiredService<TripFactCalculator>().CalculateAsync(new DerivedValueCalculationContext(ReportingFactConsts.TripFactTargetType, world.TripId, 1, CancellationToken.None));

            var orders = services.GetRequiredService<IRepository<OrderFact, Guid>>();
            var second = new OrderFact(Guid.NewGuid(), tenant);
            second.Apply(Day, TransportOrderStatus.Open, world.CustomerId, unit, world.Base.OrderTypeId, "EUR", revenue, 10m, revenue - 10m, 2m, DateTime.Now);
            await orders.InsertAsync(second, autoSave: true);

            var trips = services.GetRequiredService<IRepository<TripFact, Guid>>();
            var trip = new TripFact(Guid.NewGuid(), tenant);
            trip.Apply(Day, OpenTms.PlanningExecution.TransportTrips.TransportTripStatus.Planned, OpenTms.PlanningExecution.TransportTrips.TripExecution.OwnFleet, world.CarrierId, world.VehicleId, unit, world.Base.TripTypeId, tripCost, tripCost + 5m, 1m, 0.5m, 1000, 900, 2, 1, DateTime.Now);
            await trips.InsertAsync(trip, autoSave: true);

            await services.GetRequiredService<ReportingFactState>().SetBackfillCompletedAtAsync(DateTime.Now);
        });

        var made = new MetricWorld(world, world.CustomerId, world.CarrierId, world.Base.TripTypeId, world.Base.OrderTypeId, unit, revenue, tripCost);
        Worlds[tenant] = made;
        return made;
    }

    private async Task<string> TokenAsync(Guid tenant, params string[] permissions)
    {
        var user = await _h.CreateUserAsync(tenant, permissions.Length == 0 ? MetricUserPermissions : permissions);
        return (await _h.ConnectAsync(user)).AccessToken!;
    }

    private static Dictionary<string, object?> Ask(string[] metrics, string[]? groupBy = null, Dictionary<string, object?>? filters = null, string from = "2026-01-01", string to = "2026-12-31")
    {
        var args = new Dictionary<string, object?> { ["metrics"] = metrics, ["dateFrom"] = from, ["dateTo"] = to };
        if (groupBy != null)
        {
            args["groupBy"] = groupBy;
        }

        if (filters != null)
        {
            args["filters"] = filters;
        }

        return args;
    }

    /* ---------- Over HTTP ---------- */

    [Fact]
    public async Task List_metrics_and_query_metrics_answer_over_http_with_real_grants_and_stored_features()
    {
        var facts = await FactsOfAsync(_h.TenantA);
        var token = await TokenAsync(_h.TenantA);

        var tools = await _h.ListToolsAsync(token);
        tools.ShouldContain(ToolNames.ListMetrics);
        tools.ShouldContain(ToolNames.QueryMetrics);

        var list = await _h.ToolJsonAsync(token, ToolNames.ListMetrics);
        list.GetProperty("factsReady").GetBoolean().ShouldBeTrue();
        list.GetProperty("metrics").EnumerateArray().Select(m => m.GetProperty("key").GetString()).ShouldBe(MetricCatalog.All.Select(m => m.Key), ignoreOrder: true);
        var orderRevenue = list.GetProperty("metrics").EnumerateArray().Single(m => m.GetProperty("key").GetString() == MetricKeys.OrderRevenue);
        orderRevenue.GetProperty("unit").GetString().ShouldBe(MetricUnits.Money);
        orderRevenue.GetProperty("definition").GetString().ShouldContain("Not invoiced revenue");
        orderRevenue.GetProperty("breakdowns").EnumerateArray().Select(b => b.GetString()).ShouldBe(MetricCatalog.OrderBreakdowns, ignoreOrder: true);
        orderRevenue.GetProperty("filters").EnumerateArray().Select(b => b.GetString()).ShouldBe(MetricCatalog.OrderFilters, ignoreOrder: true);

        var answer = await _h.CallToolAsync(token, ToolNames.QueryMetrics, Ask([MetricKeys.OrderRevenue, MetricKeys.OrderCount], [MetricBreakdowns.Customer], from: "2026-05-01", to: "2026-05-31"));
        answer.IsError.ShouldBeFalse(answer.Text);
        var json = answer.Json;
        json.GetProperty("status").GetString().ShouldBe(MetricAnswerStatus.Ok);
        json.GetProperty("period").GetProperty("dateFrom").GetString().ShouldBe("2026-05-01");
        json.GetProperty("definitions").GetArrayLength().ShouldBe(2);
        var row = json.GetProperty("rows").EnumerateArray().Single();
        row.GetProperty("currencyCode").GetString().ShouldBe("EUR");
        var group = row.GetProperty("groups").EnumerateArray().Single();
        group.GetProperty("breakdown").GetString().ShouldBe(MetricBreakdowns.Customer);
        group.GetProperty("id").GetGuid().ShouldBe(facts.Customer);
        group.GetProperty("name").GetString().ShouldBe(facts.World.CustomerName, "a name looked up at read time, from Master Data");
        row.GetProperty("values").GetProperty(MetricKeys.OrderRevenue).GetDecimal().ShouldBe(Marker.OrderRevenue + facts.Revenue);
        row.GetProperty("values").GetProperty(MetricKeys.OrderCount).GetDecimal().ShouldBe(2);
        json.GetProperty("coverage").GetProperty("recordsIncluded").GetInt64().ShouldBe(2);
        json.GetProperty("lastFactUpdateAt").ValueKind.ShouldBe(JsonValueKind.String);

        var trips = await _h.ToolJsonAsync(token, ToolNames.QueryMetrics, Ask([MetricKeys.TripCost], [MetricBreakdowns.Carrier], from: "2026-05-01", to: "2026-05-31"));
        var tripRow = trips.GetProperty("rows").EnumerateArray().Single();
        tripRow.GetProperty("groups")[0].GetProperty("name").GetString().ShouldBe(facts.World.CarrierName);
        tripRow.GetProperty("values").GetProperty(MetricKeys.TripCost).GetDecimal().ShouldBe(Marker.TripCost + facts.TripCost);
    }

    [Fact]
    public async Task Order_revenue_of_a_customer_equals_get_customer_revenue_over_the_same_connection()
    {
        var facts = await FactsOfAsync(_h.TenantA);
        var token = await TokenAsync(_h.TenantA, AllToolPermissions);

        /* get_customer_revenue reads the orders; the order facts must say the same for the customer's stored revenue. */
        var customer = Guid.NewGuid();
        var mixed = await _h.InScopeAsync(_h.TenantA, async services =>
        {
            var ids = new List<Guid>
            {
                await ConnectorWorldBuilder.SeedOrderAsync(services, _h.TenantA, customer, new DateOnly(2025, 2, 1), TransportOrderStatus.Open, "EUR", 100.25m),
                await ConnectorWorldBuilder.SeedOrderAsync(services, _h.TenantA, customer, new DateOnly(2025, 2, 2), TransportOrderStatus.Invoiced, "EUR", 200.50m),
                await ConnectorWorldBuilder.SeedOrderAsync(services, _h.TenantA, customer, new DateOnly(2025, 2, 3), TransportOrderStatus.Open, "USD", 30m),
                await ConnectorWorldBuilder.SeedOrderAsync(services, _h.TenantA, customer, new DateOnly(2025, 2, 4), TransportOrderStatus.Draft, "EUR", 999m),
                await ConnectorWorldBuilder.SeedOrderAsync(services, _h.TenantA, customer, new DateOnly(2025, 2, 5), TransportOrderStatus.Open, "EUR", null),
            };
            foreach (var id in ids)
            {
                await services.GetRequiredService<OrderFactCalculator>().CalculateAsync(new DerivedValueCalculationContext(ReportingFactConsts.OrderFactTargetType, id, 1, CancellationToken.None));
            }

            return ids;
        });
        mixed.Count.ShouldBe(5);

        var revenue = await _h.ToolJsonAsync(token, ToolNames.GetCustomerRevenue, new Dictionary<string, object?> { ["customerRelationId"] = customer, ["dateFrom"] = "2025-01-01", ["dateTo"] = "2025-12-31" });
        var metric = await _h.ToolJsonAsync(await TokenAsync(_h.TenantA), ToolNames.QueryMetrics, Ask([MetricKeys.OrderRevenue], null, new Dictionary<string, object?> { ["customerRelationId"] = customer }, "2025-01-01", "2025-12-31"));

        var byTool = revenue.GetProperty("totals").EnumerateArray().ToDictionary(t => t.GetProperty("currencyCode").GetString()!, t => t.GetProperty("revenueAmount").GetDecimal());
        var byMetric = metric.GetProperty("rows").EnumerateArray().Where(r => r.GetProperty("values").GetProperty(MetricKeys.OrderRevenue).ValueKind == JsonValueKind.Number).ToDictionary(r => r.GetProperty("currencyCode").GetString()!, r => r.GetProperty("values").GetProperty(MetricKeys.OrderRevenue).GetDecimal());
        byMetric.ShouldBe(byTool);
        byTool["EUR"].ShouldBe(300.75m);
        byTool["USD"].ShouldBe(30m);
        revenue.GetProperty("ordersWithoutAmount").GetInt32().ShouldBe((int)metric.GetProperty("coverage").GetProperty("recordsWithoutValue").GetInt64());
        revenue.GetProperty("excludedDraftCount").GetInt32().ShouldBe((int)metric.GetProperty("coverage").GetProperty("excludedByStatus").GetInt64());
    }

    [Fact]
    public async Task Mixed_currencies_reaches_the_ai_tool_over_http()
    {
        await FactsOfAsync(_h.TenantA);
        var token = await TokenAsync(_h.TenantA);
        var day = new DateOnly(2024, 6, 3);
        await _h.InScopeAsync(_h.TenantA, async services =>
        {
            foreach (var currency in new[] { "EUR", "USD" })
            {
                var fact = new OrderFact(Guid.NewGuid(), _h.TenantA);
                fact.Apply(day, TransportOrderStatus.Open, Guid.NewGuid(), null, Guid.NewGuid(), currency, 10m, 5m, 5m, null, DateTime.Now);
                await services.GetRequiredService<IRepository<OrderFact, Guid>>().InsertAsync(fact, autoSave: true);
            }
        });

        var cost = await _h.ToolJsonAsync(token, ToolNames.QueryMetrics, Ask([MetricKeys.OrderCost], null, null, "2024-06-03", "2024-06-03"));
        cost.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ShouldBe([MetricWarnings.MixedCurrencies]);

        var revenue = await _h.ToolJsonAsync(token, ToolNames.QueryMetrics, Ask([MetricKeys.OrderRevenue], null, null, "2024-06-03", "2024-06-03"));
        revenue.TryGetProperty("warnings", out _).ShouldBeFalse("order_revenue splits by currency, so nothing is mixed");
        revenue.GetProperty("rows").GetArrayLength().ShouldBe(2);
    }

    /* ---------- Tenant isolation ---------- */

    [Theory]
    [InlineData("own")]
    [InlineData("shared")]
    public async Task Each_tool_each_grain_and_a_group_query_return_only_the_tenants_facts(string tier)
    {
        var (mine, other, otherName) = _h.Pair(tier);
        var myFacts = await FactsOfAsync(mine);
        var theirFacts = await FactsOfAsync(other);
        var token = await TokenAsync(mine);

        var cases = new[]
        {
            (Metric: MetricKeys.OrderRevenue, Expected: Marker.OrderRevenue + myFacts.Revenue, Group: MetricBreakdowns.Customer, Name: myFacts.World.CustomerName, Id: myFacts.Customer),
            (Metric: MetricKeys.TripCost, Expected: Marker.TripCost + myFacts.TripCost, Group: MetricBreakdowns.Carrier, Name: myFacts.World.CarrierName, Id: myFacts.Carrier),
        };
        foreach (var (metric, expected, group, name, id) in cases)
        {
            /* Without a group, with one, and asking for the other tenant in the request. */
            var total = await _h.CallToolAsync(token, ToolNames.QueryMetrics, Ask([metric], null, null, "2026-05-01", "2026-05-31"));
            total.IsError.ShouldBeFalse(total.Text);
            total.Json.GetProperty("rows").EnumerateArray().Single().GetProperty("values").GetProperty(metric).GetDecimal().ShouldBe(expected, metric);
            total.Text.ShouldNotContain(theirFacts.World.Tag);

            var grouped = await _h.CallToolAsync(token, ToolNames.QueryMetrics, Ask([metric], [group], null, "2026-05-01", "2026-05-31"));
            var row = grouped.Json.GetProperty("rows").EnumerateArray().Single();
            row.GetProperty("groups")[0].GetProperty("id").GetGuid().ShouldBe(id);
            row.GetProperty("groups")[0].GetProperty("name").GetString().ShouldBe(name);
            grouped.Text.ShouldNotContain(theirFacts.World.Tag);

            var asked = await _h.CallToolAsync(token, ToolNames.QueryMetrics, Ask([metric], [group], null, "2026-05-01", "2026-05-31"), request =>
            {
                request.Headers.Add("__tenant", other.ToString());
                request.RequestUri = new Uri("/mcp?__tenant=" + otherName, UriKind.Relative);
            });
            asked.Json.GetProperty("rows").EnumerateArray().Single().GetProperty("values").GetProperty(metric).GetDecimal().ShouldBe(expected, "the token decides");
        }

        var list = await _h.CallToolAsync(token, ToolNames.ListMetrics);
        list.IsError.ShouldBeFalse();
        list.Text.ShouldNotContain(theirFacts.World.Tag);
    }

    [Theory]
    [InlineData("own")]
    [InlineData("shared")]
    public async Task A_filter_id_of_another_tenant_gives_zero_rows_and_no_name(string tier)
    {
        var (mine, other, _) = _h.Pair(tier);
        await FactsOfAsync(mine);
        var theirs = await FactsOfAsync(other);
        var token = await TokenAsync(mine);

        var filters = new (string Metric, string Key, Guid Id, string Group)[]
        {
            (MetricKeys.OrderRevenue, "customerRelationId", theirs.Customer, MetricBreakdowns.Customer),
            (MetricKeys.OrderCount, "organizationUnitId", theirs.Unit, MetricBreakdowns.OrganizationUnit),
            (MetricKeys.TripCost, "carrierRelationId", theirs.Carrier, MetricBreakdowns.Carrier),
            (MetricKeys.TripCount, "vehicleId", theirs.World.VehicleId, MetricBreakdowns.Vehicle),
            (MetricKeys.TripCount, "organizationUnitId", theirs.Unit, MetricBreakdowns.OrganizationUnit),
        };
        foreach (var (metric, key, id, group) in filters)
        {
            var answer = await _h.CallToolAsync(token, ToolNames.QueryMetrics, Ask([metric], [group], new Dictionary<string, object?> { [key] = id }));
            answer.IsError.ShouldBeFalse(answer.Text);
            var json = answer.Json;
            json.GetProperty("status").GetString().ShouldBe(MetricAnswerStatus.Ok, key);
            json.GetProperty("rows").GetArrayLength().ShouldBe(0, key);
            json.GetProperty("coverage").GetProperty("recordsIncluded").GetInt64().ShouldBe(0, key);
            answer.Text.ShouldNotContain(theirs.World.Tag);
            answer.Text.ShouldNotContain(id.ToString(), Case.Insensitive);
        }
    }

    /* ---------- Money ---------- */

    [Fact]
    public async Task Without_RateLines_each_money_metric_is_absent_from_the_list_and_refused_alone_and_in_a_mix_and_reveals_no_value()
    {
        await FactsOfAsync(_h.TenantA);
        var token = await TokenAsync(_h.TenantA, IntegrationsPermissions.AiConnector.Use, OrdersPermissions.TransportOrders.Default, PlanningExecutionPermissions.TransportTrips.Default);

        var list = await _h.ToolJsonAsync(token, ToolNames.ListMetrics);
        var keys = list.GetProperty("metrics").EnumerateArray().Select(m => m.GetProperty("key").GetString()!).ToList();
        keys.ShouldBe(MetricCatalog.All.Where(m => !m.IsMoney).Select(m => m.Key), ignoreOrder: true);
        MoneyMetrics.Length.ShouldBe(6, "three of each grain in the design's catalog");

        foreach (var money in MoneyMetrics)
        {
            var definition = MetricCatalog.Find(money)!;
            var allowed = definition.Grain == MetricGrain.Order ? MetricKeys.OrderCount : MetricKeys.TripCount;
            foreach (var request in new[] { new[] { money }, new[] { allowed, money }, new[] { money, allowed } })
            {
                var answer = await _h.CallToolAsync(token, ToolNames.QueryMetrics, Ask(request));
                answer.IsError.ShouldBeFalse("a refusal is an answer: " + answer.Text);
                var json = answer.Json;
                json.GetProperty("status").GetString().ShouldBe(MetricAnswerStatus.NotAllowed, string.Join("+", request));
                json.TryGetProperty("rows", out _).ShouldBeFalse();
                json.TryGetProperty("coverage", out _).ShouldBeFalse();
                json.TryGetProperty("totalGroups", out _).ShouldBeFalse();
                json.TryGetProperty("definitions", out _).ShouldBeFalse();
                json.TryGetProperty("lastFactUpdateAt", out _).ShouldBeFalse();
                answer.Text.ShouldNotContain("987654");
            }
        }

        /* The rest of the list still answers. */
        (await _h.ToolJsonAsync(token, ToolNames.QueryMetrics, Ask([MetricKeys.OrderCount]))).GetProperty("status").GetString().ShouldBe(MetricAnswerStatus.Ok);
        (await _h.ToolJsonAsync(token, ToolNames.QueryMetrics, Ask([MetricKeys.TripCo2Kg]))).GetProperty("status").GetString().ShouldBe(MetricAnswerStatus.Ok);
    }

    [Fact]
    public async Task A_money_refusal_comes_before_the_backfills_progress()
    {
        var tenant = _h.TenantB;
        await FactsOfAsync(tenant);
        var token = await TokenAsync(tenant, IntegrationsPermissions.AiConnector.Use, OrdersPermissions.TransportOrders.Default);
        await _h.InScopeAsync(tenant, services => services.GetRequiredService<ReportingFactState>().ClearBackfillAsync());
        try
        {
            var refused = await _h.ToolJsonAsync(token, ToolNames.QueryMetrics, Ask([MetricKeys.OrderRevenue]));
            refused.GetProperty("status").GetString().ShouldBe(MetricAnswerStatus.NotAllowed);
            refused.GetProperty("hint").GetString()!.ShouldNotContain("prepared");

            var notReady = await _h.ToolJsonAsync(token, ToolNames.QueryMetrics, Ask([MetricKeys.OrderCount]));
            notReady.GetProperty("status").GetString().ShouldBe(MetricAnswerStatus.NotReady);
            notReady.GetProperty("hint").GetString()!.ShouldContain("prepared");
            (await _h.ToolJsonAsync(token, ToolNames.ListMetrics)).GetProperty("factsReady").GetBoolean().ShouldBeFalse();
        }
        finally
        {
            await _h.InScopeAsync(tenant, services => services.GetRequiredService<ReportingFactState>().SetBackfillCompletedAtAsync(DateTime.Now));
        }
    }

    [Fact]
    public async Task Without_a_grains_permission_or_its_feature_its_metrics_are_refused_and_not_listed()
    {
        var tenant = _h.TenantC;
        await FactsOfAsync(tenant);

        var ordersOnly = await TokenAsync(tenant, IntegrationsPermissions.AiConnector.Use, OrdersPermissions.TransportOrders.Default);
        var ordersList = await _h.ToolJsonAsync(ordersOnly, ToolNames.ListMetrics);
        ordersList.GetProperty("metrics").EnumerateArray().Select(m => m.GetProperty("key").GetString()).ShouldBe([MetricKeys.OrderCount, MetricKeys.OrderCo2Kg], ignoreOrder: true);
        (await _h.ToolJsonAsync(ordersOnly, ToolNames.QueryMetrics, Ask([MetricKeys.TripCount]))).GetProperty("status").GetString().ShouldBe(MetricAnswerStatus.NotAllowed);

        var all = await TokenAsync(tenant);
        await _h.WithFeatureOffAsync(tenant, AppFeatures.TransportApp.Orders, async () =>
        {
            (await _h.ToolJsonAsync(all, ToolNames.QueryMetrics, Ask([MetricKeys.OrderCount]))).GetProperty("status").GetString().ShouldBe(MetricAnswerStatus.NotAllowed);
            (await _h.ToolJsonAsync(all, ToolNames.QueryMetrics, Ask([MetricKeys.TripCount]))).GetProperty("status").GetString().ShouldBe(MetricAnswerStatus.Ok);
        });

        /* Trips: the Transport app or the Planboard. */
        await _h.WithFeatureOffAsync(tenant, AppFeatures.Transport, async () =>
        {
            (await _h.ToolJsonAsync(all, ToolNames.QueryMetrics, Ask([MetricKeys.TripCount]))).GetProperty("status").GetString().ShouldBe(MetricAnswerStatus.Ok, "the Planboard alone");
            (await _h.ToolJsonAsync(all, ToolNames.QueryMetrics, Ask([MetricKeys.OrderCount]))).GetProperty("status").GetString().ShouldBe(MetricAnswerStatus.NotAllowed);
            await _h.WithFeatureOffAsync(tenant, AppFeatures.Planboard, async () =>
                (await _h.ToolJsonAsync(all, ToolNames.QueryMetrics, Ask([MetricKeys.TripCount]))).GetProperty("status").GetString().ShouldBe(MetricAnswerStatus.NotAllowed));
        });

        await _h.WithFeatureOffAsync(tenant, ReportingFeatures.Metrics, async () =>
        {
            var tools = await _h.ListToolsAsync(all);
            tools.ShouldNotContain(ToolNames.ListMetrics);
            tools.ShouldNotContain(ToolNames.QueryMetrics);
            (await _h.CallToolAsync(all, ToolNames.QueryMetrics, Ask([MetricKeys.OrderCount]))).IsError.ShouldBeTrue();
        });
    }

    [Fact]
    public async Task IMetricQuery_called_directly_checks_for_itself_with_real_grants_and_gives_nothing_to_no_user()
    {
        var tenant = _h.TenantA;
        await FactsOfAsync(tenant);
        var nobody = await _h.CreateUserAsync(tenant);
        var ordersOnly = await _h.CreateUserAsync(tenant, OrdersPermissions.TransportOrders.Default);
        var request = new MetricQueryRequestDto { Metrics = [MetricKeys.OrderRevenue], DateFrom = new DateOnly(2026, 1, 1), DateTo = new DateOnly(2026, 12, 31) };

        foreach (var user in new[] { nobody, ordersOnly })
        {
            var refused = await _h.InScopeAsync(tenant, services => services.GetRequiredService<IMetricQuery>().QueryAsync(request), PrincipalOf(user));
            refused.Status.ShouldBe(MetricAnswerStatus.NotAllowed);
            refused.Rows.ShouldBeNull();
        }

        var anonymous = await _h.InScopeAsync(tenant, services => services.GetRequiredService<IMetricQuery>().QueryAsync(request));
        anonymous.Status.ShouldBe(MetricAnswerStatus.NotAllowed, "no user holds no permission");

        (await _h.InScopeAsync(tenant, services => services.GetRequiredService<IMetricQuery>().ListAsync(), PrincipalOf(nobody))).Metrics.ShouldBeEmpty();
        var justOrders = await _h.InScopeAsync(tenant, services => services.GetRequiredService<IMetricQuery>().ListAsync(), PrincipalOf(ordersOnly));
        justOrders.Metrics.Select(m => m.Key).ShouldBe([MetricKeys.OrderCount, MetricKeys.OrderCo2Kg], ignoreOrder: true);
    }

    /* ---------- Refusals over HTTP ---------- */

    public static TheoryData<string, Dictionary<string, object?>, string> Refusals => new()
    {
        { "unknown", new() { ["metrics"] = new[] { "angry_customers" }, ["dateFrom"] = "2026-01-01", ["dateTo"] = "2026-01-02" }, MetricAnswerStatus.UnknownMetric },
        { "both grains", new() { ["metrics"] = new[] { MetricKeys.OrderCount, MetricKeys.TripCount }, ["dateFrom"] = "2026-01-01", ["dateTo"] = "2026-01-02" }, MetricAnswerStatus.UnsupportedCombination },
        { "no metrics", new() { ["dateFrom"] = "2026-01-01", ["dateTo"] = "2026-01-02" }, MetricAnswerStatus.UnsupportedCombination },
        { "other grain's breakdown", new() { ["metrics"] = new[] { MetricKeys.OrderCount }, ["groupBy"] = new[] { MetricBreakdowns.Carrier }, ["dateFrom"] = "2026-01-01", ["dateTo"] = "2026-01-02" }, MetricAnswerStatus.UnsupportedCombination },
        { "no period", new() { ["metrics"] = new[] { MetricKeys.OrderCount } }, MetricAnswerStatus.InvalidPeriod },
        { "reversed", new() { ["metrics"] = new[] { MetricKeys.OrderCount }, ["dateFrom"] = "2026-02-01", ["dateTo"] = "2026-01-02" }, MetricAnswerStatus.InvalidPeriod },
        { "over five years", new() { ["metrics"] = new[] { MetricKeys.OrderCount }, ["dateFrom"] = "2020-01-01", ["dateTo"] = "2026-01-02" }, MetricAnswerStatus.InvalidPeriod },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_refusal_comes_back_as_a_tool_result_with_a_status_message_and_hint(string what, Dictionary<string, object?> args, string status)
    {
        await FactsOfAsync(_h.TenantA);
        var answer = await _h.CallToolAsync(await TokenAsync(_h.TenantA), ToolNames.QueryMetrics, args);
        answer.IsError.ShouldBeFalse(what + ": the status is the answer");
        var json = answer.Json;
        json.GetProperty("status").GetString().ShouldBe(status, what);
        json.GetProperty("message").GetString().ShouldNotBeNullOrEmpty();
        json.GetProperty("hint").GetString().ShouldNotBeNullOrEmpty();
        json.TryGetProperty("rows", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_date_that_is_no_date_is_the_connectors_invalid_arguments_result()
    {
        await FactsOfAsync(_h.TenantA);
        var answer = await _h.CallToolAsync(await TokenAsync(_h.TenantA), ToolNames.QueryMetrics, new Dictionary<string, object?> { ["metrics"] = new[] { MetricKeys.OrderCount }, ["dateFrom"] = "SECRET-not-a-date", ["dateTo"] = "2026-01-02" });
        answer.IsError.ShouldBeTrue(answer.Text);
        answer.Text.ShouldStartWith("Invalid arguments");
        answer.Text.ShouldNotContain("SECRET");
    }

    [Fact]
    public async Task A_query_past_two_seconds_answers_timeout_over_http_with_no_partial_answer()
    {
        var tenant = _h.TenantC;
        await FactsOfAsync(tenant);
        var token = await TokenAsync(tenant);

        await using var blocker = new NpgsqlConnection(HostIntegrationFixture.ConnectionStringFor(HostIntegrationFixture.TenantCDatabase));
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using (var lockFacts = new NpgsqlCommand("LOCK TABLE reporting.\"OrderFacts\" IN ACCESS EXCLUSIVE MODE", blocker, transaction))
        {
            await lockFacts.ExecuteNonQueryAsync();
        }

        var watch = Stopwatch.StartNew();
        var answer = await _h.CallToolAsync(token, ToolNames.QueryMetrics, Ask([MetricKeys.OrderCount], [MetricBreakdowns.Customer]));
        watch.Stop();
        await transaction.RollbackAsync();

        answer.IsError.ShouldBeFalse(answer.Text);
        answer.Json.GetProperty("status").GetString().ShouldBe(MetricAnswerStatus.Timeout);
        answer.Json.TryGetProperty("rows", out _).ShouldBeFalse();
        watch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1.9));
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    /* ---------- Logs ---------- */

    [Fact]
    public async Task The_log_holds_keys_and_counts_and_never_a_filter_value()
    {
        await FactsOfAsync(_h.TenantA);
        var token = await TokenAsync(_h.TenantA);
        var filterId = Guid.NewGuid();
        var before = _h.Host.Logs.Lines.Count;

        (await _h.ToolJsonAsync(token, ToolNames.QueryMetrics, Ask([MetricKeys.OrderCount], [MetricBreakdowns.Customer], new Dictionary<string, object?> { ["customerRelationId"] = filterId })))
            .GetProperty("status").GetString().ShouldBe(MetricAnswerStatus.Ok);

        var lines = _h.Host.Logs.Lines.Skip(before).ToList();
        lines.ShouldContain(l => l.Contains("Metric query by user", StringComparison.Ordinal) && l.Contains(MetricKeys.OrderCount, StringComparison.Ordinal), string.Join("\n", lines.Take(10)));
        string.Join("\n", lines).ShouldNotContain(filterId.ToString(), Case.Insensitive);
    }
}
