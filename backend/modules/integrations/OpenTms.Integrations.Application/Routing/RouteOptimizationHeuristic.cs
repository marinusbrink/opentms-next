using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// The built-in stop optimiser of design 68 §5: nearest neighbour from every admissible start, then 2-opt, on any cost
/// function (routed seconds or great-circle metres). Precedence pairs (pickup before delivery) are honoured throughout: a
/// stop is admissible only after all its predecessors, and a 2-opt reversal that would put a pair inside the reversed
/// segment is skipped. The input order is improved by 2-opt as well and wins ties, so the result is never worse than
/// what the planner had. Deterministic: ties go to the lowest index. Pure; no I/O.
/// </summary>
public static class RouteOptimizationHeuristic
{
    /// <summary>The ceiling of 2-opt passes; each pass is O(n² · n) at n ≤ 100 stops.</summary>
    public const int MaxPasses = 100;

    private const double Epsilon = 1e-9;

    /// <param name="count">The number of stops (indices 0..count-1; the input order is 0, 1, 2, …).</param>
    /// <param name="cost">The cost of driving from stop i to stop j.</param>
    /// <param name="fromStart">The cost from the fixed start to stop i; null = the route may start at any stop.</param>
    /// <param name="toEnd">The cost from stop i to the fixed end; null = the route may end at any stop.</param>
    /// <param name="precedences">Index pairs whose first must come before the second.</param>
    /// <returns>The optimised order and its cost, or null when the precedences contain a cycle (no feasible order).</returns>
    public static (int[] Order, double Cost)? Solve(
        int count,
        Func<int, int, double> cost,
        Func<int, double>? fromStart,
        Func<int, double>? toEnd,
        IReadOnlyList<(int Before, int After)> precedences)
    {
        if (count <= 0)
        {
            return ([], 0);
        }

        var pairs = precedences.Where(p => p.Before != p.After && p.Before >= 0 && p.After >= 0 && p.Before < count && p.After < count).Distinct().ToList();
        var predecessors = new List<int>[count];
        for (var i = 0; i < count; i++)
        {
            predecessors[i] = new List<int>();
        }

        foreach (var (before, after) in pairs)
        {
            predecessors[after].Add(before);
        }

        double RouteCost(int[] order)
        {
            var total = fromStart?.Invoke(order[0]) ?? 0;
            for (var k = 0; k + 1 < order.Length; k++)
            {
                total += cost(order[k], order[k + 1]);
            }

            return total + (toEnd?.Invoke(order[^1]) ?? 0);
        }

        var candidates = new List<int[]>();
        var input = Enumerable.Range(0, count).ToArray();
        if (IsFeasible(input, pairs))
        {
            candidates.Add(input);
        }

        if (fromStart != null)
        {
            if (NearestNeighbour(count, cost, predecessors, first: null, fromStart) is { } route)
            {
                candidates.Add(route);
            }
        }
        else
        {
            for (var first = 0; first < count; first++)
            {
                if (predecessors[first].Count == 0 && NearestNeighbour(count, cost, predecessors, first, null) is { } route)
                {
                    candidates.Add(route);
                }
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        int[]? best = null;
        var bestCost = double.MaxValue;
        foreach (var candidate in candidates)
        {
            var improved = TwoOpt(candidate, pairs, RouteCost);
            var improvedCost = RouteCost(improved);
            if (best == null || improvedCost < bestCost - Epsilon)
            {
                best = improved;
                bestCost = improvedCost;
            }
        }

        return (best!, bestCost);
    }

    /// <summary>Great-circle distance in metres (haversine, mean earth radius).</summary>
    public static double GreatCircleM(GeoPoint a, GeoPoint b)
    {
        const double r = 6_371_000;
        var dLat = Radians(b.Latitude - a.Latitude);
        var dLon = Radians(b.Longitude - a.Longitude);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(Radians(a.Latitude)) * Math.Cos(Radians(b.Latitude)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;

    private static bool IsFeasible(int[] order, List<(int Before, int After)> pairs)
    {
        var position = Positions(order);
        return pairs.All(p => position[p.Before] < position[p.After]);
    }

    private static int[] Positions(int[] order)
    {
        var position = new int[order.Length];
        for (var k = 0; k < order.Length; k++)
        {
            position[order[k]] = k;
        }

        return position;
    }

    /// <summary>From <paramref name="first"/> (or the fixed start), always the cheapest admissible unvisited stop next.</summary>
    private static int[]? NearestNeighbour(int count, Func<int, int, double> cost, List<int>[] predecessors, int? first, Func<int, double>? fromStart)
    {
        var visited = new bool[count];
        var order = new List<int>(count);
        if (first != null)
        {
            visited[first.Value] = true;
            order.Add(first.Value);
        }

        while (order.Count < count)
        {
            var next = -1;
            var nextCost = double.MaxValue;
            for (var j = 0; j < count; j++)
            {
                if (visited[j] || predecessors[j].Any(p => !visited[p]))
                {
                    continue;
                }

                var c = order.Count == 0 ? fromStart?.Invoke(j) ?? 0 : cost(order[^1], j);
                if (c < nextCost - Epsilon)
                {
                    next = j;
                    nextCost = c;
                }
            }

            if (next < 0)
            {
                /* Every remaining stop waits for another remaining stop: the precedences form a cycle. */
                return null;
            }

            visited[next] = true;
            order.Add(next);
        }

        return order.ToArray();
    }

    /// <summary>Reverses segments while that lowers the cost and keeps every precedence pair outside the reversed segment.</summary>
    private static int[] TwoOpt(int[] start, List<(int Before, int After)> pairs, Func<int[], double> routeCost)
    {
        var order = (int[])start.Clone();
        var current = routeCost(order);
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var improved = false;
            for (var i = 0; i < order.Length - 1; i++)
            {
                for (var j = i + 1; j < order.Length; j++)
                {
                    var position = Positions(order);
                    if (pairs.Any(p => position[p.Before] >= i && position[p.After] <= j && position[p.Before] <= j && position[p.After] >= i))
                    {
                        continue;
                    }

                    Array.Reverse(order, i, j - i + 1);
                    var candidate = routeCost(order);
                    if (candidate < current - Epsilon)
                    {
                        current = candidate;
                        improved = true;
                    }
                    else
                    {
                        Array.Reverse(order, i, j - i + 1);
                    }
                }
            }

            if (!improved)
            {
                break;
            }
        }

        return order;
    }
}
