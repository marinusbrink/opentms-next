using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTms.Platform.Maintenance;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// The VROOM stop optimiser (design 68 §5). Optional: without it, or while it is down, the built-in heuristic orders the
/// stops. So not configured is Healthy and unreachable is Degraded. The probe is cached in <see cref="RoutingProbeCache"/>.
/// </summary>
public class RouteOptimizerHealthCheck : IHealthCheck
{
    public const string Name = "route-optimizer";

    private static readonly string[] Settings = [$"{RoutingIntegrationOptions.SectionName}:Vroom:BaseUrl"];

    private readonly VroomRouteOptimizer _vroom;
    private readonly RoutingProbeCache _probes;

    public RouteOptimizerHealthCheck(VroomRouteOptimizer vroom, RoutingProbeCache probes)
    {
        _vroom = vroom;
        _probes = probes;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_vroom.IsConfigured)
        {
            return HealthCheckResult.Healthy(
                "VROOM: not configured; the built-in heuristic orders the stops.",
                HealthCheckFacts.Data(HealthCheckFacts.Reasons.NotConfigured, Settings, optional: true));
        }

        var (reachable, _) = await _probes.GetOrProbeAsync(VroomRouteOptimizer.ProviderName, _vroom.ProbeAsync, cancellationToken);
        return reachable
            ? HealthCheckResult.Healthy("VROOM answered the probe.", HealthCheckFacts.Data(HealthCheckFacts.Reasons.Ok, Settings, _vroom.Host, optional: true))
            : HealthCheckResult.Degraded(
                "VROOM did not answer the probe; the built-in heuristic orders the stops.",
                data: HealthCheckFacts.Data(HealthCheckFacts.Reasons.Unreachable, Settings, _vroom.Host, optional: true));
    }
}
