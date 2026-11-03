using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using OpenTms.Localization;
using OpenTms.Platform.Accounts;
using OpenTms.SignIn;
using Volo.Abp.Account.Web.Pages.Account;
using Volo.Abp.Auditing;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace OpenTms.Pages.Account;

/// <summary>
/// Choosing a password through a one-time link (docs/designs/62-sign-in-tenant-detection.md, "Password recovery"): an
/// invitation, a reset link from an administrator or from "Forgot password?", or the sign-in page itself when the user
/// must choose a new password first. The page runs in the host and changes to the tenant of the link's <c>__tenant</c>.
/// The link's token arrives once in the query string; the page keeps it in a sealed cookie
/// (<see cref="PasswordLinkProtector"/>) and goes on at its own address without it.
/// Choosing the password also clears "must change the password" and a lockout: the user has just proved the mailbox, and
/// ABP's own page left both in place, so the user stayed locked out after the reset. Afterwards the browser goes to the
/// SPA (or back to the sign-in it came from), never to the auth server's root.
/// </summary>
public class OpenTmsResetPasswordModel : AccountPageModel
{
    private readonly PasswordLinkTokens _tokens;
    private readonly ITenantStore _tenantStore;
    private readonly SignInEmailProtector _emailProtector;
    private readonly PasswordLinkProtector _linkProtector;
    private readonly IStringLocalizer<OpenTmsResource> _openTmsLocalizer;

    /// <summary>The link of the mail: only on its first request, then taken into the cookie.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid UserId { get; set; }

    [BindProperty(SupportsGet = true, Name = TenantResolverConsts.DefaultTenantKey)]
    public string? TenantKey { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ResetToken { get; set; }

    [BindProperty]
    [DisableAuditing]
    public string? Password { get; set; }

    [BindProperty]
    [DisableAuditing]
    public string? ConfirmPassword { get; set; }

    /// <summary>Sent here by the sign-in page: the user must choose a new password before signing in.</summary>
    public bool Change { get; private set; }

    /// <summary>The link is expired, spent, for an inactive user or organisation, or not a link at all.</summary>
    public bool InvalidLink { get; private set; }

    /// <summary>The account's address, shown and given to password managers.</summary>
    public string? AccountEmail { get; private set; }

    public OpenTmsResetPasswordModel(
        PasswordLinkTokens tokens,
        ITenantStore tenantStore,
        SignInEmailProtector emailProtector,
        PasswordLinkProtector linkProtector,
        IStringLocalizer<OpenTmsResource> openTmsLocalizer)
    {
        _tokens = tokens;
        _tenantStore = tenantStore;
        _emailProtector = emailProtector;
        _linkProtector = linkProtector;
        _openTmsLocalizer = openTmsLocalizer;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        /* The mail link: its token goes into the cookie, the browser on to this page's address without it. */
        if (!string.IsNullOrEmpty(ResetToken))
        {
            Response.Cookies.Append(
                PasswordLinkProtector.CookieName,
                _linkProtector.Protect(new PasswordLink(UserId, TenantKey, ResetToken, Change: false, ReturnUrl: null)),
                SignInCookies.Options(Request, SameSiteMode.Lax, null));
            return RedirectToPage("./ResetPassword");
        }

        var link = ReadLink();
        var tenant = link == null ? null : await LinkTenantAsync(link);
        if (link == null || tenant == null)
        {
            InvalidLink = true;
            return Page();
        }

        Change = link.Change;
        using (CurrentTenant.Change(tenant.Value.Id, tenant.Value.Name))
        {
            var (user, _) = await LinkUserAsync(link);
            InvalidLink = user == null;
            AccountEmail = user?.Email;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ModelState.Clear();
        var link = ReadLink();
        var tenant = link == null ? null : await LinkTenantAsync(link);
        if (link == null || tenant == null)
        {
            InvalidLink = true;
            return Page();
        }

        Change = link.Change;
        using (CurrentTenant.Change(tenant.Value.Id, tenant.Value.Name))
        {
            await IdentityOptions.SetAsync();
            var (user, resetToken) = await LinkUserAsync(link);
            if (user == null || resetToken == null)
            {
                InvalidLink = true;
                return Page();
            }

            AccountEmail = user.Email;
            if (string.IsNullOrEmpty(Password))
            {
                Alerts.Danger(_openTmsLocalizer["SignIn:NewPasswordRequired"]);
                return Page();
            }

            if (Password != ConfirmPassword)
            {
                Alerts.Danger(_openTmsLocalizer["SignIn:PasswordsDiffer"]);
                return Page();
            }

            try
            {
                (await UserManager.ResetPasswordAsync(user, resetToken, Password)).CheckErrors();
            }
            catch (AbpIdentityResultException exception)
            {
                Alerts.Danger(GetLocalizeExceptionMessage(exception));
                return Page();
            }

            user.SetShouldChangePasswordOnNextLogin(false);
            (await UserManager.UpdateAsync(user)).CheckErrors();
            (await UserManager.SetLockoutEndDateAsync(user, null)).CheckErrors();
            (await UserManager.ResetAccessFailedCountAsync(user)).CheckErrors();

            await IdentitySecurityLogManager.SaveAsync(new IdentitySecurityLogContext
            {
                Identity = IdentitySecurityLogIdentityConsts.Identity,
                Action = IdentitySecurityLogActionConsts.ChangePassword,
                UserName = user.UserName,
            });

            RememberEmailForSignIn(user.Email);
        }

        Response.Cookies.Delete(PasswordLinkProtector.CookieName, SignInCookies.Options(Request, SameSiteMode.Lax, null));
        return RedirectToPage("./ResetPasswordConfirmation", new { returnUrl = link.Change && IsLocal(link.ReturnUrl) ? link.ReturnUrl : null });
    }

    private PasswordLink? ReadLink() => _linkProtector.Unprotect(Request.Cookies[PasswordLinkProtector.CookieName]);

    /// <summary>The tenant the link is for: none (the host), or an active tenant; null when the link names no such tenant.</summary>
    private async Task<(Guid? Id, string? Name)?> LinkTenantAsync(PasswordLink link)
    {
        if (string.IsNullOrEmpty(link.TenantKey))
        {
            return (null, null);
        }

        if (!Guid.TryParse(link.TenantKey, out var tenantId))
        {
            return null;
        }

        var tenant = await _tenantStore.FindAsync(tenantId);
        return tenant is { IsActive: true } ? (tenant.Id, tenant.Name) : null;
    }

    /// <summary>The link's active user with a token Identity accepts, in the current tenant; nulls when the link does not hold.</summary>
    private async Task<(IdentityUser? User, string? ResetToken)> LinkUserAsync(PasswordLink link)
    {
        var user = link.UserId == Guid.Empty ? null : await UserManager.FindByIdAsync(link.UserId.ToString());
        if (user == null || !user.IsActive)
        {
            return (null, null);
        }

        var resetToken = await _tokens.ToResetTokenAsync(user, link.Token);
        return resetToken == null ? (null, null) : (user, resetToken);
    }

    /// <summary>
    /// The sign-in page then starts at the password for this address. An address the browser already remembers for
    /// sign-in stays as it is ("Remember me" included); another is kept for the browser session only.
    /// </summary>
    private void RememberEmailForSignIn(string email)
    {
        var remembered = _emailProtector.Unprotect(Request.Cookies[SignInEmailProtector.CookieName]);
        if (remembered != null && string.Equals(remembered.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Response.Cookies.Append(
            SignInEmailProtector.CookieName,
            _emailProtector.Protect(new SignInEmail(email, RememberMe: false)),
            SignInCookies.Options(Request, SameSiteMode.Lax, null));
    }

    private bool IsLocal(string? url) => !string.IsNullOrEmpty(url) && Url.IsLocalUrl(url);
}
