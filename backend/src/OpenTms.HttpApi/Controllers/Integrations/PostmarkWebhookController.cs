using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using OpenTms.Integrations.Emailing;

namespace OpenTms.Controllers.Integrations;

/// <summary>
/// Postmark's delivery webhooks (docs/designs/64-email-delivery-postmark.md): anonymous at the ASP.NET level because
/// Postmark has no OpenTMS identity; the processor checks Postmark's Basic credentials itself. Runs in the Api role until
/// the Ingest role exists (AD-11). No tenant header: the e-mail status sink finds the tenant in Platform's own route.
/// </summary>
[ApiController]
[Route("api/integrations/postmark/webhook")]
[IgnoreAntiforgeryToken]
[ApiExplorerSettings(IgnoreApi = true)]
public class PostmarkWebhookController : OpenTmsController
{
    /// <summary>Postmark's reports are a few kilobytes; anything far larger is not one.</summary>
    private const int MaxBodyBytes = 256 * 1024;

    private readonly IPostmarkWebhookProcessor _processor;

    public PostmarkWebhookController(IPostmarkWebhookProcessor processor)
    {
        _processor = processor;
    }

    [HttpPost]
    [AllowAnonymous]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> ReceiveAsync()
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        return await _processor.ProcessAsync(Request.Headers[HeaderNames.Authorization], body) switch
        {
            PostmarkWebhookOutcome.Accepted => Ok(),
            PostmarkWebhookOutcome.Unauthorized => Unauthorized(),
            _ => BadRequest()
        };
    }
}
