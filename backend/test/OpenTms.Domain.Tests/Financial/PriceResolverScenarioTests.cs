using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Tariffs;
using OpenTms.Financial.Tariffs.Pricing;
using OpenTms.Platform.Localization;
using Shouldly;
using Xunit;
using static OpenTms.Financial.PricingTestData;

namespace OpenTms.Financial;

/// <summary>
/// The lookup, manual/requery/fixed and discount scenarios of docs/input/transport-assessment/pricing.md §10 (1–55) and the
/// cost-side resolver inputs (56, 60, 61), one test per scenario, over the pure resolver (design 57, test risk analysis:
/// Critical, decimal exactness). Scenarios 26, 27, 37 and 44 are the inverted rebuild behaviour (pricing assessment §8).
/// </summary>
public class PriceResolverScenarioTests
{
    private static PriceListRule Lane(PriceList list, int position, Guid? fromRelation = null, Guid? fromRegion = null, Guid? toRelation = null, Guid? toRegion = null, Guid? unit = null, Guid? product = null, Guid? vehicleType = null, decimal price = 10m)
        => Rule(list, position, fromRelation, fromRegion, toRelation, toRegion, unit, product, vehicleType, null, null, price);

    private static PriceResolution Resolve(PricingRequest request) => PriceResolver.Resolve(request);

    private static RateLine Single(PriceResolution resolution, RateLineKind kind = RateLineKind.PriceList) => resolution.NewLines.Single(l => l.Kind == kind);

    // ── Price list lookup (1–28) ───────────────────────────────────────────────

    [Fact]
    public void Scenario_01_both_stops_matching_a_rule_with_both_relations_wins_over_region_rules()
    {
        var list = List();
        var regions = Lane(list, 1, fromRegion: NlWest, toRegion: Be, price: 20m);
        var relations = Lane(list, 2, fromRelation: Ams, toRelation: Bru, price: 12m);

        var line = Single(Resolve(Request([Line()], [WithRules(list, regions, relations)])));

        line.PriceListRuleId.ShouldBe(relations.Id);
        line.UnitPrice.ShouldBe(12m);
    }

    [Fact]
    public void Scenario_02_only_region_rules_the_region_to_region_rule_applies()
    {
        var list = List();
        var regions = Lane(list, 1, fromRegion: NlWest, toRegion: Be, price: 20m);
        var elsewhere = Lane(list, 2, fromRegion: Be, toRegion: NlWest, price: 99m);

        var line = Single(Resolve(Request([Line()], [WithRules(list, regions, elsewhere)])));

        line.PriceListRuleId.ShouldBe(regions.Id);
        line.Amount.ShouldBe(20m);
    }

    [Fact]
    public void Scenario_03_an_exact_origin_with_a_region_destination_beats_region_to_region()
    {
        var list = List();
        var regions = Lane(list, 1, fromRegion: NlWest, toRegion: Be, price: 20m);
        var mixed = Lane(list, 2, fromRelation: Ams, toRegion: Be, price: 15m);

        Single(Resolve(Request([Line()], [WithRules(list, regions, mixed)]))).PriceListRuleId.ShouldBe(mixed.Id);
    }

    [Fact]
    public void Scenario_04_last_stop_matching_the_exact_relation_wins_over_the_region()
    {
        var list = List(stopMatching: StopMatching.Last);
        var region = Lane(list, 1, toRegion: Be, price: 20m);
        var exact = Lane(list, 2, toRelation: Bru, price: 11m);
        var otherOrigin = Line(from: Rtm, fromRegions: [NlWest]);

        var line = Single(Resolve(Request([otherOrigin], [WithRules(list, region, exact)])));

        line.PriceListRuleId.ShouldBe(exact.Id);
    }

    [Fact]
    public void Scenario_05_a_rule_matching_unit_product_and_vehicle_type_wins_over_partial_rules()
    {
        var list = List();
        var t1 = Lane(list, 1, fromRelation: Ams, toRelation: Bru, unit: Pallet, price: 30m);
        var t2 = Lane(list, 2, fromRelation: Ams, toRelation: Bru, unit: Pallet, product: Flowers, price: 25m);
        var t3 = Lane(list, 3, fromRelation: Ams, toRelation: Bru, unit: Pallet, product: Flowers, vehicleType: Truck, price: 20m);
        var line = Line(product: Flowers, vehicleType: Truck);

        var resolved = Single(Resolve(Request([line], [WithRules(list, t1, t2, t3)])));

        resolved.PriceListRuleId.ShouldBe(t3.Id);
        var explained = PriceResolver.Explain(WithRules(list, t1, t2, t3), line, [line]);
        explained.Candidates.Select(c => (c.RuleId, c.Reason)).ShouldBe([(t3.Id, "Winner"), (t2.Id, "Candidate"), (t1.Id, "Candidate")]);
    }

    [Fact]
    public void Scenario_06_only_the_transport_unit_matches_the_unit_rule_wins_and_the_product_rule_is_no_candidate()
    {
        var list = List();
        var byUnit = Lane(list, 1, fromRelation: Ams, toRelation: Bru, unit: Pallet, price: 30m);
        var byProduct = Lane(list, 2, fromRelation: Ams, toRelation: Bru, product: Flowers, price: 25m);
        var line = Line(product: Guid.NewGuid());

        Single(Resolve(Request([line], [WithRules(list, byUnit, byProduct)]))).PriceListRuleId.ShouldBe(byUnit.Id);
        PriceResolver.Explain(WithRules(list, byUnit, byProduct), line, [line]).Candidates.Single(c => c.RuleId == byProduct.Id).Reason.ShouldBe("Dimensions");
    }

    [Fact]
    public void Scenario_07_no_dimension_matches_the_lane_only_rule_applies()
    {
        var list = List();
        var laneOnly = Lane(list, 1, fromRelation: Ams, toRelation: Bru, price: 40m);
        var byUnit = Lane(list, 2, fromRelation: Ams, toRelation: Bru, unit: Roll, price: 30m);

        Single(Resolve(Request([Line()], [WithRules(list, laneOnly, byUnit)]))).PriceListRuleId.ShouldBe(laneOnly.Id);
    }

    [Fact]
    public void Scenario_08_no_lane_matches_no_line_is_produced_not_a_placeholder()
    {
        var list = List();
        var other = Lane(list, 1, fromRelation: Rtm, toRelation: Ant);

        var resolution = Resolve(Request([Line()], [WithRules(list, other)]));

        resolution.NewLines.ShouldBeEmpty();
        resolution.Warnings.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(350, 2, 14)]   // scenario 9
    [InlineData(1500, 4, 9)]   // scenario 10: the open column
    [InlineData(500, 2, 14)]   // scenario 11: the bound is inclusive
    public void Scenarios_09_10_11_the_weight_selects_the_column_with_an_inclusive_bound_and_an_open_last_column(int weight, int column, decimal unitPrice)
    {
        var list = List(columnQuantity: ColumnQuantitySource.LineWeightKg, columns: [100m, 500m, 1000m, null]);
        var rule = Rule(list, 1, Ams, null, Bru, null, null, null, null, null, null, 15m, 14m, 12m, 9m);

        var line = Single(Resolve(Request([Line(quantity: 3m, weightKg: weight)], [WithRules(list, rule)])));

        line.ColumnPosition.ShouldBe(column);
        line.UnitPrice.ShouldBe(unitPrice);
        line.Quantity.ShouldBe(3m);
        line.Amount.ShouldBe(3m * unitPrice);
    }

    [Fact]
    public void Scenario_12_the_lane_sum_puts_two_lines_of_300_kg_to_one_relation_in_the_600_kg_column()
    {
        var list = List(basis: QuantitySource.WeightKg, columnQuantity: ColumnQuantitySource.SumOfLaneLines, columns: [500m, null]);
        var rule = Rule(list, 1, Ams, null, Bru, null, null, null, null, null, null, 0.10m, 0.08m);
        var lines = new[] { Line(1, weightKg: 300m), Line(2, weightKg: 300m) };

        var resolution = Resolve(Request(lines, [WithRules(list, rule)]));

        resolution.NewLines.ShouldAllBe(l => l.ColumnPosition == 2 && l.UnitPrice == 0.08m && l.Amount == 24.00m);
    }

    [Fact]
    public void Scenario_13_a_second_line_to_another_relation_in_the_same_region_is_not_summed()
    {
        var list = List(basis: QuantitySource.WeightKg, columnQuantity: ColumnQuantitySource.SumOfLaneLines, columns: [500m, null]);
        var rule = Rule(list, 1, null, NlWest, null, Be, null, null, null, null, null, 0.10m, 0.08m);
        var lines = new[] { Line(1, weightKg: 300m), Line(2, to: Ant, toRegions: [Be], weightKg: 300m) };

        var resolution = Resolve(Request(lines, [WithRules(list, rule)]));

        resolution.NewLines.ShouldAllBe(l => l.ColumnPosition == 1 && l.UnitPrice == 0.10m);
    }

    [Fact]
    public void Scenario_14_grouped_by_transport_unit_pallets_are_summed_with_pallets_only()
    {
        var list = List(columnQuantity: ColumnQuantitySource.SumByTransportUnit, columns: [5m, null]);
        var rule = Rule(list, 1, Ams, null, Bru, null, null, null, null, null, null, 20m, 15m);
        var pallets1 = Line(1, quantity: 3m, unit: Pallet);
        var pallets2 = Line(2, quantity: 3m, unit: Pallet);
        var rolls = Line(3, quantity: 3m, unit: Roll);

        var resolution = Resolve(Request([pallets1, pallets2, rolls], [WithRules(list, rule)]));

        resolution.NewLines.Where(l => l.OrderLineId != rolls.AnchorId).ShouldAllBe(l => l.ColumnPosition == 2);
        resolution.NewLines.Single(l => l.OrderLineId == rolls.AnchorId).ColumnPosition.ShouldBe(1);
    }

    [Fact]
    public void Scenario_15_row_brackets_by_distance_select_the_tier_up_to_150_km()
    {
        var list = List(columns: [100m, 500m, null]);
        var rule = Rule(list, 1, Ams, null, Bru, null, Pallet, null, null, QuantitySource.DistanceKm, null, null,
            (50m, [12m, 11m, 10m]), (150m, [15m, 14m, 13m]), (null, [20m, 19m, 18m]));
        var line = Line(quantity: 3m, distanceKm: 120m);

        var resolved = Single(Resolve(Request([line], [WithRules(list, rule)])));

        resolved.PriceListBracketId.ShouldBe(rule.Brackets.Single(b => b.Position == 2).Id);
        resolved.ColumnPosition.ShouldBe(1);
        resolved.Amount.ShouldBe(45m);
    }

    [Fact]
    public void Scenario_16_a_line_without_distance_on_a_distance_graded_rule_gets_no_line_and_a_warning()
    {
        var list = List();
        var rule = Rule(list, 1, Ams, null, Bru, null, null, null, null, QuantitySource.DistanceKm, null, null, (50m, [12m]), (null, [20m]));

        var resolution = Resolve(Request([Line(distanceKm: null)], [WithRules(list, rule)]));

        resolution.NewLines.ShouldBeEmpty();
        resolution.Warnings.ShouldBe([FinancialErrorCodes.QuantityMissingWarning]);
    }

    [Fact]
    public void Scenario_17_per_pallet_type_the_quantity_times_the_unit_price()
    {
        var list = List();
        var pallets = Lane(list, 1, fromRelation: Ams, toRelation: Bru, unit: Pallet, price: 12.5m);
        var rolls = Lane(list, 2, fromRelation: Ams, toRelation: Bru, unit: Roll, price: 4m);

        var resolution = Resolve(Request([Line(1, quantity: 4m, unit: Pallet), Line(2, quantity: 10m, unit: Roll)], [WithRules(list, pallets, rolls)]));

        resolution.NewLines.Select(l => l.Amount).OrderBy(a => a).ShouldBe([40m, 50m]);
        resolution.NewLines.ShouldAllBe(l => l.QuantitySource == QuantitySource.LineQuantity);
    }

    [Fact]
    public void Scenario_18_a_per_vehicle_type_list_gives_one_line_per_line_with_quantity_1()
    {
        var list = List(basis: QuantitySource.VehicleType);
        var rule = Lane(list, 1, fromRelation: Ams, toRelation: Bru, vehicleType: Truck, price: 450m);

        var line = Single(Resolve(Request([Line(quantity: 7m, vehicleType: Truck)], [WithRules(list, rule)])));

        line.Quantity.ShouldBe(1m);
        line.Amount.ShouldBe(450m);
        line.QuantitySource.ShouldBe(QuantitySource.VehicleType);
    }

    [Fact]
    public void Scenario_19_a_per_kilometre_list_charges_the_distance_and_suppresses_the_per_kilometre_surcharge()
    {
        var list = List(basis: QuantitySource.DistanceKm);
        var rule = Lane(list, 1, fromRelation: Ams, toRelation: Bru, price: 1.2m);
        var perKm = Surcharge("DIESEL", DiscountOrSurchargeValueKind.PerKilometre, 0.2m);

        var resolution = Resolve(Request([Line(distanceKm: 150m)], [WithRules(list, rule)], [perKm]));

        var line = Single(resolution);
        line.Quantity.ShouldBe(150m);
        line.Amount.ShouldBe(180m);
        resolution.NewLines.ShouldNotContain(l => l.Kind == RateLineKind.DiscountOrSurcharge);
    }

    [Fact]
    public void Scenario_20_a_per_hour_list_charges_the_duration_in_hours()
    {
        var list = List(basis: QuantitySource.DurationH);
        var rule = Lane(list, 1, fromRelation: Ams, toRelation: Bru, price: 60m);

        var line = Single(Resolve(Request([Line(durationH: 2.5m)], [WithRules(list, rule)])));

        line.Quantity.ShouldBe(2.5m);
        line.Amount.ShouldBe(150m);
    }

    [Fact]
    public void Scenario_21_the_conversion_factor_1000_turns_2500_kg_into_2_5()
    {
        var list = List(basis: QuantitySource.WeightKg, factor: 1000m);
        var rule = Lane(list, 1, fromRelation: Ams, toRelation: Bru, price: 80m);

        var line = Single(Resolve(Request([Line(weightKg: 2500m)], [WithRules(list, rule)])));

        line.Quantity.ShouldBe(2.5m);
        line.Amount.ShouldBe(200m);
    }

    [Fact]
    public void Scenario_22_per_last_stop_the_first_line_to_a_delivery_gets_1_the_others_0()
    {
        var list = List(multiplier: PriceMultiplier.PerLastStop);
        var rule = Lane(list, 1, fromRelation: Ams, toRelation: Bru, price: 75m);
        var lines = new[] { Line(1), Line(2), Line(3) };

        var resolution = Resolve(Request(lines, [WithRules(list, rule)]));

        resolution.NewLines.Single(l => l.OrderLineId == lines[0].AnchorId).Amount.ShouldBe(75m);
        resolution.NewLines.Where(l => l.OrderLineId != lines[0].AnchorId).ShouldAllBe(l => l.Quantity == 0m && l.Amount == 0m);
    }

    [Fact]
    public void Scenario_23_a_manual_line_with_a_typed_quantity_keeps_that_quantity_on_a_requery()
    {
        var line = Line(quantity: 3m);
        var manual = RateLine.Manual(Guid.NewGuid(), TenantId, RateSide.Revenue, RateLineAnchor.OrderLine(OrderId, line.AnchorId), RateLineKind.Manual, "EUR", "Half a pallet", 0.5m, QuantitySource.Manual, 100m, null);

        var resolution = Resolve(Request([line], kept: [manual]));

        resolution.KeptLines.ShouldHaveSingleItem().Quantity.ShouldBe(0.5m);
        manual.Amount.ShouldBe(50m);
        manual.CalculationVersion.ShouldBe(1);
    }

    [Fact]
    public void Scenario_24_highest_wins_keeps_the_higher_of_two_lists_per_line()
    {
        var byQuantity = List("QTY");
        var byWeight = List("WGT", basis: QuantitySource.WeightKg);
        var q = Lane(byQuantity, 1, fromRelation: Ams, toRelation: Bru, price: 20m);
        var w = Lane(byWeight, 1, fromRelation: Ams, toRelation: Bru, price: 0.1m);

        var resolution = Resolve(Request([Line(quantity: 2m, weightKg: 1000m)], [WithRules(byQuantity, q), WithRules(byWeight, w)], multipleMatches: MultipleMatches.HighestWins));

        var line = Single(resolution);
        line.PriceListId.ShouldBe(byWeight.Id);
        line.Amount.ShouldBe(100m);
    }

    [Fact]
    public void Scenario_25_apply_all_keeps_both_lines()
    {
        var byQuantity = List("QTY");
        var byWeight = List("WGT", basis: QuantitySource.WeightKg);
        var q = Lane(byQuantity, 1, fromRelation: Ams, toRelation: Bru, price: 20m);
        var w = Lane(byWeight, 1, fromRelation: Ams, toRelation: Bru, price: 0.1m);

        var resolution = Resolve(Request([Line(quantity: 2m, weightKg: 1000m)], [WithRules(byQuantity, q), WithRules(byWeight, w)]));

        resolution.NewLines.Select(l => l.Amount).OrderBy(a => a).ShouldBe([40m, 100m]);
    }

    [Fact]
    public void Scenario_26_min_and_max_amounts_apply_in_the_rebuild()
    {
        var list = List();
        var rule = Rule(list, 1, Ams, null, Bru, null, null, null, null, 50m, 120m, 10m);

        Single(Resolve(Request([Line(quantity: 2m)], [WithRules(list, rule)]))).Amount.ShouldBe(50m, "clamped up to the minimum");
        Single(Resolve(Request([Line(quantity: 20m)], [WithRules(list, rule)]))).Amount.ShouldBe(120m, "clamped down to the maximum");
        Single(Resolve(Request([Line(quantity: 8m)], [WithRules(list, rule)]))).Amount.ShouldBe(80m);
    }

    [Fact]
    public void Scenario_27_an_expired_list_is_not_applied_in_the_rebuild()
    {
        var expired = List(validFrom: new DateOnly(2025, 1, 1), validTill: Monday.AddDays(-1));
        var future = List("FUT", validFrom: Monday.AddDays(1));
        var current = List("CUR", validFrom: Monday, validTill: Monday);
        var lists = new[] { WithRules(expired, Lane(expired, 1, fromRelation: Ams, toRelation: Bru)), WithRules(future, Lane(future, 1, fromRelation: Ams, toRelation: Bru)), WithRules(current, Lane(current, 1, fromRelation: Ams, toRelation: Bru, price: 7m)) };

        var resolution = Resolve(Request([Line()], lists));

        Single(resolution).PriceListId.ShouldBe(current.Id);
    }

    [Fact]
    public void Scenario_28_a_basis_without_a_list_produces_no_empty_line()
    {
        Resolve(Request([Line()])).NewLines.ShouldBeEmpty();
    }

    // ── Manual lines, requery, fixed price (29–39) ─────────────────────────────

    [Fact]
    public void Scenario_29_a_manual_distance_line_is_re_valued_from_the_current_distance()
    {
        var line = Line(distanceKm: 180m);
        var manual = RateLine.Manual(Guid.NewGuid(), TenantId, RateSide.Revenue, RateLineAnchor.OrderLine(OrderId, line.AnchorId), RateLineKind.Manual, "EUR", "Extra km", 150m, QuantitySource.DistanceKm, 0.5m, null);

        Resolve(Request([line], kept: [manual]));

        manual.Quantity.ShouldBe(180m);
        manual.Amount.ShouldBe(90m);
        manual.IsManual.ShouldBeTrue();
    }

    [Fact]
    public void Scenario_30_an_adjusted_list_line_keeps_its_unit_price_and_takes_the_fresh_quantity()
    {
        var list = List();
        var rule = Lane(list, 1, fromRelation: Ams, toRelation: Bru, price: 10m);
        var line = Line(quantity: 5m);
        var adjusted = RateLine.Calculated(Guid.NewGuid(), TenantId, RateSide.Revenue, RateLineAnchor.OrderLine(OrderId, line.AnchorId), RateLineKind.PriceList, "EUR", PriceResolver.PriceListDescriptionKey, 3m, QuantitySource.LineQuantity, 10m, 30m);
        adjusted.SetProvenance(list.Id, rule.Id, rule.Brackets.First().Id, 1);
        adjusted.AdjustUnitPrice(9m);

        var resolution = Resolve(Request([line], [WithRules(list, rule)], kept: [adjusted]));

        resolution.NewLines.ShouldBeEmpty("the fresh line is dropped for the adjusted one");
        adjusted.Quantity.ShouldBe(5m);
        adjusted.UnitPrice.ShouldBe(9m);
        adjusted.Amount.ShouldBe(45m);
        adjusted.IsAdjusted.ShouldBeTrue();
    }

    [Fact]
    public void Scenario_31_a_list_switched_off_loses_its_calculated_lines_while_manual_lines_stay()
    {
        var line = Line();
        var manual = RateLine.Manual(Guid.NewGuid(), TenantId, RateSide.Revenue, RateLineAnchor.OrderLine(OrderId, line.AnchorId), RateLineKind.Manual, "EUR", "Hand", 1m, QuantitySource.Manual, 25m, null);

        var resolution = Resolve(Request([line], lists: [], kept: [manual]));

        resolution.NewLines.ShouldBeEmpty();
        resolution.KeptLines.ShouldBe([manual]);
    }

    [Fact]
    public void Scenario_32_an_adjusted_line_of_a_switched_off_list_is_kept_as_it_is()
    {
        var line = Line(quantity: 5m);
        var adjusted = RateLine.Calculated(Guid.NewGuid(), TenantId, RateSide.Revenue, RateLineAnchor.OrderLine(OrderId, line.AnchorId), RateLineKind.PriceList, "EUR", PriceResolver.PriceListDescriptionKey, 3m, QuantitySource.LineQuantity, 10m, 30m);
        adjusted.SetProvenance(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
        adjusted.AdjustUnitPrice(9m);

        var resolution = Resolve(Request([line], kept: [adjusted]));

        resolution.KeptLines.ShouldBe([adjusted]);
        adjusted.Quantity.ShouldBe(3m);
        adjusted.Amount.ShouldBe(27m);
    }

    [Fact]
    public void Scenario_33_a_changed_distance_re_values_the_per_kilometre_surcharge_line()
    {
        var perKm = Surcharge("DIESEL", DiscountOrSurchargeValueKind.PerKilometre, 0.2m);
        var line = Line(distanceKm: 200m);

        var first = Single(Resolve(Request([line], discounts: [perKm])), RateLineKind.DiscountOrSurcharge);
        var second = Single(Resolve(Request([Line(anchorId: line.AnchorId, distanceKm: 250m)], discounts: [perKm])), RateLineKind.DiscountOrSurcharge);

        first.Amount.ShouldBe(40m);
        second.Amount.ShouldBe(50m);
        second.DiscountOrSurchargeId.ShouldBe(perKm.Id);
    }

    [Fact]
    public void Scenario_34_a_deselected_toll_surcharge_produces_no_toll_line()
    {
        var toll = Surcharge("TOLL", DiscountOrSurchargeValueKind.PerKilometre, 0.15m, kilometres: KilometreBasis.TollDistance);

        Resolve(Request([Line(distanceKm: 200m, tollKm: 40m)], discounts: [toll])).NewLines.ShouldHaveSingleItem();
        Resolve(Request([Line(distanceKm: 200m, tollKm: 40m)], discounts: [])).NewLines.ShouldBeEmpty();
    }

    [Fact]
    public void Scenario_35_a_selected_toll_surcharge_keeps_the_tolled_kilometres_as_its_quantity()
    {
        var toll = Surcharge("TOLL", DiscountOrSurchargeValueKind.PerKilometre, 0.15m, kilometres: KilometreBasis.TollDistance);

        var line = Single(Resolve(Request([Line(distanceKm: 200m, tollKm: 40.1234m)], discounts: [toll])), RateLineKind.DiscountOrSurcharge);

        line.Quantity.ShouldBe(40.123m, "kilometres rounded to 3 decimals");
        line.QuantitySource.ShouldBe(QuantitySource.TollDistanceKm);
        line.UnitPrice.ShouldBe(0.15m);
    }

    [Fact]
    public void Scenario_36_a_new_line_gets_its_own_per_kilometre_line_at_the_same_rate()
    {
        var perKm = Surcharge("DIESEL", DiscountOrSurchargeValueKind.PerKilometre, 0.2m);

        var resolution = Resolve(Request([Line(1, distanceKm: 100m), Line(2, distanceKm: 50m)], discounts: [perKm]));

        resolution.NewLines.Where(l => l.Kind == RateLineKind.DiscountOrSurcharge).Select(l => l.Amount).OrderBy(a => a).ShouldBe([10m, 20m]);
        resolution.NewLines.ShouldAllBe(l => l.UnitPrice == 0.2m);
    }

    [Fact]
    public void Scenario_37_a_fixed_price_and_freight_lines_exist_together_in_the_rebuild()
    {
        var list = List();
        var rule = Lane(list, 1, fromRelation: Ams, toRelation: Bru, price: 100m);

        var resolution = Resolve(Request([Line(quantity: 3m)], [WithRules(list, rule)], fixedPrice: 800m));

        resolution.NewLines.Count(l => l.Kind == RateLineKind.PriceList).ShouldBe(1);
        var fixedPrice = Single(resolution, RateLineKind.FixedPrice);
        fixedPrice.OverridesChildren.ShouldBeTrue();
        fixedPrice.OrderId.ShouldBe(OrderId);
    }

    [Fact]
    public void Scenario_38_the_total_of_a_fixed_price_order_is_the_fixed_price_with_the_lines_as_breakdown()
    {
        var list = List();
        var rule = Lane(list, 1, fromRelation: Ams, toRelation: Bru, price: 100m);

        var resolution = Resolve(Request([Line(1, quantity: 3m), Line(2, quantity: 2m)], [WithRules(list, rule)], fixedPrice: 800m));
        var totals = RateLineTotals.Of(OrderId, resolution.NewLines);

        totals.TotalAmount.ShouldBe(800m);
        totals.LinesAmount.ShouldBe(800m);
        totals.HasOverridingLine.ShouldBeTrue();
        totals.ByChild.Values.OrderBy(v => v).ShouldBe([200m, 300m]);
    }

    [Fact]
    public void Scenario_39_a_fixed_price_of_zero_is_an_overriding_line_of_zero()
    {
        var resolution = Resolve(Request([Line()], fixedPrice: 0m));

        Single(resolution, RateLineKind.FixedPrice).Amount.ShouldBe(0m);
        RateLineTotals.Of(OrderId, resolution.NewLines).TotalAmount.ShouldBe(0m);
    }

    // ── Discounts and surcharges, additional services, toll (40–55) ────────────

    private static (PriceListWithRules List, PricingLine Line) ThousandEuroOrder()
    {
        var list = List();
        var rule = Lane(list, 1, fromRelation: Ams, toRelation: Bru, price: 100m);
        return (WithRules(list, rule), Line(quantity: 10m, distanceKm: 150m));
    }

    [Fact]
    public void Scenario_40_a_5_percent_fuel_surcharge_on_1000_gives_1050()
    {
        var (list, line) = ThousandEuroOrder();
        var fuel = Surcharge("FUEL", DiscountOrSurchargeValueKind.Percentage, 5m);

        var resolution = Resolve(Request([line], [list], [fuel]));

        var surcharge = Single(resolution, RateLineKind.DiscountOrSurcharge);
        surcharge.Amount.ShouldBe(50m);
        surcharge.OrderId.ShouldBe(OrderId);
        surcharge.DiscountOrSurchargeValueId.ShouldBe(fuel.Values.Single().Id);
        RateLineTotals.Of(OrderId, resolution.NewLines).TotalAmount.ShouldBe(1050m);
    }

    [Fact]
    public void Scenario_41_a_2_percent_second_pass_applies_over_1050_and_gives_1071()
    {
        var (list, line) = ThousandEuroOrder();
        var fuel = Surcharge("FUEL", DiscountOrSurchargeValueKind.Percentage, 5m);
        var admin = Surcharge("ADMIN", DiscountOrSurchargeValueKind.Percentage, 2m, afterOthers: true);

        var resolution = Resolve(Request([line], [list], [admin, fuel]));

        resolution.NewLines.Single(l => l.DiscountOrSurchargeId == admin.Id).Amount.ShouldBe(21m);
        resolution.NewLines.Single(l => l.DiscountOrSurchargeId == admin.Id).AppliesAfterOthers.ShouldBeTrue();
        RateLineTotals.Of(OrderId, resolution.NewLines).TotalAmount.ShouldBe(1071m);
    }

    [Fact]
    public void Scenario_42_a_fixed_surcharge_is_added_once_whatever_the_number_of_lines()
    {
        var (list, _) = ThousandEuroOrder();
        var fixedFee = Surcharge("FEE", DiscountOrSurchargeValueKind.FixedAmount, 25m);

        var resolution = Resolve(Request([Line(1), Line(2), Line(3)], [list], [fixedFee]));

        resolution.NewLines.Count(l => l.Kind == RateLineKind.DiscountOrSurcharge).ShouldBe(1);
        Single(resolution, RateLineKind.DiscountOrSurcharge).Amount.ShouldBe(25m);
    }

    [Fact]
    public void Scenario_43_a_saturday_only_surcharge_applies_on_a_saturday_order_and_not_on_a_monday()
    {
        var (list, _) = ThousandEuroOrder();
        var weekend = Surcharge("SAT", DiscountOrSurchargeValueKind.Percentage, 10m, days: DaysOfWeek.Saturday);

        Resolve(Request([Line(quantity: 10m, asOf: Saturday)], [list], [weekend])).NewLines.ShouldContain(l => l.Kind == RateLineKind.DiscountOrSurcharge);
        Resolve(Request([Line(quantity: 10m, asOf: Monday)], [list], [weekend])).NewLines.ShouldNotContain(l => l.Kind == RateLineKind.DiscountOrSurcharge);
    }

    [Fact]
    public void Scenario_44_a_region_surcharge_with_two_matching_lines_is_charged_once_over_both_lines()
    {
        var (list, _) = ThousandEuroOrder();
        var regional = Surcharge("BE", DiscountOrSurchargeValueKind.Percentage, 10m, regions: [(Be, RegionAppliesAt.Unloading)]);
        var elsewhere = Line(3, quantity: 10m, to: Rtm, toRegions: [NlWest]);
        var toBelgium = new[] { Line(1, quantity: 10m), Line(2, quantity: 5m), elsewhere };

        var resolution = Resolve(Request(toBelgium, [list], [regional]));

        var surcharge = Single(resolution, RateLineKind.DiscountOrSurcharge);
        surcharge.Amount.ShouldBe(150m, "10 % over the two Belgian lines (1,000 + 500), not over the Dutch one");
    }

    [Fact]
    public void Scenario_45_a_region_fixed_surcharge_is_charged_once_when_any_line_matches()
    {
        var (list, _) = ThousandEuroOrder();
        var regional = Surcharge("BEFEE", DiscountOrSurchargeValueKind.FixedAmount, 30m, regions: [(Be, RegionAppliesAt.Unloading)]);

        Resolve(Request([Line(1), Line(2)], [list], [regional])).NewLines.Count(l => l.Kind == RateLineKind.DiscountOrSurcharge).ShouldBe(1);
        Resolve(Request([Line(1, to: Rtm, toRegions: [NlWest])], [list], [regional])).NewLines.ShouldNotContain(l => l.Kind == RateLineKind.DiscountOrSurcharge);
    }

    [Fact]
    public void Scenario_46_a_per_kilometre_surcharge_of_0_20_over_150_km_is_30()
    {
        var perKm = Surcharge("DIESEL", DiscountOrSurchargeValueKind.PerKilometre, 0.2m);
        var line = Line(distanceKm: 150m);

        var surcharge = Single(Resolve(Request([line], discounts: [perKm])), RateLineKind.DiscountOrSurcharge);

        surcharge.Quantity.ShouldBe(150m);
        surcharge.UnitPrice.ShouldBe(0.2m);
        surcharge.Amount.ShouldBe(30.00m);
        surcharge.OrderLineId.ShouldBe(line.AnchorId);
        surcharge.DescriptionKey.ShouldBe(PriceResolver.DiscountOrSurchargeDescriptionKey);
    }

    [Fact]
    public void Scenario_47_80_percent_billable_kilometres_bake_into_the_rate()
    {
        var perKm = Surcharge("DIESEL", DiscountOrSurchargeValueKind.PerKilometre, 0.2m, billable: 80m);

        var surcharge = Single(Resolve(Request([Line(distanceKm: 150m)], discounts: [perKm])), RateLineKind.DiscountOrSurcharge);

        surcharge.UnitPrice.ShouldBe(0.16m);
        surcharge.Amount.ShouldBe(24m);
    }

    [Fact]
    public void Scenario_48_a_round_trip_doubles_the_rate()
    {
        var perKm = Surcharge("DIESEL", DiscountOrSurchargeValueKind.PerKilometre, 0.2m, roundTrip: true);

        var surcharge = Single(Resolve(Request([Line(distanceKm: 150m)], discounts: [perKm])), RateLineKind.DiscountOrSurcharge);

        surcharge.UnitPrice.ShouldBe(0.4m);
        surcharge.Amount.ShouldBe(60m);
    }

    [Fact]
    public void Scenario_49_a_toll_surcharge_charges_the_tolled_kilometres_at_the_levy()
    {
        var toll = Surcharge("TOLL", DiscountOrSurchargeValueKind.PerKilometre, 0.1825m, kilometres: KilometreBasis.TollDistance);

        var surcharge = Single(Resolve(Request([Line(distanceKm: 200m, tollKm: 40m)], discounts: [toll])), RateLineKind.DiscountOrSurcharge);

        surcharge.Quantity.ShouldBe(40m);
        surcharge.UnitPrice.ShouldBe(0.1825m);
        surcharge.Amount.ShouldBe(7.30m);
    }

    [Fact]
    public void Scenario_50_a_toll_surcharge_without_tolled_kilometres_gives_no_line_and_a_warning()
    {
        var toll = Surcharge("TOLL", DiscountOrSurchargeValueKind.PerKilometre, 0.15m, kilometres: KilometreBasis.TollDistance);

        var resolution = Resolve(Request([Line(distanceKm: 200m, tollKm: null)], discounts: [toll]));

        resolution.NewLines.ShouldBeEmpty();
        resolution.Warnings.ShouldBe([FinancialErrorCodes.QuantityMissingWarning]);
    }

    [Fact]
    public void Scenario_51_a_toll_round_trip_at_50_percent_applies_to_the_kilometres_and_keeps_the_levy()
    {
        var toll = Surcharge("TOLL", DiscountOrSurchargeValueKind.PerKilometre, 0.15m, kilometres: KilometreBasis.TollDistance, billable: 50m, roundTrip: true);

        var surcharge = Single(Resolve(Request([Line(distanceKm: 200m, tollKm: 40m)], discounts: [toll])), RateLineKind.DiscountOrSurcharge);

        surcharge.Quantity.ShouldBe(40m, "40 × 2 × 0.5");
        surcharge.UnitPrice.ShouldBe(0.15m);
        surcharge.Amount.ShouldBe(6m);
    }

    [Fact]
    public void Scenario_52_a_renamed_surcharge_is_still_recognised_by_its_id()
    {
        var perKm = Surcharge("DIESEL", DiscountOrSurchargeValueKind.PerKilometre, 0.2m);
        var before = Single(Resolve(Request([Line(distanceKm: 100m)], discounts: [perKm])), RateLineKind.DiscountOrSurcharge);

        perKm.SetName("Brandstoftoeslag 2027");
        var after = Single(Resolve(Request([Line(distanceKm: 100m)], discounts: [perKm])), RateLineKind.DiscountOrSurcharge);

        after.DiscountOrSurchargeId.ShouldBe(before.DiscountOrSurchargeId);
        after.Description.ShouldBeNull("names are never stored on a calculated line");
    }

    [Fact]
    public void Scenario_53_the_value_row_valid_on_the_as_of_date_is_used_and_stored_as_provenance()
    {
        var fuel = Surcharge("FUEL", DiscountOrSurchargeValueKind.Percentage, 5m, validFrom: new DateOnly(2020, 1, 1));
        fuel.SetValues([(new DateOnly(2020, 1, 1), 5m, null), (Monday.AddDays(1), 7m, null)], fixedOn: null, Guid.NewGuid);
        var (list, line) = ThousandEuroOrder();

        var onMonday = Single(Resolve(Request([line], [list], [fuel])), RateLineKind.DiscountOrSurcharge);
        var later = Single(Resolve(Request([Line(quantity: 10m, asOf: Monday.AddDays(5))], [list], [fuel])), RateLineKind.DiscountOrSurcharge);

        onMonday.Amount.ShouldBe(50m);
        onMonday.DiscountOrSurchargeValueId.ShouldBe(fuel.Values.Single(v => v.Value == 5m).Id);
        later.Amount.ShouldBe(70m);
        later.DiscountOrSurchargeValueId.ShouldBe(fuel.Values.Single(v => v.Value == 7m).Id);
    }

    [Fact]
    public void Scenario_54_an_additional_service_line_is_kept_outside_the_surcharge_base_and_added_to_the_total()
    {
        var (list, line) = ThousandEuroOrder();
        var fuel = Surcharge("FUEL", DiscountOrSurchargeValueKind.Percentage, 5m);
        var waiting = RateLine.Manual(Guid.NewGuid(), TenantId, RateSide.Revenue, RateLineAnchor.Order(OrderId), RateLineKind.AdditionalService, "EUR", "Waiting time", 2m, QuantitySource.Manual, 45m, Guid.NewGuid());

        var resolution = Resolve(Request([line], [list], [fuel], kept: [waiting]));
        var totals = RateLineTotals.Of(OrderId, resolution.AllLines.ToList());

        Single(resolution, RateLineKind.DiscountOrSurcharge).Amount.ShouldBe(50m, "5 % over the freight only");
        totals.AdditionalServicesAmount.ShouldBe(90m);
        totals.TotalAmount.ShouldBe(1140m);
    }

    [Fact]
    public void Scenario_55_a_relation_default_valid_till_last_month_is_not_proposed_today()
    {
        var settings = new RelationFinancialSettings(Guid.NewGuid(), TenantId, Guid.NewGuid());
        var expired = Guid.NewGuid();
        var current = Guid.NewGuid();
        settings.SetDiscountsOrSurcharges([(expired, new DateOnly(2025, 1, 1), Monday.AddMonths(-1)), (current, new DateOnly(2025, 1, 1), null)], Guid.NewGuid);

        settings.DiscountOrSurchargeIdsOn(Monday).ShouldBe([current]);
    }

    // ── The cost side (56, 60, 61) ─────────────────────────────────────────────

    [Fact]
    public void Scenario_56_a_carrier_list_prices_a_segment_on_the_cost_side_anchored_to_the_leg()
    {
        var tripId = Guid.NewGuid();
        var legId = Guid.NewGuid();
        var inbound = List("CARRIER", side: RateSide.Cost);
        var rule = Lane(inbound, 1, fromRelation: Ams, toRelation: Bru, price: 8m);
        var revenueOnly = Surcharge("FUEL", DiscountOrSurchargeValueKind.Percentage, 5m, side: DiscountOrSurchargeSide.Revenue);

        var resolution = Resolve(Request([Line(anchorId: legId, quantity: 4m)], [WithRules(inbound, rule)], [revenueOnly], side: RateSide.Cost, rootId: tripId));

        var cost = Single(resolution);
        cost.Side.ShouldBe(RateSide.Cost);
        cost.LegId.ShouldBe(legId);
        cost.AnchorTripId.ShouldBe(tripId);
        cost.Amount.ShouldBe(32m);
        resolution.NewLines.ShouldNotContain(l => l.Kind == RateLineKind.DiscountOrSurcharge, "a revenue-only surcharge does not apply to cost");
    }

    [Fact]
    public void Scenario_60_two_carrier_lists_with_lowest_wins_give_one_line_per_segment()
    {
        var tripId = Guid.NewGuid();
        var a = List("A", side: RateSide.Cost);
        var b = List("B", side: RateSide.Cost);
        var lists = new[] { WithRules(a, Lane(a, 1, fromRelation: Ams, toRelation: Bru, price: 9m)), WithRules(b, Lane(b, 1, fromRelation: Ams, toRelation: Bru, price: 7m)) };

        var resolution = Resolve(Request([Line(1), Line(2)], lists, multipleMatches: MultipleMatches.LowestWins, side: RateSide.Cost, rootId: tripId));

        resolution.NewLines.Count.ShouldBe(2);
        resolution.NewLines.ShouldAllBe(l => l.PriceListId == b.Id && l.Amount == 7m);
    }

    [Fact]
    public void Scenario_61_a_manual_cost_line_is_kept_and_re_valued()
    {
        var tripId = Guid.NewGuid();
        var segment = Line(distanceKm: 320m);
        var manual = RateLine.Manual(Guid.NewGuid(), TenantId, RateSide.Cost, RateLineAnchor.Leg(tripId, segment.AnchorId), RateLineKind.Manual, "EUR", "Ferry", 300m, QuantitySource.DistanceKm, 0.9m, null);

        var resolution = Resolve(Request([segment], kept: [manual], side: RateSide.Cost, rootId: tripId));

        resolution.KeptLines.ShouldBe([manual]);
        manual.Quantity.ShouldBe(320m);
        manual.Amount.ShouldBe(288m);
    }

    // ── Cross-cutting rules of the design ──────────────────────────────────────

    [Fact]
    public void A_list_in_another_currency_is_skipped_with_a_warning()
    {
        var usd = List("USD", currency: "USD");
        var rule = Lane(usd, 1, fromRelation: Ams, toRelation: Bru);

        var resolution = Resolve(Request([Line()], [WithRules(usd, rule)]));

        resolution.NewLines.ShouldBeEmpty();
        resolution.Warnings.ShouldBe([FinancialErrorCodes.CurrencyMismatchWarning]);
    }

    [Fact]
    public void A_fixed_surcharge_in_another_currency_is_skipped_with_a_warning_while_a_percentage_has_none()
    {
        var (list, line) = ThousandEuroOrder();
        var usdFee = Surcharge("USDFEE", DiscountOrSurchargeValueKind.FixedAmount, 25m, currency: "USD");
        var pct = Surcharge("PCT", DiscountOrSurchargeValueKind.Percentage, 5m);

        var resolution = Resolve(Request([line], [list], [usdFee, pct]));

        resolution.NewLines.Count(l => l.Kind == RateLineKind.DiscountOrSurcharge).ShouldBe(1);
        resolution.Warnings.ShouldBe([FinancialErrorCodes.CurrencyMismatchWarning]);
    }

    [Fact]
    public void A_discount_is_a_negative_line()
    {
        var (list, line) = ThousandEuroOrder();
        var discount = Surcharge("LOYAL", DiscountOrSurchargeValueKind.Percentage, 2.5m, direction: DiscountOrSurchargeDirection.Discount);

        var resolution = Resolve(Request([line], [list], [discount]));

        Single(resolution, RateLineKind.DiscountOrSurcharge).Amount.ShouldBe(-25m);
        RateLineTotals.Of(OrderId, resolution.NewLines).TotalAmount.ShouldBe(975m);
    }

    [Fact]
    public void The_vat_rate_valid_on_the_as_of_date_is_stored_on_every_revenue_line()
    {
        var vat = new VatPercentage(Guid.NewGuid(), TenantId, "HIGH", LocalizedName.Create(new Dictionary<string, string?> { ["en"] = "High" }), VatKind.Standard, "NL");
        vat.SetRates([(new DateOnly(2012, 10, 1), 21m), (Monday.AddDays(1), 22m)], fixedOn: null, Guid.NewGuid);
        var (list, line) = ThousandEuroOrder();

        var resolution = Resolve(Request([line], [list], vat: vat));

        var current = vat.Rates.Single(r => r.Percentage == 21m);
        resolution.NewLines.ShouldAllBe(l => l.VatPercentageId == vat.Id && l.VatPercentageRateId == current.Id);
    }

    [Fact]
    public void Amounts_are_rounded_half_away_from_zero_at_the_line_and_totals_are_sums_of_lines()
    {
        var list = List(basis: QuantitySource.WeightKg);
        var rule = Lane(list, 1, fromRelation: Ams, toRelation: Bru, price: 0.0333m);

        var resolution = Resolve(Request([Line(1, weightKg: 1001m), Line(2, weightKg: 1002m)], [WithRules(list, rule)]));

        resolution.NewLines.Select(l => l.Amount).OrderBy(a => a).ShouldBe([33.33m, 33.37m]);
        RateLineTotals.Of(OrderId, resolution.NewLines).TotalAmount.ShouldBe(66.70m);
    }

    [Fact]
    public void Lines_are_numbered_1_to_n_lines_first_then_the_order_level_lines()
    {
        var (list, line) = ThousandEuroOrder();
        var fuel = Surcharge("FUEL", DiscountOrSurchargeValueKind.Percentage, 5m);
        var admin = Surcharge("ADMIN", DiscountOrSurchargeValueKind.Percentage, 2m, afterOthers: true);

        var resolution = Resolve(Request([line, Line(2, quantity: 1m)], [list], [admin, fuel], fixedPrice: 1500m));

        var ordered = resolution.NewLines.OrderBy(l => l.Position).ToList();
        ordered.Select(l => l.Position).ShouldBe(Enumerable.Range(1, ordered.Count));
        ordered.Select(l => l.Kind).ShouldBe([RateLineKind.PriceList, RateLineKind.PriceList, RateLineKind.FixedPrice, RateLineKind.DiscountOrSurcharge, RateLineKind.DiscountOrSurcharge]);
        ordered[3].DiscountOrSurchargeId.ShouldBe(fuel.Id);
        ordered[4].DiscountOrSurchargeId.ShouldBe(admin.Id);
    }
}
