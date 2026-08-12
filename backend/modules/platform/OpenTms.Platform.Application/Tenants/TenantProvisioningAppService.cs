using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Platform.Permissions;
using Volo.Abp.BackgroundJobs;

namespace OpenTms.Platform.Tenants;

[Authorize(PlatformPermissions.TenantProvisioning.Manage)]
public class TenantProvisioningAppService : PlatformAppServiceBase, ITenantProvisioningAppService
{
    private readonly IBackgroundJobManager _backgroundJobManager;

    public TenantProvisioningAppService(IBackgroundJobManager backgroundJobManager)
    {
        _backgroundJobManager = backgroundJobManager;
    }

    public async Task<TenantProvisioningResultDto> RequestProvisioningAsync(TenantProvisioningRequestDto input)
    {
        /* The heavy lifting (CREATE DATABASE, migrations, seed) runs in a background job
         * (Hangfire) — provisioning must never block a request (performance budgets:
         * heavy operations are always asynchronous). */
        var jobId = await _backgroundJobManager.EnqueueAsync(new TenantProvisioningArgs
        {
            TenantId = input.TenantId,
            DatabaseName = input.DatabaseName
        });

        return new TenantProvisioningResultDto
        {
            TenantId = input.TenantId,
            JobId = jobId,
            Status = "Queued"
        };
    }
}
