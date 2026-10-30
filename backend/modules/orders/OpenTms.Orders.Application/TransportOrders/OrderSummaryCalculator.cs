using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.PlanningExecution.Published;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.DerivedValues;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// The calculator of target type <c>Orders.TransportOrder</c> (design 56 "Flow 2"; design 49): writes the order's stored
/// revenue, cost of revenue and margin from Financial's rate lines, the execution progress, trip numbers, CO₂ and per-line
/// distances from Planning's legs, and the grid columns (counts, first and last stop) from the order and Master Data.
/// Reads only published readers; runs in the job's transaction under the record's tenant; never calls out of process.
/// </summary>
public class OrderSummaryCalculator : IDerivedValueCalculator, ITransientDependency
{
    private readonly IRepository<TransportOrder, Guid> _orders;
    private readonly IRateLineReader _rateLines;
    private readonly ILegReader _legs;
    private readonly OrdersReferences _references;
    private readonly IClock _clock;

    public ILogger<OrderSummaryCalculator> Logger { get; set; } = NullLogger<OrderSummaryCalculator>.Instance;

    public OrderSummaryCalculator(IRepository<TransportOrder, Guid> orders, IRateLineReader rateLines, ILegReader legs, OrdersReferences references, IClock clock)
    {
        _orders = orders;
        _rateLines = rateLines;
        _legs = legs;
        _references = references;
        _clock = clock;
    }

    public async Task CalculateAsync(DerivedValueCalculationContext context)
    {
        var order = await _orders.FindAsync(context.TargetId, includeDetails: true, context.CancellationToken);
        if (order == null)
        {
            Logger.LogInformation("Order summary of {OrderId} v{Version}: the order no longer exists; nothing written.", context.TargetId, context.Version);
            return;
        }

        var totals = await _rateLines.GetOrderTotalsAsync(order.Id, context.CancellationToken);
        var legs = await _legs.GetByOrderAsync(order.Id, context.CancellationToken);
        var (first, last, stopCount) = order.StopEnds();
        var addresses = await _references.AddressesAsync(new[] { first?.Details.RelationAddressId, last?.Details.RelationAddressId }.OfType<Guid>(), require: false);

        var summary = Summarise(order, totals, legs, stopCount, Place(first, addresses), Place(last, addresses));
        order.WriteSummary(summary, _clock.Now);
        foreach (var line in order.Lines)
        {
            var lineLegs = legs.Where(l => l.OrderLineId == line.Id).ToList();
            line.WriteLegValues(Sum(lineLegs, l => l.DistanceM), Sum(lineLegs, l => l.DurationS), SumDecimal(lineLegs, l => l.EstimatedCo2Kg));
        }

        await _orders.UpdateAsync(order, cancellationToken: context.CancellationToken);
        Logger.LogInformation("Order summary of {OrderId} v{Version}: {Progress}, {LegCount} legs.", order.Id, context.Version, summary.ExecutionProgress, legs.Count);
    }

    /// <summary>The pure part, so the rules are unit-testable without a database.</summary>
    public static OrderSummary Summarise(TransportOrder order, RateLineTotalsDto totals, IReadOnlyList<LegSnapshotDto> legs, int stopCount, (string? Name, string? City) firstPlace, (string? Name, string? City) lastPlace)
    {
        var (first, last, _) = order.StopEnds();
        var revenue = totals.CalculationVersion == null ? (decimal?)null : totals.TotalAmount;
        var cost = SumDecimal(legs, l => l.AllocatedCostAmount);
        var tripNumbers = legs.Where(l => l.TripNumber != null).Select(l => l.TripNumber!).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();

        return new OrderSummary(
            revenue,
            cost,
            revenue != null && cost != null ? revenue - cost : null,
            SumDecimal(legs, l => l.EstimatedCo2Kg),
            ProgressOf(legs),
            order.Lines.Count,
            stopCount,
            firstPlace.Name,
            firstPlace.City,
            first?.Date,
            lastPlace.Name,
            lastPlace.City,
            last?.Date,
            tripNumbers.Count == 0 ? null : string.Join(", ", tripNumbers));
    }

    /// <summary>Unplanned | PartlyPlanned | Planned | InProgress | Delivered from the legs' trips and arrivals (design 56).</summary>
    public static ExecutionProgress ProgressOf(IReadOnlyList<LegSnapshotDto> legs)
    {
        if (legs.Count == 0 || legs.All(l => l.TripId == null))
        {
            return ExecutionProgress.Unplanned;
        }

        if (legs.Any(l => l.TripId == null))
        {
            return ExecutionProgress.PartlyPlanned;
        }

        if (legs.All(l => l.Destination.ActualAt != null || l.TripStatus == TransportTripStatus.Completed))
        {
            return ExecutionProgress.Delivered;
        }

        return legs.Any(l => l.TripStatus == TransportTripStatus.InProgress || l.Origin.ActualAt != null)
            ? ExecutionProgress.InProgress
            : ExecutionProgress.Planned;
    }

    private static (string? Name, string? City) Place(TransportOrderStop? stop, IReadOnlyDictionary<Guid, MasterData.Published.MasterDataAddressReferenceDto> addresses)
    {
        if (stop?.Details.RelationAddressId == null || !addresses.TryGetValue(stop.Details.RelationAddressId.Value, out var address))
        {
            return (null, null);
        }

        return (address.RelationName, address.City);
    }

    private static int? Sum(IReadOnlyList<LegSnapshotDto> legs, Func<LegSnapshotDto, int?> value)
        => legs.Count == 0 || legs.Any(l => value(l) == null) ? null : legs.Sum(l => value(l)!.Value);

    private static decimal? SumDecimal(IReadOnlyList<LegSnapshotDto> legs, Func<LegSnapshotDto, decimal?> value)
    {
        var known = legs.Select(value).OfType<decimal>().ToList();
        return known.Count == 0 ? null : known.Sum();
    }
}
