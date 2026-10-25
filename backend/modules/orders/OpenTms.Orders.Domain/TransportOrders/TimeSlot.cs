using System;
using System.Linq;
using OpenTms.Platform.Localization;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// A named delivery window (OpenTMS TimeSlot), design 51. The times are local times at the stop (the address's IANA time
/// zone); the slot itself has no time zone (Assumption 3). An end before the start means the window ends the next day
/// (assessment §5: OpenTMS's overnight rule was wrong); an end equal to the start is refused.
/// </summary>
public class TimeSlot : ReferenceDataAggregateRoot
{
    public string Code { get; private set; } = default!;

    public LocalizedName Name { get; private set; } = default!;

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    public bool IsArchived { get; private set; }

    /// <summary>The window ends the next day (22:00–06:00).</summary>
    public bool IsOvernight => EndTime < StartTime;

    protected TimeSlot()
    {
    }

    public TimeSlot(Guid id, Guid? tenantId, string code, LocalizedName name, TimeOnly startTime, TimeOnly endTime)
        : base(id, tenantId)
    {
        SetCode(code);
        SetName(name);
        SetTimes(startTime, endTime);
    }

    public void SetCode(string code) => Code = NormalizeCode(code);

    public void SetName(LocalizedName name) => Name = RequireName(name);

    public void SetArchived(bool isArchived) => IsArchived = isArchived;

    public void SetTimes(TimeOnly startTime, TimeOnly endTime)
    {
        if (startTime == endTime)
        {
            throw new BusinessException(OrdersErrorCodes.EmptyTimeSlot);
        }

        StartTime = startTime;
        EndTime = endTime;
    }

    /// <summary>
    /// The window on <paramref name="date"/> at a stop in <paramref name="ianaTimeZoneId"/>: from the start on that date
    /// till the end on that date, or on the next date for an overnight slot. A local time that does not exist (the spring
    /// DST gap) moves forward to the first valid instant; an ambiguous one (the autumn overlap) takes the earlier offset.
    /// </summary>
    public (DateTimeOffset From, DateTimeOffset Till) ToWindow(DateOnly date, string ianaTimeZoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Check.NotNullOrWhiteSpace(ianaTimeZoneId, nameof(ianaTimeZoneId)));
        var from = Resolve(date.ToDateTime(StartTime), zone);
        var till = Resolve((IsOvernight ? date.AddDays(1) : date).ToDateTime(EndTime), zone);
        return (from, till);
    }

    /// <summary>The instant of a local (wall-clock) time in <paramref name="zone"/>, with the gap and overlap rules above.</summary>
    public static DateTimeOffset Resolve(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        if (zone.IsAmbiguousTime(local))
        {
            /* The larger offset is the earlier of the two instants (summer time, before the clocks go back). */
            var offset = zone.GetAmbiguousTimeOffsets(local).Max();
            return new DateTimeOffset(local, offset);
        }

        if (zone.IsInvalidTime(local))
        {
            return FirstInstantAfterGap(local, zone);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>
    /// The transition that skipped <paramref name="local"/>: the offset before the gap is that of a valid time a day
    /// earlier, the one after it that of a day later; the transition lies between the two candidate UTC instants and is
    /// found by bisection (to the second; transitions are on whole minutes).
    /// </summary>
    private static DateTimeOffset FirstInstantAfterGap(DateTime local, TimeZoneInfo zone)
    {
        var before = zone.GetUtcOffset(local.AddDays(-1));
        var after = zone.GetUtcOffset(local.AddDays(1));

        /* low is still in the old offset; high is already in the new one. */
        var low = DateTime.SpecifyKind(local - after, DateTimeKind.Utc);
        var high = DateTime.SpecifyKind(local - before, DateTimeKind.Utc);
        while (high - low > TimeSpan.FromSeconds(1))
        {
            var middle = low + TimeSpan.FromTicks((high - low).Ticks / 2);
            if (zone.GetUtcOffset(middle) == before)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        var transition = new DateTime(high.Ticks - high.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        if (zone.GetUtcOffset(transition) == before)
        {
            transition = transition.AddSeconds(1);
        }

        return TimeZoneInfo.ConvertTime(new DateTimeOffset(transition), zone);
    }
}
