using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenTms.HostIntegration;
using OpenTms.Integrations.AiConnector;
using OpenTms.Integrations.OrderIntake;
using OpenTms.Integrations.Permissions;
using OpenTms.Platform.Features;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Security.Claims;
using Volo.Abp.TenantManagement;
using Xunit;
using static OpenTms.AiConnector.AiConnectorHarness;

namespace OpenTms.AiConnector;

/// <summary>
/// Design 74 "Token separation" (risk class Critical), both ways, on the real host: a connector token gets 403 on a REST endpoint; a browser token, a client-credentials token, a token of another audience
/// and a signed-in browser session without a token are all refused on <c>/mcp</c>. Also the 401 and metadata that let an AI tool find the authorization server, 405 on GET and DELETE, and what a connector token states.
/// </summary>
[Collection(HostIntegrationCollection.Name)]
public class McpTokenSeparationHostTests
{
    private readonly AiConnectorHarness _h;

    public McpTokenSeparationHostTests(HostIntegrationFixture host)
    {
        _h = new AiConnectorHarness(host);
    }

    private async Task<(TestUser User, TokenAnswer Token)> ConnectedAsync(params string[] extra)
    {
        await _h.ReadyAsync(_h.TenantA);
        var user = await _h.CreateUserAsync(_h.TenantA, AllToolPermissions.Concat(extra).ToArray());
        return (user, await _h.ConnectAsync(user));
    }

    [Theory]
    [InlineData("/api/abp/application-configuration")]
    [InlineData("/api/integrations/ai-connector/status")]
    [InlineData("/api/orders/transport-order?StartRow=0&EndRow=10")]
    [InlineData("/api/planning/transport-trip?StartRow=0&EndRow=10")]
    [InlineData("/.well-known/oauth-protected-resource")]
    [InlineData("/health/live")]
    public async Task A_connector_token_gets_403_on_every_path_other_than_mcp(string path)
    {
        var (_, token) = await ConnectedAsync(IntegrationsPermissions.AiConnector.Manage);
        using var response = await _h.RestAsync(token.AccessToken, HttpMethod.Get, path);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, body);
        body.ShouldContain("connector_token_not_allowed_here");
    }

    [Fact]
    public async Task A_connector_token_cannot_write_through_a_rest_endpoint_either()
    {
        var (_, token) = await ConnectedAsync(IntegrationsPermissions.AiConnector.Manage);
        using var response = await _h.RestAsync(token.AccessToken, HttpMethod.Post, "/api/integrations/ai-connector/withdraw");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_browser_token_of_the_spa_is_refused_on_mcp()
    {
        await _h.ReadyAsync(_h.TenantA);
        var user = await _h.CreateUserAsync(_h.TenantA, AllToolPermissions);
        var browserToken = await _h.BrowserTokenAsync(user);

        /* The token is real: the API takes it. */
        using (var api = await _h.RestAsync(browserToken, HttpMethod.Get, "/api/integrations/ai-connector/status"))
        {
            api.StatusCode.ShouldBe(HttpStatusCode.OK, await api.Content.ReadAsStringAsync());
        }

        using var response = await _h.PostMcpAsync(browserToken, "tools/list");
        await ShouldBeRefusedAsync(response, "not_a_connector_token");
    }

    [Fact]
    public async Task A_client_credentials_token_is_refused_on_mcp()
    {
        await _h.EnsureFeaturesOnAsync(_h.TenantA);
        const string clientId = "host-test-mcp-other-client";
        const string secret = "a-long-enough-secret-for-the-mcp-test-0123456789";
        await _h.InScopeAsync(null, async services =>
        {
            var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
            if (await applications.FindByClientIdAsync(clientId) == null)
            {
                await applications.CreateAsync(new OpenIddictApplicationDescriptor
                {
                    ClientId = clientId,
                    ClientSecret = secret,
                    ClientType = OpenIddictConstants.ClientTypes.Confidential,
                    DisplayName = "Not a connector",
                    Permissions =
                    {
                        OpenIddictConstants.Permissions.Endpoints.Token,
                        OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                        OpenIddictConstants.Permissions.Prefixes.Scope + OrderIntakeConsts.ApiScope,
                    },
                });
            }
        });

        var token = await _h.PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = OpenIddictConstants.GrantTypes.ClientCredentials,
            ["client_id"] = clientId,
            ["client_secret"] = secret,
            ["scope"] = OrderIntakeConsts.ApiScope,
        });
        token.Status.ShouldBe(HttpStatusCode.OK, token.Body);

        using var response = await _h.PostMcpAsync(token.AccessToken, "tools/list");
        await ShouldBeRefusedAsync(response, "not_a_connector_token");
    }

    [Fact]
    public async Task A_client_credentials_client_cannot_ask_for_the_connector_scope()
    {
        const string clientId = "host-test-mcp-scope-client";
        const string secret = "a-long-enough-secret-for-the-mcp-test-9876543210";
        await _h.InScopeAsync(null, async services =>
        {
            var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
            if (await applications.FindByClientIdAsync(clientId) == null)
            {
                await applications.CreateAsync(new OpenIddictApplicationDescriptor
                {
                    ClientId = clientId,
                    ClientSecret = secret,
                    ClientType = OpenIddictConstants.ClientTypes.Confidential,
                    DisplayName = "Not a connector either",
                    Permissions = { OpenIddictConstants.Permissions.Endpoints.Token, OpenIddictConstants.Permissions.GrantTypes.ClientCredentials },
                });
            }
        });

        var token = await _h.PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = OpenIddictConstants.GrantTypes.ClientCredentials,
            ["client_id"] = clientId,
            ["client_secret"] = secret,
            ["scope"] = AiConnectorConsts.Scope,
        });
        token.Status.ShouldBe(HttpStatusCode.BadRequest, token.Body);
        token.AccessToken.ShouldBeNull();
    }

    [Fact]
    public async Task A_token_of_another_audience_is_refused_on_mcp()
    {
        await _h.ReadyAsync(_h.TenantA);
        var user = await _h.CreateUserAsync(_h.TenantA, AllToolPermissions);

        /* An audience this host validates, the REST API's own: signed by the host, so only the gate stands in the way. */
        using (var rest = await _h.PostMcpAsync(_h.MintToken(user.Id, _h.TenantA, audience: "OpenTms"), "tools/list"))
        {
            await ShouldBeRefusedAsync(rest, "not_a_connector_token");
        }

        /* An audience nobody here validates: refused by token validation already. */
        using var foreign = await _h.PostMcpAsync(_h.MintToken(user.Id, _h.TenantA, audience: "Somebody.Else"), "tools/list");
        foreign.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_signed_in_browser_session_without_a_token_is_refused_on_mcp()
    {
        await _h.ReadyAsync(_h.TenantA);
        var user = await _h.CreateUserAsync(_h.TenantA, AllToolPermissions);
        var browser = await _h.SignedInBrowserAsync(user);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await browser.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync());
        response.Headers.WwwAuthenticate.ToString().ShouldContain("resource_metadata");
    }

    [Fact]
    public async Task A_session_cookie_beside_a_browser_token_does_not_help_and_a_session_beside_a_connector_token_does_not_change_its_user()
    {
        await _h.ReadyAsync(_h.TenantA);
        var user = await _h.CreateUserAsync(_h.TenantA, AllToolPermissions);
        var other = await _h.CreateUserAsync(_h.TenantA, AllToolPermissions);
        var token = await _h.ConnectAsync(user);
        var otherBrowser = await _h.SignedInBrowserAsync(other);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var response = await otherBrowser.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        /* Whose connection it counted on: the token's. */
        (await _h.InScopeAsync(_h.TenantA, services => services.GetRequiredService<IRepository<AiConnection, Guid>>().CountAsync(c => c.UserId == user.Id && c.LastUsedAt != null))).ShouldBe(1);
        (await _h.InScopeAsync(_h.TenantA, services => services.GetRequiredService<IRepository<AiConnection, Guid>>().CountAsync(c => c.UserId == other.Id))).ShouldBe(0);
    }

    [Fact]
    public async Task No_token_or_a_broken_one_gets_a_401_pointing_to_the_metadata()
    {
        using (var none = await _h.PostMcpAsync(null, "tools/list"))
        {
            none.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            var challenge = none.Headers.WwwAuthenticate.ToString();
            challenge.ShouldContain("resource_metadata=\"");
            challenge.ShouldContain(AiConnectorTokenGate.ProtectedResourceMetadataPath);
            challenge.ShouldContain(AiConnectorConsts.Scope);
            challenge.ShouldContain("offline_access");
            challenge.ShouldNotContain("invalid_token");
        }

        using var broken = await _h.PostMcpAsync("not.a.token", "tools/list");
        broken.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        broken.Headers.WwwAuthenticate.ToString().ShouldContain("invalid_token");
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("DELETE")]
    public async Task Get_and_delete_on_mcp_answer_405_stateless_mode(string method)
    {
        var (_, token) = await ConnectedAsync();
        using var response = await _h.RestAsync(token.AccessToken, new HttpMethod(method), "/mcp");
        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        response.Content.Headers.Allow.ShouldContain("POST");
    }

    [Theory]
    [InlineData("/mcp/")]
    [InlineData("/MCP")]
    public async Task The_gate_guards_every_spelling_routing_accepts(string path)
    {
        await _h.ReadyAsync(_h.TenantA);
        var user = await _h.CreateUserAsync(_h.TenantA, AllToolPermissions);
        var browserToken = await _h.BrowserTokenAsync(user);
        using var response = await _h.PostMcpAsync(browserToken, "tools/list", path: path);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_metadata_tells_an_ai_tool_where_to_go_without_a_token()
    {
        foreach (var path in new[] { AiConnectorTokenGate.ProtectedResourceMetadataPath, AiConnectorTokenGate.ProtectedResourceMetadataPath + "/mcp" })
        {
            using var response = await _h.RestAsync(null, HttpMethod.Get, path);
            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            json.GetProperty("resource").GetString().ShouldEndWith("/mcp");
            json.GetProperty("authorization_servers").GetArrayLength().ShouldBe(1);
            json.GetProperty("scopes_supported").EnumerateArray().Select(s => s.GetString()).ShouldBe([AiConnectorConsts.Scope, "offline_access"], ignoreOrder: true);
        }

        foreach (var path in new[] { "/.well-known/openid-configuration", "/.well-known/oauth-authorization-server" })
        {
            using var response = await _h.RestAsync(null, HttpMethod.Get, path);
            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
            json.GetProperty("registration_endpoint").GetString().ShouldEndWith("/connect/register", Case.Sensitive, path);
            json.GetProperty("token_endpoint_auth_methods_supported").EnumerateArray().Select(s => s.GetString()).ShouldContain("none", path);
        }
    }

    [Fact]
    public async Task A_connector_token_carries_the_user_id_and_the_tenant_and_no_other_persons_data()
    {
        var (user, token) = await ConnectedAsync();
        var claims = ClaimsOf(token.AccessToken!);
        claims.Single(c => c.Type == AbpClaimTypes.UserId || c.Type == "sub").Value.ShouldBe(user.Id.ToString());
        claims.Single(c => c.Type == AbpClaimTypes.TenantId).Value.ShouldBe(_h.TenantA.ToString());
        claims.Where(c => c.Type == OpenIddictConstants.Claims.Audience).Select(c => c.Value).ShouldBe([AiConnectorConsts.Resource], "its audience is the connector alone");
        foreach (var type in new[] { "email", "name", "phone_number", "preferred_username", "role", "unique_name", "given_name", "family_name", "phone_number_verified", "email_verified", "sid" })
        {
            claims.ShouldNotContain(c => c.Type == type, type);
        }

        token.AccessToken!.ShouldNotContain(user.Email);
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(Pad(token.AccessToken!.Split('.')[1])));
        payload.ShouldNotContain(user.Email);
    }

    private static string Pad(string base64Url)
    {
        var text = base64Url.Replace('-', '+').Replace('_', '/');
        return text.PadRight(text.Length + (4 - text.Length % 4) % 4, '=');
    }
}

/// <summary>
/// Design 74 "Refusals on /mcp and connect screen" (risk class Critical), each check refused separately on the real host, each with 403 and no data, and on the screen with no code: host user, impersonation, either
/// feature off, not accepted, no <c>Use</c>, user deactivated, connection revoked; tenant deactivated; refresh refused after revoke, withdraw and deactivation. Signing out in the browser leaves the connection working.
/// </summary>
[Collection(HostIntegrationCollection.Name)]
public class McpRefusalHostTests
{
    private readonly AiConnectorHarness _h;

    public McpRefusalHostTests(HostIntegrationFixture host)
    {
        _h = new AiConnectorHarness(host);
    }

    private Guid Tenant => _h.TenantA;

    private async Task<(TestUser User, TokenAnswer Token)> ConnectedAsync(Guid? tenant = null)
    {
        await _h.ReadyAsync(tenant ?? Tenant);
        var user = await _h.CreateUserAsync(tenant ?? Tenant, AllToolPermissions);
        return (user, await _h.ConnectAsync(user));
    }

    private async Task ShouldBeServedAsync(TokenAnswer token) => (await _h.ListToolsAsync(token.AccessToken!)).ShouldNotBeEmpty();

    private async Task ShouldBeRefusedOnMcpAsync(string token, string reason)
    {
        using var list = await _h.PostMcpAsync(token, "tools/list");
        await ShouldBeRefusedAsync(list, reason);
        using var call = await _h.PostMcpAsync(token, "tools/call", new Dictionary<string, object?> { ["name"] = ToolNames.SearchOrders, ["arguments"] = new Dictionary<string, object?>() });
        await ShouldBeRefusedAsync(call, reason);
    }

    /// <summary>The screen explains the refusal in place of Allow and offers only the way back, and no code leaves.</summary>
    private static async Task ShouldRefuseOnScreenAsync(Browser browser, string refusal)
    {
        var request = ClaudeRequest(NewVerifier());
        using var screen = await browser.GetAsync(request.Url);
        var html = await screen.Content.ReadAsStringAsync();
        screen.StatusCode.ShouldBe(HttpStatusCode.OK, html);
        html.ShouldContain("data-refusal=\"" + refusal + "\"", Case.Sensitive);
        html.ShouldNotContain("name=\"allow\"", Case.Sensitive, "the refusal replaces Allow");
        html.ShouldContain("name=\"deny\"", Case.Sensitive);

        /* Posting Allow anyway runs the checks again and issues nothing. */
        using var forced = await browser.SubmitConnectFormAsync(html, "allow");
        var forcedHtml = await forced.Content.ReadAsStringAsync();
        forced.StatusCode.ShouldBe(HttpStatusCode.OK, forcedHtml);
        forcedHtml.ShouldContain("data-refusal=\"" + refusal + "\"", Case.Sensitive);
        forced.Headers.Location.ShouldBeNull();

        /* Back to the tool is the same as Deny. */
        using var back = await browser.SubmitConnectFormAsync(html, "deny");
        back.StatusCode.ShouldBe(HttpStatusCode.Found);
        QueryValue(back.Headers.Location, "error").ShouldBe("access_denied");
        QueryValue(back.Headers.Location, "code").ShouldBeNull();
    }

    /* ---------- Check 1 ---------- */

    [Fact]
    public async Task A_host_user_is_refused_on_mcp_and_on_the_screen()
    {
        var host = await _h.CreateUserAsync(null);
        await ShouldBeRefusedOnMcpAsync(_h.MintToken(host.Id, null), nameof(AiConnectorRefusal.HostUser));
        await ShouldRefuseOnScreenAsync(await _h.SignedInBrowserAsync(host), nameof(AiConnectorRefusal.HostUser));
    }

    [Fact]
    public async Task A_host_session_asking_for_a_tenant_by_cookie_or_header_gets_no_further()
    {
        var host = await _h.CreateUserAsync(null);
        var token = _h.MintToken(host.Id, null);
        using var response = await _h.PostMcpAsync(token, "tools/list", null, request => request.Headers.Add("__tenant", Tenant.ToString()));
        await ShouldBeRefusedAsync(response, nameof(AiConnectorRefusal.HostUser));
    }

    /* ---------- Check 2 ---------- */

    [Fact]
    public async Task An_impersonated_token_is_refused_on_mcp_though_the_token_is_otherwise_good()
    {
        var (user, token) = await ConnectedAsync();
        var impersonator = new Claim(AbpClaimTypes.ImpersonatorUserId, Guid.NewGuid().ToString());
        var minted = _h.MintToken(user.Id, Tenant, authorizationId: AuthorizationIdOf(token.AccessToken!), extra: impersonator);
        await ShouldBeRefusedOnMcpAsync(minted, nameof(AiConnectorRefusal.Impersonation));

        /* The control: the same token without the claim passes every check. */
        (await _h.ListToolsAsync(_h.MintToken(user.Id, Tenant, authorizationId: AuthorizationIdOf(token.AccessToken!)))).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task An_impersonated_session_is_refused_on_the_connect_screen()
    {
        await _h.ReadyAsync(Tenant);
        var user = await _h.CreateUserAsync(Tenant, AllToolPermissions);
        var browser = await _h.SignedInBrowserAsync(user, new Claim(AbpClaimTypes.ImpersonatorUserId, Guid.NewGuid().ToString()), new Claim(AbpClaimTypes.ImpersonatorUserName, "support"));
        await ShouldRefuseOnScreenAsync(browser, nameof(AiConnectorRefusal.Impersonation));
    }

    /* ---------- Check 3 ---------- */

    [Theory]
    [InlineData(AppFeatures.Integrations)]
    [InlineData(AppFeatures.IntegrationsApp.AiConnector)]
    public async Task Either_feature_off_refuses_on_mcp_and_the_screen_and_on_again_resumes(string feature)
    {
        var (user, token) = await ConnectedAsync();
        var browser = await _h.SignedInBrowserAsync(user);
        await _h.WithFeatureOffAsync(Tenant, feature, async () =>
        {
            await ShouldBeRefusedOnMcpAsync(token.AccessToken!, nameof(AiConnectorRefusal.FeatureOff));
            await ShouldRefuseOnScreenAsync(browser, nameof(AiConnectorRefusal.FeatureOff));
        });
        await ShouldBeServedAsync(token);
    }

    [Fact]
    public async Task Not_accepted_refuses_on_mcp_and_the_screen_and_accepting_resumes()
    {
        var (user, token) = await ConnectedAsync();
        var browser = await _h.SignedInBrowserAsync(user);
        await _h.ClearAcceptedAsync(Tenant);
        try
        {
            await ShouldBeRefusedOnMcpAsync(token.AccessToken!, nameof(AiConnectorRefusal.NotAccepted));
            await ShouldRefuseOnScreenAsync(browser, nameof(AiConnectorRefusal.NotAccepted));
        }
        finally
        {
            await _h.SetAcceptedAsync(Tenant);
        }

        await ShouldBeServedAsync(token);
    }

    /* ---------- Check 4 ---------- */

    [Fact]
    public async Task Without_Use_it_refuses_on_mcp_and_the_screen_and_a_new_grant_resumes()
    {
        var (user, token) = await ConnectedAsync();
        var browser = await _h.SignedInBrowserAsync(user);
        await _h.SetPermissionAsync(user, IntegrationsPermissions.AiConnector.Use, false);
        await ShouldBeRefusedOnMcpAsync(token.AccessToken!, nameof(AiConnectorRefusal.PermissionMissing));
        await ShouldRefuseOnScreenAsync(browser, nameof(AiConnectorRefusal.PermissionMissing));
        await _h.SetPermissionAsync(user, IntegrationsPermissions.AiConnector.Use, true);
        await ShouldBeServedAsync(token);
    }

    /* ---------- Check 5 ---------- */

    [Fact]
    public async Task A_deactivated_user_is_refused_at_once_on_mcp_and_the_screen_reactivating_resumes()
    {
        var (user, token) = await ConnectedAsync();
        var browser = await _h.SignedInBrowserAsync(user);
        await ShouldBeServedAsync(token);
        await _h.SetUserActiveAsync(user, false);
        await ShouldBeRefusedOnMcpAsync(token.AccessToken!, nameof(AiConnectorRefusal.UserInactive));
        await ShouldRefuseOnScreenAsync(browser, nameof(AiConnectorRefusal.UserInactive));
        await _h.SetUserActiveAsync(user, true);
        await ShouldBeServedAsync(token);
    }

    /* ---------- Check 6 ---------- */

    [Fact]
    public async Task A_revoked_connection_is_refused_at_once_and_its_refresh_token_stops()
    {
        var (user, token) = await ConnectedAsync();
        await ShouldBeServedAsync(token);
        var rest = await _h.BrowserTokenAsync(user);
        var authorization = AuthorizationIdOf(token.AccessToken!);
        var rowId = await _h.InScopeAsync(Tenant, async services => (await services.GetRequiredService<IRepository<AiConnection, Guid>>().FirstAsync(c => c.AuthorizationId == authorization)).Id);

        using (var revoke = await _h.RestAsync(rest, HttpMethod.Post, $"/api/integrations/ai-connector/connections/{rowId}/revoke"))
        {
            revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent, await revoke.Content.ReadAsStringAsync());
        }

        await ShouldBeRefusedOnMcpAsync(token.AccessToken!, nameof(AiConnectorRefusal.ConnectionRevoked));

        var refreshed = await _h.RefreshAsync(ClaudeClient, token.RefreshToken!);
        refreshed.Status.ShouldBe(HttpStatusCode.BadRequest, refreshed.Body);
        refreshed.AccessToken.ShouldBeNull();
    }

    [Fact]
    public async Task A_token_with_no_connection_row_is_refused()
    {
        await _h.ReadyAsync(Tenant);
        var user = await _h.CreateUserAsync(Tenant, AllToolPermissions);
        await ShouldBeRefusedOnMcpAsync(_h.MintToken(user.Id, Tenant), nameof(AiConnectorRefusal.ConnectionRevoked));
    }

    [Fact]
    public async Task A_connection_of_another_user_does_not_serve_this_user()
    {
        var (_, token) = await ConnectedAsync();
        var other = await _h.CreateUserAsync(Tenant, AllToolPermissions);
        await ShouldBeRefusedOnMcpAsync(_h.MintToken(other.Id, Tenant, authorizationId: AuthorizationIdOf(token.AccessToken!)), nameof(AiConnectorRefusal.ConnectionRevoked));
    }

    /* ---------- Tenant gone ---------- */

    /// <summary>
    /// The design's "tenant deactivated": ABP's open-source tenant has no activation state and this codebase adds none, so the one way a tenant stops is being deleted (the Tenants app).
    /// With only the record deleted (a shared-tier tenant has nothing else to delete), <c>/mcp</c> refuses and returns no data; the token endpoint reads no tenant state, so a refresh still
    /// issues a token (design 74 "Build notes"), and that token reaches nothing.
    /// </summary>
    [Fact]
    public async Task A_deleted_tenants_token_is_refused_on_mcp_with_no_data_while_a_refresh_still_issues_a_token_that_reaches_nothing()
    {
        var tenant = await _h.NewTenantAsync();
        var (_, token) = await ConnectedAsync(tenant);
        await ShouldBeServedAsync(token);

        await _h.InScopeAsync(null, services => services.GetRequiredService<ITenantRepository>().DeleteAsync(tenant, autoSave: true));

        await ShouldHoldNothingOnMcpAsync(token.AccessToken!);

        var refreshed = await _h.RefreshAsync(ClaudeClient, token.RefreshToken!);
        refreshed.Status.ShouldBe(HttpStatusCode.OK, refreshed.Body);
        refreshed.AccessToken.ShouldNotBeNullOrEmpty(refreshed.Body);
        await ShouldHoldNothingOnMcpAsync(refreshed.AccessToken!);
    }

    /// <summary>
    /// What a real deletion in the Tenants app ends in for a tenant with its own database (<c>TenantAdministrationAppService.DeleteAsync</c> and <c>TenantDatabaseDropJob</c>): the record soft-deleted and
    /// the database gone. A token still in a tool's hands then gets no tenant data on <c>/mcp</c> or at the token endpoint, and no answer says how the host failed.
    /// </summary>
    [Fact]
    public async Task A_deleted_tenants_token_with_the_database_dropped_gets_no_data_and_no_detail_on_mcp_or_at_the_token_endpoint()
    {
        var (tenant, database) = await _h.NewTenantWithOwnDatabaseAsync();
        try
        {
            const string relation = "Dropped tenant relation";
            await _h.InScopeAsync(tenant, services => services.GetRequiredService<IRepository<OpenTms.MasterData.Relations.Relation, Guid>>()
                .InsertAsync(new OpenTms.MasterData.Relations.Relation(Guid.NewGuid(), tenant, "DROPPED", relation, OpenTms.MasterData.Relations.RelationRoles.Customer)));
            var (user, token) = await ConnectedAsync(tenant);
            var served = await _h.CallToolAsync(token.AccessToken!, ToolNames.SearchRelations);
            served.IsError.ShouldBeFalse(served.Text);
            served.Text.ShouldContain(relation, Case.Sensitive, "the control: this token did reach this tenant's data");

            await _h.DeleteTenantAndDropDatabaseAsync(tenant, database);

            await ShouldHoldNothingOnMcpAsync(token.AccessToken!, relation, database, user.Email);

            var refreshed = await _h.RefreshAsync(ClaudeClient, token.RefreshToken!);
            ShouldStateNoDetail(refreshed.Body, relation, database, user.Email);
            refreshed.Status.ShouldBe(HttpStatusCode.BadRequest, refreshed.Body);
            refreshed.AccessToken.ShouldBeNull();
            JsonDocument.Parse(refreshed.Body).RootElement.GetProperty("error").GetString().ShouldBe("invalid_grant", "unlike a deleted record alone: with the database gone no user can be read to renew for");
        }
        finally
        {
            await _h.DropDatabaseAsync(database);
        }
    }

    private async Task ShouldHoldNothingOnMcpAsync(string token, params string[] secrets)
    {
        foreach (var method in new[] { "tools/list", "tools/call" })
        {
            using var response = await _h.PostMcpAsync(token, method, method == "tools/list" ? null : new Dictionary<string, object?> { ["name"] = ToolNames.SearchRelations, ["arguments"] = new Dictionary<string, object?>() });
            var body = await response.Content.ReadAsStringAsync();
            /* ABP's tenant resolution answers before any check of the connector runs: its own page, naming only the id the token itself states, not the connector's 403. */
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, method + ": " + body);
            body.ShouldContain("Tenant not found!", Case.Sensitive, method);
            body.ShouldNotContain("\"rows\"");
            body.ShouldNotContain("\"tools\"");
            ShouldStateNoDetail(body, secrets);
        }
    }

    private static void ShouldStateNoDetail(string body, params string[] secrets)
    {
        foreach (var detail in secrets.Concat(["Npgsql", "Exception", "PostgreSQL", "3D000", "Host=", "Database=", "Password", "   at "]))
        {
            body.ShouldNotContain(detail, Case.Insensitive, "an answer states no detail of the failure");
        }
    }

    /* ---------- Refresh ---------- */

    [Fact]
    public async Task A_refresh_token_renews_and_rolls_on_while_nothing_is_wrong()
    {
        var (_, token) = await ConnectedAsync();
        var first = await _h.RefreshAsync(ClaudeClient, token.RefreshToken!);
        first.Status.ShouldBe(HttpStatusCode.OK, first.Body);
        first.RefreshToken.ShouldNotBeNullOrEmpty();
        await ShouldBeServedAsync(first);
        var second = await _h.RefreshAsync(ClaudeClient, first.RefreshToken!);
        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);
        await ShouldBeServedAsync(second);
    }

    [Fact]
    public async Task A_refresh_is_refused_after_a_withdrawal_and_mcp_refuses_every_connection_of_the_tenant()
    {
        var tenant = _h.TenantC;
        await _h.ReadyAsync(tenant);
        var manager = await _h.CreateUserAsync(tenant, IntegrationsPermissions.AiConnector.Manage);
        var user = await _h.CreateUserAsync(tenant, AllToolPermissions);
        var token = await _h.ConnectAsync(user);
        await ShouldBeServedAsync(token);
        var rest = await _h.BrowserTokenAsync(manager);

        using (var withdraw = await _h.RestAsync(rest, HttpMethod.Post, "/api/integrations/ai-connector/withdraw"))
        {
            var body = await withdraw.Content.ReadAsStringAsync();
            withdraw.StatusCode.ShouldBe(HttpStatusCode.OK, body);
            JsonDocument.Parse(body).RootElement.GetProperty("isAccepted").GetBoolean().ShouldBeFalse();
        }

        await ShouldBeRefusedOnMcpAsync(token.AccessToken!, nameof(AiConnectorRefusal.NotAccepted));
        var refreshed = await _h.RefreshAsync(ClaudeClient, token.RefreshToken!);
        refreshed.Status.ShouldBe(HttpStatusCode.BadRequest, refreshed.Body);
        refreshed.AccessToken.ShouldBeNull();

        /* Accepting again does not bring a withdrawn connection back. */
        await _h.SetAcceptedAsync(tenant);
        await ShouldBeRefusedOnMcpAsync(token.AccessToken!, nameof(AiConnectorRefusal.ConnectionRevoked));
    }

    [Fact]
    public async Task A_refresh_is_refused_after_deactivation_and_works_again_after_reactivation()
    {
        var (user, token) = await ConnectedAsync();
        await _h.SetUserActiveAsync(user, false);

        var refused = await _h.RefreshAsync(ClaudeClient, token.RefreshToken!);
        refused.AccessToken.ShouldBeNull(refused.Body);
        refused.Status.ShouldBe(HttpStatusCode.BadRequest, refused.Body);

        await _h.SetUserActiveAsync(user, true);
        await ShouldBeServedAsync(token);
    }

    /* ---------- Signing out ---------- */

    [Fact]
    public async Task Signing_out_in_the_browser_leaves_the_connection_working()
    {
        await _h.ReadyAsync(Tenant);
        var user = await _h.CreateUserAsync(Tenant, AllToolPermissions);
        var browser = await _h.SignedInBrowserAsync(user);
        var request = ClaudeRequest(NewVerifier());
        using var allowed = await PressAsync(browser, request, "allow");
        var token = await _h.RedeemCodeAsync(request, QueryValue(allowed.Headers.Location, "code")!);
        token.Status.ShouldBe(HttpStatusCode.OK, token.Body);

        /* The real sign-out. */
        using (var signOut = await browser.GetAsync("/connect/endsession"))
        {
            signOut.StatusCode.ShouldBe(HttpStatusCode.OK, await signOut.Content.ReadAsStringAsync());
        }

        using (var logout = await browser.GetAsync("/Account/Logout"))
        {
            logout.StatusCode.ShouldBe(HttpStatusCode.Found, await logout.Content.ReadAsStringAsync());
        }

        /* The sign-out did happen: the session no longer reaches the connect screen. */
        using (var after = await browser.GetAsync(ClaudeRequest(NewVerifier()).Url))
        {
            after.StatusCode.ShouldBe(HttpStatusCode.Found, await after.Content.ReadAsStringAsync());
            after.Headers.Location!.OriginalString.ShouldContain("/Account/Login", Case.Insensitive);
        }

        ClaimsOf(token.AccessToken!).ShouldNotContain(c => c.Type == "sid", "a connector token is tied to no browser session");
        await ShouldBeServedAsync(token);
        var refreshed = await _h.RefreshAsync(ClaudeClient, token.RefreshToken!);
        refreshed.Status.ShouldBe(HttpStatusCode.OK, refreshed.Body);
        await ShouldBeServedAsync(refreshed);
    }

    /* ---------- The order of the checks ---------- */

    [Fact]
    public async Task The_first_failing_check_names_the_refusal()
    {
        var (user, token) = await ConnectedAsync();
        await _h.SetUserActiveAsync(user, false);
        await _h.SetPermissionAsync(user, IntegrationsPermissions.AiConnector.Use, false);
        try
        {
            await ShouldBeRefusedOnMcpAsync(token.AccessToken!, nameof(AiConnectorRefusal.PermissionMissing));
            await _h.WithFeatureOffAsync(Tenant, AppFeatures.IntegrationsApp.AiConnector, () => ShouldBeRefusedOnMcpAsync(token.AccessToken!, nameof(AiConnectorRefusal.FeatureOff)));
        }
        finally
        {
            await _h.SetUserActiveAsync(user, true);
            await _h.SetPermissionAsync(user, IntegrationsPermissions.AiConnector.Use, true);
        }
    }
}
