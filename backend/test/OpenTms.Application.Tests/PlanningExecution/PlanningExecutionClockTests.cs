using System;
using Microsoft.Extensions.Options;
using OpenTms.PlanningExecution;
using OpenTms.Platform.NumberSequences;
using Shouldly;
using Volo.Abp.Timing;
using Xunit;

namespace OpenTms.PlanningExecution;

/// <summary>The planner's "today" is the tenant's local date (NumberSequenceOptions.TimeZone), not the server's UTC date.</summary>
public class PlanningExecutionClockTests
{
    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime Now => now;

        public DateTimeKind Kind => DateTimeKind.Utc;

        public bool SupportsMultipleTimezone => true;

        public DateTime Normalize(DateTime dateTime) => dateTime;

        public DateTime ConvertToUserTime(DateTime dateTime) => dateTime;

        public DateTimeOffset ConvertToUserTime(DateTimeOffset dateTime) => dateTime;

        public DateTime ConvertToUtc(DateTime dateTime) => dateTime;
    }

    [Fact]
    public void Late_in_the_evening_UTC_is_already_tomorrow_in_Amsterdam()
    {
        var clock = new PlanningExecutionClock(new FixedClock(new DateTime(2026, 10, 25, 23, 30, 0, DateTimeKind.Utc)), Options.Create(new NumberSequenceOptions { TimeZone = "Europe/Amsterdam" }));
        clock.Today().ShouldBe(new DateOnly(2026, 10, 26));

        var utc = new PlanningExecutionClock(new FixedClock(new DateTime(2026, 10, 25, 23, 30, 0, DateTimeKind.Utc)), Options.Create(new NumberSequenceOptions { TimeZone = "UTC" }));
        utc.Today().ShouldBe(new DateOnly(2026, 10, 25));
    }
}
