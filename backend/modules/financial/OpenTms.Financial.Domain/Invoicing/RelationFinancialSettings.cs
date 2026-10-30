using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Financial.Tariffs;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Financial.Invoicing;

/// <summary>
/// The financial defaults of one Master Data relation (design 57, assessment §2, QP5): its ordered revenue and cost price
/// lists, how several matching lists combine per side, its default discounts and surcharges with their validity, VAT,
/// payment term, fixed-price default and document flag. Created on first edit; keyed by the relation id (unique per
/// tenant, no cross-schema foreign key). Orders copies these into an order's financial terms (design 58).
/// </summary>
public class RelationFinancialSettings : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    /// <summary>A Master Data relation, checked through IMasterDataReferenceLookup.</summary>
    public Guid RelationId { get; private set; }

    /// <summary>
    /// The relation's name as Master Data gave it at the last save: a copy for sorting and searching the list in the
    /// query (review M2), like the order's customer name. Screens show the live name from the lookup; a rename in Master
    /// Data reaches this copy at the next save of the settings.
    /// </summary>
    public string RelationName { get; private set; } = string.Empty;

    public MultipleMatches RevenueMultipleMatches { get; private set; } = MultipleMatches.All;

    public MultipleMatches CostMultipleMatches { get; private set; } = MultipleMatches.All;

    /// <summary>
    /// The revenue bases charged by default (design 61 "Order page review" point 3) as a bit mask (<see cref="ChargedBases"/>);
    /// Orders copies them with the other defaults when the customer is chosen.
    /// </summary>
    public int RevenueChargedBasesMask { get; private set; }

    /// <summary>The revenue bases switched on, in the legacy order of the block.</summary>
    public IReadOnlyList<QuantitySource> RevenueChargedBases => ChargedBases.FromMask(RevenueChargedBasesMask);

    /// <summary>Null = the tenant's default VAT category.</summary>
    public Guid? VatPercentageId { get; set; }

    public int? PaymentTermDays { get; private set; }

    public bool ChargeFixedPriceByDefault { get; set; }

    public bool SendFreightDocumentsWithInvoice { get; set; }

    /// <summary>ISO 4217; null = the relation's currency from Master Data.</summary>
    public string? CurrencyCode { get; private set; }

    public virtual ICollection<RelationPriceList> PriceLists { get; private set; } = new List<RelationPriceList>();

    public virtual ICollection<RelationDiscountOrSurcharge> DiscountsOrSurcharges { get; private set; } = new List<RelationDiscountOrSurcharge>();

    protected RelationFinancialSettings()
    {
    }

    public RelationFinancialSettings(Guid id, Guid? tenantId, Guid relationId)
        : base(id)
    {
        if (relationId == Guid.Empty)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "relationId");
        }

        TenantId = tenantId;
        RelationId = relationId;
    }

    public void SetMultipleMatches(MultipleMatches revenue, MultipleMatches cost)
    {
        RevenueMultipleMatches = FinancialValues.Defined(revenue, "revenueMultipleMatches");
        CostMultipleMatches = FinancialValues.Defined(cost, "costMultipleMatches");
    }

    /// <summary>Each basis once, revenue bases only (<see cref="ChargedBases.ToMask"/>).</summary>
    public void SetRevenueChargedBases(IReadOnlyCollection<QuantitySource>? bases)
        => RevenueChargedBasesMask = ChargedBases.ToMask(bases, "revenueChargedBases");

    public MultipleMatches MultipleMatchesFor(RateSide side) => side == RateSide.Cost ? CostMultipleMatches : RevenueMultipleMatches;

    public void SetRelationName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        RelationName = trimmed.Length > FinancialConsts.MaxRelationNameLength ? trimmed[..FinancialConsts.MaxRelationNameLength] : trimmed;
    }

    public void SetPaymentTermDays(int? days)
    {
        if (days is < 0 or > 366)
        {
            throw FinancialValues.Invalid("paymentTermDays", days.Value.ToString());
        }

        PaymentTermDays = days;
    }

    public void SetCurrencyCode(string? currencyCode)
        => CurrencyCode = string.IsNullOrWhiteSpace(currencyCode) ? null : FinancialValues.IsoCode(currencyCode, ReferenceDataConsts.IsoCurrencyCodeLength, "currencyCode");

    /// <summary>Replaces the lists of one side in the given order (positions 1..n); each list once per side; at most 50.</summary>
    public void SetPriceLists(RateSide side, IReadOnlyList<Guid> priceListIds, Func<Guid> newId)
    {
        FinancialValues.Defined(side, "side");
        if (priceListIds.Count > FinancialConsts.MaxRelationPriceLists)
        {
            throw new BusinessException(ReferenceDataErrorCodes.TooManyChildren).WithData("field", "priceLists").WithData("max", FinancialConsts.MaxRelationPriceLists);
        }

        var duplicate = priceListIds.GroupBy(id => id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
        {
            throw new BusinessException(ReferenceDataErrorCodes.DuplicateKey).WithData("field", "priceListId").WithData("value", duplicate.Key.ToString());
        }

        var ofSide = PriceLists.Where(p => p.Side == side).ToList();
        foreach (var stale in ofSide.Where(p => !priceListIds.Contains(p.PriceListId)))
        {
            PriceLists.Remove(stale);
        }

        for (var i = 0; i < priceListIds.Count; i++)
        {
            if (priceListIds[i] == Guid.Empty)
            {
                throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "priceListId");
            }

            var existing = ofSide.FirstOrDefault(p => p.PriceListId == priceListIds[i]);
            if (existing == null)
            {
                PriceLists.Add(new RelationPriceList(newId(), TenantId, Id, side, priceListIds[i], i + 1));
            }
            else
            {
                existing.Position = i + 1;
            }
        }
    }

    public IReadOnlyList<Guid> PriceListIdsFor(RateSide side)
        => PriceLists.Where(p => p.Side == side).OrderBy(p => p.Position).Select(p => p.PriceListId).ToList();

    /// <summary>Replaces the default discounts and surcharges; each once; at most 100; a validity end never before its start.</summary>
    public void SetDiscountsOrSurcharges(IReadOnlyList<(Guid DiscountOrSurchargeId, DateOnly ValidFrom, DateOnly? ValidTill)> rows, Func<Guid> newId)
    {
        if (rows.Count > FinancialConsts.MaxRelationDiscountsOrSurcharges)
        {
            throw new BusinessException(ReferenceDataErrorCodes.TooManyChildren).WithData("field", "discountsOrSurcharges").WithData("max", FinancialConsts.MaxRelationDiscountsOrSurcharges);
        }

        var duplicate = rows.GroupBy(r => r.DiscountOrSurchargeId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
        {
            throw new BusinessException(ReferenceDataErrorCodes.DuplicateKey).WithData("field", "discountOrSurchargeId").WithData("value", duplicate.Key.ToString());
        }

        foreach (var row in rows)
        {
            if (row.DiscountOrSurchargeId == Guid.Empty)
            {
                throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "discountOrSurchargeId");
            }

            if (row.ValidTill != null && row.ValidTill.Value < row.ValidFrom)
            {
                throw new BusinessException(FinancialErrorCodes.InvalidValidity)
                    .WithData("validFrom", row.ValidFrom.ToString("yyyy-MM-dd"))
                    .WithData("validTill", row.ValidTill.Value.ToString("yyyy-MM-dd"));
            }

            var existing = DiscountsOrSurcharges.FirstOrDefault(d => d.DiscountOrSurchargeId == row.DiscountOrSurchargeId);
            if (existing == null)
            {
                DiscountsOrSurcharges.Add(new RelationDiscountOrSurcharge(newId(), TenantId, Id, row.DiscountOrSurchargeId, row.ValidFrom, row.ValidTill));
            }
            else
            {
                existing.SetValidity(row.ValidFrom, row.ValidTill);
            }
        }

        var keep = rows.Select(r => r.DiscountOrSurchargeId).ToHashSet();
        foreach (var stale in DiscountsOrSurcharges.Where(d => !keep.Contains(d.DiscountOrSurchargeId)).ToList())
        {
            DiscountsOrSurcharges.Remove(stale);
        }
    }

    /// <summary>The default discounts and surcharges valid on <paramref name="date"/> (scenario 55).</summary>
    public IReadOnlyList<Guid> DiscountOrSurchargeIdsOn(DateOnly date)
        => DiscountsOrSurcharges.Where(d => d.IsValidOn(date)).Select(d => d.DiscountOrSurchargeId).ToList();
}

/// <summary>A price list applied to a relation's orders (revenue) or legs (cost), in position order.</summary>
public class RelationPriceList : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid RelationFinancialSettingsId { get; private set; }

    public RateSide Side { get; private set; }

    /// <summary>FK to a price list of this module (restrict: the list cannot be deleted while referenced).</summary>
    public Guid PriceListId { get; private set; }

    public int Position { get; internal set; }

    protected RelationPriceList()
    {
    }

    internal RelationPriceList(Guid id, Guid? tenantId, Guid settingsId, RateSide side, Guid priceListId, int position)
        : base(id)
    {
        TenantId = tenantId;
        RelationFinancialSettingsId = settingsId;
        Side = side;
        PriceListId = priceListId;
        Position = position;
    }
}

/// <summary>A default discount or surcharge of a relation, proposed on orders dated inside its validity.</summary>
public class RelationDiscountOrSurcharge : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid RelationFinancialSettingsId { get; private set; }

    /// <summary>FK to a discount or surcharge of this module (restrict).</summary>
    public Guid DiscountOrSurchargeId { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    public DateOnly? ValidTill { get; private set; }

    protected RelationDiscountOrSurcharge()
    {
    }

    internal RelationDiscountOrSurcharge(Guid id, Guid? tenantId, Guid settingsId, Guid discountOrSurchargeId, DateOnly validFrom, DateOnly? validTill)
        : base(id)
    {
        TenantId = tenantId;
        RelationFinancialSettingsId = settingsId;
        DiscountOrSurchargeId = discountOrSurchargeId;
        SetValidity(validFrom, validTill);
    }

    internal void SetValidity(DateOnly validFrom, DateOnly? validTill)
    {
        ValidFrom = validFrom;
        ValidTill = validTill;
    }

    public bool IsValidOn(DateOnly date) => ValidFrom <= date && (ValidTill == null || date <= ValidTill.Value);
}
