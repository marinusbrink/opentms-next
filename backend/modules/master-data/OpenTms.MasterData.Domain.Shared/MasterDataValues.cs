using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenTms.MasterData;

/// <summary>Normalisation and shape checks shared by the Relation and Driver run (design 43).</summary>
public static partial class MasterDataValues
{
    [GeneratedRegex(@"^[A-Z]{2}[A-Z0-9]{2,13}$")]
    private static partial Regex VatShape();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailShape();

    /// <summary>Lower-case, diacritics folded, anything but letters, digits and single spaces removed.</summary>
    public static string SearchKey(params string?[] parts)
    {
        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            var folded = part.Normalize(NormalizationForm.FormD);
            foreach (var c in folded)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
                else if (builder.Length > 0 && builder[^1] != ' ')
                {
                    builder.Append(' ');
                }
            }

            if (builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }

        var key = builder.ToString().Trim();
        return key.Length > MasterDataConsts.MaxSearchKeyLength ? key[..MasterDataConsts.MaxSearchKeyLength] : key;
    }

    /// <summary>Upper-case, spaces and dots removed; null when empty.</summary>
    public static string? NormalizeLegalNumber(string? value)
    {
        var text = (value ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Replace(".", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();
        return text.Length == 0 ? null : text;
    }

    public static bool IsVatNumberShape(string normalized) => VatShape().IsMatch(normalized);

    public static bool IsEmailShape(string value) => EmailShape().IsMatch(value);

    /// <summary>Upper-case, no spaces; null when empty.</summary>
    public static string? NormalizeIban(string? value)
    {
        var text = (value ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();
        return text.Length == 0 ? null : text;
    }

    /// <summary>ISO 7064 mod 97-10 over the rearranged IBAN; length 15–34, letters and digits only.</summary>
    public static bool IsValidIban(string normalized)
    {
        if (normalized.Length is < 15 or > 34 || !normalized.All(char.IsAsciiLetterOrDigit) || !char.IsAsciiLetter(normalized[0]) || !char.IsAsciiLetter(normalized[1]))
        {
            return false;
        }

        var rearranged = normalized[4..] + normalized[..4];
        var remainder = 0;
        foreach (var c in rearranged)
        {
            var value = char.IsAsciiDigit(c) ? c - '0' : c - 'A' + 10;
            remainder = value >= 10 ? (remainder * 100 + value) % 97 : (remainder * 10 + value) % 97;
        }

        return remainder == 1;
    }

    /// <summary>The language code of a configured culture ("nl", "en"); null when empty.</summary>
    public static string? NormalizeCulture(string? value)
    {
        var text = (value ?? string.Empty).Trim().ToLowerInvariant();
        return text.Length == 0 ? null : text;
    }
}
