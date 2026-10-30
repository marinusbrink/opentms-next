using System;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Financial.Tariffs;

/// <summary>Where a rate line hangs (AD-08, AP-07): exactly one of the five anchors.</summary>
public enum RateLineAnchorKind
{
    Order = 1,
    OrderLine = 2,
    Leg = 3,
    Stop = 4,
    Trip = 5
}

/// <summary>
/// The anchor of a rate line: the anchored record and the root it belongs to, so "all lines of order X" and "all lines of
/// trip Y" are one indexed read (<c>AnchorOrderId</c>, <c>AnchorTripId</c>). An order-side anchor always names its order;
/// a trip-side anchor always names its trip.
/// </summary>
public readonly record struct RateLineAnchor(RateLineAnchorKind Kind, Guid Id, Guid? OrderId, Guid? TripId)
{
    public static RateLineAnchor Order(Guid orderId) => new(RateLineAnchorKind.Order, orderId, orderId, null);

    public static RateLineAnchor OrderLine(Guid orderId, Guid lineId) => new(RateLineAnchorKind.OrderLine, lineId, orderId, null);

    public static RateLineAnchor Stop(Guid orderId, Guid stopId) => new(RateLineAnchorKind.Stop, stopId, orderId, null);

    public static RateLineAnchor Trip(Guid tripId) => new(RateLineAnchorKind.Trip, tripId, null, tripId);

    public static RateLineAnchor Leg(Guid tripId, Guid legId) => new(RateLineAnchorKind.Leg, legId, null, tripId);
}

/// <summary>
/// A stored money line of an order, order line, stop, leg or trip (design 57 "RateLine", AD-08): what was charged or
/// paid, how it was calculated (quantity × unit price, rounded to 2 at the line, QP4) and where it came from (the rule,
/// bracket, discount value row and VAT rate row — provenance, never recomputed on read, AD-07). One table for both sides
/// (AP-07). Money is <c>decimal</c>: amounts <c>numeric(18,2)</c>, quantities and unit prices <c>numeric(18,4)</c>.
/// </summary>
public class RateLine : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public RateSide Side { get; private set; }

    /* Exactly one anchor (check constraint); ids without foreign keys across modules (AD-20). */
    public Guid? OrderId { get; private set; }

    public Guid? OrderLineId { get; private set; }

    public Guid? LegId { get; private set; }

    public Guid? StopId { get; private set; }

    public Guid? TripId { get; private set; }

    /// <summary>The order every order-side line belongs to (set for order, order-line and stop anchors).</summary>
    public Guid? AnchorOrderId { get; private set; }

    /// <summary>The trip every trip-side line belongs to (set for trip and leg anchors).</summary>
    public Guid? AnchorTripId { get; private set; }

    public RateLineKind Kind { get; private set; }

    public int Position { get; set; }

    /// <summary>A localisation key for a calculated line ("Financial:RateLine.PriceList").</summary>
    public string? DescriptionKey { get; private set; }

    /// <summary>Free text of a manual line.</summary>
    public string? Description { get; private set; }

    /// <summary><c>numeric(18,4)</c>.</summary>
    public decimal Quantity { get; private set; }

    public QuantitySource QuantitySource { get; private set; }

    /// <summary><c>numeric(18,4)</c>.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary><c>numeric(18,2)</c>: <c>round(Quantity × UnitPrice, 2)</c>, clamped to the rule's min/max for a list line.</summary>
    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; } = "EUR";

    /// <summary>The line is the total of its anchor; the lines beneath stay as the breakdown (a fixed price, an agreed carrier price).</summary>
    public bool OverridesChildren { get; private set; }

    /// <summary>Typed by a user; re-valued from the current quantity on every calculation, never replaced (QP6).</summary>
    public bool IsManual { get; private set; }

    /// <summary>A calculated line whose unit price a user overrode; it keeps that unit price on recalculation (QP6).</summary>
    public bool IsAdjusted { get; private set; }

    /// <summary>A discount or surcharge of the second pass (over the lines plus the first pass).</summary>
    public bool AppliesAfterOthers { get; private set; }

    /* Provenance (design 57, assessment §4.4): the rows that produced the line, by id. */
    public Guid? PriceListId { get; private set; }

    public Guid? PriceListRuleId { get; private set; }

    public Guid? PriceListBracketId { get; private set; }

    public int? ColumnPosition { get; private set; }

    public Guid? DiscountOrSurchargeId { get; private set; }

    public Guid? DiscountOrSurchargeValueId { get; private set; }

    public Guid? AdditionalServiceId { get; private set; }

    public Guid? VatPercentageId { get; private set; }

    public Guid? VatPercentageRateId { get; private set; }

    public DateTimeOffset? CalculatedAt { get; private set; }

    public long? CalculationVersion { get; private set; }

    protected RateLine()
    {
    }

    private RateLine(Guid id, Guid? tenantId, RateSide side, RateLineAnchor anchor, RateLineKind kind, string currencyCode)
        : base(id)
    {
        TenantId = tenantId;
        Side = FinancialValues.Defined(side, "side");
        Kind = FinancialValues.Defined(kind, "kind");
        CurrencyCode = FinancialValues.IsoCode(currencyCode, ReferenceDataConsts.IsoCurrencyCodeLength, "currencyCode");
        SetAnchor(anchor);
    }

    /// <summary>A line the resolver produced: a price-list, discount/surcharge or own-fleet line with its provenance.</summary>
    public static RateLine Calculated(Guid id, Guid? tenantId, RateSide side, RateLineAnchor anchor, RateLineKind kind, string currencyCode, string descriptionKey, decimal quantity, QuantitySource quantitySource, decimal unitPrice, decimal amount)
    {
        if (kind is RateLineKind.Manual or RateLineKind.AdditionalService or RateLineKind.FixedPrice or RateLineKind.AgreedPrice)
        {
            throw FinancialValues.Invalid("kind", ((int)kind).ToString());
        }

        var line = new RateLine(id, tenantId, side, anchor, kind, currencyCode)
        {
            DescriptionKey = Check.Length(descriptionKey, nameof(descriptionKey), FinancialConsts.MaxDescriptionKeyLength),
            QuantitySource = FinancialValues.Defined(quantitySource, "quantitySource")
        };
        line.SetValues(quantity, unitPrice, amount);
        return line;
    }

    /// <summary>A line a user typed (QP6): a free line or an additional service with its quantity in the service's unit.</summary>
    public static RateLine Manual(Guid id, Guid? tenantId, RateSide side, RateLineAnchor anchor, RateLineKind kind, string currencyCode, string? description, decimal quantity, QuantitySource quantitySource, decimal unitPrice, Guid? additionalServiceId)
    {
        if (kind is not (RateLineKind.Manual or RateLineKind.AdditionalService))
        {
            throw FinancialValues.Invalid("kind", ((int)kind).ToString());
        }

        var line = new RateLine(id, tenantId, side, anchor, kind, currencyCode) { IsManual = true, AdditionalServiceId = additionalServiceId };
        line.SetManual(description, quantity, quantitySource, unitPrice);
        return line;
    }

    /// <summary>The one line that is the total of its anchor (AD-08): a fixed order price or an agreed carrier price.</summary>
    public static RateLine Overriding(Guid id, Guid? tenantId, RateSide side, RateLineAnchor anchor, RateLineKind kind, string currencyCode, decimal amount)
    {
        if (kind is not (RateLineKind.FixedPrice or RateLineKind.AgreedPrice))
        {
            throw FinancialValues.Invalid("kind", ((int)kind).ToString());
        }

        var line = new RateLine(id, tenantId, side, anchor, kind, currencyCode)
        {
            OverridesChildren = true,
            DescriptionKey = "Financial:RateLine." + kind,
            QuantitySource = QuantitySource.Manual
        };
        line.SetAmount(amount);
        return line;
    }

    public RateLineAnchor Anchor => OrderLineId != null ? RateLineAnchor.OrderLine(AnchorOrderId!.Value, OrderLineId.Value)
        : StopId != null ? RateLineAnchor.Stop(AnchorOrderId!.Value, StopId.Value)
        : OrderId != null ? RateLineAnchor.Order(OrderId.Value)
        : LegId != null ? RateLineAnchor.Leg(AnchorTripId!.Value, LegId.Value)
        : RateLineAnchor.Trip(TripId!.Value);

    private void SetAnchor(RateLineAnchor anchor)
    {
        if (anchor.Id == Guid.Empty)
        {
            throw new BusinessException(FinancialErrorCodes.AnchorRequired);
        }

        OrderId = anchor.Kind == RateLineAnchorKind.Order ? anchor.Id : null;
        OrderLineId = anchor.Kind == RateLineAnchorKind.OrderLine ? anchor.Id : null;
        StopId = anchor.Kind == RateLineAnchorKind.Stop ? anchor.Id : null;
        LegId = anchor.Kind == RateLineAnchorKind.Leg ? anchor.Id : null;
        TripId = anchor.Kind == RateLineAnchorKind.Trip ? anchor.Id : null;
        AnchorOrderId = anchor.OrderId;
        AnchorTripId = anchor.TripId;

        if ((AnchorOrderId == null) == (AnchorTripId == null) || AnchorOrderId == Guid.Empty || AnchorTripId == Guid.Empty)
        {
            throw new BusinessException(FinancialErrorCodes.AnchorRequired);
        }
    }

    /// <summary>Sets the fields a user may change on a manual line; the amount follows.</summary>
    public void SetManual(string? description, decimal quantity, QuantitySource quantitySource, decimal unitPrice)
    {
        if (!IsManual)
        {
            throw new BusinessException(FinancialErrorCodes.CalculatedLineReadOnly);
        }

        var text = description?.Trim();
        Description = string.IsNullOrEmpty(text) ? null : Check.Length(text, nameof(description), FinancialConsts.MaxRateLineDescriptionLength);
        QuantitySource = FinancialValues.Defined(quantitySource, "quantitySource");
        SetValues(quantity, unitPrice, Round2(quantity * unitPrice));
    }

    /// <summary>A user overrides the unit price of a calculated line (QP6); it keeps that price on recalculation.</summary>
    public void AdjustUnitPrice(decimal unitPrice)
    {
        if (IsManual || Kind != RateLineKind.PriceList)
        {
            throw new BusinessException(FinancialErrorCodes.CalculatedLineReadOnly);
        }

        IsAdjusted = true;
        SetValues(Quantity, unitPrice, Round2(Quantity * unitPrice));
    }

    /// <summary>A manual or adjusted line re-valued from the current quantity of its anchor (QP6, scenarios 29–30).</summary>
    public void Revalue(decimal quantity, DateTimeOffset calculatedAt, long version)
    {
        SetValues(quantity, UnitPrice, Round2(quantity * UnitPrice));
        CalculatedAt = calculatedAt;
        CalculationVersion = version;
    }

    /// <summary>The amount of an overriding line.</summary>
    public void SetAmount(decimal amount)
    {
        if (!OverridesChildren)
        {
            throw new BusinessException(FinancialErrorCodes.CalculatedLineReadOnly);
        }

        SetValues(1m, amount, amount);
    }

    public void SetProvenance(Guid? priceListId, Guid? priceListRuleId, Guid? priceListBracketId, int? columnPosition)
    {
        PriceListId = priceListId;
        PriceListRuleId = priceListRuleId;
        PriceListBracketId = priceListBracketId;
        ColumnPosition = columnPosition;
    }

    public void SetDiscountOrSurcharge(Guid discountOrSurchargeId, Guid? valueId, bool appliesAfterOthers)
    {
        DiscountOrSurchargeId = discountOrSurchargeId;
        DiscountOrSurchargeValueId = valueId;
        AppliesAfterOthers = appliesAfterOthers;
    }

    public void SetVat(Guid? vatPercentageId, Guid? vatPercentageRateId)
    {
        VatPercentageId = vatPercentageId;
        VatPercentageRateId = vatPercentageRateId;
    }

    public void SetCalculation(DateTimeOffset calculatedAt, long version)
    {
        CalculatedAt = calculatedAt;
        CalculationVersion = version;
    }

    private void SetValues(decimal quantity, decimal unitPrice, decimal amount)
    {
        if (!FinancialValues.HasScale(quantity, FinancialConsts.ValueScale))
        {
            throw FinancialValues.Invalid("quantity", quantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (!FinancialValues.HasScale(unitPrice, FinancialConsts.ValueScale))
        {
            throw FinancialValues.Invalid("unitPrice", unitPrice.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (!FinancialValues.HasScale(amount, FinancialConsts.AmountScale))
        {
            throw FinancialValues.Invalid("amount", amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Quantity = quantity;
        UnitPrice = unitPrice;
        Amount = amount;
    }

    /// <summary>Money rounding of this module: half away from zero to 2 decimals (QP4), as an invoice line.</summary>
    public static decimal Round2(decimal value) => decimal.Round(value, FinancialConsts.AmountScale, MidpointRounding.AwayFromZero);

    public static decimal Round4(decimal value) => decimal.Round(value, FinancialConsts.ValueScale, MidpointRounding.AwayFromZero);
}
