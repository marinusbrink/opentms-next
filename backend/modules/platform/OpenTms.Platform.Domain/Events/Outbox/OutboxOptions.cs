using System;

namespace OpenTms.Platform.Events.Outbox;

/// <summary>Bound from the <c>Outbox</c> configuration section (see appsettings.json of the host).</summary>
public class OutboxOptions
{
    /// <summary>
    /// Per-instance switch: whether this process runs the outbox dispatcher. Today one host does
    /// everything; after the role split (AD-11) only the Jobs role keeps it on.
    /// </summary>
    public bool DispatchEnabled { get; set; } = true;

    /// <summary>How often the dispatcher sweeps every database as a safety net behind the commit signal.</summary>
    public TimeSpan SweepPeriod { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How many waiting events one database visit reads per round trip.</summary>
    public int BatchSize { get; set; } = 100;
}
