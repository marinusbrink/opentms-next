using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Platform.Permissions;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Data;
using Volo.Abp.TenantManagement;

namespace OpenTms.Platform.Tenants;

/* The older provisioning endpoint. It follows the Tenants app's rules (docs/designs/tenants-and-editions.md): host
 * only, and only a tenant whose provisioning failed is queued again, so two runs never overlap. The work itself
 * runs in TenantProvisioningJob, never in the request (performance budgets: heavy operations are asynchronous). */
[Authorize(PlatformPermissions.TenantProvisioning.Manage)]
public class TenantProvisioningAppService : PlatformAppServiceBase, ITenantProvisioningAppService
{
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly ITenantRepository _tenantRepository;

    public TenantProvisioningAppService(IBackgroundJobManager backgroundJobManager, ITenantRepository tenantRepository)
    {
        _backgroundJobManager = backgroundJobManager;
        _tenantRepository = tenantRepository;
    }

    public async Task<TenantProvisioningResultDto> RequestProvisioningAsync(TenantProvisioningRequestDto input)
    {
        HostOnlyGuard.Ensure(CurrentTenant);

        var tenant = await _tenantRepository.GetAsync(input.TenantId);
        if (tenant.GetProperty<string?>(TenantProperties.ProvisioningState) != TenantProvisioningState.Failed)
        {
            throw new UserFriendlyException(L["Tenants:RetryOnlyWhenFailed"]);
        }

        tenant.SetProperty(TenantProperties.ProvisioningState, TenantProvisioningState.Queued);
        await _tenantRepository.UpdateAsync(tenant, autoSave: true);

        string? jobId = null;
        CurrentUnitOfWork!.OnCompleted(async () =>
            jobId = await _backgroundJobManager.EnqueueAsync(new TenantProvisioningArgs { TenantId = input.TenantId }));

        return new TenantProvisioningResultDto
        {
            TenantId = input.TenantId,
            JobId = jobId,
            Status = TenantProvisioningState.Queued
        };
    }
}
