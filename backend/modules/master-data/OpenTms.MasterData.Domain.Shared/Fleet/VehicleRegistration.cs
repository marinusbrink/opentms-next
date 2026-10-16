using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenTms.MasterData.Fleet;

/// <summary>
/// The licence plate and VIN rules of design 48 ("licence plate as a value object", assessment §5). A plate is
/// stored normalised: upper case, letters and digits only ("12-abc-3" → "12ABC3"); spaces, dashes and dots are
/// separators and dropped, any other character makes the plate invalid. It is shown through <see cref="Format"/>:
/// Dutch plates by sidecode, other countries as stored. A VIN has the ISO 3779 shape: 17 characters, letters and
/// digits without I, O and Q.
/// </summary>
public static partial class VehicleRegistration
{
    public const string NetherlandsIsoAlpha2 = "NL";

    [GeneratedRegex("^[A-HJ-NPR-Z0-9]{17}$")]
    private static partial Regex VinShape();

    /* The Dutch sidecodes 1–14 as letter (L) / digit (D) patterns with the positions of the dashes. */
    private static readonly Dictionary<string, int[]> NlSidecodes = new(StringComparer.Ordinal)
    {
        ["LLDDDD"] = [2, 4],
        ["DDDDLL"] = [2, 4],
        ["DDLLDD"] = [2, 4],
        ["LLDDLL"] = [2, 4],
        ["LLLLDD"] = [2, 4],
        ["DDLLLL"] = [2, 4],
        ["DDLLLD"] = [2, 5],
        ["DLLLDD"] = [1, 4],
        ["LLDDDL"] = [2, 5],
        ["LDDDLL"] = [1, 4],
        ["LLLDDL"] = [3, 5],
        ["LDDLLL"] = [1, 3],
        ["DLLDDD"] = [1, 3],
        ["DDDLLD"] = [3, 5],
    };

    private static bool IsSeparator(char c) => char.IsWhiteSpace(c) || c is '-' or '.' or '·' or '_';

    /// <summary>
    /// Normalises a typed plate. Returns true with null for an empty input, true with the normalised plate for a valid
    /// one, and false when the plate holds anything but letters, digits and separators or is longer than the column.
    /// </summary>
    public static bool TryNormalizeLicencePlate(string? value, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (IsSeparator(c))
            {
                continue;
            }

            if (!char.IsAsciiLetterOrDigit(c))
            {
                return false;
            }

            builder.Append(char.ToUpperInvariant(c));
        }

        if (builder.Length == 0)
        {
            return true;
        }

        if (builder.Length > MasterDataConsts.MaxLicencePlateLength)
        {
            return false;
        }

        normalized = builder.ToString();
        return true;
    }

    /// <summary>The search form of a term: separators dropped, upper case; other characters kept (the search simply finds nothing).</summary>
    public static string SearchKey(string? term)
    {
        var builder = new StringBuilder();
        foreach (var c in term ?? string.Empty)
        {
            if (!IsSeparator(c))
            {
                builder.Append(char.ToUpperInvariant(c));
            }
        }

        return builder.ToString();
    }

    /// <summary>A stored plate as shown: Dutch plates with the dashes of their sidecode, everything else as stored.</summary>
    public static string? Format(string? plate, string? countryIsoAlpha2)
    {
        if (string.IsNullOrEmpty(plate))
        {
            return plate;
        }

        if (!string.Equals(countryIsoAlpha2, NetherlandsIsoAlpha2, StringComparison.OrdinalIgnoreCase) || plate.Length != 6)
        {
            return plate;
        }

        var pattern = new StringBuilder(6);
        foreach (var c in plate)
        {
            pattern.Append(char.IsAsciiDigit(c) ? 'D' : char.IsAsciiLetter(c) ? 'L' : '?');
        }

        if (!NlSidecodes.TryGetValue(pattern.ToString(), out var dashes))
        {
            return plate;
        }

        return plate[..dashes[0]] + "-" + plate[dashes[0]..dashes[1]] + "-" + plate[dashes[1]..];
    }

    /// <summary>Trimmed, upper case; null when empty. Shape checked with <see cref="IsVinShape"/>.</summary>
    public static string? NormalizeVin(string? value)
    {
        var text = value?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    public static bool IsVinShape(string normalized) => VinShape().IsMatch(normalized);
}
