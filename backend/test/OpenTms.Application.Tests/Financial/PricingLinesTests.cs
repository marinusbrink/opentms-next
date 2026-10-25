using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Financial.Tariffs.Calculators;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.PlanningExecution.Published;
using Shouldly;
using Xunit;

namespace OpenTms.Financial;

/// <summary>
/// The mapping of the other modules' snapshots onto the resolver's lines (design 57 "OrderRateLineCalculator",
/// "TripRateLineCalculator"; pricing.md scenarios 57–58: the segment of one order line on one trip takes the summed leg
/// distances and the legs' own distance when nothing better is known). Pure; risk class Critical (cost inputs).
/// </summary>
public class PricingLinesTests
{
    private static readonly Guid OrderId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid Ams = Guid.NewGuid();
    private static readonly Guid Depot = Guid.NewGuid();
    private static readonly Guid Bru = Guid.NewGuid();
    private static readonly Guid Be = Guid.NewGuid();
    private static readonly DateOnly Monday = new(2026, 10, 26);

    private static PricingStopDto Stop(int position, Guid relation, Guid? stopId = null, params Guid[] regions)
        => new() { StopId = stopId ?? Guid.NewGuid(), Position = position, RelationId = relation, GeographicalRegionIds = regions.ToList(), Date = Monday.AddDays(position - 1) };

    private static LegSnapshotDto Leg(Guid lineId, int position, PricingStopDto origin, PricingStopDto destination, int? distanceM, int? tollM = null, Guid? tripId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            OrderId = OrderId,
            OrderLineId = lineId,
            Position = position,
            Origin = new LegEndDto { StopId = origin.StopId!.Value, RelationId = origin.RelationId, PlannedDate = origin.Date },
            Destination = new LegEndDto { StopId = destination.StopId!.Value, RelationId = destination.RelationId, PlannedDate = destination.Date },
            DistanceM = distanceM,
            TollDistanceM = tollM,
            TripId = tripId,
            Quantity = 2m,
            WeightKg = 400
        };

    [Fact]
    public void An_order_line_takes_its_first_and_last_stop_the_legs_distances_and_the_first_stops_date()
    {
        var lineId = Guid.NewGuid();
        var load = Stop(1, Ams);
        var via = Stop(2, Depot);
        var unload = Stop(3, Bru, regions: Be);
        var snapshot = new PricingSnapshotDto
        {
            OrderId = OrderId,
            OrderDate = Monday.AddDays(-3),
            Lines = [new PricingLineDto { LineId = lineId, Position = 1, TransportUnitId = Guid.NewGuid(), Quantity = 3m, WeightKg = 1250.5, DistanceM = 999_000, Stops = [unload, load, via] }]
        };
        var legs = new[] { Leg(lineId, 1, load, via, 100_000, 5_000), Leg(lineId, 2, via, unload, 150_500, null) };

        var line = PricingLines.OfOrder(snapshot, legs).ShouldHaveSingleItem();

        line.AnchorId.ShouldBe(lineId);
        line.First.RelationId.ShouldBe(Ams);
        line.Last.RelationId.ShouldBe(Bru);
        line.Last.RegionIds.ShouldBe([Be]);
        line.AsOf.ShouldBe(Monday);
        line.DistanceKm.ShouldBe(250.5m, "the legs' distances, not the snapshot's");
        line.TollDistanceKm.ShouldBe(5m);
        line.WeightKg.ShouldBe(1250.5m);
        PricingLines.AsOfOf(snapshot, [line]).ShouldBe(Monday);
    }

    [Fact]
    public void Without_routed_legs_the_snapshot_distance_is_used_and_an_unsaved_line_gets_a_synthetic_anchor()
    {
        var snapshot = new PricingSnapshotDto
        {
            OrderDate = Monday,
            Lines = [new PricingLineDto { LineId = null, Position = 2, TransportUnitId = Guid.NewGuid(), Quantity = 1m, DistanceM = 42_000, Stops = [Stop(1, Ams), Stop(2, Bru)] }]
        };

        var line = PricingLines.OfOrder(snapshot, []).ShouldHaveSingleItem();

        line.DistanceKm.ShouldBe(42m);
        line.AnchorId.ShouldBe(PricingLines.SyntheticId(2));
        PricingLines.SyntheticId(2).ShouldNotBe(PricingLines.SyntheticId(3));
        PricingLines.AsOfOf(snapshot, []).ShouldBe(Monday, "no lines: the order date");
    }

    [Fact]
    public void Scenario_57_58_a_segment_spans_the_lines_legs_on_the_trip_with_their_summed_distances_anchored_to_the_first_leg()
    {
        var lineId = Guid.NewGuid();
        var load = Stop(1, Ams);
        var via = Stop(2, Depot);
        var unload = Stop(3, Bru, regions: Be);
        var unitId = Guid.NewGuid();
        var orders = new[]
        {
            new PricingSnapshotDto { OrderId = OrderId, Lines = [new PricingLineDto { LineId = lineId, Position = 1, TransportUnitId = unitId, Quantity = 5m, WeightKg = 900, Stops = [load, via, unload] }] }
        };
        var first = Leg(lineId, 1, load, via, 100_000, tripId: TripId);
        var second = Leg(lineId, 2, via, unload, 200_000, tripId: TripId);
        var otherLine = Guid.NewGuid();
        var other = Leg(otherLine, 1, load, unload, null, tripId: TripId);

        var segments = PricingLines.OfTrip([second, first, other], orders, Monday);

        segments.Count.ShouldBe(2);
        var segment = segments.Single(s => s.AnchorId == first.Id);
        segment.First.RelationId.ShouldBe(Ams);
        segment.Last.RelationId.ShouldBe(Bru);
        segment.Last.RegionIds.ShouldBe([Be], "regions come from the order's stop snapshot");
        segment.DistanceKm.ShouldBe(300m);
        segment.Quantity.ShouldBe(5m);
        segment.WeightKg.ShouldBe(900m);
        segment.TransportUnitId.ShouldBe(unitId);
        segment.AsOf.ShouldBe(Monday, "cost prices on the trip date (QP1)");

        var unknown = segments.Single(s => s.AnchorId == other.Id);
        unknown.DistanceKm.ShouldBeNull("a leg without a distance gives no segment distance");
        unknown.Quantity.ShouldBe(2m, "the leg's own quantity when the order snapshot does not know the line");
        unknown.WeightKg.ShouldBe(400m);
    }
}
