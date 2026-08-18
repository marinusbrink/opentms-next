using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenTms.Platform.Administration;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Permissions;

namespace OpenTms.Controllers.Platform;

[ApiController]
[Route("api/platform/administration/roles")]
public class UserRoleAdministrationController : OpenTmsController
{
    private readonly IUserRoleAppService _userRoleAppService;

    public UserRoleAdministrationController(IUserRoleAppService userRoleAppService)
    {
        _userRoleAppService = userRoleAppService;
    }

    [HttpGet("")]
    [Authorize(PlatformPermissions.Administration.Roles.Default)]
    [ProducesResponseType(typeof(GridResponse<AdministrationRoleRowDto>), 200)]
    public async Task<ActionResult<GridResponse<AdministrationRoleRowDto>>> GetListAsync(
        [FromQuery] int startRow = 0,
        [FromQuery] int endRow = 50,
        [FromQuery] string? wildcardSearch = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null)
    {
        var sortModels = sortBy != null
            ? new System.Collections.Generic.List<SortModel> { new SortModel(sortBy, sortDir ?? "asc") }
            : new System.Collections.Generic.List<SortModel>();

        var request = new GridRequest
        {
            StartRow = startRow,
            EndRow = endRow,
            WildcardSearch = wildcardSearch,
            SortModels = sortModels
        };

        var result = await _userRoleAppService.GetListAsync(request);
        return Ok(result);
    }

    [HttpPost("")]
    [Authorize(PlatformPermissions.Administration.Roles.Create)]
    [ProducesResponseType(typeof(AdministrationRoleRowDto), 200)]
    public async Task<ActionResult<AdministrationRoleRowDto>> CreateAsync([FromBody] AdministrationRoleCreateUpdateDto input)
    {
        var result = await _userRoleAppService.CreateAsync(input);
        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(PlatformPermissions.Administration.Roles.Update)]
    [ProducesResponseType(typeof(AdministrationRoleRowDto), 200)]
    public async Task<ActionResult<AdministrationRoleRowDto>> UpdateAsync(Guid id, [FromBody] AdministrationRoleCreateUpdateDto input)
    {
        var result = await _userRoleAppService.UpdateAsync(id, input);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(PlatformPermissions.Administration.Roles.Delete)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(RoleDeleteCheckDto), 409)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteAsync(Guid id, [FromQuery] bool force = false)
    {
        try
        {
            await _userRoleAppService.DeleteAsync(id, force);
            return NoContent();
        }
        catch (RoleHasUsersException ex)
        {
            return Conflict(new RoleDeleteCheckDto { RoleName = ex.RoleName, UserCount = ex.UserCount });
        }
    }
}
