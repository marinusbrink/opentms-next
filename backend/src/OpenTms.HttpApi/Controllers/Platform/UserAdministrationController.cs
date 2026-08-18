using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenTms.Platform.Administration;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Permissions;

namespace OpenTms.Controllers.Platform;

[ApiController]
[Route("api/platform/administration/users")]
public class UserAdministrationController : OpenTmsController
{
    private readonly IUserAppService _userAppService;

    public UserAdministrationController(IUserAppService userAppService)
    {
        _userAppService = userAppService;
    }

    [HttpGet("")]
    [Authorize(PlatformPermissions.Administration.Users.Default)]
    [ProducesResponseType(typeof(GridResponse<AdministrationUserRowDto>), 200)]
    public async Task<ActionResult<GridResponse<AdministrationUserRowDto>>> GetListAsync(
        [FromQuery] int startRow = 0,
        [FromQuery] int endRow = 50,
        [FromQuery] string? wildcardSearch = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null,
        [FromQuery] string? filterUserName = null,
        [FromQuery] string? filterEmail = null)
    {
        var request = BuildGridRequest(startRow, endRow, wildcardSearch, sortBy, sortDir, filterUserName, filterEmail);
        var result = await _userAppService.GetListAsync(request);
        return Ok(result);
    }

    [HttpPost("")]
    [Authorize(PlatformPermissions.Administration.Users.Create)]
    [ProducesResponseType(typeof(AdministrationUserRowDto), 200)]
    [ProducesResponseType(422)]
    public async Task<ActionResult<AdministrationUserRowDto>> CreateAsync([FromBody] AdministrationUserCreateDto input)
    {
        var result = await _userAppService.CreateAsync(input);
        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(PlatformPermissions.Administration.Users.Update)]
    [ProducesResponseType(typeof(AdministrationUserRowDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(422)]
    public async Task<ActionResult<AdministrationUserRowDto>> UpdateAsync(Guid id, [FromBody] AdministrationUserUpdateDto input)
    {
        var result = await _userAppService.UpdateAsync(id, input);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(PlatformPermissions.Administration.Users.Delete)]
    [ProducesResponseType(204)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteAsync(Guid id)
    {
        await _userAppService.DeleteAsync(id);
        return NoContent();
    }

    [HttpPost("bulk-delete")]
    [Authorize(PlatformPermissions.Administration.Users.BulkDelete)]
    [ProducesResponseType(typeof(BulkDeleteUsersResponseDto), 200)]
    public async Task<ActionResult<BulkDeleteUsersResponseDto>> BulkDeleteAsync([FromBody] BulkDeleteUsersRequestDto input)
    {
        var result = await _userAppService.BulkDeleteAsync(input);
        return Ok(result);
    }

    [HttpPost("{id:guid}/reset-password")]
    [Authorize(PlatformPermissions.Administration.Users.ResetPassword)]
    [ProducesResponseType(204)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> ResetPasswordAsync(Guid id, [FromBody] AdministrationResetPasswordDto input)
    {
        await _userAppService.ResetPasswordAsync(id, input);
        return NoContent();
    }

    private static GridRequest BuildGridRequest(
        int startRow, int endRow,
        string? wildcardSearch,
        string? sortBy, string? sortDir,
        string? filterUserName, string? filterEmail)
    {
        var sortModels = sortBy != null
            ? new System.Collections.Generic.List<SortModel> { new SortModel(sortBy, sortDir ?? "asc") }
            : new System.Collections.Generic.List<SortModel>();

        var columnFilters = new System.Collections.Generic.Dictionary<string, ColumnFilterModel>();
        if (!string.IsNullOrWhiteSpace(filterUserName))
            columnFilters["userName"] = new ColumnFilterModel { FilterType = "text", Type = "contains", Filter = filterUserName };
        if (!string.IsNullOrWhiteSpace(filterEmail))
            columnFilters["email"] = new ColumnFilterModel { FilterType = "text", Type = "contains", Filter = filterEmail };

        return new GridRequest
        {
            StartRow = startRow,
            EndRow = endRow,
            WildcardSearch = wildcardSearch,
            SortModels = sortModels,
            ColumnFilters = columnFilters
        };
    }
}
