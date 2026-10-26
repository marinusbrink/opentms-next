using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.Integrations.CarbonFootprint;
using OpenTms.Integrations.Routing;
using OpenTms.MasterData.Published;
using OpenTms.PlanningExecution.Events;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.Settings;
using OpenTms.Platform.DerivedValues;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Settings;
using Volo.Abp.Timing;

namespace OpenTms.PlanningExecution.TransportTrips;

/// <summary>
/// Design 59 "Calculators", <c>PlanningExecution.TransportTrip</c> (AD-07, AD-20): (a) the stops' planned times and
/// cumulative distances from one matrix call, (b) load factor and loading metres against the vehicles' capacity,
/// (c) the warnings, (d) CO₂ for the trip with the per-leg allocation, (e) the cost and revenue allocation over the legs
/// from Financial's stored rate lines. Every value is written as a plain column; the job's transaction holds them until
/// the guarded completion. A routing provider that is down throws <see cref="RoutingUnavailableException"/>, which fails
/// the job (old values and warnings stay, the state records <c>OpenTms.Integrations:RoutingUnavailable</c>, Hangfire
/// retries); one that answers null (not configured, no path) leaves the old times and warns <c>RoutingNotConfigured</c>.
/// No <c>RoutingUnavailable</c> warning exists: a failed job rolls back, so the trip could never store one; the
/// derived-value state carries that code instead. CO₂ falls back to great circle when only the route function is down.
/// </summary>
public class TripCalculator : IDerivedValueCalculator, ITransientDependency
{
    public ILogger<TripCalculator> Logger { get; set; } = NullLogger<TripCalculator>.Instance;

    private readonly IRepository<TransportTrip, Guid> _trips;
    private readonly IRepository<Leg, Guid> _legs;
    private readonly IRoutingService _routing;
    private readonly ICarbonFootprintCalculator _carbon;
    private readonly IRateLineReader _rateLines;
    private readonly MasterDataReferences _masterData;
    private readonly ISettingProvider _settings;
    private readonly IDistributedEventBus _events;
    private readonly IClock _clock;

    public TripCalculator(
        IRepository<TransportTrip, Guid> trips,
        IRepository<Leg, Guid> legs,
        IRoutingService routing,
        ICarbonFootprintCalculator carbon,
        IRateLineReader rateLines,
        MasterDataReferences masterData,
        ISettingProvider settings,
        IDistributedEventBus events,
        IClock clock)
    {
        _trips = trips;
        _legs = legs;
        _routing = routing;
        _carbon = carbon;
        _rateLines = rateLines;
        _masterData = masterData;
        _settings = settings;
        _events = events;
        _clock = clock;
    }

    public async Task CalculateAsync(DerivedValueCalculationContext context)
    {
        var trip = await _trips.AsyncExecuter.FirstOrDefaultAsync((await _trips.WithDetailsAsync()).Where(t => t.Id == context.TargetId), context.CancellationToken);
        if (trip == null)
        {
            Logger.LogDebug("Trip {TripId} no longer exists; nothing to calculate.", context.TargetId);
            return;
        }

        var legs = (await _legs.GetListAsync(l => l.TripId == trip.Id, cancellationToken: context.CancellationToken))
            .OrderBy(l => l.TripPosition ?? int.MaxValue).ThenBy(l => l.Id).ToList();
        var warnings = new List<TripWarning>();
        await CarryOverChangedLegWarningsAsync(trip, warnings);

        var vehicleIds = await _masterData.TripVehicleIdsAsync(trip.VehicleCombinationId, trip.Vehicles.OrderBy(v => v.Position).Select(v => v.VehicleId));
        var vehiclesById = await _masterData.VehiclesAsync(vehicleIds);
        var vehicles = vehicleIds.Where(vehiclesById.ContainsKey).Select(id => vehiclesById[id]).ToList();
        var spec = MasterDataReferences.SpecOf(vehicles);

        var route = await CalculateStopTimesAsync(trip, legs, spec, warnings, context);
        CalculateLoad(trip, legs, vehicles, warnings);
        await AddCompatibilityWarningsAsync(trip, legs, vehicles, warnings);
        await EstimateCo2Async(trip, legs, spec, route, context);
        var orderIds = await AllocateAsync(trip, legs, warnings, context.Version);

        trip.SetWarnings(warnings);
        trip.MarkCalculated(Now);
        await _trips.UpdateAsync(trip);
        await _legs.UpdateManyAsync(legs);
        await _events.PublishAsync(new TransportTripRecalculatedEto { TripId = trip.Id, Version = context.Version, OrderIds = orderIds });
        Logger.LogInformation("Trip {TripId} v{Version} calculated: {StopCount} stops, {LegCount} legs, {WarningCount} warnings.", trip.Id, context.Version, trip.Stops.Count, legs.Count, warnings.Count);
    }

    private DateTimeOffset Now => new(_clock.Now.ToUniversalTime(), TimeSpan.Zero);

    /// <summary>A <c>LegChangedAfterPlanning</c> warning stays until the leg is planned again or gone.</summary>
    private async Task CarryOverChangedLegWarningsAsync(TransportTrip trip, List<TripWarning> warnings)
    {
        var carried = trip.Warnings.Where(w => w.Code == TripWarningCode.LegChangedAfterPlanning && w.LegId != null).ToList();
        if (carried.Count == 0)
        {
            return;
        }

        var ids = carried.Select(w => w.LegId!.Value).Distinct().ToList();
        var unplanned = (await _legs.GetListAsync(l => ids.Contains(l.Id) && l.TripId == null)).Select(l => l.Id).ToHashSet();
        warnings.AddRange(carried.Where(w => unplanned.Contains(w.LegId!.Value)));
    }

    private sealed record RoutePoint(GeoPoint Point, TripStop? Stop);

    private sealed record TripRoute(List<RoutePoint> Points, RouteMatrix Matrix);

    /// <summary>(a) From the planned departure through the stops: one matrix call, speed-corrected durations, stays per stop.</summary>
    private async Task<TripRoute?> CalculateStopTimesAsync(TransportTrip trip, List<Leg> legs, VehicleSpec spec, List<TripWarning> warnings, DerivedValueCalculationContext context)
    {
        var stops = trip.OrderedStops.ToList();
        foreach (var stop in stops.Where(s => !s.HasCoordinates))
        {
            warnings.Add(new TripWarning(TripWarningCode.AddressNotGeocoded, null, stop.Id));
        }

        foreach (var leg in legs.Where(l => !l.HasCoordinates))
        {
            warnings.Add(new TripWarning(TripWarningCode.AddressNotGeocoded, leg.Id));
        }

        var points = new List<RoutePoint>();
        var departureAddress = trip.FirstLastStopRule.HasFlag(FirstLastStopRule.DepartureIsFirstStop) ? null : await PlaceAsync(trip.DepartureAddressId);
        var arrivalAddress = trip.FirstLastStopRule.HasFlag(FirstLastStopRule.ArrivalIsLastStop) ? null : await PlaceAsync(trip.ArrivalAddressId);
        if (departureAddress != null)
        {
            points.Add(new RoutePoint(departureAddress.Value, null));
        }

        points.AddRange(stops.Where(s => s.HasCoordinates).Select(s => new RoutePoint(new GeoPoint(s.Latitude!.Value, s.Longitude!.Value), s)));
        if (arrivalAddress != null)
        {
            points.Add(new RoutePoint(arrivalAddress.Value, null));
        }

        if (points.Count < 2)
        {
            trip.SetPlannedRoute(null, null, null);
            return null;
        }

        var matrix = await _routing.GetMatrixAsync(points.Select(p => p.Point).ToList(), spec, context.CancellationToken);
        if (matrix == null || matrix.Legs.Count != points.Count - 1)
        {
            warnings.Add(new TripWarning(TripWarningCode.RoutingNotConfigured));
            return null;
        }

        var factor = await SpeedCorrectionFactorAsync();
        var time = trip.PlannedDeparture;
        var cumulative = 0;
        for (var i = 0; i < points.Count; i++)
        {
            if (i > 0)
            {
                var segment = matrix.Legs[i - 1];
                cumulative += segment.DistanceM;
                time = time.AddSeconds(Math.Round(segment.DurationS * factor, MidpointRounding.AwayFromZero));
            }

            if (points[i].Stop is { } stop)
            {
                var departure = time.AddMinutes(stop.PlannedDurationMin ?? 0);
                stop.SetPlanned(time, departure, cumulative);
                AddWindowWarnings(stop, legs, time, warnings);
                time = departure;
            }
        }

        var arrivalKnown = arrivalAddress != null || trip.FirstLastStopRule.HasFlag(FirstLastStopRule.ArrivalIsLastStop);
        trip.SetPlannedRoute(cumulative, (int)(time - trip.PlannedDeparture).TotalSeconds, arrivalKnown ? time : null);
        return new TripRoute(points, matrix);
    }

    private static void AddWindowWarnings(TripStop stop, List<Leg> legs, DateTimeOffset arrival, List<TripWarning> warnings)
    {
        foreach (var link in stop.Legs)
        {
            var leg = legs.FirstOrDefault(l => l.Id == link.LegId);
            if (leg == null)
            {
                continue;
            }

            var end = leg.End(link.Role);
            if ((end.WindowFrom != null && arrival < end.WindowFrom) || (end.WindowTill != null && arrival > end.WindowTill))
            {
                warnings.Add(new TripWarning(TripWarningCode.EtaOutsideStopWindow, leg.Id, stop.Id));
            }
        }
    }

    private async Task<GeoPoint?> PlaceAsync(Guid? addressId)
    {
        if (addressId == null)
        {
            return null;
        }

        var addresses = await _masterData.AddressesAsync([addressId]);
        return addresses.TryGetValue(addressId.Value, out var address) && address.Latitude != null && address.Longitude != null
            ? new GeoPoint(address.Latitude.Value, address.Longitude.Value)
            : null;
    }

    private async Task<double> SpeedCorrectionFactorAsync()
    {
        var text = await _settings.GetOrNullAsync(PlanningExecutionSettings.SpeedCorrectionPercent);
        var percent = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value is >= 50 and <= 300
            ? value
            : PlanningExecutionConsts.DefaultSpeedCorrectionPercent;
        return percent / 100.0;
    }

    /// <summary>(b) OpenTMS's load factor: the legs' quantities against the vehicles' summed capacity; the worst ratio counts.</summary>
    public static void CalculateLoad(TransportTrip trip, IReadOnlyList<Leg> legs, IReadOnlyList<MasterDataVehicleReferenceDto> vehicles, List<TripWarning> warnings)
    {
        var loadingMeters = legs.Any(l => l.LoadingMeters != null) ? legs.Sum(l => l.LoadingMeters ?? 0) : (double?)null;
        if (vehicles.Count == 0)
        {
            trip.SetLoad(null, loadingMeters);
            return;
        }

        var ratios = new List<(string Measure, double Ratio)>();
        AddRatio(ratios, "epq", legs.Sum(l => l.Epq ?? 0), vehicles.Sum(v => v.MaxEpq ?? 0));
        AddRatio(ratios, "loadingMeters", loadingMeters ?? 0, vehicles.Sum(v => v.MaxLoadingMeters ?? 0));
        AddRatio(ratios, "payloadKg", legs.Sum(l => l.WeightKg ?? 0), vehicles.Sum(v => v.MaxPayloadKg ?? 0));

        var worst = ratios.Count == 0 ? (decimal?)null : (decimal)ratios.Max(r => r.Ratio);
        trip.SetLoad(worst, loadingMeters);
        foreach (var (measure, ratio) in ratios.Where(r => r.Ratio > 1.0001))
        {
            warnings.Add(new TripWarning(TripWarningCode.CapacityExceeded, null, null, measure));
        }
    }

    private static void AddRatio(List<(string, double)> ratios, string measure, double load, double capacity)
    {
        if (capacity > 0 && load > 0)
        {
            ratios.Add((measure, load / capacity));
        }
    }

    /// <summary>(c) Missing characteristics, incompatible goods on one trip, legs outside the trip's window.</summary>
    private async Task AddCompatibilityWarningsAsync(TransportTrip trip, List<Leg> legs, IReadOnlyList<MasterDataVehicleReferenceDto> vehicles, List<TripWarning> warnings)
    {
        var available = vehicles.SelectMany(v => v.TransportCharacteristicIds).ToHashSet();
        foreach (var required in legs.SelectMany(l => l.RequiredTransportCharacteristicIds).Distinct().Where(id => !available.Contains(id)))
        {
            warnings.Add(new TripWarning(TripWarningCode.MissingTransportCharacteristic, null, null, required.ToString()));
        }

        var temperatures = legs.Where(l => l.TemperatureId != null).Select(l => l.TemperatureId!.Value).Distinct().ToList();
        var products = legs.Where(l => l.ProductId != null).Select(l => l.ProductId!.Value).Distinct().ToList();
        var units = legs.Select(l => l.TransportUnitId).Where(id => id != Guid.Empty).Distinct().ToList();
        if (temperatures.Count > 1 || products.Count > 1 || units.Count > 1)
        {
            var pairs = await _masterData.IncompatibilitiesAsync(temperatures, products, units);
            warnings.AddRange(pairs.Temperatures.Select(p => new TripWarning(TripWarningCode.IncompatibleTemperatures, null, null, $"{p.LeftId};{p.RightId}")));
            warnings.AddRange(pairs.Products.Select(p => new TripWarning(TripWarningCode.IncompatibleProducts, null, null, $"{p.LeftId};{p.RightId}")));
            warnings.AddRange(pairs.TransportUnits.Select(p => new TripWarning(TripWarningCode.IncompatibleTransportUnits, null, null, $"{p.LeftId};{p.RightId}")));
        }

        foreach (var leg in legs.Where(l => !l.IsWithinWindow(trip.DepartureDate, trip.ArrivalDate)))
        {
            warnings.Add(new TripWarning(TripWarningCode.LegOutsideTripWindow, leg.Id));
        }
    }

    /// <summary>(d) The trip's CO₂ and its allocation over the legs by tonne-kilometre (COFRET), written on the legs.</summary>
    private async Task EstimateCo2Async(TransportTrip trip, List<Leg> legs, VehicleSpec spec, TripRoute? route, DerivedValueCalculationContext context)
    {
        if (route == null || legs.Count == 0)
        {
            trip.SetCo2(null);
            return;
        }

        var indexOf = new Dictionary<(Guid LegId, StopLegRole Role), int>();
        for (var i = 0; i < route.Points.Count; i++)
        {
            if (route.Points[i].Stop is { } stop)
            {
                foreach (var link in stop.Legs)
                {
                    indexOf[(link.LegId, link.Role)] = i;
                }
            }
        }

        var included = legs.Where(l => indexOf.ContainsKey((l.Id, StopLegRole.Origin)) && indexOf.ContainsKey((l.Id, StopLegRole.Destination))).ToList();
        if (included.Count == 0)
        {
            trip.SetCo2(null);
            return;
        }

        var loads = included.Select(l => new LegLoad(l.WeightKg, indexOf[(l.Id, StopLegRole.Origin)], indexOf[(l.Id, StopLegRole.Destination)])).ToList();
        var estimate = await _carbon.EstimateTripAsync(route.Points.Select(p => p.Point).ToList(), spec, loads, context.CancellationToken);
        trip.SetCo2(estimate.TotalKg);
        for (var i = 0; i < included.Count; i++)
        {
            included[i].SetCo2(i < estimate.PerLegKg.Count ? estimate.PerLegKg[i] : null, estimate.Method, estimate.FactorSet);
        }
    }

    /// <summary>(e) PO decision Q1 / AD-20: trip cost over the legs by distance share, order revenue over the order's legs by distance share.</summary>
    private async Task<List<Guid>> AllocateAsync(TransportTrip trip, List<Leg> legs, List<TripWarning> warnings, long version)
    {
        var orderIds = legs.Select(l => l.OrderId).Distinct().ToList();
        var tripTotals = await _rateLines.GetTripTotalsAsync(trip.Id);
        var cost = tripTotals.CalculationVersion != null || tripTotals.TotalAmount != 0 ? tripTotals.TotalAmount : (decimal?)null;
        if (legs.Count > 0 && (cost ?? 0) == 0 && !tripTotals.HasFixedPrice)
        {
            warnings.Add(new TripWarning(TripWarningCode.NoCostPriceSource));
        }

        var orderTotals = orderIds.Count == 0
            ? new Dictionary<Guid, RateLineTotalsDto>()
            : new Dictionary<Guid, RateLineTotalsDto>(await _rateLines.GetOrderTotalsAsync(orderIds));
        var allLegsOfOrders = orderIds.Count == 0 ? [] : await _legs.GetListAsync(l => orderIds.Contains(l.OrderId));

        var allocation = Allocate(legs, cost, orderTotals, allLegsOfOrders);
        var now = Now;
        var revenue = 0m;
        foreach (var leg in legs)
        {
            var (legCost, legRevenue) = allocation[leg.Id];
            leg.SetAllocation(legCost, legRevenue, version, now);
            revenue += legRevenue ?? 0;
        }

        trip.SetMoney(cost, legs.Count == 0 ? null : revenue);
        return orderIds;
    }

    /// <summary>
    /// Pure allocation (pricing scenarios 62–69 as design 59 corrects them): cost by leg km over the trip's assigned km, equal
    /// shares without distances; revenue per order by leg km over the order's leg km, the full order revenue when the trip
    /// holds all the order's legs and none has a distance, else nothing. Cent-exact: the sum of the parts equals the total.
    /// </summary>
    public static Dictionary<Guid, (decimal? Cost, decimal? Revenue)> Allocate(
        IReadOnlyList<Leg> legsOnTrip,
        decimal? tripCost,
        IReadOnlyDictionary<Guid, RateLineTotalsDto> orderTotals,
        IReadOnlyList<Leg> allLegsOfOrders)
    {
        var result = legsOnTrip.ToDictionary(l => l.Id, _ => ((decimal?)null, (decimal?)null));
        if (legsOnTrip.Count == 0)
        {
            return result;
        }

        if (tripCost != null)
        {
            var weights = legsOnTrip.Any(l => l.DistanceM is > 0)
                ? legsOnTrip.Select(l => (decimal)(l.DistanceM ?? 0)).ToList()
                : legsOnTrip.Select(_ => 1m).ToList();
            var costs = Split(tripCost.Value, weights);
            for (var i = 0; i < legsOnTrip.Count; i++)
            {
                result[legsOnTrip[i].Id] = (costs[i], null);
            }
        }

        foreach (var group in legsOnTrip.GroupBy(l => l.OrderId))
        {
            if (!orderTotals.TryGetValue(group.Key, out var totals))
            {
                continue;
            }

            var orderLegs = allLegsOfOrders.Where(l => l.OrderId == group.Key).ToList();
            var tripLegs = group.ToList();
            var orderKm = orderLegs.Sum(l => (decimal)(l.DistanceM ?? 0));
            decimal[] revenues;
            if (orderKm > 0)
            {
                /* Each leg's share of the order's kilometres; the order's total over all its legs is cent-exact. */
                var shares = Split(totals.TotalAmount, orderLegs.Select(l => (decimal)(l.DistanceM ?? 0)).ToList());
                revenues = tripLegs.Select(l => shares[orderLegs.FindIndex(o => o.Id == l.Id)]).ToArray();
            }
            else if (orderLegs.Count > 0 && orderLegs.All(l => l.TripId == tripLegs[0].TripId))
            {
                revenues = Split(totals.TotalAmount, tripLegs.Select(_ => 1m).ToList()).ToArray();
            }
            else
            {
                revenues = tripLegs.Select(_ => 0m).ToArray();
            }

            for (var i = 0; i < tripLegs.Count; i++)
            {
                result[tripLegs[i].Id] = (result[tripLegs[i].Id].Item1, revenues[i]);
            }
        }

        return result;
    }

    /// <summary>Splits an amount by weights to two decimals; the rounding remainder goes to the largest fractional parts (largest remainder method).</summary>
    public static List<decimal> Split(decimal total, IReadOnlyList<decimal> weights)
    {
        var sum = weights.Sum();
        if (weights.Count == 0)
        {
            return [];
        }

        if (sum <= 0)
        {
            return weights.Select(_ => 0m).ToList();
        }

        var raw = weights.Select(w => total * w / sum).ToList();
        var floored = raw.Select(r => Math.Round(r, 2, MidpointRounding.ToZero)).ToList();
        var remainderCents = (int)Math.Round((total - floored.Sum()) * 100, MidpointRounding.AwayFromZero);
        var sign = remainderCents < 0 ? -1 : 1;
        var order = raw.Select((r, i) => (Fraction: sign * (r - floored[i]), Index: i)).OrderByDescending(x => x.Fraction).ThenBy(x => x.Index).ToList();
        for (var n = 0; n < Math.Abs(remainderCents); n++)
        {
            floored[order[n % order.Count].Index] += 0.01m * sign;
        }

        return floored;
    }
}
