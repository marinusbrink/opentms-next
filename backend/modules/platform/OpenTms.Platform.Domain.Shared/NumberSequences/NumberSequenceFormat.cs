using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;

namespace OpenTms.Platform.NumberSequences;

/// <summary>
/// How a numbered type formats its numbers (assessment §3.9; docs/designs/50-platform-reference-data-and-number-sequences.md):
/// prefix, year, month and a zero-padded counter, joined by one separator. Stored as owned columns on each numbered
/// type. Immutable; <see cref="Create"/> validates.
/// </summary>
public sealed class NumberSequenceFormat : IEquatable<NumberSequenceFormat>
{
    public const int MaxPrefixLength = 16;
    public const int MinDigits = 3;
    public const int MaxDigits = 10;
    public const int MaxPeriodLength = 7;

    /// <summary>The separators a format may use; empty joins the parts directly.</summary>
    public static readonly IReadOnlyList<string> AllowedSeparators = ["", "-", "/", "."];

    public string? Prefix { get; private set; }

    public bool IncludeYear { get; private set; }

    public bool IncludeMonth { get; private set; }

    public string Separator { get; private set; } = "";

    public int Digits { get; private set; } = 5;

    private NumberSequenceFormat()
    {
    }

    public static NumberSequenceFormat Create(string? prefix, bool includeYear, bool includeMonth, string? separator, int digits)
    {
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix) ? null : prefix.Trim().ToUpperInvariant();
        var normalizedSeparator = separator ?? "";

        if (normalizedPrefix != null && (normalizedPrefix.Length > MaxPrefixLength || !normalizedPrefix.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
        {
            throw Invalid("prefix", normalizedPrefix);
        }

        if (includeMonth && !includeYear)
        {
            throw Invalid("includeMonth", "true");
        }

        if (!AllowedSeparators.Contains(normalizedSeparator))
        {
            throw Invalid("separator", normalizedSeparator);
        }

        if (digits < MinDigits || digits > MaxDigits)
        {
            throw Invalid("digits", digits.ToString(CultureInfo.InvariantCulture));
        }

        return new NumberSequenceFormat
        {
            Prefix = normalizedPrefix,
            IncludeYear = includeYear,
            IncludeMonth = includeMonth,
            Separator = normalizedSeparator,
            Digits = digits
        };
    }

    /// <summary>The counter period a number issued on <paramref name="localDate"/> belongs to: "", "2026" or "2026-10".</summary>
    public string Period(DateOnly localDate)
    {
        if (!IncludeYear)
        {
            return "";
        }

        var year = localDate.Year.ToString("D4", CultureInfo.InvariantCulture);
        return IncludeMonth ? year + "-" + localDate.Month.ToString("D2", CultureInfo.InvariantCulture) : year;
    }

    /// <summary>The number for <paramref name="counter"/> on <paramref name="localDate"/>, e.g. "TO-2026-10-00042".</summary>
    public string Format(long counter, DateOnly localDate)
    {
        if (counter < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(counter), "A counter starts at 1.");
        }

        var parts = new List<string>(4);
        if (Prefix != null)
        {
            parts.Add(Prefix);
        }

        if (IncludeYear)
        {
            parts.Add(localDate.Year.ToString("D4", CultureInfo.InvariantCulture));
        }

        if (IncludeMonth)
        {
            parts.Add(localDate.Month.ToString("D2", CultureInfo.InvariantCulture));
        }

        parts.Add(counter.ToString("D" + Digits, CultureInfo.InvariantCulture));
        return string.Join(Separator, parts);
    }

    public bool Equals(NumberSequenceFormat? other)
        => other != null && Prefix == other.Prefix && IncludeYear == other.IncludeYear && IncludeMonth == other.IncludeMonth
           && Separator == other.Separator && Digits == other.Digits;

    public override bool Equals(object? obj) => Equals(obj as NumberSequenceFormat);

    public override int GetHashCode() => HashCode.Combine(Prefix, IncludeYear, IncludeMonth, Separator, Digits);

    private static BusinessException Invalid(string field, string value)
        => new BusinessException(ReferenceDataErrorCodes.InvalidNumberSequence).WithData("field", field).WithData("value", value);
}
