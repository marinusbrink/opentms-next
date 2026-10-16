using System;
using OpenTms.Platform.NumberSequences;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.PlatformMasterData;

/// <summary>
/// NumberSequenceFormatter as a table (docs/designs/49-platform-master-data.md, "NumberSequence": Number = Prefix +
/// [YearPrefix + yyyy|yy + YearSuffix] + [MonthPrefix + MM + MonthSuffix] + counter padded to MinimumDigits; the period of
/// a date per scheme). Risk class: Critical (part of number generation; the frontend mirrors it for the example).
/// </summary>
public class NumberSequenceFormatterTests
{
    private static NumberSequenceFormat Format(NumberSequenceScheme scheme, string? prefix = null, string? yearPrefix = null, string? yearSuffix = null,
        string? monthPrefix = null, string? monthSuffix = null, int digits = 4)
        => new(scheme, prefix, yearPrefix, yearSuffix, monthPrefix, monthSuffix, digits);

    [Theory]
    /* The design's example: "O-" + "26" + "-" + "0001". */
    [InlineData(NumberSequenceScheme.TwoDigitYear, "O-", null, "-", null, null, 4, "2026-03-05", 1L, "O-26-0001")]
    [InlineData(NumberSequenceScheme.FourDigitYear, "O-", null, "-", null, null, 4, "2026-03-05", 1L, "O-2026-0001")]
    [InlineData(NumberSequenceScheme.FourDigitYear, null, null, null, null, null, 4, "2026-12-31", 123L, "20260123")]
    /* Every affix in its place. */
    [InlineData(NumberSequenceScheme.FourDigitYearMonth, "F", "Y", "/", "M", "-", 3, "2026-03-05", 7L, "FY2026/M03-007")]
    [InlineData(NumberSequenceScheme.TwoDigitYearMonth, "INV", null, null, null, null, 5, "2026-11-30", 42L, "INV261100042")]
    [InlineData(NumberSequenceScheme.TwoDigitYearMonth, "T", "[", "]", "(", ")", 2, "2027-01-01", 9L, "T[27](01)09")]
    /* The year and month affixes do not apply where the scheme has no year or month. */
    [InlineData(NumberSequenceScheme.CounterOnly, "P", "X", "X", "Y", "Y", 6, "2026-03-05", 12L, "P000012")]
    [InlineData(NumberSequenceScheme.FourDigitYear, "P", "Y", "-", "M", "-", 4, "2026-03-05", 1L, "PY2026-0001")]
    /* Separators are kept as typed, a space included. */
    [InlineData(NumberSequenceScheme.TwoDigitYear, "O ", null, " ", null, null, 4, "2026-03-05", 1L, "O 26 0001")]
    /* Two-digit years keep their leading zero. */
    [InlineData(NumberSequenceScheme.TwoDigitYear, null, null, null, null, null, 4, "2005-01-01", 1L, "050001")]
    [InlineData(NumberSequenceScheme.TwoDigitYear, null, null, null, null, null, 4, "2100-01-01", 1L, "000001")]
    [InlineData(NumberSequenceScheme.TwoDigitYearMonth, null, null, null, null, null, 1, "2009-09-09", 1L, "09091")]
    /* Minimum digits: 1 and 12, and a counter longer than the minimum is never cut. */
    [InlineData(NumberSequenceScheme.CounterOnly, null, null, null, null, null, 1, "2026-03-05", 1L, "1")]
    [InlineData(NumberSequenceScheme.CounterOnly, null, null, null, null, null, 12, "2026-03-05", 1L, "000000000001")]
    [InlineData(NumberSequenceScheme.CounterOnly, "A", null, null, null, null, 2, "2026-03-05", 123456L, "A123456")]
    [InlineData(NumberSequenceScheme.FourDigitYear, "R", null, "-", null, null, 4, "2026-03-05", 10000L, "R2026-10000")]
    [InlineData(NumberSequenceScheme.CounterOnly, null, null, null, null, null, 4, "2026-03-05", 9_999_999_999_999L, "9999999999999")]
    [InlineData(NumberSequenceScheme.CounterOnly, null, null, null, null, null, 4, "2026-03-05", 0L, "0000")]
    public void A_number_is_prefix_year_month_and_the_padded_counter(
        NumberSequenceScheme scheme, string? prefix, string? yearPrefix, string? yearSuffix, string? monthPrefix, string? monthSuffix,
        int digits, string date, long counter, string expected)
    {
        NumberSequenceFormatter.Format(Format(scheme, prefix, yearPrefix, yearSuffix, monthPrefix, monthSuffix, digits), DateOnly.Parse(date), counter)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData(NumberSequenceScheme.FourDigitYear, "2026-03-05", 2026, 0)]
    [InlineData(NumberSequenceScheme.TwoDigitYear, "2026-12-31", 2026, 0)]
    [InlineData(NumberSequenceScheme.FourDigitYearMonth, "2026-03-05", 2026, 3)]
    [InlineData(NumberSequenceScheme.TwoDigitYearMonth, "2027-01-01", 2027, 1)]
    [InlineData(NumberSequenceScheme.CounterOnly, "2026-03-05", 0, 0)]
    public void The_period_of_a_date_follows_the_scheme_with_0_for_no_year_or_month(NumberSequenceScheme scheme, string date, int year, int month)
    {
        NumberSequenceFormatter.PeriodOf(scheme, DateOnly.Parse(date)).ShouldBe((year, month));
        NumberSequenceFormatter.NoPeriod.ShouldBe(0);
    }

    [Fact]
    public void The_scheme_helpers_say_which_parts_a_scheme_has()
    {
        foreach (var scheme in Enum.GetValues<NumberSequenceScheme>())
        {
            NumberSequenceFormatter.UsesYear(scheme).ShouldBe(scheme != NumberSequenceScheme.CounterOnly, scheme.ToString());
            NumberSequenceFormatter.UsesMonth(scheme).ShouldBe(scheme is NumberSequenceScheme.FourDigitYearMonth or NumberSequenceScheme.TwoDigitYearMonth, scheme.ToString());
            NumberSequenceFormatter.UsesTwoDigitYear(scheme).ShouldBe(scheme is NumberSequenceScheme.TwoDigitYear or NumberSequenceScheme.TwoDigitYearMonth, scheme.ToString());
        }

        /* OpenTMS EntityNumberCreationOption, same order. */
        Enum.GetValues<NumberSequenceScheme>().ShouldBe(
        [
            NumberSequenceScheme.FourDigitYear, NumberSequenceScheme.TwoDigitYear, NumberSequenceScheme.FourDigitYearMonth,
            NumberSequenceScheme.TwoDigitYearMonth, NumberSequenceScheme.CounterOnly
        ]);
    }

    [Fact]
    public void An_unknown_scheme_or_a_negative_counter_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NumberSequenceFormatter.Format(Format((NumberSequenceScheme)0), new DateOnly(2026, 1, 1), 1));
        Should.Throw<ArgumentOutOfRangeException>(() => NumberSequenceFormatter.PeriodOf((NumberSequenceScheme)99, new DateOnly(2026, 1, 1)));
        Should.Throw<ArgumentOutOfRangeException>(() => NumberSequenceFormatter.Format(Format(NumberSequenceScheme.CounterOnly), new DateOnly(2026, 1, 1), -1));
        Should.Throw<ArgumentNullException>(() => NumberSequenceFormatter.Format(null!, new DateOnly(2026, 1, 1), 1));
    }

    [Fact]
    public void Two_formats_that_differ_only_in_empty_versus_missing_affixes_are_equal_once_normalized()
    {
        var typed = new NumberSequenceFormat(NumberSequenceScheme.TwoDigitYear, "O-", "", "-", "", null, 4);
        var stored = new NumberSequenceFormat(NumberSequenceScheme.TwoDigitYear, "O-", null, "-", null, "", 4);
        typed.Normalize().ShouldBe(stored.Normalize());
        (typed with { YearSuffix = " " }).Normalize().YearSuffix.ShouldBe(" ", "a space is a separator, not an empty affix");
        (typed with { YearSuffix = " " }).Normalize().ShouldNotBe(stored.Normalize());
    }
}
