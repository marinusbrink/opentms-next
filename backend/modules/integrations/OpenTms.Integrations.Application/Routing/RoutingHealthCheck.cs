using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// The routing provider's reachability as a health check (design 60, "API contract"), registered like the
/// pdfgeneratorapi one: tagged <see cref="Tag"/>, so it stays out of <c>/health/ready</c> and the polled health endpoint
/// and runs on demand in the host's Maintenance → Health tab. Not configured is Degraded, not Unhealthy; the probe is the
/// cached one of <see cref="RoutingProbeCache"/>.
/// </summary>
public class RoutingHealthCheck : IHealthCheck
{
    public const string Name = "routing";

    /// <summary>Checks of external services: excluded from the probes and the polled health endpoint.</summary>
    public const string Tag = "external";

    private readonly IRoutingService _routing;

    public RoutingHealthCheck(IRoutingService routing)
    {
        _routing = routing;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = await _routing.GetStatusAsync(cancellationToken);
        if (!status.Configured)
        {
            return HealthCheckResult.Degraded($"routing: provider {status.Provider} is not configured (Integrations:Routing).");
        }

        return status.Reachable
            ? HealthCheckResult.Healthy($"routing: {status.Provider} answered the probe.")
            : HealthCheckResult.Unhealthy($"routing: {status.Provider} did not answer the probe.");
    }
}
