using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// Process-wide memory of the routing service (design 60, "API contract"): the last probe per provider, served for
/// <see cref="ProbeLifetime"/> so the status endpoint and the health check do not hit the engine on every call, and the
/// moment the "not configured" warning was last written per tenant, so an unconfigured environment logs it once an hour
/// instead of once per routing call.
/// </summary>
public class RoutingProbeCache : ISingletonDependency
{
    public static readonly TimeSpan ProbeLifetime = TimeSpan.FromSeconds(60);

    public static readonly TimeSpan WarningInterval = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, (bool Reachable, DateTimeOffset At)> _probes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _warnings = new();
    private readonly TimeProvider _time;

    public RoutingProbeCache(TimeProvider time)
    {
        _time = time;
    }

    /// <summary>The cached probe of <paramref name="provider"/> when younger than <see cref="ProbeLifetime"/>, else a fresh one.</summary>
    public async Task<(bool Reachable, DateTimeOffset At)> GetOrProbeAsync(string provider, Func<CancellationToken, Task<bool>> probe, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        if (_probes.TryGetValue(provider, out var cached) && now - cached.At < ProbeLifetime)
        {
            return cached;
        }

        var result = (await probe(cancellationToken), now);
        _probes[provider] = result;
        return result;
    }

    /// <summary>True once per tenant per hour (the host counts as <see cref="Guid.Empty"/>).</summary>
    public bool ShouldWarnNotConfigured(Guid? tenantId)
    {
        var now = _time.GetUtcNow();
        var key = tenantId ?? Guid.Empty;
        if (_warnings.TryGetValue(key, out var last) && now - last < WarningInterval)
        {
            return false;
        }

        _warnings[key] = now;
        return true;
    }

    public void Clear()
    {
        _probes.Clear();
        _warnings.Clear();
    }
}
