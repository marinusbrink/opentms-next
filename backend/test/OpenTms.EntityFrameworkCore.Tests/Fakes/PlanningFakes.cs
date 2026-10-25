using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.Integrations.CarbonFootprint;
using OpenTms.Integrations.Routing;
using OpenTms.Orders.Published;
using OpenTms.Orders.TransportOrders;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Fakes;

/*
 * The published contracts of Orders, Financial and Integrations that Planning consumes (design 59) have no implementation
 * in this worktree: the modules are built in parallel. These fakes stand in for them in the SQLite and PostgreSQL tests and
 * are DELETED AT INTEGRATION, when the real implementations arrive. Every fake is tenant-aware: a tenant sees only the
 * facts registered for it (the negative tenant-isolation tests rely on that).
 */

/// <summary>The facts the fakes answer with, per tenant; the tests fill it.</summary>
public class PlanningFakeWorld : ISingletonDependency
{
    public ConcurrentDictionary<(Guid? TenantId, Guid OrderId), PricingSnapshotDto> Snapshots { get; } = new();

    public ConcurrentDictionary<(Guid? TenantId, Guid OrderId), TransportOrderReferenceDto> OrderReferences { get; } = new();

    public ConcurrentDictionary<(Guid? TenantId, Guid OrderId), TripRequestDto> TripRequests { get; } = new();

    public ConcurrentDictionary<(Guid? TenantId, Guid TripId), RateLineTotalsDto> TripTotals { get; } = new();

    public ConcurrentDictionary<(Guid? TenantId, Guid OrderId), RateLineTotalsDto> OrderTotals { get; } = new();

    /// <summary>Metres per kilometre of great-circle distance the fake router reports (1.3: roads are longer than the crow flies).</summary>
    public double RoadFactor { get; set; } = 1.3;

    /// <summary>Metres per second the fake router drives.</summary>
    public double SpeedMps { get; set; } = 20;

    /// <summary>False: every routing call answers null (provider not configured or down).</summary>
    public volatile bool RoutingAvailable = true;

    /// <summary>True: the next routing calls throw (provider failure the job must retry).</summary>
    public volatile bool RoutingThrows;

    public ConcurrentQueue<(Guid? TenantId, int Points)> MatrixCalls { get; } = new();

    public ConcurrentQueue<(Guid? TenantId, GeoPoint From, GeoPoint To, VehicleSpec? Vehicle)> RouteCalls { get; } = new();

    /// <summary>Grams CO₂ per tonne-kilometre the fake CO₂ calculator uses.</summary>
    public decimal CpiGramPerTonneKm { get; set; } = 62m;

    public void Register(Guid? tenantId, PricingSnapshotDto snapshot, string? number = null, string customerName = "Customer")
    {
        var orderId = snapshot.OrderId ?? throw new ArgumentException("OrderId required", nameof(snapshot));
        Snapshots[(tenantId, orderId)] = snapshot;
        OrderReferences[(tenantId, orderId)] = new TransportOrderReferenceDto
        {
            Id = orderId,
            Number = number ?? "TO-" + orderId.ToString("N")[..6].ToUpperInvariant(),
            Status = (TransportOrderStatus)snapshot.Status,
            CustomerRelationId = snapshot.CustomerRelationId,
            CustomerName = customerName
        };
    }

    public void Clear(Guid? tenantId)
    {
        foreach (var key in Snapshots.Keys.Where(k => k.TenantId == tenantId).ToList()) Snapshots.TryRemove(key, out _);
        foreach (var key in OrderReferences.Keys.Where(k => k.TenantId == tenantId).ToList()) OrderReferences.TryRemove(key, out _);
        foreach (var key in TripRequests.Keys.Where(k => k.TenantId == tenantId).ToList()) TripRequests.TryRemove(key, out _);
        foreach (var key in TripTotals.Keys.Where(k => k.TenantId == tenantId).ToList()) TripTotals.TryRemove(key, out _);
        foreach (var key in OrderTotals.Keys.Where(k => k.TenantId == tenantId).ToList()) OrderTotals.TryRemove(key, out _);
    }

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
        return new RouteResult(distance, duration, null, toll, null, null, null, "Fake", DateTimeOffset.UtcNow, false);
    }
}

public class PlanningFakeOrderPricingReader : IOrderPricingReader, ITransientDependency
{
    private readonly PlanningFakeWorld _world;
    private readonly ICurrentTenant _currentTenant;

    public PlanningFakeOrderPricingReader(PlanningFakeWorld world, ICurrentTenant currentTenant)
    {
        _world = world;
        _currentTenant = currentTenant;
    }

    public Task<PricingSnapshotDto?> GetSnapshotAsync(Guid orderId, CancellationToken cancellationToken = default)
        => Task.FromResult(_world.Snapshots.TryGetValue((_currentTenant.Id, orderId), out var snapshot) ? snapshot : null);

    public Task<IReadOnlyList<PricingSnapshotDto>> GetSnapshotsAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PricingSnapshotDto>>(orderIds.Select(id => _world.Snapshots.GetValueOrDefault((_currentTenant.Id, id))).Where(s => s != null).Select(s => s!).ToList());
}

public class FakeTransportOrderLookup : ITransportOrderLookup, ITransientDependency
{
    private readonly PlanningFakeWorld _world;
    private readonly ICurrentTenant _currentTenant;

    public FakeTransportOrderLookup(PlanningFakeWorld world, ICurrentTenant currentTenant)
    {
        _world = world;
        _currentTenant = currentTenant;
    }

    public Task<IReadOnlyList<TransportOrderReferenceDto>> GetAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<TransportOrderReferenceDto>>(orderIds.Select(id => _world.OrderReferences.GetValueOrDefault((_currentTenant.Id, id))).Where(o => o != null).Select(o => o!).ToList());

    public Task<TripRequestDto?> GetTripRequestAsync(Guid orderId, CancellationToken cancellationToken = default)
        => Task.FromResult(_world.TripRequests.TryGetValue((_currentTenant.Id, orderId), out var request) ? request : null);
}

public class FakeRateLineReader : IRateLineReader, ITransientDependency
{
    private readonly PlanningFakeWorld _world;
    private readonly ICurrentTenant _currentTenant;

    public FakeRateLineReader(PlanningFakeWorld world, ICurrentTenant currentTenant)
    {
        _world = world;
        _currentTenant = currentTenant;
    }

    public Task<RateLineTotalsDto> GetOrderTotalsAsync(Guid orderId, CancellationToken cancellationToken = default)
        => Task.FromResult(_world.OrderTotals.GetValueOrDefault((_currentTenant.Id, orderId)) ?? new RateLineTotalsDto { AnchorId = orderId });

    public Task<IReadOnlyDictionary<Guid, RateLineTotalsDto>> GetOrderTotalsAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, RateLineTotalsDto>>(orderIds.Distinct().ToDictionary(id => id, id => _world.OrderTotals.GetValueOrDefault((_currentTenant.Id, id)) ?? new RateLineTotalsDto { AnchorId = id }));

    public Task<RateLineTotalsDto> GetTripTotalsAsync(Guid tripId, CancellationToken cancellationToken = default)
        => Task.FromResult(_world.TripTotals.GetValueOrDefault((_currentTenant.Id, tripId)) ?? new RateLineTotalsDto { AnchorId = tripId });

    public Task<IReadOnlyList<RateLineSnapshotDto>> GetLegLinesAsync(IReadOnlyCollection<Guid> legIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RateLineSnapshotDto>>([]);
}

public class FakeRoutingService : IRoutingService, ITransientDependency
{
    private readonly PlanningFakeWorld _world;
    private readonly ICurrentTenant _currentTenant;

    public FakeRoutingService(PlanningFakeWorld world, ICurrentTenant currentTenant)
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

        _world.RouteCalls.Enqueue((_currentTenant.Id, from, to, vehicle));
        return Task.FromResult(_world.RoutingAvailable ? _world.Route(from, to, vehicle) : null);
    }

    public Task<RouteMatrix?> GetMatrixAsync(IReadOnlyList<GeoPoint> points, VehicleSpec? vehicle, CancellationToken cancellationToken = default)
    {
        if (_world.RoutingThrows)
        {
            throw new InvalidOperationException("Simulated routing provider failure");
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

        return Task.FromResult<RouteMatrix?>(new RouteMatrix(legs, "Fake", DateTimeOffset.UtcNow));
    }

    public Task<RoutingStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new RoutingStatus("Fake", _world.RoutingAvailable, _world.RoutingAvailable, null, 0, null));
}

public class FakeCarbonFootprintCalculator : ICarbonFootprintCalculator, ITransientDependency
{
    private readonly PlanningFakeWorld _world;

    public FakeCarbonFootprintCalculator(PlanningFakeWorld world)
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
                km += PlanningFakeWorld.GreatCircleM(stops[i], stops[i + 1]) / 1000;
            }

            var tonnes = (l.WeightKg ?? 1000) / 1000;
            return Math.Round((decimal)(km * tonnes) * _world.CpiGramPerTonneKm / 1000, 3);
        }).ToList();
        return Task.FromResult(new Co2Estimate(perLeg.Sum(), perLeg, Co2Estimate.MethodGreatCircleCpi, "Fake"));
    }

    public Co2Estimate EstimateLegFallback(GeoPoint from, GeoPoint to, double? weightKg)
    {
        var km = PlanningFakeWorld.GreatCircleM(from, to) / 1000;
        var kg = Math.Round((decimal)(km * (weightKg ?? 1000) / 1000) * _world.CpiGramPerTonneKm / 1000, 3);
        return new Co2Estimate(kg, [kg], Co2Estimate.MethodGreatCircleCpi, "Fake");
    }
}
