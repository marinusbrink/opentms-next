using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// Stop-sequence optimisation behind one adapter (design 68 §5, PO decision D20). Published, in-process only, called from
/// Planning's optimisation job, never from a request. VROOM over HTTP when <c>Integrations:Routing:Vroom:BaseUrl</c> is
/// configured; otherwise (and when VROOM fails or cannot express the request) a built-in heuristic: nearest neighbour then
/// 2-opt, honouring the precedence pairs and the fixed start and end, on the routing provider's duration table, or on
/// great-circle distance when routing is off. Null means "nothing to optimise" (fewer than two stops).
/// </summary>
public interface IRouteOptimizer
{
    Task<RouteOptimizationResult?> OptimizeAsync(RouteOptimizationRequest request, CancellationToken cancellationToken = default);
}

/// <param name="Start">The fixed place the route starts from; null = the route may start at any stop.</param>
/// <param name="End">The fixed place the route ends at; null = the route may end at any stop.</param>
/// <param name="Stops">The stops to sequence (at most <see cref="RouteOptimizationRequest.MaxStops"/>).</param>
/// <param name="Precedences">Stop pairs whose first must come before the second (pickup before delivery of the same leg).</param>
/// <param name="Vehicle">The truck profile for the duration table; null = the default truck.</param>
public sealed record RouteOptimizationRequest(
    GeoPoint? Start,
    GeoPoint? End,
    IReadOnlyList<RouteOptimizationStop> Stops,
    IReadOnlyList<RouteOptimizationPrecedence> Precedences,
    VehicleSpec? Vehicle = null)
{
    public const int MaxStops = 100;
}

/// <param name="WindowFrom">The earliest arrival (VROOM honours it; the heuristic does not).</param>
/// <param name="WindowTill">The latest arrival (VROOM honours it; the heuristic does not).</param>
/// <param name="ServiceMinutes">The time spent at the stop.</param>
public sealed record RouteOptimizationStop(Guid Id, GeoPoint Place, DateTimeOffset? WindowFrom, DateTimeOffset? WindowTill, int ServiceMinutes);

public sealed record RouteOptimizationPrecedence(Guid BeforeStopId, Guid AfterStopId);

/// <param name="StopIds">Every requested stop id once, in the optimised order.</param>
/// <param name="Method"><see cref="MethodVroom"/>, <see cref="MethodHeuristicRouted"/> or <see cref="MethodHeuristicGreatCircle"/>.</param>
/// <param name="Cost">The optimised route's cost in the method's unit (seconds, or metres of great-circle distance); null for VROOM.</param>
public sealed record RouteOptimizationResult(IReadOnlyList<Guid> StopIds, string Method, double? Cost)
{
    public const string MethodVroom = "Vroom";
    public const string MethodHeuristicRouted = "HeuristicRouted";
    public const string MethodHeuristicGreatCircle = "HeuristicGreatCircle";
}
