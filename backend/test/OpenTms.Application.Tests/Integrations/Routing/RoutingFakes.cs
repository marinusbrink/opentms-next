using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace OpenTms.Integrations.Routing;

/// <summary>One recorded outbound request of a routing provider, kept as values because the message is disposed with the client.</summary>
public sealed record RecordedRoutingRequest(HttpMethod Method, Uri Uri, string? Body, IReadOnlyDictionary<string, string> Headers)
{
    public JsonElement Json => JsonDocument.Parse(Body!).RootElement.Clone();

    public string PathAndQuery => Uri.PathAndQuery;
}

/// <summary>
/// The fake transport of design 60's test plan: answers from a script (then a fallback), records every request. No
/// request ever leaves the process; the base URLs of the tests end in the reserved ".invalid" top-level domain.
/// </summary>
public sealed class RoutingHttpHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _script = new();

    public List<RecordedRoutingRequest> Requests { get; } = new();

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Fallback { get; set; } = (_, _) => Task.FromResult(Json(RoutingRecorded.OsrmRouteOk));

    public RoutingHttpHandler ThenJson(string json) => Then(() => Json(json));

    public RoutingHttpHandler ThenStatus(HttpStatusCode status, string body = "{}") => Then(() => Status(status, body));

    public RoutingHttpHandler ThenStatus(int status, string body = "{}") => Then(() => Status((HttpStatusCode)status, body));

    public RoutingHttpHandler ThenThrow(Exception exception)
    {
        _script.Enqueue((_, _) => throw exception);
        return this;
    }

    /// <summary>An answer that never comes: the call ends only through the caller's timeout token, as a real socket would.</summary>
    public RoutingHttpHandler ThenHang()
    {
        _script.Enqueue(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Json("{}");
        });
        return this;
    }

    public void Always(string json) => Fallback = (_, _) => Task.FromResult(Json(json));

    public void AlwaysStatus(HttpStatusCode status, string body = "{}") => Fallback = (_, _) => Task.FromResult(Status(status, body));

    public void AlwaysThrow(Exception exception) => Fallback = (_, _) => throw exception;

    /// <summary>Answers by path: OSRM route, table and nearest calls and the function's POST each get their recorded body.</summary>
    public void AnswerByPath() => Fallback = (request, _) =>
    {
        var path = request.RequestUri!.PathAndQuery;
        if (request.Method == HttpMethod.Post)
        {
            return Task.FromResult(Json(RoutingRecorded.FunctionOk));
        }

        if (path.Contains("/table/", StringComparison.Ordinal))
        {
            return Task.FromResult(Json(RoutingRecorded.OsrmTableOk));
        }

        if (path.Contains("/nearest/", StringComparison.Ordinal))
        {
            return Task.FromResult(Json(RoutingRecorded.OsrmNearestOk));
        }

        return Task.FromResult(Json(RoutingRecorded.OsrmRouteOk));
    };

    public void Reset()
    {
        lock (_gate)
        {
            Requests.Clear();
            _script.Clear();
        }

        AnswerByPath();
    }

    public static HttpResponseMessage Json(string json) => Status(HttpStatusCode.OK, json);

    public static HttpResponseMessage Status(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private RoutingHttpHandler Then(Func<HttpResponseMessage> response)
    {
        _script.Enqueue((_, _) => Task.FromResult(response()));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer;
        lock (_gate)
        {
            Requests.Add(new RecordedRoutingRequest(
                request.Method,
                request.RequestUri!,
                body,
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase)));
            answer = _script.Count > 0 ? _script.Dequeue() : Fallback;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await answer(request, cancellationToken);
    }
}

/// <summary>Builds the providers of design 60 on the fake transport, as the module wires them.</summary>
public static class RoutingHarness
{
    public const string OsrmBaseUrl = "https://osrm.invalid/";

    public const string FunctionUrl = "https://route-function.invalid/prod-route";

    public static readonly GeoPoint Amsterdam = new(52.3702, 4.8952);

    public static readonly GeoPoint Rotterdam = new(51.9225, 4.47917);

    public static readonly GeoPoint Utrecht = new(52.0907, 5.1214);

    public static RoutingIntegrationOptions Options(bool osrm = true, bool function = true, string? apiKey = null) => new()
    {
        Osrm = { BaseUrl = osrm ? OsrmBaseUrl : null },
        RouteFunction = { BaseUrl = function ? FunctionUrl : null, ApiKey = apiKey },
        TimeoutSeconds = 1
    };

    public static OsrmRoutingProvider Osrm(RoutingHttpHandler handler, RoutingIntegrationOptions? options = null)
    {
        options ??= Options();
        var client = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(5) };
        if (options.Osrm.IsConfigured)
        {
            client.BaseAddress = new Uri(options.Osrm.BaseUrl!);
        }

        return new OsrmRoutingProvider(client, Microsoft.Extensions.Options.Options.Create(options));
    }

    public static ValhallaFunctionProvider Function(RoutingHttpHandler handler, RoutingIntegrationOptions? options = null)
    {
        options ??= Options();
        var client = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(5) };
        return new ValhallaFunctionProvider(client, Microsoft.Extensions.Options.Options.Create(options));
    }
}

/// <summary>Recorded answers (OSRM v5 shapes; the route function's contract of the assessment appendix §1.4).</summary>
public static class RoutingRecorded
{
    /* GET /route/v1/driving/4.8952,52.3702;4.47917,51.9225?overview=false — OSRM gives metres and seconds as doubles. */
    public const string OsrmRouteOk = """
        {"code":"Ok","routes":[{"legs":[{"steps":[],"summary":"","weight":3712.4,"duration":3712.4,"distance":72484.6}],"weight_name":"routability","weight":3712.4,"duration":3712.4,"distance":72484.6}],"waypoints":[{"hint":"a","distance":4.1,"name":"Dam","location":[4.8952,52.3702]},{"hint":"b","distance":2.2,"name":"Coolsingel","location":[4.47917,51.9225]}]}
        """;

    /* Three coordinates: two legs; the first leg's distance ends in .5 and must round away from zero (1234.5 → 1235). */
    public const string OsrmRouteTwoLegs = """
        {"code":"Ok","routes":[{"legs":[{"duration":1800.49,"distance":41234.5},{"duration":2400.0,"distance":57800.0}],"duration":4200.49,"distance":99034.5}],"waypoints":[]}
        """;

    /* overview=full&geometries=polyline adds the encoded geometry on the route. */
    public const string OsrmRouteWithGeometry = """
        {"code":"Ok","routes":[{"geometry":"_p~iF~ps|U_ulLnnqC_mqNvxq`@","legs":[{"duration":3712.4,"distance":72484.6}],"duration":3712.4,"distance":72484.6}],"waypoints":[]}
        """;

    /* GET /table/v1/driving/…?annotations=distance,duration over three points. */
    public const string OsrmTableOk = """
        {"code":"Ok","distances":[[0,72484.6,45120.2],[72510.1,0,58900.0],[45200.5,58950.3,0]],"durations":[[0,3712.4,2410.0],[3720.0,0,3010.6],[2415.2,3015.0,0]],"sources":[],"destinations":[]}
        """;

    /* A pair without a road connection: OSRM answers null cells. */
    public const string OsrmTableUnreachable = """
        {"code":"Ok","distances":[[0,null],[null,0]],"durations":[[0,null],[null,0]],"sources":[],"destinations":[]}
        """;

    /* HTTP 400 with OSRM's own code. */
    public const string OsrmNoRoute = """
        {"code":"NoRoute","message":"Impossible route between points"}
        """;

    public const string OsrmNearestOk = """
        {"code":"Ok","waypoints":[{"hint":"x","distance":3.2,"name":"Damrak","location":[4.9041,52.3676]}]}
        """;

    /* POST to the route function: route, toll and carbon footprint (GLEC v3 / ISO 14083). */
    public const string FunctionOk = """
        {"route":{"distance":74210.3,"duration":3805.7,"polyline":"_p~iF~ps|U_ulLnnqC_mqNvxq`@"},"toll":{"totalCost":12.345,"totalDistance":51230.8,"currency":"eur","segments":[{"cost":12.345,"ratePerKm":0.24,"distance":51230.8,"geometry":"abc"}]},"carbonFootprint":{"totalKgCO2e":58.1234,"gCO2ePerTkm":62.7}}
        """;

    /* The retry without a vehicle keeps the route only. */
    public const string FunctionNoVehicle = """
        {"route":{"distance":73000.0,"duration":3700.0,"polyline":"abcd"}}
        """;

    /* Valhalla's "no path" as a 400 with the message in the body. */
    public const string FunctionNoPathBody = """
        {"error":"No path could be found for input","error_code":442}
        """;
}
