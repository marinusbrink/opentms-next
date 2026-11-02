using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTms.Platform.Maintenance;
using Volo.Abp.DependencyInjection;

namespace OpenTms.HealthChecks;

/// <summary>
/// The distributed cache is in memory, per instance, until Redis exists (AP-13). With one instance that is fine; with
/// more, a change of settings, features or permissions saved on one instance reaches the others only when their entry
/// expires: Degraded, with the instance count. A deploy's canary runs two revisions briefly and shows it too.
/// </summary>
public class CacheHealthCheck : IHealthCheck, ITransientDependency
{
    public const string Name = "cache";

    public const string PerInstance = "PerInstance";

    private readonly IInstanceFleet _fleet;

    public CacheHealthCheck(IInstanceFleet fleet)
    {
        _fleet = fleet;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var running = (await _fleet.GetRunningAsync(cancellationToken))?.Count ?? 1;
        var extra = new Dictionary<string, object> { ["instances"] = running };
        return running > 1
            ? HealthCheckResult.Degraded(
                $"cache: in memory per instance while {running} instances run.",
                data: HealthCheckFacts.Data(PerInstance, extra: extra))
            : HealthCheckResult.Healthy(
                "cache: in memory, one instance.",
                HealthCheckFacts.Data(HealthCheckFacts.Reasons.Ok, optional: true, extra: extra));
    }
}
