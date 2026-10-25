using System;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.DerivedValues;

/// <summary>
/// The calculation state of one record (design 49): platform."DerivedValueStates", one row per (tenant, target type,
/// target id). <see cref="RequestedVersion"/> grows on every request, <see cref="CalculatedVersion"/> is the version whose
/// result is stored; the record is "calculating" while the first is ahead. Written only through
/// <see cref="IDerivedValueStateStore"/>'s atomic statements, never through a repository update.
/// </summary>
public class DerivedValueState : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public string TargetType { get; private set; } = default!;

    public Guid TargetId { get; private set; }

    public long RequestedVersion { get; private set; }

    public long CalculatedVersion { get; private set; }

    public DerivedValueStatus Status { get; private set; }

    public int FailedAttempts { get; private set; }

    /// <summary>A business-exception code or an exception type name; never a message (it may carry data).</summary>
    public string? LastErrorCode { get; private set; }

    public DateTime RequestedAt { get; private set; }

    public DateTime? CalculatedAt { get; private set; }

    protected DerivedValueState()
    {
    }

    public DerivedValueState(Guid id, Guid? tenantId, string targetType, Guid targetId, long requestedVersion, DateTime requestedAt)
        : base(id)
    {
        TenantId = tenantId;
        TargetType = targetType;
        TargetId = targetId;
        RequestedVersion = requestedVersion;
        RequestedAt = requestedAt;
        Status = DerivedValueStatus.Pending;
    }
}
