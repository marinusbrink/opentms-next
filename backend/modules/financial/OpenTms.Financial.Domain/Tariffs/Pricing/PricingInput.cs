using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Financial.Invoicing;

namespace OpenTms.Financial.Tariffs.Pricing;

/// <summary>A stop as the resolver matches it: the relation, the regions its address lies in, its planned date.</summary>
public sealed class PricingStop
{
    public Guid? StopId { get; init; }

    public Guid RelationId { get; init; }

    public IReadOnlyCollection<Guid> RegionIds { get; init; } = Array.Empty<Guid>();

    public DateOnly Date { get; init; }
}

/// <summary>
/// One priced unit (design 57 "The calculation"): an order line with its quantities and its first and last stop, or on
/// the cost side a segment (one order line on one trip) with the segment's origin, destination and distance. Numbers
/// only; <see cref="AnchorId"/> is the order line or the leg the resulting lines hang on.
/// </summary>
public sealed class PricingLine
{
    public Guid AnchorId { get; init; }

    public int Position { get; init; }

    public Guid TransportUnitId { get; init; }

    public Guid? ProductId { get; init; }

    public Guid? VehicleTypeId { get; init; }

    public decimal Quantity { get; init; }

    public decimal? Positions { get; init; }

    public decimal? Colli { get; init; }

    public decimal? WeightKg { get; init; }

    public decimal? VolumeM3 { get; init; }

    public decimal? LoadingMeters { get; init; }

    public decimal? LoadFactor { get; init; }

    public decimal? Epq { get; init; }

    public decimal? DistanceKm { get; init; }

    public decimal? TollDistanceKm { get; init; }

    public decimal? DurationH { get; init; }

    public PricingStop First { get; init; } = new();

    public PricingStop Last { get; init; } = new();

    /// <summary>The as-of date of this line (QP1): the first stop's date for revenue, the trip date for cost.</summary>
    public DateOnly AsOf { get; init; }

    /// <summary>The line's value of a quantity source; null when the fact is unknown (no distance yet). Per line, per vehicle type: 1.</summary>
    public decimal? ValueOf(QuantitySource source) => source switch
    {
        QuantitySource.LineQuantity => Quantity,
        QuantitySource.Positions => Positions,
        QuantitySource.Colli => Colli,
        QuantitySource.WeightKg => WeightKg,
        QuantitySource.VolumeM3 => VolumeM3,
        QuantitySource.LoadingMeters => LoadingMeters,
        QuantitySource.LoadFactor => LoadFactor,
        QuantitySource.Epq => Epq,
        QuantitySource.DistanceKm or QuantitySource.TripDistanceKm => DistanceKm,
        QuantitySource.TollDistanceKm => TollDistanceKm,
        QuantitySource.DurationH => DurationH,
        QuantitySource.VehicleType or QuantitySource.PerLine => 1m,
        _ => null
    };
}

/// <summary>A price list with the rules the resolver may use (loaded once per calculation, never per line).</summary>
public sealed record PriceListWithRules(PriceList List, IReadOnlyList<PriceListRule> Rules);

/// <summary>Everything one resolution needs; built by the calculators from the snapshots and by the preview from the DTO.</summary>
public sealed class PricingRequest
{
    public RateSide Side { get; init; } = RateSide.Revenue;

    public string CurrencyCode { get; init; } = "EUR";

    /// <summary>The as-of date of the order-level lines (the discounts and surcharges, VAT): the earliest line date, else the order date.</summary>
    public DateOnly AsOf { get; init; }

    public IReadOnlyList<PricingLine> Lines { get; init; } = Array.Empty<PricingLine>();

    /// <summary>In position order (QP5).</summary>
    public IReadOnlyList<PriceListWithRules> PriceLists { get; init; } = Array.Empty<PriceListWithRules>();

    public MultipleMatches MultipleMatches { get; init; } = MultipleMatches.All;

    /// <summary>
    /// The bases the order is charged by (design 61 "Order page review" point 3; revenue only). A basis no list of
    /// <see cref="PriceLists"/> prices by gives every line one open line (<see cref="PriceResolver.OpenBasisDescriptionKey"/>);
    /// none while <see cref="FixedPriceAmount"/> is set.
    /// </summary>
    public IReadOnlyCollection<QuantitySource> ChargedBases { get; init; } = Array.Empty<QuantitySource>();

    /// <summary>The selected discounts and surcharges, with their value rows and regions loaded.</summary>
    public IReadOnlyList<DiscountOrSurcharge> DiscountsOrSurcharges { get; init; } = Array.Empty<DiscountOrSurcharge>();

    /// <summary>The order's VAT category with its rates, for the provenance of revenue lines; null = none stored.</summary>
    public VatPercentage? Vat { get; init; }

    /// <summary>An order-level fixed price (AD-08): emitted as the overriding line of the root.</summary>
    public decimal? FixedPriceAmount { get; init; }

    /// <summary>The stored manual and adjusted lines that survive the calculation (QP6); re-valued, never replaced.</summary>
    public IReadOnlyList<RateLine> KeptLines { get; init; } = Array.Empty<RateLine>();

    /// <summary>
    /// The basis of each price list a kept adjusted line names, also of a list no longer on the order (design 61 build note 5:
    /// such a line suppresses the open line of its basis). A list without an entry falls back to the line's quantity source.
    /// </summary>
    public IReadOnlyDictionary<Guid, QuantitySource> KeptLineListBases { get; init; } = new Dictionary<Guid, QuantitySource>();

    /// <summary>The root the order-level lines hang on (the order or the trip).</summary>
    public RateLineAnchor RootAnchor { get; init; }

    /// <summary>The anchor of a priced unit's lines by its <see cref="PricingLine.AnchorId"/> (an order line or a leg).</summary>
    public Func<Guid, RateLineAnchor> LineAnchor { get; init; } = _ => throw new InvalidOperationException("LineAnchor is not set.");

    public Guid? TenantId { get; init; }

    public Func<Guid> NewId { get; init; } = Guid.NewGuid;

    public DateTimeOffset Now { get; init; }

    public long Version { get; init; }
}

/// <summary>The outcome of one resolution: the new calculated lines, the kept lines re-valued, and the warning codes.</summary>
public sealed class PriceResolution
{
    public List<RateLine> NewLines { get; } = new();

    public List<RateLine> KeptLines { get; } = new();

    /// <summary>Warning codes (FinancialErrorCodes.*Warning), each once.</summary>
    public List<string> Warnings { get; } = new();

    public IEnumerable<RateLine> AllLines => KeptLines.Concat(NewLines);
}

/// <summary>Why a rule was or was not the winner for a line, for the Simulate panel (the mockup's cascade trace).</summary>
public sealed record RuleCandidate(Guid RuleId, int Tier, int LaneScore, int Position, string Reason);

/// <summary>The resolution of one list for one line, with its ranked candidates.</summary>
public sealed class LineResolution
{
    public Guid PriceListId { get; init; }

    public IReadOnlyList<RuleCandidate> Candidates { get; init; } = Array.Empty<RuleCandidate>();

    public PriceListRule? WinningRule { get; init; }

    public PriceListBracket? Bracket { get; init; }

    public int? ColumnPosition { get; init; }

    public decimal? UnitPrice { get; init; }

    public decimal? Quantity { get; init; }

    public decimal? Amount { get; init; }

    /// <summary>A warning when the line could not be priced (quantity missing), else null.</summary>
    public string? Warning { get; init; }
}
