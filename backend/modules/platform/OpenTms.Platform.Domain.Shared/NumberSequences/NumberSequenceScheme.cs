namespace OpenTms.Platform.NumberSequences;

/// <summary>
/// How a number sequence builds its numbers and when its counter restarts (design 49; OpenTMS
/// <c>EntityNumberCreationOption</c>, same order): per year, per month, or never.
/// </summary>
public enum NumberSequenceScheme
{
    /// <summary>Prefix + YearPrefix + yyyy + YearSuffix + counter; the counter restarts every year.</summary>
    FourDigitYear = 1,

    /// <summary>Prefix + YearPrefix + yy + YearSuffix + counter; the counter restarts every year.</summary>
    TwoDigitYear = 2,

    /// <summary>Prefix + YearPrefix + yyyy + YearSuffix + MonthPrefix + MM + MonthSuffix + counter; restarts every month.</summary>
    FourDigitYearMonth = 3,

    /// <summary>Prefix + YearPrefix + yy + YearSuffix + MonthPrefix + MM + MonthSuffix + counter; restarts every month.</summary>
    TwoDigitYearMonth = 4,

    /// <summary>Prefix + counter; the counter never restarts.</summary>
    CounterOnly = 5
}
