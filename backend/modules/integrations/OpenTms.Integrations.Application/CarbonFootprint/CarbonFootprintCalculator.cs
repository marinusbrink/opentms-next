using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OpenTms.Integrations.Routing;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Integrations.CarbonFootprint;

/// <summary>
/// CO₂e of a trip and its legs (design 60, "Carbon footprint"; PO decision Q10). The trip's total is the route function's
/// GLEC v3 / ISO 14083 figure for the stops with the vehicle spec (Tier 2 when the consumption is known, else the
/// function's Tier 1 defaults by class); it is allocated over the legs by tonne-kilometre — the leg's weight times the road
/// distance of the stop span it covers, from the routing matrix — with equal shares when no leg has a weight (COFRET, as
/// OpenTMS intended). Without the function, <see cref="GreatCircleCo2Fallback"/> estimates each leg from great-circle
/// distance, tonnes and the tenant's CPI. Per-leg figures are rounded to three decimals and sum to the total exactly.
/// </summary>
public class CarbonFootprintCalculator : ICarbonFootprintCalculator, ITransientDependency
{
    public const string FactorSetRouteFunction = "GLEC v3 / ISO 14083";

    private readonly IRoutingService _routing;
    private readonly IRouteFunction _function;
    private readonly GreatCircleCo2Fallback _fallback;

    public CarbonFootprintCalculator(IRoutingService routing, IRouteFunction function, IOptions<CarbonFootprintOptions> options)
    {
        _routing = routing;
        _function = function;
        _fallback = new GreatCircleCo2Fallback(options.Value.CpiGramPerTonneKm);
    }

    public async Task<Co2Estimate> EstimateTripAsync(IReadOnlyList<GeoPoint> stops, VehicleSpec? vehicle, IReadOnlyList<LegLoad> legs, CancellationToken cancellationToken = default)
    {
        var pairs = Math.Max(0, stops.Count - 1);
        var greatCircleM = new double[pairs];
        for (var i = 0; i < pairs; i++)
        {
            greatCircleM[i] = GreatCircle.DistanceM(stops[i], stops[i + 1]);
        }

        if (pairs == 0)
        {
            return new Co2Estimate(0m, legs.Select(_ => 0m).ToList(), Co2Estimate.MethodGreatCircleCpi, _fallback.FactorSet);
        }

        var function = _function.IsConfigured ? await _function.CalculateAsync(stops, vehicle, cancellationToken) : null;
        if (function?.Co2Kg is { } totalKg)
        {
            /* Road distance per stop pair from the matrix (cached per pair); great circle for a pair the engine cannot route. */
            var matrix = await _routing.GetMatrixAsync(stops, vehicle, cancellationToken);
            var roadM = new double[pairs];
            for (var i = 0; i < pairs; i++)
            {
                roadM[i] = matrix != null && matrix.Legs.Count == pairs ? matrix.Legs[i].DistanceM : greatCircleM[i];
            }

            var perLeg = Allocate(totalKg, legs, legs.Select(l => SpanDistance(roadM, l)).ToList());
            return new Co2Estimate(totalKg, perLeg, Co2Estimate.MethodRouteFunction, FactorSetRouteFunction);
        }

        var perLegFallback = legs.Select(l => _fallback.EstimateKg(SpanDistance(greatCircleM, l), l.WeightKg)).ToList();
        return new Co2Estimate(perLegFallback.Sum(), perLegFallback, Co2Estimate.MethodGreatCircleCpi, _fallback.FactorSet);
    }

    public Co2Estimate EstimateLegFallback(GeoPoint from, GeoPoint to, double? weightKg)
    {
        var kg = _fallback.EstimateKg(GreatCircle.DistanceM(from, to), weightKg);
        return new Co2Estimate(kg, [kg], Co2Estimate.MethodGreatCircleCpi, _fallback.FactorSet);
    }

    /// <summary>
    /// Splits <paramref name="totalKg"/> over the legs by weight × distance; equal shares when no weight is known (or
    /// every share is zero). Rounded to three decimals, the last leg absorbing the rounding residue.
    /// </summary>
    public static IReadOnlyList<decimal> Allocate(decimal totalKg, IReadOnlyList<LegLoad> legs, IReadOnlyList<double> legDistancesM)
    {
        if (legs.Count == 0)
        {
            return [];
        }

        var anyWeight = legs.Any(l => l.WeightKg is > 0);
        var shares = new double[legs.Count];
        for (var i = 0; i < legs.Count; i++)
        {
            shares[i] = anyWeight ? Math.Max(0, legs[i].WeightKg ?? 0) * legDistancesM[i] : 1d;
        }

        var sum = shares.Sum();
        if (sum <= 0)
        {
            Array.Fill(shares, 1d);
            sum = legs.Count;
        }

        var result = new decimal[legs.Count];
        var allocated = 0m;
        for (var i = 0; i < legs.Count - 1; i++)
        {
            result[i] = Math.Round(totalKg * (decimal)(shares[i] / sum), RoutingConsts.Co2Scale, MidpointRounding.AwayFromZero);
            allocated += result[i];
        }

        result[^1] = totalKg - allocated;
        return result;
    }

    /// <summary>The distance of the stop span a leg covers: the sum of the pairs from its first to its last stop.</summary>
    public static double SpanDistance(double[] pairDistancesM, LegLoad leg)
    {
        var total = 0d;
        for (var i = Math.Max(0, leg.FromStopIndex); i < Math.Min(leg.ToStopIndex, pairDistancesM.Length); i++)
        {
            total += pairDistancesM[i];
        }

        return total;
    }
}

/// <summary>
/// The last resort of design 60: great-circle kilometres × tonnes × the tenant's accounting CPI (grams CO₂e per
/// tonne-kilometre, <c>Integrations:CarbonFootprint:CpiGramPerTonneKm</c>, default 62). A leg without a weight counts as
/// one tonne, so the figure is CPI × kilometres (OpenTMS's "amount × distance" with amount 1).
/// </summary>
public sealed class GreatCircleCo2Fallback
{
    public const double DefaultWeightKg = 1_000d;

    public GreatCircleCo2Fallback(decimal cpiGramPerTonneKm)
    {
        CpiGramPerTonneKm = cpiGramPerTonneKm;
        FactorSet = "CPI " + cpiGramPerTonneKm.ToString("0.##", CultureInfo.InvariantCulture) + " g/tkm";
    }

    public decimal CpiGramPerTonneKm { get; }

    public string FactorSet { get; }

    public decimal EstimateKg(double distanceM, double? weightKg)
    {
        var tonnes = (weightKg is > 0 ? weightKg.Value : DefaultWeightKg) / 1_000d;
        var grams = (decimal)(distanceM / 1_000d * tonnes) * CpiGramPerTonneKm;
        return Math.Round(grams / 1_000m, RoutingConsts.Co2Scale, MidpointRounding.AwayFromZero);
    }
}
