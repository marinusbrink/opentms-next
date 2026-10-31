using System;
using System.Linq;
using OpenTms.PlanningExecution.Availability;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace OpenTms.PlanningExecution;

/// <summary>
/// Design 68 §2 (resource availability), the pure parts: a period's validation, the weekly expansion in the tenant's zone
/// across both daylight-saving changes (wall-clock times kept, the spring gap moved forward, the autumn fold's first
/// instant), and the inverse of a working pattern (clipped, merged across midnight, a weekday without a row adds nothing).
/// Risk class: High (planning correctness).
/// </summary>
public class AvailabilityExpanderTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
    private static readonly TimeSpan Cest = TimeSpan.FromHours(2);
    private static readonly TimeSpan Cet = TimeSpan.FromHours(1);
    private static readonly Guid Driver = Guid.NewGuid();

    private readonly AvailabilityExpander _expander = new();

    private static ResourceUnavailability Unavailability(DateTimeOffset from, DateTimeOffset till, int? weekdays = null, DateOnly? until = null, UnavailabilityReason reason = UnavailabilityReason.Leave, string? remarks = null)
        => new(Guid.NewGuid(), null, ResourceKind.Driver, Driver, from, till, reason, remarks, weekdays, until);

    private static ResourceWorkingPattern Pattern(int weekday, int startHour, int endHour)
        => new(Guid.NewGuid(), null, ResourceKind.Driver, Driver, weekday, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0));

    /* Validation */

    [Fact]
    public void A_period_must_end_after_it_starts_and_last_at_most_366_days()
    {
        var from = new DateTimeOffset(2026, 10, 26, 8, 0, 0, Cet);
        Should.Throw<BusinessException>(() => Unavailability(from, from)).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidUnavailabilityPeriod);
        Should.Throw<BusinessException>(() => Unavailability(from, from.AddHours(-1))).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidUnavailabilityPeriod);
        Should.Throw<BusinessException>(() => Unavailability(from, from.AddDays(PlanningExecutionConsts.MaxUnavailabilityDays).AddMinutes(1))).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidUnavailabilityPeriod);
        Should.Throw<BusinessException>(() => Unavailability(from, from.AddHours(1), reason: (UnavailabilityReason)42)).Code.ShouldBe(Platform.ReferenceData.ReferenceDataErrorCodes.InvalidCode);
        Should.Throw<ArgumentException>(() => new ResourceUnavailability(Guid.NewGuid(), null, ResourceKind.Driver, Guid.Empty, from, from.AddHours(1), UnavailabilityReason.Leave, null, null, null));

        var ok = Unavailability(from, from.AddDays(PlanningExecutionConsts.MaxUnavailabilityDays), remarks: "  " + new string('x', 600) + "  ");
        ok.Remarks!.Length.ShouldBe(PlanningExecutionConsts.MaxUnavailabilityRemarksLength, "trimmed and cut to the maximum");
        ok.FromUtc.ShouldBe(from.UtcDateTime);
        ok.SpanTillUtc.ShouldBe(ok.Till.UtcDateTime, "a single period's filter bound is its end");
        Unavailability(from, from.AddHours(1), remarks: "   ").Remarks.ShouldBeNull();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(128, true)]
    [InlineData(-1, true)]
    [InlineData(null, true)]
    [InlineData(AvailabilityWeekdays.Monday, false)]
    [InlineData(AvailabilityWeekdays.All, false)]
    public void A_repetition_needs_valid_weekdays_and_an_until_date(int? weekdays, bool refused)
    {
        var from = new DateTimeOffset(2026, 10, 26, 9, 0, 0, Cet);
        Func<ResourceUnavailability> create = () => Unavailability(from, from.AddHours(8), weekdays, new DateOnly(2026, 11, 30));
        if (refused)
        {
            Should.Throw<BusinessException>(create).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidRecurrence);
        }
        else
        {
            create().IsRecurring.ShouldBeTrue();
        }
    }

    [Fact]
    public void A_repetition_refuses_a_missing_or_early_or_far_until_and_an_occurrence_over_24_hours()
    {
        var from = new DateTimeOffset(2026, 10, 26, 9, 0, 0, Cet);
        Should.Throw<BusinessException>(() => Unavailability(from, from.AddHours(8), AvailabilityWeekdays.Monday, null)).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidRecurrence);
        Should.Throw<BusinessException>(() => Unavailability(from, from.AddHours(8), null, new DateOnly(2026, 11, 30))).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidRecurrence);
        Should.Throw<BusinessException>(() => Unavailability(from, from.AddHours(8), AvailabilityWeekdays.Monday, new DateOnly(2026, 10, 25))).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidRecurrence);
        Should.Throw<BusinessException>(() => Unavailability(from, from.AddHours(8), AvailabilityWeekdays.Monday, new DateOnly(2027, 10, 28))).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidRecurrence);
        Should.Throw<BusinessException>(() => Unavailability(from, from.AddHours(25), AvailabilityWeekdays.Monday, new DateOnly(2026, 11, 30))).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidRecurrence);

        var weekly = Unavailability(from, from.AddHours(8), AvailabilityWeekdays.Monday, new DateOnly(2026, 11, 30));
        weekly.SpanTillUtc.ShouldBeGreaterThanOrEqualTo(new DateTimeOffset(2026, 11, 30, 17, 0, 0, Cet).UtcDateTime, "the filter bound reaches past the last occurrence");
    }

    [Fact]
    public void Weekday_flags_follow_Monday_one_to_Sunday_sixty_four()
    {
        AvailabilityWeekdays.FlagOf(DayOfWeek.Monday).ShouldBe(AvailabilityWeekdays.Monday);
        AvailabilityWeekdays.FlagOf(DayOfWeek.Sunday).ShouldBe(AvailabilityWeekdays.Sunday);
        AvailabilityWeekdays.IndexOf(DayOfWeek.Monday).ShouldBe(0);
        AvailabilityWeekdays.IndexOf(DayOfWeek.Sunday).ShouldBe(6);
        AvailabilityWeekdays.Contains(AvailabilityWeekdays.Monday | AvailabilityWeekdays.Friday, DayOfWeek.Friday).ShouldBeTrue();
        AvailabilityWeekdays.Contains(AvailabilityWeekdays.Monday | AvailabilityWeekdays.Friday, DayOfWeek.Tuesday).ShouldBeFalse();
    }

    /* Expansion */

    [Fact]
    public void A_single_period_is_returned_when_it_overlaps_the_window_and_not_when_it_only_touches_it()
    {
        var u = Unavailability(new DateTimeOffset(2026, 10, 26, 8, 0, 0, Cet), new DateTimeOffset(2026, 10, 26, 12, 0, 0, Cet));
        _expander.Expand(u, new DateTimeOffset(2026, 10, 26, 11, 0, 0, Cet), new DateTimeOffset(2026, 10, 27, 0, 0, 0, Cet), Amsterdam).ShouldHaveSingleItem().UnavailabilityId.ShouldBe(u.Id);
        _expander.Expand(u, new DateTimeOffset(2026, 10, 26, 12, 0, 0, Cet), new DateTimeOffset(2026, 10, 27, 0, 0, 0, Cet), Amsterdam).ShouldBeEmpty("ends where the window starts");
        _expander.Expand(u, new DateTimeOffset(2026, 10, 26, 0, 0, 0, Cet), new DateTimeOffset(2026, 10, 26, 8, 0, 0, Cet), Amsterdam).ShouldBeEmpty("starts where the window ends");
    }

    [Fact]
    public void A_weekly_repetition_keeps_its_wall_clock_times_across_the_autumn_change()
    {
        /* Every day 09:00–17:00 from Thursday 22 October (CEST) through Tuesday 27 October 2026 (CET after the 25th). */
        var u = Unavailability(new DateTimeOffset(2026, 10, 22, 9, 0, 0, Cest), new DateTimeOffset(2026, 10, 22, 17, 0, 0, Cest), AvailabilityWeekdays.All, new DateOnly(2026, 10, 27));

        var blocks = _expander.Expand(u, new DateTimeOffset(2026, 10, 20, 0, 0, 0, Cest), new DateTimeOffset(2026, 11, 2, 0, 0, 0, Cet), Amsterdam).ToList();

        blocks.Select(b => b.From.Day).ShouldBe([22, 23, 24, 25, 26, 27], "from the first date through the until date");
        blocks.ShouldAllBe(b => b.From.Hour == 9 && b.Till.Hour == 17, "the local times stay 09:00–17:00");
        blocks.Single(b => b.From.Day == 24).From.Offset.ShouldBe(Cest);
        blocks.Single(b => b.From.Day == 24).From.UtcDateTime.Hour.ShouldBe(7);
        blocks.Single(b => b.From.Day == 26).From.Offset.ShouldBe(Cet);
        blocks.Single(b => b.From.Day == 26).From.UtcDateTime.Hour.ShouldBe(8, "the UTC instant moves by the hour");
        blocks.ShouldAllBe(b => b.UnavailabilityId == u.Id && b.Reason == UnavailabilityReason.Leave);
    }

    [Fact]
    public void Only_the_weekdays_of_the_repetition_occur()
    {
        /* Mondays and Wednesdays from Monday 2 November through Sunday 15 November 2026. */
        var u = Unavailability(new DateTimeOffset(2026, 11, 2, 13, 0, 0, Cet), new DateTimeOffset(2026, 11, 2, 15, 0, 0, Cet),
            AvailabilityWeekdays.Monday | AvailabilityWeekdays.Wednesday, new DateOnly(2026, 11, 15));

        var blocks = _expander.Expand(u, new DateTimeOffset(2026, 11, 1, 0, 0, 0, Cet), new DateTimeOffset(2026, 11, 30, 0, 0, 0, Cet), Amsterdam).ToList();
        blocks.Select(b => b.From.Day).ShouldBe([2, 4, 9, 11]);
    }

    [Fact]
    public void An_occurrence_in_the_spring_gap_moves_forward_by_the_gap()
    {
        /* Daily 02:30–04:00 local; on 29 March 2026 the clock jumps from 02:00 to 03:00. */
        var u = Unavailability(new DateTimeOffset(2026, 3, 27, 2, 30, 0, Cet), new DateTimeOffset(2026, 3, 27, 4, 0, 0, Cet), AvailabilityWeekdays.All, new DateOnly(2026, 3, 30));

        var blocks = _expander.Expand(u, new DateTimeOffset(2026, 3, 27, 0, 0, 0, Cet), new DateTimeOffset(2026, 3, 31, 0, 0, 0, Cest), Amsterdam).ToList();

        var gapDay = blocks.Single(b => b.From.Day == 29);
        gapDay.From.ShouldBe(new DateTimeOffset(2026, 3, 29, 3, 30, 0, Cest), "02:30 does not exist; it becomes 03:30");
        gapDay.Till.ShouldBe(new DateTimeOffset(2026, 3, 29, 4, 0, 0, Cest));
        blocks.Single(b => b.From.Day == 30).From.ShouldBe(new DateTimeOffset(2026, 3, 30, 2, 30, 0, Cest));
    }

    [Fact]
    public void An_occurrence_in_the_autumn_fold_takes_the_first_daylight_time_instant()
    {
        AvailabilityExpander.ToInstant(new DateTime(2026, 10, 25, 2, 30, 0), Amsterdam).ShouldBe(new DateTimeOffset(2026, 10, 25, 2, 30, 0, Cest));
        AvailabilityExpander.ToInstant(new DateTime(2026, 3, 29, 2, 30, 0), Amsterdam).ShouldBe(new DateTimeOffset(2026, 3, 29, 3, 30, 0, Cest));
        AvailabilityExpander.ToInstant(new DateTime(2026, 7, 1, 12, 0, 0), Amsterdam).ShouldBe(new DateTimeOffset(2026, 7, 1, 12, 0, 0, Cest));

        var u = Unavailability(new DateTimeOffset(2026, 10, 24, 2, 30, 0, Cest), new DateTimeOffset(2026, 10, 24, 5, 0, 0, Cest), AvailabilityWeekdays.All, new DateOnly(2026, 10, 25));
        var fold = _expander.Expand(u, new DateTimeOffset(2026, 10, 25, 0, 0, 0, Cest), new DateTimeOffset(2026, 10, 26, 0, 0, 0, Cet), Amsterdam).Single(b => b.From.Day == 25);
        fold.From.UtcDateTime.ShouldBe(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc));
        (fold.Till - fold.From).ShouldBe(TimeSpan.FromHours(3.5), "the fold's repeated hour lies inside the occurrence");
    }

    [Fact]
    public void An_overnight_occurrence_of_the_day_before_the_window_reaches_into_it()
    {
        /* Every Monday 22:00 until Tuesday 06:00 local, from 26 October 2026 through 30 November. */
        var u = Unavailability(new DateTimeOffset(2026, 10, 26, 22, 0, 0, Cet), new DateTimeOffset(2026, 10, 27, 6, 0, 0, Cet), AvailabilityWeekdays.Monday, new DateOnly(2026, 11, 30));

        var blocks = _expander.Expand(u, new DateTimeOffset(2026, 11, 3, 0, 0, 0, Cet), new DateTimeOffset(2026, 11, 3, 12, 0, 0, Cet), Amsterdam).ToList();

        var block = blocks.ShouldHaveSingleItem("Monday 2 November's occurrence runs into Tuesday morning");
        block.From.ShouldBe(new DateTimeOffset(2026, 11, 2, 22, 0, 0, Cet));
        block.Till.ShouldBe(new DateTimeOffset(2026, 11, 3, 6, 0, 0, Cet));
    }

    /* Working pattern inverse */

    [Fact]
    public void The_inverse_of_a_working_pattern_is_clipped_to_the_window_and_merged_across_midnight()
    {
        /* Monday 08–17 and Tuesday 08–17; Wednesday has no row (adds nothing). Week of Monday 26 October 2026 (CET). */
        var patterns = new[] { Pattern(0, 8, 17), Pattern(1, 8, 17) };
        var blocks = _expander.OutsideWorkingTime(ResourceKind.Driver, Driver, patterns,
            new DateTimeOffset(2026, 10, 26, 6, 0, 0, Cet), new DateTimeOffset(2026, 10, 28, 12, 0, 0, Cet), Amsterdam);

        blocks.Select(b => (b.From, b.Till)).ShouldBe([
            (new DateTimeOffset(2026, 10, 26, 6, 0, 0, Cet), new DateTimeOffset(2026, 10, 26, 8, 0, 0, Cet)),
            (new DateTimeOffset(2026, 10, 26, 17, 0, 0, Cet), new DateTimeOffset(2026, 10, 27, 8, 0, 0, Cet)),
            (new DateTimeOffset(2026, 10, 27, 17, 0, 0, Cet), new DateTimeOffset(2026, 10, 28, 0, 0, 0, Cet))
        ]);
        blocks.ShouldAllBe(b => b.UnavailabilityId == null && b.Reason == null && b.ResourceId == Driver);
    }

    [Fact]
    public void A_resource_without_a_pattern_or_another_resources_pattern_has_no_outside_working_time()
    {
        var window = (From: new DateTimeOffset(2026, 10, 26, 0, 0, 0, Cet), Till: new DateTimeOffset(2026, 10, 27, 0, 0, 0, Cet));
        _expander.OutsideWorkingTime(ResourceKind.Driver, Driver, [], window.From, window.Till, Amsterdam).ShouldBeEmpty();
        _expander.OutsideWorkingTime(ResourceKind.Driver, Guid.NewGuid(), [Pattern(0, 8, 17)], window.From, window.Till, Amsterdam).ShouldBeEmpty();
        _expander.OutsideWorkingTime(ResourceKind.Vehicle, Driver, [Pattern(0, 8, 17)], window.From, window.Till, Amsterdam).ShouldBeEmpty("the kind is part of the key");

        var inside = _expander.OutsideWorkingTime(ResourceKind.Driver, Driver, [Pattern(0, 8, 17)],
            new DateTimeOffset(2026, 10, 26, 9, 0, 0, Cet), new DateTimeOffset(2026, 10, 26, 16, 0, 0, Cet), Amsterdam);
        inside.ShouldBeEmpty("a window inside working time has no block");
    }

    [Fact]
    public void A_working_pattern_rejects_an_end_before_its_start_and_a_weekday_out_of_range()
    {
        Should.Throw<BusinessException>(() => Pattern(7, 8, 17)).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidWorkingPattern);
        Should.Throw<BusinessException>(() => Pattern(0, 17, 8)).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidWorkingPattern);
    }
}
