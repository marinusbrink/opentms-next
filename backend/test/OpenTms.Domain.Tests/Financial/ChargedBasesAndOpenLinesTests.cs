using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Tariffs;
using OpenTms.Financial.Tariffs.Pricing;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Xunit;
using static OpenTms.Financial.PricingTestData;

namespace OpenTms.Financial;

/// <summary>
/// Design 61 "Order page review" point 3 with build notes 1, 2 and 5 over the pure parts: the charged-bases mask (the
/// thirteen revenue bases in the legacy order, <c>Manual</c> and the cost-only <c>TripDistanceKm</c> refused, a repeat
/// refused), the relation settings' setter, and the resolver's open lines ("To be priced"): one per order line for every
/// charged basis no list of the order prices by (whatever its validity), absent under a fixed price and on the cost side,
/// never pruned by multiple matches, counting as applied for the per-kilometre surcharges; an adjusted open line kept and
/// re-valued (also after its basis was switched off), suppressing the list line of its basis for that order line; any kept
/// adjusted line of a basis suppressing the open line of that basis for that order line. Risk class: Critical (money).
/// </summary>
public class ChargedBasesAndOpenLinesTests
{
    private static readonly QuantitySource[] LegacyOrder =
    [
        QuantitySource.LineQuantity, QuantitySource.Colli, QuantitySource.Epq, QuantitySource.WeightKg, QuantitySource.DistanceKm,
        QuantitySource.Positions, QuantitySource.PerLine, QuantitySource.VolumeM3, QuantitySource.LoadingMeters, QuantitySource.LoadFactor,
        QuantitySource.DurationH, QuantitySource.VehicleType, QuantitySource.TollDistanceKm
    ];

    private static PricingRequest Charged(PricingRequest request, params QuantitySource[] bases)
        => new()
        {
            Side = request.Side, CurrencyCode = request.CurrencyCode, AsOf = request.AsOf, Lines = request.Lines, PriceLists = request.PriceLists,
            MultipleMatches = request.MultipleMatches, ChargedBases = bases, DiscountsOrSurcharges = request.DiscountsOrSurcharges, Vat = request.Vat,
            FixedPriceAmount = request.FixedPriceAmount, KeptLines = request.KeptLines, KeptLineListBases = request.KeptLineListBases,
            RootAnchor = request.RootAnchor, LineAnchor = request.LineAnchor, TenantId = request.TenantId, NewId = request.NewId, Now = request.Now,
            Version = request.Version
        };

    private static PricingRequest WithListBases(PricingRequest request, IReadOnlyDictionary<Guid, QuantitySource> bases)
        => new()
        {
            Side = request.Side, CurrencyCode = request.CurrencyCode, AsOf = request.AsOf, Lines = request.Lines, PriceLists = request.PriceLists,
            MultipleMatches = request.MultipleMatches, ChargedBases = request.ChargedBases, DiscountsOrSurcharges = request.DiscountsOrSurcharges, Vat = request.Vat,
            FixedPriceAmount = request.FixedPriceAmount, KeptLines = request.KeptLines, KeptLineListBases = bases,
            RootAnchor = request.RootAnchor, LineAnchor = request.LineAnchor, TenantId = request.TenantId, NewId = request.NewId, Now = request.Now,
            Version = request.Version
        };

    private static bool IsOpen(RateLine l) => l.Kind == RateLineKind.PriceList && l.PriceListId == null && !l.IsManual;

    private static RateLine AdjustedOpen(PricingLine line, QuantitySource basis, decimal storedQuantity, decimal unitPrice)
    {
        var open = RateLine.Calculated(Guid.NewGuid(), TenantId, RateSide.Revenue, RateLineAnchor.OrderLine(OrderId, line.AnchorId), RateLineKind.PriceList, "EUR",
            PriceResolver.OpenBasisDescriptionKey, storedQuantity, basis, 0m, 0m);
        open.AdjustUnitPrice(unitPrice);
        return open;
    }

    private static RateLine AdjustedListLine(PricingLine line, Guid priceListId, QuantitySource quantitySource, decimal storedQuantity, decimal unitPrice)
    {
        var adjusted = RateLine.Calculated(Guid.NewGuid(), TenantId, RateSide.Revenue, RateLineAnchor.OrderLine(OrderId, line.AnchorId), RateLineKind.PriceList, "EUR",
            PriceResolver.PriceListDescriptionKey, storedQuantity, quantitySource, 10m, storedQuantity * 10m);
        adjusted.SetProvenance(priceListId, Guid.NewGuid(), Guid.NewGuid(), 1);
        adjusted.AdjustUnitPrice(unitPrice);
        return adjusted;
    }

    // ── the mask ───────────────────────────────────────────────────────────────

    [Fact]
    public void The_revenue_bases_are_the_thirteen_of_the_block_in_the_legacy_order_and_each_fits_the_mask()
    {
        ChargedBases.RevenueBases.ShouldBe(LegacyOrder);
        ChargedBases.RevenueBases.ShouldAllBe(b => (int)b >= 0 && (int)b < 31, "bit n = value n must fit a 32-bit integer column");
        ChargedBases.IsRevenueBasis(QuantitySource.Manual).ShouldBeFalse();
        ChargedBases.IsRevenueBasis(QuantitySource.TripDistanceKm).ShouldBeFalse();
    }

    [Fact]
    public void A_mask_round_trips_into_the_legacy_order_whatever_the_order_posted()
    {
        var mask = ChargedBases.ToMask([QuantitySource.TollDistanceKm, QuantitySource.WeightKg, QuantitySource.LineQuantity, QuantitySource.PerLine], "chargedBases");

        ChargedBases.FromMask(mask).ShouldBe([QuantitySource.LineQuantity, QuantitySource.WeightKg, QuantitySource.PerLine, QuantitySource.TollDistanceKm]);
        ChargedBases.FromMask(ChargedBases.ToMask(LegacyOrder.Reverse().ToList(), "chargedBases")).ShouldBe(LegacyOrder);
        ChargedBases.ToMask(null, "chargedBases").ShouldBe(0);
        ChargedBases.ToMask([], "chargedBases").ShouldBe(0);
        ChargedBases.FromMask(0).ShouldBeEmpty();
        ChargedBases.FromMask(1 << (int)QuantitySource.Manual | 1 << (int)QuantitySource.TripDistanceKm | 1 << 30).ShouldBeEmpty("unknown bits are ignored on read");
    }

    [Theory]
    [InlineData(QuantitySource.Manual)]
    [InlineData(QuantitySource.TripDistanceKm)]
    [InlineData((QuantitySource)99)]
    public void A_basis_that_cannot_be_charged_to_a_customer_is_refused_with_InvalidCode_and_the_field(QuantitySource basis)
    {
        var refusal = Should.Throw<BusinessException>(() => ChargedBases.ToMask([QuantitySource.LineQuantity, basis], "chargedBases"));

        refusal.Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        refusal.Data["field"].ShouldBe("chargedBases");
        refusal.Data["value"].ShouldBe(((int)basis).ToString());
    }

    [Fact]
    public void A_repeated_basis_is_refused_with_DuplicateKey()
    {
        var refusal = Should.Throw<BusinessException>(() => ChargedBases.ToMask([QuantitySource.WeightKg, QuantitySource.LineQuantity, QuantitySource.WeightKg], "revenueChargedBases"));

        refusal.Code.ShouldBe(ReferenceDataErrorCodes.DuplicateKey);
        refusal.Data["field"].ShouldBe("revenueChargedBases");
    }

    [Fact]
    public void The_relation_settings_store_the_revenue_charged_bases_with_the_same_rules()
    {
        var settings = new RelationFinancialSettings(Guid.NewGuid(), TenantId, Guid.NewGuid());
        settings.RevenueChargedBases.ShouldBeEmpty("existing rows hold 0: no basis");

        settings.SetRevenueChargedBases([QuantitySource.DistanceKm, QuantitySource.Colli]);
        settings.RevenueChargedBases.ShouldBe([QuantitySource.Colli, QuantitySource.DistanceKm]);

        Should.Throw<BusinessException>(() => settings.SetRevenueChargedBases([QuantitySource.TripDistanceKm])).Data["field"].ShouldBe("revenueChargedBases");
        Should.Throw<BusinessException>(() => settings.SetRevenueChargedBases([QuantitySource.Colli, QuantitySource.Colli])).Code.ShouldBe(ReferenceDataErrorCodes.DuplicateKey);
        settings.RevenueChargedBases.ShouldBe([QuantitySource.Colli, QuantitySource.DistanceKm], "a refused change leaves the stored bases alone");

        settings.SetRevenueChargedBases(null);
        settings.RevenueChargedBases.ShouldBeEmpty();
    }

    // ── open lines ─────────────────────────────────────────────────────────────

    [Fact]
    public void A_charged_basis_without_a_list_gives_every_order_line_one_open_line_with_its_quantity_of_the_basis_at_zero()
    {
        var first = Line(1, quantity: 3m, weightKg: 1250.5m);
        var second = Line(2, quantity: 1m, weightKg: 80m);

        var resolution = PriceResolver.Resolve(Charged(Request([first, second]), QuantitySource.WeightKg));

        resolution.NewLines.Count.ShouldBe(2);
        resolution.NewLines.ShouldAllBe(l => IsOpen(l));
        foreach (var (line, weight) in new[] { (first, 1250.5m), (second, 80m) })
        {
            var open = resolution.NewLines.Single(l => l.OrderLineId == line.AnchorId);
            (open.QuantitySource, open.Quantity, open.UnitPrice, open.Amount, open.DescriptionKey, open.IsAdjusted, open.Side)
                .ShouldBe((QuantitySource.WeightKg, weight, 0m, 0m, "Financial:RateLine.OpenBasis", false, RateSide.Revenue));
            open.AnchorOrderId.ShouldBe(OrderId);
        }

        resolution.Warnings.ShouldBeEmpty();
        RateLineTotals.Of(OrderId, resolution.NewLines).TotalAmount.ShouldBe(0m, "an open line adds nothing until it is priced");
    }

    [Fact]
    public void Open_lines_follow_the_legacy_order_of_the_bases_and_PerLine_and_VehicleType_count_one()
    {
        var line = Line(quantity: 4m, weightKg: 100m, distanceKm: 210m);

        var resolution = PriceResolver.Resolve(Charged(Request([line]), QuantitySource.VehicleType, QuantitySource.DistanceKm, QuantitySource.PerLine, QuantitySource.LineQuantity));

        resolution.NewLines.Select(l => (l.QuantitySource, l.Quantity)).ShouldBe([
            (QuantitySource.LineQuantity, 4m), (QuantitySource.DistanceKm, 210m), (QuantitySource.PerLine, 1m), (QuantitySource.VehicleType, 1m)]);
    }

    [Fact]
    public void An_unknown_value_of_the_basis_gives_quantity_zero_and_the_quantity_missing_warning()
    {
        var line = Line(distanceKm: null);

        var resolution = PriceResolver.Resolve(Charged(Request([line]), QuantitySource.DistanceKm));

        resolution.NewLines.ShouldHaveSingleItem().Quantity.ShouldBe(0m);
        resolution.Warnings.ShouldContain(FinancialErrorCodes.QuantityMissingWarning);
    }

    [Fact]
    public void A_list_of_the_basis_on_the_order_suppresses_the_open_line_whatever_its_validity_or_currency()
    {
        var expired = List("OLD", basis: QuantitySource.WeightKg, validFrom: new DateOnly(2025, 1, 1), validTill: Monday.AddDays(-1));
        var dollars = List("USD", basis: QuantitySource.DistanceKm, currency: "USD");
        var lists = new[] { WithRules(expired, Rule(expired, 1, Ams, null, Bru, null, null, null, null, null, null, 1m)), WithRules(dollars, Rule(dollars, 1, Ams, null, Bru, null, null, null, null, null, null, 1m)) };

        var resolution = PriceResolver.Resolve(Charged(Request([Line(weightKg: 500m, distanceKm: 100m)], lists), QuantitySource.WeightKg, QuantitySource.DistanceKm, QuantitySource.LineQuantity));

        resolution.NewLines.ShouldHaveSingleItem().QuantitySource.ShouldBe(QuantitySource.LineQuantity, "only the basis no list prices by is open");
        resolution.NewLines.ShouldAllBe(l => IsOpen(l), "the expired and foreign-currency lists price nothing");
    }

    [Fact]
    public void A_fixed_price_writes_no_open_lines_and_the_cost_side_never_does()
    {
        var withFixedPrice = PriceResolver.Resolve(Charged(Request([Line(weightKg: 500m)], fixedPrice: 800m), QuantitySource.WeightKg));
        withFixedPrice.NewLines.ShouldNotContain(l => IsOpen(l));
        withFixedPrice.NewLines.ShouldContain(l => l.Kind == RateLineKind.FixedPrice);

        var cost = PriceResolver.Resolve(Charged(Request([Line(weightKg: 500m)], side: RateSide.Cost), QuantitySource.WeightKg));
        cost.NewLines.ShouldBeEmpty();
    }

    [Fact]
    public void Bases_that_are_not_revenue_bases_never_give_an_open_line_even_when_they_reach_the_resolver()
    {
        var resolution = PriceResolver.Resolve(Charged(Request([Line(distanceKm: 100m)]), QuantitySource.Manual, QuantitySource.TripDistanceKm));

        resolution.NewLines.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(MultipleMatches.LowestWins)]
    [InlineData(MultipleMatches.HighestWins)]
    public void Open_lines_are_not_pruned_by_multiple_matches_and_do_not_win_lowest_over_a_priced_line(MultipleMatches rule)
    {
        var list = List("QTY");
        var priced = Rule(list, 1, Ams, null, Bru, null, null, null, null, null, null, 20m);
        var line = Line(quantity: 2m, weightKg: 900m);

        var resolution = PriceResolver.Resolve(Charged(Request([line], [WithRules(list, priced)], multipleMatches: rule), QuantitySource.WeightKg));

        resolution.NewLines.Single(l => l.PriceListId == list.Id).Amount.ShouldBe(40m, "the priced line survives next to an open line of 0");
        resolution.NewLines.Single(l => IsOpen(l)).QuantitySource.ShouldBe(QuantitySource.WeightKg);
    }

    [Fact]
    public void A_charged_distance_basis_counts_as_applied_and_suppresses_the_per_kilometre_surcharge_scenario_19()
    {
        var perKm = Surcharge("DIESEL", DiscountOrSurchargeValueKind.PerKilometre, 0.2m);

        var charged = PriceResolver.Resolve(Charged(Request([Line(distanceKm: 150m)], discounts: [perKm]), QuantitySource.DistanceKm));
        charged.NewLines.ShouldNotContain(l => l.Kind == RateLineKind.DiscountOrSurcharge);
        charged.NewLines.ShouldHaveSingleItem().QuantitySource.ShouldBe(QuantitySource.DistanceKm);

        var notCharged = PriceResolver.Resolve(Request([Line(distanceKm: 150m)], discounts: [perKm]));
        notCharged.NewLines.ShouldContain(l => l.Kind == RateLineKind.DiscountOrSurcharge, "without the basis the surcharge applies as before");
    }

    // ── adjusted open lines (QP6, build note 5) ────────────────────────────────

    [Fact]
    public void An_adjusted_open_line_is_kept_and_re_valued_from_the_lines_quantity_and_no_second_open_line_is_written()
    {
        var line = Line(quantity: 2m, weightKg: 500m);
        var adjusted = AdjustedOpen(line, QuantitySource.WeightKg, storedQuantity: 300m, unitPrice: 0.12m);

        var resolution = PriceResolver.Resolve(Charged(Request([line], kept: [adjusted]), QuantitySource.WeightKg));

        resolution.NewLines.ShouldBeEmpty("the fresh open line of that line and basis is the adjusted one");
        resolution.KeptLines.ShouldBe([adjusted]);
        (adjusted.Quantity, adjusted.UnitPrice, adjusted.Amount, adjusted.IsAdjusted, adjusted.CalculationVersion).ShouldBe((500m, 0.12m, 60m, true, 1L));
        adjusted.PriceListId.ShouldBeNull();
    }

    [Fact]
    public void An_adjusted_open_line_of_one_order_line_leaves_the_other_lines_open_line_alone()
    {
        var first = Line(1, weightKg: 500m);
        var second = Line(2, weightKg: 250m);
        var adjusted = AdjustedOpen(first, QuantitySource.WeightKg, 500m, 0.1m);

        var resolution = PriceResolver.Resolve(Charged(Request([first, second], kept: [adjusted]), QuantitySource.WeightKg));

        var open = resolution.NewLines.ShouldHaveSingleItem();
        open.OrderLineId.ShouldBe(second.AnchorId);
        open.Quantity.ShouldBe(250m);
    }

    [Fact]
    public void An_adjusted_open_line_stays_when_its_basis_is_switched_off_re_valued_from_the_current_quantity()
    {
        var line = Line(weightKg: 640m);
        var adjusted = AdjustedOpen(line, QuantitySource.WeightKg, storedQuantity: 500m, unitPrice: 0.1m);

        var resolution = PriceResolver.Resolve(Request([line], kept: [adjusted]));

        resolution.NewLines.ShouldBeEmpty();
        resolution.KeptLines.ShouldBe([adjusted]);
        (adjusted.Quantity, adjusted.Amount).ShouldBe((640m, 64m));
    }

    [Fact]
    public void An_adjusted_open_line_whose_fact_is_unknown_now_keeps_its_stored_quantity()
    {
        var line = Line(distanceKm: null);
        var adjusted = AdjustedOpen(line, QuantitySource.DistanceKm, storedQuantity: 120m, unitPrice: 1.5m);

        PriceResolver.Resolve(Request([line], kept: [adjusted]));

        (adjusted.Quantity, adjusted.Amount).ShouldBe((120m, 180m));
    }

    [Fact]
    public void An_adjusted_open_line_stays_under_a_fixed_price()
    {
        var line = Line(weightKg: 700m);
        var adjusted = AdjustedOpen(line, QuantitySource.WeightKg, storedQuantity: 500m, unitPrice: 0.1m);

        var resolution = PriceResolver.Resolve(Charged(Request([line], kept: [adjusted], fixedPrice: 900m), QuantitySource.WeightKg));

        resolution.KeptLines.ShouldBe([adjusted]);
        adjusted.Quantity.ShouldBe(700m);
        resolution.NewLines.ShouldNotContain(l => IsOpen(l));
    }

    [Fact]
    public void A_list_of_its_basis_chosen_later_writes_no_line_for_the_order_line_with_an_adjusted_open_line_no_double_charge()
    {
        var list = List("WGT", basis: QuantitySource.WeightKg);
        var rule = Rule(list, 1, Ams, null, Bru, null, null, null, null, null, null, 0.08m);
        var adjustedLine = Line(1, weightKg: 1000m);
        var otherLine = Line(2, weightKg: 500m);
        var adjusted = AdjustedOpen(adjustedLine, QuantitySource.WeightKg, storedQuantity: 1000m, unitPrice: 0.1m);

        var resolution = PriceResolver.Resolve(Charged(Request([adjustedLine, otherLine], [WithRules(list, rule)], kept: [adjusted]), QuantitySource.WeightKg));

        resolution.KeptLines.ShouldBe([adjusted]);
        adjusted.Amount.ShouldBe(100m);
        resolution.NewLines.ShouldNotContain(l => l.OrderLineId == adjustedLine.AnchorId, "the adjusted open line is that line's line for its basis");
        var listed = resolution.NewLines.ShouldHaveSingleItem();
        (listed.OrderLineId, listed.PriceListId, listed.Amount).ShouldBe((otherLine.AnchorId, list.Id, 40m));
        RateLineTotals.Of(OrderId, resolution.NewLines.Concat(resolution.KeptLines).ToList()).TotalAmount.ShouldBe(140m);
    }

    [Fact]
    public void An_adjusted_open_line_does_not_suppress_a_list_line_of_another_basis()
    {
        var list = List("QTY");
        var rule = Rule(list, 1, Ams, null, Bru, null, null, null, null, null, null, 20m);
        var line = Line(quantity: 2m, weightKg: 1000m);
        var adjusted = AdjustedOpen(line, QuantitySource.WeightKg, 1000m, 0.1m);

        var resolution = PriceResolver.Resolve(Charged(Request([line], [WithRules(list, rule)], kept: [adjusted]), QuantitySource.WeightKg));

        resolution.NewLines.ShouldHaveSingleItem().PriceListId.ShouldBe(list.Id);
        resolution.KeptLines.ShouldBe([adjusted]);
    }

    [Fact]
    public void An_adjusted_list_line_whose_list_was_removed_suppresses_the_open_line_of_the_lists_basis_for_its_order_line()
    {
        var removedListId = Guid.NewGuid();
        var withAdjusted = Line(1, quantity: 2m, weightKg: 800m);
        var other = Line(2, quantity: 1m, weightKg: 300m);
        /* The stored line's quantity source is the list's basis; the basis is read from Financial's table (KeptLineListBases). */
        var adjusted = AdjustedListLine(withAdjusted, removedListId, QuantitySource.WeightKg, 800m, 0.09m);

        var request = WithListBases(Charged(Request([withAdjusted, other], kept: [adjusted]), QuantitySource.WeightKg), new Dictionary<Guid, QuantitySource> { [removedListId] = QuantitySource.WeightKg });
        var resolution = PriceResolver.Resolve(request);

        resolution.KeptLines.ShouldBe([adjusted]);
        var open = resolution.NewLines.ShouldHaveSingleItem("only the line without an adjusted line of the basis is open");
        open.OrderLineId.ShouldBe(other.AnchorId);
    }

    [Fact]
    public void A_kept_adjusted_line_of_an_unknown_list_falls_back_to_its_quantity_source()
    {
        var line = Line(weightKg: 800m);
        var adjusted = AdjustedListLine(line, Guid.NewGuid(), QuantitySource.WeightKg, 800m, 0.09m);

        var resolution = PriceResolver.Resolve(Charged(Request([line], kept: [adjusted]), QuantitySource.WeightKg));

        resolution.NewLines.ShouldBeEmpty();
        resolution.KeptLines.ShouldBe([adjusted]);
    }

    [Fact]
    public void A_kept_adjusted_list_line_of_another_basis_leaves_the_open_line_of_the_charged_basis()
    {
        var removedListId = Guid.NewGuid();
        var line = Line(quantity: 2m, weightKg: 800m);
        /* A per-line-quantity list whose stored line took the quantity; the basis of the list (LineQuantity) is not the charged one. */
        var adjusted = AdjustedListLine(line, removedListId, QuantitySource.LineQuantity, 2m, 12m);

        var request = WithListBases(Charged(Request([line], kept: [adjusted]), QuantitySource.WeightKg), new Dictionary<Guid, QuantitySource> { [removedListId] = QuantitySource.LineQuantity });
        var resolution = PriceResolver.Resolve(request);

        resolution.NewLines.ShouldHaveSingleItem().QuantitySource.ShouldBe(QuantitySource.WeightKg);
    }

    [Fact]
    public void Manual_lines_never_suppress_an_open_line()
    {
        var line = Line(weightKg: 800m);
        var manual = RateLine.Manual(Guid.NewGuid(), TenantId, RateSide.Revenue, RateLineAnchor.OrderLine(OrderId, line.AnchorId), RateLineKind.Manual, "EUR", "By hand", 800m, QuantitySource.WeightKg, 0.1m, null);

        var resolution = PriceResolver.Resolve(Charged(Request([line], kept: [manual]), QuantitySource.WeightKg));

        resolution.NewLines.ShouldHaveSingleItem().ShouldMatch(l => IsOpen(l));
        resolution.KeptLines.ShouldBe([manual]);
    }
}

file static class OpenLineShouldly
{
    public static void ShouldMatch(this RateLine line, Func<RateLine, bool> predicate) => predicate(line).ShouldBeTrue();
}
