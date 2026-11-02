using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTms.Platform.Maintenance;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// The route function (design 60, "Carbon footprint") on its own: the CO₂ calculation calls it whatever the routing
/// provider is, and falls back to the great-circle estimate without it. So not configured is optional (Healthy) and an
/// unreachable function is Degraded, never Unhealthy. The probe is the cached one of <see cref="RoutingProbeCache"/>.
/// </summary>
public class RouteFunctionHealthCheck : IHealthCheck
{
    public const string Name = "route-function";

    private static readonly string[] Settings = [$"{RoutingIntegrationOptions.SectionName}:RouteFunction:BaseUrl"];

    private readonly ValhallaFunctionProvider _function;
    private readonly RoutingProbeCache _probes;

    public RouteFunctionHealthCheck(ValhallaFunctionProvider function, RoutingProbeCache probes)
    {
        _function = function;
        _probes = probes;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_function.IsConfigured)
        {
            return HealthCheckResult.Healthy(
                "route function: not configured; CO₂ uses the great-circle estimate.",
                HealthCheckFacts.Data(HealthCheckFacts.Reasons.NotConfigured, Settings, optional: true));
        }

        var (reachable, _) = await _probes.GetOrProbeAsync(_function.Name, _function.ProbeAsync, cancellationToken);
        return reachable
            ? HealthCheckResult.Healthy("route function answered the probe.", HealthCheckFacts.Data(HealthCheckFacts.Reasons.Ok, Settings, _function.Host, optional: true))
            : HealthCheckResult.Degraded(
                "route function did not answer the probe; CO₂ uses the great-circle estimate.",
                data: HealthCheckFacts.Data(HealthCheckFacts.Reasons.Unreachable, Settings, _function.Host, optional: true));
    }
}
