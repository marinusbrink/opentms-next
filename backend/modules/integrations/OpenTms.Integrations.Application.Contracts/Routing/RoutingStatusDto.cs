using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// What <c>GET /api/integrations/routing/status</c> shows (design 60, "API contract"): the provider's name, whether it
/// is configured and answered the last probe, the route function's host (never a full URL or a key) and the cache rows
/// of the current tenant.
/// </summary>
public class RoutingStatusDto
{
    public string Provider { get; set; } = default!;

    public bool Configured { get; set; }

    public bool Reachable { get; set; }

    /// <summary>The host name of the route function when configured; the path and any key stay out.</summary>
    public string? RouteFunctionHost { get; set; }

    public long CacheRows { get; set; }

    public DateTimeOffset? LastProbeAt { get; set; }
}

/// <summary>The status endpoint of design 60; conventional route <c>GET /api/integrations/routing/status</c>.</summary>
public interface IRoutingAppService : IApplicationService
{
    Task<RoutingStatusDto> GetStatusAsync();
}
