using System;
using Volo.Abp.BackgroundJobs;

namespace OpenTms.Platform.Tenants;

[BackgroundJobName("Platform.TenantProvisioning")]
public class TenantProvisioningArgs
{
    public Guid TenantId { get; set; }

    public string? DatabaseName { get; set; }
}
