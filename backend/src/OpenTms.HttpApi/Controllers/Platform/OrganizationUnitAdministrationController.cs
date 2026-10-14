using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenTms.Platform.Administration;
using OpenTms.Platform.Grid;
using OpenTms.Features;
using OpenTms.Platform.Features;
using OpenTms.Platform.Permissions;

namespace OpenTms.Controllers.Platform;

[ApiController]
[Route("api/platform/administration/organization-units")]
public class OrganizationUnitAdministrationController : OpenTmsController
{
    private readonly IOrganizationUnitAppService _organizationUnitAppService;

    public OrganizationUnitAdministrationController(IOrganizationUnitAppService organizationUnitAppService)
    {
        _organizationUnitAppService = organizationUnitAppService;
    }

    /// <summary>One level of the tree (<paramref name="parentId"/> null = the roots), or a flat search over the whole tree.</summary>
    [RequiresTenantAppFeature(AppFeatures.Administration.Default, AppFeatures.Administration.OrganizationUnits)]
    [HttpGet("")]
    [Authorize(PlatformPermissions.Administration.OrganizationUnits.Default)]
    [ProducesResponseType(typeof(GridResponse<AdministrationOrganizationUnitRowDto>), 200)]
    public async Task<ActionResult<GridResponse<AdministrationOrganizationUnitRowDto>>> GetListAsync(
        [FromQuery] int startRow = 0,
        [FromQuery] int endRow = 50,
        [FromQuery] string? wildcardSearch = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null,
        [FromQuery] Guid? parentId = null)
    {
        var request = BuildGridRequest(startRow, endRow, wildcardSearch, sortBy, sortDir);
        var result = await _organizationUnitAppService.GetListAsync(request, parentId);
        return Ok(result);
    }

    /* The picker other screens use (user editor, relation editor): permission only, no app feature. */
    /// <summary>The whole tree as a flat list in tree order (the parent picker).</summary>
    [HttpGet("all")]
    [Authorize(PlatformPermissions.Administration.OrganizationUnits.Default)]
    [ProducesResponseType(typeof(List<AdministrationOrganizationUnitRowDto>), 200)]
    public async Task<ActionResult<List<AdministrationOrganizationUnitRowDto>>> GetAllAsync()
    {
        var result = await _organizationUnitAppService.GetAllAsync();
        return Ok(result);
    }

    [RequiresTenantAppFeature(AppFeatures.Administration.Default, AppFeatures.Administration.OrganizationUnits)]
    [HttpPost("")]
    [Authorize(PlatformPermissions.Administration.OrganizationUnits.Create)]
    [ProducesResponseType(typeof(AdministrationOrganizationUnitRowDto), 200)]
    public async Task<ActionResult<AdministrationOrganizationUnitRowDto>> CreateAsync([FromBody] AdministrationOrganizationUnitCreateDto input)
    {
        var result = await _organizationUnitAppService.CreateAsync(input);
        return Ok(result);
    }

    [RequiresTenantAppFeature(AppFeatures.Administration.Default, AppFeatures.Administration.OrganizationUnits)]
    [HttpPut("{id:guid}")]
    [Authorize(PlatformPermissions.Administration.OrganizationUnits.Update)]
    [ProducesResponseType(typeof(AdministrationOrganizationUnitRowDto), 200)]
    public async Task<ActionResult<AdministrationOrganizationUnitRowDto>> UpdateAsync(Guid id, [FromBody] AdministrationOrganizationUnitUpdateDto input)
    {
        var result = await _organizationUnitAppService.UpdateAsync(id, input);
        return Ok(result);
    }

    [RequiresTenantAppFeature(AppFeatures.Administration.Default, AppFeatures.Administration.OrganizationUnits)]
    [HttpDelete("{id:guid}")]
    [Authorize(PlatformPermissions.Administration.OrganizationUnits.Delete)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(OrganizationUnitDeleteCheckDto), 409)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteAsync(Guid id, [FromQuery] bool force = false)
    {
        try
        {
            await _organizationUnitAppService.DeleteAsync(id, force);
            return NoContent();
        }
        catch (OrganizationUnitInUseException ex)
        {
            return Conflict(new OrganizationUnitDeleteCheckDto
            {
                DisplayName = ex.DisplayName,
                ChildCount = ex.ChildCount,
                MemberCount = ex.MemberCount
            });
        }
    }

    [RequiresTenantAppFeature(AppFeatures.Administration.Default, AppFeatures.Administration.OrganizationUnits)]
    [HttpGet("{id:guid}/members")]
    [Authorize(PlatformPermissions.Administration.OrganizationUnits.Default)]
    [ProducesResponseType(typeof(GridResponse<AdministrationUserRowDto>), 200)]
    public async Task<ActionResult<GridResponse<AdministrationUserRowDto>>> GetMembersAsync(
        Guid id,
        [FromQuery] int startRow = 0,
        [FromQuery] int endRow = 50,
        [FromQuery] string? wildcardSearch = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null)
    {
        var request = BuildGridRequest(startRow, endRow, wildcardSearch, sortBy, sortDir);
        var result = await _organizationUnitAppService.GetMembersAsync(id, request);
        return Ok(result);
    }

    [RequiresTenantAppFeature(AppFeatures.Administration.Default, AppFeatures.Administration.OrganizationUnits)]
    /// <summary>The users that are not yet members: the picker list for "Add members".</summary>
    [HttpGet("{id:guid}/unadded-users")]
    [Authorize(PlatformPermissions.Administration.OrganizationUnits.ManageMembers)]
    [ProducesResponseType(typeof(GridResponse<AdministrationUserRowDto>), 200)]
    public async Task<ActionResult<GridResponse<AdministrationUserRowDto>>> GetUnaddedUsersAsync(
        Guid id,
        [FromQuery] int startRow = 0,
        [FromQuery] int endRow = 50,
        [FromQuery] string? wildcardSearch = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null)
    {
        var request = BuildGridRequest(startRow, endRow, wildcardSearch, sortBy, sortDir);
        var result = await _organizationUnitAppService.GetUnaddedUsersAsync(id, request);
        return Ok(result);
    }

    [RequiresTenantAppFeature(AppFeatures.Administration.Default, AppFeatures.Administration.OrganizationUnits)]
    [HttpPost("{id:guid}/members")]
    [Authorize(PlatformPermissions.Administration.OrganizationUnits.ManageMembers)]
    [ProducesResponseType(typeof(OrganizationUnitMembersResponseDto), 200)]
    public async Task<ActionResult<OrganizationUnitMembersResponseDto>> AddMembersAsync(Guid id, [FromBody] OrganizationUnitMembersRequestDto input)
    {
        var result = await _organizationUnitAppService.AddMembersAsync(id, input);
        return Ok(result);
    }

    [RequiresTenantAppFeature(AppFeatures.Administration.Default, AppFeatures.Administration.OrganizationUnits)]
    [HttpPost("{id:guid}/members/remove")]
    [Authorize(PlatformPermissions.Administration.OrganizationUnits.ManageMembers)]
    [ProducesResponseType(typeof(OrganizationUnitMembersResponseDto), 200)]
    public async Task<ActionResult<OrganizationUnitMembersResponseDto>> RemoveMembersAsync(Guid id, [FromBody] OrganizationUnitMembersRequestDto input)
    {
        var result = await _organizationUnitAppService.RemoveMembersAsync(id, input);
        return Ok(result);
    }

    private static GridRequest BuildGridRequest(int startRow, int endRow, string? wildcardSearch, string? sortBy, string? sortDir)
    {
        var sortModels = sortBy != null
            ? new List<SortModel> { new SortModel(sortBy, sortDir ?? "asc") }
            : new List<SortModel>();

        return new GridRequest
        {
            StartRow = startRow,
            EndRow = endRow,
            WildcardSearch = wildcardSearch,
            SortModels = sortModels
        };
    }
}
