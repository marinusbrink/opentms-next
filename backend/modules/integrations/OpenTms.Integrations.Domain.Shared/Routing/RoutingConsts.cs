namespace OpenTms.Integrations.Routing;

/// <summary>Column lengths, scales and provider names of the route cache (design 60, "Route cache").</summary>
public static class RoutingConsts
{
    /// <summary>Two rounded coordinate pairs, the vehicle class, the provider and the option flags.</summary>
    public const int MaxKeyLength = 160;

    public const int MaxProviderLength = 32;

    /// <summary>ISO 4217.</summary>
    public const int MaxCurrencyCodeLength = 3;

    /// <summary>Coordinates in a cache key are rounded to four decimals (about 11 m): the same lane gives the same key.</summary>
    public const int KeyCoordinateDecimals = 4;

    public const int MoneyPrecision = 18;

    public const int MoneyScale = 2;

    public const int Co2Precision = 18;

    /// <summary>CO₂ in kilograms with three decimals (design 56, "Measures").</summary>
    public const int Co2Scale = 3;

    /// <summary>The value of <c>Integrations:Routing:Provider</c> for OSRM (the default).</summary>
    public const string OsrmProviderName = "Osrm";

    /// <summary>The value of <c>Integrations:Routing:Provider</c> for the Valhalla route function.</summary>
    public const string RouteFunctionProviderName = "RouteFunction";
}
