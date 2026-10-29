using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTms.Platform.Accounts;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.UI.Navigation.Urls;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace OpenTms.Pages.Account;

/// <summary>
/// Redeems an account-switch ticket from the user menu (docs/designs/63-user-menu.md). The ticket is honoured only for
/// the session it was issued to (the auth cookie's user and tenant must be the ticket's expected ones, and the session
/// must not be impersonating). The session is then switched to the user's account in the other tenant, keeping its
/// persistence, with the places whose password the session knows, and the browser goes to the SPA, which signs in again
/// through the normal code flow, as after "Log in as".
/// </summary>
[IgnoreAntiforgeryToken]
public class SwitchAccountModel : PageModel
{
    private readonly IAccountSwitchTicketProtector _ticketProtector;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly IOptions<IdentityOptions> _identityOptions;
    private readonly IdentitySecurityLogManager _securityLogManager;
    private readonly AppUrlOptions _appUrlOptions;
    private readonly ILogger<SwitchAccountModel> _logger;

    public SwitchAccountModel(
        IAccountSwitchTicketProtector ticketProtector,
        SignInManager<IdentityUser> signInManager,
        ICurrentTenant currentTenant,
        IOptions<IdentityOptions> identityOptions,
        IdentitySecurityLogManager securityLogManager,
        IOptions<AppUrlOptions> appUrlOptions,
        ILogger<SwitchAccountModel> logger)
    {
        _ticketProtector = ticketProtector;
        _signInManager = signInManager;
        _currentTenant = currentTenant;
        _identityOptions = identityOptions;
        _securityLogManager = securityLogManager;
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

        var authentication = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var session = authentication.Principal;
        if (session?.FindUserId() == null)
        {
            /* The auth-server session expired (or the server restarted): log in first, then come back here. */
            return Challenge(IdentityConstants.ApplicationScheme);
        }

        if (session.FindUserId() != redeemed.ExpectedUserId || session.FindTenantId() != redeemed.ExpectedTenantId)
        {
            _logger.LogWarning(
                "Account switch session mismatch: cookie user {SessionUserId} (tenant {SessionTenantId}), ticket expects {ExpectedUserId} (tenant {ExpectedTenantId})",
                session.FindUserId(), session.FindTenantId(), redeemed.ExpectedUserId, redeemed.ExpectedTenantId);
            return Refuse("ticket was issued to another session");
        }

        if (session.FindImpersonatorUserId() != null)
        {
            return Refuse("session is impersonating");
        }

        var isPersistent = authentication.Properties?.IsPersistent ?? false;
        using (_currentTenant.Change(redeemed.TargetTenantId))
        {
            await _identityOptions.SetAsync();
            var user = await _signInManager.UserManager.FindByIdAsync(redeemed.TargetUserId.ToString());
            if (user == null || !user.IsActive || await _signInManager.UserManager.IsLockedOutAsync(user)
                || !await _signInManager.CanSignInAsync(user))
            {
                return Refuse("target user missing, inactive, locked out or not allowed");
            }

            await _signInManager.SignOutAsync();
            await _signInManager.SignInWithClaimsAsync(user, isPersistent,
            [
                new Claim(AccountSwitchClaims.PasswordTenants, redeemed.PasswordTenants)
            ]);
            await _securityLogManager.SaveAsync(new IdentitySecurityLogContext
            {
                Identity = IdentitySecurityLogIdentityConsts.Identity,
                Action = IdentitySecurityLogActionConsts.LoginSucceeded,
                UserName = user.UserName,
            });
        }

        _logger.LogInformation(
            "Account switch: session of user {UserId} (tenant {TenantId}) is now user {TargetUserId} (tenant {TargetTenantId})",
            redeemed.ExpectedUserId, redeemed.ExpectedTenantId, redeemed.TargetUserId, redeemed.TargetTenantId);

        return Redirect(SpaUrl() + LandingPath);
    }

    /// <summary>
    /// A route of the SPA that needs a login, so the SPA signs in again by itself in the new session's tenant instead of
    /// showing its start page; the same as "Log in as".
    /// </summary>
    private const string LandingPath = "admin";

    private string SpaUrl() => OpenTms.SignIn.SpaUrl.Root(_appUrlOptions);

    /// <summary>Back to the SPA, which signs in again in the session it had and tells the user.</summary>
    private IActionResult Refuse(string reason)
    {
        _logger.LogWarning("Account switch ticket refused: {Reason}", reason);
        return Redirect(SpaUrl() + LandingPath + "?impersonation=refused");
    }
}
