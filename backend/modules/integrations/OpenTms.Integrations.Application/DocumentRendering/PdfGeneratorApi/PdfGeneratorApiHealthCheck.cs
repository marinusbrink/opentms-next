using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTms.Platform.DocumentRendering;
using Volo.Abp;

namespace OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;

/// <summary>
/// The connection test of pdfgeneratorapi.com (design 50): <c>GET templates?per_page=1</c> in the default workspace,
/// one interactive attempt (an operator waits on it). Tagged <see cref="Tag"/>: not part of <c>/health/ready</c> or <c>/health-status</c>; it runs on demand
/// in the host's Maintenance → Health tab. Without key and secret it reports Degraded ("not configured"), not Unhealthy.
/// </summary>
public class PdfGeneratorApiHealthCheck : IHealthCheck
{
    public const string Name = "pdfgeneratorapi";

    /// <summary>Checks of external services: excluded from the probes and the polled health endpoint.</summary>
    public const string Tag = "external";

    private readonly PdfGeneratorApiClient _client;

    public PdfGeneratorApiHealthCheck(PdfGeneratorApiClient client)
    {
        _client = client;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_client.IsConfigured)
        {
            return HealthCheckResult.Degraded("pdfgeneratorapi: not configured (Integrations:PdfGeneratorApi Key and Secret).");
        }

        try
        {
            await _client.SendAsync(
                HttpMethod.Get, "templates?per_page=1", null, "health", null, RenderCallMode.Interactive, cancellationToken,
                workspace: _client.DefaultWorkspace);
            return HealthCheckResult.Healthy("pdfgeneratorapi answered in the default workspace.");
        }
        catch (BusinessException e)
        {
            var status = e.Data["status"] is { } code ? $"HTTP {code}: " : string.Empty;
            return HealthCheckResult.Unhealthy($"pdfgeneratorapi: {status}{e.Data["message"]}");
        }
    }
}
