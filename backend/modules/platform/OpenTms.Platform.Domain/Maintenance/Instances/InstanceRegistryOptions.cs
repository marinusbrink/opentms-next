using System;

namespace OpenTms.Platform.Maintenance.Instances;

/// <summary>Bound from the <c>InstanceRegistry</c> configuration section (see appsettings.json of the host).</summary>
public class InstanceRegistryOptions
{
    /// <summary>Per-instance switch: whether this process registers itself and heartbeats.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The role this process runs (AD-11). Today every process is the Api; the role split selects it
    /// by environment variable (proposal AP-12), which lands here through configuration.
    /// </summary>
    public string Role { get; set; } = "Api";

    public TimeSpan HeartbeatPeriod { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>After this much silence an instance without a graceful stop is presumed dead (default three heartbeats).</summary>
    public TimeSpan UnresponsiveAfter { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>Stopped and unresponsive instances older than this are removed by the heartbeat's housekeeping.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);
}
