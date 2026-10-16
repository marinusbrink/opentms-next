namespace OpenTms.Platform.NumberSequences;

/// <summary>
/// The numbering fields of a number sequence: the scheme, the five affixes and the minimum number of counter digits
/// (design 49). Affixes are kept as typed (separators such as "-" or " " are meaningful); an empty affix is null.
/// </summary>
public sealed record NumberSequenceFormat(
    NumberSequenceScheme Scheme,
    string? Prefix,
    string? YearPrefix,
    string? YearSuffix,
    string? MonthPrefix,
    string? MonthSuffix,
    int MinimumDigits)
{
    /// <summary>The same format with every empty affix as null, so two equal formats compare equal.</summary>
    public NumberSequenceFormat Normalize() => this with
    {
        Prefix = Affix(Prefix),
        YearPrefix = Affix(YearPrefix),
        YearSuffix = Affix(YearSuffix),
        MonthPrefix = Affix(MonthPrefix),
        MonthSuffix = Affix(MonthSuffix)
    };

    private static string? Affix(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
