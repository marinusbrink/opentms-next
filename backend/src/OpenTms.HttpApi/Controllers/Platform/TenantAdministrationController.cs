using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenTms.Platform.Editions;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Impersonation;
using OpenTms.Platform.Permissions;
using OpenTms.Platform.Tenants;

namespace OpenTms.Controllers.Platform;

/// <summary>The Tenants app (host only): docs/designs/tenants-and-editions.md.</summary>
[ApiController]
[Route("api/platform/tenants")]
[Authorize(PlatformPermissions.Tenants.Default)]
public class TenantAdministrationController : OpenTmsController
{
    private readonly ITenantAdministrationAppService _tenantAppService;
    private readonly IImpersonationAppService _impersonationAppService;

    public TenantAdministrationController(
        ITenantAdministrationAppService tenantAppService,
        IImpersonationAppService impersonationAppService)
    {
        _tenantAppService = tenantAppService;
        _impersonationAppService = impersonationAppService;
    }

    [HttpGet("")]
    [ProducesResponseType(typeof(GridResponse<TenantRowDto>), 200)]
    public async Task<ActionResult<GridResponse<TenantRowDto>>> GetListAsync(
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

        return Ok(await _tenantAppService.GetListAsync(request));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TenantDto), 200)]
    public async Task<ActionResult<TenantDto>> GetAsync(Guid id) => Ok(await _tenantAppService.GetAsync(id));

    /// <summary>The editions to choose from in the tenant form.</summary>
    [HttpGet("edition-lookup")]
    [ProducesResponseType(typeof(IReadOnlyList<EditionLookupDto>), 200)]
    public async Task<ActionResult<IReadOnlyList<EditionLookupDto>>> GetEditionLookupAsync() =>
        Ok(await _tenantAppService.GetEditionLookupAsync());

    [HttpGet("connection-preview")]
    [Authorize(PlatformPermissions.Tenants.Create)]
    [ProducesResponseType(typeof(TenantConnectionPreviewDto), 200)]
    public async Task<ActionResult<TenantConnectionPreviewDto>> GetConnectionPreviewAsync([FromQuery] string? name = null) =>
        Ok(await _tenantAppService.GetConnectionPreviewAsync(name ?? string.Empty));

    [HttpPost("")]
    [Authorize(PlatformPermissions.Tenants.Create)]
    [ProducesResponseType(typeof(TenantDto), 200)]
    public async Task<ActionResult<TenantDto>> CreateAsync([FromBody] TenantCreateDto input) =>
        Ok(await _tenantAppService.CreateAsync(input));

    [HttpPut("{id:guid}")]
    [Authorize(PlatformPermissions.Tenants.Update)]
    [ProducesResponseType(typeof(TenantDto), 200)]
    public async Task<ActionResult<TenantDto>> UpdateAsync(Guid id, [FromBody] TenantUpdateDto input) =>
        Ok(await _tenantAppService.UpdateAsync(id, input));

    [HttpDelete("{id:guid}")]
    [Authorize(PlatformPermissions.Tenants.Delete)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteAsync(Guid id)
    {
        await _tenantAppService.DeleteAsync(id);
        return NoContent();
    }

    [HttpPost("{id:guid}/retry-provisioning")]
    [Authorize(PlatformPermissions.Tenants.Create)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> RetryProvisioningAsync(Guid id)
    {
        await _tenantAppService.RetryProvisioningAsync(id);
        return NoContent();
    }

    [HttpPost("{id:guid}/resend-invitation")]
    [Authorize(PlatformPermissions.Tenants.Update)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ResendInvitationAsync(Guid id)
    {
        await _tenantAppService.ResendInvitationAsync(id);
        return NoContent();
    }

    /// <summary>The tenant's users for the "Log in as" dialog.</summary>
    [HttpGet("{id:guid}/users")]
    [Authorize(PlatformPermissions.Tenants.Impersonation)]
    [ProducesResponseType(typeof(GridResponse<TenantUserRowDto>), 200)]
    public async Task<ActionResult<GridResponse<TenantUserRowDto>>> GetUsersAsync(
        Guid id,
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

        return Ok(await _impersonationAppService.GetTenantUsersAsync(id, request));
    }

    /// <summary>"Log in as" a user of this tenant: returns the auth-server URL that switches the browser session.</summary>
    [HttpPost("{id:guid}/impersonate/{userId:guid}")]
    [Authorize(PlatformPermissions.Tenants.Impersonation)]
    [ProducesResponseType(typeof(ImpersonationRedirectDto), 200)]
    public async Task<ActionResult<ImpersonationRedirectDto>> ImpersonateAsync(Guid id, Guid userId) =>
        Ok(await _impersonationAppService.StartAsync(id, userId));
}
