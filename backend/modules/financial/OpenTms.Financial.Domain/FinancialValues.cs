using System;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;

namespace OpenTms.Financial;

/// <summary>Normalisation of the plain values the Financial master data shares (design 52): ledger accounts, ISO codes, enums.</summary>
public static class FinancialValues
{
    /// <summary>A free ledger account of at most 32 characters (Assumption 2); blank is null.</summary>
    public static string? LedgerAccount(string? value, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > ReferenceDataConsts.MaxLedgerAccountLength ? throw Invalid(field, trimmed) : trimmed;
    }

    /// <summary>An ISO code of exactly <paramref name="length"/> ASCII letters, stored upper case.</summary>
    public static string IsoCode(string? value, int length, string field)
    {
        var normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length != length)
        {
            throw Invalid(field, normalized);
        }

        foreach (var c in normalized)
        {
            if (!char.IsAsciiLetterUpper(c))
            {
                throw Invalid(field, normalized);
            }
        }

        return normalized;
    }

    public static TEnum Defined<TEnum>(TEnum value, string field)
        where TEnum : struct, Enum
        => Enum.IsDefined(value) ? value : throw Invalid(field, Convert.ToInt32(value).ToString());

    /// <summary>True when <paramref name="value"/> has at most <paramref name="scale"/> decimals (no silent rounding by the column).</summary>
    public static bool HasScale(decimal value, int scale) => decimal.Round(value, scale) == value;

    public static BusinessException Invalid(string field, string value)
        => new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", field).WithData("value", value);
}
