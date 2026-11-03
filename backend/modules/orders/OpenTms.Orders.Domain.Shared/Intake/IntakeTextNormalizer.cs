using System.Globalization;
using System.Text;

namespace OpenTms.Orders.Intake;

/// <summary>
/// The one normalisation of the texts an intake alias matches and the resolver compares names with (design 72 "Review"):
/// trimmed, whitespace collapsed to single spaces, lower case (invariant), diacritics stripped, cut at
/// <see cref="IntakeConsts.MaxAliasTextLength"/>. "  Café  De Kroon " and "cafe de kroon" are the same text.
/// </summary>
public static class IntakeTextNormalizer
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        var normalized = builder.ToString().Normalize(NormalizationForm.FormC);
        return normalized.Length > IntakeConsts.MaxAliasTextLength ? normalized[..IntakeConsts.MaxAliasTextLength].TrimEnd() : normalized;
    }

    /// <summary>The parts joined by single spaces and normalised; empty when every part is empty.</summary>
    public static string Join(params string?[] parts) => Normalize(string.Join(' ', parts));
}
