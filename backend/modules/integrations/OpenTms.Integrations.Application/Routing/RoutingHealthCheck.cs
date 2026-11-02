using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTms.Platform.Maintenance;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// The routing provider's reachability as a health check (design 60, "API contract"), registered like the
/// pdfgeneratorapi one: tagged <see cref="Tag"/>, so it stays out of <c>/health/ready</c> and the polled health endpoint
/// and runs on demand in the host's Maintenance → Health tab. Not configured is Degraded, not Unhealthy; the probe is the
/// cached one of <see cref="RoutingProbeCache"/>. The facts name the setting of the chosen provider and its host.
/// </summary>
public class RoutingHealthCheck : IHealthCheck
{
    public const string Name = "routing";

    /// <summary>Checks of external services: excluded from the probes and the polled health endpoint.</summary>
    public const string Tag = HealthCheckFacts.ExternalTag;

    private readonly IRoutingService _routing;
    private readonly RoutingIntegrationOptions _options;

    public RoutingHealthCheck(IRoutingService routing, IOptions<RoutingIntegrationOptions> options)
    {
        _routing = routing;
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = await _routing.GetStatusAsync(cancellationToken);
        var (setting, baseUrl) = ProviderSetting(status.Provider);
        var host = Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ? uri.Host : null;
        var settings = new[] { $"{RoutingIntegrationOptions.SectionName}:Provider", setting };
        var extra = new Dictionary<string, object> { ["provider"] = status.Provider };

        if (!status.Configured)
        {
            return HealthCheckResult.Degraded(
                $"routing: provider {status.Provider} is not configured (Integrations:Routing).",
                data: HealthCheckFacts.Data(HealthCheckFacts.Reasons.NotConfigured, settings, host, extra: extra));
        }

        return status.Reachable
            ? HealthCheckResult.Healthy(
                $"routing: {status.Provider} answered the probe.",
                HealthCheckFacts.Data(HealthCheckFacts.Reasons.Ok, settings, host, extra: extra))
            : HealthCheckResult.Unhealthy(
                $"routing: {status.Provider} did not answer the probe.",
                data: HealthCheckFacts.Data(HealthCheckFacts.Reasons.Unreachable, settings, host, extra: extra));
    }

    private (string Setting, string? BaseUrl) ProviderSetting(string provider) =>
        string.Equals(provider, RoutingConsts.RouteFunctionProviderName, StringComparison.OrdinalIgnoreCase)
            ? ($"{RoutingIntegrationOptions.SectionName}:RouteFunction:BaseUrl", _options.RouteFunction.BaseUrl)
            : string.Equals(provider, RoutingConsts.OsrmProviderName, StringComparison.OrdinalIgnoreCase)
                ? ($"{RoutingIntegrationOptions.SectionName}:Osrm:BaseUrl", _options.Osrm.BaseUrl)
                : ($"{RoutingIntegrationOptions.SectionName}:Provider", null);
}
