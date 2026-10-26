using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// The one adapter in front of the routing engines (design 60; AD-12). Per question: build the cache key, read the
/// tenant's unexpired row, else ask the configured provider for distance and duration and, when asked, the route function
/// for toll, CO₂ and geometry, then write the row. The matrix is the consecutive pairs with the cache per pair; the
/// pairs the cache misses go to the provider in one table call. Null means "no route": not configured (one warning per
/// tenant per hour, not per call) or no path. An engine that is down — the distance provider, or the route function when
/// toll or CO₂ is asked — throws <see cref="RoutingUnavailableException"/> through this service, so the calculation job
/// fails and is retried with backoff; nothing is cached for that question. In-process only, called from calculator jobs.
/// </summary>
public class RoutingService : IRoutingService, ITransientDependency
{
    private readonly IEnumerable<IRouteProvider> _providers;
    private readonly IRouteFunction _function;
    private readonly IRepository<RouteCacheEntry, Guid> _cache;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly IGuidGenerator _guids;
    private readonly TimeProvider _time;
    private readonly RoutingProbeCache _probes;
    private readonly RoutingIntegrationOptions _options;

    public ILogger<RoutingService> Logger { get; set; } = NullLogger<RoutingService>.Instance;

    public RoutingService(
        IEnumerable<IRouteProvider> providers,
        IRouteFunction function,
        IRepository<RouteCacheEntry, Guid> cache,
        IUnitOfWorkManager unitOfWorkManager,
        ICurrentTenant currentTenant,
        IGuidGenerator guids,
        TimeProvider time,
        RoutingProbeCache probes,
        IOptions<RoutingIntegrationOptions> options)
    {
        _providers = providers;
        _function = function;
        _cache = cache;
        _unitOfWorkManager = unitOfWorkManager;
        _currentTenant = currentTenant;
        _guids = guids;
        _time = time;
        _probes = probes;
        _options = options.Value;
    }

    public async Task<RouteResult?> GetRouteAsync(GeoPoint from, GeoPoint to, VehicleSpec? vehicle, RoutingOptions options, CancellationToken cancellationToken = default)
    {
        var provider = ConfiguredProvider();
        if (provider == null)
        {
            return null;
        }

        /* Toll and CO₂ always come from the function when it is configured (PO decision Q4); a provider that is the
         * function itself answers them in the same call. */
        var wantsFunction = options.IncludeToll || options.IncludeCo2;
        var function = wantsFunction && _function.IsConfigured && provider is not IRouteFunction ? _function : null;
        var cacheProvider = function == null ? provider.Name : provider.Name + "+" + RoutingConsts.RouteFunctionProviderName;
        var key = RouteCacheKey.Build(from, to, vehicle, cacheProvider, options);
        var now = _time.GetUtcNow();

        var hit = await _cache.FindAsync(e => e.Key == key && e.ExpiresAt > now.UtcDateTime, cancellationToken: cancellationToken);
        if (hit != null)
        {
            return ToResult(hit, provider.Name, now, fromCache: true);
        }

        var route = await provider.GetRouteAsync([from, to], vehicle, options.IncludeGeometry, cancellationToken);
        if (route == null)
        {
            return null;
        }

        /* A function that is down throws: toll kilometres are a pricing input, so the leg waits for the retry rather than
         * storing a distance without them. A function without a path (null) leaves toll and CO₂ empty. */
        var extra = function == null ? null : await function.CalculateAsync([from, to], vehicle, cancellationToken);
        var source = extra ?? route;
        var result = new RouteResult(
            route.TotalDistanceM,
            route.TotalDurationS,
            options.IncludeGeometry ? route.EncodedPolyline ?? extra?.EncodedPolyline : null,
            options.IncludeToll ? source.TollDistanceM : null,
            options.IncludeToll ? source.TollAmount : null,
            options.IncludeToll ? source.TollCurrencyCode : null,
            options.IncludeCo2 ? source.Co2Kg : null,
            provider.Name,
            now,
            FromCache: false);

        /* A row is written only when every asked part is in hand: a function outage must not pin "no toll" for 30 days. */
        if (function == null || extra != null)
        {
            await StoreAsync([(key, cacheProvider, result)], now, cancellationToken);
        }

        return result;
    }

    public async Task<RouteMatrix?> GetMatrixAsync(IReadOnlyList<GeoPoint> points, VehicleSpec? vehicle, CancellationToken cancellationToken = default)
    {
        var provider = ConfiguredProvider();
        if (provider == null)
        {
            return null;
        }

        var now = _time.GetUtcNow();
        var pairs = points.Count - 1;
        if (pairs < 1)
        {
            return new RouteMatrix([], provider.Name, now);
        }

        var keys = new string[pairs];
        for (var i = 0; i < pairs; i++)
        {
            keys[i] = RouteCacheKey.Build(points[i], points[i + 1], vehicle, provider.Name, RoutingOptions.DistanceOnly);
        }

        var distinctKeys = keys.Distinct().ToList();
        var hits = (await _cache.GetListAsync(e => distinctKeys.Contains(e.Key) && e.ExpiresAt > now.UtcDateTime, cancellationToken: cancellationToken))
            .ToDictionary(e => e.Key);

        var legs = new RouteResult?[pairs];
        var missing = new List<int>();
        for (var i = 0; i < pairs; i++)
        {
            if (hits.TryGetValue(keys[i], out var hit))
            {
                legs[i] = ToResult(hit, provider.Name, now, fromCache: true);
            }
            else
            {
                missing.Add(i);
            }
        }

        if (missing.Count > 0)
        {
            var fresh = new Dictionary<string, ProviderLeg>();
            if (missing.Count == 1 || provider is IRouteFunction)
            {
                /* One pair, or an engine that knows only totals: a route call per missing pair. */
                foreach (var i in missing)
                {
                    if (fresh.ContainsKey(keys[i]))
                    {
                        continue;
                    }

                    var route = await provider.GetRouteAsync([points[i], points[i + 1]], vehicle, false, cancellationToken);
                    if (route == null)
                    {
                        return null;
                    }

                    fresh[keys[i]] = new ProviderLeg(route.TotalDistanceM, route.TotalDurationS);
                }
            }
            else
            {
                /* Several pairs: one table call over the trip's points, the consecutive cells are the legs. */
                var table = await provider.GetTableAsync(points, vehicle, cancellationToken);
                if (table == null)
                {
                    return null;
                }

                foreach (var i in missing)
                {
                    var cell = table[i, i + 1];
                    if (cell == null)
                    {
                        Logger.LogWarning("{Provider} table has no connection for a consecutive pair of the trip.", provider.Name);
                        return null;
                    }

                    fresh[keys[i]] = cell;
                }
            }

            foreach (var i in missing)
            {
                var leg = fresh[keys[i]];
                legs[i] = new RouteResult(leg.DistanceM, leg.DurationS, null, null, null, null, null, provider.Name, now, FromCache: false);
            }

            await StoreAsync(fresh.Select(f => (f.Key, provider.Name, new RouteResult(f.Value.DistanceM, f.Value.DurationS, null, null, null, null, null, provider.Name, now, false))).ToList(), now, cancellationToken);
        }

        return new RouteMatrix(legs.Select(l => l!).ToList(), provider.Name, now);
    }

    public async Task<RoutingStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var provider = ResolveProvider();
        var configured = provider?.IsConfigured ?? false;
        var (reachable, probedAt) = configured
            ? await _probes.GetOrProbeAsync(provider!.Name, provider.ProbeAsync, cancellationToken)
            : (false, (DateTimeOffset?)null);
        var rows = await _cache.GetCountAsync(cancellationToken);
        return new RoutingStatus(
            provider?.Name ?? _options.Provider,
            configured,
            reachable,
            _function.IsConfigured ? _function.Host : null,
            rows,
            probedAt);
    }

    private IRouteProvider? ResolveProvider()
        => _providers.FirstOrDefault(p => string.Equals(p.Name, _options.Provider, StringComparison.OrdinalIgnoreCase));

    /// <summary>The configured provider, or null with a throttled warning when the name is unknown or its base URL unset.</summary>
    private IRouteProvider? ConfiguredProvider()
    {
        var provider = ResolveProvider();
        if (provider is { IsConfigured: true })
        {
            return provider;
        }

        if (_probes.ShouldWarnNotConfigured(_currentTenant.Id))
        {
            Logger.LogWarning(
                "Routing is not configured for tenant {TenantId}: provider {Provider} {Reason}. Routing answers null until Integrations:Routing is set.",
                _currentTenant.Id, _options.Provider, provider == null ? "is unknown" : "has no base URL");
        }

        return null;
    }

    private static RouteResult ToResult(RouteCacheEntry entry, string providerName, DateTimeOffset at, bool fromCache)
        => new(entry.DistanceM, entry.DurationS, entry.Polyline, entry.TollDistanceM, entry.TollAmount, entry.TollCurrencyCode, entry.Co2Kg, providerName, at, fromCache);

    /// <summary>
    /// Writes the rows in a unit of work of their own: the caller's transaction (a calculation job) must not fail on a
    /// cache write, and two jobs computing the same lane at once race on the unique key — the second loses quietly. An
    /// expired row with the same key (not purged yet) is refreshed in place, so the key stays unique and the lane is
    /// served from the cache again at once.
    /// </summary>
    private async Task StoreAsync(IReadOnlyList<(string Key, string Provider, RouteResult Result)> rows, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var expiresAt = now.UtcDateTime + _options.CacheLifetime;
        try
        {
            using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = false }, requiresNew: true);
            var keys = rows.Select(r => r.Key).ToList();
            var existing = (await _cache.GetListAsync(e => keys.Contains(e.Key), cancellationToken: cancellationToken)).ToDictionary(e => e.Key);
            var inserts = new List<RouteCacheEntry>();
            foreach (var (key, provider, result) in rows)
            {
                if (existing.TryGetValue(key, out var entry))
                {
                    entry.Refresh(provider, result.DistanceM, result.DurationS, result.TollDistanceM, result.TollAmount, result.TollCurrencyCode, result.Co2Kg, result.EncodedPolyline, now.UtcDateTime, expiresAt);
                    await _cache.UpdateAsync(entry, cancellationToken: cancellationToken);
                }
                else
                {
                    inserts.Add(new RouteCacheEntry(
                        _guids.Create(), _currentTenant.Id, key, provider,
                        result.DistanceM, result.DurationS, result.TollDistanceM, result.TollAmount, result.TollCurrencyCode, result.Co2Kg,
                        result.EncodedPolyline, now.UtcDateTime, expiresAt));
                }
            }

            if (inserts.Count > 0)
            {
                await _cache.InsertManyAsync(inserts, cancellationToken: cancellationToken);
            }

            await unitOfWork.CompleteAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            /* The type only: the exception text may carry the statement with its key. */
            Logger.LogDebug("Route cache rows were not stored ({Error}); another writer was first or the database refused them.", e.GetType().Name);
        }
    }
}
