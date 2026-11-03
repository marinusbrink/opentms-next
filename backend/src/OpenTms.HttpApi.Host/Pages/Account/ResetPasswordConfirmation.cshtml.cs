using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenTms.SignIn;
using Volo.Abp.Account.Web.Pages.Account;
using Volo.Abp.UI.Navigation.Urls;

namespace OpenTms.Pages.Account;

/// <summary>
/// The password is set (docs/designs/62-sign-in-tenant-detection.md, "Password recovery"). The way on is the sign-in the
/// user came from (a password change the sign-in page asked for) or else the SPA, which starts a fresh sign-in. ABP's page
/// sent the browser to the auth server's root, which opens Swagger UI.
/// </summary>
public class OpenTmsResetPasswordConfirmationModel : AccountPageModel
{
    private readonly AppUrlOptions _appUrlOptions;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public OpenTmsResetPasswordConfirmationModel(IOptions<AppUrlOptions> appUrlOptions)
    {
        _appUrlOptions = appUrlOptions.Value;
    }

    /// <summary>True when the way on is the sign-in the user came from.</summary>
    public bool BackToSignIn => !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl);

    public string ContinueUrl => BackToSignIn ? Url.Page("./Login", new { returnUrl = ReturnUrl })! : SpaUrl.Root(_appUrlOptions);

    public IActionResult OnGet() => Page();
}
