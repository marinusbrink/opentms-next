using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.Orders.Published;
using OpenTms.PlanningExecution.Published;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Fakes;

/*
 * The published readers of Orders and Planning that Financial's calculators consume (design 57) are not implemented in
 * this worktree. These fakes stand in until the real implementations arrive at integration, when this file is deleted.
 * Tests arrange the facts in the singleton store; the fakes answer from it regardless of tenant (the tests choose ids).
 */

public class FinancialFakeStore : ISingletonDependency
{
    public ConcurrentDictionary<Guid, PricingSnapshotDto> Orders { get; } = new();

    public ConcurrentDictionary<Guid, LegSnapshotDto> Legs { get; } = new();

    public ConcurrentDictionary<Guid, TransportTripReferenceDto> Trips { get; } = new();

    public void Clear()
    {
        Orders.Clear();
        Legs.Clear();
        Trips.Clear();
    }
}

public class FakeOrderPricingReader : IOrderPricingReader, ITransientDependency
{
    private readonly FinancialFakeStore _store;

    public FakeOrderPricingReader(FinancialFakeStore store) => _store = store;

    public Task<PricingSnapshotDto?> GetSnapshotAsync(Guid orderId, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.Orders.TryGetValue(orderId, out var snapshot) ? snapshot : null);

    public Task<IReadOnlyList<PricingSnapshotDto>> GetSnapshotsAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PricingSnapshotDto>>(orderIds.Where(_store.Orders.ContainsKey).Select(id => _store.Orders[id]).ToList());
}

public class FakeLegReader : ILegReader, ITransientDependency
{
    private readonly FinancialFakeStore _store;

    public FakeLegReader(FinancialFakeStore store) => _store = store;

    public Task<IReadOnlyList<LegSnapshotDto>> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<LegSnapshotDto>>(_store.Legs.Values.Where(l => l.OrderId == orderId).OrderBy(l => l.Position).ToList());

    public Task<IReadOnlyList<LegSnapshotDto>> GetByOrdersAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<LegSnapshotDto>>(_store.Legs.Values.Where(l => orderIds.Contains(l.OrderId)).OrderBy(l => l.Position).ToList());

    public Task<IReadOnlyList<LegSnapshotDto>> GetByTripAsync(Guid tripId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<LegSnapshotDto>>(_store.Legs.Values.Where(l => l.TripId == tripId).OrderBy(l => l.Position).ToList());

    public Task<IReadOnlyList<LegSnapshotDto>> GetAsync(IReadOnlyCollection<Guid> legIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<LegSnapshotDto>>(legIds.Where(_store.Legs.ContainsKey).Select(id => _store.Legs[id]).ToList());
}

public class FakeTransportTripLookup : ITransportTripLookup, ITransientDependency
{
    private readonly FinancialFakeStore _store;

    public FakeTransportTripLookup(FinancialFakeStore store) => _store = store;

    public Task<IReadOnlyList<TransportTripReferenceDto>> GetAsync(IReadOnlyCollection<Guid> tripIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<TransportTripReferenceDto>>(tripIds.Where(_store.Trips.ContainsKey).Select(id => _store.Trips[id]).ToList());
}
