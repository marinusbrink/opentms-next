using System;
using System.Globalization;
using System.Text;

namespace OpenTms.Platform.NumberSequences;

/// <summary>
/// Builds a number from a sequence's format, the record's date and the counter value (design 49), and gives the
/// counter period of a date. Pure; the frontend mirrors it for the editor's example.
/// Number = Prefix + [YearPrefix + yyyy|yy + YearSuffix] + [MonthPrefix + MM + MonthSuffix] + counter padded to
/// MinimumDigits. Example: "O-" + "26" + "-" + "0001" = "O-26-0001".
/// </summary>
public static class NumberSequenceFormatter
{
    /// <summary>The period value stored for "no year" or "no month" (0 instead of NULL, so the upsert's conflict target works).</summary>
    public const int NoPeriod = 0;

    public static bool UsesYear(NumberSequenceScheme scheme) => scheme is
        NumberSequenceScheme.FourDigitYear or NumberSequenceScheme.TwoDigitYear or
        NumberSequenceScheme.FourDigitYearMonth or NumberSequenceScheme.TwoDigitYearMonth;

    public static bool UsesMonth(NumberSequenceScheme scheme) => scheme is
        NumberSequenceScheme.FourDigitYearMonth or NumberSequenceScheme.TwoDigitYearMonth;

    public static bool UsesTwoDigitYear(NumberSequenceScheme scheme) => scheme is
        NumberSequenceScheme.TwoDigitYear or NumberSequenceScheme.TwoDigitYearMonth;

    /// <summary>The counter period of <paramref name="date"/>: (year, 0) per year, (year, month) per month, (0, 0) never.</summary>
    public static (int PeriodYear, int PeriodMonth) PeriodOf(NumberSequenceScheme scheme, DateOnly date)
    {
        EnsureDefined(scheme);
        return (UsesYear(scheme) ? date.Year : NoPeriod, UsesMonth(scheme) ? date.Month : NoPeriod);
    }

    public static string Format(NumberSequenceFormat format, DateOnly date, long counter)
    {
        ArgumentNullException.ThrowIfNull(format);
        EnsureDefined(format.Scheme);
        ArgumentOutOfRangeException.ThrowIfNegative(counter);

        var number = new StringBuilder();
        number.Append(format.Prefix);

        if (UsesYear(format.Scheme))
        {
            var year = UsesTwoDigitYear(format.Scheme)
                ? (date.Year % 100).ToString("D2", CultureInfo.InvariantCulture)
                : date.Year.ToString("D4", CultureInfo.InvariantCulture);
            number.Append(format.YearPrefix).Append(year).Append(format.YearSuffix);
        }

        if (UsesMonth(format.Scheme))
        {
            number.Append(format.MonthPrefix).Append(date.Month.ToString("D2", CultureInfo.InvariantCulture)).Append(format.MonthSuffix);
        }

        var digits = Math.Clamp(format.MinimumDigits, PlatformMasterDataConsts.MinMinimumDigits, PlatformMasterDataConsts.MaxMinimumDigits);
        number.Append(counter.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0'));
        return number.ToString();
    }

    private static void EnsureDefined(NumberSequenceScheme scheme)
    {
        if (!Enum.IsDefined(scheme))
        {
            throw new ArgumentOutOfRangeException(nameof(scheme), scheme, "Unknown number sequence scheme.");
        }
    }
}
