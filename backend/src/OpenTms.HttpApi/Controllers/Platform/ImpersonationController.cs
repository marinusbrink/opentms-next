using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Impersonation;
using OpenTms.Platform.Permissions;

namespace OpenTms.Controllers.Platform;

/// <summary>
/// "Log in as" (docs/designs/tenants-and-editions.md) and the way back. The tenant list, a tenant's users and the start
/// need only the Impersonation permission, not the Tenants app, so the Customer Success Manager role can use them from
/// the user menu (docs/designs/63-user-menu.md). Any signed-in user may call the return; the service refuses unless the
/// session carries ABP's impersonator claims, and the ticket names the impersonator as target.
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

    /// <summary>The tenants a host user may log in to, paged and searchable by name.</summary>
    [HttpGet("tenants")]
    [Authorize(PlatformPermissions.Tenants.Impersonation)]
    [ProducesResponseType(typeof(GridResponse<ImpersonationTenantRowDto>), 200)]
    public async Task<ActionResult<GridResponse<ImpersonationTenantRowDto>>> GetTenantsAsync(
        [FromQuery] int startRow = 0,
        [FromQuery] int endRow = 50,
        [FromQuery] string? wildcardSearch = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null) =>
        Ok(await _impersonationAppService.GetTenantsAsync(BuildGridRequest(startRow, endRow, wildcardSearch, sortBy, sortDir)));

    /// <summary>The tenant's users for the "Log in as" dialog.</summary>
    [HttpGet("tenants/{id:guid}/users")]
    [Authorize(PlatformPermissions.Tenants.Impersonation)]
    [ProducesResponseType(typeof(GridResponse<TenantUserRowDto>), 200)]
    public async Task<ActionResult<GridResponse<TenantUserRowDto>>> GetUsersAsync(
        Guid id,
        [FromQuery] int startRow = 0,
        [FromQuery] int endRow = 50,
        [FromQuery] string? wildcardSearch = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null) =>
        Ok(await _impersonationAppService.GetTenantUsersAsync(id, BuildGridRequest(startRow, endRow, wildcardSearch, sortBy, sortDir)));

    /// <summary>"Log in as" a user of this tenant: returns the auth-server URL that switches the browser session.</summary>
    [HttpPost("tenants/{id:guid}/users/{userId:guid}")]
    [Authorize(PlatformPermissions.Tenants.Impersonation)]
    [ProducesResponseType(typeof(ImpersonationRedirectDto), 200)]
    public async Task<ActionResult<ImpersonationRedirectDto>> ImpersonateAsync(Guid id, Guid userId) =>
        Ok(await _impersonationAppService.StartAsync(id, userId));

    [HttpPost("return")]
    [ProducesResponseType(typeof(ImpersonationRedirectDto), 200)]
    public async Task<ActionResult<ImpersonationRedirectDto>> ReturnAsync() =>
        Ok(await _impersonationAppService.ReturnAsync());

    private static GridRequest BuildGridRequest(int startRow, int endRow, string? wildcardSearch, string? sortBy, string? sortDir) => new()
    {
        StartRow = startRow,
        EndRow = endRow,
        WildcardSearch = wildcardSearch,
        SortModels = sortBy != null ? new List<SortModel> { new(sortBy, sortDir ?? "asc") } : new List<SortModel>()
    };
}
