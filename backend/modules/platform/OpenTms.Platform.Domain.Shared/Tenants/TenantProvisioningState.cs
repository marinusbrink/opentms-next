namespace OpenTms.Platform.Tenants;

/// <summary>Where provisioning of a tenant's own database stands (docs/designs/tenants-and-editions.md).</summary>
public static class TenantProvisioningState
{
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}
