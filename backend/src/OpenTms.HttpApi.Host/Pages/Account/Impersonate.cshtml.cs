using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTms.Impersonation;
using OpenTms.Platform.Impersonation;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.TenantManagement;
using Volo.Abp.UI.Navigation.Urls;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace OpenTms.Pages.Account;

/// <summary>
/// Redeems an impersonation ticket (docs/designs/tenants-and-editions.md, "Log in as a tenant user"). The ticket is
/// honoured only for the session it was issued to: the auth cookie's user and tenant must be the ticket's expected
/// ones, and on the way back the cookie must carry the impersonator claims that name the target. The session is then
/// switched to the target user (with the impersonator claims on the way in, without them on the way back) and the
/// browser goes to the SPA, which signs in again through the normal code flow.
/// </summary>
[IgnoreAntiforgeryToken]
public class ImpersonateModel : PageModel
{
    private readonly IImpersonationTicketProtector _ticketProtector;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly ITenantRepository _tenantRepository;
    private readonly AppUrlOptions _appUrlOptions;
    private readonly ILogger<ImpersonateModel> _logger;

    public ImpersonateModel(
        IImpersonationTicketProtector ticketProtector,
        SignInManager<IdentityUser> signInManager,
        ICurrentTenant currentTenant,
        ITenantRepository tenantRepository,
        IOptions<AppUrlOptions> appUrlOptions,
        ILogger<ImpersonateModel> logger)
    {
        _ticketProtector = ticketProtector;
        _signInManager = signInManager;
        _currentTenant = currentTenant;
        _tenantRepository = tenantRepository;
        _appUrlOptions = appUrlOptions.Value;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync(string? ticket)
    {
        var redeemed = string.IsNullOrEmpty(ticket) ? null : _ticketProtector.Unprotect(ticket);
        if (redeemed == null)
        {
            return Refuse("invalid or expired ticket");
        }

        var session = (await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme)).Principal;
        if (session?.FindUserId() == null)
        {
            /* The auth-server session expired (or the server restarted): log in first, then come back here. */
            return Challenge(IdentityConstants.ApplicationScheme);
        }

        if (session.FindUserId() != redeemed.ExpectedUserId || session.FindTenantId() != redeemed.ExpectedTenantId)
        {
            _logger.LogWarning(
                "Impersonation session mismatch: cookie user {SessionUserId} (tenant {SessionTenantId}), ticket expects {ExpectedUserId} (tenant {ExpectedTenantId})",
                session.FindUserId(), session.FindTenantId(), redeemed.ExpectedUserId, redeemed.ExpectedTenantId);
            return Refuse("ticket was issued to another session");
        }

        if (redeemed.IsReturn
            && (session.FindImpersonatorUserId() != redeemed.TargetUserId
                || session.FindImpersonatorTenantId() != redeemed.TargetTenantId))
        {
            return Refuse("return ticket does not match the impersonator");
        }

        var additionalClaims = redeemed.IsReturn ? [] : await ImpersonatorClaimsAsync(session);

        using (_currentTenant.Change(redeemed.TargetTenantId))
        {
            var user = await _signInManager.UserManager.FindByIdAsync(redeemed.TargetUserId.ToString());
            if (user == null || !user.IsActive)
            {
                return Refuse("target user missing or inactive");
            }

            await _signInManager.SignOutAsync();
            await _signInManager.SignInWithClaimsAsync(user, isPersistent: false, additionalClaims);
        }

        _logger.LogInformation(
            redeemed.IsReturn
                ? "Impersonation ended: session returned to user {UserId} (tenant {TenantId})"
                : "Impersonation started: session is now user {UserId} (tenant {TenantId})",
            redeemed.TargetUserId, redeemed.TargetTenantId);

        return Redirect(SpaUrl() + LandingPath);
    }

    private async Task<List<Claim>> ImpersonatorClaimsAsync(ClaimsPrincipal session)
    {
        var claims = new List<Claim> { new(AbpClaimTypes.ImpersonatorUserId, session.FindUserId()!.Value.ToString()) };
        var userName = session.FindFirst(AbpClaimTypes.UserName)?.Value;
        if (!string.IsNullOrEmpty(userName))
        {
            claims.Add(new Claim(AbpClaimTypes.ImpersonatorUserName, userName));
        }

        if (session.FindTenantId() is { } tenantId)
        {
            claims.Add(new Claim(AbpClaimTypes.ImpersonatorTenantId, tenantId.ToString()));
            using (_currentTenant.Change(null))
            {
                var tenant = await _tenantRepository.FindAsync(tenantId);
                if (tenant != null)
                {
                    claims.Add(new Claim(AbpClaimTypes.ImpersonatorTenantName, tenant.Name));
                }
            }
        }

        return claims;
    }

    /// <summary>
    /// A route of the SPA that needs a login (the Administration app, which every tenant and the host have), so the SPA
    /// signs in again by itself in the new session's tenant instead of showing its start page.
    /// </summary>
    private const string LandingPath = "admin";

    private string SpaUrl() => OpenTms.SignIn.SpaUrl.Root(_appUrlOptions);

    /// <summary>Back to the SPA, which signs in again in the session it had and tells the user.</summary>
    private IActionResult Refuse(string reason)
    {
        _logger.LogWarning("Impersonation ticket refused: {Reason}", reason);
        return Redirect(SpaUrl() + LandingPath + "?impersonation=refused");
    }
}
