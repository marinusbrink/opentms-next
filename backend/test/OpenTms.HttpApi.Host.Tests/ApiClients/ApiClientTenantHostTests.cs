using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenTms.Integrations.OrderIntake;
using Shouldly;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.OpenIddict;
using Volo.Abp.Security.Claims;
using Xunit;

namespace OpenTms.ApiClients;

/// <summary>
/// The tenant of an API client's token (design 72 "Tokens for API clients", "Tenant dimension", build note 11): the
/// claims handler puts the tenant of the client's OpenIddict application into a client-credentials token and removes any
/// tenant the request asked for (<c>__tenant</c>); the resolve contributor runs first and takes an API client's tenant
/// from that claim, so nothing later (query string, header, cookie) can change it. Plain unit tests on the production
/// classes, every port faked; the host has no harness for a real <c>/connect/token</c> round trip (see the report).
/// Risk class: Critical — authentication and tenant isolation (a client of tenant A that could land in tenant B would
/// submit orders into another customer's database).
/// </summary>
public class ApiClientTenantClaimsPrincipalHandlerTests
{
    private const string ClientId = "otms_ABCDEFGHIJKLMNOPQRSTUVWX";
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid ApiClientA = Guid.NewGuid();

    /// <summary>An OpenIddict application manager that knows one application with the given properties.</summary>
    public static IOpenIddictApplicationManager Applications(string clientId, IDictionary<string, JsonElement>? properties)
    {
        var proxy = DispatchProxy.Create<IOpenIddictApplicationManager, ApplicationManagerProxy>();
        var fake = (ApplicationManagerProxy)(object)proxy;
        fake.ClientId = clientId;
        fake.Properties = properties?.ToImmutableDictionary() ?? ImmutableDictionary<string, JsonElement>.Empty;
        return proxy;
    }

    public class ApplicationManagerProxy : DispatchProxy
    {
        public string ClientId { get; set; } = string.Empty;

        public ImmutableDictionary<string, JsonElement> Properties { get; set; } = ImmutableDictionary<string, JsonElement>.Empty;

        private readonly object _application = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod!.Name)
            {
                case nameof(IOpenIddictApplicationManager.FindByClientIdAsync):
                    return new ValueTask<object?>((string?)args![0] == ClientId ? _application : null);
                case nameof(IOpenIddictApplicationManager.GetPropertiesAsync):
                    args![0].ShouldBeSameAs(_application);
                    return new ValueTask<ImmutableDictionary<string, JsonElement>>(Properties);
                default:
                    throw new NotSupportedException(targetMethod.Name + " is not expected from the claims handler.");
            }
        }
    }

    private static JsonElement Json(string value) => JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement.Clone();

    private static JsonElement JsonNumber(int value) => JsonDocument.Parse(value.ToString()).RootElement.Clone();

    private static Dictionary<string, JsonElement> ApiClientProperties(string? tenant, string? apiClient)
    {
        var properties = new Dictionary<string, JsonElement>();
        if (tenant != null)
        {
            properties[OrderIntakeConsts.TenantIdProperty] = Json(tenant);
        }

        if (apiClient != null)
        {
            properties[OrderIntakeConsts.ApiClientIdProperty] = Json(apiClient);
        }

        return properties;
    }

    /// <summary>The principal OpenIddict builds for the token request; <paramref name="requestedTenant"/> is what __tenant made ABP add.</summary>
    private static ClaimsPrincipal TokenPrincipal(Guid? requestedTenant, params Claim[] extra)
    {
        var claims = new List<Claim> { new(OpenIddictConstants.Claims.Subject, ClientId), new(AbpClaimTypes.ClientId, ClientId) };
        if (requestedTenant != null)
        {
            claims.Add(new Claim(AbpClaimTypes.TenantId, requestedTenant.Value.ToString()));
        }

        claims.AddRange(extra);
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "OpenIddict"));
    }

    private static AbpOpenIddictClaimsPrincipalHandlerContext Context(ClaimsPrincipal principal, string grantType, IOpenIddictApplicationManager applications, string? clientId = ClientId)
    {
        var services = new ServiceCollection().AddSingleton(applications).BuildServiceProvider();
        return new AbpOpenIddictClaimsPrincipalHandlerContext(services, new OpenIddictRequest { GrantType = grantType, ClientId = clientId }, principal);
    }

    private static Task HandleAsync(AbpOpenIddictClaimsPrincipalHandlerContext context) => new ApiClientTenantClaimsPrincipalHandler().HandleAsync(context);

    [Fact]
    public async Task A_client_of_tenant_A_that_asks_for_tenant_B_gets_a_token_for_tenant_A()
    {
        var principal = TokenPrincipal(requestedTenant: TenantB);

        await HandleAsync(Context(principal, OpenIddictConstants.GrantTypes.ClientCredentials,
            Applications(ClientId, ApiClientProperties(TenantA.ToString(), ApiClientA.ToString()))));

        principal.FindAll(AbpClaimTypes.TenantId).Select(c => c.Value).ShouldBe([TenantA.ToString()], "the requested tenant is removed, the application's set");
        principal.FindAll(OrderIntakeConsts.ApiClientIdProperty).Select(c => c.Value).ShouldBe([ApiClientA.ToString()]);
        foreach (var claim in principal.Claims.Where(c => c.Type == AbpClaimTypes.TenantId || c.Type == OrderIntakeConsts.ApiClientIdProperty))
        {
            claim.GetDestinations().ShouldContain(OpenIddictConstants.Destinations.AccessToken, claim.Type);
        }
    }

    [Fact]
    public async Task A_client_without_a_requested_tenant_gets_its_application_tenant()
    {
        var principal = TokenPrincipal(requestedTenant: null);

        await HandleAsync(Context(principal, OpenIddictConstants.GrantTypes.ClientCredentials,
            Applications(ClientId, ApiClientProperties(TenantA.ToString(), ApiClientA.ToString()))));

        principal.FindFirst(AbpClaimTypes.TenantId)!.Value.ShouldBe(TenantA.ToString());
    }

    [Fact]
    public async Task An_api_client_id_claim_smuggled_into_the_principal_is_replaced_by_the_applications()
    {
        var principal = TokenPrincipal(TenantB, new Claim(OrderIntakeConsts.ApiClientIdProperty, Guid.NewGuid().ToString()));

        await HandleAsync(Context(principal, OpenIddictConstants.GrantTypes.ClientCredentials,
            Applications(ClientId, ApiClientProperties(TenantA.ToString(), ApiClientA.ToString()))));

        principal.FindAll(OrderIntakeConsts.ApiClientIdProperty).Select(c => c.Value).ShouldBe([ApiClientA.ToString()]);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("")]
    public async Task A_malformed_tenant_property_gives_no_tenant_at_all_never_the_requested_one(string tenantProperty)
    {
        var principal = TokenPrincipal(requestedTenant: TenantB);

        await HandleAsync(Context(principal, OpenIddictConstants.GrantTypes.ClientCredentials,
            Applications(ClientId, ApiClientProperties(tenantProperty, ApiClientA.ToString()))));

        principal.FindFirst(AbpClaimTypes.TenantId).ShouldBeNull("a token without a tenant is refused by the intake API; tenant B must never appear");
    }

    [Fact]
    public async Task A_tenant_property_that_is_not_a_string_gives_no_tenant()
    {
        var principal = TokenPrincipal(requestedTenant: TenantB);
        var properties = new Dictionary<string, JsonElement> { [OrderIntakeConsts.TenantIdProperty] = JsonNumber(42) };

        await HandleAsync(Context(principal, OpenIddictConstants.GrantTypes.ClientCredentials, Applications(ClientId, properties)));

        principal.FindFirst(AbpClaimTypes.TenantId).ShouldBeNull();
        principal.FindFirst(OrderIntakeConsts.ApiClientIdProperty).ShouldBeNull();
    }

    [Theory]
    [InlineData(OpenIddictConstants.GrantTypes.AuthorizationCode)]
    [InlineData(OpenIddictConstants.GrantTypes.RefreshToken)]
    [InlineData(OpenIddictConstants.GrantTypes.Password)]
    public async Task Tokens_of_other_grants_are_untouched(string grantType)
    {
        var principal = TokenPrincipal(requestedTenant: TenantB);
        var before = principal.Claims.Select(c => (c.Type, c.Value)).ToList();

        await HandleAsync(Context(principal, grantType, Applications(ClientId, ApiClientProperties(TenantA.ToString(), ApiClientA.ToString()))));

        principal.Claims.Select(c => (c.Type, c.Value)).ShouldBe(before);
    }

    [Fact]
    public async Task A_client_credentials_application_without_the_property_is_untouched()
    {
        /* The SPA's or Swagger's application, or any confidential client that is not an API client. */
        var principal = TokenPrincipal(requestedTenant: TenantB);
        var before = principal.Claims.Select(c => (c.Type, c.Value)).ToList();

        await HandleAsync(Context(principal, OpenIddictConstants.GrantTypes.ClientCredentials, Applications(ClientId, properties: null)));

        principal.Claims.Select(c => (c.Type, c.Value)).ShouldBe(before);
    }

    [Fact]
    public async Task An_unknown_client_or_a_request_without_client_id_is_untouched()
    {
        var principal = TokenPrincipal(requestedTenant: TenantB);
        var before = principal.Claims.Select(c => (c.Type, c.Value)).ToList();
        var applications = Applications(ClientId, ApiClientProperties(TenantA.ToString(), ApiClientA.ToString()));

        await HandleAsync(Context(principal, OpenIddictConstants.GrantTypes.ClientCredentials, applications, clientId: "otms_someoneelse"));
        await HandleAsync(Context(principal, OpenIddictConstants.GrantTypes.ClientCredentials, applications, clientId: null));

        principal.Claims.Select(c => (c.Type, c.Value)).ShouldBe(before);
    }
}

public class ApiClientTenantResolveContributorTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private sealed class PrincipalAccessor(ClaimsPrincipal? principal) : ICurrentPrincipalAccessor
    {
        public ClaimsPrincipal Principal => principal!;

        public IDisposable Change(ClaimsPrincipal principal) => throw new NotSupportedException();
    }

    /// <summary>What ABP's query-string, header or cookie contributor would do with <c>__tenant=B</c>.</summary>
    private sealed class RequestedTenantContributor(Guid tenant) : TenantResolveContributorBase
    {
        public override string Name => "RequestedTenant";

        public override Task ResolveAsync(ITenantResolveContext context)
        {
            context.TenantIdOrName = tenant.ToString();
            context.Handled = true;
            return Task.CompletedTask;
        }
    }

    private static ServiceProvider Services(ClaimsPrincipal? principal, params ITenantResolveContributor[] contributors)
        => new ServiceCollection()
            .AddSingleton<ICurrentPrincipalAccessor>(new PrincipalAccessor(principal))
            .Configure<AbpTenantResolveOptions>(o => o.TenantResolvers.AddRange(contributors))
            .BuildServiceProvider();

    private static ClaimsPrincipal ApiClient(Guid? tenant)
    {
        var claims = new List<Claim> { new(OrderIntakeConsts.ApiClientIdProperty, Guid.NewGuid().ToString()), new(AbpClaimTypes.ClientId, "otms_x") };
        if (tenant != null)
        {
            claims.Add(new Claim(AbpClaimTypes.TenantId, tenant.Value.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private static async Task<TenantResolveResult> ResolveAsync(ClaimsPrincipal? principal, bool withOurs = true)
    {
        var contributors = new List<ITenantResolveContributor>();
        if (withOurs)
        {
            contributors.Add(new ApiClientTenantResolveContributor());
        }

        contributors.Add(new RequestedTenantContributor(TenantB));
        var services = Services(principal, contributors.ToArray());
        return await new TenantResolver(services.GetRequiredService<IOptions<AbpTenantResolveOptions>>(), services).ResolveTenantIdOrNameAsync();
    }

    [Fact]
    public async Task An_api_client_token_of_tenant_A_resolves_to_A_whatever_the_request_asks_for()
    {
        var result = await ResolveAsync(ApiClient(TenantA));

        result.TenantIdOrName.ShouldBe(TenantA.ToString());
        result.AppliedResolvers.ShouldBe([ApiClientTenantResolveContributor.ContributorName], "no later resolver runs");
    }

    [Fact]
    public async Task Without_the_contributor_the_request_would_choose_the_tenant()
    {
        /* The reason it exists (build note 11): ABP's claim resolver answers only for a signed-in user. */
        (await ResolveAsync(ApiClient(TenantA), withOurs: false)).TenantIdOrName.ShouldBe(TenantB.ToString());
    }

    [Fact]
    public async Task An_api_client_token_without_a_tenant_resolves_to_the_host_never_to_the_requested_tenant()
    {
        var result = await ResolveAsync(ApiClient(tenant: null));

        result.TenantIdOrName.ShouldBeNull();
        result.AppliedResolvers.ShouldBe([ApiClientTenantResolveContributor.ContributorName]);
    }

    [Fact]
    public async Task Other_principals_are_left_to_the_other_resolvers()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AbpClaimTypes.UserId, Guid.NewGuid().ToString()), new Claim(AbpClaimTypes.TenantId, TenantA.ToString())], "Bearer"));

        (await ResolveAsync(user)).TenantIdOrName.ShouldBe(TenantB.ToString(), "a user token is ABP's claim resolver's business, not this one's");
        (await ResolveAsync(principal: null)).TenantIdOrName.ShouldBe(TenantB.ToString());
        (await ResolveAsync(new ClaimsPrincipal(new ClaimsIdentity()))).TenantIdOrName.ShouldBe(TenantB.ToString());
    }
}

public class ApiClientHostWiringTests
{
    /// <summary>Runs the host module's ConfigureApiClients on a fresh service collection.</summary>
    private static ServiceProvider ConfigureApiClients()
    {
        var services = new ServiceCollection();
        /* ABP's own resolvers are configured first (the multi-tenancy module), so "first" must be earned by inserting. */
        services.Configure<AbpTenantResolveOptions>(o => o.TenantResolvers.Add(new CurrentUserTenantResolveContributor()));
        var context = new ServiceConfigurationContext(services);
        var module = new OpenTmsHttpApiHostModule();
        typeof(AbpModule).GetProperty("ServiceConfigurationContext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(module, context);
        var method = typeof(OpenTmsHttpApiHostModule).GetMethod("ConfigureApiClients", BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(module, method.GetParameters().Length == 0 ? [] : [context]);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void The_claims_handler_is_registered_with_abps_openiddict_claims_principal_options()
    {
        ConfigureApiClients().GetRequiredService<IOptions<AbpOpenIddictClaimsPrincipalOptions>>().Value
            .ClaimsPrincipalHandlers.ShouldContain(typeof(ApiClientTenantClaimsPrincipalHandler));
    }

    [Fact]
    public void The_resolve_contributor_runs_before_every_other_resolver()
    {
        var resolvers = ConfigureApiClients().GetRequiredService<IOptions<AbpTenantResolveOptions>>().Value.TenantResolvers;
        resolvers[0].ShouldBeOfType<ApiClientTenantResolveContributor>();
    }
}
