using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp.DependencyInjection;

namespace OpenTms.PlanningExecution.Availability;

/// <summary>
/// A concrete period a resource is not available: an occurrence of an unavailability, or "outside working time" (then
/// <see cref="UnavailabilityId"/> and <see cref="Reason"/> are null). Instants carry the tenant zone's offset.
/// </summary>
public sealed record AvailabilityBlock(ResourceKind ResourceKind, Guid ResourceId, DateTimeOffset From, DateTimeOffset Till, Guid? UnavailabilityId, UnavailabilityReason? Reason)
{
    public bool Overlaps(DateTimeOffset from, DateTimeOffset till) => From < till && from < Till;
}

/// <summary>
/// Design 68 §2: turns availability records into concrete blocks for a window, in the tenant's time zone. A weekly
/// repetition keeps its wall-clock times across daylight-saving changes (09:00–17:00 stays 09:00–17:00 local, so its UTC
/// instants move by an hour); a local time that does not exist (the spring-forward gap) moves forward by the gap, an
/// ambiguous one (the autumn fold) takes the first, daylight-time instant. The inverse of a working pattern gives the
/// "outside working time" blocks, clipped to the window and merged across midnight. Pure: no I/O.
/// </summary>
public class AvailabilityExpander : ITransientDependency
{
    /// <summary>The occurrences of <paramref name="unavailability"/> that overlap [<paramref name="from"/>, <paramref name="till"/>), unclipped.</summary>
    public virtual IEnumerable<AvailabilityBlock> Expand(ResourceUnavailability unavailability, DateTimeOffset from, DateTimeOffset till, TimeZoneInfo zone)
    {
        if (!unavailability.IsRecurring)
        {
            var single = Block(unavailability, unavailability.From, unavailability.Till, zone);
            if (single.Overlaps(from, till))
            {
                yield return single;
            }

            yield break;
        }

        var localFrom = TimeZoneInfo.ConvertTime(unavailability.From, zone).DateTime;
        var localTill = TimeZoneInfo.ConvertTime(unavailability.Till, zone).DateTime;
        var dayOffset = (localTill.Date - localFrom.Date).Days;
        var startTime = localFrom.TimeOfDay;
        var endTime = localTill.TimeOfDay;

        var firstDate = DateOnly.FromDateTime(localFrom.Date);
        var windowFirst = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from, zone).Date).AddDays(-dayOffset - 1);
        var windowLast = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(till, zone).Date);
        var date = firstDate > windowFirst ? firstDate : windowFirst;
        var last = unavailability.RecurrenceUntil!.Value < windowLast ? unavailability.RecurrenceUntil.Value : windowLast;
        for (; date <= last; date = date.AddDays(1))
        {
            if (!AvailabilityWeekdays.Contains(unavailability.RecurrenceWeekdays!.Value, date.DayOfWeek))
            {
                continue;
            }

            var day = date.ToDateTime(TimeOnly.MinValue);
            var start = ToInstant(day + startTime, zone);
            var end = ToInstant(day.AddDays(dayOffset) + endTime, zone);
            if (end <= start)
            {
                continue;
            }

            var block = Block(unavailability, start, end, zone);
            if (block.Overlaps(from, till))
            {
                yield return block;
            }
        }
    }

    /// <summary>
    /// The periods of [<paramref name="from"/>, <paramref name="till"/>) outside the resource's working pattern: before the
    /// start and after the end of every weekday that has a row; a weekday without a row adds nothing. Clipped and merged.
    /// </summary>
    public virtual List<AvailabilityBlock> OutsideWorkingTime(ResourceKind kind, Guid resourceId, IEnumerable<ResourceWorkingPattern> patterns, DateTimeOffset from, DateTimeOffset till, TimeZoneInfo zone)
    {
        var byWeekday = patterns.Where(p => p.ResourceKind == kind && p.ResourceId == resourceId).GroupBy(p => p.Weekday).ToDictionary(g => g.Key, g => g.First());
        var blocks = new List<AvailabilityBlock>();
        if (byWeekday.Count == 0 || till <= from)
        {
            return blocks;
        }

        var first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from, zone).Date).AddDays(-1);
        var last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(till, zone).Date);
        var raw = new List<(DateTimeOffset From, DateTimeOffset Till)>();
        for (var date = first; date <= last; date = date.AddDays(1))
        {
            if (!byWeekday.TryGetValue(AvailabilityWeekdays.IndexOf(date.DayOfWeek), out var pattern))
            {
                continue;
            }

            var day = date.ToDateTime(TimeOnly.MinValue);
            var midnight = ToInstant(day, zone);
            var nextMidnight = ToInstant(day.AddDays(1), zone);
            var start = ToInstant(day + pattern.Start.ToTimeSpan(), zone);
            var end = ToInstant(day + pattern.End.ToTimeSpan(), zone);
            if (start > midnight)
            {
                raw.Add((midnight, start));
            }

            if (nextMidnight > end)
            {
                raw.Add((end, nextMidnight));
            }
        }

        foreach (var (blockFrom, blockTill) in raw.OrderBy(b => b.From))
        {
            var clippedFrom = blockFrom > from ? blockFrom : from;
            var clippedTill = blockTill < till ? blockTill : till;
            if (clippedTill <= clippedFrom)
            {
                continue;
            }

            if (blocks.Count > 0 && blocks[^1].Till >= clippedFrom)
            {
                var previous = blocks[^1];
                blocks[^1] = previous with { Till = Local(clippedTill > previous.Till ? clippedTill : previous.Till, zone) };
                continue;
            }

            blocks.Add(new AvailabilityBlock(kind, resourceId, Local(clippedFrom, zone), Local(clippedTill, zone), null, null));
        }

        return blocks;
    }

    /// <summary>
    /// The instant of a wall-clock time in <paramref name="zone"/>: a time in the spring-forward gap moves forward by the gap
    /// (02:30 becomes 03:30), an ambiguous time in the autumn fold is the first (daylight-time) instant.
    /// </summary>
    public static DateTimeOffset ToInstant(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            /* Read with the offset before the gap: the instant lies the gap's length later on the clock after it. */
            return Local(new DateTimeOffset(local, zone.GetUtcOffset(local.AddDays(-1))), zone);
        }

        if (zone.IsAmbiguousTime(local))
        {
            return new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max());
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static DateTimeOffset Local(DateTimeOffset instant, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(instant, zone);

    private static AvailabilityBlock Block(ResourceUnavailability u, DateTimeOffset from, DateTimeOffset till, TimeZoneInfo zone)
        => new(u.ResourceKind, u.ResourceId, Local(from, zone), Local(till, zone), u.Id, u.Reason);
}
