using System;
using Volo.Abp;

namespace OpenTms.Platform.NumberSequences;

/// <summary>
/// A number sequence (design 49, assessment §3.9): the format the order, trip, invoice and purchase-order types draw
/// their numbers from, referenced by id. The counters live in <see cref="NumberSequenceCounter"/> rows, one per period,
/// written only by the atomic upsert of <see cref="INumberSequenceCounterRepository"/>. Once any counter row exists
/// ("in use") the numbering fields are frozen and the sequence cannot be deleted; name and description stay editable.
/// </summary>
public class NumberSequence : PlatformMasterDataAggregateRoot
{
    public string Name { get; private set; } = default!;

    /// <summary>The name in upper case: the unique key (PO 2026-10-16).</summary>
    public string NormalizedName { get; private set; } = default!;

    public NumberSequenceScheme Scheme { get; private set; }

    public string? Prefix { get; private set; }

    public string? YearPrefix { get; private set; }

    public string? YearSuffix { get; private set; }

    public string? MonthPrefix { get; private set; }

    public string? MonthSuffix { get; private set; }

    /// <summary>1–12: the counter is padded with zeros to this many digits (replaces OpenTMS's zeros-only Format).</summary>
    public int MinimumDigits { get; private set; }

    protected NumberSequence()
    {
    }

    public NumberSequence(Guid id, Guid? tenantId, string name, NumberSequenceFormat format)
        : base(id, tenantId)
    {
        SetName(name);
        ChangeFormat(format, isInUse: false);
    }

    public NumberSequenceFormat Format => new(Scheme, Prefix, YearPrefix, YearSuffix, MonthPrefix, MonthSuffix, MinimumDigits);

    public void SetName(string name)
    {
        Name = NormalizeName(name);
        NormalizedName = Name.ToUpperInvariant();
    }

    /// <summary>
    /// Sets the numbering fields. Refused with NumberSequenceInUse when numbers have been drawn and anything would change
    /// (OpenTMS <c>OutboundInvoiceTypeAppService</c>): a changed format would break the numbering of past records.
    /// </summary>
    public void ChangeFormat(NumberSequenceFormat format, bool isInUse)
    {
        var normalized = Check.NotNull(format, nameof(format)).Normalize();
        if (isInUse && normalized != Format.Normalize())
        {
            throw new BusinessException(PlatformErrorCodes.NumberSequenceInUse);
        }

        if (normalized.MinimumDigits < PlatformMasterDataConsts.MinMinimumDigits || normalized.MinimumDigits > PlatformMasterDataConsts.MaxMinimumDigits)
        {
            throw new ArgumentOutOfRangeException(nameof(format), normalized.MinimumDigits, "MinimumDigits must be between 1 and 12.");
        }

        Scheme = Defined(normalized.Scheme, nameof(format));
        Prefix = Affix(normalized.Prefix, nameof(format.Prefix));
        YearPrefix = Affix(normalized.YearPrefix, nameof(format.YearPrefix));
        YearSuffix = Affix(normalized.YearSuffix, nameof(format.YearSuffix));
        MonthPrefix = Affix(normalized.MonthPrefix, nameof(format.MonthPrefix));
        MonthSuffix = Affix(normalized.MonthSuffix, nameof(format.MonthSuffix));
        MinimumDigits = normalized.MinimumDigits;
    }

    /// <summary>Kept as typed (a separator may be a space); only the length is checked.</summary>
    private static string? Affix(string? value, string parameterName)
        => value == null ? null : Check.Length(value, parameterName, PlatformMasterDataConsts.MaxAffixLength);
}
