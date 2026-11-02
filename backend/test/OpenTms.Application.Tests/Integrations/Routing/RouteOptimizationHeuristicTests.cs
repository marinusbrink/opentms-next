using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace OpenTms.Integrations.Routing;

/* Design 68 §5 (stop optimisation), the pure parts: the built-in heuristic honours precedence pairs (pickup before
 * delivery), keeps the fixed start and end, never returns a worse order than the input, refuses a precedence cycle,
 * and the optimiser answers with the great-circle heuristic when neither VROOM nor a routing provider is configured.
 * Risk class: High (planning correctness). */
public class RouteOptimizationHeuristicTests
{
    /* Points on one line of latitude: the cost is the longitude difference, so the optimum is obvious. */
    private static Func<int, int, double> Line(double[] longitudes) => (i, j) => Math.Abs(longitudes[i] - longitudes[j]);

    private static double RouteCost(int[] order, double[] lon, double? start, double? end)
    {
        var cost = 0d;
        if (start != null)
        {
            cost += Math.Abs(start.Value - lon[order[0]]);
        }

        for (var i = 0; i + 1 < order.Length; i++)
        {
            cost += Math.Abs(lon[order[i]] - lon[order[i + 1]]);
        }

        if (end != null)
        {
            cost += Math.Abs(lon[order[^1]] - end.Value);
        }

        return cost;
    }

    [Fact]
    public void Without_precedences_the_stops_are_sequenced_along_the_line_from_the_fixed_start_to_the_fixed_end()
    {
        var lon = new[] { 6.0, 4.0, 5.0 };
        var solved = RouteOptimizationHeuristic.Solve(3, Line(lon), i => Math.Abs(3.0 - lon[i]), i => Math.Abs(lon[i] - 7.0), []).ShouldNotBeNull();

        solved.Order.ShouldBe([1, 2, 0]);
        solved.Cost.ShouldBe(4.0, 1e-9);
    }

    [Fact]
    public void A_precedence_pair_is_honoured_even_when_the_unconstrained_optimum_breaks_it()
    {
        /* Stop 0 (lon 6) is a pickup whose delivery is stop 1 (lon 4): the optimum 1, 2, 0 is forbidden. */
        var lon = new[] { 6.0, 4.0, 5.0 };
        var solved = RouteOptimizationHeuristic.Solve(3, Line(lon), i => Math.Abs(3.0 - lon[i]), i => Math.Abs(lon[i] - 7.0), [(0, 1)]).ShouldNotBeNull();

        Array.IndexOf(solved.Order, 0).ShouldBeLessThan(Array.IndexOf(solved.Order, 1));
        solved.Order.OrderBy(i => i).ToArray().ShouldBe(new[] { 0, 1, 2 }, "every stop exactly once");
        solved.Cost.ShouldBe(RouteCost(solved.Order, lon, 3.0, 7.0), 1e-9);
    }

    [Fact]
    public void Several_precedences_and_an_open_route_are_honoured_and_never_worse_than_the_input_order()
    {
        var random = new Random(68);
        for (var round = 0; round < 25; round++)
        {
            var count = random.Next(4, 12);
            var lon = Enumerable.Range(0, count).Select(_ => random.NextDouble() * 10).ToArray();
            var pairs = new List<(int, int)>();
            for (var p = 0; p + 1 < count; p += 3)
            {
                pairs.Add((p, p + 1));
            }

            var solved = RouteOptimizationHeuristic.Solve(count, Line(lon), null, null, pairs).ShouldNotBeNull();
            solved.Order.OrderBy(i => i).ToArray().ShouldBe(Enumerable.Range(0, count).ToArray());
            foreach (var (before, after) in pairs)
            {
                Array.IndexOf(solved.Order, before).ShouldBeLessThan(Array.IndexOf(solved.Order, after), $"round {round}: {before} before {after}");
            }

            /* The input order 0..n-1 honours the pairs (each before its successor), so the answer may not cost more. */
            solved.Cost.ShouldBeLessThanOrEqualTo(RouteCost(Enumerable.Range(0, count).ToArray(), lon, null, null) + 1e-9, $"round {round}");
            solved.Cost.ShouldBe(RouteCost(solved.Order, lon, null, null), 1e-9);
        }
    }

    [Fact]
    public void A_precedence_cycle_has_no_feasible_order()
    {
        RouteOptimizationHeuristic.Solve(3, Line([1, 2, 3]), null, null, [(0, 1), (1, 2), (2, 0)]).ShouldBeNull();
    }

    private static RouteOptimizer Unconfigured()
    {
        var options = Options.Create(new RoutingIntegrationOptions());
        return new RouteOptimizer([], new VroomRouteOptimizer(new HttpClient(), options), options);
    }

    private static RouteOptimizationStop Stop(Guid id, double longitude) => new(id, new GeoPoint(52.0, longitude), null, null, 15);

    [Fact]
    public async Task Without_VROOM_and_without_routing_the_optimiser_answers_with_the_great_circle_heuristic()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var result = (await Unconfigured().OptimizeAsync(new RouteOptimizationRequest(
            new GeoPoint(52.0, 3.0), new GeoPoint(52.0, 7.0), [Stop(a, 6.0), Stop(b, 4.0), Stop(c, 5.0)], []))).ShouldNotBeNull();

        result.Method.ShouldBe(RouteOptimizationResult.MethodHeuristicGreatCircle);
        result.StopIds.ShouldBe([b, c, a]);
        result.Cost.ShouldNotBeNull().ShouldBeGreaterThan(0);

        var constrained = (await Unconfigured().OptimizeAsync(new RouteOptimizationRequest(
            new GeoPoint(52.0, 3.0), new GeoPoint(52.0, 7.0), [Stop(a, 6.0), Stop(b, 4.0), Stop(c, 5.0)], [new RouteOptimizationPrecedence(a, b)]))).ShouldNotBeNull();
        constrained.StopIds.ToList().IndexOf(a).ShouldBeLessThan(constrained.StopIds.ToList().IndexOf(b));
    }

    [Fact]
    public async Task Fewer_than_two_stops_is_nothing_to_optimise_and_more_than_the_maximum_is_refused()
    {
        (await Unconfigured().OptimizeAsync(new RouteOptimizationRequest(null, null, [Stop(Guid.NewGuid(), 5)], []))).ShouldBeNull();
        var tooMany = Enumerable.Range(0, RouteOptimizationRequest.MaxStops + 1).Select(i => Stop(Guid.NewGuid(), i * 0.01)).ToList();
        await Should.ThrowAsync<ArgumentException>(() => Unconfigured().OptimizeAsync(new RouteOptimizationRequest(null, null, tooMany, [])));
    }
}
