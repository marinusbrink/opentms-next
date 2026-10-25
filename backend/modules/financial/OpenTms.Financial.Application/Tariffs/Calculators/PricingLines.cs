using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Financial.Tariffs.Pricing;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.PlanningExecution.Published;

namespace OpenTms.Financial.Tariffs.Calculators;

/// <summary>
/// Turns the snapshots of other modules into the resolver's lines (design 57 "The calculation"): an order's lines with
/// their stops and the distances of their legs (revenue), or the segments of one trip (cost: one order line on one
/// trip, from the first leg's origin to the last leg's destination, with the summed distances, anchored to the
/// segment's first leg). Numbers only.
/// </summary>
public static class PricingLines
{
    /// <summary>The revenue lines of an order; an unsaved line (no id) gets a synthetic anchor id from its position.</summary>
    public static IReadOnlyList<PricingLine> OfOrder(PricingSnapshotDto snapshot, IReadOnlyList<LegSnapshotDto> legs)
    {
        var legsByLine = legs.GroupBy(l => l.OrderLineId).ToDictionary(g => g.Key, g => g.ToList());
        var lines = new List<PricingLine>();
        foreach (var line in snapshot.Lines.OrderBy(l => l.Position))
        {
            var anchorId = line.LineId ?? SyntheticId(line.Position);
            var stops = line.Stops.OrderBy(s => s.Position).ToList();
            if (stops.Count == 0)
            {
                continue;
            }

            var lineLegs = line.LineId != null ? legsByLine.GetValueOrDefault(line.LineId.Value) : null;
            var distanceM = lineLegs != null && lineLegs.Any(l => l.DistanceM != null) ? lineLegs.Sum(l => l.DistanceM ?? 0) : line.DistanceM;
            var tollM = lineLegs != null && lineLegs.Any(l => l.TollDistanceM != null) ? lineLegs.Sum(l => l.TollDistanceM ?? 0) : line.TollDistanceM;
            var durationS = lineLegs != null && lineLegs.Any(l => l.DurationS != null) ? lineLegs.Sum(l => l.DurationS ?? 0) : line.DurationS;

            lines.Add(new PricingLine
            {
                AnchorId = anchorId,
                Position = line.Position,
                TransportUnitId = line.TransportUnitId,
                ProductId = line.ProductId,
                VehicleTypeId = line.VehicleTypeId,
                Quantity = line.Quantity,
                Positions = line.Positions,
                Colli = line.Colli,
                WeightKg = ToDecimal(line.WeightKg),
                VolumeM3 = ToDecimal(line.VolumeM3),
                LoadingMeters = ToDecimal(line.LoadingMeters),
                LoadFactor = ToDecimal(line.LoadFactor),
                Epq = ToDecimal(line.Epq),
                DistanceKm = Km(distanceM),
                TollDistanceKm = Km(tollM),
                DurationH = Hours(durationS),
                First = Stop(stops[0]),
                Last = Stop(stops[^1]),
                AsOf = stops[0].Date
            });
        }

        return lines;
    }

    /// <summary>The cost segments of one trip: the legs of each order line on the trip, in leg order.</summary>
    public static IReadOnlyList<PricingLine> OfTrip(IReadOnlyList<LegSnapshotDto> tripLegs, IReadOnlyList<PricingSnapshotDto> orders, DateOnly tripDate)
    {
        var lineFacts = orders.SelectMany(o => o.Lines.Where(l => l.LineId != null)).ToDictionary(l => l.LineId!.Value);
        var stopFacts = orders.SelectMany(o => o.Lines).SelectMany(l => l.Stops).Where(s => s.StopId != null)
            .GroupBy(s => s.StopId!.Value).ToDictionary(g => g.Key, g => g.First());

        var segments = new List<PricingLine>();
        var position = 0;
        foreach (var group in tripLegs.GroupBy(l => l.OrderLineId).OrderBy(g => g.Min(l => l.Position)))
        {
            var legs = group.OrderBy(l => l.Position).ToList();
            var first = legs[0];
            var last = legs[^1];
            lineFacts.TryGetValue(group.Key, out var facts);
            position++;

            segments.Add(new PricingLine
            {
                AnchorId = first.Id,
                Position = position,
                TransportUnitId = facts?.TransportUnitId ?? Guid.Empty,
                ProductId = facts?.ProductId,
                VehicleTypeId = facts?.VehicleTypeId,
                Quantity = facts?.Quantity ?? first.Quantity,
                Positions = facts?.Positions,
                Colli = facts?.Colli,
                WeightKg = ToDecimal(facts?.WeightKg ?? first.WeightKg),
                VolumeM3 = ToDecimal(facts?.VolumeM3),
                LoadingMeters = ToDecimal(facts?.LoadingMeters),
                LoadFactor = ToDecimal(facts?.LoadFactor),
                Epq = ToDecimal(facts?.Epq),
                DistanceKm = legs.Any(l => l.DistanceM != null) ? Km(legs.Sum(l => l.DistanceM ?? 0)) : null,
                TollDistanceKm = legs.Any(l => l.TollDistanceM != null) ? Km(legs.Sum(l => l.TollDistanceM ?? 0)) : null,
                DurationH = legs.Any(l => l.DurationS != null) ? Hours(legs.Sum(l => l.DurationS ?? 0)) : null,
                First = End(first.Origin, stopFacts),
                Last = End(last.Destination, stopFacts),
                AsOf = tripDate
            });
        }

        return segments;
    }

    /// <summary>The order-level as-of date (QP1): the earliest line date, else the order date.</summary>
    public static DateOnly AsOfOf(PricingSnapshotDto snapshot, IReadOnlyList<PricingLine> lines)
        => lines.Count == 0 ? snapshot.OrderDate : lines.Min(l => l.AsOf);

    public static Guid SyntheticId(int position)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(position).CopyTo(bytes, 0);
        bytes[15] = 0x7F;
        return new Guid(bytes);
    }

    private static PricingStop Stop(PricingStopDto stop) => new()
    {
        StopId = stop.StopId,
        RelationId = stop.RelationId,
        RegionIds = stop.GeographicalRegionIds,
        Date = stop.Date
    };

    private static PricingStop End(LegEndDto end, IReadOnlyDictionary<Guid, PricingStopDto> stopFacts) => new()
    {
        StopId = end.StopId,
        RelationId = end.RelationId,
        RegionIds = stopFacts.TryGetValue(end.StopId, out var stop) ? stop.GeographicalRegionIds : Array.Empty<Guid>(),
        Date = end.PlannedDate
    };

    private static decimal? ToDecimal(double? value) => value == null ? null : RateLine.Round4((decimal)value.Value);

    private static decimal? Km(int? metres) => metres == null ? null : RateLine.Round4(metres.Value / 1000m);

    private static decimal? Hours(int? seconds) => seconds == null ? null : RateLine.Round4(seconds.Value / 3600m);
}
