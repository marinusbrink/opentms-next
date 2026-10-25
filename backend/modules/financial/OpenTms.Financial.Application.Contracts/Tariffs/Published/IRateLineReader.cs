using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Financial.Tariffs.Published;

/// <summary>
/// The stored rate lines and totals of an order or a trip for other modules (design 56: Planning allocates cost and
/// revenue over the legs from these, AD-20; Orders stores revenue and margin; approval rules read revenue). In-process
/// only; reads stored columns, never calculates (AD-07).
/// </summary>
public interface IRateLineReader
{
    /// <summary>The totals of one order: revenue before and after discounts and surcharges, services, fixed price.</summary>
    Task<RateLineTotalsDto> GetOrderTotalsAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>The totals of several orders (at most <see cref="MaxKeys"/>), keyed by order id; orders without lines have zero totals.</summary>
    Task<IReadOnlyDictionary<Guid, RateLineTotalsDto>> GetOrderTotalsAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default);

    /// <summary>The cost total of one trip (the overriding trip line if any, else the sum of its lines and its legs' lines).</summary>
    Task<RateLineTotalsDto> GetTripTotalsAsync(Guid tripId, CancellationToken cancellationToken = default);

    /// <summary>The lines anchored to the given legs (cost side), for the allocation of a trip's cost over its legs.</summary>
    Task<IReadOnlyList<RateLineSnapshotDto>> GetLegLinesAsync(IReadOnlyCollection<Guid> legIds, CancellationToken cancellationToken = default);

    const int MaxKeys = 500;
}

public class RateLineTotalsDto
{
    public Guid AnchorId { get; set; }

    public string CurrencyCode { get; set; } = "EUR";

    /// <summary>The sum of the price-list and manual lines (before discounts, surcharges and services), or the fixed price.</summary>
    public decimal LinesAmount { get; set; }

    public decimal DiscountsAndSurchargesAmount { get; set; }

    public decimal AdditionalServicesAmount { get; set; }

    /// <summary>The total per AD-08: the overriding line if present, else the sum of everything beneath.</summary>
    public decimal TotalAmount { get; set; }

    public bool HasFixedPrice { get; set; }

    /// <summary>The derived-value version the lines were written for, null when no calculation ran yet.</summary>
    public long? CalculationVersion { get; set; }

    /// <summary>Totals per order line (revenue) or per leg (cost), by anchor id.</summary>
    public Dictionary<Guid, decimal> ByChild { get; set; } = new();
}

public class RateLineSnapshotDto
{
    public Guid Id { get; set; }

    public RateSide Side { get; set; }

    public RateLineKind Kind { get; set; }

    public Guid? OrderId { get; set; }

    public Guid? OrderLineId { get; set; }

    public Guid? LegId { get; set; }

    public Guid? StopId { get; set; }

    public Guid? TripId { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = "EUR";

    public bool OverridesChildren { get; set; }
}
