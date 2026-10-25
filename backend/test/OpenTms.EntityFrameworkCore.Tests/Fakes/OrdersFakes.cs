using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OpenTms.Financial.Published;
using OpenTms.Financial.Tariffs;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.Orders.Events;
using OpenTms.PlanningExecution.Published;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.NumberSequences;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace OpenTms.Fakes;

/*
 * The test doubles of the Orders run (design 58): Financial's and Planning's published contracts have no implementation
 * in this worktree yet, so these stand in. Every fake is a plain in-memory store the test fills. DELETED AT INTEGRATION:
 * once Financial and Planning ship their implementations, the Fake* classes marked ITransientDependency go, and the
 * tests that depend on their stores move to the real services. The SQLite-only replacements at the bottom are
 * registered by OpenTmsEntityFrameworkCoreTestModule (SQLite runs without transactions, the production code requires one).
 */

/// <summary>Financial's reference rows the tests declare; unknown ids are absent, as the real lookup.</summary>
public class FakeFinancialReferences : ISingletonDependency
{
    public ConcurrentDictionary<Guid, FinancialReferenceDto> VatPercentages { get; } = new();
    public ConcurrentDictionary<Guid, FinancialReferenceDto> DiscountsOrSurcharges { get; } = new();
    public ConcurrentDictionary<Guid, FinancialReferenceDto> AdditionalServices { get; } = new();
    public ConcurrentDictionary<Guid, PriceListReferenceDto> PriceLists { get; } = new();

    public Guid AddVat(string name = "Hoog") => Add(VatPercentages, new FinancialReferenceDto { Id = Guid.NewGuid(), Code = name.ToUpperInvariant(), Name = name });

    public Guid AddDiscountOrSurcharge(string name = "Toeslag") => Add(DiscountsOrSurcharges, new FinancialReferenceDto { Id = Guid.NewGuid(), Code = name.ToUpperInvariant(), Name = name });

    public Guid AddPriceList(string name = "Tarieven 2026") => Add(PriceLists, new PriceListReferenceDto { Id = Guid.NewGuid(), Code = name.ToUpperInvariant(), Name = name, Side = RateSide.Revenue, CurrencyCode = "EUR" });

    private static Guid Add<T>(ConcurrentDictionary<Guid, T> store, T row) where T : FinancialReferenceDto
    {
        store[row.Id] = row;
        return row.Id;
    }
}

public class FakeFinancialReferenceLookup : IFinancialReferenceLookup, ITransientDependency
{
    private readonly FakeFinancialReferences _store;

    public FakeFinancialReferenceLookup(FakeFinancialReferences store)
    {
        _store = store;
    }

    public Task<IReadOnlyList<FinancialReferenceDto>> GetVatPercentagesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<FinancialReferenceDto>>(ids.Where(_store.VatPercentages.ContainsKey).Select(id => _store.VatPercentages[id]).ToList());

    public Task<IReadOnlyList<FinancialReferenceDto>> GetDiscountsOrSurchargesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<FinancialReferenceDto>>(ids.Where(_store.DiscountsOrSurcharges.ContainsKey).Select(id => _store.DiscountsOrSurcharges[id]).ToList());

    public Task<IReadOnlyList<FinancialReferenceDto>> GetAdditionalServicesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<FinancialReferenceDto>>(ids.Where(_store.AdditionalServices.ContainsKey).Select(id => _store.AdditionalServices[id]).ToList());

    public Task<IReadOnlyList<PriceListReferenceDto>> GetPriceListsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PriceListReferenceDto>>(ids.Where(_store.PriceLists.ContainsKey).Select(id => _store.PriceLists[id]).ToList());
}

/// <summary>A preview of 10 per unit of quantity per line, so a test can see its snapshot was priced.</summary>
public class FakePricePreviewService : IPricePreviewService, ISingletonDependency
{
    public ConcurrentQueue<PricingSnapshotDto> Snapshots { get; } = new();

    public Task<PricePreviewDto> PreviewAsync(PricingSnapshotDto snapshot, CancellationToken cancellationToken = default)
    {
        Snapshots.Enqueue(snapshot);
        var lines = snapshot.Lines.Select(l => new PricePreviewLineDto
        {
            OrderLineId = l.LineId, Kind = RateLineKind.PriceList, Quantity = l.Quantity, QuantitySource = QuantitySource.LineQuantity, UnitPrice = 10m, Amount = l.Quantity * 10m
        }).ToList();
        var total = snapshot.FixedPriceAmount ?? lines.Sum(l => l.Amount);
        return Task.FromResult(new PricePreviewDto { CurrencyCode = snapshot.CurrencyCode, Lines = lines, LinesAmount = lines.Sum(l => l.Amount), TotalAmount = total });
    }
}

/// <summary>The stored rate-line totals per order, as a test wants them.</summary>
public class FakeRateLines : ISingletonDependency
{
    public ConcurrentDictionary<Guid, RateLineTotalsDto> OrderTotals { get; } = new();

    public void SetRevenue(Guid orderId, decimal total, long version = 1, Dictionary<Guid, decimal>? byLine = null)
        => OrderTotals[orderId] = new RateLineTotalsDto { AnchorId = orderId, TotalAmount = total, LinesAmount = total, CalculationVersion = version, ByChild = byLine ?? new Dictionary<Guid, decimal>() };
}

public class FakeRateLineReader : IRateLineReader, ITransientDependency
{
    private readonly FakeRateLines _store;

    public FakeRateLineReader(FakeRateLines store)
    {
        _store = store;
    }

    public Task<RateLineTotalsDto> GetOrderTotalsAsync(Guid orderId, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.OrderTotals.TryGetValue(orderId, out var totals) ? totals : new RateLineTotalsDto { AnchorId = orderId });

    public Task<IReadOnlyDictionary<Guid, RateLineTotalsDto>> GetOrderTotalsAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, RateLineTotalsDto>>(orderIds.Distinct().ToDictionary(id => id, id => _store.OrderTotals.TryGetValue(id, out var t) ? t : new RateLineTotalsDto { AnchorId = id }));

    public Task<RateLineTotalsDto> GetTripTotalsAsync(Guid tripId, CancellationToken cancellationToken = default)
        => Task.FromResult(new RateLineTotalsDto { AnchorId = tripId });

    public Task<IReadOnlyList<RateLineSnapshotDto>> GetLegLinesAsync(IReadOnlyCollection<Guid> legIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RateLineSnapshotDto>>([]);
}

/// <summary>Planning's legs per order, as a test wants them.</summary>
public class FakeLegs : ISingletonDependency
{
    public ConcurrentDictionary<Guid, List<LegSnapshotDto>> ByOrder { get; } = new();

    public LegSnapshotDto Add(Guid orderId, Guid lineId, Guid? tripId = null, string? tripNumber = null, PlanningExecution.TransportTrips.TransportTripStatus? tripStatus = null, int? distanceM = null, decimal? co2Kg = null, decimal? cost = null, DateTimeOffset? arrivedAt = null)
    {
        var leg = new LegSnapshotDto
        {
            Id = Guid.NewGuid(), OrderId = orderId, OrderLineId = lineId, Position = 1, TripId = tripId, TripNumber = tripNumber, TripStatus = tripStatus,
            DistanceM = distanceM, EstimatedCo2Kg = co2Kg, AllocatedCostAmount = cost,
            Origin = new LegEndDto { PlannedDate = new DateOnly(2026, 10, 20) },
            Destination = new LegEndDto { PlannedDate = new DateOnly(2026, 10, 20), ActualAt = arrivedAt }
        };
        ByOrder.AddOrUpdate(orderId, _ => [leg], (_, list) => { list.Add(leg); return list; });
        return leg;
    }
}

public class FakeLegReader : ILegReader, ITransientDependency
{
    private readonly FakeLegs _store;

    public FakeLegReader(FakeLegs store)
    {
        _store = store;
    }

    public Task<IReadOnlyList<LegSnapshotDto>> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<LegSnapshotDto>>(_store.ByOrder.TryGetValue(orderId, out var legs) ? legs.ToList() : []);

    public Task<IReadOnlyList<LegSnapshotDto>> GetByOrdersAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<LegSnapshotDto>>(orderIds.SelectMany(id => _store.ByOrder.TryGetValue(id, out var legs) ? legs : []).ToList());

    public Task<IReadOnlyList<LegSnapshotDto>> GetByTripAsync(Guid tripId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<LegSnapshotDto>>(_store.ByOrder.Values.SelectMany(l => l).Where(l => l.TripId == tripId).ToList());

    public Task<IReadOnlyList<LegSnapshotDto>> GetAsync(IReadOnlyCollection<Guid> legIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<LegSnapshotDto>>(_store.ByOrder.Values.SelectMany(l => l).Where(l => legIds.Contains(l.Id)).ToList());
}

public class FakeTrips : ISingletonDependency
{
    public ConcurrentDictionary<Guid, TransportTripReferenceDto> Trips { get; } = new();

    public TransportTripReferenceDto Add(string number, Guid? carrierRelationId = null, string? carrierName = null, Guid? departureRelationId = null, Guid? arrivalRelationId = null)
    {
        var trip = new TransportTripReferenceDto
        {
            Id = Guid.NewGuid(), Number = number, TripDate = new DateOnly(2026, 10, 20), CarrierRelationId = carrierRelationId, CarrierName = carrierName,
            DepartureRelationId = departureRelationId, ArrivalRelationId = arrivalRelationId, VehicleNames = ["Truck 12"], LicencePlates = ["12-BXL-3"], DriverNames = ["Jan de Vries"],
            PlannedDeparture = new DateTimeOffset(2026, 10, 20, 6, 0, 0, TimeSpan.Zero)
        };
        Trips[trip.Id] = trip;
        return trip;
    }
}

public class FakeTransportTripLookup : ITransportTripLookup, ITransientDependency
{
    private readonly FakeTrips _store;

    public FakeTransportTripLookup(FakeTrips store)
    {
        _store = store;
    }

    public Task<IReadOnlyList<TransportTripReferenceDto>> GetAsync(IReadOnlyCollection<Guid> tripIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<TransportTripReferenceDto>>(tripIds.Where(_store.Trips.ContainsKey).Select(id => _store.Trips[id]).ToList());
}

/// <summary>Records every Orders fact that was published, with the tenant it was raised in.</summary>
public class OrdersEventRecorder : ISingletonDependency
{
    private readonly ConcurrentQueue<(Guid? TenantId, object Event)> _events = new();

    public IReadOnlyList<(Guid? TenantId, object Event)> All => _events.ToList();

    public IReadOnlyList<T> Of<T>(Guid orderId) => _events.Select(e => e.Event).OfType<T>().Where(e => OrderIdOf(e) == orderId).ToList();

    public void Record(Guid? tenantId, object eventData) => _events.Enqueue((tenantId, eventData));

    private static Guid OrderIdOf(object e) => e switch
    {
        TransportOrderConfirmedEto x => x.OrderId,
        TransportOrderLinesChangedEto x => x.OrderId,
        TransportOrderStopsChangedEto x => x.OrderId,
        TransportOrderFinancialTermsChangedEto x => x.OrderId,
        TransportOrderCancelledEto x => x.OrderId,
        TransportOrderStatusChangedEto x => x.OrderId,
        _ => Guid.Empty
    };
}

public class OrdersEventRecordingHandler :
    IDistributedEventHandler<TransportOrderConfirmedEto>, IDistributedEventHandler<TransportOrderLinesChangedEto>, IDistributedEventHandler<TransportOrderStopsChangedEto>,
    IDistributedEventHandler<TransportOrderFinancialTermsChangedEto>, IDistributedEventHandler<TransportOrderCancelledEto>, IDistributedEventHandler<TransportOrderStatusChangedEto>,
    ITransientDependency
{
    private readonly OrdersEventRecorder _recorder;
    private readonly ICurrentTenant _currentTenant;

    public OrdersEventRecordingHandler(OrdersEventRecorder recorder, ICurrentTenant currentTenant)
    {
        _recorder = recorder;
        _currentTenant = currentTenant;
    }

    public Task HandleEventAsync(TransportOrderConfirmedEto e) => Record(e);
    public Task HandleEventAsync(TransportOrderLinesChangedEto e) => Record(e);
    public Task HandleEventAsync(TransportOrderStopsChangedEto e) => Record(e);
    public Task HandleEventAsync(TransportOrderFinancialTermsChangedEto e) => Record(e);
    public Task HandleEventAsync(TransportOrderCancelledEto e) => Record(e);
    public Task HandleEventAsync(TransportOrderStatusChangedEto e) => Record(e);

    private Task Record(object e)
    {
        _recorder.Record(_currentTenant.Id, e);
        return Task.CompletedTask;
    }
}

// ── SQLite-only replacements (registered explicitly by OpenTmsEntityFrameworkCoreTestModule, not auto-registered) ──

/// <summary>SQLite runs without transactions; the gapless rule itself is covered on PostgreSQL. Exposed as itself only: the base class's convention would otherwise register it as the INumberSequenceGenerator of every test process.</summary>
[ExposeServices(typeof(LenientNumberSequenceGenerator))]
public class LenientNumberSequenceGenerator : NumberSequenceGenerator
{
    private readonly INumberSequenceCounterStore _store;

    public LenientNumberSequenceGenerator(INumberSequenceCounterStore store, IUnitOfWorkManager unitOfWorkManager, IClock clock, IOptions<NumberSequenceOptions> options)
        : base(store, unitOfWorkManager, clock, options)
    {
        _store = store;
    }

    public override async Task<string> NextAsync(string sequenceKey, NumberSequenceFormat format)
    {
        var today = LocalToday();
        return format.Format(await _store.IncrementAsync(sequenceKey, format.Period(today)), today);
    }
}

/// <summary>Records the derived-value requests instead of writing the state (SQLite has no transaction to bind them to).</summary>
public class RecordingDerivedValueScheduler : IDerivedValueScheduler
{
    private readonly ConcurrentQueue<(string TargetType, Guid TargetId, string? Reason)> _requests = new();
    private long _version;

    public IReadOnlyList<(string TargetType, Guid TargetId, string? Reason)> Requests => _requests.ToList();

    public IReadOnlyList<string> ReasonsFor(Guid targetId) => _requests.Where(r => r.TargetId == targetId).Select(r => r.Reason ?? string.Empty).ToList();

    public Task<long> RequestAsync(string targetType, Guid targetId, string? reason = null)
    {
        _requests.Enqueue((targetType, targetId, reason));
        return Task.FromResult(Interlocked.Increment(ref _version));
    }

    public Task<int> RequestManyAsync(string targetType, IReadOnlyCollection<Guid> targetIds, string? reason = null)
    {
        foreach (var id in targetIds)
        {
            _requests.Enqueue((targetType, id, reason));
        }

        return Task.FromResult(targetIds.Count);
    }
}
