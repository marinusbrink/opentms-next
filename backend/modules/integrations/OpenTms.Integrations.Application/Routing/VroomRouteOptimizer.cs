using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// The VROOM adapter of design 68 §5: one POST of the problem to <c>Integrations:Routing:Vroom:BaseUrl</c> with one
/// vehicle (the fixed start and end when given), a job per free stop and a shipment per precedence pair. VROOM routes on
/// its own engine. Answers null when the request is not expressible (a stop in more than one precedence pair) or VROOM
/// leaves a stop unassigned; the caller then uses the heuristic. A timeout, an unreachable host, an error status or an
/// answer of the wrong shape throws <see cref="RoutingUnavailableException"/>. Logs carry the status only, never
/// coordinates (constitution rule 5). The typed client carries the base URL and the timeout.
/// </summary>
public class VroomRouteOptimizer
{
    public const string ProviderName = "Vroom";

    private readonly HttpClient _http;
    private readonly RoutingIntegrationOptions _options;

    public ILogger<VroomRouteOptimizer> Logger { get; set; } = NullLogger<VroomRouteOptimizer>.Instance;

    public VroomRouteOptimizer(HttpClient http, IOptions<RoutingIntegrationOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public bool IsConfigured => _options.Vroom.IsConfigured;

    public async Task<RouteOptimizationResult?> OptimizeAsync(RouteOptimizationRequest request, CancellationToken cancellationToken)
    {
        if (!IsConfigured || request.Stops.Count < 2 || (request.Start == null && request.End == null))
        {
            /* A VROOM vehicle needs a start or an end; an open route on both sides is the heuristic's. */
            return null;
        }

        var indexOf = request.Stops.Select((s, i) => (s.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var pairs = request.Precedences
            .Where(p => indexOf.ContainsKey(p.BeforeStopId) && indexOf.ContainsKey(p.AfterStopId) && p.BeforeStopId != p.AfterStopId)
            .Select(p => (Before: indexOf[p.BeforeStopId], After: indexOf[p.AfterStopId]))
            .Distinct()
            .ToList();
        var inPairs = pairs.SelectMany(p => new[] { p.Before, p.After }).ToList();
        if (inPairs.Count != inPairs.Distinct().Count())
        {
            /* A stop that picks up one leg and delivers another is in two pairs: a VROOM shipment cannot share a step. */
            return null;
        }

        var body = Problem(request, pairs, inPairs.ToHashSet());
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(string.Empty, body, cancellationToken);
        }
        catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.LogWarning("VROOM did not answer within {Timeout}.", _options.Timeout);
            throw new RoutingUnavailableException(ProviderName, "optimize", RoutingFailure.Timeout, null, e);
        }
        catch (HttpRequestException e)
        {
            Logger.LogWarning("VROOM could not be reached ({Error}).", e.GetType().Name);
            throw new RoutingUnavailableException(ProviderName, "optimize", RoutingFailure.Unreachable, null, e);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning("VROOM answered HTTP {StatusCode}.", (int)response.StatusCode);
                throw new RoutingUnavailableException(ProviderName, "optimize", RoutingFailure.HttpStatus, (int)response.StatusCode);
            }

            try
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
                var root = json.RootElement;
                if (root.GetProperty("code").GetInt32() != 0)
                {
                    Logger.LogWarning("VROOM refused the problem with code {Code}.", root.GetProperty("code").GetInt32());
                    return null;
                }

                if (root.TryGetProperty("unassigned", out var unassigned) && unassigned.GetArrayLength() > 0)
                {
                    Logger.LogInformation("VROOM left {Count} stops unassigned; the heuristic sequences the trip.", unassigned.GetArrayLength());
                    return null;
                }

                var order = new List<Guid>();
                foreach (var step in root.GetProperty("routes")[0].GetProperty("steps").EnumerateArray())
                {
                    var type = step.GetProperty("type").GetString();
                    if (type is "job" or "pickup" or "delivery")
                    {
                        var index = step.GetProperty("id").GetInt32() - 1;
                        order.Add(request.Stops[index].Id);
                    }
                }

                if (order.Count != request.Stops.Count || order.Distinct().Count() != order.Count)
                {
                    Logger.LogWarning("VROOM answered {Count} steps for {StopCount} stops.", order.Count, request.Stops.Count);
                    throw new RoutingUnavailableException(ProviderName, "optimize", RoutingFailure.InvalidAnswer);
                }

                return new RouteOptimizationResult(order, RouteOptimizationResult.MethodVroom, null);
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or ArgumentOutOfRangeException)
            {
                Logger.LogWarning("VROOM answered a body that is not its solution shape ({Error}).", e.GetType().Name);
                throw new RoutingUnavailableException(ProviderName, "optimize", RoutingFailure.InvalidAnswer, null, e);
            }
        }
    }

    /// <summary>Times are seconds from one day before the earliest window, so the vehicle (starting at 0) can reach every stop.</summary>
    private static JsonObject Problem(RouteOptimizationRequest request, List<(int Before, int After)> pairs, HashSet<int> inPairs)
    {
        var windows = request.Stops.SelectMany(s => new[] { s.WindowFrom, s.WindowTill }).Where(t => t != null).Select(t => t!.Value).ToList();
        var origin = windows.Count == 0 ? DateTimeOffset.UnixEpoch : windows.Min().AddDays(-1);

        JsonArray Location(GeoPoint p) => new(p.Longitude, p.Latitude);

        JsonObject Step(int index)
        {
            var stop = request.Stops[index];
            var step = new JsonObject
            {
                ["id"] = index + 1,
                ["location"] = Location(stop.Place),
                ["service"] = Math.Max(0, stop.ServiceMinutes) * 60
            };
            if (stop.WindowFrom != null || stop.WindowTill != null)
            {
                var from = stop.WindowFrom == null ? 0 : Math.Max(0, (long)(stop.WindowFrom.Value - origin).TotalSeconds);
                var till = stop.WindowTill == null ? uint.MaxValue : Math.Max(from, (long)(stop.WindowTill.Value - origin).TotalSeconds);
                step["time_windows"] = new JsonArray(new JsonArray(from, till));
            }

            return step;
        }

        var vehicle = new JsonObject { ["id"] = 1, ["profile"] = "car" };
        if (request.Start is { } start)
        {
            vehicle["start"] = Location(start);
        }

        if (request.End is { } end)
        {
            vehicle["end"] = Location(end);
        }

        var jobs = new JsonArray();
        for (var i = 0; i < request.Stops.Count; i++)
        {
            if (!inPairs.Contains(i))
            {
                jobs.Add(Step(i));
            }
        }

        var shipments = new JsonArray();
        foreach (var (before, after) in pairs)
        {
            shipments.Add(new JsonObject { ["pickup"] = Step(before), ["delivery"] = Step(after) });
        }

        return new JsonObject
        {
            ["vehicles"] = new JsonArray(vehicle),
            ["jobs"] = jobs,
            ["shipments"] = shipments
        };
    }
}
