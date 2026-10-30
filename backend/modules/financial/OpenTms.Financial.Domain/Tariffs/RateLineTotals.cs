using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTms.Financial.Tariffs;

/// <summary>The totals of one anchor tree (AD-08), as the reader and the screens show them.</summary>
public sealed record RateLineTotalsResult(
    decimal LinesAmount,
    decimal DiscountsAndSurchargesAmount,
    decimal AdditionalServicesAmount,
    decimal TotalAmount,
    bool HasOverridingLine,
    long? CalculationVersion,
    IReadOnlyDictionary<Guid, decimal> ByChild);

/// <summary>
/// AD-08, implemented once (design 57 "Totals"): the total of an anchor is its overriding line when it has one, else the
/// sum of its own lines plus the totals of its children (order → lines and stops; trip → legs). Pure: works on the
/// stored lines, never calculates a price.
/// </summary>
public static class RateLineTotals
{
    /// <summary>The totals of the root anchor over all lines of its tree (the lines sharing one AnchorOrderId or AnchorTripId).</summary>
    public static RateLineTotalsResult Of(Guid rootId, IReadOnlyCollection<RateLine> lines)
    {
        var rootLines = lines.Where(l => IsRootLine(l, rootId)).ToList();
        var childLines = lines.Where(l => !IsRootLine(l, rootId)).ToList();

        var byChild = childLines
            .GroupBy(l => l.Anchor.Id)
            .ToDictionary(g => g.Key, g => TotalOf(g.ToList()));

        var linesAmount = Sum(childLines.Where(IsBaseLine)) + Sum(rootLines.Where(IsBaseLine));
        var discounts = Sum(lines.Where(l => l.Kind == RateLineKind.DiscountOrSurcharge));
        var services = Sum(lines.Where(l => l.Kind == RateLineKind.AdditionalService));

        var overriding = rootLines.FirstOrDefault(l => l.OverridesChildren);
        var total = overriding != null
            ? overriding.Amount
            : Sum(rootLines) + byChild.Values.Sum();

        return new RateLineTotalsResult(
            overriding != null ? overriding.Amount : linesAmount,
            discounts,
            services,
            total,
            overriding != null,
            lines.Max(l => l.CalculationVersion),
            byChild);
    }

    /// <summary>The total of one anchor's own lines: its overriding line if any, else their sum.</summary>
    public static decimal TotalOf(IReadOnlyCollection<RateLine> ownLines)
    {
        var overriding = ownLines.FirstOrDefault(l => l.OverridesChildren);
        return overriding != null ? overriding.Amount : Sum(ownLines);
    }

    private static bool IsRootLine(RateLine line, Guid rootId) => line.Anchor.Kind is RateLineAnchorKind.Order or RateLineAnchorKind.Trip && line.Anchor.Id == rootId;

    /// <summary>Price-list, manual and own-fleet lines: the freight itself, before discounts, surcharges and services.</summary>
    private static bool IsBaseLine(RateLine line) => line.Kind is RateLineKind.PriceList or RateLineKind.Manual or RateLineKind.OwnFleetDistance;

    private static decimal Sum(IEnumerable<RateLine> lines) => lines.Where(l => !l.OverridesChildren).Sum(l => l.Amount);
}
