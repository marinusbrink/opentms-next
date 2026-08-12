using System;

namespace OpenTms.Platform.Tenants;

public class TenantProvisioningRequestDto
{
    public Guid TenantId { get; set; }

    /// <summary>Optional database name override; defaults to a name derived from the tenant.</summary>
    public string? DatabaseName { get; set; }
}
