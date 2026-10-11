using System;
using System.Text.RegularExpressions;
using Volo.Abp;

namespace OpenTms.MasterData;

/// <summary>Code normalisation: codes are stored trimmed and upper-cased so uniqueness is on one spelling.</summary>
public static partial class MasterDataCodes
{
    [GeneratedRegex("^[A-Z]{2}$")]
    private static partial Regex Alpha2();

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex Alpha3();

    [GeneratedRegex("^[0-9]{4}$")]
    private static partial Regex FourDigits();

    public static string? Normalize(string? code)
    {
        var value = code?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public static string NormalizeRequired(string code, int maxLength, string field)
    {
        var value = Normalize(code);
        if (value == null || value.Length > maxLength)
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidCode).WithData("field", field).WithData("value", code ?? string.Empty);
        }

        return value;
    }

    public static string NormalizeIsoAlpha2(string code)
    {
        var value = Normalize(code);
        if (value == null || !Alpha2().IsMatch(value))
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidCode).WithData("field", "isoAlpha2").WithData("value", code ?? string.Empty);
        }

        return value;
    }

    public static string NormalizeIsoAlpha3(string code, string field)
    {
        var value = Normalize(code);
        if (value == null || !Alpha3().IsMatch(value))
        {
            throw new BusinessException(MasterDataErrorCodes.InvalidCode).WithData("field", field).WithData("value", code ?? string.Empty);
        }

        return value;
    }

    public static bool IsUnNumber(string value) => FourDigits().IsMatch(value);

    /// <summary>Postcodes and prefixes compare without spaces and case.</summary>
    public static string NormalizePostalCode(string? value)
        => (value ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();
}
