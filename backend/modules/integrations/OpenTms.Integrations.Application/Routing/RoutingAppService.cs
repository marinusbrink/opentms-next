using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Integrations.Permissions;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// <c>GET /api/integrations/routing/status</c> (design 60, "API contract"): the provider, whether it is configured and
/// answered the cached probe, the function's host only, the current tenant's cache rows. Never a URL, a path or a key.
/// </summary>
public class RoutingAppService : IntegrationsAppServiceBase, IRoutingAppService
{
    private readonly IRoutingService _routing;

    public RoutingAppService(IRoutingService routing)
    {
        _routing = routing;
    }

    [Authorize(IntegrationsPermissions.Routing)]
    public async Task<RoutingStatusDto> GetStatusAsync()
    {
        var status = await _routing.GetStatusAsync();
        return new RoutingStatusDto
        {
            Provider = status.Provider,
            Configured = status.Configured,
            Reachable = status.Reachable,
            RouteFunctionHost = status.RouteFunctionHost,
            CacheRows = status.CacheRows,
            LastProbeAt = status.LastProbeAt
        };
    }
}
