using System;
using System.Linq;
using System.Threading.Tasks;
using OpenIddict.Abstractions;
using OpenTms.Impersonation;
using OpenTms.Integrations.AiConnector;
using Volo.Abp.DependencyInjection;
using Volo.Abp.OpenIddict;
using Volo.Abp.Security.Claims;

namespace OpenTms.AiConnector;

/// <summary>
/// A connector's access token is readable by the AI vendor (ABP disables access-token encryption), so it carries the user id
/// and the tenant and no other person's data (design 74, PO decision 4): no e-mail, name, phone or role, and none of the
/// browser session's carried claims. The host adds names and roles back per request through dynamic claims. An impersonator
/// claim stays, so that check 2 can see one. Any other client's token is untouched.
/// </summary>
public class AiConnectorTokenClaimsHandler : IAbpOpenIddictClaimsPrincipalHandler, ITransientDependency
{
    public Task HandleAsync(AbpOpenIddictClaimsPrincipalHandlerContext context)
    {
        if (!AiConnectorConsts.IsConnectorClient(context.OpenIddictRequest.ClientId))
        {
            return Task.CompletedTask;
        }

        foreach (var claim in context.Principal.Claims)
        {
            if (claim.Type == AbpClaimTypes.UserId || claim.Type == AbpClaimTypes.TenantId || ImpersonationClaims.Types.Contains(claim.Type))
            {
                claim.SetDestinations(OpenIddictConstants.Destinations.AccessToken);
            }
            else
            {
                claim.SetDestinations(Array.Empty<string>());
            }
        }

        return Task.CompletedTask;
    }
}
