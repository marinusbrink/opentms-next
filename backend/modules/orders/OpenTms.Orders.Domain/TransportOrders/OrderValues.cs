using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;

namespace OpenTms.Orders.TransportOrders;

/// <summary>The value rules of the order aggregate (design 58): bounded texts, defined enums, attributes within bounds.</summary>
public static class OrderValues
{
    public static string? Text(string? value, int maxLength, string field)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length > maxLength)
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", field).WithData("value", text.Length);
        }

        return text;
    }

    public static string RequiredText(string? value, int maxLength, string field)
        => Text(value, maxLength, field) ?? throw new BusinessException(ReferenceDataErrorCodes.NameRequired).WithData("culture", field);

    public static TEnum Defined<TEnum>(TEnum value, string field) where TEnum : struct, Enum
        => Enum.IsDefined(value) ? value : throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", field).WithData("value", Convert.ToInt32(value));

    public static void NonNegative(decimal? value, string field)
    {
        if (value is < 0)
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", field).WithData("value", value.Value);
        }
    }

    public static void NonNegative(double? value, string field)
    {
        if (value is < 0 || (value != null && (double.IsNaN(value.Value) || double.IsInfinity(value.Value))))
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", field).WithData("value", value.Value);
        }
    }

    public static string CurrencyCode(string? code)
    {
        var value = code?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(value) || value.Length != OrdersConsts.MaxCurrencyCodeLength || !value.All(char.IsAsciiLetterUpper))
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "currencyCode").WithData("value", code ?? "");
        }

        return value;
    }

    /// <summary>
    /// The attributes of an order, line or template (design 56, Q12): at most <see cref="OrdersConsts.MaxAttributes"/> keys,
    /// keys trimmed and at most 32 characters, values at most 500. Empty values drop the key. The service checks that
    /// every key is a defined attribute of the target before it reaches here.
    /// </summary>
    public static Dictionary<string, string> Attributes(IReadOnlyDictionary<string, string>? attributes)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (attributes == null)
        {
            return result;
        }

        foreach (var (rawKey, rawValue) in attributes)
        {
            var key = rawKey?.Trim();
            if (string.IsNullOrEmpty(key) || key.Length > OrdersConsts.MaxAttributeKeyLength)
            {
                throw new BusinessException(OrdersErrorCodes.InvalidAttributes).WithData("key", rawKey ?? "");
            }

            var value = rawValue?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (value.Length > OrdersConsts.MaxAttributeValueLength)
            {
                throw new BusinessException(OrdersErrorCodes.InvalidAttributes).WithData("key", key);
            }

            result[key] = value;
        }

        if (result.Count > OrdersConsts.MaxAttributes)
        {
            throw new BusinessException(OrdersErrorCodes.InvalidAttributes).WithData("key", "");
        }

        return result;
    }

    public static BusinessException TooMany(string field, int max)
        => new BusinessException(ReferenceDataErrorCodes.TooManyChildren).WithData("max", max).WithData("field", field);
}
