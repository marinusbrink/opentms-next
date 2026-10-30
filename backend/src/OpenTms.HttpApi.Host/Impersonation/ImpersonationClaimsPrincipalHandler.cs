using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenTms.Accounts;
using OpenIddict.Server.AspNetCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.OpenIddict;

namespace OpenTms.Impersonation;

/// <summary>
/// Carries ABP's impersonator claims, and the user menu's password places (<see cref="CarriedSessionClaims"/>), into the
/// tokens. ABP rebuilds the principal from the user at every authorize
/// and token request, which would drop them: at authorize they come from the auth cookie the Impersonate page set,
/// at the token endpoint (code exchange, refresh) only from the code or refresh token being redeemed. They go into the
/// access and identity tokens, so the API's ICurrentUser and the audit log see the real actor.
/// </summary>
public class ImpersonationClaimsPrincipalHandler : IAbpOpenIddictClaimsPrincipalHandler, ITransientDependency
{
    public async Task HandleAsync(AbpOpenIddictClaimsPrincipalHandlerContext context)
    {
        var httpContext = context.ScopeServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext;
        if (httpContext == null)
        {
            return;
        }

        /* One source per request, never both: a token request (grant_type set) takes the impersonator only from the
         * code or refresh token it redeems, so a cookie a same-origin client happens to send cannot change it; an
         * authorize request takes it from the auth cookie the Impersonate page set. */
        var source = string.IsNullOrEmpty(context.OpenIddictRequest.GrantType)
            ? (await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme)).Principal
            : (await httpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;
        var claims = CarriedSessionClaims.From(source);

        var identity = context.Principal.Identities.First();
        foreach (var claim in claims.Where(c => !context.Principal.HasClaim(c.Type, c.Value)))
        {
            identity.AddClaim(claim.Type, claim.Value);
        }

        foreach (var claim in context.Principal.Claims.Where(c => CarriedSessionClaims.Types.Contains(c.Type)))
        {
            claim.SetDestinations(OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken);
        }
    }
}
