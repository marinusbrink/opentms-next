using OpenTms.MasterData.Fleet;
using Shouldly;
using Xunit;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// The cache key of design 60 ("from/to rounded to 4 decimals + vehicle class + provider + options"): the same lane
/// within eleven metres is the same key, a different truck, provider or set of asked parts is a different one, and the
/// key fits its column in any culture.
/// </summary>
public class RouteCacheKeyTests
{
    [Fact]
    public void Coordinates_are_rounded_to_four_decimals_in_the_invariant_culture()
    {
        using (new CultureScope("nl-NL"))
        {
            var key = RouteCacheKey.Build(new GeoPoint(52.37024999, 4.89519), new GeoPoint(51.92250001, 4.4791749), null, "Osrm", RoutingOptions.DistanceOnly);

            key.ShouldBe("52.3702,4.8952>51.9225,4.4792|none|Osrm|-");
        }
    }

    [Fact]
    public void Points_within_rounding_distance_share_the_key_and_the_direction_matters()
    {
        var a = new GeoPoint(52.37020, 4.89520);
        var b = new GeoPoint(51.92250, 4.47917);
        var aNearby = new GeoPoint(52.37022, 4.89518);

        RouteCacheKey.Build(a, b, null, "Osrm", RoutingOptions.DistanceOnly).ShouldBe(RouteCacheKey.Build(aNearby, b, null, "Osrm", RoutingOptions.DistanceOnly));
        RouteCacheKey.Build(a, b, null, "Osrm", RoutingOptions.DistanceOnly).ShouldNotBe(RouteCacheKey.Build(b, a, null, "Osrm", RoutingOptions.DistanceOnly));
    }

    [Fact]
    public void The_vehicle_class_carries_weight_euro_class_fuel_dimensions_and_consumption()
    {
        RouteCacheKey.VehicleClass(new VehicleSpec(18_000, EuroClass.Euro5, FuelType.Lng, 28.56, 400, 255, 1650)).ShouldBe("18000kg;Euro5;Lng;400x255x1650;28.6");
        RouteCacheKey.VehicleClass(VehicleSpec.Default).ShouldBe("40000kg;Euro6;Diesel;-x-x-;-");
        RouteCacheKey.VehicleClass(null).ShouldBe(RouteCacheKey.NoVehicle);
    }

    [Fact]
    public void Options_and_provider_make_different_keys()
    {
        var a = new GeoPoint(52.3702, 4.8952);
        var b = new GeoPoint(51.9225, 4.47917);

        RouteCacheKey.Flags(RoutingOptions.DistanceOnly).ShouldBe("-");
        RouteCacheKey.Flags(new RoutingOptions(IncludeGeometry: true, IncludeToll: true, IncludeCo2: true)).ShouldBe("gtc");
        RouteCacheKey.Build(a, b, null, "Osrm", new RoutingOptions(IncludeToll: true)).ShouldEndWith("|Osrm|t");
        RouteCacheKey.Build(a, b, null, "Osrm+RouteFunction", RoutingOptions.DistanceOnly).ShouldNotBe(RouteCacheKey.Build(a, b, null, "Osrm", RoutingOptions.DistanceOnly));
    }

    [Fact]
    public void The_longest_key_fits_the_column()
    {
        var key = RouteCacheKey.Build(
            new GeoPoint(-89.99999, -179.99999), new GeoPoint(89.99999, 179.99999),
            new VehicleSpec(int.MaxValue, EuroClass.ZeroEmission, FuelType.DieselElectricHybrid, 999999.9, int.MaxValue, int.MaxValue, int.MaxValue),
            "Osrm+RouteFunction", new RoutingOptions(true, true, true));

        key.Length.ShouldBeLessThanOrEqualTo(RoutingConsts.MaxKeyLength);
    }
}
