using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// One routing engine behind <see cref="RoutingService"/> (design 60, "Providers"). Registered in the module and chosen
/// by <see cref="Name"/> from <c>Integrations:Routing:Provider</c>; no factories, no reflection. A provider answers null
/// for "not configured" and "no path" only; a timeout, an unreachable host, an error status or an answer of the wrong
/// shape is logged at warning with the status code only and thrown as <see cref="RoutingUnavailableException"/>, so the
/// calculation job fails and is retried with backoff (constitution rule 3).
/// </summary>
public interface IRouteProvider
{
    string Name { get; }

    bool IsConfigured { get; }

    /// <summary>The route through <paramref name="points"/> in order: one leg per consecutive pair, geometry only when asked.</summary>
    Task<ProviderRoute?> GetRouteAsync(IReadOnlyList<GeoPoint> points, VehicleSpec? vehicle, bool includeGeometry, CancellationToken cancellationToken);

    /// <summary>Distance and duration between every pair of <paramref name="points"/> (row = from, column = to).</summary>
    Task<ProviderTable?> GetTableAsync(IReadOnlyList<GeoPoint> points, VehicleSpec? vehicle, CancellationToken cancellationToken);

    /// <summary>One small call that proves the engine answers; false for an unreachable host or a non-JSON answer.</summary>
    Task<bool> ProbeAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The toll and CO₂e source (design 60: the Valhalla route function, always, when configured). Separate from
/// <see cref="IRouteProvider"/> because the distance provider may be another engine.
/// </summary>
public interface IRouteFunction
{
    bool IsConfigured { get; }

    /// <summary>The host name for the status endpoint; never the full URL.</summary>
    string? Host { get; }

    /// <summary>The route through the points with toll and CO₂e for <paramref name="vehicle"/> (the default truck when null).</summary>
    Task<ProviderRoute?> CalculateAsync(IReadOnlyList<GeoPoint> points, VehicleSpec? vehicle, CancellationToken cancellationToken);

    Task<bool> ProbeAsync(CancellationToken cancellationToken);
}

/// <summary>Metres and seconds as the engine returned them, rounded to the unit only.</summary>
public sealed record ProviderLeg(int DistanceM, int DurationS);

/// <summary>
/// An engine's answer. <see cref="Legs"/> has one element per consecutive pair of the asked points (the route function
/// knows only the total, so it answers one leg). Toll and CO₂ are null when the engine does not know them, or when the
/// truck profile had to be dropped (<see cref="VehicleDropped"/>).
/// </summary>
public sealed record ProviderRoute(
    IReadOnlyList<ProviderLeg> Legs,
    string? EncodedPolyline,
    int? TollDistanceM,
    decimal? TollAmount,
    string? TollCurrencyCode,
    decimal? Co2Kg,
    bool VehicleDropped = false)
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

/// <summary>A distance and duration matrix over the asked points; <c>null</c> cells are unreachable pairs.</summary>
public sealed record ProviderTable(ProviderLeg?[][] Cells)
{
    public ProviderLeg? this[int from, int to] => Cells[from][to];
}
