using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.MasterData.Fleet;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// Distances, durations, geometry and toll between coordinates, from the tenant's routing provider behind one adapter
/// (AD-12, design 60; PO decision Q4: OSRM for distances, the Valhalla route function for toll and CO₂). In-process only,
/// called from calculator jobs, never from a request. Null means "no route" (unreachable, not configured, provider down):
/// the caller stores nothing and records a warning; a retry comes with the next calculation.
/// </summary>
public interface IRoutingService
{
    Task<RouteResult?> GetRouteAsync(GeoPoint from, GeoPoint to, VehicleSpec? vehicle, RoutingOptions options, CancellationToken cancellationToken = default);

    /// <summary>The route through the points in order: one element per consecutive pair, or null when any pair fails.</summary>
    Task<RouteMatrix?> GetMatrixAsync(IReadOnlyList<GeoPoint> points, VehicleSpec? vehicle, CancellationToken cancellationToken = default);

    Task<RoutingStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>WGS84 coordinates.</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude);

/// <summary>What the truck profile and the toll and CO₂ figures depend on (design 60 "Vehicle spec source").</summary>
public sealed record VehicleSpec(
    int GrossWeightKg,
    EuroClass? EuroClass,
    FuelType? FuelType,
    double? FuelConsumptionLPer100Km = null,
    int? HeightCm = null,
    int? WidthCm = null,
    int? LengthCm = null)
{
    /// <summary>The OpenTMS default when no vehicle is known: 40 t, Euro VI, diesel.</summary>
    public static VehicleSpec Default => new(40_000, MasterData.Fleet.EuroClass.Euro6, MasterData.Fleet.FuelType.Diesel);
}

public sealed record RoutingOptions(bool IncludeGeometry = false, bool IncludeToll = false, bool IncludeCo2 = false)
{
    public static RoutingOptions DistanceOnly => new();
}

public sealed record RouteResult(
    int DistanceM,
    int DurationS,
    string? EncodedPolyline,
    int? TollDistanceM,
    decimal? TollAmount,
    string? TollCurrencyCode,
    decimal? Co2Kg,
    string Provider,
    DateTimeOffset At,
    bool FromCache);

public sealed record RouteMatrix(IReadOnlyList<RouteResult> Legs, string Provider, DateTimeOffset At)
{
    public int TotalDistanceM
    {
        get
        {
            var total = 0;
            foreach (var leg in Legs)
            {
                total += leg.DistanceM;
            }

            return total;
        }
    }

    public int TotalDurationS
    {
        get
        {
            var total = 0;
            foreach (var leg in Legs)
            {
                total += leg.DurationS;
            }

            return total;
        }
    }
}

/// <summary>The status endpoint's figures: the provider name, whether it answered the last probe, cache statistics. No URLs or keys.</summary>
public sealed record RoutingStatus(string Provider, bool Configured, bool Reachable, string? RouteFunctionHost, long CacheRows, DateTimeOffset? LastProbeAt);
