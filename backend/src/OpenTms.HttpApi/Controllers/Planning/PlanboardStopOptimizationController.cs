using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenTms.PlanningExecution.Planboard;

namespace OpenTms.Controllers.Planning;

/// <summary>
/// Design 68 §5: <c>POST /api/planning/planboard/{id}/optimize-stops</c> answers 202 Accepted at once; the optimisation runs
/// as a background job in the tenant's context. A conventional controller cannot answer 202, so the application service is
/// <c>[RemoteService(IsEnabled = false)]</c> and checks the permission and feature itself.
/// </summary>
[ApiController]
[Route("api/planning/planboard")]
[Authorize]
public class PlanboardStopOptimizationController : OpenTmsController
{
    private readonly IPlanboardStopOptimizationAppService _optimization;

    public PlanboardStopOptimizationController(IPlanboardStopOptimizationAppService optimization)
    {
        _optimization = optimization;
    }

    [HttpPost("{id:guid}/optimize-stops")]
    [ProducesResponseType(typeof(PlanboardOptimizeStopsDto), 202)]
    public async Task<ActionResult<PlanboardOptimizeStopsDto>> OptimizeStopsAsync([FromRoute] Guid id)
        => Accepted(await _optimization.OptimizeStopsAsync(id));
}
