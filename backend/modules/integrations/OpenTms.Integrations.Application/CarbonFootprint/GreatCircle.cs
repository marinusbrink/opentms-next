using System;
using OpenTms.Integrations.Routing;

namespace OpenTms.Integrations.CarbonFootprint;

/// <summary>The great-circle (haversine) distance on the WGS84 mean sphere; the fallback when no road distance is known.</summary>
public static class GreatCircle
{
    /// <summary>The IUGG mean Earth radius in metres.</summary>
    public const double EarthRadiusM = 6_371_008.8;

    public static double DistanceM(GeoPoint from, GeoPoint to)
    {
        var lat1 = Radians(from.Latitude);
        var lat2 = Radians(to.Latitude);
        var dLat = lat2 - lat1;
        var dLon = Radians(to.Longitude - from.Longitude);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusM * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180d;
}
