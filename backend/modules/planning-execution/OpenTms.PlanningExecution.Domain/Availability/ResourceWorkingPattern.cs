using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.PlanningExecution.Availability;

/// <summary>
/// The working time of a vehicle or driver on one weekday (design 68 §2, PO decision D10): one row per resource and weekday
/// (0 = Monday … 6 = Sunday), the local start and end in the tenant's time zone. No row for a weekday means no working-time
/// constraint that day. The inverse (before the start, after the end) is the "outside working time" of the Scheduler and
/// of the <c>OutsideWorkingTime</c> warning. GDPR: a driver's pattern is personal data, exported and erased with the driver.
/// </summary>
public class ResourceWorkingPattern : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public ResourceKind ResourceKind { get; private set; }

    public Guid ResourceId { get; private set; }

    /// <summary>0 = Monday … 6 = Sunday.</summary>
    public int Weekday { get; private set; }

    public TimeOnly Start { get; private set; }

    public TimeOnly End { get; private set; }

    protected ResourceWorkingPattern()
    {
    }

    public ResourceWorkingPattern(Guid id, Guid? tenantId, ResourceKind resourceKind, Guid resourceId, int weekday, TimeOnly start, TimeOnly end)
        : base(id)
    {
        if (!Enum.IsDefined(resourceKind) || resourceId == Guid.Empty || weekday is < 0 or > 6)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.InvalidWorkingPattern);
        }

        TenantId = tenantId;
        ResourceKind = resourceKind;
        ResourceId = resourceId;
        Weekday = weekday;
        SetTimes(start, end);
    }

    public void SetTimes(TimeOnly start, TimeOnly end)
    {
        if (end <= start)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.InvalidWorkingPattern);
        }

        Start = start;
        End = end;
    }
}
