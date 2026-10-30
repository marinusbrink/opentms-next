using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenTms.Platform.Accounts;

namespace OpenTms.Controllers.Platform;

/// <summary>
/// The user menu's other accounts of the signed-in user's e-mail address and the switch to one of them
/// (docs/designs/63-user-menu.md).
/// </summary>
[ApiController]
[Route("api/platform/my-accounts")]
[Authorize]
public class MyAccountsController : OpenTmsController
{
    private readonly IMyAccountsAppService _myAccountsAppService;

    public MyAccountsController(IMyAccountsAppService myAccountsAppService)
    {
        _myAccountsAppService = myAccountsAppService;
    }

    [HttpGet("")]
    [ProducesResponseType(typeof(List<MyAccountDto>), 200)]
    public async Task<ActionResult<IReadOnlyList<MyAccountDto>>> GetListAsync() => Ok(await _myAccountsAppService.GetListAsync());

    /// <summary>Returns the auth-server URL that switches the browser session to that account.</summary>
    [HttpPost("switch")]
    [ProducesResponseType(typeof(AccountSwitchRedirectDto), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<AccountSwitchRedirectDto>> SwitchAsync([FromBody] SwitchAccountDto input) =>
        Ok(await _myAccountsAppService.SwitchAsync(input));

    /// <summary>Forgets the remembered switch to the account in <paramref name="tenantId"/> (absent: the host).</summary>
    [HttpDelete("remembered")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ForgetAsync([FromQuery] Guid? tenantId)
    {
        await _myAccountsAppService.ForgetAsync(tenantId);
        return NoContent();
    }
}
