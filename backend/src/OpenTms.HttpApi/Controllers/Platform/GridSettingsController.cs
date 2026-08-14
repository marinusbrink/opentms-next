using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenTms.Platform.Grid;

namespace OpenTms.Controllers.Platform;

[Authorize]
[ApiController]
[Route("api/platform/grid-settings")]
public class GridSettingsController : OpenTmsController
{
    private static readonly Regex GridIdPattern = new("^[a-zA-Z0-9_.-]{1,100}$", RegexOptions.Compiled);

    private readonly IGridSettingsAppService _gridSettingsAppService;

    public GridSettingsController(IGridSettingsAppService gridSettingsAppService)
    {
        _gridSettingsAppService = gridSettingsAppService;
    }

    [HttpGet("{gridId}")]
    [ProducesResponseType(typeof(GridSettingsDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<GridSettingsDto>> GetAsync([FromRoute] string gridId)
    {
        if (!GridIdPattern.IsMatch(gridId))
        {
            return BadRequest("Invalid gridId format.");
        }

        var result = await _gridSettingsAppService.GetAsync(gridId);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("{gridId}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> SetAsync([FromRoute] string gridId, [FromBody] GridSettingsDto input)
    {
        if (!GridIdPattern.IsMatch(gridId))
        {
            return BadRequest("Invalid gridId format.");
        }

        await _gridSettingsAppService.SetAsync(gridId, input);
        return NoContent();
    }

    [HttpDelete("{gridId}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ResetAsync([FromRoute] string gridId)
    {
        if (!GridIdPattern.IsMatch(gridId))
        {
            return BadRequest("Invalid gridId format.");
        }

        await _gridSettingsAppService.ResetAsync(gridId);
        return NoContent();
    }
}
