using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using OpenTms.Integrations;
using OpenTms.Integrations.Localization;
using OpenTms.Integrations.OrderIntake;

namespace OpenTms.Controllers.Integrations;

/// <summary>
/// The OAuth redirect URI of the mailbox connections (design 72 "Connect"): Google and Microsoft send the browser here with
/// the code and the data-protected state. Anonymous at the ASP.NET level because the provider's redirect carries no
/// OpenTMS identity; the tenant and connection come only from the state, which the processor checks. A refused state
/// gets a plain 400, never a redirect to the SPA.
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
        var result = await _processor.ProcessAsync(code, state, error, HttpContext.RequestAborted);
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
}
