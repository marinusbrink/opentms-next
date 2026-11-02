using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// The one stop optimiser of design 68 §5 (<see cref="IRouteOptimizer"/>). VROOM first when configured; when VROOM is not
/// configured, cannot express the request, leaves stops unassigned or is down, the heuristic answers. The heuristic's
/// costs are the configured provider's duration table over the start, the stops and the end (one table call), or
/// great-circle metres when routing is off, a cell is missing or the provider is down: optimisation degrades rather than
/// fails. In-process only, called from Planning's optimisation job.
/// </summary>
public class RouteOptimizer : IRouteOptimizer, ITransientDependency
{
    private readonly IEnumerable<IRouteProvider> _providers;
    private readonly VroomRouteOptimizer _vroom;
    private readonly RoutingIntegrationOptions _options;

    public ILogger<RouteOptimizer> Logger { get; set; } = NullLogger<RouteOptimizer>.Instance;

    public RouteOptimizer(IEnumerable<IRouteProvider> providers, VroomRouteOptimizer vroom, IOptions<RoutingIntegrationOptions> options)
    {
        _providers = providers;
        _vroom = vroom;
        _options = options.Value;
    }

    public async Task<RouteOptimizationResult?> OptimizeAsync(RouteOptimizationRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request, nameof(request));
        if (request.Stops.Count > RouteOptimizationRequest.MaxStops)
        {
            throw new ArgumentException($"At most {RouteOptimizationRequest.MaxStops} stops can be optimised.", nameof(request));
        }

        if (request.Stops.Count < 2)
        {
            return null;
        }

        if (_vroom.IsConfigured)
        {
            try
            {
                var solved = await _vroom.OptimizeAsync(request, cancellationToken);
                if (solved != null)
                {
                    return solved;
                }
            }
            catch (RoutingUnavailableException e)
            {
                Logger.LogWarning("VROOM is unavailable ({Failure}); the heuristic sequences the stops.", e.Failure);
            }
        }

        return await HeuristicAsync(request, cancellationToken);
    }

    private async Task<RouteOptimizationResult?> HeuristicAsync(RouteOptimizationRequest request, CancellationToken cancellationToken)
    {
        var points = new List<GeoPoint>();
        var startIndex = -1;
        if (request.Start is { } start)
        {
            startIndex = points.Count;
            points.Add(start);
        }

        var firstStop = points.Count;
        points.AddRange(request.Stops.Select(s => s.Place));
        var endIndex = -1;
        if (request.End is { } end)
        {
            endIndex = points.Count;
            points.Add(end);
        }

        var table = await DurationTableAsync(points, request.Vehicle, cancellationToken);
        var method = table == null ? RouteOptimizationResult.MethodHeuristicGreatCircle : RouteOptimizationResult.MethodHeuristicRouted;
        double Cost(int from, int to) => table?[from][to] ?? RouteOptimizationHeuristic.GreatCircleM(points[from], points[to]);

        var indexOf = request.Stops.Select((s, i) => (s.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var precedences = request.Precedences
            .Where(p => indexOf.ContainsKey(p.BeforeStopId) && indexOf.ContainsKey(p.AfterStopId))
            .Select(p => (indexOf[p.BeforeStopId], indexOf[p.AfterStopId]))
            .ToList();

        var solved = RouteOptimizationHeuristic.Solve(
            request.Stops.Count,
            (i, j) => Cost(firstStop + i, firstStop + j),
            startIndex < 0 ? null : i => Cost(startIndex, firstStop + i),
            endIndex < 0 ? null : i => Cost(firstStop + i, endIndex),
            precedences);
        if (solved == null)
        {
            Logger.LogInformation("The stops' precedences form a cycle; no order honours them.");
            return null;
        }

        return new RouteOptimizationResult(solved.Value.Order.Select(i => request.Stops[i].Id).ToList(), method, solved.Value.Cost);
    }

    /// <summary>Seconds between every pair of points from the configured provider, or null (not configured, a gap, down).</summary>
    private async Task<double[][]?> DurationTableAsync(IReadOnlyList<GeoPoint> points, VehicleSpec? vehicle, CancellationToken cancellationToken)
    {
        var provider = _providers.FirstOrDefault(p => string.Equals(p.Name, _options.Provider, StringComparison.OrdinalIgnoreCase));
        if (provider is not { IsConfigured: true } || provider is IRouteFunction)
        {
            return null;
        }

        try
        {
            var table = await provider.GetTableAsync(points, vehicle, cancellationToken);
            if (table == null)
            {
                return null;
            }

            var result = new double[points.Count][];
            for (var i = 0; i < points.Count; i++)
            {
                result[i] = new double[points.Count];
                for (var j = 0; j < points.Count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    var cell = table[i, j];
                    if (cell == null)
                    {
                        return null;
                    }

                    result[i][j] = cell.DurationS;
                }
            }

            return result;
        }
        catch (RoutingUnavailableException e)
        {
            Logger.LogWarning("The routing provider is unavailable for the optimiser ({Failure}); great-circle distances are used.", e.Failure);
            return null;
        }
    }
}
