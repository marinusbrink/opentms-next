using System;

namespace OpenTms.Platform.Tenants;

public class TenantProvisioningResultDto
{
    public Guid TenantId { get; set; }

    public string? JobId { get; set; }

    public string Status { get; set; } = string.Empty;
}
