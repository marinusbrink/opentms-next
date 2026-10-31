using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenTms.Platform.Editions;
using OpenTms.Platform.Grid;
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

    public TenantAdministrationController(ITenantAdministrationAppService tenantAppService)
    {
        _tenantAppService = tenantAppService;
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

    /// <summary>"Reset to dataset" (docs/designs/65-tenant-test-datasets.md): queued; the list shows its progress.</summary>
    [HttpPost("{id:guid}/reset-to-dataset")]
    [Authorize(PlatformPermissions.Tenants.ResetToDataset)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ResetToDatasetAsync(Guid id, [FromBody] TenantResetToDatasetDto input)
    {
        await _tenantAppService.ResetToDatasetAsync(id, input);
        return NoContent();
    }
}
