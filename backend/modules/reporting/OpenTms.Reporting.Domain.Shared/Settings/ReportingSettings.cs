namespace OpenTms.Reporting.Settings;

/// <summary>The per-tenant state of the fact tables' workers (design 75 "Migration strategy"); never visible to clients.</summary>
public static class ReportingSettings
{
    private const string Prefix = "Reporting.Metrics.";

    /// <summary>When this tenant's backfill finished. Until it is set, <c>query_metrics</c> answers <c>not_ready</c>.</summary>
    public const string BackfillCompletedAt = Prefix + "BackfillCompletedAt";

    /// <summary>How far this tenant's backfill got. Its absence means the next pass is a first pass, which clears the facts.</summary>
    public const string BackfillPosition = Prefix + "BackfillPosition";

    /// <summary>What a backfill that ran out of sweeps left open; the daily repair recounts it. Its absence means nothing is on record.</summary>
    public const string FactsMissing = Prefix + "FactsMissing";

    /// <summary>When the daily repair last finished for this tenant: a timer restarted by a deploy forgets it (design 75 build notes).</summary>
    public const string RepairLastRunAt = Prefix + "RepairLastRunAt";
}
