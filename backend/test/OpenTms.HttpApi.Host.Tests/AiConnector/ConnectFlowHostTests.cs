using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenTms.HostIntegration;
using OpenTms.Integrations.AiConnector;
using OpenTms.OpenIddict;
using OpenTms.Integrations.Permissions;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;
using static OpenTms.AiConnector.AiConnectorHarness;

namespace OpenTms.AiConnector;

/// <summary>
/// Design 74 "Connect flow" (risk class Critical), through the real authorize and token endpoints: an existing session shows its organisation and issues no code without Allow; Deny issues none; a second connect
/// still shows the screen and reuses the same connection; PKCE is required; only the connector's scopes are grantable; the advertised scopes yield a refresh token. Also Gemini's flow with its secret, and the
/// real sign-in page leading to the screen.
/// </summary>
[Collection(HostIntegrationCollection.Name)]
public class ConnectFlowHostTests
{
    private readonly AiConnectorHarness _h;

    public ConnectFlowHostTests(HostIntegrationFixture host)
    {
        _h = new AiConnectorHarness(host);
    }

    private Guid Tenant => _h.TenantA;

    private async Task<TestUser> UserAsync()
    {
        await _h.ReadyAsync(Tenant);
        return await _h.CreateUserAsync(Tenant, AllToolPermissions);
    }

    private Task<List<AiConnection>> ConnectionsOfAsync(TestUser user, Guid? tenant = null)
        => _h.InScopeAsync(tenant ?? Tenant, services => services.GetRequiredService<IRepository<AiConnection, Guid>>().GetListAsync(c => c.UserId == user.Id));

    private static void ShouldHaveNoCode(HttpResponseMessage response)
        => QueryValue(response.Headers.Location, "code").ShouldBeNull(response.Headers.Location?.ToString());

    [Fact]
    public async Task An_existing_session_shows_its_organisation_and_user_and_issues_nothing_without_Allow()
    {
        var user = await UserAsync();
        var browser = await _h.SignedInBrowserAsync(user);

        using var screen = await browser.GetAsync(ClaudeRequest(NewVerifier()).Url);
        var html = await screen.Content.ReadAsStringAsync();

        screen.StatusCode.ShouldBe(HttpStatusCode.OK, html);
        html.ShouldContain(HostIntegrationFixture.TenantAName, Case.Sensitive, "the screen names the organisation of the session");
        html.ShouldContain(user.Email, Case.Insensitive, "and the user");
        html.ShouldContain("Claude", Case.Sensitive, "and the tool, from our configuration");
        html.ShouldContain("name=\"allow\"", Case.Sensitive);
        html.ShouldContain("name=\"deny\"", Case.Sensitive);
        html.ShouldNotContain("data-refusal", Case.Sensitive);
        screen.Headers.Location.ShouldBeNull();
        screen.Headers.GetValues("X-Frame-Options").ShouldContain("DENY");
        string.Join(";", screen.Headers.GetValues("Content-Security-Policy")).ShouldContain("frame-ancestors 'none'");
        (await ConnectionsOfAsync(user)).ShouldBeEmpty("nothing is created before Allow");
    }

    [Fact]
    public async Task Allow_issues_a_code_for_the_same_state_and_a_token_for_that_user_in_that_tenant()
    {
        var user = await UserAsync();
        var browser = await _h.SignedInBrowserAsync(user);
        var request = ClaudeRequest(NewVerifier());

        using var allowed = await PressAsync(browser, request, "allow");

        allowed.StatusCode.ShouldBe(HttpStatusCode.Found, await allowed.Content.ReadAsStringAsync());
        allowed.Headers.Location!.ToString().ShouldStartWith(HostIntegrationFixture.ClaudeRedirectUri);
        QueryValue(allowed.Headers.Location, "state").ShouldBe(request.State);
        var code = QueryValue(allowed.Headers.Location, "code").ShouldNotBeNull();

        var connection = (await ConnectionsOfAsync(user)).ShouldHaveSingleItem("committed before the code leaves");
        connection.ClientId.ShouldBe(ClaudeClient);
        connection.TenantId.ShouldBe(Tenant);
        connection.RevokedAt.ShouldBeNull();

        var token = await _h.RedeemCodeAsync(request, code);
        token.Status.ShouldBe(HttpStatusCode.OK, token.Body);
        AuthorizationIdOf(token.AccessToken!).ShouldBe(connection.AuthorizationId);
        (await _h.ListToolsAsync(token.AccessToken!)).Count.ShouldBeGreaterThan(0);

        /* A code is good once. */
        (await _h.RedeemCodeAsync(request, code)).AccessToken.ShouldBeNull("an authorization code is redeemed once");
    }

    [Fact]
    public async Task Deny_issues_no_code_and_creates_nothing()
    {
        var user = await UserAsync();
        var browser = await _h.SignedInBrowserAsync(user);
        var request = ClaudeRequest(NewVerifier());

        using var denied = await PressAsync(browser, request, "deny");

        denied.StatusCode.ShouldBe(HttpStatusCode.Found, await denied.Content.ReadAsStringAsync());
        denied.Headers.Location!.ToString().ShouldStartWith(HostIntegrationFixture.ClaudeRedirectUri);
        QueryValue(denied.Headers.Location, "error").ShouldBe("access_denied");
        QueryValue(denied.Headers.Location, "state").ShouldBe(request.State);
        ShouldHaveNoCode(denied);
        (await ConnectionsOfAsync(user)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_second_connect_of_the_same_user_and_tool_still_shows_the_screen_and_reuses_the_connection()
    {
        var user = await UserAsync();
        var first = await _h.ConnectAsync(user);
        var connection = (await ConnectionsOfAsync(user)).ShouldHaveSingleItem();

        /* With a valid permanent authorization ABP itself would skip consent: the screen must appear anyway. */
        var browser = await _h.SignedInBrowserAsync(user);
        var request = ClaudeRequest(NewVerifier());
        using var screen = await browser.GetAsync(request.Url);
        var html = await screen.Content.ReadAsStringAsync();
        screen.StatusCode.ShouldBe(HttpStatusCode.OK, html);
        html.ShouldContain("name=\"allow\"", Case.Sensitive);

        using var allowed = await browser.SubmitConnectFormAsync(html, "allow");
        allowed.StatusCode.ShouldBe(HttpStatusCode.Found, await allowed.Content.ReadAsStringAsync());
        var second = await _h.RedeemCodeAsync(request, QueryValue(allowed.Headers.Location, "code")!);
        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);

        var again = (await ConnectionsOfAsync(user)).ShouldHaveSingleItem("no unique-index failure and no second row");
        again.Id.ShouldBe(connection.Id);
        again.AuthorizationId.ShouldBe(connection.AuthorizationId);
        AuthorizationIdOf(second.AccessToken!).ShouldBe(connection.AuthorizationId);
        (await _h.ListToolsAsync(first.AccessToken!)).ShouldNotBeEmpty("the first token still works");
        (await _h.ListToolsAsync(second.AccessToken!)).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Connecting_again_after_switching_off_gives_a_live_connection()
    {
        var user = await UserAsync();
        var first = await _h.ConnectAsync(user);
        var row = (await ConnectionsOfAsync(user)).Single();
        var rest = await _h.BrowserTokenAsync(user);
        using (var revoke = await _h.RestAsync(rest, HttpMethod.Post, $"/api/integrations/ai-connector/connections/{row.Id}/revoke"))
        {
            revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var second = await _h.ConnectAsync(user);
        (await _h.ListToolsAsync(second.AccessToken!)).ShouldNotBeEmpty();
        using var stale = await _h.PostMcpAsync(first.AccessToken, "tools/list");
        await ShouldBeRefusedAsync(stale, nameof(AiConnectorRefusal.ConnectionRevoked));
    }

    /* ---------- PKCE ---------- */

    [Fact]
    public async Task A_public_client_without_a_code_challenge_gets_no_code()
    {
        var user = await UserAsync();
        var browser = await _h.SignedInBrowserAsync(user);
        var request = new AuthorizeRequest(ClaudeClient, HostIntegrationFixture.ClaudeRedirectUri, ConnectorScopes, null);

        using var response = await browser.GetAsync(request.Url);
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain("name=\"allow\"", Case.Sensitive, "no screen to allow on");
        ShouldHaveNoCode(response);
        response.Headers.Location.ShouldBeNull("OpenIddict answers on its own page, no redirect");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        body.ShouldContain("error:invalid_request", Case.Sensitive);
        body.ShouldContain("ID2054", Case.Sensitive, "the code_challenge is what is missing");
    }

    [Fact]
    public async Task The_plain_challenge_method_gets_no_code()
    {
        var user = await UserAsync();
        var browser = await _h.SignedInBrowserAsync(user);
        var request = new AuthorizeRequest(ClaudeClient, HostIntegrationFixture.ClaudeRedirectUri, ConnectorScopes, NewVerifier(), ChallengeMethod: "plain");

        using var response = await browser.GetAsync(request.Url);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("name=\"allow\"", Case.Sensitive);
        ShouldHaveNoCode(response);
        response.StatusCode.ShouldBe(HttpStatusCode.Found, body);
        QueryValue(response.Headers.Location, "error").ShouldBe("invalid_request", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task A_wrong_code_verifier_gets_no_token()
    {
        var user = await UserAsync();
        var browser = await _h.SignedInBrowserAsync(user);
        var request = ClaudeRequest(NewVerifier());
        using var allowed = await PressAsync(browser, request, "allow");
        var code = QueryValue(allowed.Headers.Location, "code")!;

        var token = await _h.RedeemCodeAsync(request, code, verifier: NewVerifier());
        token.Status.ShouldBe(HttpStatusCode.BadRequest, token.Body);
        token.AccessToken.ShouldBeNull();
        JsonDocument.Parse(token.Body).RootElement.GetProperty("error").GetString().ShouldBe("invalid_grant");
    }

    /* ---------- Scopes ---------- */

    [Theory]
    [InlineData("OpenTms")]
    [InlineData("OpenTmsMcp offline_access OpenTms")]
    [InlineData("OpenTmsMcp offline_access email")]
    public async Task A_scope_the_client_has_no_permission_for_is_refused(string scope)
    {
        var user = await UserAsync();
        var browser = await _h.SignedInBrowserAsync(user);
        using var response = await browser.GetAsync(ClaudeRequest(NewVerifier(), scope: scope).Url);
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain("name=\"allow\"", Case.Sensitive, scope);
        ShouldHaveNoCode(response);
        response.Headers.Location.ShouldBeNull("OpenIddict answers on its own page, no redirect: " + scope);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, scope + ": " + body);
        body.ShouldContain("error:invalid_request", Case.Sensitive, scope);
        body.ShouldContain("ID2051", Case.Sensitive, "this client is not allowed the scope: " + scope);
    }

    [Theory]
    [InlineData("OpenTmsMcp offline_access")]
    [InlineData("OpenTmsMcp")]
    [InlineData("offline_access")]
    public async Task Whatever_part_of_its_scopes_a_tool_asks_for_it_gets_exactly_the_connectors_with_a_refresh_token(string scope)
    {
        var user = await UserAsync();
        var token = await _h.ConnectAsync(user, ClaudeRequest(NewVerifier(), scope: scope));

        token.RefreshToken.ShouldNotBeNullOrEmpty("without one the connection drops: " + scope);
        var granted = (token.Scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        granted.ShouldBe([AiConnectorConsts.Scope, OpenIddictConstants.Scopes.OfflineAccess], ignoreOrder: true);
        ClaimsOf(token.AccessToken!).Where(c => c.Type == OpenIddictConstants.Claims.Audience).Select(c => c.Value).ShouldBe([AiConnectorConsts.Resource]);
        (await _h.ListToolsAsync(token.AccessToken!)).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Openid_which_OpenIddict_asks_no_permission_for_is_not_granted_either()
    {
        /* OpenIddict lets openid through to the screen; the token still holds the connector's two scopes and no identity token. */
        var user = await UserAsync();
        var token = await _h.ConnectAsync(user, ClaudeRequest(NewVerifier(), scope: "OpenTmsMcp offline_access openid"));
        (token.Scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).ShouldBe([AiConnectorConsts.Scope, OpenIddictConstants.Scopes.OfflineAccess], ignoreOrder: true);
        JsonDocument.Parse(token.Body).RootElement.TryGetProperty("id_token", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task The_scopes_the_metadata_advertises_yield_a_refresh_token_end_to_end()
    {
        using var metadata = await _h.RestAsync(null, HttpMethod.Get, AiConnectorTokenGate.ProtectedResourceMetadataPath);
        var advertised = string.Join(" ", JsonDocument.Parse(await metadata.Content.ReadAsStringAsync()).RootElement.GetProperty("scopes_supported").EnumerateArray().Select(s => s.GetString()));
        var user = await UserAsync();

        var token = await _h.ConnectAsync(user, ClaudeRequest(NewVerifier(), scope: advertised));

        token.RefreshToken.ShouldNotBeNullOrEmpty();
    }

    /* ---------- The resource parameter ---------- */

    [Fact]
    public async Task The_connectors_own_resource_is_accepted_and_any_other_refused()
    {
        var user = await UserAsync();
        var own = (_h.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["App:SelfUrl"] ?? string.Empty).TrimEnd('/') + "/mcp";
        var token = await _h.ConnectAsync(user, ClaudeRequest(NewVerifier(), resource: own));
        (await _h.ListToolsAsync(token.AccessToken!)).ShouldNotBeEmpty();

        var browser = await _h.SignedInBrowserAsync(user);
        using var other = await browser.GetAsync(ClaudeRequest(NewVerifier(), resource: "https://somebody-else.example/mcp").Url);
        var body = await other.Content.ReadAsStringAsync();
        body.ShouldNotContain("name=\"allow\"", Case.Sensitive);
        ShouldHaveNoCode(other);
    }

    /* ---------- prompt ---------- */

    [Fact]
    public async Task Prompt_none_never_shows_a_screen_or_issues_a_code()
    {
        var user = await UserAsync();
        var browser = await _h.SignedInBrowserAsync(user);
        using var withSession = await browser.GetAsync(ClaudeRequest(NewVerifier(), prompt: "none").Url);
        withSession.StatusCode.ShouldBe(HttpStatusCode.Found, await withSession.Content.ReadAsStringAsync());
        QueryValue(withSession.Headers.Location, "error").ShouldBe("consent_required");
        ShouldHaveNoCode(withSession);

        using var withoutSession = await _h.NewBrowser().GetAsync(ClaudeRequest(NewVerifier(), prompt: "none").Url);
        withoutSession.StatusCode.ShouldBe(HttpStatusCode.Found, await withoutSession.Content.ReadAsStringAsync());
        QueryValue(withoutSession.Headers.Location, "error").ShouldBe("login_required");
        ShouldHaveNoCode(withoutSession);
    }

    /* ---------- Clients ---------- */

    [Fact]
    public async Task An_unknown_client_or_a_redirect_uri_it_does_not_have_gets_nowhere()
    {
        var user = await UserAsync();
        var browser = await _h.SignedInBrowserAsync(user);

        using var client = await browser.GetAsync(new AuthorizeRequest("OpenTms_Mcp_Nobody", HostIntegrationFixture.ClaudeRedirectUri, ConnectorScopes, NewVerifier()).Url);
        client.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await client.Content.ReadAsStringAsync());
        client.Headers.Location.ShouldBeNull();

        using var uri = await browser.GetAsync(new AuthorizeRequest(ClaudeClient, "https://claude.ai.evil.example/api/mcp/auth_callback", ConnectorScopes, NewVerifier()).Url);
        uri.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await uri.Content.ReadAsStringAsync());
        uri.Headers.Location.ShouldBeNull("never redirect to a URI the client does not have");
    }

    /* ---------- Signing in ---------- */

    [Fact]
    public async Task Without_a_session_the_real_sign_in_page_finds_the_tenant_and_leads_to_the_screen()
    {
        var user = await UserAsync();
        var browser = _h.NewBrowser();
        var request = ClaudeRequest(NewVerifier());

        using var challenge = await browser.GetAsync(request.Url);
        challenge.StatusCode.ShouldBe(HttpStatusCode.Found, await challenge.Content.ReadAsStringAsync());
        var login = challenge.Headers.Location!.ToString();
        login.ShouldContain("/Account/Login", Case.Insensitive);
        var path = new Uri(new Uri("http://localhost"), login).PathAndQuery;

        using var page = await browser.GetAsync(path);
        var html = await page.Content.ReadAsStringAsync();
        page.StatusCode.ShouldBe(HttpStatusCode.OK, html);

        var form = Browser.HiddenInputs(html).ToList();
        form.Add(new("Email", user.Email));
        using var emailStep = await browser.PostFormAsync("/Account/Login?handler=Email", form);
        emailStep.StatusCode.ShouldBe(HttpStatusCode.Found, await emailStep.Content.ReadAsStringAsync());

        var next = new Uri(new Uri("http://localhost"), emailStep.Headers.Location!).PathAndQuery;
        using var passwordPage = await browser.GetAsync(next);
        var passwordHtml = await passwordPage.Content.ReadAsStringAsync();
        passwordPage.StatusCode.ShouldBe(HttpStatusCode.OK, passwordHtml);
        var passwordForm = Browser.HiddenInputs(passwordHtml).ToList();
        passwordForm.Add(new("Password", Password));
        using var passwordStep = await browser.PostFormAsync("/Account/Login?handler=Password", passwordForm);
        passwordStep.StatusCode.ShouldBe(HttpStatusCode.Found, await passwordStep.Content.ReadAsStringAsync());

        var back = new Uri(new Uri("http://localhost"), passwordStep.Headers.Location!).PathAndQuery;
        back.ShouldStartWith("/connect/authorize", Case.Insensitive);
        using var screen = await browser.GetAsync(back);
        var screenHtml = await screen.Content.ReadAsStringAsync();
        screen.StatusCode.ShouldBe(HttpStatusCode.OK, screenHtml);
        screenHtml.ShouldContain(HostIntegrationFixture.TenantAName, Case.Sensitive);
        screenHtml.ShouldContain("name=\"allow\"", Case.Sensitive);
    }

    [Fact]
    public async Task Use_a_different_account_signs_out_and_restarts_the_request_and_only_as_a_post_with_its_antiforgery_token()
    {
        var user = await UserAsync();
        var browser = await _h.SignedInBrowserAsync(user);
        var request = ClaudeRequest(NewVerifier());

        using var screen = await browser.GetAsync(request.Url);
        var html = await screen.Content.ReadAsStringAsync();

        /* Without the antiforgery token nothing happens, so another site cannot sign anyone out. */
        var forged = Browser.HiddenInputs(html).Where(p => p.Key != "__RequestVerificationToken").Append(new KeyValuePair<string, string>("different-account", "1"));
        using (var refused = await browser.PostFormAsync("/connect/authorize/callback", forged))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        }

        using var different = await browser.SubmitConnectFormAsync(html, "different-account");
        different.StatusCode.ShouldBe(HttpStatusCode.Found, await different.Content.ReadAsStringAsync());
        different.Headers.Location!.ToString().ShouldContain("/connect/authorize", Case.Insensitive);
        ShouldHaveNoCode(different);

        /* The restart now asks for a sign-in. */
        using var restarted = await browser.GetAsync(new Uri(new Uri("http://localhost"), different.Headers.Location!).PathAndQuery);
        restarted.StatusCode.ShouldBe(HttpStatusCode.Found);
        restarted.Headers.Location!.ToString().ShouldContain("/Account/Login", Case.Insensitive);
    }

    [Fact]
    public async Task Allow_without_the_antiforgery_token_issues_nothing()
    {
        var user = await UserAsync();
        var browser = await _h.SignedInBrowserAsync(user);
        var request = ClaudeRequest(NewVerifier());
        using var screen = await browser.GetAsync(request.Url);
        var html = await screen.Content.ReadAsStringAsync();

        var forged = Browser.HiddenInputs(html).Where(p => p.Key != "__RequestVerificationToken").Append(new KeyValuePair<string, string>("allow", "1"));
        using var response = await browser.PostFormAsync("/connect/authorize/callback", forged);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        ShouldHaveNoCode(response);
        (await ConnectionsOfAsync(user)).ShouldBeEmpty();
    }

    /* ---------- Gemini ---------- */

    private static AuthorizeRequest GeminiRequest() => new(GeminiClient, HostIntegrationFixture.GeminiRedirectUri, ConnectorScopes, null);

    [Fact]
    public async Task Gemini_connects_with_its_secret_without_pkce_and_renews_with_it()
    {
        var user = await UserAsync();
        var token = await _h.ConnectAsync(user, GeminiRequest(), HostIntegrationFixture.GeminiClientSecret);

        token.RefreshToken.ShouldNotBeNullOrEmpty();
        (await _h.ListToolsAsync(token.AccessToken!)).ShouldNotBeEmpty();
        var connection = (await ConnectionsOfAsync(user)).ShouldHaveSingleItem();
        connection.ClientId.ShouldBe(GeminiClient);

        var refused = await _h.RefreshAsync(GeminiClient, token.RefreshToken!, secret: "wrong-secret");
        refused.AccessToken.ShouldBeNull(refused.Body);
        refused.Status.ShouldNotBe(HttpStatusCode.OK);

        var refreshed = await _h.RefreshAsync(GeminiClient, token.RefreshToken!, secret: HostIntegrationFixture.GeminiClientSecret);
        refreshed.Status.ShouldBe(HttpStatusCode.OK, refreshed.Body);
        (await _h.ListToolsAsync(refreshed.AccessToken!)).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("wrong-secret")]
    [InlineData(null)]
    public async Task Gemini_gets_no_token_without_its_right_secret(string? secret)
    {
        var user = await UserAsync();
        var browser = await _h.SignedInBrowserAsync(user);
        var request = GeminiRequest();
        using var allowed = await PressAsync(browser, request, "allow");
        allowed.StatusCode.ShouldBe(HttpStatusCode.Found, await allowed.Content.ReadAsStringAsync());
        var code = QueryValue(allowed.Headers.Location, "code").ShouldNotBeNull();

        var token = await _h.RedeemCodeAsync(request, code, secret: secret);
        token.AccessToken.ShouldBeNull(token.Body);
        token.Status.ShouldNotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_screen_names_the_tool_of_the_client_from_our_configuration()
    {
        var user = await UserAsync();
        var browser = await _h.SignedInBrowserAsync(user);
        using var screen = await browser.GetAsync(GeminiRequest().Url);
        var html = await screen.Content.ReadAsStringAsync();
        screen.StatusCode.ShouldBe(HttpStatusCode.OK, html);
        html.ShouldContain("Gemini", Case.Sensitive);
    }

    [Fact]
    public async Task Clients_revoke_their_own_refresh_token_at_the_revocation_endpoint()
    {
        var user = await UserAsync();
        var token = await _h.ConnectAsync(user);
        using var client = _h.Host.CreateClient();
        using var response = await client.PostAsync("/connect/revocat", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClaudeClient,
            ["token"] = token.RefreshToken!,
            ["token_type_hint"] = "refresh_token",
        }));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        (await _h.RefreshAsync(ClaudeClient, token.RefreshToken!)).AccessToken.ShouldBeNull();
    }
}

/// <summary>
/// Design 74 "Registration" (risk class High), on the real endpoint and PostgreSQL: Claude's URIs return its fixed client and write nothing; an approved ChatGPT URI returns the same client when repeated;
/// mixed, unknown, <c>http</c> and look-alike URIs get 400 and write nothing; a confidential client is never handed out; the name comes from our configuration, never from the caller; a registered ChatGPT client connects.
/// </summary>
[Collection(HostIntegrationCollection.Name)]
public class RegistrationHostTests
{
    private const string ChatGptBase = "https://chatgpt.com/connector/oauth/";

    private readonly AiConnectorHarness _h;

    public RegistrationHostTests(HostIntegrationFixture host)
    {
        _h = new AiConnectorHarness(host);
    }

    /* Each test its own address: the limiter partitions on it, and these tests are not about it. */
    private static string NewAddress() => "198.51.100." + Random.Shared.Next(1, 254) + "-" + Guid.NewGuid().ToString("N")[..6];

    private async Task<HttpResponseMessage> RegisterAsync(string body, string? address = null)
    {
        address ??= NewAddress();
        return await _h.RestAsync(null, HttpMethod.Post, "/connect/register", null, request =>
        {
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", address);
        });
    }

    private Task<HttpResponseMessage> RegisterUrisAsync(params string[] uris)
        => RegisterAsync(JsonSerializer.Serialize(new Dictionary<string, object> { ["redirect_uris"] = uris, ["client_name"] = "Evil Corp", ["token_endpoint_auth_method"] = "client_secret_post", ["scope"] = "OpenTms admin" }));

    private async Task<long> ApplicationCountAsync()
        => await _h.InScopeAsync(null, async services => await services.GetRequiredService<IOpenIddictApplicationManager>().CountAsync(), transactional: false);

    [Theory]
    [InlineData(HostIntegrationFixture.ClaudeRedirectUri)]
    [InlineData(HostIntegrationFixture.ClaudeComRedirectUri)]
    public async Task One_of_Claudes_uris_returns_its_fixed_client_and_writes_nothing(string uri)
    {
        var before = await ApplicationCountAsync();
        using var response = await RegisterUrisAsync(uri);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("client_id").GetString().ShouldBe(ClaudeClient);
        json.GetProperty("client_name").GetString().ShouldBe("Claude", "never the caller's name");
        json.GetProperty("token_endpoint_auth_method").GetString().ShouldBe("none", "never what the caller asked for");
        json.GetProperty("scope").GetString().ShouldBe(ConnectorScopes);
        json.TryGetProperty("client_secret", out _).ShouldBeFalse();
        (await ApplicationCountAsync()).ShouldBe(before, "a tool with fixed URIs writes nothing");
    }

    [Fact]
    public async Task Both_of_Claudes_uris_at_once_are_one_tool()
    {
        using var response = await RegisterUrisAsync(HostIntegrationFixture.ClaudeRedirectUri, HostIntegrationFixture.ClaudeComRedirectUri);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        JsonDocument.Parse(body).RootElement.GetProperty("client_id").GetString().ShouldBe(ClaudeClient);
    }

    [Fact]
    public async Task An_approved_chatgpt_uri_gets_an_application_of_its_own_and_the_same_client_when_repeated()
    {
        var uri = ChatGptBase + "conn_" + Guid.NewGuid().ToString("N")[..16];
        var before = await ApplicationCountAsync();

        using var first = await RegisterUrisAsync(uri);
        var firstBody = await first.Content.ReadAsStringAsync();
        first.StatusCode.ShouldBe(HttpStatusCode.Created, firstBody);
        var firstJson = JsonDocument.Parse(firstBody).RootElement;
        var clientId = firstJson.GetProperty("client_id").GetString().ShouldNotBeNull();
        clientId.ShouldStartWith(AiConnectorConsts.ChatGptClientIdPrefix);
        firstJson.GetProperty("client_name").GetString().ShouldBe("ChatGPT");
        (await ApplicationCountAsync()).ShouldBe(before + 1);

        using var second = await RegisterUrisAsync(uri);
        var secondBody = await second.Content.ReadAsStringAsync();
        second.StatusCode.ShouldBe(HttpStatusCode.Created, secondBody);
        JsonDocument.Parse(secondBody).RootElement.GetProperty("client_id").GetString().ShouldBe(clientId);
        (await ApplicationCountAsync()).ShouldBe(before + 1, "registering again writes nothing");
    }

    [Fact]
    public async Task Two_registrations_of_one_chatgpt_uri_at_the_same_moment_give_one_client()
    {
        var uri = ChatGptBase + "conn_" + Guid.NewGuid().ToString("N")[..16];
        var before = await ApplicationCountAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RegisterUrisAsync(uri)));
        var ids = new List<string?>();
        foreach (var response in responses)
        {
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
            ids.Add(JsonDocument.Parse(body).RootElement.GetProperty("client_id").GetString());
            response.Dispose();
        }

        ids.Distinct().ShouldHaveSingleItem();
        (await ApplicationCountAsync()).ShouldBe(before + 1);
    }

    [Fact]
    public async Task A_registered_chatgpt_client_connects_and_the_connection_names_its_tool()
    {
        await _h.ReadyAsync(_h.TenantA);
        var user = await _h.CreateUserAsync(_h.TenantA, AllToolPermissions);
        var uri = ChatGptBase + "conn_" + Guid.NewGuid().ToString("N")[..16];
        using var registered = await RegisterUrisAsync(uri);
        var clientId = JsonDocument.Parse(await registered.Content.ReadAsStringAsync()).RootElement.GetProperty("client_id").GetString()!;

        var token = await _h.ConnectAsync(user, new AuthorizeRequest(clientId, uri, ConnectorScopes, NewVerifier()));

        (await _h.ListToolsAsync(token.AccessToken!)).ShouldNotBeEmpty();
        var rest = await _h.BrowserTokenAsync(user);
        using var list = await _h.RestAsync(rest, HttpMethod.Get, "/api/integrations/ai-connector/connections?StartRow=0&EndRow=10");
        var body = await list.Content.ReadAsStringAsync();
        list.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        JsonDocument.Parse(body).RootElement.GetProperty("rows").EnumerateArray().Single().GetProperty("toolName").GetString().ShouldBe("ChatGPT");
    }

    [Fact]
    public async Task Chatgpts_platform_callback_is_approved_too()
    {
        using var response = await RegisterUrisAsync("https://chatgpt.com/connector_platform_oauth_redirect");
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    public static TheoryData<string, string[]> Refused => new()
    {
        { "unknown", ["https://example.org/callback"] },
        { "http instead of https", ["http://claude.ai/api/mcp/auth_callback"] },
        { "http chatgpt", ["http://chatgpt.com/connector/oauth/abc"] },
        { "a look-alike of claude's host", ["https://claude.ai.evil.example/api/mcp/auth_callback"] },
        { "a look-alike of chatgpt's host", ["https://chatgpt.com.evil.example/connector/oauth/abc"] },
        { "another host ending in chatgpt.com", ["https://notchatgpt.com/connector/oauth/abc"] },
        { "a user part in front of chatgpt", ["https://chatgpt.com@evil.example/connector/oauth/abc"] },
        { "extra path segments on chatgpt", ["https://chatgpt.com/connector/oauth/abc/def"] },
        { "extra path segments on claude", ["https://claude.ai/api/mcp/auth_callback/extra"] },
        { "a query on chatgpt", ["https://chatgpt.com/connector/oauth/abc?x=1"] },
        { "a fragment on chatgpt", ["https://chatgpt.com/connector/oauth/abc#x"] },
        { "a traversal on chatgpt", ["https://chatgpt.com/connector/oauth/../../evil"] },
        { "a trailing newline on chatgpt", ["https://chatgpt.com/connector/oauth/abc\n"] },
        { "an empty chatgpt id", ["https://chatgpt.com/connector/oauth/"] },
        { "a chatgpt id too long", ["https://chatgpt.com/connector/oauth/" + new string('a', 101)] },
        { "a port on chatgpt", ["https://chatgpt.com:8443/connector/oauth/abc"] },
        { "upper case host", ["https://CHATGPT.com/connector/oauth/abc"] },
        { "mixed claude and unknown", [HostIntegrationFixture.ClaudeRedirectUri, "https://example.org/callback"] },
        { "mixed claude and chatgpt", [HostIntegrationFixture.ClaudeRedirectUri, "https://chatgpt.com/connector/oauth/abc"] },
        { "two chatgpt uris", ["https://chatgpt.com/connector/oauth/abc", "https://chatgpt.com/connector/oauth/def"] },
        { "loopback", ["http://127.0.0.1:33418/callback"] },
        { "localhost", ["http://localhost:33418/callback"] },
        { "gemini's confidential client", [HostIntegrationFixture.GeminiRedirectUri] },
        { "nothing", [] },
        { "an empty uri", [""] },
        { "eleven uris", Enumerable.Repeat(HostIntegrationFixture.ClaudeRedirectUri, 11).ToArray() },
        { "a uri over 2048 characters", ["https://claude.ai/" + new string('a', 2048)] },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Anything_but_all_the_uris_of_one_approved_tool_gets_400_and_writes_nothing(string what, string[] uris)
    {
        var before = await ApplicationCountAsync();
        using var response = await RegisterUrisAsync(uris);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, what + ": " + body);
        JsonDocument.Parse(body).RootElement.GetProperty("error").GetString().ShouldBe("invalid_redirect_uri", what);
        (await ApplicationCountAsync()).ShouldBe(before, what);
    }

    [Theory]
    [InlineData("this is not json", "invalid_client_metadata")]
    [InlineData("{}", "invalid_redirect_uri")]
    [InlineData("[]", "invalid_redirect_uri")]
    [InlineData("{\"redirect_uris\":\"https://claude.ai/api/mcp/auth_callback\"}", "invalid_redirect_uri")]
    [InlineData("{\"redirect_uris\":[1,2]}", "invalid_redirect_uri")]
    public async Task A_malformed_body_gets_400(string body, string error)
    {
        using var response = await RegisterAsync(body);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, text);
        JsonDocument.Parse(text).RootElement.GetProperty("error").GetString().ShouldBe(error);
    }

    [Fact]
    public async Task The_eleventh_registration_of_an_address_in_a_minute_answers_429_and_another_address_goes_on()
    {
        var address = NewAddress();
        var body = JsonSerializer.Serialize(new Dictionary<string, object> { ["redirect_uris"] = new[] { HostIntegrationFixture.ClaudeRedirectUri } });
        for (var i = 0; i < AiConnectorRateLimits.RegistrationsPerMinute; i++)
        {
            using var allowed = await RegisterAsync(body, address);
            allowed.StatusCode.ShouldBe(HttpStatusCode.Created, await allowed.Content.ReadAsStringAsync());
        }

        using var limited = await RegisterAsync(body, address);
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.ShouldNotBeNull();

        /* A spoofed first value does not buy a new partition: the last value is the proxy's. */
        using var spoofed = await RegisterAsync(body, "203.0.113.9, " + address);
        spoofed.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        using var other = await RegisterAsync(body);
        other.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_redirect_uri_over_the_length_limit_gets_400_and_writes_nothing()
    {
        var before = await ApplicationCountAsync();
        using var response = await RegisterUrisAsync(ChatGptBase + new string('a', 20_000));
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        JsonDocument.Parse(body).RootElement.GetProperty("error").GetString().ShouldBe("invalid_redirect_uri");
        (await ApplicationCountAsync()).ShouldBe(before);
    }

    /// <summary>
    /// The 16 KB limit on the body itself is Kestrel's to enforce: <see cref="Microsoft.AspNetCore.TestHost.TestServer"/> has no maximum request body size feature, so a request here cannot
    /// reach it, and a body over the limit proves nothing on this host. What can be proved here is that the limit is declared on the action.
    /// </summary>
    [Fact]
    public void The_registration_action_declares_a_body_limit_of_16_kb()
    {
        var limit = typeof(AiConnectorRegistrationController).GetMethod(nameof(AiConnectorRegistrationController.RegisterAsync))!
            .GetCustomAttributes(inherit: true).OfType<Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata>().ShouldHaveSingleItem();
        limit.MaxRequestBodySize.ShouldBe(16 * 1024);
    }

    /* ---------- The cap (design 74 "Build notes") ---------- */

    private async Task<long> ChatGptClientCountAsync()
        => await _h.CountAsync(null, "SELECT count(*) FROM \"OpenIddictApplications\" WHERE \"ClientId\" LIKE @prefix", ("prefix", AiConnectorConsts.ChatGptClientIdPrefix + "%"));

    private static async Task<string> ClientIdOfAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("client_id").GetString()!;
    }

    /// <summary>Runs with the cap at exactly the applications that exist now, and always puts it back. Options are a singleton, so the running registrar reads this value.</summary>
    private async Task AtTheCapAsync(Func<long, Task> action)
    {
        var options = _h.Services.GetRequiredService<IOptions<AiConnectorRegistrationOptions>>().Value;
        var cap = options.MaxChatGptClients;
        var count = await ChatGptClientCountAsync();
        options.MaxChatGptClients = (int)count;
        try
        {
            await action(count);
        }
        finally
        {
            options.MaxChatGptClients = cap;
        }
    }

    [Fact]
    public async Task At_the_cap_a_registered_uri_still_returns_its_client_and_writes_nothing()
    {
        var uri = ChatGptBase + "conn_" + Guid.NewGuid().ToString("N")[..16];
        using var first = await RegisterUrisAsync(uri);
        var clientId = await ClientIdOfAsync(first);

        await AtTheCapAsync(async count =>
        {
            var before = _h.Host.Logs.Entries.Count;
            using var again = await RegisterUrisAsync(uri);
            (await ClientIdOfAsync(again)).ShouldBe(clientId);

            (await ChatGptClientCountAsync()).ShouldBe(count);
            _h.Host.Logs.Entries.Skip(before).ShouldNotContain(e => e.Level >= LogLevel.Error, "a registered URI is not refused");

            /* A tool with fixed URIs writes nothing either, so the cap has no say. */
            using var claude = await RegisterUrisAsync(HostIntegrationFixture.ClaudeRedirectUri);
            (await ClientIdOfAsync(claude)).ShouldBe(ClaudeClient);
        });
    }

    [Fact]
    public async Task At_the_cap_a_new_uri_gets_400_writes_nothing_and_logs_an_error_with_the_count_and_the_cap_and_no_uri()
    {
        /* At least one application exists, so the cap is not zero. */
        using (var seed = await RegisterUrisAsync(ChatGptBase + "conn_" + Guid.NewGuid().ToString("N")[..16]))
        {
            await ClientIdOfAsync(seed);
        }

        var connection = "conn_" + Guid.NewGuid().ToString("N")[..16];
        var uri = ChatGptBase + connection;
        await AtTheCapAsync(async count =>
        {
            var before = _h.Host.Logs.Entries.Count;
            var applications = await ApplicationCountAsync();

            using var refused = await RegisterUrisAsync(uri);
            var body = await refused.Content.ReadAsStringAsync();

            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
            var json = JsonDocument.Parse(body).RootElement;
            json.GetProperty("error").GetString().ShouldBe("invalid_redirect_uri");
            json.TryGetProperty("client_id", out _).ShouldBeFalse(body);
            body.ShouldNotContain(count.ToString(System.Globalization.CultureInfo.InvariantCulture), Case.Sensitive, "an anonymous caller learns nothing about the count");
            body.ShouldNotContain("cap", Case.Insensitive, "an anonymous caller learns nothing about the cap");

            (await ApplicationCountAsync()).ShouldBe(applications, "nothing written");
            (await ChatGptClientCountAsync()).ShouldBe(count);

            var entries = _h.Host.Logs.Entries.Skip(before).ToList();
            var error = entries.Where(e => e.Level >= LogLevel.Error).ShouldHaveSingleItem();
            error.Category.ShouldBe(typeof(AiConnectorClientRegistrar).FullName);
            error.Message.ShouldContain(count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " exist");
            error.Message.ShouldContain("the cap is " + count.ToString(System.Globalization.CultureInfo.InvariantCulture));

            /* Not the URI, not its connection id, not the hash that stands for it: in no line of the request, at any level. */
            var clientId = AiConnectorClients.ChatGptClientId(uri);
            foreach (var entry in entries)
            {
                var text = entry.Message + " " + entry.Exception;
                text.ShouldNotContain(connection, Case.Insensitive, entry.Category);
                text.ShouldNotContain(clientId, Case.Insensitive, entry.Category);
                text.ShouldNotContain("chatgpt.com/connector", Case.Insensitive, entry.Category);
            }
        });

        /* The cap is the only reason: once there is room the same URI registers. */
        using var allowed = await RegisterUrisAsync(uri);
        (await ClientIdOfAsync(allowed)).ShouldBe(AiConnectorClients.ChatGptClientId(uri));
    }

    [Fact]
    public async Task A_refused_registration_works_under_any_tenant_in_the_request_and_writes_in_the_host_only()
    {
        var uri = ChatGptBase + "conn_" + Guid.NewGuid().ToString("N")[..16];
        var before = await ApplicationCountAsync();
        using var response = await _h.RestAsync(null, HttpMethod.Post, "/connect/register?__tenant=" + HostIntegrationFixture.TenantAName, null, request =>
        {
            request.Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, object> { ["redirect_uris"] = new[] { uri } }), System.Text.Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", NewAddress());
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await ApplicationCountAsync()).ShouldBe(before + 1, "OpenIddict's rows live in the host database, whatever the request names");
    }
}
