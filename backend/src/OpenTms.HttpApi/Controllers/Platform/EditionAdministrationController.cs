using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenTms.Platform.Editions;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Permissions;

namespace OpenTms.Controllers.Platform;

/// <summary>
/// The Editions app (host only): docs/designs/tenants-and-editions.md. Feature values are edited through ABP's
/// <c>api/feature-management/features?providerName=E&amp;providerKey={id}</c>.
/// </summary>
[ApiController]
[Route("api/platform/editions")]
[Authorize(PlatformPermissions.Editions.Default)]
public class EditionAdministrationController : OpenTmsController
{
    private readonly IEditionAdministrationAppService _editionAppService;

    public EditionAdministrationController(IEditionAdministrationAppService editionAppService)
    {
        _editionAppService = editionAppService;
    }

    [HttpGet("")]
    [ProducesResponseType(typeof(GridResponse<EditionRowDto>), 200)]
    public async Task<ActionResult<GridResponse<EditionRowDto>>> GetListAsync(
        [FromQuery] int startRow = 0,
        [FromQuery] int endRow = 50,
        [FromQuery] string? wildcardSearch = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null)
    {
        var request = new GridRequest
        {
            StartRow = startRow,
            EndRow = endRow,
            WildcardSearch = wildcardSearch,
            SortModels = sortBy != null ? new List<SortModel> { new(sortBy, sortDir ?? "asc") } : new List<SortModel>()
        };

        return Ok(await _editionAppService.GetListAsync(request));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(EditionRowDto), 200)]
    public async Task<ActionResult<EditionRowDto>> GetAsync(Guid id) => Ok(await _editionAppService.GetAsync(id));

    [HttpPost("")]
    [Authorize(PlatformPermissions.Editions.Create)]
    [ProducesResponseType(typeof(EditionRowDto), 200)]
    public async Task<ActionResult<EditionRowDto>> CreateAsync([FromBody] EditionCreateUpdateDto input) =>
        Ok(await _editionAppService.CreateAsync(input));

    [HttpPut("{id:guid}")]
    [Authorize(PlatformPermissions.Editions.Update)]
    [ProducesResponseType(typeof(EditionRowDto), 200)]
    public async Task<ActionResult<EditionRowDto>> UpdateAsync(Guid id, [FromBody] EditionCreateUpdateDto input) =>
        Ok(await _editionAppService.UpdateAsync(id, input));

    [HttpDelete("{id:guid}")]
    [Authorize(PlatformPermissions.Editions.Delete)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(EditionDeleteCheckDto), 409)]
    public async Task<IActionResult> DeleteAsync(Guid id)
    {
        try
        {
            await _editionAppService.DeleteAsync(id);
            return NoContent();
        }
        catch (EditionInUseException ex)
        {
            return Conflict(new EditionDeleteCheckDto { DisplayName = ex.DisplayName, TenantCount = ex.TenantCount });
        }
    }
}
