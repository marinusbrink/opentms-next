using System;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.NumberSequences;

/// <summary>
/// The last issued counter of one sequence in one period (design 50): platform."NumberSequenceCounters", unique per
/// (tenant, sequence key, period). Written only by <see cref="INumberSequenceGenerator"/> through one atomic upsert,
/// in the transaction of the fact that takes the number.
/// </summary>
public class NumberSequenceCounter : Entity<Guid>, IMultiTenant
{
    public const int MaxSequenceKeyLength = 128;

    public Guid? TenantId { get; private set; }

    public string SequenceKey { get; private set; } = default!;

    public string Period { get; private set; } = default!;

    public long LastValue { get; private set; }

    protected NumberSequenceCounter()
    {
    }

    public NumberSequenceCounter(Guid id, Guid? tenantId, string sequenceKey, string period, long lastValue)
        : base(id)
    {
        TenantId = tenantId;
        SequenceKey = sequenceKey;
        Period = period;
        LastValue = lastValue;
    }
}
