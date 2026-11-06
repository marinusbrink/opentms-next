using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenTms.EntityFrameworkCore;
using OpenTms.HostIntegration;
using OpenTms.Integrations.AiConnector;
using OpenTms.Integrations.Permissions;
using OpenTms.Integrations.Settings;
using OpenTms.Platform.Features;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Settings;
using Volo.Abp.Timing;
using Xunit;
using static OpenTms.AiConnector.AiConnectorHarness;

namespace OpenTms.AiConnector;

/// <summary>
/// Design 74 "Administration endpoints" (risk class Medium), over the real HTTP routes with real browser tokens: the status, accepting and withdrawing, own versus all connections, an idempotent revoke, and a negative
/// tenant test per endpoint. And art. 17: deleting a user revokes and removes the user's connections and clears the acceptance's user id, leaving the acceptance standing.
/// </summary>
[Collection(HostIntegrationCollection.Name)]
public class AdministrationHostTests
{
    private const string Root = "/api/integrations/ai-connector";

    private readonly AiConnectorHarness _h;

    public AdministrationHostTests(HostIntegrationFixture host)
    {
        _h = new AiConnectorHarness(host);
    }

    private Guid Tenant => _h.TenantA;

    private async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(expected, body);
        return JsonDocument.Parse(body).RootElement;
    }

    private async Task<string> ManagerTokenAsync(Guid tenant) => await _h.BrowserTokenAsync(await _h.CreateUserAsync(tenant, IntegrationsPermissions.AiConnector.Manage));

    private async Task<(TestUser User, TokenAnswer Token, Guid RowId)> ConnectedAsync(Guid? tenant = null)
    {
        var t = tenant ?? Tenant;
        await _h.ReadyAsync(t);
        var user = await _h.CreateUserAsync(t, AllToolPermissions);
        var token = await _h.ConnectAsync(user);
        var authorization = AuthorizationIdOf(token.AccessToken!);
        var row = await _h.InScopeAsync(t, services => services.GetRequiredService<IRepository<AiConnection, Guid>>().FirstAsync(c => c.AuthorizationId == authorization));
        return (user, token, row.Id);
    }

    private static string ListUrl(int endRow = 200) => Root + "/connections?StartRow=0&EndRow=" + endRow;

    private static List<Guid> RowIds(JsonElement list) => list.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).ToList();

    /* ---------- The routes ---------- */

    [Fact]
    public async Task The_status_route_answers_the_url_and_who_accepted_when()
    {
        var tenant = _h.TenantB;
        await _h.EnsureFeaturesOnAsync(tenant);
        await _h.ClearAcceptedAsync(tenant);
        var manager = await _h.CreateUserAsync(tenant, IntegrationsPermissions.AiConnector.Manage);
        var token = await _h.BrowserTokenAsync(manager);

        var open = await JsonAsync(await _h.RestAsync(token, HttpMethod.Get, Root + "/status"));
        open.GetProperty("connectorUrl").GetString().ShouldEndWith("/mcp");
        open.GetProperty("isAccepted").GetBoolean().ShouldBeFalse();
        open.GetProperty("acceptedAt").ValueKind.ShouldBe(JsonValueKind.Null);

        var accepted = await JsonAsync(await _h.RestAsync(token, HttpMethod.Post, Root + "/accept"));
        accepted.GetProperty("isAccepted").GetBoolean().ShouldBeTrue();
        accepted.GetProperty("acceptedAt").ValueKind.ShouldBe(JsonValueKind.String);
        accepted.GetProperty("acceptedByUserName").GetString().ShouldBe(manager.Email);

        var read = await JsonAsync(await _h.RestAsync(token, HttpMethod.Get, Root + "/status"));
        read.GetProperty("isAccepted").GetBoolean().ShouldBeTrue();
        read.GetProperty("acceptedByUserName").GetString().ShouldBe(manager.Email);

        /* Accepting twice keeps the first acceptance on record. */
        var other = await _h.CreateUserAsync(tenant, IntegrationsPermissions.AiConnector.Manage);
        var otherToken = await _h.BrowserTokenAsync(other);
        var again = await JsonAsync(await _h.RestAsync(otherToken, HttpMethod.Post, Root + "/accept"));
        again.GetProperty("acceptedByUserName").GetString().ShouldBe(manager.Email);
        again.GetProperty("acceptedAt").GetString().ShouldBe(accepted.GetProperty("acceptedAt").GetString());

        var withdrawn = await JsonAsync(await _h.RestAsync(token, HttpMethod.Post, Root + "/withdraw"));
        withdrawn.GetProperty("isAccepted").GetBoolean().ShouldBeFalse();
        withdrawn.GetProperty("acceptedByUserName").ValueKind.ShouldBe(JsonValueKind.Null);
        (await JsonAsync(await _h.RestAsync(token, HttpMethod.Get, Root + "/status"))).GetProperty("isAccepted").GetBoolean().ShouldBeFalse();

        await _h.SetAcceptedAsync(tenant);
    }

    public static TheoryData<string, string> RoutesNeedingManage => new()
    {
        { "POST", Root + "/accept" },
        { "POST", Root + "/withdraw" },
    };

    [Theory]
    [MemberData(nameof(RoutesNeedingManage))]
    public async Task Accept_and_withdraw_need_Manage(string method, string path)
    {
        await _h.ReadyAsync(Tenant);
        var useOnly = await _h.CreateUserAsync(Tenant, IntegrationsPermissions.AiConnector.Use);
        using var response = await _h.RestAsync(await _h.BrowserTokenAsync(useOnly), new HttpMethod(method), path);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
    }

    public static TheoryData<string, string> AllRoutes => new()
    {
        { "GET", Root + "/status" },
        { "POST", Root + "/accept" },
        { "POST", Root + "/withdraw" },
        { "GET", Root + "/connections?StartRow=0&EndRow=10" },
        { "POST", Root + "/connections/00000000-0000-0000-0000-000000000001/revoke" },
    };

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task Every_route_refuses_anonymous_and_a_user_with_neither_permission(string method, string path)
    {
        await _h.ReadyAsync(Tenant);
        using (var anonymous = await _h.RestAsync(null, new HttpMethod(method), path))
        {
            anonymous.StatusCode.ShouldBe(HttpStatusCode.Found, path);
            anonymous.Headers.Location!.OriginalString.ShouldContain("/Account/Login", Case.Insensitive, path);
        }

        var nobody = await _h.CreateUserAsync(Tenant);
        using var response = await _h.RestAsync(await _h.BrowserTokenAsync(nobody), new HttpMethod(method), path);
        if (path.EndsWith("/revoke", StringComparison.Ordinal))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, "a row that does not exist, before any permission of a row");
        }
        else
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
        }
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task Every_route_is_off_without_either_feature(string method, string path)
    {
        var tenant = _h.TenantC;
        await _h.ReadyAsync(tenant);
        var manager = await _h.CreateUserAsync(tenant, IntegrationsPermissions.AiConnector.Use, IntegrationsPermissions.AiConnector.Manage);
        var token = await _h.BrowserTokenAsync(manager);

        foreach (var feature in new[] { AppFeatures.Integrations, AppFeatures.IntegrationsApp.AiConnector })
        {
            await _h.WithFeatureOffAsync(tenant, feature, async () =>
            {
                using var response = await _h.RestAsync(token, new HttpMethod(method), path);
                response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, feature + " " + path + ": " + await response.Content.ReadAsStringAsync());
            });
        }

        await _h.SetAcceptedAsync(tenant);
    }

    /* ---------- Own versus all ---------- */

    [Fact]
    public async Task Use_sees_only_its_own_connections_and_Manage_sees_all()
    {
        var (mine, _, myRow) = await ConnectedAsync();
        var (_, _, theirRow) = await ConnectedAsync();

        var ownView = await JsonAsync(await _h.RestAsync(await _h.BrowserTokenAsync(mine), HttpMethod.Get, ListUrl()));
        RowIds(ownView).ShouldBe([myRow]);
        ownView.GetProperty("totalCount").GetInt64().ShouldBe(1);
        ownView.GetProperty("filteredCount").GetInt64().ShouldBe(1);

        var row = ownView.GetProperty("rows")[0];
        row.GetProperty("userId").GetGuid().ShouldBe(mine.Id);
        row.GetProperty("userName").GetString().ShouldBe(mine.Email);
        row.GetProperty("toolName").GetString().ShouldBe("Claude");
        row.GetProperty("connectedAt").ValueKind.ShouldBe(JsonValueKind.String);

        var manager = await ManagerTokenAsync(Tenant);
        var allView = await JsonAsync(await _h.RestAsync(manager, HttpMethod.Get, ListUrl()));
        RowIds(allView).ShouldContain(myRow);
        RowIds(allView).ShouldContain(theirRow);
    }

    /* ---------- Revoke ---------- */

    [Fact]
    public async Task Use_revokes_its_own_connection_and_not_anothers_and_Manage_revokes_anyones()
    {
        var (mine, myToken, myRow) = await ConnectedAsync();
        var (_, theirToken, theirRow) = await ConnectedAsync();
        var mineRest = await _h.BrowserTokenAsync(mine);

        using (var forbidden = await _h.RestAsync(mineRest, HttpMethod.Post, $"{Root}/connections/{theirRow}/revoke"))
        {
            forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await forbidden.Content.ReadAsStringAsync());
        }

        (await _h.ListToolsAsync(theirToken.AccessToken!)).ShouldNotBeEmpty("a refused revoke changes nothing");

        using (var own = await _h.RestAsync(mineRest, HttpMethod.Post, $"{Root}/connections/{myRow}/revoke"))
        {
            own.StatusCode.ShouldBe(HttpStatusCode.NoContent, await own.Content.ReadAsStringAsync());
        }

        using (var refused = await _h.PostMcpAsync(myToken.AccessToken, "tools/list"))
        {
            await ShouldBeRefusedAsync(refused, nameof(AiConnectorRefusal.ConnectionRevoked));
        }

        var manager = await _h.CreateUserAsync(Tenant, IntegrationsPermissions.AiConnector.Manage);
        using (var managed = await _h.RestAsync(await _h.BrowserTokenAsync(manager), HttpMethod.Post, $"{Root}/connections/{theirRow}/revoke"))
        {
            managed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await managed.Content.ReadAsStringAsync());
        }

        var row = await _h.InScopeAsync(Tenant, services => services.GetRequiredService<IRepository<AiConnection, Guid>>().GetAsync(theirRow));
        row.RevokedAt.ShouldNotBeNull();
        row.RevokedByUserId.ShouldBe(manager.Id);
    }

    [Fact]
    public async Task Revoking_twice_answers_204_and_keeps_the_first_revocation()
    {
        var (user, token, rowId) = await ConnectedAsync();
        var rest = await _h.BrowserTokenAsync(user);

        using (var first = await _h.RestAsync(rest, HttpMethod.Post, $"{Root}/connections/{rowId}/revoke"))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var before = await _h.InScopeAsync(Tenant, services => services.GetRequiredService<IRepository<AiConnection, Guid>>().GetAsync(rowId));

        var manager = await _h.CreateUserAsync(Tenant, IntegrationsPermissions.AiConnector.Manage);
        using (var second = await _h.RestAsync(await _h.BrowserTokenAsync(manager), HttpMethod.Post, $"{Root}/connections/{rowId}/revoke"))
        {
            second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var after = await _h.InScopeAsync(Tenant, services => services.GetRequiredService<IRepository<AiConnection, Guid>>().GetAsync(rowId));
        after.RevokedAt.ShouldBe(before.RevokedAt);
        after.RevokedByUserId.ShouldBe(user.Id);

        /* The host side went too: the authorization no longer renews. */
        (await _h.RefreshAsync(ClaudeClient, token.RefreshToken!)).AccessToken.ShouldBeNull();
    }

    [Fact]
    public async Task Withdrawing_revokes_every_connection_of_the_tenant_and_nothing_of_another()
    {
        var tenant = _h.TenantB;
        var other = _h.TenantD;
        var (_, tokenA, rowA) = await ConnectedAsync(tenant);
        var (_, tokenB, rowB) = await ConnectedAsync(tenant);
        var (_, otherToken, otherRow) = await ConnectedAsync(other);
        var manager = await ManagerTokenAsync(tenant);

        (await JsonAsync(await _h.RestAsync(manager, HttpMethod.Post, Root + "/withdraw"))).GetProperty("isAccepted").GetBoolean().ShouldBeFalse();
        await _h.SetAcceptedAsync(tenant);

        foreach (var (token, row) in new[] { (tokenA, rowA), (tokenB, rowB) })
        {
            using var refused = await _h.PostMcpAsync(token.AccessToken, "tools/list");
            await ShouldBeRefusedAsync(refused, nameof(AiConnectorRefusal.ConnectionRevoked));
            (await _h.InScopeAsync(tenant, services => services.GetRequiredService<IRepository<AiConnection, Guid>>().GetAsync(row))).RevokedAt.ShouldNotBeNull();
            (await _h.RefreshAsync(ClaudeClient, token.RefreshToken!)).AccessToken.ShouldBeNull();
        }

        (await _h.ListToolsAsync(otherToken.AccessToken!)).ShouldNotBeEmpty("the shared-tier neighbour in the same database goes on");
        (await _h.InScopeAsync(other, services => services.GetRequiredService<IRepository<AiConnection, Guid>>().GetAsync(otherRow))).RevokedAt.ShouldBeNull();
    }

    /* ---------- Negative tenant test per endpoint ---------- */

    [Theory]
    [InlineData("own")]
    [InlineData("shared")]
    public async Task Each_endpoint_of_another_tenant_reaches_nothing_of_this_tenants(string tier)
    {
        var (mine, other, otherName) = _h.Pair(tier);
        var (user, token, rowId) = await ConnectedAsync(mine);
        await _h.ReadyAsync(other);
        var otherManager = await ManagerTokenAsync(other);

        var status = await JsonAsync(await _h.RestAsync(otherManager, HttpMethod.Get, Root + "/status"));
        JsonSerializer.Serialize(status).ShouldNotContain(user.Email);

        var list = await JsonAsync(await _h.RestAsync(otherManager, HttpMethod.Get, ListUrl()));
        RowIds(list).ShouldNotContain(rowId);
        JsonSerializer.Serialize(list).ShouldNotContain(user.Id.ToString());

        using (var revoke = await _h.RestAsync(otherManager, HttpMethod.Post, $"{Root}/connections/{rowId}/revoke"))
        {
            revoke.StatusCode.ShouldBe(HttpStatusCode.NotFound, await revoke.Content.ReadAsStringAsync());
        }

        /* And asking for the tenant by name changes nothing: the token decides. */
        var mineName = tier == "own" ? HostIntegrationFixture.TenantAName : HostIntegrationFixture.TenantBName;
        var asked = await JsonAsync(await _h.RestAsync(otherManager, HttpMethod.Get, ListUrl() + "&__tenant=" + mineName));
        RowIds(asked).ShouldNotContain(rowId);

        using (var withdraw = await _h.RestAsync(otherManager, HttpMethod.Post, Root + "/withdraw"))
        {
            withdraw.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await _h.SetAcceptedAsync(other);
        using (var accept = await _h.RestAsync(otherManager, HttpMethod.Post, Root + "/accept"))
        {
            accept.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await _h.ListToolsAsync(token.AccessToken!)).ShouldNotBeEmpty("this tenant's connection goes on");
    }

    /* ---------- Art. 17 ---------- */

    private async Task DeleteUserAsync(TestUser user)
        => await _h.InScopeAsync(user.TenantId, async services =>
        {
            var users = services.GetRequiredService<Volo.Abp.Identity.IdentityUserManager>();
            (await users.DeleteAsync(await users.GetByIdAsync(user.Id))).Succeeded.ShouldBeTrue();
        });

    private async Task<string?> SettingAsync(Guid tenant, string name)
        => await _h.InScopeAsync(tenant, services => services.GetRequiredService<ISettingProvider>().GetOrNullAsync(name), transactional: false);

    [Theory]
    [InlineData("own")]
    [InlineData("shared")]
    public async Task Deleting_a_user_revokes_and_removes_the_users_connections(string tier)
    {
        var tenant = _h.Pair(tier).Mine;
        var (user, token, rowId) = await ConnectedAsync(tenant);
        var (_, otherToken, otherRow) = await ConnectedAsync(tenant);
        var authorization = AuthorizationIdOf(token.AccessToken!);

        await DeleteUserAsync(user);

        (await _h.InScopeAsync(tenant, services => services.GetRequiredService<IRepository<AiConnection, Guid>>().FindAsync(rowId))).ShouldBeNull("art. 17: the row goes");
        (await _h.RefreshAsync(ClaudeClient, token.RefreshToken!)).AccessToken.ShouldBeNull("its authorization went in the host database");
        var status = await _h.InScopeAsync(null, async services =>
        {
            var manager = services.GetRequiredService<IOpenIddictAuthorizationManager>();
            var row = await manager.FindByIdAsync(authorization);
            return row == null ? null : await manager.GetStatusAsync(row);
        });
        status.ShouldBe(OpenIddictConstants.Statuses.Revoked);

        using var refused = await _h.PostMcpAsync(token.AccessToken, "tools/list");
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await _h.InScopeAsync(tenant, services => services.GetRequiredService<IRepository<AiConnection, Guid>>().FindAsync(otherRow))).ShouldNotBeNull("another user's connection stays");
        (await _h.ListToolsAsync(otherToken.AccessToken!)).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Deleting_the_user_who_accepted_clears_the_id_and_leaves_the_acceptance_standing()
    {
        var tenant = _h.TenantC;
        await _h.EnsureFeaturesOnAsync(tenant);
        await _h.ClearAcceptedAsync(tenant);
        var acceptor = await _h.CreateUserAsync(tenant, IntegrationsPermissions.AiConnector.Manage);
        var keeper = await _h.CreateUserAsync(tenant, IntegrationsPermissions.AiConnector.Manage);
        var acceptorToken = await _h.BrowserTokenAsync(acceptor);
        var keeperToken = await _h.BrowserTokenAsync(keeper);
        using (var accept = await _h.RestAsync(acceptorToken, HttpMethod.Post, Root + "/accept"))
        {
            accept.StatusCode.ShouldBe(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());
        }

        (await SettingAsync(tenant, IntegrationsSettings.AiConnectorAcceptedByUserId)).ShouldBe(acceptor.Id.ToString());
        var acceptedAt = await SettingAsync(tenant, IntegrationsSettings.AiConnectorAcceptedAt);
        acceptedAt.ShouldNotBeNullOrEmpty();

        await DeleteUserAsync(acceptor);

        (await SettingAsync(tenant, IntegrationsSettings.AiConnectorAcceptedByUserId)).ShouldBeNullOrEmpty("art. 17 clears the id");
        (await SettingAsync(tenant, IntegrationsSettings.AiConnectorAcceptedAt)).ShouldBe(acceptedAt, "the acceptance stands");

        var status = await JsonAsync(await _h.RestAsync(keeperToken, HttpMethod.Get, Root + "/status"));
        status.GetProperty("isAccepted").GetBoolean().ShouldBeTrue();
        status.GetProperty("acceptedByUserName").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Deleting_another_user_leaves_the_acceptances_id_alone()
    {
        var tenant = _h.TenantC;
        await _h.EnsureFeaturesOnAsync(tenant);
        await _h.ClearAcceptedAsync(tenant);
        var acceptor = await _h.CreateUserAsync(tenant, IntegrationsPermissions.AiConnector.Manage);
        var bystander = await _h.CreateUserAsync(tenant);
        using (var accept = await _h.RestAsync(await _h.BrowserTokenAsync(acceptor), HttpMethod.Post, Root + "/accept"))
        {
            accept.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await DeleteUserAsync(bystander);

        (await SettingAsync(tenant, IntegrationsSettings.AiConnectorAcceptedByUserId)).ShouldBe(acceptor.Id.ToString());
    }
}

/// <summary>
/// Design 74 "A row's status is what /mcp would do" (PO decision 6, risk class Medium for the screen, Critical where it reads across users), over HTTP on PostgreSQL: each of Live, NotWorking, Expired and SwitchedOff; Expired wins over NotWorking;
/// the roles of organisation units count, as in <c>/mcp</c>'s claims; the page's role reads do not grow with its users; the negative tenant test; the sort translates on PostgreSQL and orders as the design says.
/// </summary>
[Collection(HostIntegrationCollection.Name)]
public class ConnectionStatusHostTests
{
    private const string Root = "/api/integrations/ai-connector";

    private readonly AiConnectorHarness _h;

    public ConnectionStatusHostTests(HostIntegrationFixture host)
    {
        _h = new AiConnectorHarness(host);
    }

    private async Task<(TestUser User, TokenAnswer Token, Guid RowId)> ConnectedAsync(Guid tenant, params string[]? permissions)
    {
        await _h.ReadyAsync(tenant);
        var user = await _h.CreateUserAsync(tenant, permissions is { Length: > 0 } ? permissions : AllToolPermissions);
        var token = await _h.ConnectAsync(user);
        var authorization = AuthorizationIdOf(token.AccessToken!);
        var row = await _h.InScopeAsync(tenant, services => services.GetRequiredService<IRepository<AiConnection, Guid>>().FirstAsync(c => c.AuthorizationId == authorization));
        return (user, token, row.Id);
    }

    private async Task BackdateAsync(Guid tenant, Guid rowId, TimeSpan connectedAgo, TimeSpan? lastUsedAgo)
    {
        var now = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
        await _h.ExecuteAsync(tenant, "UPDATE integrations.\"AiConnections\" SET \"ConnectedAt\" = @c, \"LastUsedAt\" = @u WHERE \"Id\" = @id",
            ("c", now - connectedAgo), ("u", lastUsedAgo == null ? DBNull.Value : now - lastUsedAgo.Value), ("id", rowId));
    }

    /// <summary>A row of the connections screen, read as the screen reads it, with a manager's real token.</summary>
    private async Task<JsonElement> RowAsync(Guid tenant, Guid rowId)
    {
        var manager = await _h.CreateUserAsync(tenant, IntegrationsPermissions.AiConnector.Manage);
        using var response = await _h.RestAsync(await _h.BrowserTokenAsync(manager), HttpMethod.Get, Root + "/connections?StartRow=0&EndRow=200");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == rowId);
    }

    private async Task StatusShouldBeAsync(Guid tenant, Guid rowId, AiConnectionStatus expected)
        => (await RowAsync(tenant, rowId)).GetProperty("status").GetInt32().ShouldBe((int)expected, expected.ToString());

    /* ---------- Each status, and that it says what /mcp does ---------- */

    [Fact]
    public async Task A_fresh_connection_is_Live_and_mcp_serves_it()
    {
        var tenant = await _h.NewTenantAsync();
        var (_, token, row) = await ConnectedAsync(tenant);
        await StatusShouldBeAsync(tenant, row, AiConnectionStatus.Live);
        (await _h.ListToolsAsync(token.AccessToken!)).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_deactivated_user_is_NotWorking_like_mcp_and_Live_again_on_reactivation()
    {
        var tenant = await _h.NewTenantAsync();
        var (user, token, row) = await ConnectedAsync(tenant);
        await _h.SetUserActiveAsync(user, false);
        await StatusShouldBeAsync(tenant, row, AiConnectionStatus.NotWorking);
        using (var refused = await _h.PostMcpAsync(token.AccessToken, "tools/list"))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        await _h.SetUserActiveAsync(user, true);
        await StatusShouldBeAsync(tenant, row, AiConnectionStatus.Live);
    }

    [Fact]
    public async Task A_user_without_Use_is_NotWorking_like_mcp()
    {
        var tenant = await _h.NewTenantAsync();
        var (user, token, row) = await ConnectedAsync(tenant);
        await _h.SetPermissionAsync(user, IntegrationsPermissions.AiConnector.Use, false);
        await StatusShouldBeAsync(tenant, row, AiConnectionStatus.NotWorking);
        using var refused = await _h.PostMcpAsync(token.AccessToken, "tools/list");
        await ShouldBeRefusedAsync(refused, nameof(AiConnectorRefusal.PermissionMissing));
    }

    [Fact]
    public async Task A_row_whose_user_is_missing_is_NotWorking()
    {
        var tenant = await _h.NewTenantAsync();
        await _h.ReadyAsync(tenant);
        var ghost = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        await _h.InScopeAsync(tenant, services => services.GetRequiredService<IRepository<AiConnection, Guid>>().InsertAsync(
            new AiConnection(rowId, tenant, ghost, ClaudeClient, "ghost-" + rowId.ToString("N"), DateTime.Now), autoSave: true));

        var row = await RowAsync(tenant, rowId);
        row.GetProperty("status").GetInt32().ShouldBe((int)AiConnectionStatus.NotWorking);
        row.GetProperty("userName").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Unused_past_the_refresh_lifetime_is_Expired_and_within_it_is_Live()
    {
        var tenant = await _h.NewTenantAsync();
        var (_, _, row) = await ConnectedAsync(tenant);
        var lifetime = AiConnectorConsts.RefreshTokenLifetime;

        await BackdateAsync(tenant, row, lifetime + TimeSpan.FromHours(1), lifetime + TimeSpan.FromHours(1));
        await StatusShouldBeAsync(tenant, row, AiConnectionStatus.Expired);

        await BackdateAsync(tenant, row, lifetime + TimeSpan.FromHours(1), null);
        await StatusShouldBeAsync(tenant, row, AiConnectionStatus.Expired);

        /* A recent use keeps an old connection live, and a recent connecting keeps an unused one live. */
        await BackdateAsync(tenant, row, lifetime + TimeSpan.FromDays(100), lifetime - TimeSpan.FromHours(1));
        await StatusShouldBeAsync(tenant, row, AiConnectionStatus.Live);

        await BackdateAsync(tenant, row, lifetime - TimeSpan.FromHours(1), null);
        await StatusShouldBeAsync(tenant, row, AiConnectionStatus.Live);
    }

    [Fact]
    public async Task Expired_wins_over_NotWorking()
    {
        var tenant = await _h.NewTenantAsync();
        var (user, _, row) = await ConnectedAsync(tenant);
        await BackdateAsync(tenant, row, AiConnectorConsts.RefreshTokenLifetime + TimeSpan.FromHours(1), null);
        await _h.SetUserActiveAsync(user, false);
        await StatusShouldBeAsync(tenant, row, AiConnectionStatus.Expired);

        /* Whatever is fixed first, the lapsed token stays lapsed. */
        await _h.SetUserActiveAsync(user, true);
        await StatusShouldBeAsync(tenant, row, AiConnectionStatus.Expired);
    }

    [Fact]
    public async Task A_revoked_row_is_SwitchedOff_whatever_else_holds()
    {
        var tenant = await _h.NewTenantAsync();
        var (user, _, row) = await ConnectedAsync(tenant);
        using (var revoke = await _h.RestAsync(await _h.BrowserTokenAsync(user), HttpMethod.Post, $"{Root}/connections/{row}/revoke"))
        {
            revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var read = await RowAsync(tenant, row);
        read.GetProperty("status").GetInt32().ShouldBe((int)AiConnectionStatus.SwitchedOff);
        read.GetProperty("revokedAt").ValueKind.ShouldBe(JsonValueKind.String);

        await BackdateAsync(tenant, row, AiConnectorConsts.RefreshTokenLifetime + TimeSpan.FromHours(1), null);
        await _h.SetUserActiveAsync(user, false);
        await StatusShouldBeAsync(tenant, row, AiConnectionStatus.SwitchedOff);
    }

    /* ---------- Roles ---------- */

    private async Task<string> RoleWithUseAsync(Guid tenant)
    {
        var name = "ai-role-" + Guid.NewGuid().ToString("N")[..8];
        await _h.InScopeAsync(tenant, async services =>
        {
            var roles = services.GetRequiredService<Volo.Abp.Identity.IdentityRoleManager>();
            (await roles.CreateAsync(new Volo.Abp.Identity.IdentityRole(Guid.NewGuid(), name, tenant))).Succeeded.ShouldBeTrue();
            var grants = services.GetRequiredService<IPermissionManager>();
            foreach (var permission in AllToolPermissions)
            {
                await grants.SetForRoleAsync(name, permission, true);
            }
        });
        return name;
    }

    [Fact]
    public async Task The_role_of_an_organisation_unit_counts_on_the_screen_as_it_does_on_mcp()
    {
        var tenant = await _h.NewTenantAsync();
        await _h.ReadyAsync(tenant);
        var roleName = await RoleWithUseAsync(tenant);
        var unitId = Guid.NewGuid();
        await _h.InScopeAsync(tenant, async services =>
        {
            await services.GetRequiredService<OrganizationUnitManager>().CreateAsync(new OrganizationUnit(unitId, "Unit " + roleName, null, tenant));
        });
        await _h.InScopeAsync(tenant, async services =>
        {
            var role = await services.GetRequiredService<Volo.Abp.Identity.IdentityRoleManager>().FindByNameAsync(roleName);
            await services.GetRequiredService<OrganizationUnitManager>().AddRoleToOrganizationUnitAsync(role!.Id, unitId);
        });

        /* No grant on the user itself, no role of its own: only the unit. */
        var user = await _h.CreateUserAsync(tenant);
        await _h.InScopeAsync(tenant, services => services.GetRequiredService<Volo.Abp.Identity.IdentityUserManager>().AddToOrganizationUnitAsync(user.Id, unitId));

        var token = await _h.ConnectAsync(user);
        var authorization = AuthorizationIdOf(token.AccessToken!);
        var row = await _h.InScopeAsync(tenant, services => services.GetRequiredService<IRepository<AiConnection, Guid>>().FirstAsync(c => c.AuthorizationId == authorization));

        await StatusShouldBeAsync(tenant, row.Id, AiConnectionStatus.Live);
        (await _h.ListToolsAsync(token.AccessToken!)).ShouldNotBeEmpty("/mcp agrees: it serves the role of the unit");

        /* Out of the unit: both stop. */
        await _h.InScopeAsync(tenant, services => services.GetRequiredService<Volo.Abp.Identity.IdentityUserManager>().RemoveFromOrganizationUnitAsync(user.Id, unitId));
        await ClearDynamicClaimsAsync(user);
        await StatusShouldBeAsync(tenant, row.Id, AiConnectionStatus.NotWorking);
    }

    [Fact]
    public async Task A_direct_role_counts()
    {
        var tenant = await _h.NewTenantAsync();
        await _h.ReadyAsync(tenant);
        var roleName = await RoleWithUseAsync(tenant);
        var user = await _h.CreateUserAsync(tenant);
        await _h.InScopeAsync(tenant, async services =>
        {
            var users = services.GetRequiredService<Volo.Abp.Identity.IdentityUserManager>();
            (await users.AddToRoleAsync(await users.GetByIdAsync(user.Id), roleName)).Succeeded.ShouldBeTrue();
        });
        var token = await _h.ConnectAsync(user);
        var authorization = AuthorizationIdOf(token.AccessToken!);
        var row = await _h.InScopeAsync(tenant, services => services.GetRequiredService<IRepository<AiConnection, Guid>>().FirstAsync(c => c.AuthorizationId == authorization));

        await StatusShouldBeAsync(tenant, row.Id, AiConnectionStatus.Live);
        (await _h.ListToolsAsync(token.AccessToken!)).ShouldNotBeEmpty();
    }

    private async Task ClearDynamicClaimsAsync(TestUser user)
        => await _h.InScopeAsync(user.TenantId, services => services.GetRequiredService<IdentityDynamicClaimsPrincipalContributorCache>().ClearAsync(user.Id, user.TenantId));

    /* ---------- No N+1 ---------- */

    private async Task<int> RoleReadsOfAPageAsync(Guid tenant, TestUser manager)
    {
        return await _h.InScopeAsync(tenant, async services =>
        {
            using var capture = EfCommandCapture.Begin();
            await Task.Delay(EfCommandCapture.LoggingCacheTime);
            var page = await services.GetRequiredService<IAiConnectionAppService>().GetListAsync(new ReferenceDataListRequestDto { StartRow = 0, EndRow = 200 });
            page.Rows.Count.ShouldBeGreaterThan(0);
            capture.Commands.ShouldNotBeEmpty("the capture saw the page's own queries");
            return capture.On("AbpUserRoles").Count;
        }, AiConnectorHarness.PrincipalOf(manager), transactional: false);
    }

    [Fact]
    public async Task A_pages_role_reads_do_not_grow_with_its_users()
    {
        var tenant = await _h.NewTenantAsync();
        await _h.ReadyAsync(tenant);
        var manager = await _h.CreateUserAsync(tenant, IntegrationsPermissions.AiConnector.Manage);

        for (var i = 0; i < 3; i++)
        {
            await ConnectedAsync(tenant);
        }

        var few = await RoleReadsOfAPageAsync(tenant, manager);

        for (var i = 0; i < 9; i++)
        {
            await ConnectedAsync(tenant);
        }

        var many = await RoleReadsOfAPageAsync(tenant, manager);

        few.ShouldBeGreaterThan(0, "the page reads the roles");
        many.ShouldBe(few, "one batched read for the page, however many users it has");
    }

    /* ---------- Tenants ---------- */

    [Theory]
    [InlineData("own")]
    [InlineData("shared")]
    public async Task A_status_is_read_only_in_the_tenants_own_rows(string tier)
    {
        var (mine, other, _) = _h.Pair(tier);
        var (_, _, row) = await ConnectedAsync(mine);
        await _h.ReadyAsync(other);
        var manager = await _h.CreateUserAsync(other, IntegrationsPermissions.AiConnector.Manage);
        using var response = await _h.RestAsync(await _h.BrowserTokenAsync(manager), HttpMethod.Get, Root + "/connections?StartRow=0&EndRow=200");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        body.ShouldNotContain(row.ToString(), Case.Insensitive);
    }

    /* ---------- The sort ---------- */

    [Theory]
    [InlineData(null, false)]
    [InlineData("connectedAt", false)]
    [InlineData("connectedAt", true)]
    [InlineData("lastUsedAt", false)]
    [InlineData("lastUsedAt", true)]
    [InlineData("revokedAt", false)]
    [InlineData("revokedAt", true)]
    [InlineData("nonsense", true)]
    public async Task The_sort_translates_on_PostgreSQL_with_live_rows_first_then_expired_then_switched_off(string? sortBy, bool descending)
    {
        var tenant = await _h.NewTenantAsync();
        var (user, _, switchedOff) = await ConnectedAsync(tenant);
        using (var revoke = await _h.RestAsync(await _h.BrowserTokenAsync(user), HttpMethod.Post, $"{Root}/connections/{switchedOff}/revoke"))
        {
            revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var (_, _, expired) = await ConnectedAsync(tenant);
        await BackdateAsync(tenant, expired, AiConnectorConsts.RefreshTokenLifetime + TimeSpan.FromHours(1), null);
        var (notWorkingUser, _, notWorking) = await ConnectedAsync(tenant);
        await _h.SetUserActiveAsync(notWorkingUser, false);
        var (_, _, live) = await ConnectedAsync(tenant);

        var manager = await _h.CreateUserAsync(tenant, IntegrationsPermissions.AiConnector.Manage);
        var url = Root + "/connections?StartRow=0&EndRow=200" + (sortBy == null ? string.Empty : "&SortBy=" + sortBy + "&SortDir=" + (descending ? "desc" : "asc"));
        using var response = await _h.RestAsync(await _h.BrowserTokenAsync(manager), HttpMethod.Get, url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, sortBy + ": " + body);

        var rows = JsonDocument.Parse(body).RootElement.GetProperty("rows").EnumerateArray().Select(r => (Id: r.GetProperty("id").GetGuid(), Status: r.GetProperty("status").GetInt32())).ToList();
        rows.Select(r => r.Status).ShouldBeInOrder(SortDirection.Ascending, "Live first, then Not working, Expired, Switched off, within the page");
        rows.Single(r => r.Id == live).Status.ShouldBe((int)AiConnectionStatus.Live);
        rows.Single(r => r.Id == notWorking).Status.ShouldBe((int)AiConnectionStatus.NotWorking);
        rows.Single(r => r.Id == expired).Status.ShouldBe((int)AiConnectionStatus.Expired);
        rows.Single(r => r.Id == switchedOff).Status.ShouldBe((int)AiConnectionStatus.SwitchedOff);
    }

    [Fact]
    public async Task The_sort_asked_for_holds_among_live_rows()
    {
        var tenant = await _h.NewTenantAsync();
        var (_, _, older) = await ConnectedAsync(tenant);
        var (_, _, newer) = await ConnectedAsync(tenant);
        await BackdateAsync(tenant, older, TimeSpan.FromDays(3), null);
        await BackdateAsync(tenant, newer, TimeSpan.FromDays(1), null);

        var manager = await _h.CreateUserAsync(tenant, IntegrationsPermissions.AiConnector.Manage);
        var token = await _h.BrowserTokenAsync(manager);

        async Task<List<Guid>> OrderAsync(string query)
        {
            using var response = await _h.RestAsync(token, HttpMethod.Get, Root + "/connections?StartRow=0&EndRow=200" + query);
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
            return JsonDocument.Parse(body).RootElement.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).Where(id => id == older || id == newer).ToList();
        }

        (await OrderAsync("&SortBy=connectedAt&SortDir=asc")).ShouldBe([older, newer]);
        (await OrderAsync("&SortBy=connectedAt&SortDir=desc")).ShouldBe([newer, older]);
        (await OrderAsync(string.Empty)).ShouldBe([newer, older], "the newest by default");
    }
}
