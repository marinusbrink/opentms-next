using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Integrations.CarbonFootprint;
using OpenTms.Integrations.Routing;
using OpenTms.PlanningExecution.Settings;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.DerivedValues;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Settings;
using Volo.Abp.Timing;

namespace OpenTms.PlanningExecution.Legs;

/// <summary>
/// Design 59 "Calculators", <c>PlanningExecution.Leg</c>: routes one leg through the tenant's routing provider with the
/// trip's vehicle profile (or the default), applies the tenant's speed correction to the duration, stores toll
/// kilometres when the provider gives them, and estimates CO₂ for an unplanned leg (great-circle × CPI). A leg without
/// coordinates gets nothing (the trip warns <c>AddressNotGeocoded</c>). A provider that answers null leaves the old values;
/// a provider that throws fails the job, which Hangfire retries (flow 2 C).
/// </summary>
public class LegRouteCalculator : IDerivedValueCalculator, ITransientDependency
{
    public ILogger<LegRouteCalculator> Logger { get; set; } = NullLogger<LegRouteCalculator>.Instance;

    private readonly IRepository<Leg, Guid> _legs;
    private readonly IRepository<TransportTrip, Guid> _trips;
    private readonly IRoutingService _routing;
    private readonly ICarbonFootprintCalculator _carbon;
    private readonly ISettingProvider _settings;
    private readonly MasterDataReferences _masterData;
    private readonly IDerivedValueScheduler _scheduler;
    private readonly IClock _clock;

    public LegRouteCalculator(
        IRepository<Leg, Guid> legs,
        IRepository<TransportTrip, Guid> trips,
        IRoutingService routing,
        ICarbonFootprintCalculator carbon,
        ISettingProvider settings,
        MasterDataReferences masterData,
        IDerivedValueScheduler scheduler,
        IClock clock)
    {
        _legs = legs;
        _trips = trips;
        _routing = routing;
        _carbon = carbon;
        _settings = settings;
        _masterData = masterData;
        _scheduler = scheduler;
        _clock = clock;
    }

    public async Task CalculateAsync(DerivedValueCalculationContext context)
    {
        var leg = await _legs.FindAsync(context.TargetId);
        if (leg == null)
        {
            Logger.LogDebug("Leg {LegId} no longer exists; nothing to route.", context.TargetId);
            return;
        }

        if (!leg.HasCoordinates)
        {
            leg.ClearRoute();
            leg.SetCo2(null, null, null);
            await _legs.UpdateAsync(leg);
            return;
        }

        var from = new GeoPoint(leg.OriginLatitude!.Value, leg.OriginLongitude!.Value);
        var to = new GeoPoint(leg.DestinationLatitude!.Value, leg.DestinationLongitude!.Value);
        var spec = await VehicleSpecAsync(leg.TripId);

        var route = await _routing.GetRouteAsync(from, to, spec, new RoutingOptions(IncludeToll: true), context.CancellationToken);
        if (route == null)
        {
            Logger.LogInformation("Leg {LegId}: the routing provider gave no route; stored values kept.", leg.Id);
        }
        else
        {
            var factor = await SpeedCorrectionFactorAsync();
            leg.SetRoute(route.DistanceM, (int)Math.Round(route.DurationS * factor, MidpointRounding.AwayFromZero), route.TollDistanceM, route.Provider, route.At);
        }

        if (leg.TripId == null)
        {
            var estimate = _carbon.EstimateLegFallback(from, to, leg.WeightKg);
            leg.SetCo2(estimate.TotalKg, estimate.Method, estimate.FactorSet);
        }

        await _legs.UpdateAsync(leg);

        if (leg.TripId != null && route != null)
        {
            /* The trip's times, load and allocation depend on the leg's distance (design 59: LegsGenerated for assigned legs). */
            await _scheduler.RequestAsync(PlanningExecutionApplicationModule.TripTargetType, leg.TripId.Value, "LegRouted");
        }
    }

    private async Task<VehicleSpec> VehicleSpecAsync(Guid? tripId)
    {
        if (tripId == null)
        {
            return VehicleSpec.Default;
        }

        var trip = await (await _trips.WithDetailsAsync(t => t.Vehicles)).Where(t => t.Id == tripId).FirstOrDefaultAsync(_trips.AsyncExecuter);
        if (trip == null)
        {
            return VehicleSpec.Default;
        }

        var vehicleIds = await _masterData.TripVehicleIdsAsync(trip.VehicleCombinationId, trip.Vehicles.OrderBy(v => v.Position).Select(v => v.VehicleId));
        var vehicles = await _masterData.VehiclesAsync(vehicleIds);
        return MasterDataReferences.SpecOf(vehicles.Values.ToList());
    }

    private async Task<double> SpeedCorrectionFactorAsync()
    {
        var text = await _settings.GetOrNullAsync(PlanningExecutionSettings.SpeedCorrectionPercent);
        var percent = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value is >= 50 and <= 300
            ? value
            : PlanningExecutionConsts.DefaultSpeedCorrectionPercent;
        return percent / 100.0;
    }
}

internal static class QueryableExtensions
{
    public static Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> query, Volo.Abp.Linq.IAsyncQueryableExecuter executer) where T : class
        => executer.FirstOrDefaultAsync(query);
}
