using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTms.Integrations.CarbonFootprint;
using OpenTms.Integrations.Routing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.TestDoubles;

/// <summary>
/// A controllable stand-in for Integrations' routing engine and CO₂ calculator, for the Planning tests and the chain
/// test: great-circle distance times a road factor, a fixed speed, toll as half the distance when a vehicle is given, CO₂
/// per tonne-kilometre. NOT auto-registered: a test module that needs it calls <see cref="Register"/>, so the real
/// adapters of design 60 stay in place for every other module's tests.
/// </summary>
public class TestRoutingWorld
{
    /// <summary>Metres per kilometre of great-circle distance the router reports (1.3: roads are longer than the crow flies).</summary>
    public double RoadFactor { get; set; } = 1.3;

    /// <summary>Metres per second the router drives.</summary>
    public double SpeedMps { get; set; } = 20;

    /// <summary>False: every routing call answers null (provider not configured or down).</summary>
    public volatile bool RoutingAvailable = true;

    /// <summary>True: the next routing calls throw (provider failure the job must retry).</summary>
    public volatile bool RoutingThrows;

    /// <summary>True: the next routing calls throw <see cref="RoutingUnavailableException"/>, as the real providers do for an outage.</summary>
    public volatile bool RoutingDown;

    public ConcurrentQueue<(Guid? TenantId, int Points)> MatrixCalls { get; } = new();

    public ConcurrentQueue<(Guid? TenantId, GeoPoint From, GeoPoint To, VehicleSpec? Vehicle)> RouteCalls { get; } = new();

    /// <summary>Grams CO₂ per tonne-kilometre the CO₂ calculator uses.</summary>
    public decimal CpiGramPerTonneKm { get; set; } = 62m;

    public static double GreatCircleM(GeoPoint a, GeoPoint b)
    {
        const double r = 6_371_000;
        var dLat = Rad(b.Latitude - a.Latitude);
        var dLon = Rad(b.Longitude - a.Longitude);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(a.Latitude)) * Math.Cos(Rad(b.Latitude)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    private static double Rad(double degrees) => degrees * Math.PI / 180;

    public RouteResult Route(GeoPoint from, GeoPoint to, VehicleSpec? vehicle)
    {
        var distance = (int)Math.Round(GreatCircleM(from, to) * RoadFactor);
        var duration = (int)Math.Round(distance / SpeedMps);
        var toll = vehicle == null ? null : (int?)(distance / 2);
        return new RouteResult(distance, duration, null, toll, null, null, null, "Test", DateTimeOffset.UtcNow, false);
    }

    /// <summary>Replaces <see cref="IRoutingService"/> and <see cref="ICarbonFootprintCalculator"/> in the given container with the doubles.</summary>
    public static TestRoutingWorld Register(IServiceCollection services)
    {
        var world = new TestRoutingWorld();
        services.AddSingleton(world);
        services.Replace(ServiceDescriptor.Transient<IRoutingService, TestRoutingService>());
        services.Replace(ServiceDescriptor.Transient<ICarbonFootprintCalculator, TestCarbonFootprintCalculator>());
        return world;
    }
}

public class TestRoutingService : IRoutingService
{
    private readonly TestRoutingWorld _world;
    private readonly ICurrentTenant _currentTenant;

    public TestRoutingService(TestRoutingWorld world, ICurrentTenant currentTenant)
    {
        _world = world;
        _currentTenant = currentTenant;
    }

    public Task<RouteResult?> GetRouteAsync(GeoPoint from, GeoPoint to, VehicleSpec? vehicle, RoutingOptions options, CancellationToken cancellationToken = default)
    {
        if (_world.RoutingThrows)
        {
            throw new InvalidOperationException("Simulated routing provider failure");
        }

        if (_world.RoutingDown)
        {
            throw new RoutingUnavailableException("Test", "route", RoutingFailure.HttpStatus, 503);
        }

        _world.RouteCalls.Enqueue((_currentTenant.Id, from, to, vehicle));
        return Task.FromResult(_world.RoutingAvailable ? _world.Route(from, to, vehicle) : null);
    }

    public Task<RouteMatrix?> GetMatrixAsync(IReadOnlyList<GeoPoint> points, VehicleSpec? vehicle, CancellationToken cancellationToken = default)
    {
        if (_world.RoutingThrows)
        {
            throw new InvalidOperationException("Simulated routing provider failure");
        }

        if (_world.RoutingDown)
        {
            throw new RoutingUnavailableException("Test", "table", RoutingFailure.Timeout);
        }

        _world.MatrixCalls.Enqueue((_currentTenant.Id, points.Count));
        if (!_world.RoutingAvailable || points.Count < 2)
        {
            return Task.FromResult<RouteMatrix?>(null);
        }

        var legs = new List<RouteResult>();
        for (var i = 0; i + 1 < points.Count; i++)
        {
            legs.Add(_world.Route(points[i], points[i + 1], vehicle));
        }

        return Task.FromResult<RouteMatrix?>(new RouteMatrix(legs, "Test", DateTimeOffset.UtcNow));
    }

    public Task<RoutingStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new RoutingStatus("Test", _world.RoutingAvailable, _world.RoutingAvailable, null, 0, null));
}

public class TestCarbonFootprintCalculator : ICarbonFootprintCalculator
{
    private readonly TestRoutingWorld _world;

    public TestCarbonFootprintCalculator(TestRoutingWorld world)
    {
        _world = world;
    }

    public Task<Co2Estimate> EstimateTripAsync(IReadOnlyList<GeoPoint> stops, VehicleSpec? vehicle, IReadOnlyList<LegLoad> legs, CancellationToken cancellationToken = default)
    {
        var perLeg = legs.Select(l =>
        {
            var km = 0.0;
            for (var i = Math.Min(l.FromStopIndex, l.ToStopIndex); i < Math.Max(l.FromStopIndex, l.ToStopIndex) && i + 1 < stops.Count; i++)
            {
                km += TestRoutingWorld.GreatCircleM(stops[i], stops[i + 1]) / 1000;
            }

            var tonnes = (l.WeightKg ?? 1000) / 1000;
            return Math.Round((decimal)(km * tonnes) * _world.CpiGramPerTonneKm / 1000, 3);
        }).ToList();
        return Task.FromResult(new Co2Estimate(perLeg.Sum(), perLeg, Co2Estimate.MethodGreatCircleCpi, "Test"));
    }

    public Co2Estimate EstimateLegFallback(GeoPoint from, GeoPoint to, double? weightKg)
    {
        var km = TestRoutingWorld.GreatCircleM(from, to) / 1000;
        var kg = Math.Round((decimal)(km * (weightKg ?? 1000) / 1000) * _world.CpiGramPerTonneKm / 1000, 3);
        return new Co2Estimate(kg, [kg], Co2Estimate.MethodGreatCircleCpi, "Test");
    }
}
