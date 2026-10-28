using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OpenTms.Pages.Account;

/// <summary>
/// Replaces ABP's register page: users never register themselves (docs/designs/62-sign-in-tenant-detection.md,
/// "No self-registration"), so every request to it, external-login sign-ups included, gets 404.
/// </summary>
[IgnoreAntiforgeryToken]
public class RegisterModel : PageModel
{
    public IActionResult OnGet() => NotFound();

    public IActionResult OnPost() => NotFound();
}
