using System;
using System.Globalization;
using System.Text;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// The cache key of one routing question (design 60, "Route cache"): both coordinate pairs rounded to four decimals, the
/// vehicle class, the provider that answered and the option flags. Pure, so the same lane with the same truck always
/// reads the same row; a different provider or a request for geometry, toll or CO₂ is a different row.
/// </summary>
public static class RouteCacheKey
{
    public const string NoVehicle = "none";

    public static string Build(GeoPoint from, GeoPoint to, VehicleSpec? vehicle, string provider, RoutingOptions options)
    {
        var builder = new StringBuilder(RoutingConsts.MaxKeyLength);
        Append(builder, from);
        builder.Append('>');
        Append(builder, to);
        builder.Append('|').Append(VehicleClass(vehicle));
        builder.Append('|').Append(provider);
        builder.Append('|').Append(Flags(options));
        return builder.ToString();
    }

    /// <summary>What toll and CO₂ depend on: weight, Euro class, fuel, dimensions and consumption; <see cref="NoVehicle"/> when none.</summary>
    public static string VehicleClass(VehicleSpec? vehicle)
    {
        if (vehicle == null)
        {
            return NoVehicle;
        }

        return string.Join(';',
            vehicle.GrossWeightKg.ToString(CultureInfo.InvariantCulture) + "kg",
            vehicle.EuroClass?.ToString() ?? "-",
            vehicle.FuelType?.ToString() ?? "-",
            Dimension(vehicle.HeightCm) + "x" + Dimension(vehicle.WidthCm) + "x" + Dimension(vehicle.LengthCm),
            vehicle.FuelConsumptionLPer100Km is { } c ? c.ToString("0.#", CultureInfo.InvariantCulture) : "-");
    }

    public static string Flags(RoutingOptions options)
    {
        var flags = (options.IncludeGeometry ? "g" : string.Empty) + (options.IncludeToll ? "t" : string.Empty) + (options.IncludeCo2 ? "c" : string.Empty);
        return flags.Length == 0 ? "-" : flags;
    }

    public static string Coordinate(double value)
        => Math.Round(value, RoutingConsts.KeyCoordinateDecimals, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture);

    private static void Append(StringBuilder builder, GeoPoint point)
        => builder.Append(Coordinate(point.Latitude)).Append(',').Append(Coordinate(point.Longitude));

    private static string Dimension(int? cm) => cm?.ToString(CultureInfo.InvariantCulture) ?? "-";
}
