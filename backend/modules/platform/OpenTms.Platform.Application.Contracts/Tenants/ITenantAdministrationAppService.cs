using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTms.Platform.Editions;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.Platform.Tenants;

/// <summary>
/// The Tenants app (host only, docs/designs/tenants-and-editions.md). Every tenant created here gets its own
/// database, provisioned in the background; deleting one drops that database in the background.
/// </summary>
public interface ITenantAdministrationAppService : IApplicationService
{
    Task<GridResponse<TenantRowDto>> GetListAsync(GridRequest request);

    Task<TenantDto> GetAsync(Guid id);

    /// <summary>The editions to choose from in the tenant form (Tenants permission is enough).</summary>
    Task<IReadOnlyList<EditionLookupDto>> GetEditionLookupAsync();

    Task<TenantConnectionPreviewDto> GetConnectionPreviewAsync(string name);

    Task<TenantDto> CreateAsync(TenantCreateDto input);

    Task<TenantDto> UpdateAsync(Guid id, TenantUpdateDto input);

    Task DeleteAsync(Guid id);

    /// <summary>Queues provisioning again for a tenant whose last run failed; every step is idempotent.</summary>
    Task RetryProvisioningAsync(Guid id);

    /// <summary>Queues a new set-password invitation for the tenant administrator.</summary>
    Task ResendInvitationAsync(Guid id);
}
