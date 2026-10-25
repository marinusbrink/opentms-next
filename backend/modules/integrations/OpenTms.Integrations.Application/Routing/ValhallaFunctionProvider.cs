using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTms.MasterData.Fleet;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// The adapter of the OpenTMS Cloud Function in front of Valhalla (design 60, "Providers"; assessment §1.4): one POST
/// <c>{ origin, destination, waypoints[], vehicle{ weight, euroClass, height, width, length, fuelType, fuelConsumption } }</c>
/// answers <c>route{ distance, duration, polyline }</c>, <c>toll{ totalCost, totalDistance, currency?, segments[] }</c> and
/// <c>carbonFootprint{ totalKgCO2e, gCO2ePerTkm }</c> (GLEC v3 / ISO 14083). The function knows only the total, so a
/// route is one leg and a table is one call per pair. When truck routing finds no path (HTTP 442, or an error body that
/// says "no path") the call is retried once without the vehicle; the answer then keeps distance, duration and geometry
/// and drops toll and CO₂ (<see cref="ProviderRoute.VehicleDropped"/>). Ten seconds per attempt; the optional key goes
/// in the configured header. Logs carry the operation and the status code, never coordinates, the key or the body.
/// </summary>
public class ValhallaFunctionProvider : IRouteProvider, IRouteFunction
{
    /// <summary>Valhalla's own status for "no path could be found", passed through by the function.</summary>
    public const int NoPathStatusCode = 442;

    private const string NoPathMarker = "no path";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly RoutingIntegrationOptions _options;

    public ILogger<ValhallaFunctionProvider> Logger { get; set; } = NullLogger<ValhallaFunctionProvider>.Instance;

    public ValhallaFunctionProvider(HttpClient http, IOptions<RoutingIntegrationOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public string Name => RoutingConsts.RouteFunctionProviderName;

    public bool IsConfigured => _options.RouteFunction.IsConfigured;

    public string? Host => _options.RouteFunction.Host;

    public Task<ProviderRoute?> GetRouteAsync(IReadOnlyList<GeoPoint> points, VehicleSpec? vehicle, bool includeGeometry, CancellationToken cancellationToken)
        => CalculateAsync(points, vehicle, cancellationToken);

    public async Task<ProviderTable?> GetTableAsync(IReadOnlyList<GeoPoint> points, VehicleSpec? vehicle, CancellationToken cancellationToken)
    {
        if (points.Count < 2)
        {
            return null;
        }

        /* One call per ordered pair of distinct points; the diagonal is zero. */
        var cells = new ProviderLeg?[points.Count][];
        for (var from = 0; from < points.Count; from++)
        {
            cells[from] = new ProviderLeg?[points.Count];
            for (var to = 0; to < points.Count; to++)
            {
                if (from == to)
                {
                    cells[from][to] = new ProviderLeg(0, 0);
                    continue;
                }

                var route = await CalculateAsync([points[from], points[to]], vehicle, cancellationToken);
                if (route == null)
                {
                    return null;
                }

                cells[from][to] = new ProviderLeg(route.TotalDistanceM, route.TotalDurationS);
            }
        }

        return new ProviderTable(cells);
    }

    public async Task<ProviderRoute?> CalculateAsync(IReadOnlyList<GeoPoint> points, VehicleSpec? vehicle, CancellationToken cancellationToken)
    {
        if (!IsConfigured || points.Count < 2)
        {
            return null;
        }

        var spec = vehicle ?? VehicleSpec.Default;
        var (status, body) = await PostAsync(RouteFunctionRequest.From(points, spec), cancellationToken);
        var vehicleDropped = false;
        if (status == NoPathStatusCode || (status is >= 400 and < 500 && body != null && body.Contains(NoPathMarker, StringComparison.OrdinalIgnoreCase)))
        {
            /* Truck routing found no path: retry as a plain vehicle and keep only what does not depend on the profile. */
            Logger.LogInformation("Route function found no truck path (HTTP {StatusCode}); retrying without the vehicle.", status);
            (status, body) = await PostAsync(RouteFunctionRequest.From(points, null), cancellationToken);
            vehicleDropped = true;
        }

        if (status is null or < 200 or >= 300 || body == null)
        {
            if (status != null)
            {
                Logger.LogWarning("Route function answered HTTP {StatusCode}.", status);
            }

            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var route = root.GetProperty("route");
            var legs = new List<ProviderLeg> { new(Round(route.GetProperty("distance")), Round(route.GetProperty("duration"))) };
            var polyline = route.TryGetProperty("polyline", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            if (vehicleDropped)
            {
                return new ProviderRoute(legs, polyline, null, null, null, null, VehicleDropped: true);
            }

            int? tollDistance = null;
            decimal? tollAmount = null;
            string? currency = null;
            if (root.TryGetProperty("toll", out var toll) && toll.ValueKind == JsonValueKind.Object)
            {
                tollDistance = toll.TryGetProperty("totalDistance", out var d) && d.ValueKind == JsonValueKind.Number ? Round(d) : null;
                tollAmount = toll.TryGetProperty("totalCost", out var c) && c.ValueKind == JsonValueKind.Number ? Math.Round(c.GetDecimal(), RoutingConsts.MoneyScale, MidpointRounding.AwayFromZero) : null;
                currency = toll.TryGetProperty("currency", out var cur) && cur.ValueKind == JsonValueKind.String ? cur.GetString()?.ToUpperInvariant() : null;
                if (currency is { Length: > RoutingConsts.MaxCurrencyCodeLength })
                {
                    currency = null;
                }
            }

            decimal? co2 = null;
            if (root.TryGetProperty("carbonFootprint", out var footprint) && footprint.ValueKind == JsonValueKind.Object
                && footprint.TryGetProperty("totalKgCO2e", out var kg) && kg.ValueKind == JsonValueKind.Number)
            {
                co2 = Math.Round(kg.GetDecimal(), RoutingConsts.Co2Scale, MidpointRounding.AwayFromZero);
            }

            return new ProviderRoute(legs, polyline, tollDistance, tollAmount, currency, co2);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            Logger.LogWarning("Route function answered an unexpected shape: {Error}.", e.GetType().Name);
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
            using var request = new HttpRequestMessage(HttpMethod.Get, _options.RouteFunction.BaseUrl);
            AddKey(request);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            /* The function takes POSTs; a 404 or 405 on GET still proves the host answers. A 5xx does not. */
            return (int)response.StatusCode < 500;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<(int? Status, string? Body)> PostAsync(RouteFunctionRequest payload, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.Timeout);
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.RouteFunction.BaseUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
            };
            AddKey(request);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.LogWarning("Route function timed out after {TimeoutSeconds} s.", _options.Timeout.TotalSeconds);
            return (null, null);
        }
        catch (HttpRequestException e)
        {
            Logger.LogWarning("Route function could not be reached: {RequestError}.", e.HttpRequestError);
            return (null, null);
        }
    }

    private void AddKey(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_options.RouteFunction.ApiKey) && !string.IsNullOrWhiteSpace(_options.RouteFunction.ApiKeyHeader))
        {
            request.Headers.TryAddWithoutValidation(_options.RouteFunction.ApiKeyHeader, _options.RouteFunction.ApiKey);
        }
    }

    private static int Round(JsonElement value) => (int)Math.Round(value.GetDouble(), MidpointRounding.AwayFromZero);

    /// <summary>The request body of the function (assessment §1.4). Coordinates are <c>{ lat, lon }</c> objects.</summary>
    public sealed record RouteFunctionRequest(
        RouteFunctionPoint Origin,
        RouteFunctionPoint Destination,
        IReadOnlyList<RouteFunctionPoint> Waypoints,
        RouteFunctionVehicle? Vehicle)
    {
        public static RouteFunctionRequest From(IReadOnlyList<GeoPoint> points, VehicleSpec? vehicle) => new(
            RouteFunctionPoint.From(points[0]),
            RouteFunctionPoint.From(points[^1]),
            points.Skip(1).Take(points.Count - 2).Select(RouteFunctionPoint.From).ToList(),
            vehicle == null ? null : RouteFunctionVehicle.From(vehicle));
    }

    public sealed record RouteFunctionPoint(double Lat, double Lon)
    {
        public static RouteFunctionPoint From(GeoPoint point) => new(point.Latitude, point.Longitude);
    }

    /// <summary>
    /// The vehicle block as the legacy <c>TransportFileRouteCalculator</c> sends it (verified against
    /// <c>RouteVehicleSpecMapper.cs</c> on 2026-10-25): weight in <b>tonnes</b>, the Euro class as <c>EURO_0</c>…<c>EURO_VI</c>
    /// (EEV counts as Euro V, zero emission as Euro VI), dimensions in metres, the fuel as one of the GLEC names
    /// <c>Diesel</c>, <c>Petrol</c>, <c>Electric</c>, <c>CNG</c> (left out when not confidently mappable), consumption in
    /// litres per kilometre when known.
    /// </summary>
    public sealed record RouteFunctionVehicle(double Weight, string EuroClass, double? Height, double? Width, double? Length, string? FuelType, double? FuelConsumption)
    {
        public static RouteFunctionVehicle From(VehicleSpec vehicle) => new(
            Math.Round(vehicle.GrossWeightKg / 1000d, 3),
            EuroClassName(vehicle.EuroClass ?? MasterData.Fleet.EuroClass.Euro6),
            vehicle.HeightCm is { } h ? h / 100d : null,
            vehicle.WidthCm is { } w ? w / 100d : null,
            vehicle.LengthCm is { } l ? l / 100d : null,
            FuelTypeName(vehicle.FuelType ?? MasterData.Fleet.FuelType.Diesel),
            vehicle.FuelConsumptionLPer100Km is { } c and > 0 ? Math.Round(c / 100d, 4) : null);

        public static string EuroClassName(EuroClass euroClass) => euroClass switch
        {
            MasterData.Fleet.EuroClass.Euro0 => "EURO_0",
            MasterData.Fleet.EuroClass.Euro1 => "EURO_I",
            MasterData.Fleet.EuroClass.Euro2 => "EURO_II",
            MasterData.Fleet.EuroClass.Euro3 => "EURO_III",
            MasterData.Fleet.EuroClass.Euro4 => "EURO_IV",
            MasterData.Fleet.EuroClass.Euro5 => "EURO_V",
            MasterData.Fleet.EuroClass.Eev => "EURO_V",
            _ => "EURO_VI"
        };

        /// <summary>The GLEC fuel names the route function knows; other fuels are left out so the function applies its default.</summary>
        public static string? FuelTypeName(FuelType fuelType) => fuelType switch
        {
            MasterData.Fleet.FuelType.Diesel => "Diesel",
            MasterData.Fleet.FuelType.Petrol => "Petrol",
            MasterData.Fleet.FuelType.Electric => "Electric",
            MasterData.Fleet.FuelType.Cng => "CNG",
            _ => null
        };
    }
}
