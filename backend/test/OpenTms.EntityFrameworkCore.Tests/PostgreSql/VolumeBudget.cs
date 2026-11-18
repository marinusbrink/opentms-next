using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;

namespace OpenTms.PostgreSql;

/// <summary>
/// The timing assertions of the volume tests (trait <see cref="Category"/>). A shared CI runner's speed decides a timing more than the
/// code does, so CI runs these tests with <c>OPENTMS_VOLUME_BUDGETS=report</c>: the data, the answers and the printed timings as always,
/// the budget not enforced. The deploy's performance gate runs them on a database the size of production's, enforced, and so does a
/// developer machine by default (docs/designs/76-ci-split-and-performance-gate.md).
/// </summary>
public static class VolumeBudget
{
    public const string Category = "Volume";

    public static bool Enforced { get; } = !string.Equals(Environment.GetEnvironmentVariable("OPENTMS_VOLUME_BUDGETS"), "report", StringComparison.OrdinalIgnoreCase);

    /// <summary>The warm runs a read is timed over; <see cref="MeasureAsync"/> reports their median.</summary>
    public const int WarmRuns = 3;

    /// <summary>
    /// One read timed the way every volume test times it: once cold (query compilation and cache included), then
    /// <see cref="WarmRuns"/> times warm, of which the median is the warm time held to the budget. One warm sample let a single
    /// noise spike on the gate's one-vCPU database fail a read that is otherwise far within its budget (Deploy #28: code list
    /// page 1 measured 340 ms once, 112 ms on other runs); a slow query is slow in every run, so the median still catches it.
    /// <paramref name="check"/>, when given, verifies each result, so a fast wrong answer cannot pass.
    /// </summary>
    public static async Task<(TimeSpan Cold, TimeSpan Warm)> MeasureAsync<T>(Func<Task<T>> read, Action<T>? check = null)
    {
        var cold = Stopwatch.StartNew();
        var first = await read();
        cold.Stop();
        check?.Invoke(first);

        var warm = new List<TimeSpan>(WarmRuns);
        for (var i = 0; i < WarmRuns; i++)
        {
            var watch = Stopwatch.StartNew();
            var result = await read();
            watch.Stop();
            check?.Invoke(result);
            warm.Add(watch.Elapsed);
        }

        warm.Sort();
        return (cold.Elapsed, warm[WarmRuns / 2]);
    }

    /// <summary>Strictly under the budget.</summary>
    public static void Within(TimeSpan elapsed, TimeSpan budget, string because)
    {
        if (Enforced)
        {
            elapsed.ShouldBeLessThan(budget, because);
        }
    }

    /// <summary>At most the budget.</summary>
    public static void AtMost(TimeSpan elapsed, TimeSpan budget, string because)
    {
        if (Enforced)
        {
            elapsed.ShouldBeLessThanOrEqualTo(budget, because);
        }
    }

    /// <summary>No measurement over its limit; <paramref name="over"/> names the ones that are.</summary>
    public static void None(IEnumerable<string> over, string because)
    {
        if (Enforced)
        {
            over.ToList().ShouldBeEmpty(because);
        }
    }
}
