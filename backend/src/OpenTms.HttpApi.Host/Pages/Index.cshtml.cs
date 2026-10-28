using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OpenTms.Pages;

/// <summary>
/// The backend's root (docs/designs/62-sign-in-tenant-detection.md, "Where the browser lands"): Swagger UI. ABP's
/// template listed the OpenID Connect applications here. Swagger UI has its own sign-in (its Authorize button opens the
/// API sign-in page), so the root shows no sign-in or welcome page of its own; the view sends the browser on at once.
/// </summary>
public class IndexModel : PageModel
{
    public string SwaggerUrl => Url.Content("~/swagger");

    public void OnGet()
    {
    }
}
