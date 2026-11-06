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
using OpenTms.MasterData.Relations;
using OpenTms.Orders.TransportOrders;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;
using static OpenTms.AiConnector.AiConnectorHarness;

namespace OpenTms.AiConnector;

/// <summary>
/// Design 74 "Limits" (risk class Medium), on the real host: <c>take</c> over 50 is capped; <c>total</c> respects the filters; the rate limit answers 429 per connection; logs and audit records carry ids only, with no
/// tool arguments. Input refused with what is wrong and never its value.
/// </summary>
[Collection(HostIntegrationCollection.Name)]
public class LimitsHostTests
{
    private static bool _relationsSeeded;

    private readonly AiConnectorHarness _h;

    public LimitsHostTests(HostIntegrationFixture host)
    {
        _h = new AiConnectorHarness(host);
    }

    private Guid Tenant => _h.TenantD;

    private async Task<string> TokenAsync()
    {
        await _h.ReadyAsync(Tenant);
        var user = await _h.CreateUserAsync(Tenant, AllToolPermissions);
        return (await _h.ConnectAsync(user)).AccessToken!;
    }

    /// <summary>More than a page of relations, straight in the table: the service is under test, not their creation.</summary>
    private async Task EnsureManyRelationsAsync()
    {
        if (_relationsSeeded)
        {
            return;
        }

        await _h.WorldOfAsync(Tenant, "TNTDDD");
        await _h.InScopeAsync(Tenant, async services =>
        {
            var repository = services.GetRequiredService<IRepository<Relation, Guid>>();
            for (var i = 0; i < 60; i++)
            {
                await repository.InsertAsync(new Relation(Guid.NewGuid(), Tenant, "LIM" + i.ToString("00"), "Limit Relation " + i.ToString("00"), RelationRoles.Customer));
            }
        });
        _relationsSeeded = true;
    }

    [Theory]
    [InlineData(51, 50)]
    [InlineData(5_000, 50)]
    [InlineData(50, 50)]
    [InlineData(10, 10)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    public async Task Take_is_capped_at_50_rows_and_held_to_at_least_one(int take, int rows)
    {
        await EnsureManyRelationsAsync();
        var json = await _h.ToolJsonAsync(await TokenAsync(), ToolNames.SearchRelations, new Dictionary<string, object?> { ["take"] = take });
        json.GetProperty("rows").GetArrayLength().ShouldBe(rows);
        json.GetProperty("total").GetInt64().ShouldBeGreaterThanOrEqualTo(60);
    }

    [Fact]
    public async Task Take_defaults_to_25_and_skip_moves_the_window()
    {
        await EnsureManyRelationsAsync();
        var token = await TokenAsync();
        var first = await _h.ToolJsonAsync(token, ToolNames.SearchRelations, new Dictionary<string, object?> { ["text"] = "Limit Relation" });
        first.GetProperty("rows").GetArrayLength().ShouldBe(25);
        first.GetProperty("total").GetInt64().ShouldBe(60);

        var second = await _h.ToolJsonAsync(token, ToolNames.SearchRelations, new Dictionary<string, object?> { ["text"] = "Limit Relation", ["skip"] = 50, ["take"] = 50 });
        second.GetProperty("rows").GetArrayLength().ShouldBe(10);
        second.GetProperty("total").GetInt64().ShouldBe(60);
        var a = first.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("id").GetGuid());
        var b = second.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("id").GetGuid());
        a.Intersect(b).ShouldBeEmpty();
    }

    [Fact]
    public async Task Total_respects_the_filters_on_every_list_tool()
    {
        var world = await _h.WorldOfAsync(Tenant, "TNTDDD");
        var token = await TokenAsync();

        (await _h.ToolJsonAsync(token, ToolNames.SearchOrders, new Dictionary<string, object?> { ["customerRelationId"] = world.CustomerId })).GetProperty("total").GetInt64().ShouldBe(1);
        (await _h.ToolJsonAsync(token, ToolNames.SearchOrders, new Dictionary<string, object?> { ["customerRelationId"] = Guid.NewGuid() })).GetProperty("total").GetInt64().ShouldBe(0);
        (await _h.ToolJsonAsync(token, ToolNames.SearchOrders, new Dictionary<string, object?> { ["customerRelationId"] = world.CustomerId, ["status"] = "Cancelled" })).GetProperty("total").GetInt64().ShouldBe(0);
        (await _h.ToolJsonAsync(token, ToolNames.SearchOrders, new Dictionary<string, object?> { ["customerRelationId"] = world.CustomerId, ["status"] = "Open" })).GetProperty("total").GetInt64().ShouldBe(1);
        (await _h.ToolJsonAsync(token, ToolNames.SearchOrders, new Dictionary<string, object?> { ["customerRelationId"] = world.CustomerId, ["dateFrom"] = "2020-01-01", ["dateTo"] = "2020-12-31" })).GetProperty("total").GetInt64().ShouldBe(0);

        /* The trip list's TotalCount ignores filters; total is its filtered count (design 74). */
        (await _h.ToolJsonAsync(token, ToolNames.SearchTrips, new Dictionary<string, object?> { ["carrierRelationId"] = world.CarrierId })).GetProperty("total").GetInt64().ShouldBe(1);
        (await _h.ToolJsonAsync(token, ToolNames.SearchTrips, new Dictionary<string, object?> { ["carrierRelationId"] = Guid.NewGuid() })).GetProperty("total").GetInt64().ShouldBe(0);
        (await _h.ToolJsonAsync(token, ToolNames.SearchTrips, new Dictionary<string, object?> { ["status"] = "Cancelled" })).GetProperty("total").GetInt64().ShouldBe(0);
        (await _h.ToolJsonAsync(token, ToolNames.SearchTrips, new Dictionary<string, object?> { ["dateFrom"] = "2020-01-01", ["dateTo"] = "2020-12-31" })).GetProperty("total").GetInt64().ShouldBe(0);

        (await _h.ToolJsonAsync(token, ToolNames.SearchRelations, new Dictionary<string, object?> { ["text"] = "NoSuchRelationAnywhere" })).GetProperty("total").GetInt64().ShouldBe(0);
        (await _h.ToolJsonAsync(token, ToolNames.SearchVehicles, new Dictionary<string, object?> { ["vehicleTypeId"] = world.VehicleTypeId })).GetProperty("total").GetInt64().ShouldBe(1);
        (await _h.ToolJsonAsync(token, ToolNames.SearchVehicles, new Dictionary<string, object?> { ["vehicleTypeId"] = Guid.NewGuid() })).GetProperty("total").GetInt64().ShouldBe(0);
        (await _h.ToolJsonAsync(token, ToolNames.GetLegs, new Dictionary<string, object?> { ["orderId"] = world.OrderId })).GetProperty("total").GetInt64().ShouldBe(1);
        (await _h.ToolJsonAsync(token, ToolNames.GetLegs, new Dictionary<string, object?> { ["tripId"] = world.TripId })).GetProperty("total").GetInt64().ShouldBe(1);
    }

    /* ---------- Input ---------- */

    [Fact]
    public async Task Input_that_breaks_a_rule_gets_invalid_arguments_and_never_its_value()
    {
        var token = await TokenAsync();
        const string secretValue = "SECRET-VALUE-12345";

        var longText = await _h.CallToolAsync(token, ToolNames.SearchOrders, new Dictionary<string, object?> { ["text"] = secretValue + new string('x', 201) });
        longText.IsError.ShouldBeTrue("text is at most 200 characters: " + longText.Text);
        longText.Text.ShouldStartWith("Invalid arguments");
        longText.Text.ShouldNotContain(secretValue);

        var notAGuid = await _h.CallToolAsync(token, ToolNames.GetOrder, new Dictionary<string, object?> { ["id"] = secretValue });
        notAGuid.IsError.ShouldBeTrue(notAGuid.Text);
        notAGuid.Text.ShouldStartWith("Invalid arguments");
        notAGuid.Text.ShouldNotContain(secretValue);

        var missing = await _h.CallToolAsync(token, ToolNames.GetOrder, new Dictionary<string, object?>());
        missing.IsError.ShouldBeTrue(missing.Text);
        missing.Text.ShouldStartWith("Invalid arguments");

        var notANumber = await _h.CallToolAsync(token, ToolNames.SearchOrders, new Dictionary<string, object?> { ["take"] = "12" });
        notANumber.IsError.ShouldBeTrue("numbers are strict: " + notANumber.Text);

        var both = await _h.CallToolAsync(token, ToolNames.GetLegs, new Dictionary<string, object?> { ["orderId"] = Guid.NewGuid(), ["tripId"] = Guid.NewGuid() });
        both.IsError.ShouldBeTrue("orderId or tripId, not both: " + both.Text);
        var neither = await _h.CallToolAsync(token, ToolNames.GetLegs, new Dictionary<string, object?>());
        neither.IsError.ShouldBeTrue("one of them is required: " + neither.Text);

        var skip = await _h.CallToolAsync(token, ToolNames.SearchOrders, new Dictionary<string, object?> { ["skip"] = -1 });
        skip.IsError.ShouldBeTrue("skip has a range: " + skip.Text);
    }

    /* ---------- The rate limit ---------- */

    [Fact]
    public async Task The_61st_request_of_a_connection_in_a_minute_answers_429_and_another_connection_goes_on()
    {
        var limited = await TokenAsync();
        var other = await TokenAsync();
        for (var i = 0; i < AiConnectorRateLimits.ToolCallsPerMinute; i++)
        {
            using var allowed = await _h.PostMcpAsync(limited, "tools/list");
            allowed.StatusCode.ShouldBe(HttpStatusCode.OK, "request " + i);
        }

        using var refused = await _h.PostMcpAsync(limited, "tools/list");
        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter.ShouldNotBeNull();
        refused.Headers.RetryAfter!.Delta!.Value.TotalSeconds.ShouldBe(60);

        using var fine = await _h.PostMcpAsync(other, "tools/list");
        fine.StatusCode.ShouldBe(HttpStatusCode.OK, "the limit is per connection");
    }

    /* ---------- Logs and audit ---------- */

    [Fact]
    public async Task A_tool_call_logs_ids_and_counts_and_never_its_arguments_or_results()
    {
        var world = await _h.WorldOfAsync(Tenant, "TNTDDD");
        var token = await TokenAsync();
        var searchText = "PERSONAL-NAME-" + Guid.NewGuid().ToString("N")[..10];
        var before = _h.Host.Logs.Lines.Count;

        await _h.ToolJsonAsync(token, ToolNames.SearchOrders, new Dictionary<string, object?> { ["text"] = searchText, ["customerRelationId"] = world.CustomerId });
        await _h.ToolJsonAsync(token, ToolNames.GetOrder, new Dictionary<string, object?> { ["id"] = world.OrderId });

        var lines = _h.Host.Logs.Lines.Skip(before).ToList();
        var toolLines = lines.Where(l => l.Contains("AI connector tool", StringComparison.Ordinal)).ToList();
        toolLines.Count.ShouldBeGreaterThanOrEqualTo(2, string.Join("\n", lines.Take(20)));
        toolLines.ShouldContain(l => l.Contains(ToolNames.SearchOrders, StringComparison.Ordinal));
        toolLines.ShouldAllBe(l => l.Contains(Tenant.ToString(), StringComparison.OrdinalIgnoreCase));

        string.Join("\n", lines).ShouldNotContain(searchText, Case.Insensitive, "a search text can hold a name (constitution rule 5)");
        string.Join("\n", lines).ShouldNotContain(world.CustomerName, Case.Insensitive, "no result in the log");
        string.Join("\n", toolLines).ShouldNotContain(world.CustomerId.ToString(), Case.Insensitive, "no argument in the tool's log line");
    }

    [Fact]
    public async Task A_refused_request_is_logged_by_its_check_with_ids()
    {
        await _h.ReadyAsync(Tenant);
        var user = await _h.CreateUserAsync(Tenant, AllToolPermissions);
        var token = await _h.ConnectAsync(user);
        await _h.SetUserActiveAsync(user, false);
        var before = _h.Host.Logs.Lines.Count;

        using var response = await _h.PostMcpAsync(token.AccessToken, "tools/list");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        _h.Host.Logs.Lines.Skip(before).ShouldContain(l => l.Contains("AI connector request refused", StringComparison.Ordinal) && l.Contains(nameof(AiConnectorRefusal.UserInactive), StringComparison.Ordinal) && l.Contains(user.Id.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_audit_record_of_a_tool_call_keeps_who_called_what_and_none_of_its_arguments()
    {
        var token = await TokenAsync();
        var searchText = "AUDITED-NAME-" + Guid.NewGuid().ToString("N")[..10];
        var args = new Dictionary<string, object?> { ["text"] = searchText };

        var since = DateTime.Now.AddSeconds(-1);
        await _h.ToolJsonAsync(token, ToolNames.SearchOrders, args);

        /* The record is written when the request ends; the test client has read its answer by then or very soon after. */
        long records = 0;
        for (var i = 0; i < 50 && records == 0; i++)
        {
            records = await _h.CountAsync(Tenant, """
                SELECT count(*) FROM "AbpAuditLogs" WHERE "Url" LIKE '%/mcp%' AND "TenantId" = @tenant AND "ExecutionTime" >= @since
                """, ("tenant", Tenant), ("since", DateTime.SpecifyKind(since, DateTimeKind.Unspecified)));
            if (records == 0)
            {
                await Task.Delay(100);
            }
        }

        records.ShouldBeGreaterThan(0, "the request leaves an audit record");

        /* Nowhere in it, in the host database or the tenant's. */
        foreach (var column in new[] { "\"Comments\"", "\"Exceptions\"", "\"ExtraProperties\"", "\"Url\"" })
        {
            (await _h.CountAsync(Tenant, $"SELECT count(*) FROM \"AbpAuditLogs\" WHERE {column} LIKE @text", ("text", "%" + searchText + "%"))).ShouldBe(0, column);
        }

        (await _h.CountAsync(Tenant, "SELECT count(*) FROM \"AbpAuditLogActions\" WHERE \"Parameters\" LIKE @text", ("text", "%" + searchText + "%"))).ShouldBe(0, "the arguments are emptied");
        (await _h.CountAsync(Tenant, "SELECT count(*) FROM \"AbpAuditLogActions\" a JOIN \"AbpAuditLogs\" l ON l.\"Id\" = a.\"AuditLogId\" WHERE l.\"Url\" LIKE '%/mcp%' AND l.\"TenantId\" = @tenant AND a.\"Parameters\" <> '{}'", ("tenant", Tenant))).ShouldBe(0, "a /mcp action stores no parameters");
    }
}
