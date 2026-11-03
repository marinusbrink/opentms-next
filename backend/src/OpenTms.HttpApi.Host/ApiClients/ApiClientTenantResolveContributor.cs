using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.Integrations.OrderIntake;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;

namespace OpenTms.ApiClients;

/// <summary>
/// Resolves the tenant of an API client's request from its token, first of all resolvers (design 72 "Tokens for API
/// clients": "the API resolves the tenant from the token's claim"). ABP's own claim resolver
/// (<c>CurrentUserTenantResolveContributor</c>) only answers for a signed-in user, and a client-credentials token has no
/// user (ABP removes its subject), so without this a request could pick its tenant with <c>__tenant</c>. A principal that
/// carries <see cref="OrderIntakeConsts.ApiClientIdProperty"/> takes its tenant from the token's <c>tenantid</c> claim, and
/// nothing later can change it; a token without one resolves to the host, where the intake API refuses it.
/// </summary>
public class ApiClientTenantResolveContributor : TenantResolveContributorBase
{
    public const string ContributorName = "OpenTmsApiClient";

    public override string Name => ContributorName;

    public override Task ResolveAsync(ITenantResolveContext context)
    {
        var principal = context.ServiceProvider.GetRequiredService<ICurrentPrincipalAccessor>().Principal;
        if (principal?.FindFirst(OrderIntakeConsts.ApiClientIdProperty) != null)
        {
            context.Handled = true;
            context.TenantIdOrName = principal.FindFirst(AbpClaimTypes.TenantId)?.Value;
        }

        return Task.CompletedTask;
    }
}
