using Volo.Abp;

namespace OpenTms.Platform.Editions;

/// <summary>An edition with tenants on it cannot be deleted; move the tenants first.</summary>
public class EditionInUseException : BusinessException
{
    public string DisplayName { get; }
    public int TenantCount { get; }

    public EditionInUseException(string displayName, int tenantCount)
        : base("Editions:InUse")
    {
        DisplayName = displayName;
        TenantCount = tenantCount;
    }
}
