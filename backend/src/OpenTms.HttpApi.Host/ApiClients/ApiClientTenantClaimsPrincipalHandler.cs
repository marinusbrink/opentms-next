using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenTms.Integrations.OrderIntake;
using Volo.Abp.DependencyInjection;
using Volo.Abp.OpenIddict;
using Volo.Abp.Security.Claims;

namespace OpenTms.ApiClients;

/// <summary>
/// The tenant of an API client's token (design 72 "Tokens for API clients", "Tenant dimension"). For a
/// <c>client_credentials</c> token of an OpenIddict application that carries <see cref="OrderIntakeConsts.TenantIdProperty"/>,
/// every tenant claim is removed and <c>tenantid</c> is set from the application, with
/// <see cref="OrderIntakeConsts.ApiClientIdProperty"/>; both go into the access token. So a token request that carries
/// <c>__tenant</c> of another tenant (header, query, cookie) cannot change the tenant the token is for. Any other token
/// (the SPA's code flow, Swagger, a client without the property) is untouched.
/// </summary>
public class ApiClientTenantClaimsPrincipalHandler : IAbpOpenIddictClaimsPrincipalHandler, ITransientDependency
{
    public async Task HandleAsync(AbpOpenIddictClaimsPrincipalHandlerContext context)
    {
        if (!context.OpenIddictRequest.IsClientCredentialsGrantType() || string.IsNullOrEmpty(context.OpenIddictRequest.ClientId))
        {
            return;
        }

        var applications = context.ScopeServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await applications.FindByClientIdAsync(context.OpenIddictRequest.ClientId);
        if (application == null)
        {
            return;
        }

        var properties = await applications.GetPropertiesAsync(application);
        if (!properties.TryGetValue(OrderIntakeConsts.TenantIdProperty, out var tenantProperty))
        {
            return;
        }

        var identity = context.Principal.Identities.First();
        foreach (var claim in context.Principal.Claims.Where(c => c.Type == AbpClaimTypes.TenantId).ToList())
        {
            claim.Subject?.RemoveClaim(claim);
        }

        foreach (var claim in context.Principal.Claims.Where(c => c.Type == OrderIntakeConsts.ApiClientIdProperty).ToList())
        {
            claim.Subject?.RemoveClaim(claim);
        }

        /* A malformed property gives the token no tenant at all, never the requested one: the intake API then refuses it. */
        if (Read(tenantProperty) is { } tenantId)
        {
            identity.AddClaim(AbpClaimTypes.TenantId, tenantId.ToString());
        }

        if (properties.TryGetValue(OrderIntakeConsts.ApiClientIdProperty, out var clientProperty) && Read(clientProperty) is { } apiClientId)
        {
            identity.AddClaim(OrderIntakeConsts.ApiClientIdProperty, apiClientId.ToString());
        }

        foreach (var claim in context.Principal.Claims.Where(c => c.Type == AbpClaimTypes.TenantId || c.Type == OrderIntakeConsts.ApiClientIdProperty))
        {
            claim.SetDestinations(OpenIddictConstants.Destinations.AccessToken);
        }
    }

    private static Guid? Read(JsonElement property)
        => property.ValueKind == JsonValueKind.String && Guid.TryParse(property.GetString(), out var id) && id != Guid.Empty ? id : null;
}
