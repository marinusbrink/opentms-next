using System;

namespace OpenTms.MasterData.Regions;

/// <summary>
/// The one place that decides whether a postcode falls in a range (design 41, "Postal-code matching").
/// A numeric range matches on the leading digit run of the postcode; a postcode without leading digits
/// never matches a numeric range (the OpenTMS bug of assessment §5). A prefix range matches when the
/// normalised postcode starts with the normalised prefix. Pure, so it is unit-tested as a table.
/// </summary>
public static class PostalCodeRangeMatcher
{
    public static bool Matches(GeographicalRegionPostalCodeRange range, string? postalCode)
        => Matches(range.FromNumeric, range.TillNumeric, range.Prefix, postalCode);

    public static bool Matches(int? fromNumeric, int? tillNumeric, string? prefix, string? postalCode)
    {
        var code = MasterDataCodes.NormalizePostalCode(postalCode);
        if (code.Length == 0)
        {
            return false;
        }

        var normalizedPrefix = MasterDataCodes.NormalizePostalCode(prefix);
        if (normalizedPrefix.Length > 0)
        {
            return code.StartsWith(normalizedPrefix, StringComparison.Ordinal);
        }

        if (fromNumeric == null || tillNumeric == null)
        {
            return false;
        }

        var leading = LeadingNumber(code);
        return leading != null && leading >= fromNumeric && leading <= tillNumeric;
    }

    /// <summary>The number formed by the leading digits of a normalised postcode; null when it starts with no digit.</summary>
    public static int? LeadingNumber(string normalizedPostalCode)
    {
        var length = 0;
        while (length < normalizedPostalCode.Length && char.IsAsciiDigit(normalizedPostalCode[length]))
        {
            length++;
        }

        if (length == 0 || length > 9)
        {
            return null;
        }

        return int.Parse(normalizedPostalCode.AsSpan(0, length));
    }
}
