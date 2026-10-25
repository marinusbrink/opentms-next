using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// The OSRM adapter (design 60, "Providers"; assessment §1.4): <c>GET {base}/route/v1/driving/{lon,lat;…}?overview=false</c>
/// for a route (one leg per consecutive pair) and <c>GET {base}/table/v1/driving/{…}?annotations=distance,duration</c> for
/// a matrix. Metres and seconds are taken as OSRM returns them, rounded to the unit only (never to kilometres or
/// minutes as OpenTMS did). OSRM has no vehicle profile: the spec is ignored here. Any non-success answer, a
/// <c>code</c> other than <c>Ok</c>, a timeout or an unreachable host gives null and one warning with the status code —
/// never coordinates. The typed client carries the base URL and the timeout from <see cref="RoutingIntegrationOptions"/>.
/// </summary>
public class OsrmRoutingProvider : IRouteProvider
{
    public const string RoutePath = "route/v1/driving/";

    public const string TablePath = "table/v1/driving/";

    /// <summary>The probe asks the nearest road segment of one point; any JSON answer with a code means the engine is up.</summary>
    public const string ProbePath = "nearest/v1/driving/4.9041,52.3676";

    private readonly HttpClient _http;
    private readonly RoutingIntegrationOptions _options;

    public ILogger<OsrmRoutingProvider> Logger { get; set; } = NullLogger<OsrmRoutingProvider>.Instance;

    public OsrmRoutingProvider(HttpClient http, IOptions<RoutingIntegrationOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public string Name => RoutingConsts.OsrmProviderName;

    public bool IsConfigured => _options.Osrm.IsConfigured;

    public async Task<ProviderRoute?> GetRouteAsync(IReadOnlyList<GeoPoint> points, VehicleSpec? vehicle, bool includeGeometry, CancellationToken cancellationToken)
    {
        if (points.Count < 2)
        {
            return null;
        }

        var query = includeGeometry ? "?overview=full&geometries=polyline" : "?overview=false";
        var json = await GetJsonAsync(RoutePath + Coordinates(points) + query, "route", cancellationToken);
        if (json == null)
        {
            return null;
        }

        try
        {
            var route = json.Value.GetProperty("routes")[0];
            var legs = route.GetProperty("legs").EnumerateArray()
                .Select(leg => new ProviderLeg(Metres(leg.GetProperty("distance")), Seconds(leg.GetProperty("duration"))))
                .ToList();
            if (legs.Count != points.Count - 1)
            {
                Logger.LogWarning("OSRM route answered {LegCount} legs for {PointCount} points.", legs.Count, points.Count);
                return null;
            }

            string? polyline = null;
            if (includeGeometry && route.TryGetProperty("geometry", out var geometry) && geometry.ValueKind == JsonValueKind.String)
            {
                polyline = geometry.GetString();
            }

            return new ProviderRoute(legs, polyline, null, null, null, null);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            Logger.LogWarning("OSRM route answered an unexpected shape: {Error}", e.GetType().Name);
            return null;
        }
    }

    public async Task<ProviderTable?> GetTableAsync(IReadOnlyList<GeoPoint> points, VehicleSpec? vehicle, CancellationToken cancellationToken)
    {
        if (points.Count < 2)
        {
            return null;
        }

        var json = await GetJsonAsync(TablePath + Coordinates(points) + "?annotations=distance,duration", "table", cancellationToken);
        if (json == null)
        {
            return null;
        }

        try
        {
            var distances = json.Value.GetProperty("distances");
            var durations = json.Value.GetProperty("durations");
            var cells = new ProviderLeg?[points.Count][];
            for (var from = 0; from < points.Count; from++)
            {
                cells[from] = new ProviderLeg?[points.Count];
                var distanceRow = distances[from];
                var durationRow = durations[from];
                for (var to = 0; to < points.Count; to++)
                {
                    var distance = distanceRow[to];
                    var duration = durationRow[to];
                    /* OSRM answers null for a pair without a road connection. */
                    cells[from][to] = distance.ValueKind == JsonValueKind.Null || duration.ValueKind == JsonValueKind.Null
                        ? null
                        : new ProviderLeg(Metres(distance), Seconds(duration));
                }
            }

            return new ProviderTable(cells);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            Logger.LogWarning("OSRM table answered an unexpected shape: {Error}", e.GetType().Name);
            return null;
        }
    }

    public async Task<bool> ProbeAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return false;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.Timeout);
            using var response = await _http.GetAsync(ProbePath, HttpCompletionOption.ResponseContentRead, timeout.Token);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            /* Any OSRM answer carries a code ("Ok", "NoSegment", "InvalidUrl"): the engine is up, whatever it thinks of the point. */
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("code", out _);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException)
        {
            return false;
        }
    }

    /// <summary>OSRM wants <c>lon,lat;lon,lat</c>, longitude first, with a dot as decimal separator.</summary>
    public static string Coordinates(IReadOnlyList<GeoPoint> points)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < points.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(';');
            }

            builder.Append(points[i].Longitude.ToString("0.######", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(points[i].Latitude.ToString("0.######", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private async Task<JsonElement?> GetJsonAsync(string relative, string operation, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return null;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.Timeout);
            using var response = await _http.GetAsync(relative, HttpCompletionOption.ResponseContentRead, timeout.Token);
            var status = (int)response.StatusCode;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var code = document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;
            if (!response.IsSuccessStatusCode || code != "Ok")
            {
                /* The status and OSRM's code only: no coordinates in the log (rule 5). */
                Logger.LogWarning("OSRM {Operation} answered HTTP {StatusCode} with code {Code}.", operation, status, code ?? "(none)");
                return null;
            }

            return document.RootElement.Clone();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.LogWarning("OSRM {Operation} timed out after {TimeoutSeconds} s.", operation, _options.Timeout.TotalSeconds);
            return null;
        }
        catch (HttpRequestException e)
        {
            Logger.LogWarning("OSRM {Operation} could not be reached: {RequestError}.", operation, e.HttpRequestError);
            return null;
        }
        catch (JsonException)
        {
            Logger.LogWarning("OSRM {Operation} answered a body that is not JSON.", operation);
            return null;
        }
    }

    private static int Metres(JsonElement value) => (int)Math.Round(value.GetDouble(), MidpointRounding.AwayFromZero);

    private static int Seconds(JsonElement value) => (int)Math.Round(value.GetDouble(), MidpointRounding.AwayFromZero);
}
