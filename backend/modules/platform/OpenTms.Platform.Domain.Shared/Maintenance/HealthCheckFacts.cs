using System.Collections.Generic;
using System.Linq;

namespace OpenTms.Platform.Maintenance;

/// <summary>
/// The shared vocabulary of every health check (docs/designs/maintenance-view.md, "Components"): what a check puts in
/// the data of its result so that the Health tab can say what is wrong, what stops working and how to fix it. The
/// Health tab localizes by the check's name and its reason: <c>HealthCheck:{name}</c> (the component),
/// <c>HealthCheck:{name}:Impact</c> and <c>HealthCheck:{name}:{reason}</c>, in the resource of the module that owns the
/// check; data values fill <c>{key}</c> placeholders. Values never carry secrets: setting names and host names only.
/// </summary>
public static class HealthCheckFacts
{
    /// <summary>The tag of the checks of the system itself (database, jobs, outbox, ...).</summary>
    public const string CoreTag = "core";

    /// <summary>The tag of the checks of external services; they never take part in <c>/health/ready</c>.</summary>
    public const string ExternalTag = "external";

    /// <summary>Data key: one of <see cref="Reasons"/> or a check-specific reason.</summary>
    public const string Reason = "reason";

    /// <summary>Data key: the configuration keys that decide this component, comma-separated ("Section:Key").</summary>
    public const string Settings = "settings";

    /// <summary>Data key: the host (or database) the check talked to.</summary>
    public const string Target = "target";

    /// <summary>Data key: "true" when the system works without the component (a fallback takes over).</summary>
    public const string Optional = "optional";

    /// <summary>The data of a check result: the reason, the settings that decide it, the target and extra facts.</summary>
    public static Dictionary<string, object> Data(
        string reason,
        IEnumerable<string>? settings = null,
        string? target = null,
        bool optional = false,
        IReadOnlyDictionary<string, object>? extra = null)
    {
        var data = new Dictionary<string, object> { [Reason] = reason };
        var keys = settings?.ToList();
        if (keys is { Count: > 0 })
        {
            data[Settings] = string.Join(",", keys);
        }

        if (!string.IsNullOrWhiteSpace(target))
        {
            data[Target] = target;
        }

        if (optional)
        {
            data[Optional] = "true";
        }

        foreach (var (key, value) in extra ?? new Dictionary<string, object>())
        {
            data[key] = value;
        }

        return data;
    }

    public static class Reasons
    {
        public const string Ok = "Ok";

        /// <summary>A required setting is missing: the component is switched off.</summary>
        public const string NotConfigured = "NotConfigured";

        /// <summary>The component did not answer (network, DNS, timeout, down).</summary>
        public const string Unreachable = "Unreachable";

        /// <summary>The component answered with an error (credentials, quota, server error).</summary>
        public const string Failing = "Failing";
    }
}
