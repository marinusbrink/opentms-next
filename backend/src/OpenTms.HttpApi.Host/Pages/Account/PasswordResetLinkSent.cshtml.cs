using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Account.Web.Pages.Account;

namespace OpenTms.Pages.Account;

/// <summary>After "Forgot password?": the mail is on its way (docs/designs/62-sign-in-tenant-detection.md, "Password recovery").</summary>
public class OpenTmsPasswordResetLinkSentModel : AccountPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrlHash { get; set; }

    public IActionResult OnGet() => Page();
}
