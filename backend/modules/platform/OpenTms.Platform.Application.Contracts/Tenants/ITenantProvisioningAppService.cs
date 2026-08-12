using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace OpenTms.Platform.Tenants;

/* Tenant provisioning is a Platform (host-side) concern. The provisioning default is
 * database-per-tenant: a new tenant gets its own PostgreSQL database containing all module
 * schemas, registered as the tenant's "Default" connection string. A shared-database tier
 * simply skips database creation (the ABP default: no connection string = host database). */
public interface ITenantProvisioningAppService : IApplicationService
{
    Task<TenantProvisioningResultDto> RequestProvisioningAsync(TenantProvisioningRequestDto input);
}
