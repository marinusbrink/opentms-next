using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.PlanningExecution.Availability;

/// <summary>
/// A period a vehicle or driver cannot be planned (design 68 §2, PO decisions D9, D10): leave, sickness, training,
/// maintenance, an inspection. Optionally repeated weekly: the From–Till time of day on every weekday of
/// <see cref="RecurrenceWeekdays"/> from From's date through <see cref="RecurrenceUntil"/> (expanded by
/// <see cref="AvailabilityExpander"/> in the tenant's time zone). The resource is a Master Data id without a foreign key,
/// validated on write by the application service; it does not change after creation.
/// <para>GDPR (constitution rule 5): a driver's unavailability is personal data (the reason may be "sick"). It belongs to
/// the driver's art. 15 export and is erased with the driver (art. 17) by deleting the driver's rows; the remarks are free
/// text for planners and must not hold medical details.</para>
/// </summary>
public class ResourceUnavailability : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public ResourceKind ResourceKind { get; private set; }

    public Guid ResourceId { get; private set; }

    public DateTimeOffset From { get; private set; }

    public DateTimeOffset Till { get; private set; }

    /// <summary>From in UTC: the sortable, indexable form (SQLite cannot compare a DateTimeOffset).</summary>
    public DateTime FromUtc { get; private set; }

    /// <summary>
    /// A filter bound, never shown: the latest instant any occurrence can end (Till in UTC for a single period; for a
    /// repetition the day after <see cref="RecurrenceUntil"/> plus the occurrence's length plus the widest UTC offset).
    /// </summary>
    public DateTime SpanTillUtc { get; private set; }

    public UnavailabilityReason Reason { get; private set; }

    public string? Remarks { get; private set; }

    /// <summary>The weekdays of a weekly repetition as flags (Mon=1 … Sun=64); null = a single period.</summary>
    public int? RecurrenceWeekdays { get; private set; }

    /// <summary>The last date (tenant time zone) a repetition occurs on; set exactly when <see cref="RecurrenceWeekdays"/> is.</summary>
    public DateOnly? RecurrenceUntil { get; private set; }

    protected ResourceUnavailability()
    {
    }

    public ResourceUnavailability(Guid id, Guid? tenantId, ResourceKind resourceKind, Guid resourceId, DateTimeOffset from, DateTimeOffset till,
        UnavailabilityReason reason, string? remarks, int? recurrenceWeekdays, DateOnly? recurrenceUntil)
        : base(id)
    {
        if (!Enum.IsDefined(resourceKind) || resourceId == Guid.Empty)
        {
            throw new ArgumentException("A resource kind and id are required.", nameof(resourceId));
        }

        TenantId = tenantId;
        ResourceKind = resourceKind;
        ResourceId = resourceId;
        Update(from, till, reason, remarks, recurrenceWeekdays, recurrenceUntil);
    }

    public bool IsRecurring => RecurrenceWeekdays != null;

    public void Update(DateTimeOffset from, DateTimeOffset till, UnavailabilityReason reason, string? remarks, int? recurrenceWeekdays, DateOnly? recurrenceUntil)
    {
        if (till <= from || till - from > TimeSpan.FromDays(PlanningExecutionConsts.MaxUnavailabilityDays))
        {
            throw new BusinessException(PlanningExecutionErrorCodes.InvalidUnavailabilityPeriod).WithData("max", PlanningExecutionConsts.MaxUnavailabilityDays);
        }

        if (!Enum.IsDefined(reason))
        {
            throw new BusinessException(Platform.ReferenceData.ReferenceDataErrorCodes.InvalidCode).WithData("field", "reason").WithData("value", (int)reason);
        }

        if (recurrenceWeekdays != null || recurrenceUntil != null)
        {
            var firstDate = DateOnly.FromDateTime(from.DateTime);
            if (recurrenceWeekdays is null or <= 0 or > AvailabilityWeekdays.All
                || recurrenceUntil == null
                || recurrenceUntil.Value < firstDate
                || recurrenceUntil.Value.DayNumber - firstDate.DayNumber > PlanningExecutionConsts.MaxUnavailabilityDays
                || till - from > TimeSpan.FromHours(PlanningExecutionConsts.MaxRecurringOccurrenceHours))
            {
                throw new BusinessException(PlanningExecutionErrorCodes.InvalidRecurrence).WithData("max", PlanningExecutionConsts.MaxUnavailabilityDays);
            }
        }

        From = from;
        Till = till;
        FromUtc = from.UtcDateTime;
        Reason = reason;
        Remarks = Text(remarks);
        RecurrenceWeekdays = recurrenceWeekdays;
        RecurrenceUntil = recurrenceUntil;
        SpanTillUtc = recurrenceUntil == null
            ? till.UtcDateTime
            : recurrenceUntil.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(1) + (till - from) + TimeSpan.FromHours(14);
    }

    private static string? Text(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= PlanningExecutionConsts.MaxUnavailabilityRemarksLength ? trimmed : trimmed[..PlanningExecutionConsts.MaxUnavailabilityRemarksLength];
    }
}
