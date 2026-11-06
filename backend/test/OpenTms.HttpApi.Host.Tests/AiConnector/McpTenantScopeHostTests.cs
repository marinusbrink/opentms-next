using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.HostIntegration;
using OpenTms.Integrations.AiConnector;
using OpenTms.Integrations.Permissions;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Security.Claims;
using Xunit;
using static OpenTms.AiConnector.AiConnectorHarness;

namespace OpenTms.AiConnector;

/// <summary>
/// Design 74 "Tenant scope of tokens and tools" (risk class Critical), on the real host and PostgreSQL, for a pair of tenants with their own databases and a pair of shared-tier tenants in
/// the host database, where only the data filter separates them: a token of a tenant returns only that tenant's data for each of the nine tools; asking for the other tenant in the
/// <c>__tenant</c> header, query or cookie changes nothing, nor on <c>/connect/token</c> during a refresh; an id of the other tenant's record gives not-found; a connection row of one tenant is not
/// visible or revocable from the other.
/// </summary>
[Collection(HostIntegrationCollection.Name)]
public class McpTenantScopeHostTests
{
    private readonly AiConnectorHarness _h;

    public McpTenantScopeHostTests(HostIntegrationFixture host)
    {
        _h = new AiConnectorHarness(host);
    }

    private async Task<(ConnectorWorld Mine, ConnectorWorld Other, string OtherName, TokenAnswer Token, TestUser User)> ArrangeAsync(string tier)
    {
        var (mine, other, otherName) = _h.Pair(tier);
        var myWorld = await _h.WorldOfAsync(mine, Tag(mine));
        var otherWorld = await _h.WorldOfAsync(other, Tag(other));
        await _h.ReadyAsync(mine);
        await _h.ReadyAsync(other);
        var user = await _h.CreateUserAsync(mine, AllToolPermissions);
        return (myWorld, otherWorld, otherName, await _h.ConnectAsync(user), user);
    }

    private string Tag(Guid tenant)
        => tenant == _h.TenantA ? "TNTAAA" : tenant == _h.TenantB ? "TNTBBB" : tenant == _h.TenantC ? "TNTCCC" : "TNTDDD";

    private static IEnumerable<(string Tool, object Args, string Expected)> AllTools(ConnectorWorld w) =>
    [
        (ToolNames.SearchOrders, new Dictionary<string, object?>(), w.CustomerName),
        (ToolNames.GetOrder, new Dictionary<string, object?> { ["id"] = w.OrderId }, w.CustomerName),
        (ToolNames.SearchTrips, new Dictionary<string, object?>(), w.CarrierName),
        (ToolNames.GetTrip, new Dictionary<string, object?> { ["id"] = w.TripId }, w.CarrierName),
        (ToolNames.GetLegs, new Dictionary<string, object?> { ["orderId"] = w.OrderId }, w.LegOriginName),
        (ToolNames.SearchRelations, new Dictionary<string, object?> { ["take"] = 50 }, w.CustomerName),
        (ToolNames.SearchVehicles, new Dictionary<string, object?>(), w.VehicleName),
        (ToolNames.GetOrderRates, new Dictionary<string, object?> { ["orderId"] = w.OrderId }, (w.UnitPrice * 2).ToString(System.Globalization.CultureInfo.InvariantCulture)),
        (ToolNames.GetCustomerRevenue, new Dictionary<string, object?> { ["customerRelationId"] = w.CustomerId, ["dateFrom"] = "2026-01-01", ["dateTo"] = "2026-12-31" }, "987654.32"),
    ];

    private static void ShouldHoldOnly(string text, ConnectorWorld mine, ConnectorWorld other, string expected, string tool)
    {
        text.ShouldContain(expected, Case.Sensitive, tool + " answers the tenant's own data");
        text.ShouldNotContain(other.Tag, Case.Sensitive, tool + " holds nothing of the other tenant");
        foreach (var id in other.Ids)
        {
            text.ShouldNotContain(id, Case.Insensitive, tool + " holds no id of the other tenant");
        }
    }

    [Theory]
    [InlineData("own")]
    [InlineData("shared")]
    public async Task A_token_returns_only_its_tenants_data_for_each_of_the_nine_tools(string tier)
    {
        var (mine, other, _, token, _) = await ArrangeAsync(tier);
        foreach (var (tool, args, expected) in AllTools(mine))
        {
            var answer = await _h.CallToolAsync(token.AccessToken!, tool, args);
            answer.IsError.ShouldBeFalse(tool + ": " + answer.Text);
            ShouldHoldOnly(answer.Text, mine, other, expected, tool);
        }
    }

    [Theory]
    [InlineData("own", "header")]
    [InlineData("own", "query")]
    [InlineData("own", "cookie")]
    [InlineData("shared", "header")]
    [InlineData("shared", "query")]
    [InlineData("shared", "cookie")]
    public async Task Asking_for_the_other_tenant_in_the_request_changes_nothing(string tier, string where)
    {
        var (mine, other, otherName, token, _) = await ArrangeAsync(tier);
        Action<HttpRequestMessage> ask = where switch
        {
            "header" => request => request.Headers.Add("__tenant", other.TenantId.ToString()),
            "query" => request => request.RequestUri = new Uri(request.RequestUri!.OriginalString + "?__tenant=" + otherName, UriKind.Relative),
            _ => request => request.Headers.Add("Cookie", "__tenant=" + other.TenantId),
        };

        foreach (var (tool, args, expected) in AllTools(mine))
        {
            var answer = await _h.CallToolAsync(token.AccessToken!, tool, args, ask);
            answer.IsError.ShouldBeFalse(tool + ": " + answer.Text);
            ShouldHoldOnly(answer.Text, mine, other, expected, tool);
        }

        /* Asking for the other tenant's record the same way gives it no better. */
        var theirs = await _h.CallToolAsync(token.AccessToken!, ToolNames.GetOrder, new Dictionary<string, object?> { ["id"] = other.OrderId }, ask);
        theirs.IsError.ShouldBeTrue(theirs.Text);
        theirs.Text.ShouldBe("Not found.");
    }

    [Theory]
    [InlineData("own", "query")]
    [InlineData("own", "header")]
    [InlineData("shared", "query")]
    [InlineData("shared", "header")]
    public async Task The_other_tenant_on_connect_token_during_a_refresh_keeps_the_token_in_its_tenant(string tier, string where)
    {
        var (mine, other, otherName, token, _) = await ArrangeAsync(tier);

        var refreshed = where == "query"
            ? await _h.RefreshAsync(ClaudeClient, token.RefreshToken!, tenantInQuery: otherName)
            : await _h.RefreshAsync(ClaudeClient, token.RefreshToken!, tenantInHeader: other.TenantId.ToString());

        refreshed.Status.ShouldBe(HttpStatusCode.OK, refreshed.Body);
        ClaimsOf(refreshed.AccessToken!).Where(c => c.Type == AbpClaimTypes.TenantId).Select(c => c.Value).ShouldBe([mine.TenantId.ToString()], "the tenant the connection was made in");
        foreach (var (tool, args, expected) in AllTools(mine))
        {
            var answer = await _h.CallToolAsync(refreshed.AccessToken!, tool, args);
            answer.IsError.ShouldBeFalse(tool + ": " + answer.Text);
            ShouldHoldOnly(answer.Text, mine, other, expected, tool);
        }
    }

    [Theory]
    [InlineData("own")]
    [InlineData("shared")]
    public async Task An_id_of_the_other_tenants_record_gives_not_found(string tier)
    {
        var (_, other, _, token, _) = await ArrangeAsync(tier);

        foreach (var (tool, args) in new (string, object)[]
                 {
                     (ToolNames.GetOrder, new Dictionary<string, object?> { ["id"] = other.OrderId }),
                     (ToolNames.GetTrip, new Dictionary<string, object?> { ["id"] = other.TripId }),
                 })
        {
            var answer = await _h.CallToolAsync(token.AccessToken!, tool, args);
            answer.IsError.ShouldBeTrue(tool + ": " + answer.Text);
            answer.Text.ShouldBe("Not found.", tool);
        }

        /* A list or a sum over another tenant's id holds nothing. */
        var legs = await _h.CallToolAsync(token.AccessToken!, ToolNames.GetLegs, new Dictionary<string, object?> { ["orderId"] = other.OrderId });
        if (!legs.IsError)
        {
            legs.Json.GetProperty("rows").GetArrayLength().ShouldBe(0);
        }

        var legsOfTrip = await _h.CallToolAsync(token.AccessToken!, ToolNames.GetLegs, new Dictionary<string, object?> { ["tripId"] = other.TripId });
        if (!legsOfTrip.IsError)
        {
            legsOfTrip.Json.GetProperty("rows").GetArrayLength().ShouldBe(0);
        }

        var rates = await _h.CallToolAsync(token.AccessToken!, ToolNames.GetOrderRates, new Dictionary<string, object?> { ["orderId"] = other.OrderId });
        rates.Text.ShouldNotContain(other.RateDescription);
        if (!rates.IsError)
        {
            rates.Json.GetProperty("lines").GetArrayLength().ShouldBe(0, "no line of the other tenant");
        }

        var revenue = await _h.ToolJsonAsync(token.AccessToken!, ToolNames.GetCustomerRevenue, new Dictionary<string, object?> { ["customerRelationId"] = other.CustomerId, ["dateFrom"] = "2026-01-01", ["dateTo"] = "2026-12-31" });
        revenue.GetProperty("totals").GetArrayLength().ShouldBe(0, "the other tenant's customer has no revenue here");

        var orders = await _h.ToolJsonAsync(token.AccessToken!, ToolNames.SearchOrders, new Dictionary<string, object?> { ["customerRelationId"] = other.CustomerId });
        orders.GetProperty("total").GetInt64().ShouldBe(0);
        var trips = await _h.ToolJsonAsync(token.AccessToken!, ToolNames.SearchTrips, new Dictionary<string, object?> { ["carrierRelationId"] = other.CarrierId });
        trips.GetProperty("total").GetInt64().ShouldBe(0);
    }

    [Theory]
    [InlineData("own")]
    [InlineData("shared")]
    public async Task A_connection_of_one_tenant_is_not_visible_or_revocable_from_the_other_and_still_works(string tier)
    {
        var (mine, other, _, token, user) = await ArrangeAsync(tier);
        var manager = await _h.CreateUserAsync(other.TenantId, IntegrationsPermissions.AiConnector.Use, IntegrationsPermissions.AiConnector.Manage);
        var managerToken = await _h.BrowserTokenAsync(manager);
        var authorization = AuthorizationIdOf(token.AccessToken!);
        var rowId = await _h.InScopeAsync(mine.TenantId, async services =>
            (await services.GetRequiredService<IRepository<AiConnection, Guid>>().FirstAsync(c => c.AuthorizationId == authorization)).Id);

        using var list = await _h.RestAsync(managerToken, HttpMethod.Get, "/api/integrations/ai-connector/connections?StartRow=0&EndRow=100");
        var listBody = await list.Content.ReadAsStringAsync();
        list.StatusCode.ShouldBe(HttpStatusCode.OK, listBody);
        listBody.ShouldNotContain(rowId.ToString(), Case.Insensitive);
        listBody.ShouldNotContain(user.Id.ToString(), Case.Insensitive);

        using var revoke = await _h.RestAsync(managerToken, HttpMethod.Post, $"/api/integrations/ai-connector/connections/{rowId}/revoke");
        revoke.StatusCode.ShouldBe(HttpStatusCode.NotFound, await revoke.Content.ReadAsStringAsync());

        /* A withdrawal in the other tenant touches only that tenant. */
        using (var withdraw = await _h.RestAsync(managerToken, HttpMethod.Post, "/api/integrations/ai-connector/withdraw"))
        {
            withdraw.StatusCode.ShouldBe(HttpStatusCode.OK, await withdraw.Content.ReadAsStringAsync());
        }

        await _h.SetAcceptedAsync(other.TenantId);
        (await _h.ListToolsAsync(token.AccessToken!)).ShouldContain(ToolNames.SearchOrders, "the connection still works");
    }

    [Theory]
    [InlineData("own")]
    [InlineData("shared")]
    public async Task A_token_minted_for_one_tenant_and_a_connection_of_another_is_refused(string tier)
    {
        var (mine, other, _, token, user) = await ArrangeAsync(tier);
        /* The same authorization id and user id, but the token says another tenant: no such connection there. */
        var forged = _h.MintToken(user.Id, other.TenantId, authorizationId: AuthorizationIdOf(token.AccessToken!));
        using var response = await _h.PostMcpAsync(forged, "tools/list");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        mine.TenantId.ShouldNotBe(other.TenantId);
    }
}

/// <summary>The tools' names, short.</summary>
public static class ToolNames
{
    public const string SearchOrders = AiConnectorToolCatalog.SearchOrders;
    public const string GetOrder = AiConnectorToolCatalog.GetOrder;
    public const string SearchTrips = AiConnectorToolCatalog.SearchTrips;
    public const string GetTrip = AiConnectorToolCatalog.GetTrip;
    public const string GetLegs = AiConnectorToolCatalog.GetLegs;
    public const string SearchRelations = AiConnectorToolCatalog.SearchRelations;
    public const string SearchVehicles = AiConnectorToolCatalog.SearchVehicles;
    public const string GetOrderRates = AiConnectorToolCatalog.GetOrderRates;
    public const string GetCustomerRevenue = AiConnectorToolCatalog.GetCustomerRevenue;
    public const string ListMetrics = AiConnectorToolCatalog.ListMetrics;
    public const string QueryMetrics = AiConnectorToolCatalog.QueryMetrics;
}
