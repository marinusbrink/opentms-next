using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Tariffs;
using OpenTms.Financial.Tariffs.Pricing;

namespace OpenTms.Financial;

/// <summary>
/// Builders for the resolver scenarios (design 57, pricing.md §10): Master Data ids, lists with rules and ladders, lines
/// with stops, discounts with value rows. Deterministic ids where a test compares them.
/// </summary>
public static class PricingTestData
{
    public static readonly Guid TenantId = Guid.NewGuid();

    /* Relations and regions. */
    public static readonly Guid Ams = Guid.NewGuid();
    public static readonly Guid Rtm = Guid.NewGuid();
    public static readonly Guid Bru = Guid.NewGuid();
    public static readonly Guid Ant = Guid.NewGuid();
    public static readonly Guid NlWest = Guid.NewGuid();
    public static readonly Guid Be = Guid.NewGuid();

    /* Dimensions. */
    public static readonly Guid Pallet = Guid.NewGuid();
    public static readonly Guid Roll = Guid.NewGuid();
    public static readonly Guid Flowers = Guid.NewGuid();
    public static readonly Guid Truck = Guid.NewGuid();

    /// <summary>Monday 2026-10-26.</summary>
    public static readonly DateOnly Monday = new(2026, 10, 26);

    public static readonly DateOnly Saturday = new(2026, 10, 31);

    public static readonly Guid OrderId = Guid.NewGuid();

    public static PriceList List(
        string code = "LIST",
        RateSide side = RateSide.Revenue,
        QuantitySource basis = QuantitySource.LineQuantity,
        PriceMultiplier multiplier = PriceMultiplier.PerUnit,
        ColumnQuantitySource columnQuantity = ColumnQuantitySource.LineBasis,
        StopMatching stopMatching = StopMatching.Both,
        decimal factor = 1m,
        string currency = "EUR",
        DateOnly? validFrom = null,
        DateOnly? validTill = null,
        params decimal?[] columns)
    {
        var list = new PriceList(Guid.NewGuid(), TenantId, code, code, side, currency, basis, validFrom ?? new DateOnly(2026, 1, 1));
        list.SetMultiplier(multiplier);
        list.SetColumnQuantity(columnQuantity);
        list.SetStopMatching(stopMatching);
        list.SetUnitConversionFactor(factor);
        list.SetValidity(validFrom ?? new DateOnly(2026, 1, 1), validTill);
        if (columns.Length > 0)
        {
            list.SetColumns(columns);
        }

        return list;
    }

    /// <summary>A rule with one open row and the given prices per column.</summary>
    public static PriceListRule Rule(
        PriceList list,
        int position,
        Guid? fromRelation = null, Guid? fromRegion = null, Guid? toRelation = null, Guid? toRegion = null,
        Guid? unit = null, Guid? product = null, Guid? vehicleType = null,
        decimal? min = null, decimal? max = null,
        params decimal?[] prices)
        => Rule(list, position, fromRelation, fromRegion, toRelation, toRegion, unit, product, vehicleType, QuantitySource.Manual, min, max, (null, prices.Length == 0 ? [10m] : prices));

    /// <summary>A rule with a bracket ladder.</summary>
    public static PriceListRule Rule(
        PriceList list,
        int position,
        Guid? fromRelation, Guid? fromRegion, Guid? toRelation, Guid? toRegion,
        Guid? unit, Guid? product, Guid? vehicleType,
        QuantitySource rowQuantity,
        decimal? min, decimal? max,
        params (decimal? UpperBound, decimal?[] Prices)[] brackets)
    {
        var rule = new PriceListRule(Guid.NewGuid(), TenantId, list.Id, position);
        rule.SetLane(fromRelation, fromRegion, toRelation, toRegion);
        rule.SetDimensions(unit, product, vehicleType);
        rule.SetRowQuantity(rowQuantity);
        rule.SetMinMax(min, max);
        rule.SetBrackets(brackets.Select(b => (b.UpperBound, (IReadOnlyList<decimal?>)b.Prices.ToList())).ToList(), list.Columns.Count, Guid.NewGuid);
        return rule;
    }

    public static PricingLine Line(
        int position = 1,
        Guid? anchorId = null,
        Guid? from = null, Guid? to = null,
        Guid[]? fromRegions = null, Guid[]? toRegions = null,
        decimal quantity = 1m,
        decimal? weightKg = null,
        decimal? distanceKm = null,
        decimal? tollKm = null,
        decimal? durationH = null,
        decimal? volumeM3 = null,
        Guid? unit = null, Guid? product = null, Guid? vehicleType = null,
        DateOnly? asOf = null)
        => new()
        {
            AnchorId = anchorId ?? Guid.NewGuid(),
            Position = position,
            TransportUnitId = unit ?? Pallet,
            ProductId = product,
            VehicleTypeId = vehicleType,
            Quantity = quantity,
            WeightKg = weightKg,
            DistanceKm = distanceKm,
            TollDistanceKm = tollKm,
            DurationH = durationH,
            VolumeM3 = volumeM3,
            First = new PricingStop { RelationId = from ?? Ams, RegionIds = fromRegions ?? [NlWest], Date = asOf ?? Monday },
            Last = new PricingStop { RelationId = to ?? Bru, RegionIds = toRegions ?? [Be], Date = asOf ?? Monday },
            AsOf = asOf ?? Monday
        };

    public static DiscountOrSurcharge Surcharge(
        string code,
        DiscountOrSurchargeValueKind kind,
        decimal value,
        DiscountOrSurchargeDirection direction = DiscountOrSurchargeDirection.Surcharge,
        DiscountOrSurchargeSide side = DiscountOrSurchargeSide.Both,
        DaysOfWeek days = DaysOfWeek.All,
        bool afterOthers = false,
        KilometreBasis kilometres = KilometreBasis.Distance,
        decimal billable = 100m,
        bool roundTrip = false,
        string? currency = null,
        DateOnly? validFrom = null,
        params (Guid RegionId, RegionAppliesAt AppliesAt)[] regions)
    {
        var item = new DiscountOrSurcharge(Guid.NewGuid(), TenantId, code, code, direction, kind, side) { AppliesAfterOthers = afterOthers };
        item.SetDaysOfWeek(days);
        item.SetKilometreRule(kilometres, billable, roundTrip);
        item.SetValues([(validFrom ?? new DateOnly(2020, 1, 1), value, kind == DiscountOrSurchargeValueKind.Percentage ? null : currency ?? "EUR")], fixedOn: null, Guid.NewGuid);
        if (regions.Length > 0)
        {
            item.SetRegions(regions.Select(r => (r.RegionId, r.AppliesAt)), Guid.NewGuid);
        }

        return item;
    }

    public static PricingRequest Request(
        IReadOnlyList<PricingLine> lines,
        IReadOnlyList<PriceListWithRules>? lists = null,
        IReadOnlyList<DiscountOrSurcharge>? discounts = null,
        MultipleMatches multipleMatches = MultipleMatches.All,
        IReadOnlyList<RateLine>? kept = null,
        decimal? fixedPrice = null,
        string currency = "EUR",
        RateSide side = RateSide.Revenue,
        DateOnly? asOf = null,
        VatPercentage? vat = null,
        Guid? rootId = null)
    {
        var root = rootId ?? OrderId;
        return new PricingRequest
        {
            Side = side,
            CurrencyCode = currency,
            AsOf = asOf ?? (lines.Count == 0 ? Monday : lines.Min(l => l.AsOf)),
            Lines = lines,
            PriceLists = lists ?? [],
            MultipleMatches = multipleMatches,
            DiscountsOrSurcharges = discounts ?? [],
            Vat = vat,
            FixedPriceAmount = fixedPrice,
            KeptLines = kept ?? [],
            RootAnchor = side == RateSide.Revenue ? RateLineAnchor.Order(root) : RateLineAnchor.Trip(root),
            LineAnchor = id => side == RateSide.Revenue ? RateLineAnchor.OrderLine(root, id) : RateLineAnchor.Leg(root, id),
            TenantId = TenantId,
            NewId = Guid.NewGuid,
            Now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero),
            Version = 1
        };
    }

    public static PriceListWithRules WithRules(PriceList list, params PriceListRule[] rules) => new(list, rules);
}
