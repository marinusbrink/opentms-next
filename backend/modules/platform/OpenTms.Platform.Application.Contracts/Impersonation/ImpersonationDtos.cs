using System;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.Platform.Impersonation;

/// <summary>A user of a tenant, as the host's "Log in as" dialog lists it. No email address: the name identifies.</summary>
public record TenantUserRowDto
{
    public Guid Id { get; init; }
    public string UserName { get; init; } = default!;
    public string? Name { get; init; }
    public string? Surname { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>A tenant a host user with the Impersonation permission may log in to (the user menu's tenant list).</summary>
public record ImpersonationTenantRowDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
}

/// <summary>Where the browser goes next: the auth server's page that switches the session.</summary>
public record ImpersonationRedirectDto
{
    public string Url { get; init; } = default!;
}

/// <summary>
/// "Log in as" (PO 2026-10-14, docs/designs/tenants-and-editions.md): a host user with the Impersonation permission
/// logs in as a chosen user of a tenant and can go back to the host session. ABP's impersonator claims travel along,
/// so the audit log records who really acted.
/// </summary>
public interface IImpersonationAppService : IApplicationService
{
    /// <summary>
    /// The tenants, paged and searchable by name: the user menu's list for a Customer Success Manager
    /// (docs/designs/63-user-menu.md). Needs only the Impersonation permission, not the Tenants app.
    /// </summary>
    Task<GridResponse<ImpersonationTenantRowDto>> GetTenantsAsync(GridRequest request);

    Task<GridResponse<TenantUserRowDto>> GetTenantUsersAsync(Guid tenantId, GridRequest request);

    Task<ImpersonationRedirectDto> StartAsync(Guid tenantId, Guid userId);

    /// <summary>Back to the impersonator's own session; only while impersonating.</summary>
    Task<ImpersonationRedirectDto> ReturnAsync();
}
