using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Tariffs;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Xunit;
using static OpenTms.Financial.PricingTestData;

namespace OpenTms.Financial;

/// <summary>
/// The entity rules of design 57: columns and brackets strictly ascending with one open bound last, the lane rules of a
/// rule, the bounds, the anchor of a rate line, the AD-08 totals over a tree. Risk class: Critical (money model).
/// </summary>
public class PriceListEntityRuleTests
{
    [Fact]
    public void Columns_must_rise_strictly_and_end_with_the_open_column()
    {
        var list = List();

        Should.Throw<BusinessException>(() => list.SetColumns([100m, 100m, null])).Code.ShouldBe(FinancialErrorCodes.ColumnsNotAscending);
        Should.Throw<BusinessException>(() => list.SetColumns([100m, 500m])).Code.ShouldBe(FinancialErrorCodes.OpenRowRequired);
        Should.Throw<BusinessException>(() => list.SetColumns([null, 500m, null])).Code.ShouldBe(FinancialErrorCodes.OpenRowRequired);
        Should.Throw<BusinessException>(() => list.SetColumns(Enumerable.Repeat<decimal?>(1m, 51).Select((v, i) => (decimal?)(v * (i + 1))).ToList())).Code.ShouldBe(ReferenceDataErrorCodes.TooManyChildren);

        list.SetColumns([100m, 500m, null]);
        list.Columns.OrderBy(c => c.Position).Select(c => c.UpperBound).ShouldBe([100m, 500m, null]);
        list.ColumnPositionOf(0m).ShouldBe(1);
        list.ColumnPositionOf(100m).ShouldBe(1);
        list.ColumnPositionOf(100.0001m).ShouldBe(2);
        list.ColumnPositionOf(99999m).ShouldBe(3);

        /* Replacing by position keeps the rows and trims the rest. */
        list.SetColumns([200m, null]);
        list.Columns.Count.ShouldBe(2);
        list.Columns.Single(c => c.Position == 1).UpperBound.ShouldBe(200m);
    }

    [Fact]
    public void A_list_validates_its_header_values()
    {
        var list = List();

        Should.Throw<BusinessException>(() => list.SetBasis(QuantitySource.Manual)).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        Should.Throw<BusinessException>(() => list.SetUnitConversionFactor(0m)).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        Should.Throw<BusinessException>(() => list.SetCurrencyCode("EURO")).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        Should.Throw<BusinessException>(() => list.SetValidity(Monday, Monday.AddDays(-1))).Code.ShouldBe(FinancialErrorCodes.InvalidValidity);

        list.SetValidity(Monday, Monday);
        list.IsValidOn(Monday).ShouldBeTrue();
        list.IsValidOn(Monday.AddDays(1)).ShouldBeFalse();
        list.IsValidOn(Monday.AddDays(-1)).ShouldBeFalse();
        list.SetValidity(Monday, null);
        list.IsValidOn(Monday.AddYears(10)).ShouldBeTrue();
    }

    [Fact]
    public void A_rule_needs_a_lane_with_a_relation_or_a_region_per_side_never_both()
    {
        var rule = new PriceListRule(Guid.NewGuid(), TenantId, Guid.NewGuid(), 1);

        Should.Throw<BusinessException>(() => rule.SetLane(null, null, null, null)).Code.ShouldBe(FinancialErrorCodes.LaneRequired);
        Should.Throw<BusinessException>(() => rule.SetLane(Ams, NlWest, null, null)).Code.ShouldBe(FinancialErrorCodes.RelationAndRegionOnOneSide);
        Should.Throw<BusinessException>(() => rule.SetLane(null, null, Bru, Be)).Code.ShouldBe(FinancialErrorCodes.RelationAndRegionOnOneSide);
        Should.Throw<BusinessException>(() => rule.SetLane(Guid.Empty, null, null, null)).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);

        rule.SetLane(Ams, null, null, Be);
        rule.HasFromSide.ShouldBeTrue();
        rule.HasToSide.ShouldBeTrue();
        rule.Tier.ShouldBe(0);
        rule.SetDimensions(Pallet, null, Truck);
        rule.Tier.ShouldBe(2);
    }

    [Fact]
    public void Brackets_must_rise_strictly_with_exactly_one_open_row_last_and_one_cell_per_column()
    {
        var list = List(columns: [100m, null]);
        var rule = new PriceListRule(Guid.NewGuid(), TenantId, list.Id, 1);
        rule.SetLane(Ams, null, Bru, null);

        Should.Throw<BusinessException>(() => rule.SetBrackets([], 2, Guid.NewGuid)).Code.ShouldBe(FinancialErrorCodes.OpenRowRequired);
        Should.Throw<BusinessException>(() => rule.SetBrackets([(50m, new List<decimal?> { 1m, 2m })], 2, Guid.NewGuid)).Code.ShouldBe(FinancialErrorCodes.OpenRowRequired);
        Should.Throw<BusinessException>(() => rule.SetBrackets([(50m, new List<decimal?> { 1m, 2m }), (50m, new List<decimal?> { 1m, 2m }), (null, new List<decimal?> { 1m, 2m })], 2, Guid.NewGuid)).Code.ShouldBe(FinancialErrorCodes.BracketsNotAscending);
        Should.Throw<BusinessException>(() => rule.SetBrackets([(null, new List<decimal?> { 1m })], 2, Guid.NewGuid)).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        Should.Throw<BusinessException>(() => rule.SetBrackets([(null, new List<decimal?> { 1m, 2.12345m })], 2, Guid.NewGuid)).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        Should.Throw<BusinessException>(() => rule.SetBrackets(Enumerable.Range(1, 31).Select(i => ((decimal?)(i == 31 ? null : i), (IReadOnlyList<decimal?>)new List<decimal?> { 1m, 2m })).ToList(), 2, Guid.NewGuid)).Code.ShouldBe(ReferenceDataErrorCodes.TooManyChildren);

        rule.SetBrackets([(50m, new List<decimal?> { 1m, null }), (null, new List<decimal?> { 3m, 4m })], 2, Guid.NewGuid);
        var first = rule.Brackets.Single(b => b.Position == 1);
        first.UnitPriceAt(1).ShouldBe(1m);
        first.UnitPriceAt(2).ShouldBeNull();
        rule.BracketOf(50m).ShouldBe(first);
        rule.BracketOf(50.5m).ShouldBe(rule.OpenBracket);

        /* Positions are stable across a replace: the first bracket keeps its id, the ladder shrinks. */
        var firstId = first.Id;
        rule.SetBrackets([(null, new List<decimal?> { 9m, 9m })], 2, Guid.NewGuid);
        rule.Brackets.Single().Id.ShouldBe(firstId);
        rule.Brackets.Single().UnitPriceAt(2).ShouldBe(9m);
    }

    [Fact]
    public void Min_and_max_must_be_non_negative_two_decimal_amounts_in_order()
    {
        var rule = new PriceListRule(Guid.NewGuid(), TenantId, Guid.NewGuid(), 1);

        Should.Throw<BusinessException>(() => rule.SetMinMax(-1m, null)).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        Should.Throw<BusinessException>(() => rule.SetMinMax(1.001m, null)).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        Should.Throw<BusinessException>(() => rule.SetMinMax(10m, 5m)).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        rule.SetMinMax(5m, 10m);
        (rule.MinAmount, rule.MaxAmount).ShouldBe((5m, 10m));
    }

    [Fact]
    public void A_rate_line_hangs_on_exactly_one_anchor_and_knows_its_root()
    {
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var legId = Guid.NewGuid();

        var onLine = RateLine.Manual(Guid.NewGuid(), TenantId, RateSide.Revenue, RateLineAnchor.OrderLine(orderId, lineId), RateLineKind.Manual, "EUR", "x", 1m, QuantitySource.Manual, 1m, null);
        (onLine.OrderId, onLine.OrderLineId, onLine.AnchorOrderId, onLine.AnchorTripId).ShouldBe((null, lineId, orderId, null));
        onLine.Anchor.ShouldBe(RateLineAnchor.OrderLine(orderId, lineId));

        var onLeg = RateLine.Manual(Guid.NewGuid(), TenantId, RateSide.Cost, RateLineAnchor.Leg(tripId, legId), RateLineKind.Manual, "EUR", "x", 1m, QuantitySource.Manual, 1m, null);
        (onLeg.LegId, onLeg.TripId, onLeg.AnchorTripId, onLeg.AnchorOrderId).ShouldBe((legId, null, tripId, null));

        Should.Throw<BusinessException>(() => RateLine.Manual(Guid.NewGuid(), TenantId, RateSide.Revenue, new RateLineAnchor(RateLineAnchorKind.Order, Guid.Empty, Guid.Empty, null), RateLineKind.Manual, "EUR", "x", 1m, QuantitySource.Manual, 1m, null))
            .Code.ShouldBe(FinancialErrorCodes.AnchorRequired);
        Should.Throw<BusinessException>(() => RateLine.Manual(Guid.NewGuid(), TenantId, RateSide.Revenue, new RateLineAnchor(RateLineAnchorKind.Order, orderId, orderId, tripId), RateLineKind.Manual, "EUR", "x", 1m, QuantitySource.Manual, 1m, null))
            .Code.ShouldBe(FinancialErrorCodes.AnchorRequired);
    }

    [Fact]
    public void The_flags_of_a_rate_line_are_set_by_its_commands_never_freely()
    {
        var orderId = Guid.NewGuid();
        var manual = RateLine.Manual(Guid.NewGuid(), TenantId, RateSide.Revenue, RateLineAnchor.Order(orderId), RateLineKind.Manual, "EUR", "x", 2m, QuantitySource.Manual, 10.5m, null);
        manual.IsManual.ShouldBeTrue();
        manual.Amount.ShouldBe(21m);
        Should.Throw<BusinessException>(() => manual.AdjustUnitPrice(5m)).Code.ShouldBe(FinancialErrorCodes.CalculatedLineReadOnly);

        var calculated = RateLine.Calculated(Guid.NewGuid(), TenantId, RateSide.Revenue, RateLineAnchor.Order(orderId), RateLineKind.PriceList, "EUR", "Financial:RateLine.PriceList", 3m, QuantitySource.LineQuantity, 10m, 30m);
        Should.Throw<BusinessException>(() => calculated.SetManual("x", 1m, QuantitySource.Manual, 1m)).Code.ShouldBe(FinancialErrorCodes.CalculatedLineReadOnly);
        calculated.AdjustUnitPrice(9m);
        (calculated.IsAdjusted, calculated.Amount).ShouldBe((true, 27m));

        Should.Throw<BusinessException>(() => RateLine.Calculated(Guid.NewGuid(), TenantId, RateSide.Revenue, RateLineAnchor.Order(orderId), RateLineKind.Manual, "EUR", "k", 1m, QuantitySource.Manual, 1m, 1m)).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        Should.Throw<BusinessException>(() => RateLine.Calculated(Guid.NewGuid(), TenantId, RateSide.Revenue, RateLineAnchor.Order(orderId), RateLineKind.PriceList, "EUR", "k", 1m, QuantitySource.Manual, 1m, 1.001m)).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);

        var fixedPrice = RateLine.Overriding(Guid.NewGuid(), TenantId, RateSide.Revenue, RateLineAnchor.Order(orderId), RateLineKind.FixedPrice, "EUR", 800m);
        (fixedPrice.OverridesChildren, fixedPrice.Amount, fixedPrice.DescriptionKey).ShouldBe((true, 800m, "Financial:RateLine.FixedPrice"));
        Should.Throw<BusinessException>(() => RateLine.Overriding(Guid.NewGuid(), TenantId, RateSide.Revenue, RateLineAnchor.Order(orderId), RateLineKind.PriceList, "EUR", 1m)).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
    }

    [Fact]
    public void Totals_follow_AD_08_the_overriding_line_else_the_sum_of_the_tree()
    {
        var tripId = Guid.NewGuid();
        var leg1 = Guid.NewGuid();
        var leg2 = Guid.NewGuid();
        var lines = new List<RateLine>
        {
            RateLine.Calculated(Guid.NewGuid(), TenantId, RateSide.Cost, RateLineAnchor.Leg(tripId, leg1), RateLineKind.PriceList, "EUR", "k", 1m, QuantitySource.LineQuantity, 100m, 100m),
            RateLine.Calculated(Guid.NewGuid(), TenantId, RateSide.Cost, RateLineAnchor.Leg(tripId, leg2), RateLineKind.PriceList, "EUR", "k", 1m, QuantitySource.LineQuantity, 60m, 60m),
            RateLine.Calculated(Guid.NewGuid(), TenantId, RateSide.Cost, RateLineAnchor.Trip(tripId), RateLineKind.OwnFleetDistance, "EUR", "k", 200m, QuantitySource.TripDistanceKm, 1.4m, 280m)
        };

        var totals = RateLineTotals.Of(tripId, lines);
        totals.TotalAmount.ShouldBe(440m);
        totals.LinesAmount.ShouldBe(440m);
        totals.ByChild.ShouldBe(new Dictionary<Guid, decimal> { [leg1] = 100m, [leg2] = 60m });
        totals.HasOverridingLine.ShouldBeFalse();
        totals.CalculationVersion.ShouldBeNull();

        lines.Add(RateLine.Overriding(Guid.NewGuid(), TenantId, RateSide.Cost, RateLineAnchor.Trip(tripId), RateLineKind.AgreedPrice, "EUR", 500m));
        var agreed = RateLineTotals.Of(tripId, lines);
        agreed.TotalAmount.ShouldBe(500m);
        agreed.HasOverridingLine.ShouldBeTrue();
        agreed.ByChild[leg1].ShouldBe(100m, "the breakdown stays");

        RateLineTotals.Of(Guid.NewGuid(), []).TotalAmount.ShouldBe(0m);
    }

    [Fact]
    public void Relation_settings_keep_one_list_per_side_in_order_and_refuse_duplicates_and_too_many()
    {
        var settings = new RelationFinancialSettings(Guid.NewGuid(), TenantId, Guid.NewGuid());
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        settings.SetPriceLists(RateSide.Revenue, [a, b], Guid.NewGuid);
        settings.SetPriceLists(RateSide.Cost, [b], Guid.NewGuid);
        settings.PriceListIdsFor(RateSide.Revenue).ShouldBe([a, b]);
        settings.PriceListIdsFor(RateSide.Cost).ShouldBe([b]);

        settings.SetPriceLists(RateSide.Revenue, [b, a], Guid.NewGuid);
        settings.PriceListIdsFor(RateSide.Revenue).ShouldBe([b, a]);
        settings.PriceLists.Count.ShouldBe(3);

        Should.Throw<BusinessException>(() => settings.SetPriceLists(RateSide.Revenue, [a, a], Guid.NewGuid)).Code.ShouldBe(ReferenceDataErrorCodes.DuplicateKey);
        Should.Throw<BusinessException>(() => settings.SetPriceLists(RateSide.Revenue, Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToList(), Guid.NewGuid)).Code.ShouldBe(ReferenceDataErrorCodes.TooManyChildren);
        Should.Throw<BusinessException>(() => settings.SetPaymentTermDays(400)).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        Should.Throw<BusinessException>(() => settings.SetDiscountsOrSurcharges([(a, Monday, Monday.AddDays(-1))], Guid.NewGuid)).Code.ShouldBe(FinancialErrorCodes.InvalidValidity);
        Should.Throw<BusinessException>(() => new RelationFinancialSettings(Guid.NewGuid(), TenantId, Guid.Empty)).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
    }

    [Fact]
    public void A_discount_or_surcharge_applies_on_its_weekdays_and_sides_and_bounds_its_kilometre_rule()
    {
        var item = Surcharge("SAT", DiscountOrSurchargeValueKind.Percentage, 10m, side: DiscountOrSurchargeSide.Revenue, days: DaysOfWeek.Saturday | DaysOfWeek.Sunday);

        item.AppliesOn(Saturday).ShouldBeTrue();
        item.AppliesOn(Monday).ShouldBeFalse();
        item.AppliesTo(RateSide.Revenue).ShouldBeTrue();
        item.AppliesTo(RateSide.Cost).ShouldBeFalse();
        Should.Throw<BusinessException>(() => item.SetKilometreRule(KilometreBasis.Distance, 100.5m, false)).Code.ShouldBe(FinancialErrorCodes.PercentageOutOfRange);
        Should.Throw<BusinessException>(() => item.SetKilometreRule(KilometreBasis.Distance, 12.345m, false)).Code.ShouldBe(FinancialErrorCodes.PercentageOutOfRange);
        item.SetKilometreRule(KilometreBasis.TollDistance, 80m, true);
        (item.KilometreBasis, item.BillableKilometrePercentage, item.RoundTrip).ShouldBe((KilometreBasis.TollDistance, 80m, true));
    }
}
