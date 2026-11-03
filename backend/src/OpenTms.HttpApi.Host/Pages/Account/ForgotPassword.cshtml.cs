using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenTms.Localization;
using OpenTms.Platform.Accounts;
using OpenTms.SignIn;
using Volo.Abp.Account.Web.Pages.Account;
using Volo.Abp.Validation;

namespace OpenTms.Pages.Account;

/// <summary>
/// "Forgot password?" without a tenant chosen up front (docs/designs/62-sign-in-tenant-detection.md, "Password
/// recovery"): every account the address has, in an active tenant or the host, gets a mail with its own link, naming the
/// organisation it is for (PO decision 2026-11-03). An unknown address is told so, as step 1 of the sign-in page tells
/// it. ABP's page looked the address up only in the tenant of ABP's tenant switch, the host unless the user typed the
/// organisation's name there, and carried the sign-in request into the mail, which then worked only in the browser
/// that asked.
/// </summary>
public class OpenTmsForgotPasswordModel : AccountPageModel
{
    private readonly SignInTenantResolver _tenantResolver;
    private readonly ForgotPasswordMailer _mailer;
    private readonly SignInEmailProtector _emailProtector;
    private readonly IStringLocalizer<OpenTmsResource> _openTmsLocalizer;

    [BindProperty]
    public string? Email { get; set; }

    /// <summary>The sign-in request the user came from: only for "Back to sign in", never in the mail.</summary>
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrlHash { get; set; }

    public OpenTmsForgotPasswordModel(
        SignInTenantResolver tenantResolver,
        ForgotPasswordMailer mailer,
        SignInEmailProtector emailProtector,
        IStringLocalizer<OpenTmsResource> openTmsLocalizer)
    {
        _tenantResolver = tenantResolver;
        _mailer = mailer;
        _emailProtector = emailProtector;
        _openTmsLocalizer = openTmsLocalizer;
    }

    /// <summary>Pre-filled with the address of the sign-in page's step 1.</summary>
    public IActionResult OnGet()
    {
        Email = _emailProtector.Unprotect(Request.Cookies[SignInEmailProtector.CookieName])?.Email;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ModelState.Clear();
        var email = Email?.Trim() ?? string.Empty;
        if (!ValidationHelper.IsValidEmailAddress(email))
        {
            Alerts.Danger(_openTmsLocalizer["SignIn:EmailInvalid"]);
            return Page();
        }

        var accounts = await _tenantResolver.FindAccountsAsync(email);
        if (accounts.Count == 0)
        {
            Alerts.Danger(_openTmsLocalizer["SignIn:EmailUnknown"]);
            return Page();
        }

        var sent = 0;
        foreach (var account in accounts)
        {
            try
            {
                if (await _mailer.SendAsync(account.TenantId, account.TenantName, account.UserId, email))
                {
                    sent++;
                }
            }
            catch (Exception exception)
            {
                Logger.LogError(exception, "Forgot-password link for user {UserId} of tenant {TenantId} not sent", account.UserId, account.TenantId);
            }
        }

        /* Nothing went out (mail is off for the organisation, or its database failed): saying "sent" would leave the user waiting. */
        if (sent == 0)
        {
            Alerts.Warning(_openTmsLocalizer["SignIn:ForgotNotSent"]);
            return Page();
        }

        return RedirectToPage("./PasswordResetLinkSent", new { ReturnUrl, ReturnUrlHash });
    }
}
