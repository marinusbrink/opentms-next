using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenTms.Platform.Impersonation;

namespace OpenTms.Controllers.Platform;

/// <summary>
/// The way back from "Log in as" (docs/designs/tenants-and-editions.md). Any signed-in user may call it; the service
/// refuses unless the session carries ABP's impersonator claims, and the ticket names the impersonator as target.
/// </summary>
[ApiController]
[Route("api/platform/impersonation")]
[Authorize]
public class ImpersonationController : OpenTmsController
{
    private readonly IImpersonationAppService _impersonationAppService;

    public ImpersonationController(IImpersonationAppService impersonationAppService)
    {
        _impersonationAppService = impersonationAppService;
    }

    [HttpPost("return")]
    [ProducesResponseType(typeof(ImpersonationRedirectDto), 200)]
    public async Task<ActionResult<ImpersonationRedirectDto>> ReturnAsync() =>
        Ok(await _impersonationAppService.ReturnAsync());
}
