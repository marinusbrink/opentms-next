using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenTms.Platform.Emailing;
using OpenTms.Platform.Permissions;

namespace OpenTms.Controllers.Platform;

/// <summary>
/// The host's e-mail settings in the Administration app (docs/designs/64-email-delivery-postmark.md, "Host e-mail
/// settings"): one configuration of the mail service for every tenant, and a test mail. Host only: the permissions are
/// host-side and the service guards the side again.
/// </summary>
[ApiController]
[Route("api/platform/administration/email-settings")]
[Authorize(PlatformPermissions.Administration.EmailSettings.Default)]
public class EmailSettingsAdministrationController : OpenTmsController
{
    private readonly IEmailSettingsAppService _settings;

    public EmailSettingsAdministrationController(IEmailSettingsAppService settings)
    {
        _settings = settings;
    }

    [HttpGet]
    [ProducesResponseType(typeof(EmailSettingsDto), 200)]
    public async Task<ActionResult<EmailSettingsDto>> GetAsync() => Ok(await _settings.GetAsync());

    [HttpPut]
    [Authorize(PlatformPermissions.Administration.EmailSettings.Update)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> UpdateAsync([FromBody] UpdateEmailSettingsDto input)
    {
        await _settings.UpdateAsync(input);
        return NoContent();
    }

    [HttpPost("test")]
    [Authorize(PlatformPermissions.Administration.EmailSettings.SendTest)]
    [ProducesResponseType(typeof(TestEmailResultDto), 200)]
    public async Task<ActionResult<TestEmailResultDto>> SendTestAsync([FromBody] SendTestEmailDto input) => Ok(await _settings.SendTestAsync(input));
}
