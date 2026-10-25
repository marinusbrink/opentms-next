using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OpenTms.Integrations.CarbonFootprint;
using Shouldly;
using Xunit;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// The carbon-footprint calculator of design 60 (test risk analysis: "CO₂ allocation by tonne-km and fallback", High):
/// the function's trip total allocated by weight × road distance of each leg's stop span, equal shares when no weight is
/// known, per-leg figures that sum to the total exactly; the great-circle × CPI fallback when the function is not
/// configured or fails, with the method and factor set named on the estimate.
/// </summary>
public class CarbonFootprintCalculatorTests
{
    private static readonly GeoPoint A = new(52.3702, 4.8952);  // Amsterdam
    private static readonly GeoPoint B = new(52.0907, 5.1214);  // Utrecht
    private static readonly GeoPoint C = new(51.9225, 4.47917); // Rotterdam

    private readonly FakeRouting _routing = new();
    private readonly FakeRouteFunction _function = new();

    private CarbonFootprintCalculator Calculator(decimal cpi = 62m)
        => new(_routing, _function, Options.Create(new CarbonFootprintOptions { CpiGramPerTonneKm = cpi }));

    [Fact]
    public async Task The_functions_total_is_allocated_by_weight_times_the_road_distance_of_each_legs_span()
    {
        _function.Co2Kg = 100m;
        _routing.PairDistancesM = [40_000, 60_000]; // A→B 40 km, B→C 60 km
        var legs = new List<LegLoad>
        {
            new(10_000, 0, 1), // 10 t over A→B: 400 tkm
            new(5_000, 0, 2),  // 5 t over A→C: 500 tkm
            new(2_000, 1, 2)   // 2 t over B→C: 120 tkm
        };

        var estimate = await Calculator().EstimateTripAsync([A, B, C], VehicleSpec.Default, legs);

        estimate.TotalKg.ShouldBe(100m);
        estimate.Method.ShouldBe(Co2Estimate.MethodRouteFunction);
        estimate.FactorSet.ShouldBe(CarbonFootprintCalculator.FactorSetRouteFunction);
        estimate.PerLegKg.Count.ShouldBe(3);
        estimate.PerLegKg[0].ShouldBe(39.216m, "400 / 1020 of 100 kg");
        estimate.PerLegKg[1].ShouldBe(49.020m, "500 / 1020");
        estimate.PerLegKg[2].ShouldBe(11.764m, "the last leg takes the rounding residue so the legs sum to the total");
        estimate.PerLegKg.Sum().ShouldBe(100m);
        _function.Calls.ShouldHaveSingleItem().Points.ShouldBe([A, B, C], "the trip's stops in order");
        _function.Calls[0].Vehicle.ShouldBe(VehicleSpec.Default);
    }

    [Fact]
    public async Task Legs_without_any_weight_share_the_total_equally()
    {
        _function.Co2Kg = 10m;
        _routing.PairDistancesM = [40_000, 60_000];

        var estimate = await Calculator().EstimateTripAsync([A, B, C], null, [new LegLoad(null, 0, 1), new LegLoad(null, 0, 2), new LegLoad(null, 1, 2)]);

        estimate.PerLegKg.ShouldBe([3.333m, 3.333m, 3.334m]);
        estimate.PerLegKg.Sum().ShouldBe(10m);
    }

    [Fact]
    public async Task A_leg_without_a_weight_among_weighed_legs_gets_nothing_by_tonne_kilometre()
    {
        _function.Co2Kg = 10m;
        _routing.PairDistancesM = [40_000, 60_000];

        var estimate = await Calculator().EstimateTripAsync([A, B, C], null, [new LegLoad(1_000, 0, 1), new LegLoad(null, 1, 2)]);

        estimate.PerLegKg.ShouldBe([10m, 0m]);
    }

    [Fact]
    public async Task Without_a_routing_matrix_the_spans_are_great_circle_and_the_functions_total_is_still_allocated()
    {
        _function.Co2Kg = 50m;
        _routing.Matrix = null;

        var estimate = await Calculator().EstimateTripAsync([A, C], null, [new LegLoad(1_000, 0, 1), new LegLoad(3_000, 0, 1)]);

        estimate.Method.ShouldBe(Co2Estimate.MethodRouteFunction);
        estimate.PerLegKg.ShouldBe([12.5m, 37.5m], "same span, weights 1 : 3");
    }

    [Fact]
    public async Task When_the_function_is_not_configured_each_leg_is_great_circle_times_tonnes_times_cpi()
    {
        _function.Configured = false;

        var estimate = await Calculator(cpi: 62m).EstimateTripAsync([A, C], VehicleSpec.Default, [new LegLoad(10_000, 0, 1), new LegLoad(null, 0, 1)]);

        var km = GreatCircle.DistanceM(A, C) / 1_000d;
        km.ShouldBeInRange(56.5, 58.5, "Amsterdam–Rotterdam is about 57 km as the crow flies");
        estimate.Method.ShouldBe(Co2Estimate.MethodGreatCircleCpi);
        estimate.FactorSet.ShouldBe("CPI 62 g/tkm");
        estimate.PerLegKg[0].ShouldBe(Math.Round((decimal)(km * 10) * 62m / 1000m, 3, MidpointRounding.AwayFromZero), "10 t × km × 62 g/tkm");
        estimate.PerLegKg[1].ShouldBe(Math.Round((decimal)km * 62m / 1000m, 3, MidpointRounding.AwayFromZero), "an unknown weight counts as one tonne");
        estimate.TotalKg.ShouldBe(estimate.PerLegKg.Sum());
        _function.Calls.ShouldBeEmpty();
        _routing.MatrixCalls.ShouldBe(0, "the fallback never asks the engine");
    }

    [Fact]
    public async Task When_the_function_fails_the_fallback_answers_instead_of_nothing()
    {
        _function.Co2Kg = null;

        var estimate = await Calculator().EstimateTripAsync([A, B, C], null, [new LegLoad(2_000, 0, 2)]);

        estimate.Method.ShouldBe(Co2Estimate.MethodGreatCircleCpi);
        estimate.TotalKg.ShouldBeGreaterThan(0m);
        var spanKm = (GreatCircle.DistanceM(A, B) + GreatCircle.DistanceM(B, C)) / 1_000d;
        estimate.PerLegKg.ShouldHaveSingleItem().ShouldBe(Math.Round((decimal)(spanKm * 2) * 62m / 1000m, 3, MidpointRounding.AwayFromZero), "the span is the sum of its great-circle pairs");
    }

    [Fact]
    public void The_leg_fallback_is_synchronous_and_names_the_cpi()
    {
        var estimate = Calculator(cpi: 80.5m).EstimateLegFallback(A, C, 20_000);

        var expected = Math.Round((decimal)(GreatCircle.DistanceM(A, C) / 1_000d * 20) * 80.5m / 1000m, 3, MidpointRounding.AwayFromZero);
        estimate.TotalKg.ShouldBe(expected);
        estimate.PerLegKg.ShouldBe([expected]);
        estimate.Method.ShouldBe(Co2Estimate.MethodGreatCircleCpi);
        estimate.FactorSet.ShouldBe("CPI 80.5 g/tkm");
    }

    [Fact]
    public async Task A_trip_with_fewer_than_two_stops_is_zero_without_a_call()
    {
        var estimate = await Calculator().EstimateTripAsync([A], null, [new LegLoad(1_000, 0, 0)]);

        estimate.TotalKg.ShouldBe(0m);
        estimate.PerLegKg.ShouldBe([0m]);
        _function.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void Allocation_handles_zero_distances_as_equal_shares_and_an_empty_leg_list()
    {
        CarbonFootprintCalculator.Allocate(9m, [new LegLoad(1_000, 0, 1), new LegLoad(2_000, 0, 1)], [0d, 0d]).ShouldBe([4.5m, 4.5m]);
        CarbonFootprintCalculator.Allocate(9m, [], []).ShouldBeEmpty();
    }

    [Fact]
    public void Great_circle_distances_are_symmetric_and_zero_for_the_same_point()
    {
        GreatCircle.DistanceM(A, A).ShouldBe(0d);
        GreatCircle.DistanceM(A, C).ShouldBe(GreatCircle.DistanceM(C, A), 0.001);
        /* Paris–London, a textbook pair: about 344 km. */
        GreatCircle.DistanceM(new GeoPoint(48.8566, 2.3522), new GeoPoint(51.5074, -0.1278)).ShouldBeInRange(343_000, 345_000);
    }

    /// <summary>A routing service that answers a fixed matrix of consecutive pairs (or null) and counts calls.</summary>
    private sealed class FakeRouting : IRoutingService
    {
        public int[]? PairDistancesM { get; set; }

        public RouteMatrix? Matrix
        {
            get => PairDistancesM == null ? null : new RouteMatrix(PairDistancesM.Select(d => new RouteResult(d, d / 20, null, null, null, null, null, "Fake", DateTimeOffset.UtcNow, false)).ToList(), "Fake", DateTimeOffset.UtcNow);
            set => PairDistancesM = value?.Legs.Select(l => l.DistanceM).ToArray();
        }

        public int MatrixCalls { get; private set; }

        public Task<RouteResult?> GetRouteAsync(GeoPoint from, GeoPoint to, VehicleSpec? vehicle, RoutingOptions options, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<RouteMatrix?> GetMatrixAsync(IReadOnlyList<GeoPoint> points, VehicleSpec? vehicle, CancellationToken cancellationToken = default)
        {
            MatrixCalls++;
            return Task.FromResult(Matrix);
        }

        public Task<RoutingStatus> GetStatusAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeRouteFunction : IRouteFunction
    {
        public bool Configured { get; set; } = true;

        public decimal? Co2Kg { get; set; } = 100m;

        public List<(IReadOnlyList<GeoPoint> Points, VehicleSpec? Vehicle)> Calls { get; } = new();

        public bool IsConfigured => Configured;

        public string? Host => Configured ? "route-function.invalid" : null;

        public Task<ProviderRoute?> CalculateAsync(IReadOnlyList<GeoPoint> points, VehicleSpec? vehicle, CancellationToken cancellationToken)
        {
            Calls.Add((points, vehicle));
            return Task.FromResult(Co2Kg == null ? null : new ProviderRoute([new ProviderLeg(100_000, 5_000)], null, null, null, null, Co2Kg));
        }

        public Task<bool> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(Configured);
    }
}
