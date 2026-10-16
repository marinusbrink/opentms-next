using System;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.NumberSequences;

/// <summary>
/// The last number drawn from a sequence in one period; key (sequence, year, month), with 0 for "no year" or "no month"
/// instead of NULL so the upsert's conflict target works on every provider (design 49). Written only by
/// <see cref="INumberSequenceCounterRepository.IncrementAsync"/>; its existence makes the sequence "in use".
/// </summary>
public class NumberSequenceCounter : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid NumberSequenceId { get; private set; }

    /// <summary>The year of the period, or 0 for a sequence that never restarts.</summary>
    public int PeriodYear { get; private set; }

    /// <summary>The month of the period, or 0 for a sequence that restarts per year or never.</summary>
    public int PeriodMonth { get; private set; }

    public long LastValue { get; private set; }

    protected NumberSequenceCounter()
    {
    }

    public NumberSequenceCounter(Guid? tenantId, Guid numberSequenceId, int periodYear, int periodMonth, long lastValue)
    {
        TenantId = tenantId;
        NumberSequenceId = numberSequenceId;
        PeriodYear = periodYear;
        PeriodMonth = periodMonth;
        LastValue = lastValue;
    }

    public override object[] GetKeys() => [NumberSequenceId, PeriodYear, PeriodMonth];
}
