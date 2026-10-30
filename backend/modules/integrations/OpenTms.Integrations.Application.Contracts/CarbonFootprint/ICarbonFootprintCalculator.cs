using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.Integrations.Routing;

namespace OpenTms.Integrations.CarbonFootprint;

/// <summary>
/// CO₂ equivalents of a trip and its legs (design 60 "Carbon footprint", PO decision Q10): GLEC v3 / ISO 14083 through
/// the route function when configured, with the per-leg allocation by tonne-kilometre (COFRET) that OpenTMS intended;
/// a great-circle × CPI fallback otherwise. In-process only, called from Planning's calculators.
/// </summary>
public interface ICarbonFootprintCalculator
{
    /// <summary>The trip's total and its allocation over <paramref name="legs"/> (same order), or the great-circle fallback when the route function is not configured, has no path or is down (it never throws <see cref="RoutingUnavailableException"/>).</summary>
    Task<Co2Estimate> EstimateTripAsync(IReadOnlyList<GeoPoint> stops, VehicleSpec? vehicle, IReadOnlyList<LegLoad> legs, CancellationToken cancellationToken = default);

    /// <summary>The estimate of one unplanned leg: great-circle distance × tonnes × the tenant's CPI (grams per tonne-kilometre).</summary>
    Co2Estimate EstimateLegFallback(GeoPoint from, GeoPoint to, double? weightKg);
}

/// <summary>A leg's load and route for the allocation: the weight it carries and the stops it spans (indexes into the trip's stops).</summary>
public sealed record LegLoad(double? WeightKg, int FromStopIndex, int ToStopIndex);

public sealed record Co2Estimate(decimal TotalKg, IReadOnlyList<decimal> PerLegKg, string Method, string FactorSet)
{
    public const string MethodRouteFunction = "Glec3RouteFunction";
    public const string MethodGreatCircleCpi = "GreatCircleCpi";
}
