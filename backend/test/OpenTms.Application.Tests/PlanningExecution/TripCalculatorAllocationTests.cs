using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Published;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using Shouldly;
using Xunit;

namespace OpenTms.PlanningExecution;

/// <summary>
/// The pure parts of TripCalculator (design 59 "Calculators" (b) and (e); AD-20; pricing scenarios 62–69 as the design
/// corrects them): cost over the legs by distance share, revenue per order by the order's leg kilometres, the
/// cent-exact split, and the load factor against the vehicles' capacity. Risk class: Critical (money).
/// </summary>
public class TripCalculatorAllocationTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Unit = Guid.NewGuid();

    private static Leg LegOf(Guid orderId, Guid? tripId, int? distanceM, double? weightKg = 1000, double? loadingMeters = null, double? epq = null)
    {
        var end = new LegEndSnapshot(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "X", null, "NL", 52, 5, null, new DateOnly(2026, 10, 26), null, null, null, null);
        var leg = new Leg(Guid.NewGuid(), Tenant, orderId, Guid.NewGuid(), Guid.NewGuid(), 1, LegSource.Generated, end, end with { StopId = Guid.NewGuid() },
            new LegQuantities(1, weightKg, null, loadingMeters, epq, null, Unit, null, null, []));
        if (tripId != null)
        {
            leg.AssignTo(tripId.Value, 1);
        }

        if (distanceM != null)
        {
            leg.SetRoute(distanceM.Value, distanceM.Value / 20, null, "Fake", DateTimeOffset.UtcNow);
        }

        return leg;
    }

    private static Dictionary<Guid, RateLineTotalsDto> Totals(params (Guid OrderId, decimal Amount)[] totals)
        => totals.ToDictionary(t => t.OrderId, t => new RateLineTotalsDto { AnchorId = t.OrderId, TotalAmount = t.Amount, CalculationVersion = 1 });

    [Fact]
    public void Scenario_65_a_shared_trip_allocates_its_cost_by_leg_kilometres()
    {
        var trip = Guid.NewGuid();
        var orderA = Guid.NewGuid();
        var orderB = Guid.NewGuid();
        var a = LegOf(orderA, trip, 100_000);
        var b = LegOf(orderB, trip, 300_000);

        var result = TripCalculator.Allocate([a, b], 400m, Totals(), [a, b]);

        result[a.Id].Cost.ShouldBe(100m, "100 of 400 km");
        result[b.Id].Cost.ShouldBe(300m);
    }

    [Fact]
    public void Without_distances_the_cost_is_split_in_equal_shares_and_stays_cent_exact()
    {
        var trip = Guid.NewGuid();
        var legs = Enumerable.Range(0, 3).Select(_ => LegOf(Guid.NewGuid(), trip, null)).ToList();

        var result = TripCalculator.Allocate(legs, 100m, Totals(), legs);

        var costs = legs.Select(l => result[l.Id].Cost!.Value).ToList();
        costs.Sum().ShouldBe(100m);
        costs.ShouldAllBe(c => c == 33.33m || c == 33.34m);
        costs.Count(c => c == 33.34m).ShouldBe(1);
    }

    [Fact]
    public void Scenario_66_the_trips_revenue_is_the_sum_of_each_legs_share_of_its_orders_revenue()
    {
        var trip = Guid.NewGuid();
        var orderA = Guid.NewGuid();
        var orderB = Guid.NewGuid();
        var a = LegOf(orderA, trip, 100_000);
        var b = LegOf(orderB, trip, 200_000);

        var result = TripCalculator.Allocate([a, b], null, Totals((orderA, 500m), (orderB, 900m)), [a, b]);

        result[a.Id].Revenue.ShouldBe(500m, "the trip holds the order's only leg");
        result[b.Id].Revenue.ShouldBe(900m);
        result[a.Id].Cost.ShouldBeNull("no trip cost known");
    }

    [Fact]
    public void An_order_with_legs_on_two_trips_gives_each_trip_its_kilometre_share()
    {
        var thisTrip = Guid.NewGuid();
        var otherTrip = Guid.NewGuid();
        var order = Guid.NewGuid();
        var here = LegOf(order, thisTrip, 150_000);
        var elsewhere = LegOf(order, otherTrip, 50_000);
        var unplanned = LegOf(order, null, 100_000);

        var result = TripCalculator.Allocate([here], null, Totals((order, 300m)), [here, elsewhere, unplanned]);

        result[here.Id].Revenue.ShouldBe(150m, "150 of the order's 300 km");
    }

    [Fact]
    public void Scenario_67_the_full_order_revenue_when_the_trip_holds_all_its_legs_and_none_has_a_distance()
    {
        var trip = Guid.NewGuid();
        var order = Guid.NewGuid();
        var a = LegOf(order, trip, null);
        var b = LegOf(order, trip, null);

        var result = TripCalculator.Allocate([a, b], null, Totals((order, 250m)), [a, b]);

        (result[a.Id].Revenue!.Value + result[b.Id].Revenue!.Value).ShouldBe(250m);
        result[a.Id].Revenue.ShouldBe(125m);
    }

    [Fact]
    public void Scenario_68_corrected_two_orders_fully_on_one_trip_without_distances_each_keep_their_full_revenue()
    {
        var trip = Guid.NewGuid();
        var orderA = Guid.NewGuid();
        var orderB = Guid.NewGuid();
        var a = LegOf(orderA, trip, null);
        var b = LegOf(orderB, trip, null);

        var result = TripCalculator.Allocate([a, b], null, Totals((orderA, 100m), (orderB, 200m)), [a, b]);

        result[a.Id].Revenue.ShouldBe(100m, "design 59 corrects OpenTMS's Count == 1 quirk: per order, not per trip");
        result[b.Id].Revenue.ShouldBe(200m);
    }

    [Fact]
    public void An_order_partly_elsewhere_without_distances_gets_nothing_here_and_a_leg_without_distance_among_routed_legs_gets_nothing()
    {
        var thisTrip = Guid.NewGuid();
        var order = Guid.NewGuid();
        var here = LegOf(order, thisTrip, null);
        var elsewhere = LegOf(order, Guid.NewGuid(), null);
        TripCalculator.Allocate([here], null, Totals((order, 300m)), [here, elsewhere])[here.Id].Revenue.ShouldBe(0m);

        var routed = LegOf(order, thisTrip, 100_000);
        var unrouted = LegOf(order, thisTrip, null);
        var result = TripCalculator.Allocate([routed, unrouted], null, Totals((order, 300m)), [routed, unrouted]);
        result[routed.Id].Revenue.ShouldBe(300m);
        result[unrouted.Id].Revenue.ShouldBe(0m);
    }

    [Fact]
    public void Scenario_64_an_order_alone_on_a_trip_carries_the_whole_trip_cost()
    {
        var trip = Guid.NewGuid();
        var order = Guid.NewGuid();
        var a = LegOf(order, trip, 120_000);
        var b = LegOf(order, trip, 80_000);

        var result = TripCalculator.Allocate([a, b], 280m, Totals((order, 400m)), [a, b]);

        (result[a.Id].Cost!.Value + result[b.Id].Cost!.Value).ShouldBe(280m, "scenario 62: €1.10 + €0.30 per km over 200 km");
        result[a.Id].Cost.ShouldBe(168m);
        result[b.Id].Cost.ShouldBe(112m);
        result[a.Id].Revenue.ShouldBe(240m);
        result[b.Id].Revenue.ShouldBe(160m);
    }

    [Fact]
    public void An_order_without_rate_lines_leaves_its_legs_revenue_null()
    {
        var trip = Guid.NewGuid();
        var a = LegOf(Guid.NewGuid(), trip, 1000);
        TripCalculator.Allocate([a], null, Totals(), [a])[a.Id].Revenue.ShouldBeNull();
    }

    [Theory]
    [InlineData("100", "1,1,1", "33.34,33.33,33.33")]
    [InlineData("10", "1,2", "3.33,6.67")]
    [InlineData("0.03", "1,1,1,1", "0.01,0.01,0.01,0")]
    [InlineData("-100", "1,1,1", "-33.34,-33.33,-33.33")]
    [InlineData("100", "0,0", "0,0")]
    [InlineData("100", "0,5", "0,100")]
    public void Split_is_cent_exact_by_the_largest_remainder(string total, string weights, string expected)
    {
        var parts = TripCalculator.Split(decimal.Parse(total, System.Globalization.CultureInfo.InvariantCulture),
            weights.Split(',').Select(w => decimal.Parse(w, System.Globalization.CultureInfo.InvariantCulture)).ToList());

        parts.ShouldBe(expected.Split(',').Select(e => decimal.Parse(e, System.Globalization.CultureInfo.InvariantCulture)).ToList());
    }

    [Fact]
    public void Scenario_42_the_load_factor_is_the_worst_ratio_against_the_vehicles_summed_capacity_and_warns_when_exceeded()
    {
        var trip = new TransportTrip(Guid.NewGuid(), Tenant, "TR-1", Guid.NewGuid(), new DateOnly(2026, 10, 26), TripExecution.OwnFleet,
            StopCompositionMode.MergeByLocationAndActivity, FirstLastStopRule.None, DateTimeOffset.UtcNow);
        var legs = new List<Leg>
        {
            LegOf(Guid.NewGuid(), trip.Id, 1000, weightKg: 8000, loadingMeters: 6.0, epq: 10),
            LegOf(Guid.NewGuid(), trip.Id, 1000, weightKg: 4000, loadingMeters: 4.0, epq: 10)
        };
        var vehicles = new List<MasterDataVehicleReferenceDto>
        {
            new() { Id = Guid.NewGuid(), Name = "Truck", Kind = VehicleTypeKind.Tractor, MaxPayloadKg = 10_000 },
            new() { Id = Guid.NewGuid(), Name = "Trailer", Kind = VehicleTypeKind.Trailer, MaxEpq = 33, MaxLoadingMeters = 13.6, MaxPayloadKg = 20_000 }
        };
        var warnings = new List<TripWarning>();

        TripCalculator.CalculateLoad(trip, legs, vehicles, warnings);

        trip.LoadingMeters.ShouldBe(10.0);
        trip.LoadFactor.ShouldBe(0.7353m, "10 of 13.6 loading metres is the worst ratio (20 of 33 EPQ, 12 of 30 t)");
        warnings.ShouldBeEmpty();

        legs.Add(LegOf(Guid.NewGuid(), trip.Id, 1000, weightKg: 1000, loadingMeters: 4.0));
        TripCalculator.CalculateLoad(trip, legs, vehicles, warnings);
        trip.LoadFactor.ShouldBe(1.0294m);
        warnings.ShouldHaveSingleItem().ShouldBe(new TripWarning(TripWarningCode.CapacityExceeded, null, null, "loadingMeters"));

        TripCalculator.CalculateLoad(trip, legs, [], warnings);
        trip.LoadFactor.ShouldBeNull("a trip without vehicles has no load factor (design 59 assumption 2)");
        trip.LoadingMeters.ShouldBe(14.0);
    }
}
