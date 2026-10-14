using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace OpenTms.Platform.Editions;

/// <summary>
/// A package of feature values a tenant can be put on, as ABP's commercial Saas module models it:
/// the edition carries a name; its feature values are stored by FeatureManagement under provider
/// <see cref="EditionConsts.FeatureProviderName"/> with the edition id as key. Host-owned, so not
/// <c>IMultiTenant</c>: it lives in <c>platform."Editions"</c> of the host database
/// (docs/designs/tenants-and-editions.md).
/// </summary>
public class Edition : FullAuditedAggregateRoot<Guid>
{
    public string DisplayName { get; private set; } = default!;

    protected Edition()
    {
    }

    public Edition(Guid id, string displayName)
        : base(id)
    {
        SetDisplayName(displayName);
    }

    public void SetDisplayName(string displayName)
    {
        DisplayName = Check.NotNullOrWhiteSpace(displayName, nameof(displayName), EditionConsts.MaxDisplayNameLength).Trim();
    }
}
