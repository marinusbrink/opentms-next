using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.Localization;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Financial.Invoicing;

/// <summary>
/// A VAT category of one country with its percentages over time (design 52, assessment §5): the kind is what
/// accounting connectors map on; the percentage is a validity row (§3.4), never a field. Exactly one default per tenant
/// (§3.8).
/// </summary>
public class VatPercentage : ReferenceDataAggregateRoot, IHasReferenceDataDefault
{
    public string Code { get; private set; } = default!;

    public LocalizedName Name { get; private set; } = default!;

    public VatKind Kind { get; private set; } = VatKind.Standard;

    /// <summary>ISO 3166-1 alpha-2 (assessment §3.7); must exist in Master Data, checked by the service.</summary>
    public string CountryCode { get; private set; } = default!;

    public string? SalesLedgerAccount { get; private set; }

    public string? PurchaseLedgerAccount { get; private set; }

    public bool IsDefault { get; private set; }

    public bool IsArchived { get; private set; }

    public virtual ICollection<VatPercentageRate> Rates { get; private set; } = new List<VatPercentageRate>();

    protected VatPercentage()
    {
    }

    public VatPercentage(Guid id, Guid? tenantId, string code, LocalizedName name, VatKind kind, string countryCode)
        : base(id, tenantId)
    {
        SetCode(code);
        SetName(name);
        SetKind(kind);
        SetCountryCode(countryCode);
    }

    public void SetCode(string code) => Code = NormalizeCode(code);

    public void SetName(LocalizedName name) => Name = RequireName(name);

    public void SetKind(VatKind kind) => Kind = FinancialValues.Defined(kind, "kind");

    public void SetCountryCode(string countryCode) => CountryCode = FinancialValues.IsoCode(countryCode, ReferenceDataConsts.IsoAlpha2Length, "countryCode");

    public void SetLedgerAccounts(string? sales, string? purchase)
    {
        SalesLedgerAccount = FinancialValues.LedgerAccount(sales, "salesLedgerAccount");
        PurchaseLedgerAccount = FinancialValues.LedgerAccount(purchase, "purchaseLedgerAccount");
    }

    public void SetArchived(bool isArchived)
    {
        if (isArchived && IsDefault)
        {
            throw new BusinessException(ReferenceDataErrorCodes.DefaultRequired).WithData("reason", "archive");
        }

        IsArchived = isArchived;
    }

    public void SetDefault(bool isDefault) => IsDefault = isDefault;

    /// <summary>
    /// Replaces the rates (§3.4): at least one, at most 50, one per date, 0–100 with two decimals. With
    /// <paramref name="fixedOn"/> (today, on an update) the rates that have taken effect must come back unchanged.
    /// </summary>
    public void SetRates(IEnumerable<(DateOnly ValidFrom, decimal Percentage)> rates, DateOnly? fixedOn, Func<Guid> newId)
    {
        var incoming = rates.ToList();
        foreach (var rate in incoming)
        {
            VatPercentageRate.CheckPercentage(rate.Percentage);
        }

        ValidityRows.Replace(
            Rates,
            incoming,
            r => r.ValidFrom,
            (row, r) => row.Percentage == r.Percentage,
            r => new VatPercentageRate(newId(), TenantId, Id, r.ValidFrom, r.Percentage),
            (row, r) => row.SetPercentage(r.Percentage),
            fixedOn,
            FinancialConsts.MaxVatRates,
            "rates");
    }

    /// <summary>The rate valid on <paramref name="date"/>; null before the first rate.</summary>
    public VatPercentageRate? RateOn(DateOnly date) => ValidityRows.On(Rates, date);
}

/// <summary>A VAT percentage valid from its date until the next row's date (design 52); fixed once in effect.</summary>
public class VatPercentageRate : Entity<Guid>, IMultiTenant, IValidityRow
{
    public Guid? TenantId { get; private set; }

    public Guid VatPercentageId { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    /// <summary><c>numeric(5,2)</c>, 0–100.</summary>
    public decimal Percentage { get; private set; }

    protected VatPercentageRate()
    {
    }

    public VatPercentageRate(Guid id, Guid? tenantId, Guid vatPercentageId, DateOnly validFrom, decimal percentage)
        : base(id)
    {
        TenantId = tenantId;
        VatPercentageId = vatPercentageId;
        ValidFrom = validFrom;
        SetPercentage(percentage);
    }

    internal void SetPercentage(decimal percentage) => Percentage = CheckPercentage(percentage);

    internal static decimal CheckPercentage(decimal percentage)
    {
        if (percentage < 0 || percentage > FinancialConsts.MaxVatPercentage || !FinancialValues.HasScale(percentage, FinancialConsts.VatPercentageScale))
        {
            throw new BusinessException(FinancialErrorCodes.PercentageOutOfRange)
                .WithData("value", percentage)
                .WithData("max", FinancialConsts.MaxVatPercentage)
                .WithData("decimals", FinancialConsts.VatPercentageScale);
        }

        return percentage;
    }
}
