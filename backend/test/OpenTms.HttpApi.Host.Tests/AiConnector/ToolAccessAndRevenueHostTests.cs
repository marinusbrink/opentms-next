using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.EntityFrameworkCore;
using OpenTms.Financial.Permissions;
using OpenTms.HostIntegration;
using OpenTms.Integrations.AiConnector;
using OpenTms.Integrations.Permissions;
using OpenTms.MasterData.Permissions;
using OpenTms.Orders.Permissions;
using OpenTms.Orders.TransportOrders;
using OpenTms.Orders.TransportOrders.Published;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Reporting.Features;
using Shouldly;
using Volo.Abp.Authorization;
using Xunit;
using static OpenTms.AiConnector.AiConnectorHarness;

namespace OpenTms.AiConnector;

/// <summary>
/// Design 74 "Permissions and features" (risk class High), with real grants and stored feature values on the real host: each tool is refused without its permission and its feature and is absent from
/// <c>tools/list</c>; <c>Integrations.AiConnector.Use</c> alone returns no business data; the service beneath the catalog enforces the same.
/// </summary>
[Collection(HostIntegrationCollection.Name)]
public class ToolAccessHostTests
{
    private const string NotAllowedText = "Not allowed: this user may not use this tool, or the organisation has switched it off.";

    private readonly AiConnectorHarness _h;

    public ToolAccessHostTests(HostIntegrationFixture host)
    {
        _h = new AiConnectorHarness(host);
    }

    private Guid Tenant => _h.TenantC;

    private async Task<ConnectorWorld> WorldAsync() => await _h.WorldOfAsync(Tenant, "TNTCCC");

    private static Dictionary<string, object?> ArgsOf(string tool, ConnectorWorld w) => tool switch
    {
        ToolNames.GetOrder => new() { ["id"] = w.OrderId },
        ToolNames.GetTrip => new() { ["id"] = w.TripId },
        ToolNames.GetLegs => new() { ["orderId"] = w.OrderId },
        ToolNames.GetOrderRates => new() { ["orderId"] = w.OrderId },
        ToolNames.GetCustomerRevenue => new() { ["customerRelationId"] = w.CustomerId, ["dateFrom"] = "2026-01-01", ["dateTo"] = "2026-12-31" },
        _ => new(),
    };

    public static TheoryData<string, string> ToolPermissions => new()
    {
        { ToolNames.SearchOrders, OrdersPermissions.TransportOrders.Default },
        { ToolNames.GetOrder, OrdersPermissions.TransportOrders.Default },
        { ToolNames.SearchTrips, PlanningExecutionPermissions.TransportTrips.Default },
        { ToolNames.GetTrip, PlanningExecutionPermissions.TransportTrips.Default },
        { ToolNames.GetLegs, PlanningExecutionPermissions.Legs.Default },
        { ToolNames.SearchRelations, MasterDataPermissions.Relations.Default },
        { ToolNames.SearchVehicles, MasterDataPermissions.Vehicles.Default },
        { ToolNames.GetOrderRates, FinancialPermissions.RateLines.Default },
        { ToolNames.GetCustomerRevenue, FinancialPermissions.RateLines.Default },
        { ToolNames.GetCustomerRevenue, OrdersPermissions.TransportOrders.Default },
    };

    [Theory]
    [MemberData(nameof(ToolPermissions))]
    public async Task Without_its_permission_a_tool_is_refused_with_no_data_and_absent_from_the_list(string tool, string permission)
    {
        var world = await WorldAsync();
        await _h.ReadyAsync(Tenant);
        var user = await _h.CreateUserAsync(Tenant, AllToolPermissions.Where(p => p != permission).ToArray());
        var token = (await _h.ConnectAsync(user)).AccessToken!;

        (await _h.ListToolsAsync(token)).ShouldNotContain(tool, permission);

        var answer = await _h.CallToolAsync(token, tool, ArgsOf(tool, world));
        answer.IsError.ShouldBeTrue(answer.Text);
        answer.Text.ShouldBe(NotAllowedText);
        answer.Result.TryGetProperty("structuredContent", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task With_every_permission_all_nine_tools_are_listed_and_answer()
    {
        var world = await WorldAsync();
        await _h.ReadyAsync(Tenant);
        var user = await _h.CreateUserAsync(Tenant, AllToolPermissions);
        var token = (await _h.ConnectAsync(user)).AccessToken!;

        var tools = await _h.ListToolsAsync(token);
        foreach (var tool in AiConnectorToolCatalog.All.Select(t => t.Name).Where(n => n is not (ToolNames.ListMetrics or ToolNames.QueryMetrics)))
        {
            tools.ShouldContain(tool);
            (await _h.CallToolAsync(token, tool, ArgsOf(tool, world))).IsError.ShouldBeFalse(tool);
        }
    }

    public static TheoryData<string, string[]> ToolFeatures => new()
    {
        { ToolNames.SearchOrders, [AppFeatures.Transport] },
        { ToolNames.SearchOrders, [AppFeatures.TransportApp.Orders] },
        { ToolNames.GetOrder, [AppFeatures.Transport] },
        { ToolNames.GetOrder, [AppFeatures.TransportApp.Orders] },
        { ToolNames.SearchTrips, [AppFeatures.Transport, AppFeatures.Planboard] },
        { ToolNames.SearchTrips, [AppFeatures.TransportApp.Trips, AppFeatures.Planboard] },
        { ToolNames.GetTrip, [AppFeatures.Transport, AppFeatures.Planboard] },
        { ToolNames.GetLegs, [AppFeatures.Transport, AppFeatures.Planboard] },
        { ToolNames.SearchRelations, [AppFeatures.MasterData.Default] },
        { ToolNames.SearchRelations, [AppFeatures.MasterData.General.Relations.RelationsList] },
        { ToolNames.SearchVehicles, [AppFeatures.MasterData.Default] },
        { ToolNames.SearchVehicles, [AppFeatures.MasterData.General.Fleet.Vehicles] },
        { ToolNames.GetOrderRates, [AppFeatures.Transport] },
        { ToolNames.GetCustomerRevenue, [AppFeatures.Transport] },
        { ToolNames.GetCustomerRevenue, [AppFeatures.TransportApp.Orders] },
        { ToolNames.ListMetrics, [ReportingFeatures.Metrics] },
        { ToolNames.QueryMetrics, [ReportingFeatures.Metrics] },
    };

    [Theory]
    [MemberData(nameof(ToolFeatures))]
    public async Task Without_its_feature_a_tool_is_refused_and_absent_from_the_list(string tool, string[] features)
    {
        var world = await WorldAsync();
        await _h.ReadyAsync(Tenant);
        var user = await _h.CreateUserAsync(Tenant, AllToolPermissions);
        var token = (await _h.ConnectAsync(user)).AccessToken!;

        foreach (var feature in features)
        {
            await _h.SetFeatureAsync(Tenant, feature, "false");
        }

        try
        {
            (await _h.ListToolsAsync(token)).ShouldNotContain(tool, string.Join("+", features));
            var answer = await _h.CallToolAsync(token, tool, ArgsOf(tool, world));
            answer.IsError.ShouldBeTrue(answer.Text);
            answer.Text.ShouldBe(NotAllowedText);
        }
        finally
        {
            foreach (var feature in features)
            {
                await _h.SetFeatureAsync(Tenant, feature, "true");
            }
        }

        (await _h.ListToolsAsync(token)).ShouldContain(tool);
    }

    [Theory]
    [InlineData(ToolNames.SearchTrips)]
    [InlineData(ToolNames.GetTrip)]
    [InlineData(ToolNames.GetLegs)]
    public async Task A_trip_tool_answers_with_the_Planboard_alone_or_the_Transport_app_alone(string tool)
    {
        var world = await WorldAsync();
        await _h.ReadyAsync(Tenant);
        var user = await _h.CreateUserAsync(Tenant, AllToolPermissions);
        var token = (await _h.ConnectAsync(user)).AccessToken!;

        await _h.WithFeatureOffAsync(Tenant, AppFeatures.Transport, async () =>
        {
            (await _h.ListToolsAsync(token)).ShouldContain(tool);
            var answer = await _h.CallToolAsync(token, tool, ArgsOf(tool, world));
            answer.IsError.ShouldBeFalse(tool + " with the Planboard alone: " + answer.Text);
        });

        await _h.WithFeatureOffAsync(Tenant, AppFeatures.Planboard, async () =>
        {
            var answer = await _h.CallToolAsync(token, tool, ArgsOf(tool, world));
            answer.IsError.ShouldBeFalse(tool + " with Transport alone: " + answer.Text);
        });
    }

    [Fact]
    public async Task Use_alone_lists_no_design_74_tool_and_returns_no_business_data()
    {
        var world = await WorldAsync();
        await _h.ReadyAsync(Tenant);
        var user = await _h.CreateUserAsync(Tenant, IntegrationsPermissions.AiConnector.Use);
        var token = (await _h.ConnectAsync(user)).AccessToken!;

        var listed = await _h.ListToolsAsync(token);
        listed.ShouldNotContain(ToolNames.SearchOrders);
        listed.Except([ToolNames.ListMetrics, ToolNames.QueryMetrics]).ShouldBeEmpty("the metric tools need only the feature in tools/list, which design 75 says");

        foreach (var tool in AiConnectorToolCatalog.All.Select(t => t.Name).Except([ToolNames.ListMetrics, ToolNames.QueryMetrics]))
        {
            var answer = await _h.CallToolAsync(token, tool, ArgsOf(tool, world));
            answer.IsError.ShouldBeTrue(tool);
            answer.Text.ShouldBe(NotAllowedText, tool);
        }

        var metrics = await _h.ToolJsonAsync(token, ToolNames.ListMetrics);
        metrics.GetProperty("metrics").GetArrayLength().ShouldBe(0, "list_metrics for a user with no permission is an empty list");
    }

    [Fact]
    public async Task The_service_beneath_the_catalog_refuses_in_process_too_without_Use_and_without_the_tools_permission()
    {
        var world = await WorldAsync();
        await _h.ReadyAsync(Tenant);
        var noUse = await _h.CreateUserAsync(Tenant, AllToolPermissions.Where(p => p != IntegrationsPermissions.AiConnector.Use).ToArray());
        var noRates = await _h.CreateUserAsync(Tenant, AllToolPermissions.Where(p => p != FinancialPermissions.RateLines.Default).ToArray());

        await Should.ThrowAsync<AbpAuthorizationException>(() => _h.InScopeAsync(Tenant, services =>
            services.GetRequiredService<IAiConnectorToolService>().SearchOrdersAsync(new SearchOrdersInput()), PrincipalOf(noUse)));
        await Should.ThrowAsync<AbpAuthorizationException>(() => _h.InScopeAsync(Tenant, services =>
            services.GetRequiredService<IAiConnectorToolService>().GetOrderRatesAsync(new GetOrderRatesInput { OrderId = world.OrderId }), PrincipalOf(noRates)));
        await Should.ThrowAsync<AbpAuthorizationException>(() => _h.InScopeAsync(Tenant, services =>
            services.GetRequiredService<IAiConnectorToolService>().GetCustomerRevenueAsync(new GetCustomerRevenueInput { CustomerRelationId = world.CustomerId, DateFrom = new DateOnly(2026, 1, 1), DateTo = new DateOnly(2026, 12, 31) }), PrincipalOf(noRates)));
    }

    [Fact]
    public async Task An_unknown_tool_is_an_error_with_no_data()
    {
        await _h.ReadyAsync(Tenant);
        var user = await _h.CreateUserAsync(Tenant, AllToolPermissions);
        var token = (await _h.ConnectAsync(user)).AccessToken!;
        foreach (var name in new[] { "delete_order", "create_order", "update_trip", "search_drivers" })
        {
            var answer = await _h.CallToolAsync(token, name);
            answer.IsError.ShouldBeTrue(name);
            answer.Text.ShouldStartWith("Unknown tool");
        }
    }

    [Fact]
    public async Task Every_listed_tool_says_it_only_reads()
    {
        await _h.ReadyAsync(Tenant);
        var user = await _h.CreateUserAsync(Tenant, AllToolPermissions);
        var token = (await _h.ConnectAsync(user)).AccessToken!;
        using var response = await _h.PostMcpAsync(token, "tools/list");
        var result = await ReadRpcResultAsync(response);
        foreach (var tool in result.GetProperty("tools").EnumerateArray())
        {
            tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean().ShouldBeTrue(tool.GetProperty("name").GetString());
            tool.GetProperty("annotations").GetProperty("destructiveHint").GetBoolean().ShouldBeFalse();
            tool.GetProperty("inputSchema").GetProperty("type").GetString().ShouldBe("object");
        }
    }
}

/// <summary>
/// Design 74 "get_customer_revenue" (risk class High), on the real host with stored values in Orders' own table: the total equals the sum of the orders' stored revenue per currency, computed apart by SQL; Draft and
/// Cancelled stay out and are counted; an order without an amount is counted as such, not as zero; refused without either permission or feature; one query per call. The volume half is <c>MetricVolumeTests</c>.
/// </summary>
[Collection(HostIntegrationCollection.Name)]
public class CustomerRevenueHostTests
{
    private readonly AiConnectorHarness _h;

    public CustomerRevenueHostTests(HostIntegrationFixture host)
    {
        _h = new AiConnectorHarness(host);
    }

    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 3, 31);

    private Dictionary<string, object?> Args(Guid customer) => new() { ["customerRelationId"] = customer, ["dateFrom"] = From.ToString("yyyy-MM-dd"), ["dateTo"] = To.ToString("yyyy-MM-dd") };

    /// <summary>The seeded mix: two currencies, every status, an order without an amount, one out of the period, one of another customer, one soft-deleted.</summary>
    private async Task<Guid> SeedAsync(Guid tenant)
    {
        var customer = Guid.NewGuid();
        await _h.InScopeAsync(tenant, async services =>
        {
            Task<Guid> Order(DateOnly date, TransportOrderStatus status, string currency, decimal? revenue, Guid? who = null)
                => ConnectorWorldBuilder.SeedOrderAsync(services, tenant, who ?? customer, date, status, currency, revenue);

            await Order(From, TransportOrderStatus.Open, "EUR", 100.10m);
            await Order(From.AddDays(10), TransportOrderStatus.ReadyForInvoicing, "EUR", 200.20m);
            await Order(From.AddDays(20), TransportOrderStatus.Invoiced, "EUR", 300.30m);
            await Order(To, TransportOrderStatus.Archived, "EUR", 400.40m);
            await Order(From.AddDays(30), TransportOrderStatus.Open, "USD", 55.55m);
            await Order(From.AddDays(31), TransportOrderStatus.Open, "USD", 44.45m);
            await Order(From.AddDays(40), TransportOrderStatus.Open, "EUR", null);
            await Order(From.AddDays(41), TransportOrderStatus.Invoiced, "USD", null);
            await Order(From.AddDays(50), TransportOrderStatus.Draft, "EUR", 7_000m);
            await Order(From.AddDays(51), TransportOrderStatus.Draft, "EUR", null);
            await Order(From.AddDays(52), TransportOrderStatus.Cancelled, "EUR", 8_000m);
            await Order(From.AddDays(-1), TransportOrderStatus.Open, "EUR", 9_000m);
            await Order(To.AddDays(1), TransportOrderStatus.Open, "EUR", 9_000m);
            await Order(From, TransportOrderStatus.Open, "EUR", 9_000m, Guid.NewGuid());
            var gone = await Order(From, TransportOrderStatus.Open, "EUR", 9_000m);
            await services.GetRequiredService<Volo.Abp.Domain.Repositories.IRepository<TransportOrder, Guid>>().DeleteAsync(gone, autoSave: true);
        });
        return customer;
    }

    [Theory]
    [InlineData("own")]
    [InlineData("shared")]
    public async Task The_total_equals_the_sum_of_the_stored_revenue_per_currency_and_each_exclusion_is_counted(string tier)
    {
        var tenant = _h.Pair(tier).Mine;
        await _h.ReadyAsync(tenant);
        var customer = await SeedAsync(tenant);
        var user = await _h.CreateUserAsync(tenant, AllToolPermissions);
        var token = (await _h.ConnectAsync(user)).AccessToken!;

        var json = await _h.ToolJsonAsync(token, ToolNames.GetCustomerRevenue, Args(customer));

        var totals = json.GetProperty("totals").EnumerateArray().ToDictionary(t => t.GetProperty("currencyCode").GetString()!, t => (Amount: t.GetProperty("revenueAmount").GetDecimal(), Count: t.GetProperty("orderCount").GetInt32()));
        totals.Keys.ShouldBe(["EUR", "USD"], ignoreOrder: true);
        totals["EUR"].Amount.ShouldBe(1_001.00m);
        totals["EUR"].Count.ShouldBe(4, "orderCount counts the orders in that total");
        totals["USD"].Amount.ShouldBe(100.00m);
        totals["USD"].Count.ShouldBe(2);
        json.GetProperty("ordersWithoutAmount").GetInt32().ShouldBe(2, "counted as such, not as zero");
        json.GetProperty("excludedDraftCount").GetInt32().ShouldBe(2);
        json.GetProperty("excludedCancelledCount").GetInt32().ShouldBe(1);
        json.GetProperty("definition").GetString().ShouldBe(AiConnectorToolCatalog.RevenueDefinition);
        json.GetProperty("customerRelationId").GetGuid().ShouldBe(customer);

        /* Apart by SQL, on the table itself. */
        var condition = $"\"TenantId\" = '{tenant}' AND \"CustomerRelationId\" = '{customer}' AND NOT \"IsDeleted\" AND \"OrderDate\" BETWEEN '{From:yyyy-MM-dd}' AND '{To:yyyy-MM-dd}' AND \"Status\" IN (2,3,4,5) AND \"RevenueAmount\" IS NOT NULL";
        (await _h.CountAsync(tenant, $"SELECT round(sum(\"RevenueAmount\") * 100) FROM orders.\"TransportOrders\" WHERE {condition} AND \"CurrencyCode\" = 'EUR'")).ShouldBe(100_100);
        (await _h.CountAsync(tenant, $"SELECT round(sum(\"RevenueAmount\") * 100) FROM orders.\"TransportOrders\" WHERE {condition} AND \"CurrencyCode\" = 'USD'")).ShouldBe(10_000);
    }

    [Fact]
    public async Task A_customer_without_orders_has_no_totals_rather_than_zero()
    {
        var tenant = _h.TenantA;
        await _h.ReadyAsync(tenant);
        var user = await _h.CreateUserAsync(tenant, AllToolPermissions);
        var token = (await _h.ConnectAsync(user)).AccessToken!;
        var json = await _h.ToolJsonAsync(token, ToolNames.GetCustomerRevenue, Args(Guid.NewGuid()));
        json.GetProperty("totals").GetArrayLength().ShouldBe(0);
        json.GetProperty("ordersWithoutAmount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Orders_that_only_have_no_amount_make_no_total_of_zero()
    {
        var tenant = _h.TenantA;
        await _h.ReadyAsync(tenant);
        var customer = Guid.NewGuid();
        await _h.InScopeAsync(tenant, services => ConnectorWorldBuilder.SeedOrderAsync(services, tenant, customer, From, TransportOrderStatus.Open, "EUR", null));
        var user = await _h.CreateUserAsync(tenant, AllToolPermissions);
        var token = (await _h.ConnectAsync(user)).AccessToken!;

        var json = await _h.ToolJsonAsync(token, ToolNames.GetCustomerRevenue, Args(customer));

        json.GetProperty("totals").GetArrayLength().ShouldBe(0, "an order still being calculated is counted apart, never as zero");
        json.GetProperty("ordersWithoutAmount").GetInt32().ShouldBe(1);
    }

    [Theory]
    [InlineData(FinancialPermissions.RateLines.Default)]
    [InlineData(OrdersPermissions.TransportOrders.Default)]
    public async Task It_is_refused_without_either_permission_and_says_nothing(string permission)
    {
        var tenant = _h.TenantA;
        await _h.ReadyAsync(tenant);
        var customer = await SeedAsync(tenant);
        var user = await _h.CreateUserAsync(tenant, AllToolPermissions.Where(p => p != permission).ToArray());
        var token = (await _h.ConnectAsync(user)).AccessToken!;

        var answer = await _h.CallToolAsync(token, ToolNames.GetCustomerRevenue, Args(customer));

        answer.IsError.ShouldBeTrue(answer.Text);
        answer.Text.ShouldNotContain("1001");
        answer.Text.ShouldNotContain("EUR");
    }

    [Theory]
    [InlineData(AppFeatures.Transport)]
    [InlineData(AppFeatures.TransportApp.Orders)]
    public async Task It_is_refused_without_either_feature(string feature)
    {
        var tenant = _h.TenantA;
        await _h.ReadyAsync(tenant);
        var customer = await SeedAsync(tenant);
        var user = await _h.CreateUserAsync(tenant, AllToolPermissions);
        var token = (await _h.ConnectAsync(user)).AccessToken!;

        await _h.WithFeatureOffAsync(tenant, feature, async () =>
        {
            var answer = await _h.CallToolAsync(token, ToolNames.GetCustomerRevenue, Args(customer));
            answer.IsError.ShouldBeTrue(answer.Text);
            answer.Text.ShouldNotContain("EUR");
        });
    }

    [Theory]
    [InlineData("2026-03-31", "2026-01-01", "the reversed period")]
    [InlineData(null, "2026-01-01", "no start")]
    [InlineData("2026-01-01", null, "no end")]
    public async Task An_invalid_period_is_refused(string? from, string? to, string what)
    {
        var tenant = _h.TenantA;
        await _h.ReadyAsync(tenant);
        var user = await _h.CreateUserAsync(tenant, AllToolPermissions);
        var token = (await _h.ConnectAsync(user)).AccessToken!;
        var args = new Dictionary<string, object?> { ["customerRelationId"] = Guid.NewGuid() };
        if (from != null)
        {
            args["dateFrom"] = from;
        }

        if (to != null)
        {
            args["dateTo"] = to;
        }

        var answer = await _h.CallToolAsync(token, ToolNames.GetCustomerRevenue, args);
        answer.IsError.ShouldBeTrue(what + ": " + answer.Text);
        answer.Text.ShouldStartWith("Invalid arguments", Case.Sensitive, what);
    }

    [Fact]
    public async Task One_call_makes_one_query_on_the_orders_table()
    {
        var tenant = _h.TenantC;
        await _h.ReadyAsync(tenant);
        var customer = await SeedAsync(tenant);

        var commands = await _h.InScopeAsync(tenant, async services =>
        {
            var reader = services.GetRequiredService<ICustomerRevenueReader>();
            /* The first call warms what is warmed once per process. */
            await reader.GetAsync(customer, From, To);
            using var capture = EfCommandCapture.Begin();
            await Task.Delay(EfCommandCapture.LoggingCacheTime);
            await reader.GetAsync(customer, From, To);
            return capture.On("TransportOrders");
        }, transactional: false);

        commands.Count.ShouldBe(1, string.Join("\n---\n", commands));
        commands.Single().ShouldContain("GROUP BY", Case.Sensitive, "the database adds up");
    }
}
