using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenTms.Financial.Permissions;
using OpenTms.HostIntegration;
using OpenTms.Integrations.AiConnector;
using OpenTms.Integrations.Permissions;
using OpenTms.Integrations.Settings;
using OpenTms.MasterData.Permissions;
using OpenTms.Orders.Permissions;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Reporting.Features;
using Shouldly;
using Volo.Abp.Features;
using Volo.Abp.FeatureManagement;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Security.Claims;
using Volo.Abp.SettingManagement;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;
using Xunit;
using IdentityUser = Volo.Abp.Identity.IdentityUser;
using IdentityUserManager = Volo.Abp.Identity.IdentityUserManager;

namespace OpenTms.AiConnector;

/// <summary>A user of a tenant the test made, with real grants.</summary>
public sealed record TestUser(Guid? TenantId, Guid Id, string Email);

/// <summary>What <c>/connect/token</c> answered.</summary>
public sealed record TokenAnswer(HttpStatusCode Status, string? AccessToken, string? RefreshToken, string? Scope, string Body);

/// <summary>What a tool answered over <c>/mcp</c>.</summary>
public sealed record ToolAnswer(bool IsError, string Text, JsonElement Result)
{
    /// <summary>The tool's JSON result; fails when the tool refused.</summary>
    public JsonElement Json => JsonDocument.Parse(Text).RootElement;
}

/// <summary>
/// Everything the AI connector tests of designs 74 and 75 share, on the real host of <see cref="HostIntegrationFixture"/>: the
/// tenants' stored features and real grants, a browser with a cookie jar, the real authorization-code flow with its connect screen,
/// the MCP requests and tokens minted with the host's own signing key for what no real flow gives (a host user, an impersonator).
/// Nothing below the HTTP surface is faked.
/// </summary>
public class AiConnectorHarness
{
    public const string Password = "1q2w3E*Test";
    public const string ClaudeClient = "OpenTms_Mcp_Claude";
    public const string GeminiClient = "OpenTms_Mcp_Gemini";

    /// <summary>The connector's own permission plus what all nine tools of design 74 read with.</summary>
    public static readonly string[] AllToolPermissions =
    [
        IntegrationsPermissions.AiConnector.Use,
        OrdersPermissions.TransportOrders.Default,
        PlanningExecutionPermissions.TransportTrips.Default,
        PlanningExecutionPermissions.Legs.Default,
        MasterDataPermissions.Relations.Default,
        MasterDataPermissions.Vehicles.Default,
        FinancialPermissions.RateLines.Default
    ];

    private static readonly HashSet<Guid> FeaturesOn = [];

    public AiConnectorHarness(HostIntegrationFixture host)
    {
        Host = host;
    }

    public HostIntegrationFixture Host { get; }

    public IServiceProvider Services => Host.Services;

    public Guid TenantA => Host.TenantAId;

    public Guid TenantB => Host.TenantBId;

    public Guid TenantC => Host.TenantCId;

    public Guid TenantD => Host.TenantDId;

    /// <summary>Two tenants of one tier. "own": each its own database. "shared": both in the host database, only the data filter separates them.</summary>
    public (Guid Mine, Guid Other, string OtherName) Pair(string tier) => tier == "own"
        ? (TenantA, TenantC, HostIntegrationFixture.TenantCName)
        : (TenantB, TenantD, HostIntegrationFixture.TenantDName);

    public string DatabaseOf(Guid? tenant)
        => tenant == TenantA ? HostIntegrationFixture.TenantADatabase
            : tenant == TenantC ? HostIntegrationFixture.TenantCDatabase
            : HostIntegrationFixture.HostDatabase;

    /* ---------- Arrangement through the host's own services ---------- */

    public async Task InScopeAsync(Guid? tenant, Func<IServiceProvider, Task> action, ClaimsPrincipal? principal = null, bool transactional = true)
    {
        using var scope = Services.CreateScope();
        var services = scope.ServiceProvider;
        using var tenantScope = services.GetRequiredService<ICurrentTenant>().Change(tenant);
        using var principalScope = principal == null ? null : services.GetRequiredService<ICurrentPrincipalAccessor>().Change(principal);
        var unitOfWorkManager = services.GetRequiredService<IUnitOfWorkManager>();
        using var unitOfWork = unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: transactional), requiresNew: true);
        await action(services);
        await unitOfWork.CompleteAsync();
    }

    public async Task<T> InScopeAsync<T>(Guid? tenant, Func<IServiceProvider, Task<T>> action, ClaimsPrincipal? principal = null, bool transactional = true)
    {
        T result = default!;
        await InScopeAsync(tenant, async services => { result = await action(services); }, principal, transactional);
        return result;
    }

    public async Task SetFeatureAsync(Guid tenant, string feature, string value)
        => await InScopeAsync(null, services => services.GetRequiredService<IFeatureManager>().SetForTenantAsync(tenant, feature, value));

    /// <summary>Every toggle of the tree on as a stored tenant value, so a test can switch one off (and back) and nothing else stands in the way.</summary>
    public async Task EnsureFeaturesOnAsync(Guid tenant)
    {
        if (!FeaturesOn.Add(tenant))
        {
            return;
        }

        await InScopeAsync(null, async services =>
        {
            var manager = services.GetRequiredService<IFeatureManager>();
            foreach (var feature in await services.GetRequiredService<IFeatureDefinitionManager>().GetAllAsync())
            {
                if (feature.Name.StartsWith(AppFeatures.GroupName + ".", StringComparison.Ordinal) || feature.Name == ReportingFeatures.Metrics)
                {
                    await manager.SetForTenantAsync(tenant, feature.Name, "true");
                }
            }
        });
    }

    /// <summary>Runs with one stored feature off, and always switches it back on.</summary>
    public async Task WithFeatureOffAsync(Guid tenant, string feature, Func<Task> action)
    {
        await SetFeatureAsync(tenant, feature, "false");
        try
        {
            await action();
        }
        finally
        {
            await SetFeatureAsync(tenant, feature, "true");
        }
    }

    /// <summary>The tenant's data sharing, on record as the Accept button would leave it.</summary>
    public async Task SetAcceptedAsync(Guid tenant, Guid? byUser = null)
        => await InScopeAsync(tenant, async services =>
        {
            var settings = services.GetRequiredService<ISettingManager>();
            await settings.SetForCurrentTenantAsync(IntegrationsSettings.AiConnectorAcceptedAt, DateTime.UtcNow.ToString("O"));
            await settings.SetForCurrentTenantAsync(IntegrationsSettings.AiConnectorAcceptedByUserId, byUser?.ToString());
        });

    public async Task ClearAcceptedAsync(Guid tenant)
        => await InScopeAsync(tenant, async services =>
        {
            var settings = services.GetRequiredService<ISettingManager>();
            await settings.SetForCurrentTenantAsync(IntegrationsSettings.AiConnectorAcceptedAt, null);
            await settings.SetForCurrentTenantAsync(IntegrationsSettings.AiConnectorAcceptedByUserId, null);
        });

    /// <summary>The tenant is ready for the connector: its features on and the sharing accepted.</summary>
    public async Task ReadyAsync(Guid tenant)
    {
        await EnsureFeaturesOnAsync(tenant);
        await SetAcceptedAsync(tenant);
    }

    /// <summary>A real user with a real password, active, and with these permissions granted to the user.</summary>
    public async Task<TestUser> CreateUserAsync(Guid? tenant, params string[] permissions)
    {
        var id = Guid.NewGuid();
        var email = "u" + id.ToString("N")[..12] + "@host-test.example";
        await InScopeAsync(tenant, async services =>
        {
            var users = services.GetRequiredService<IdentityUserManager>();
            var user = new IdentityUser(id, email, email, tenant);
            (await users.CreateAsync(user, Password)).Succeeded.ShouldBeTrue();
            var grants = services.GetRequiredService<IPermissionManager>();
            foreach (var permission in permissions)
            {
                await grants.SetForUserAsync(id, permission, true);
            }
        });
        return new TestUser(tenant, id, email);
    }

    public async Task SetPermissionAsync(TestUser user, string permission, bool granted)
        => await InScopeAsync(user.TenantId, services => services.GetRequiredService<IPermissionManager>().SetForUserAsync(user.Id, permission, granted));

    public async Task SetUserActiveAsync(TestUser user, bool active)
        => await InScopeAsync(user.TenantId, async services =>
        {
            var users = services.GetRequiredService<IdentityUserManager>();
            var row = await users.GetByIdAsync(user.Id);
            row.SetIsActive(active);
            (await users.UpdateAsync(row)).Succeeded.ShouldBeTrue();
        });

    /// <summary>A tenant of the shared tier made for one test: its connections and rows are that test's alone.</summary>
    public async Task<Guid> NewTenantAsync()
    {
        var tenant = await InScopeAsync(null, async services =>
        {
            var created = await services.GetRequiredService<ITenantManager>().CreateAsync("host-test-" + Guid.NewGuid().ToString("N")[..10]);
            await services.GetRequiredService<ITenantRepository>().InsertAsync(created, autoSave: true);
            return created.Id;
        });
        await ReadyAsync(tenant);
        return tenant;
    }

    /// <summary>The principal of a user for an in-process service call, as the host's dynamic claims would build it for a grant on the user.</summary>
    public static ClaimsPrincipal PrincipalOf(TestUser user)
    {
        var claims = new List<Claim> { new(AbpClaimTypes.UserId, user.Id.ToString()), new(AbpClaimTypes.UserName, user.Email) };
        if (user.TenantId != null)
        {
            claims.Add(new Claim(AbpClaimTypes.TenantId, user.TenantId.Value.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    /* ---------- A browser ---------- */

    /// <summary>
    /// A browser: a cookie jar over the test server. <see cref="SignInAsync"/> writes the session cookie the Identity scheme itself would, from the
    /// principal SignInManager builds; <c>ConnectFlowHostTests</c> walks the real sign-in page once.
    /// </summary>
    public Browser NewBrowser() => new(Host);

    public async Task<Browser> SignedInBrowserAsync(TestUser user, params Claim[] additionalClaims)
    {
        var browser = NewBrowser();
        await browser.SignInAsync(user, additionalClaims);
        return browser;
    }

    /* ---------- The authorization-code flow ---------- */

    public sealed record AuthorizeRequest(string ClientId, string RedirectUri, string Scope, string? CodeVerifier, string? ChallengeMethod = "S256", string State = "the-state", string? Prompt = null, string? Resource = null)
    {
        public string Url
        {
            get
            {
                var query = new Dictionary<string, string?>
                {
                    ["client_id"] = ClientId,
                    ["redirect_uri"] = RedirectUri,
                    ["response_type"] = "code",
                    ["scope"] = Scope,
                    ["state"] = State,
                };
                if (CodeVerifier != null)
                {
                    query["code_challenge"] = ChallengeMethod == "plain" ? CodeVerifier : Challenge(CodeVerifier);
                    query["code_challenge_method"] = ChallengeMethod;
                }

                if (Prompt != null)
                {
                    query["prompt"] = Prompt;
                }

                if (Resource != null)
                {
                    query["resource"] = Resource;
                }

                return "/connect/authorize?" + string.Join("&", query.Select(q => q.Key + "=" + Uri.EscapeDataString(q.Value ?? string.Empty)));
            }
        }
    }

    public const string ConnectorScopes = AiConnectorConsts.Scope + " " + OpenIddictConstants.Scopes.OfflineAccess;

    public static string NewVerifier() => WebEncoders(RandomNumberGenerator.GetBytes(48));

    private static string WebEncoders(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Challenge(string verifier) => WebEncoders(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static AuthorizeRequest ClaudeRequest(string verifier, string? scope = null, string? prompt = null, string? resource = null)
        => new(ClaudeClient, HostIntegrationFixture.ClaudeRedirectUri, scope ?? ConnectorScopes, verifier, Prompt: prompt, Resource: resource);

    /// <summary>Opens the authorize request, presses a button on the screen and returns where the browser is sent.</summary>
    public static async Task<HttpResponseMessage> PressAsync(Browser browser, AuthorizeRequest request, string button)
    {
        var screen = await browser.GetAsync(request.Url);
        var html = await screen.Content.ReadAsStringAsync();
        screen.StatusCode.ShouldBe(HttpStatusCode.OK, html);
        return await browser.SubmitConnectFormAsync(html, button);
    }

    public static string? QueryValue(Uri? location, string name)
    {
        if (location == null)
        {
            return null;
        }

        var query = location.IsAbsoluteUri ? location.Query : location.OriginalString.Contains('?') ? location.OriginalString[location.OriginalString.IndexOf('?')..] : string.Empty;
        return query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair[0] == name)
            .Select(pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty)
            .FirstOrDefault();
    }

    public async Task<TokenAnswer> RedeemCodeAsync(AuthorizeRequest request, string code, string? verifier = null, string? secret = null, string? tenantInQuery = null)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = OpenIddictConstants.GrantTypes.AuthorizationCode,
            ["client_id"] = request.ClientId,
            ["redirect_uri"] = request.RedirectUri,
            ["code"] = code,
        };
        var usedVerifier = verifier ?? request.CodeVerifier;
        if (usedVerifier != null)
        {
            form["code_verifier"] = usedVerifier;
        }

        if (secret != null)
        {
            form["client_secret"] = secret;
        }

        return await PostTokenAsync(form, tenantInQuery);
    }

    public async Task<TokenAnswer> RefreshAsync(string clientId, string refreshToken, string? secret = null, string? tenantInQuery = null, string? tenantInHeader = null)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = OpenIddictConstants.GrantTypes.RefreshToken,
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken,
        };
        if (secret != null)
        {
            form["client_secret"] = secret;
        }

        return await PostTokenAsync(form, tenantInQuery, tenantInHeader);
    }

    public async Task<TokenAnswer> PostTokenAsync(Dictionary<string, string> form, string? tenantInQuery = null, string? tenantInHeader = null)
    {
        using var client = Host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/token" + (tenantInQuery == null ? string.Empty : "?__tenant=" + Uri.EscapeDataString(tenantInQuery)))
        {
            Content = new FormUrlEncodedContent(form)
        };
        if (tenantInHeader != null)
        {
            request.Headers.Add("__tenant", tenantInHeader);
        }

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            return new TokenAnswer(response.StatusCode, null, null, null, body);
        }

        var json = JsonDocument.Parse(body).RootElement;
        return new TokenAnswer(
            response.StatusCode,
            json.GetProperty("access_token").GetString(),
            json.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
            json.TryGetProperty("scope", out var scope) ? scope.GetString() : null,
            body);
    }

    /// <summary>A connection made the whole way: a browser session, the screen, Allow, the code, the token.</summary>
    public async Task<TokenAnswer> ConnectAsync(TestUser user, AuthorizeRequest? request = null, string? secret = null)
    {
        request ??= ClaudeRequest(NewVerifier());
        var browser = await SignedInBrowserAsync(user);
        using var allowed = await PressAsync(browser, request, "allow");
        allowed.StatusCode.ShouldBe(HttpStatusCode.Found, await allowed.Content.ReadAsStringAsync());
        var code = QueryValue(allowed.Headers.Location, "code");
        code.ShouldNotBeNullOrEmpty(allowed.Headers.Location?.ToString());
        var token = await RedeemCodeAsync(request, code!, secret: secret);
        token.Status.ShouldBe(HttpStatusCode.OK, token.Body);
        return token;
    }

    /// <summary>A browser token of the SPA's own public client, through its own code flow: what the REST API takes.</summary>
    public async Task<string> BrowserTokenAsync(TestUser user)
    {
        var browser = await SignedInBrowserAsync(user);
        var verifier = NewVerifier();
        var request = new AuthorizeRequest("OpenTms_App", "http://localhost:5173/auth/callback", "openid offline_access OpenTms", verifier);
        using var response = await browser.GetAsync(request.Url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Found, body);
        var code = QueryValue(response.Headers.Location, "code");
        code.ShouldNotBeNullOrEmpty(response.Headers.Location?.ToString());
        var token = await RedeemCodeAsync(request, code!);
        token.Status.ShouldBe(HttpStatusCode.OK, token.Body);
        return token.AccessToken!;
    }

    /* ---------- Tokens no real flow gives ---------- */

    /// <summary>
    /// A token signed with the host's own OpenIddict signing key, in the shape the server issues: the checks behind token validation must then
    /// refuse it on what it says, not on a bad signature. For a host user and an impersonator, whom the connect screen never lets through.
    /// </summary>
    public string MintToken(Guid userId, Guid? tenantId, string audience = AiConnectorConsts.Resource, string clientId = ClaudeClient, string? authorizationId = null, params Claim[] extra)
    {
        var options = Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;
        var claims = new Dictionary<string, object>
        {
            [OpenIddictConstants.Claims.Subject] = userId.ToString(),
            [OpenIddictConstants.Claims.ClientId] = clientId,
            [OpenIddictConstants.Claims.Scope] = ConnectorScopes,
            [OpenIddictConstants.Claims.JwtId] = Guid.NewGuid().ToString(),
            [OpenIddictConstants.Claims.Private.AuthorizationId] = authorizationId ?? Guid.NewGuid().ToString(),
            [OpenIddictConstants.Claims.Private.TokenId] = Guid.NewGuid().ToString(),
        };
        if (tenantId != null)
        {
            claims[AbpClaimTypes.TenantId] = tenantId.Value.ToString();
        }

        foreach (var claim in extra)
        {
            claims[claim.Type] = claim.Value;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer?.AbsoluteUri ?? "http://localhost/",
            Audience = audience,
            Claims = claims,
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(30),
            TokenType = "at+jwt",
            SigningCredentials = options.SigningCredentials.First(),
        });
    }

    /// <summary>The claims a token states, read without validating (ABP does not encrypt a connector's token: the AI vendor can read it too).</summary>
    public static IReadOnlyList<Claim> ClaimsOf(string token) => new JsonWebToken(token).Claims.ToList();

    /// <summary>The OpenIddict authorization a token names.</summary>
    public static string AuthorizationIdOf(string token) => ClaimsOf(token).Single(c => c.Type == OpenIddictConstants.Claims.Private.AuthorizationId).Value;

    /* ---------- /mcp ---------- */

    private static int _rpcId;

    /// <summary>One JSON-RPC request over HTTP, as an MCP client in stateless mode sends it.</summary>
    public async Task<HttpResponseMessage> PostMcpAsync(string? token, string method, object? parameters = null, Action<HttpRequestMessage>? alter = null, string path = "/mcp", HttpClient? client = null)
    {
        var owned = client == null;
        client ??= Host.CreateClient();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = Interlocked.Increment(ref _rpcId),
                    ["method"] = method,
                    ["params"] = parameters ?? new Dictionary<string, object?>(),
                }), Encoding.UTF8, "application/json")
            };
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("text/event-stream");
            request.Headers.Add("MCP-Protocol-Version", "2025-06-18");
            if (token != null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            alter?.Invoke(request);
            return await client.SendAsync(request);
        }
        finally
        {
            if (owned)
            {
                client.Dispose();
            }
        }
    }

    /// <summary>The JSON-RPC <c>result</c> of an accepted request, from either answer form of Streamable HTTP.</summary>
    public static async Task<JsonElement> ReadRpcResultAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        var json = body.TrimStart().StartsWith('{')
            ? body
            : string.Join("\n", body.Split('\n').Where(line => line.StartsWith("data:", StringComparison.Ordinal)).Select(line => line["data:".Length..].Trim()));
        var root = JsonDocument.Parse(json).RootElement;
        root.TryGetProperty("error", out var error).ShouldBeFalse(error.ToString());
        return root.GetProperty("result");
    }

    public async Task<IReadOnlyList<string>> ListToolsAsync(string token)
    {
        using var response = await PostMcpAsync(token, "tools/list");
        var result = await ReadRpcResultAsync(response);
        return result.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()!).ToList();
    }

    public async Task<ToolAnswer> CallToolAsync(string token, string tool, object? arguments = null, Action<HttpRequestMessage>? alter = null)
    {
        using var response = await PostMcpAsync(token, "tools/call", new Dictionary<string, object?> { ["name"] = tool, ["arguments"] = arguments ?? new Dictionary<string, object?>() }, alter);
        var result = await ReadRpcResultAsync(response);
        var isError = result.TryGetProperty("isError", out var flag) && flag.ValueKind == JsonValueKind.True;
        var text = result.GetProperty("content").EnumerateArray().First().GetProperty("text").GetString()!;
        return new ToolAnswer(isError, text, result);
    }

    public async Task<JsonElement> ToolJsonAsync(string token, string tool, object? arguments = null, Action<HttpRequestMessage>? alter = null)
    {
        var answer = await CallToolAsync(token, tool, arguments, alter);
        answer.IsError.ShouldBeFalse(tool + ": " + answer.Text);
        return answer.Json;
    }

    /// <summary>The reason of a refusal on <c>/mcp</c>.</summary>
    public static async Task ShouldBeRefusedAsync(HttpResponseMessage response, string reason)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, body);
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("error").GetString().ShouldBe("access_denied");
        json.GetProperty("reason").GetString().ShouldBe(reason);
        json.EnumerateObject().Select(p => p.Name).ShouldBe(["error", "reason"], ignoreOrder: true, "a refusal states no data");
    }

    /* ---------- REST ---------- */

    public async Task<HttpResponseMessage> RestAsync(string? token, HttpMethod method, string path, object? body = null, Action<HttpRequestMessage>? alter = null)
    {
        using var client = Host.CreateClient();
        using var request = new HttpRequestMessage(method, path);
        if (token != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        alter?.Invoke(request);
        return await client.SendAsync(request);
    }

    /* ---------- Direct reads ---------- */

    public async Task<long> CountAsync(Guid? tenant, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(HostIntegrationFixture.ConnectionStringFor(DatabaseOf(tenant)));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async Task ExecuteAsync(Guid? tenant, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(HostIntegrationFixture.ConnectionStringFor(DatabaseOf(tenant)));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>A cookie jar over the test server: HttpClient's own ignores cookies a TestServer sets for a host with no scheme rules, and redirects are the tests' to read.</summary>
public sealed class Browser
{
    private readonly HostIntegrationFixture _host;
    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

    public Browser(HostIntegrationFixture host)
    {
        _host = host;
    }

    public IReadOnlyDictionary<string, string> Cookies => _cookies;

    public void SetCookie(string name, string value) => _cookies[name] = value;

    /// <summary>Writes the Identity scheme's own session cookie, from the principal SignInManager builds for the user.</summary>
    public async Task SignInAsync(TestUser user, params Claim[] additionalClaims)
    {
        using var scope = _host.Services.CreateScope();
        var services = scope.ServiceProvider;
        using var tenantScope = services.GetRequiredService<ICurrentTenant>().Change(user.TenantId);
        var unitOfWorkManager = services.GetRequiredService<IUnitOfWorkManager>();
        using var unitOfWork = unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: false), requiresNew: true);
        var signIn = services.GetRequiredService<SignInManager<IdentityUser>>();
        var identityUser = await signIn.UserManager.FindByIdAsync(user.Id.ToString());
        identityUser.ShouldNotBeNull();
        var principal = await signIn.CreateUserPrincipalAsync(identityUser);
        foreach (var claim in additionalClaims)
        {
            ((ClaimsIdentity)principal.Identity!).AddClaim(claim);
        }

        await unitOfWork.CompleteAsync();

        var options = services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow }, IdentityConstants.ApplicationScheme);
        _cookies[options.Cookie.Name!] = options.TicketDataFormat.Protect(ticket);
    }

    public async Task<HttpResponseMessage> GetAsync(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        return await SendAsync(request);
    }

    public async Task<HttpResponseMessage> PostFormAsync(string url, IEnumerable<KeyValuePair<string, string>> form)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        return await SendAsync(request);
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        using var client = _host.CreateClient();
        if (_cookies.Count > 0)
        {
            request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(c => c.Key + "=" + c.Value)));
        }

        var response = await client.SendAsync(request);
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            foreach (var value in values)
            {
                var pair = value.Split(';', 2)[0].Split('=', 2);
                if (pair.Length == 2)
                {
                    var expired = value.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase) || pair[1].Length == 0;
                    if (expired)
                    {
                        _cookies.Remove(pair[0]);
                    }
                    else
                    {
                        _cookies[pair[0]] = pair[1];
                    }
                }
            }
        }

        return response;
    }

    /// <summary>The form of the connect screen with one of its buttons pressed (hidden fields and the antiforgery token as rendered).</summary>
    public async Task<HttpResponseMessage> SubmitConnectFormAsync(string html, string button)
        => await PostFormAsync("/connect/authorize/callback", HiddenInputs(html).Append(new KeyValuePair<string, string>(button, "1")));

    /// <summary>Every hidden input of a page.</summary>
    public static IEnumerable<KeyValuePair<string, string>> HiddenInputs(string html)
    {
        foreach (Match input in Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var tag = input.Value;
            if (!tag.Contains("type=\"hidden\"", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = Regex.Match(tag, "name=\"([^\"]*)\"").Groups[1].Value;
            var value = Regex.Match(tag, "value=\"([^\"]*)\"").Groups[1].Value;
            yield return new KeyValuePair<string, string>(WebUtility.HtmlDecode(name), WebUtility.HtmlDecode(value));
        }
    }
}
