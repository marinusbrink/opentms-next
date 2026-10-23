using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Financial.Tariffs;

/// <summary>
/// A discount or surcharge (diesel surcharge, weekend surcharge, regional surcharges; design 52, assessment §5): the
/// code is the stable key invoicing matches on, never the name. The value is a validity row (§3.4) in the unit of
/// <see cref="ValueKind"/>; regions restrict where it applies (none = everywhere, Assumption 4).
/// </summary>
public class DiscountOrSurcharge : ReferenceDataAggregateRoot
{
    public string Code { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    public DiscountOrSurchargeDirection Direction { get; private set; } = DiscountOrSurchargeDirection.Surcharge;

    public DiscountOrSurchargeValueKind ValueKind { get; private set; } = DiscountOrSurchargeValueKind.FixedAmount;

    public DiscountOrSurchargeSide Side { get; private set; } = DiscountOrSurchargeSide.Revenue;

    public DaysOfWeek DaysOfWeek { get; private set; } = DaysOfWeek.All;

    /// <summary>FK to a VAT percentage of this module (restrict); null = the tenant's default VAT category.</summary>
    public Guid? VatPercentageId { get; set; }

    public string? LedgerAccount { get; private set; }

    public bool IsArchived { get; set; }

    public virtual ICollection<DiscountOrSurchargeValue> Values { get; private set; } = new List<DiscountOrSurchargeValue>();

    public virtual ICollection<DiscountOrSurchargeRegion> Regions { get; private set; } = new List<DiscountOrSurchargeRegion>();

    protected DiscountOrSurcharge()
    {
    }

    public DiscountOrSurcharge(Guid id, Guid? tenantId, string code, string name, DiscountOrSurchargeDirection direction, DiscountOrSurchargeValueKind valueKind, DiscountOrSurchargeSide side)
        : base(id, tenantId)
    {
        SetCode(code);
        SetName(name);
        SetDirection(direction);
        SetValueKind(valueKind);
        SetSide(side);
    }

    public void SetCode(string code) => Code = NormalizeCode(code);

    public void SetName(string name) => Name = NormalizeName(name);

    public void SetDirection(DiscountOrSurchargeDirection direction) => Direction = FinancialValues.Defined(direction, "direction");

    /// <summary>Set before <see cref="SetValues"/>: the currency rule of every value row follows the kind.</summary>
    public void SetValueKind(DiscountOrSurchargeValueKind valueKind) => ValueKind = FinancialValues.Defined(valueKind, "valueKind");

    public void SetSide(DiscountOrSurchargeSide side) => Side = FinancialValues.Defined(side, "side");

    public void SetDaysOfWeek(DaysOfWeek days)
    {
        if (days == DaysOfWeek.None)
        {
            throw new BusinessException(FinancialErrorCodes.DaysOfWeekRequired);
        }

        if ((days & ~DaysOfWeek.All) != 0)
        {
            throw FinancialValues.Invalid("daysOfWeek", ((int)days).ToString());
        }

        DaysOfWeek = days;
    }

    public void SetLedgerAccount(string? account) => LedgerAccount = FinancialValues.LedgerAccount(account, "ledgerAccount");

    /// <summary>True when a currency belongs on the values: fixed amounts and amounts per kilometre (assessment §5).</summary>
    public static bool NeedsCurrency(DiscountOrSurchargeValueKind kind) => kind != DiscountOrSurchargeValueKind.Percentage;

    /// <summary>
    /// Replaces the values (§3.4): at least one, at most 200, one per date, ≥ 0 with four decimals (≤ 1,000 for a
    /// percentage); a currency on every amount and none on a percentage. With <paramref name="fixedOn"/> (today, on an
    /// update) the values that have taken effect must come back unchanged.
    /// </summary>
    public void SetValues(IEnumerable<(DateOnly ValidFrom, decimal Value, string? CurrencyCode)> values, DateOnly? fixedOn, Func<Guid> newId)
    {
        var incoming = values.Select(v => (v.ValidFrom, Value: CheckValue(ValueKind, v.Value), CurrencyCode: CheckCurrency(ValueKind, v.CurrencyCode))).ToList();

        ValidityRows.Replace(
            Values,
            incoming,
            v => v.ValidFrom,
            (row, v) => row.Value == v.Value && row.CurrencyCode == v.CurrencyCode,
            v => new DiscountOrSurchargeValue(newId(), TenantId, Id, v.ValidFrom, v.Value, v.CurrencyCode),
            (row, v) => row.Set(v.Value, v.CurrencyCode),
            fixedOn,
            FinancialConsts.MaxDiscountOrSurchargeValues,
            "values");
    }

    /// <summary>The value valid on <paramref name="date"/>; null before the first value.</summary>
    public DiscountOrSurchargeValue? ValueOn(DateOnly date) => ValidityRows.On(Values, date);

    /// <summary>
    /// Replaces the regions, at most 100, each once. Written with the parent in one unit of work (OpenTMS never saved
    /// the region links of a new surcharge, assessment §6).
    /// </summary>
    public void SetRegions(IEnumerable<(Guid GeographicalRegionId, RegionAppliesAt AppliesAt)> regions, Func<Guid> newId)
    {
        var incoming = regions.ToList();
        if (incoming.Count > FinancialConsts.MaxDiscountOrSurchargeRegions)
        {
            throw new BusinessException(ReferenceDataErrorCodes.TooManyChildren).WithData("field", "regions").WithData("max", FinancialConsts.MaxDiscountOrSurchargeRegions);
        }

        var duplicate = incoming.GroupBy(r => r.GeographicalRegionId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
        {
            throw new BusinessException(ReferenceDataErrorCodes.DuplicateKey).WithData("field", "geographicalRegionId").WithData("value", duplicate.Key.ToString());
        }

        foreach (var region in incoming)
        {
            if (region.GeographicalRegionId == Guid.Empty)
            {
                throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "geographicalRegionId");
            }

            FinancialValues.Defined(region.AppliesAt, "appliesAt");
            var existing = Regions.FirstOrDefault(r => r.GeographicalRegionId == region.GeographicalRegionId);
            if (existing == null)
            {
                Regions.Add(new DiscountOrSurchargeRegion(newId(), TenantId, Id, region.GeographicalRegionId, region.AppliesAt));
            }
            else
            {
                existing.AppliesAt = region.AppliesAt;
            }
        }

        var keep = incoming.Select(r => r.GeographicalRegionId).ToHashSet();
        foreach (var stale in Regions.Where(r => !keep.Contains(r.GeographicalRegionId)).ToList())
        {
            Regions.Remove(stale);
        }
    }

    private static decimal CheckValue(DiscountOrSurchargeValueKind kind, decimal value)
    {
        if (!FinancialValues.HasScale(value, FinancialConsts.ValueScale))
        {
            throw FinancialValues.Invalid("value", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (kind == DiscountOrSurchargeValueKind.Percentage)
        {
            if (value < 0 || value > FinancialConsts.MaxDiscountOrSurchargePercentage)
            {
                throw new BusinessException(FinancialErrorCodes.PercentageOutOfRange)
                    .WithData("value", value)
                    .WithData("max", FinancialConsts.MaxDiscountOrSurchargePercentage)
                    .WithData("decimals", FinancialConsts.ValueScale);
            }
        }
        else if (value < 0)
        {
            throw FinancialValues.Invalid("value", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return value;
    }

    private static string? CheckCurrency(DiscountOrSurchargeValueKind kind, string? currencyCode)
    {
        var blank = string.IsNullOrWhiteSpace(currencyCode);
        if (NeedsCurrency(kind))
        {
            return blank
                ? throw new BusinessException(FinancialErrorCodes.CurrencyRequired)
                : FinancialValues.IsoCode(currencyCode, ReferenceDataConsts.IsoCurrencyCodeLength, "currencyCode");
        }

        return blank ? null : throw new BusinessException(FinancialErrorCodes.CurrencyNotAllowed);
    }
}

/// <summary>A value of a discount or surcharge valid from its date until the next row's date; fixed once in effect.</summary>
public class DiscountOrSurchargeValue : Entity<Guid>, IMultiTenant, IValidityRow
{
    public Guid? TenantId { get; private set; }

    public Guid DiscountOrSurchargeId { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    /// <summary><c>numeric(18,4)</c>: an amount in <see cref="CurrencyCode"/>, or a percentage.</summary>
    public decimal Value { get; private set; }

    /// <summary>ISO 4217; present for fixed amounts and amounts per kilometre, absent for a percentage.</summary>
    public string? CurrencyCode { get; private set; }

    protected DiscountOrSurchargeValue()
    {
    }

    public DiscountOrSurchargeValue(Guid id, Guid? tenantId, Guid discountOrSurchargeId, DateOnly validFrom, decimal value, string? currencyCode)
        : base(id)
    {
        TenantId = tenantId;
        DiscountOrSurchargeId = discountOrSurchargeId;
        ValidFrom = validFrom;
        Set(value, currencyCode);
    }

    /// <summary>Checked by <see cref="DiscountOrSurcharge.SetValues"/>, which knows the value kind.</summary>
    internal void Set(decimal value, string? currencyCode)
    {
        Value = value;
        CurrencyCode = currencyCode;
    }
}

/// <summary>A Master Data geographical region a discount or surcharge applies in (by id; design 52).</summary>
public class DiscountOrSurchargeRegion : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid DiscountOrSurchargeId { get; private set; }

    /// <summary>A Master Data id, checked through IMasterDataReferenceLookup; no foreign key across schemas.</summary>
    public Guid GeographicalRegionId { get; private set; }

    public RegionAppliesAt AppliesAt { get; internal set; } = RegionAppliesAt.Either;

    protected DiscountOrSurchargeRegion()
    {
    }

    public DiscountOrSurchargeRegion(Guid id, Guid? tenantId, Guid discountOrSurchargeId, Guid geographicalRegionId, RegionAppliesAt appliesAt)
        : base(id)
    {
        TenantId = tenantId;
        DiscountOrSurchargeId = discountOrSurchargeId;
        GeographicalRegionId = geographicalRegionId;
        AppliesAt = appliesAt;
    }
}
