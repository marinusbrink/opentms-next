using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.MasterData;

/// <summary>
/// The base of every Master Data aggregate: audit, soft delete and the concurrency stamp from ABP,
/// a nullable tenant (assessment §3.6, §4: host rows are parked but must stay possible), and the
/// shared description field. Never defaults the tenant; the ABP data filter scopes every query.
/// </summary>
public abstract class MasterDataAggregateRoot : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public string? Description { get; private set; }

    protected MasterDataAggregateRoot()
    {
    }

    protected MasterDataAggregateRoot(Guid id, Guid? tenantId)
        : base(id)
    {
        TenantId = tenantId;
    }

    public void SetDescription(string? description)
    {
        var value = description?.Trim();
        Description = string.IsNullOrEmpty(value)
            ? null
            : Check.Length(value, nameof(description), MasterDataConsts.MaxDescriptionLength);
    }

    protected static string NormalizeName(string name)
        => Check.NotNullOrWhiteSpace(name, nameof(name), MasterDataConsts.MaxNameLength).Trim();
}
