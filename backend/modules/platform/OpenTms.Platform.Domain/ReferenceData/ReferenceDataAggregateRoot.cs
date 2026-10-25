using System;
using OpenTms.Platform.Localization;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.ReferenceData;

/// <summary>
/// The base of every domain's reference-data aggregate built on the Platform base (design 50; Master Data's own copy is
/// design 41): audit, soft delete and the concurrency stamp from ABP, a nullable tenant (assessment §3.6, §4: host rows
/// are parked but must stay possible) and the shared description. Never defaults the tenant; the ABP data filter
/// scopes every query.
/// </summary>
public abstract class ReferenceDataAggregateRoot : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public string? Description { get; private set; }

    protected ReferenceDataAggregateRoot()
    {
    }

    protected ReferenceDataAggregateRoot(Guid id, Guid? tenantId)
        : base(id)
    {
        TenantId = tenantId;
    }

    public void SetDescription(string? description)
    {
        var value = description?.Trim();
        Description = string.IsNullOrEmpty(value)
            ? null
            : Check.Length(value, nameof(description), ReferenceDataConsts.MaxDescriptionLength);
    }

    protected static string NormalizeName(string name)
        => Check.NotNullOrWhiteSpace(name, nameof(name), ReferenceDataConsts.MaxNameLength).Trim();

    /// <summary>Codes are stored trimmed and upper case; letters, digits, '-' and '_' only.</summary>
    public static string NormalizeCode(string code)
    {
        var value = Check.NotNullOrWhiteSpace(code, nameof(code), ReferenceDataConsts.MaxCodeLength).Trim().ToUpperInvariant();
        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')
            {
                throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "code").WithData("value", value);
            }
        }

        return value;
    }

    protected static LocalizedName RequireName(LocalizedName name) => Check.NotNull(name, nameof(name));
}
