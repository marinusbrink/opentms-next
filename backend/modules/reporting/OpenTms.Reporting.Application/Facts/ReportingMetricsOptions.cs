using System;

namespace OpenTms.Reporting.Facts;

/// <summary>The switches of the fact tables and the metric query ("Reporting:Metrics"; design 75).</summary>
public class ReportingMetricsOptions
{
    public const string SectionName = "Reporting:Metrics";

    /// <summary>The per-instance switch of both workers; the work is idempotent, so every instance may run them.</summary>
    public bool WorkersEnabled { get; set; } = true;

    /// <summary>One backfill pass over every tenant per period.</summary>
    public TimeSpan BackfillPeriod { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Records of each grain one tenant's pass requests.</summary>
    public int BackfillBatchSize { get; set; } = 1_000;

    /// <summary>
    /// How long after the position ran out the fact counts may still differ from the sources' before the sweep starts again
    /// (without clearing): a calculation that failed for good is otherwise never asked for again.
    /// </summary>
    public TimeSpan BackfillResweepAfter { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How often the repair worker looks whether a tenant is due. Short on purpose: a deploy restarts this timer.</summary>
    public TimeSpan RepairCheckPeriod { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>A tenant is due this long after its last repair, which a setting remembers across deploys.</summary>
    public TimeSpan RepairInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Records modified this long ago or later are requested again.</summary>
    public TimeSpan RepairWindow { get; set; } = TimeSpan.FromHours(48);

    /// <summary>A query that takes longer is cancelled in PostgreSQL and answers <c>timeout</c> (budget: never block a request over 2 s).</summary>
    public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromSeconds(2);
}
