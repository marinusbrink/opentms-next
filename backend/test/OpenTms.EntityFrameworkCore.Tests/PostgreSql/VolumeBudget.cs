using System;
using System.Collections.Generic;
using System.Linq;
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
