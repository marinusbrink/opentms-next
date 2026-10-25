using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform;

/// <summary>
/// The base of every Platform master-data aggregate (design 54), the Platform counterpart of Master Data's
/// MasterDataAggregateRoot (Platform references no domain module): audit, soft delete and the concurrency stamp from
/// ABP, a nullable tenant (host rows are parked but must stay possible) and the shared description field. Never
/// defaults the tenant; the ABP data filter scopes every query.
/// </summary>
public abstract class PlatformMasterDataAggregateRoot : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public string? Description { get; private set; }

    protected PlatformMasterDataAggregateRoot()
    {
    }

    protected PlatformMasterDataAggregateRoot(Guid id, Guid? tenantId)
        : base(id)
    {
        TenantId = tenantId;
    }

    public void SetDescription(string? description)
    {
        var value = description?.Trim();
        Description = string.IsNullOrEmpty(value)
            ? null
            : Check.Length(value, nameof(description), PlatformMasterDataConsts.MaxDescriptionLength);
    }

    /// <summary>A required name: trimmed, at most <see cref="PlatformMasterDataConsts.MaxNameLength"/> characters.</summary>
    public static string NormalizeName(string name)
        => Check.NotNullOrWhiteSpace(name, nameof(name), PlatformMasterDataConsts.MaxNameLength).Trim();

    /// <summary>The unique key of a name: trimmed and upper case, so "Invoice" and "invoice" are one name (PO 2026-10-16).</summary>
    public static string NormalizedNameOf(string name) => NormalizeName(name).ToUpperInvariant();

    /// <summary>An optional text: trimmed, null when empty, at most <paramref name="maxLength"/> characters.</summary>
    protected static string? Optional(string? value, string parameterName, int maxLength)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : Check.Length(text, parameterName, maxLength);
    }

    protected static TEnum Defined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
        => Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(parameterName, value, $"Unknown {typeof(TEnum).Name}.");
}
