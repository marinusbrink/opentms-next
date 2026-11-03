using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using OpenTms.Integrations;
using OpenTms.Integrations.Localization;
using OpenTms.Integrations.OrderIntake;

namespace OpenTms.Controllers.Integrations;

/// <summary>
/// The OAuth redirect URI of the mailbox connections (design 72 "Connect"): Google and Microsoft send the browser here with
/// the code and the data-protected state. Anonymous for the bearer scheme because this is a top-level browser navigation
/// from the provider (no access token); the tenant and connection come only from the state, which the processor checks.
/// The browser's OpenTMS sign-in is read from the auth server's Identity application cookie (this endpoint runs on the
/// same host as the <c>/Account</c> login pages, which issue it; path <c>/</c>, SameSite Lax, so a top-level GET from the
/// provider carries it), and the processor completes the consent only for the user and tenant the state recorded (design
/// 72 build note 17). A refused state gets a plain 400, never a redirect to the SPA.
/// </summary>
[ApiController]
[Route("api/integrations/mailbox-oauth/callback")]
[ApiExplorerSettings(IgnoreApi = true)]
public class MailboxOAuthCallbackController : OpenTmsController
{
    private readonly IMailboxOAuthCallbackProcessor _processor;
    private readonly IStringLocalizer<IntegrationsResource> _localizer;

    public MailboxOAuthCallbackController(IMailboxOAuthCallbackProcessor processor, IStringLocalizer<IntegrationsResource> localizer)
    {
        _processor = processor;
        _localizer = localizer;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> CallbackAsync([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error)
    {
        var result = await _processor.ProcessAsync(code, state, error, await SessionAsync(), HttpContext.RequestAborted);
        if (result.IsRefused)
        {
            return new ContentResult
            {
                StatusCode = 400,
                Content = _localizer[IntegrationsErrorCodes.OAuthStateInvalid],
                ContentType = "text/plain; charset=utf-8"
            };
        }

        return Redirect(result.RedirectUrl!);
    }

    /// <summary>
    /// The user and tenant of the auth server's sign-in cookie (the scheme the Impersonate page reads too), or null. During
    /// an impersonation the cookie's user is the impersonated one (the impersonator rides along in separate claims), which
    /// is the effective user the state recorded.
    /// </summary>
    private async Task<MailboxOAuthCallbackSession?> SessionAsync()
    {
        var authentication = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var principal = authentication.Succeeded ? authentication.Principal : null;
        var userId = principal?.FindUserId();
        return userId == null ? null : new MailboxOAuthCallbackSession(userId.Value, principal!.FindTenantId());
    }
}
