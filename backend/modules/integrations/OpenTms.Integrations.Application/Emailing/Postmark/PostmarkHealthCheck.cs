using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OpenTms.Integrations.Emailing.Postmark;

/// <summary>
/// The connection test of Postmark (design 64): <c>GET server</c> with the server token. Tagged <see cref="Tag"/>: not
/// part of <c>/health/ready</c>; it runs on demand in the host's Maintenance → Health tab. Without a token it reports
/// Degraded ("not configured"), not Unhealthy.
/// </summary>
public class PostmarkHealthCheck : IHealthCheck
{
    public const string Name = "postmark";

    public const string Tag = "external";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PostmarkSettingsResolver _settings;

    public PostmarkHealthCheck(IHttpClientFactory httpClientFactory, PostmarkSettingsResolver settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var options = (await _settings.ResolveAsync()).Options;
        if (!options.IsConfigured)
        {
            return HealthCheckResult.Degraded("postmark: not configured (Integrations:Postmark ServerToken and FromAddress).");
        }

        var http = _httpClientFactory.CreateClient(nameof(PostmarkEmailDeliveryProvider));
        using var request = new HttpRequestMessage(HttpMethod.Get, "server");
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("X-Postmark-Server-Token", options.ServerToken);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("postmark answered for the configured server.")
                : HealthCheckResult.Unhealthy($"postmark: HTTP {(int)response.StatusCode}.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("postmark: not reachable.");
        }
    }
}
