using System;

namespace OpenTms.PlanningExecution.Availability;

/// <summary>The resource an availability record is about (design 68 §2): a Master Data vehicle or driver.</summary>
public enum ResourceKind
{
    Vehicle = 1,
    Driver = 2
}

/// <summary>Why a resource is not available (design 68 §2, PO decision D9).</summary>
public enum UnavailabilityReason
{
    Leave = 1,
    Sick = 2,
    Training = 3,
    Maintenance = 4,
    Inspection = 5,
    Other = 99
}

/// <summary>
/// The weekdays of a weekly repetition as flags (design 68 §2: Mon=1 … Sun=64) and the weekday numbering of a working
/// pattern (0 = Monday … 6 = Sunday, so a pattern's weekday <c>w</c> is the flag <c>1 &lt;&lt; w</c>).
/// </summary>
public static class AvailabilityWeekdays
{
    public const int Monday = 1;
    public const int Tuesday = 2;
    public const int Wednesday = 4;
    public const int Thursday = 8;
    public const int Friday = 16;
    public const int Saturday = 32;
    public const int Sunday = 64;

    /// <summary>Every weekday.</summary>
    public const int All = 127;

    /// <summary>The pattern weekday (0 = Monday … 6 = Sunday) of a date.</summary>
    public static int IndexOf(DayOfWeek day) => ((int)day + 6) % 7;

    public static int FlagOf(DayOfWeek day) => 1 << IndexOf(day);

    public static bool Contains(int flags, DayOfWeek day) => (flags & FlagOf(day)) != 0;
}
