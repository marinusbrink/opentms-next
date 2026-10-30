using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTms.Financial.Tariffs.Pricing;

/// <summary>
/// The one price engine of both sides (design 57 "The calculation", pricing assessment §3–§5, scenarios 1–69), pure
/// and deterministic: validity on the as-of date (QP1), the lane per stop matching with an exact relation before a
/// region, the specificity cascade over the three dimensions (tier 3 → 0), the row bracket by the rule's quantity, the
/// column by the list's quantity (sums over the lines sharing a lane when the source says so), the multiplier, the
/// conversion factor, min/max (QP8), rounding to 4 (quantities, unit prices) and 2 (amounts, QP4), multiple matches
/// over calculated lines only (QP9), manual lines re-valued and adjusted lines keeping their unit price (QP6), the
/// discounts and surcharges in two passes with per-kilometre and toll lines, the fixed price as the overriding line,
/// and a currency mismatch as a warning (QP2). It never touches a repository: the caller loads, it decides.
/// </summary>
public static class PriceResolver
{
    public const string PriceListDescriptionKey = "Financial:RateLine.PriceList";
    public const string DiscountOrSurchargeDescriptionKey = "Financial:RateLine.DiscountOrSurcharge";

    /// <summary>
    /// An open line (design 61 "Order page review" point 3): a charged basis without a price list of that basis. Kind
    /// <see cref="RateLineKind.PriceList"/> without a price list, the line's quantity of the basis (0 while the fact is unknown),
    /// unit price 0. Identifiable in a read as kind PriceList with <c>PriceListId</c> null; once the user adjusted its unit
    /// price it is an adjusted line (QP6) and stays.
    /// </summary>
    public const string OpenBasisDescriptionKey = "Financial:RateLine.OpenBasis";

    public static PriceResolution Resolve(PricingRequest request)
    {
        var result = new PriceResolution();
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        var vatRate = request.Side == RateSide.Revenue ? request.Vat?.RateOn(request.AsOf) : null;

        /* 1–6: one calculated line per list and line. */
        var calculated = new List<RateLine>();
        var appliedBases = new HashSet<QuantitySource>();
        foreach (var priceList in request.PriceLists)
        {
            var list = priceList.List;
            if (!string.Equals(list.CurrencyCode, request.CurrencyCode, StringComparison.Ordinal))
            {
                warnings.Add(FinancialErrorCodes.CurrencyMismatchWarning);
                continue;
            }

            foreach (var line in request.Lines)
            {
                if (!list.IsValidOn(line.AsOf))
                {
                    continue;
                }

                var resolution = Explain(priceList, line, request.Lines);
                if (resolution.Warning != null)
                {
                    warnings.Add(resolution.Warning);
                }

                if (resolution.WinningRule == null || resolution.Amount == null)
                {
                    continue;
                }

                appliedBases.Add(list.Basis);
                var rateLine = RateLine.Calculated(
                    request.NewId(), request.TenantId, request.Side, request.LineAnchor(line.AnchorId), RateLineKind.PriceList, request.CurrencyCode,
                    PriceListDescriptionKey, resolution.Quantity!.Value, QuantitySourceOf(list), resolution.UnitPrice!.Value, resolution.Amount.Value);
                rateLine.SetProvenance(list.Id, resolution.WinningRule.Id, resolution.Bracket!.Id, resolution.ColumnPosition);
                calculated.Add(rateLine);
            }
        }

        /* Design 61 point 3: a charged basis no list prices by gives every line an open line; never under a fixed price. They
         * are not pruned by multiple matches (an open line of 0 would win "lowest" over a priced one). */
        var open = OpenLines(request, appliedBases, warnings);
        var linesByAnchor = request.Lines.ToDictionary(l => l.AnchorId);

        /* QP6: an adjusted line keeps its unit price and takes the fresh quantity of its list; the fresh line is dropped. An
         * adjusted open line (no list) matches the fresh open line of its basis; when its basis is no longer charged (or a list
         * of that basis was chosen, or a fixed price set) it stays and is re-valued from the line's current quantity. It is the
         * line's line for its basis (legacy "a manually adjusted line exists → keep it", pricing assessment §2): a list of that
         * basis writes no second line for that line (below). */
        var adjustedOpen = new HashSet<(RateLineAnchor Anchor, QuantitySource Basis)>();
        foreach (var kept in request.KeptLines.Where(k => k.IsAdjusted && !k.IsManual))
        {
            if (kept.PriceListId == null)
            {
                adjustedOpen.Add((kept.Anchor, kept.QuantitySource));
                var freshOpen = open.FirstOrDefault(o => o.QuantitySource == kept.QuantitySource && o.Anchor == kept.Anchor);
                if (freshOpen != null)
                {
                    open.Remove(freshOpen);
                    kept.Revalue(freshOpen.Quantity, request.Now, request.Version);
                }
                else
                {
                    var current = linesByAnchor.TryGetValue(kept.Anchor.Id, out var openLine) ? openLine.ValueOf(kept.QuantitySource) : null;
                    kept.Revalue(current is { } value ? RateLine.Round4(value) : kept.Quantity, request.Now, request.Version);
                }

                if (kept.QuantitySource != QuantitySource.Manual)
                {
                    appliedBases.Add(kept.QuantitySource);
                }

                result.KeptLines.Add(kept);
                continue;
            }

            /* Decided in the build (build note 5): an adjusted list line is the line's line for its list's basis, also once that
             * list is removed while the basis stays charged; the open line of that basis for that line is not written. */
            var keptBasis = request.PriceLists.FirstOrDefault(p => p.List.Id == kept.PriceListId)?.List.Basis
                ?? (request.KeptLineListBases.TryGetValue(kept.PriceListId.Value, out var storedBasis) ? storedBasis : kept.QuantitySource);
            open.RemoveAll(o => o.Anchor == kept.Anchor && o.QuantitySource == keptBasis);

            var fresh = calculated.FirstOrDefault(c => c.PriceListId == kept.PriceListId && c.Anchor == kept.Anchor);
            if (fresh != null)
            {
                calculated.Remove(fresh);
                kept.Revalue(fresh.Quantity, request.Now, request.Version);
            }
            else
            {
                kept.Revalue(kept.Quantity, request.Now, request.Version);
            }

            result.KeptLines.Add(kept);
        }

        /* Design 61 point 3, decided in the build (build note 5): no double charge next to an adjusted open line. The lists of its basis drop
         * their fresh line for that line; the basis stays applied (the list is on the order). */
        if (adjustedOpen.Count > 0)
        {
            var basisOfList = request.PriceLists.GroupBy(p => p.List.Id).ToDictionary(g => g.Key, g => g.First().List.Basis);
            calculated.RemoveAll(c => c.PriceListId is { } listId && basisOfList.TryGetValue(listId, out var basis) && adjustedOpen.Contains((c.Anchor, basis)));
        }

        /* QP9: multiple matches prune the calculated lines of one anchor only; manual lines are never pruned. */
        if (request.MultipleMatches != MultipleMatches.All)
        {
            calculated = calculated
                .GroupBy(c => c.Anchor)
                .SelectMany(g => g.Count() == 1 ? g.ToList() : new List<RateLine> { request.MultipleMatches == MultipleMatches.HighestWins ? g.MaxBy(c => c.Amount)! : g.MinBy(c => c.Amount)! })
                .ToList();
        }

        /* QP6: manual lines are re-valued from the current quantity of their source; a typed quantity stays. */
        foreach (var kept in request.KeptLines.Where(k => k.IsManual))
        {
            var quantity = kept.Quantity;
            if (kept.QuantitySource != QuantitySource.Manual && linesByAnchor.TryGetValue(kept.Anchor.Id, out var line) && line.ValueOf(kept.QuantitySource) is { } current)
            {
                quantity = RateLine.Round4(current);
            }

            kept.Revalue(quantity, request.Now, request.Version);
            result.KeptLines.Add(kept);
        }

        result.NewLines.AddRange(calculated);
        result.NewLines.AddRange(open);

        /* 7: the discounts and surcharges, by id, over the lines they apply to. */
        ApplyDiscountsAndSurcharges(request, result, appliedBases, warnings);

        /* The fixed price: the overriding line of the root (AD-08). */
        if (request.FixedPriceAmount != null)
        {
            result.NewLines.Add(RateLine.Overriding(request.NewId(), request.TenantId, request.Side, request.RootAnchor, RateLineKind.FixedPrice, request.CurrencyCode, RateLine.Round2(request.FixedPriceAmount.Value)));
        }

        /* Provenance and positions over the whole tree. */
        foreach (var line in result.NewLines)
        {
            line.SetVat(vatRate?.VatPercentageId, vatRate?.Id);
            line.SetCalculation(request.Now, request.Version);
        }

        var position = 1;
        foreach (var line in Ordered(result.AllLines, request.Lines))
        {
            line.Position = position++;
        }

        result.Warnings.AddRange(warnings.OrderBy(w => w, StringComparer.Ordinal));
        return result;
    }

    /// <summary>
    /// Design 61 "Order page review" point 3: per charged basis that none of the order's lists prices by (whatever their
    /// currency or validity: the row has a list), one open line per line. A basis that is charged counts as applied for the
    /// per-kilometre surcharges (scenario 19: a distance charge suppresses the distance surcharge), so it is added to
    /// <paramref name="appliedBases"/>. A line without a value for the basis gets quantity 0 and the quantity-missing warning.
    /// </summary>
    private static List<RateLine> OpenLines(PricingRequest request, HashSet<QuantitySource> appliedBases, HashSet<string> warnings)
    {
        var open = new List<RateLine>();
        if (request.Side != RateSide.Revenue || request.FixedPriceAmount != null || request.ChargedBases.Count == 0)
        {
            return open;
        }

        var listBases = request.PriceLists.Select(p => p.List.Basis).ToHashSet();
        foreach (var basis in ChargedBases.RevenueBases.Where(b => request.ChargedBases.Contains(b) && !listBases.Contains(b)))
        {
            appliedBases.Add(basis);
            foreach (var line in request.Lines)
            {
                var value = line.ValueOf(basis);
                if (value == null)
                {
                    warnings.Add(FinancialErrorCodes.QuantityMissingWarning);
                }

                var quantity = value is { } known ? RateLine.Round4(known) : 0m;
                var rateLine = RateLine.Calculated(
                    request.NewId(), request.TenantId, request.Side, request.LineAnchor(line.AnchorId), RateLineKind.PriceList, request.CurrencyCode,
                    OpenBasisDescriptionKey, quantity, basis, 0m, 0m);
                open.Add(rateLine);
            }
        }

        return open;
    }

    /// <summary>
    /// The lookup of one list for one line with its ranked candidates (steps 2–6), shared by the calculation and the
    /// Simulate panel. A list that does not price the line gives no winner, never a placeholder.
    /// </summary>
    public static LineResolution Explain(PriceListWithRules priceList, PricingLine line, IReadOnlyList<PricingLine> allLines)
    {
        var list = priceList.List;
        var candidates = new List<(PriceListRule Rule, int LaneScore)>();
        var trace = new List<RuleCandidate>();
        foreach (var rule in priceList.Rules)
        {
            if (!LaneMatches(list.StopMatching, rule, line, out var laneScore))
            {
                trace.Add(new RuleCandidate(rule.Id, rule.Tier, 0, rule.Position, "Lane"));
                continue;
            }

            if (!DimensionsMatch(rule, line))
            {
                trace.Add(new RuleCandidate(rule.Id, rule.Tier, laneScore, rule.Position, "Dimensions"));
                continue;
            }

            candidates.Add((rule, laneScore));
        }

        /* Step 3: tier 3 → 0, inside a tier the exact relation before the region, then the first by position. */
        var ranked = candidates
            .OrderByDescending(c => c.Rule.Tier)
            .ThenByDescending(c => c.LaneScore)
            .ThenBy(c => c.Rule.Position)
            .ToList();
        trace.AddRange(ranked.Select((c, i) => new RuleCandidate(c.Rule.Id, c.Rule.Tier, c.LaneScore, c.Rule.Position, i == 0 ? "Winner" : "Candidate")));
        var ordered = trace.OrderBy(t => t.Reason == "Winner" ? 0 : t.Reason == "Candidate" ? 1 : 2).ThenByDescending(t => t.Tier).ThenByDescending(t => t.LaneScore).ThenBy(t => t.Position).ToList();

        if (ranked.Count == 0)
        {
            return new LineResolution { PriceListId = list.Id, Candidates = ordered };
        }

        var winner = ranked[0].Rule;

        /* Step 4: the row bracket by the rule's quantity; no value → no line. */
        PriceListBracket? bracket;
        if (winner.RowQuantity == QuantitySource.Manual)
        {
            bracket = winner.OpenBracket ?? winner.Brackets.OrderBy(b => b.Position).FirstOrDefault();
        }
        else
        {
            var rowValue = line.ValueOf(winner.RowQuantity);
            if (rowValue == null)
            {
                return new LineResolution { PriceListId = list.Id, Candidates = ordered, WinningRule = winner, Warning = FinancialErrorCodes.QuantityMissingWarning };
            }

            bracket = winner.BracketOf(rowValue.Value);
        }

        if (bracket == null)
        {
            return new LineResolution { PriceListId = list.Id, Candidates = ordered, WinningRule = winner };
        }

        /* Step 5: the column by the list's quantity; a missing value counts as 0. */
        var columnPosition = list.ColumnPositionOf(ColumnValue(list, line, allLines) ?? 0m);
        var unitPrice = bracket.UnitPriceAt(columnPosition);
        if (unitPrice == null)
        {
            return new LineResolution { PriceListId = list.Id, Candidates = ordered, WinningRule = winner, Bracket = bracket, ColumnPosition = columnPosition };
        }

        /* Step 6: the quantity per multiplier and conversion factor; the amount rounded at the line and clamped. */
        var quantity = QuantityOf(list, line, allLines);
        if (quantity == null)
        {
            return new LineResolution { PriceListId = list.Id, Candidates = ordered, WinningRule = winner, Bracket = bracket, ColumnPosition = columnPosition, UnitPrice = unitPrice, Warning = FinancialErrorCodes.QuantityMissingWarning };
        }

        var rounded = RateLine.Round4(quantity.Value / list.UnitConversionFactor);
        var amount = RateLine.Round2(rounded * unitPrice.Value);
        if (winner.MinAmount != null && amount < winner.MinAmount.Value)
        {
            amount = winner.MinAmount.Value;
        }

        if (winner.MaxAmount != null && amount > winner.MaxAmount.Value)
        {
            amount = winner.MaxAmount.Value;
        }

        return new LineResolution
        {
            PriceListId = list.Id,
            Candidates = ordered,
            WinningRule = winner,
            Bracket = bracket,
            ColumnPosition = columnPosition,
            UnitPrice = unitPrice,
            Quantity = rounded,
            Amount = amount
        };
    }

    // ── the lane and the dimensions ────────────────────────────────────────────

    /// <summary>Step 2 for a loader: true when the rule's lane matches the line under the list's stop matching.</summary>
    public static bool LaneCanMatch(StopMatching matching, PriceListRule rule, PricingLine line) => LaneMatches(matching, rule, line, out _);

    /// <summary>
    /// Step 2: a rule's lane matches per the list's stop matching; an exact relation scores 2 per side, a region 1,
    /// an unset side 0 (for <see cref="StopMatching.Both"/> a one-sided rule is the fallback of a two-sided one).
    /// </summary>
    private static bool LaneMatches(StopMatching matching, PriceListRule rule, PricingLine line, out int score)
    {
        score = 0;
        switch (matching)
        {
            case StopMatching.First:
                return rule.HasFromSide && SideMatches(rule.FromRelationId, rule.FromRegionId, line.First, out score);
            case StopMatching.Last:
                return rule.HasToSide && SideMatches(rule.ToRelationId, rule.ToRegionId, line.Last, out score);
            default:
                var fromScore = 0;
                var toScore = 0;
                if (rule.HasFromSide && !SideMatches(rule.FromRelationId, rule.FromRegionId, line.First, out fromScore))
                {
                    return false;
                }

                if (rule.HasToSide && !SideMatches(rule.ToRelationId, rule.ToRegionId, line.Last, out toScore))
                {
                    return false;
                }

                score = fromScore + toScore;
                return rule.HasFromSide || rule.HasToSide;
        }
    }

    private static bool SideMatches(Guid? relationId, Guid? regionId, PricingStop stop, out int score)
    {
        if (relationId != null)
        {
            score = 2;
            return relationId.Value == stop.RelationId;
        }

        if (regionId != null)
        {
            score = 1;
            return stop.RegionIds.Contains(regionId.Value);
        }

        score = 0;
        return false;
    }

    private static bool DimensionsMatch(PriceListRule rule, PricingLine line)
        => (rule.TransportUnitId == null || rule.TransportUnitId == line.TransportUnitId)
        && (rule.ProductId == null || rule.ProductId == line.ProductId)
        && (rule.VehicleTypeId == null || rule.VehicleTypeId == line.VehicleTypeId);

    // ── the column quantity and the multiplier ─────────────────────────────────

    private static object LaneKey(StopMatching matching, PricingLine line) => matching switch
    {
        StopMatching.First => line.First.RelationId,
        StopMatching.Last => line.Last.RelationId,
        _ => (line.First.RelationId, line.Last.RelationId)
    };

    /// <summary>Step 5: the lines sharing the lane (by relation id, never by region: scenario 13), optionally grouped.</summary>
    private static IEnumerable<PricingLine> LaneSiblings(PriceList list, PricingLine line, IReadOnlyList<PricingLine> allLines, bool byUnit, bool byProduct)
    {
        var key = LaneKey(list.StopMatching, line);
        return allLines.Where(l => Equals(LaneKey(list.StopMatching, l), key)
            && (!byUnit || l.TransportUnitId == line.TransportUnitId)
            && (!byProduct || l.ProductId == line.ProductId));
    }

    private static decimal? Sum(IEnumerable<PricingLine> lines, QuantitySource source)
    {
        decimal? total = null;
        foreach (var value in lines.Select(l => l.ValueOf(source)).Where(v => v != null))
        {
            total = (total ?? 0m) + value!.Value;
        }

        return total;
    }

    private static decimal? ColumnValue(PriceList list, PricingLine line, IReadOnlyList<PricingLine> allLines) => list.ColumnQuantity switch
    {
        ColumnQuantitySource.LineBasis => line.ValueOf(list.Basis),
        ColumnQuantitySource.SumOfLaneLines => Sum(LaneSiblings(list, line, allLines, false, false), list.Basis),
        ColumnQuantitySource.SumByTransportUnit => Sum(LaneSiblings(list, line, allLines, true, false), list.Basis),
        ColumnQuantitySource.SumByProduct => Sum(LaneSiblings(list, line, allLines, false, true), list.Basis),
        ColumnQuantitySource.SumByTransportUnitAndProduct => Sum(LaneSiblings(list, line, allLines, true, true), list.Basis),
        ColumnQuantitySource.LineWeightKg => line.WeightKg,
        ColumnQuantitySource.LaneWeightKg => Sum(LaneSiblings(list, line, allLines, false, false), QuantitySource.WeightKg),
        ColumnQuantitySource.LineVolumeM3 => line.VolumeM3,
        ColumnQuantitySource.LaneVolumeM3 => Sum(LaneSiblings(list, line, allLines, false, false), QuantitySource.VolumeM3),
        ColumnQuantitySource.LineLoadingMeters => line.LoadingMeters,
        ColumnQuantitySource.LaneLoadingMeters => Sum(LaneSiblings(list, line, allLines, false, false), QuantitySource.LoadingMeters),
        ColumnQuantitySource.LineDistanceKm => line.DistanceKm,
        ColumnQuantitySource.LaneDistanceKm => Sum(LaneSiblings(list, line, allLines, false, false), QuantitySource.DistanceKm),
        ColumnQuantitySource.LineDurationH => line.DurationH,
        ColumnQuantitySource.LaneDurationH => Sum(LaneSiblings(list, line, allLines, false, false), QuantitySource.DurationH),
        _ => null
    };

    /// <summary>Step 6: the quantity per multiplier (QP3); null when the basis fact is missing (scenario 16, 19).</summary>
    private static decimal? QuantityOf(PriceList list, PricingLine line, IReadOnlyList<PricingLine> allLines) => list.Multiplier switch
    {
        PriceMultiplier.PerLine => 1m,
        PriceMultiplier.PerLineQuantity => line.Quantity,
        PriceMultiplier.PerLastStop => allLines.Where(l => l.Last.RelationId == line.Last.RelationId).OrderBy(l => l.Position).First().AnchorId == line.AnchorId ? 1m : 0m,
        _ => line.ValueOf(list.Basis)
    };

    private static QuantitySource QuantitySourceOf(PriceList list) => list.Multiplier switch
    {
        PriceMultiplier.PerLine or PriceMultiplier.PerLastStop => QuantitySource.PerLine,
        PriceMultiplier.PerLineQuantity => QuantitySource.LineQuantity,
        _ => list.Basis
    };

    // ── discounts and surcharges (step 7, assessment §5, scenarios 40–51) ──────

    private static void ApplyDiscountsAndSurcharges(PricingRequest request, PriceResolution result, HashSet<QuantitySource> appliedBases, HashSet<string> warnings)
    {
        var applicable = request.DiscountsOrSurcharges
            .Where(d => d.AppliesTo(request.Side) && d.AppliesOn(request.AsOf))
            .Select(d => (Item: d, Value: d.ValueOn(request.AsOf)))
            .Where(d => d.Value != null)
            .ToList();

        var baseLines = result.AllLines.Where(l => l.Kind is RateLineKind.PriceList or RateLineKind.Manual or RateLineKind.OwnFleetDistance).ToList();

        /* Per-kilometre lines first: they are freight lines in the base of the percentages (OpenTMS's distance lines).
         * Not generated when a list already charges by distance (scenario 19); toll lines always are. */
        var chargesByDistance = appliedBases.Contains(QuantitySource.DistanceKm);
        foreach (var (item, value) in applicable.Where(d => d.Item.ValueKind == DiscountOrSurchargeValueKind.PerKilometre))
        {
            if (!CurrencyMatches(value!, request, warnings))
            {
                continue;
            }

            if (item.KilometreBasis == KilometreBasis.Distance && chargesByDistance)
            {
                continue;
            }

            var sign = item.Direction == DiscountOrSurchargeDirection.Discount ? -1m : 1m;
            var factor = (item.RoundTrip ? 2m : 1m) * item.BillableKilometrePercentage / 100m;
            foreach (var line in request.Lines.Where(l => RegionMatches(item, l)))
            {
                var km = item.KilometreBasis == KilometreBasis.TollDistance ? line.TollDistanceKm : line.DistanceKm;
                if (km == null)
                {
                    warnings.Add(FinancialErrorCodes.QuantityMissingWarning);
                    continue;
                }

                /* A toll line applies the modifiers to the kilometres (3 decimals) and keeps the levy; a distance line bakes them into the rate. */
                decimal quantity;
                decimal unitPrice;
                if (item.KilometreBasis == KilometreBasis.TollDistance)
                {
                    quantity = decimal.Round(km.Value * factor, 3, MidpointRounding.AwayFromZero);
                    unitPrice = value!.Value;
                }
                else
                {
                    quantity = RateLine.Round4(km.Value);
                    unitPrice = RateLine.Round4(value!.Value * factor);
                }

                var amount = RateLine.Round2(quantity * unitPrice) * sign;
                var perKm = RateLine.Calculated(
                    request.NewId(), request.TenantId, request.Side, request.LineAnchor(line.AnchorId), RateLineKind.DiscountOrSurcharge, request.CurrencyCode,
                    DiscountOrSurchargeDescriptionKey, quantity, item.KilometreBasis == KilometreBasis.TollDistance ? QuantitySource.TollDistanceKm : QuantitySource.DistanceKm, unitPrice * sign, amount);
                perKm.SetDiscountOrSurcharge(item.Id, value.Id, item.AppliesAfterOthers);
                result.NewLines.Add(perKm);
                baseLines.Add(perKm);
            }
        }

        var baseByAnchor = baseLines.GroupBy(l => l.Anchor.Id).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));
        var baseTotal = baseLines.Sum(l => l.Amount);
        var firstPassTotal = 0m;

        foreach (var afterOthers in new[] { false, true })
        {
            foreach (var (item, value) in applicable.Where(d => d.Item.ValueKind != DiscountOrSurchargeValueKind.PerKilometre && d.Item.AppliesAfterOthers == afterOthers))
            {
                var hasRegions = item.Regions.Count > 0;
                var matchingLines = hasRegions ? request.Lines.Where(l => RegionMatches(item, l)).ToList() : request.Lines.ToList();
                if (hasRegions && matchingLines.Count == 0)
                {
                    continue;
                }

                var sign = item.Direction == DiscountOrSurchargeDirection.Discount ? -1m : 1m;
                decimal quantity;
                decimal unitPrice;
                decimal amount;
                if (item.ValueKind == DiscountOrSurchargeValueKind.Percentage)
                {
                    /* Over the sum of the lines it applies to; the second pass includes the first (scenario 41). */
                    var basis = hasRegions
                        ? matchingLines.Sum(l => baseByAnchor.GetValueOrDefault(l.AnchorId))
                        : baseTotal + (afterOthers ? firstPassTotal : 0m);
                    quantity = RateLine.Round4(basis);
                    unitPrice = RateLine.Round4(value!.Value / 100m) * sign;
                    amount = RateLine.Round2(basis * value.Value / 100m) * sign;
                }
                else
                {
                    if (!CurrencyMatches(value!, request, warnings))
                    {
                        continue;
                    }

                    quantity = 1m;
                    unitPrice = value!.Value * sign;
                    amount = RateLine.Round2(value.Value) * sign;
                }

                var line = RateLine.Calculated(
                    request.NewId(), request.TenantId, request.Side, request.RootAnchor, RateLineKind.DiscountOrSurcharge, request.CurrencyCode,
                    DiscountOrSurchargeDescriptionKey, quantity, QuantitySource.Manual, unitPrice, amount);
                line.SetDiscountOrSurcharge(item.Id, value!.Id, item.AppliesAfterOthers);
                result.NewLines.Add(line);
                if (!afterOthers)
                {
                    firstPassTotal += amount;
                }
            }
        }
    }

    private static bool CurrencyMatches(DiscountOrSurchargeValue value, PricingRequest request, HashSet<string> warnings)
    {
        if (value.CurrencyCode == null || string.Equals(value.CurrencyCode, request.CurrencyCode, StringComparison.Ordinal))
        {
            return true;
        }

        warnings.Add(FinancialErrorCodes.CurrencyMismatchWarning);
        return false;
    }

    /// <summary>A discount or surcharge without regions applies everywhere; with regions, the stop per <see cref="RegionAppliesAt"/> must lie in one.</summary>
    private static bool RegionMatches(DiscountOrSurcharge item, PricingLine line)
    {
        if (item.Regions.Count == 0)
        {
            return true;
        }

        return item.Regions.Any(r =>
            (r.AppliesAt != RegionAppliesAt.Unloading && line.First.RegionIds.Contains(r.GeographicalRegionId))
            || (r.AppliesAt != RegionAppliesAt.Loading && line.Last.RegionIds.Contains(r.GeographicalRegionId)));
    }

    // ── positions ──────────────────────────────────────────────────────────────

    /// <summary>Line-anchored lines in line order (list, manual, per-kilometre), then the root's (fixed price, passes, services).</summary>
    private static IEnumerable<RateLine> Ordered(IEnumerable<RateLine> lines, IReadOnlyList<PricingLine> pricingLines)
    {
        var linePosition = pricingLines.ToDictionary(l => l.AnchorId, l => l.Position);
        return lines
            .OrderBy(l => l.Anchor.Kind is RateLineAnchorKind.Order or RateLineAnchorKind.Trip ? 1 : 0)
            .ThenBy(l => linePosition.GetValueOrDefault(l.Anchor.Id, int.MaxValue))
            .ThenBy(l => KindOrder(l))
            .ThenBy(l => l.Position);
    }

    private static int KindOrder(RateLine line) => line.Kind switch
    {
        RateLineKind.FixedPrice or RateLineKind.AgreedPrice => 0,
        RateLineKind.PriceList => 1,
        RateLineKind.OwnFleetDistance => 2,
        RateLineKind.Manual => 3,
        RateLineKind.DiscountOrSurcharge => line.AppliesAfterOthers ? 5 : 4,
        _ => 6
    };
}
