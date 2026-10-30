using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Orders.Published;
using OpenTms.PlanningExecution.Events;
using OpenTms.PlanningExecution.Legs;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace OpenTms.PlanningExecution.TransportTrips;

/// <summary>
/// The trip commands as the application service and the Orders event handler share them (design 59 "Lifecycle and
/// commands"): create with a number from the type's sequence, assign and unassign legs through the domain service,
/// persist, publish the facts (outbox) and request the calculations (flow 2). No authorization here: the application
/// service checks permissions; the handler runs on behalf of a committed Orders fact.
/// </summary>
public class TripOperations : ITransientDependency
{
    public ILogger<TripOperations> Logger { get; set; } = NullLogger<TripOperations>.Instance;

    private readonly IRepository<TransportTrip, Guid> _trips;
    private readonly IRepository<Leg, Guid> _legs;
    private readonly IRepository<TransportTripType, Guid> _types;
    private readonly INumberSequenceGenerator _numbers;
    private readonly IGuidGenerator _guids;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDistributedEventBus _events;
    private readonly IDerivedValueScheduler _scheduler;
    private readonly LegAssignmentService _assignment;
    private readonly MasterDataReferences _masterData;
    private readonly ITransportOrderLookup _orders;
    private readonly IClock _clock;
    private readonly TripTypeDefaults _typeDefaults;

    public TripOperations(
        IRepository<TransportTrip, Guid> trips,
        IRepository<Leg, Guid> legs,
        IRepository<TransportTripType, Guid> types,
        INumberSequenceGenerator numbers,
        IGuidGenerator guids,
        ICurrentTenant currentTenant,
        IDistributedEventBus events,
        IDerivedValueScheduler scheduler,
        LegAssignmentService assignment,
        MasterDataReferences masterData,
        ITransportOrderLookup orders,
        IClock clock,
        TripTypeDefaults typeDefaults)
    {
        _trips = trips;
        _legs = legs;
        _types = types;
        _numbers = numbers;
        _guids = guids;
        _currentTenant = currentTenant;
        _events = events;
        _scheduler = scheduler;
        _assignment = assignment;
        _masterData = masterData;
        _orders = orders;
        _clock = clock;
        _typeDefaults = typeDefaults;
    }

    public Guid NewId() => _guids.Create();

    public DateTimeOffset Now => new(_clock.Now, _clock.Kind == DateTimeKind.Utc ? TimeSpan.Zero : TimeZoneInfo.Local.GetUtcOffset(_clock.Now));

    public async Task<TransportTrip> GetWithDetailsAsync(Guid id)
    {
        var trip = await AsyncExecuterOf(_trips).FirstOrDefaultAsync((await _trips.WithDetailsAsync()).Where(t => t.Id == id));
        return trip ?? throw new EntityNotFoundException(typeof(TransportTrip), id);
    }

    public Task<List<Leg>> LegsOnTripAsync(Guid tripId) => _legs.GetListAsync(l => l.TripId == tripId);

    public async Task<List<Leg>> LegsAsync(IReadOnlyCollection<Guid> legIds)
    {
        if (legIds.Count > PlanningExecutionConsts.MaxLegsPerCommand)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.TooManyLegs).WithData("max", PlanningExecutionConsts.MaxLegsPerCommand);
        }

        var ids = legIds.Distinct().ToList();
        var legs = await _legs.GetListAsync(l => ids.Contains(l.Id));
        var missing = ids.FirstOrDefault(id => legs.All(l => l.Id != id));
        if (missing != Guid.Empty)
        {
            throw new EntityNotFoundException(typeof(Leg), missing);
        }

        return legs;
    }

    /// <summary>
    /// Creates a trip: every group the input leaves empty filled from the type (design 61 "Trip type defaults"; the
    /// application service and the order's trip request on Confirm both come through here), a number from the type's
    /// sequence, the Master Data ids validated.
    /// </summary>
    public async Task<TransportTrip> CreateAsync(TransportTripCreateDto request, Guid? createdFromOrderId, decimal? agreedPurchaseAmount, string? agreedPurchaseCurrencyCode)
    {
        var type = await _types.FindAsync(request.TransportTripTypeId)
                   ?? throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "transportTripTypeId").WithData("value", request.TransportTripTypeId);
        var input = await _typeDefaults.FillAsync(request, type);
        var execution = input.Execution!.Value;
        var stopCompositionMode = input.StopCompositionMode!.Value;
        var firstLastStopRule = input.FirstLastStopRule!.Value;
        await _masterData.EnsureRelationsExistAsync([input.CarrierRelationId, input.DepartureRelationId, input.ArrivalRelationId], "relations");

        await ValidateVehiclesAsync(input.VehicleCombinationId, input.VehicleIds);
        await ValidateDriversAsync(input.DriverIds);

        /* The number is drawn last: a refused request leaves no gap (the counter is taken inside the transaction). */
        var tripDate = input.TripDate ?? DateOnly.FromDateTime(input.PlannedDeparture.Date);
        var number = await _numbers.NextAsync(type.SequenceKey, type.Numbering);
        var trip = new TransportTrip(_guids.Create(), _currentTenant.Id, number, type.Id, tripDate, execution,
            stopCompositionMode, firstLastStopRule, input.PlannedDeparture);
        trip.SetHeader(input.Name, tripDate, input.PlannedDeparture, input.PlannedArrival, input.DepartureRelationId, input.DepartureAddressId,
            input.ArrivalRelationId, input.ArrivalAddressId, input.Remarks, input.DriverInstructions, input.OrganizationUnitId,
            execution, stopCompositionMode, firstLastStopRule, input.Attributes);
        trip.SetCarrier(input.CarrierRelationId, input.CarrierContactPersonId);
        trip.SetVehicles(input.VehicleCombinationId, input.VehicleIds);
        trip.SetDrivers(input.DriverIds);
        if (createdFromOrderId != null)
        {
            trip.SetCreatedFromOrder(createdFromOrderId.Value);
        }

        await RefreshSearchKeyAsync(trip, []);
        await _trips.InsertAsync(trip, autoSave: true);
        await _events.PublishAsync(new TransportTripCreatedEto
        {
            TripId = trip.Id,
            CreatedFromOrderId = createdFromOrderId,
            AgreedPurchaseAmount = agreedPurchaseAmount,
            AgreedPurchaseCurrencyCode = agreedPurchaseCurrencyCode
        });
        Logger.LogInformation("Transport trip {TripId} created for tenant {TenantId} (from order {OrderId}).", trip.Id, _currentTenant.Id, createdFromOrderId);
        return trip;
    }

    public async Task ValidateVehiclesAsync(Guid? combinationId, IReadOnlyList<Guid> vehicleIds)
    {
        if (combinationId != null)
        {
            var combinations = await _masterData.CombinationsAsync([combinationId]);
            if (!combinations.ContainsKey(combinationId.Value))
            {
                throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "vehicleCombinationId").WithData("value", combinationId.Value);
            }
        }

        var ids = vehicleIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var vehicles = await _masterData.VehiclesAsync(ids);
        var missing = ids.FirstOrDefault(id => !vehicles.ContainsKey(id));
        if (missing != Guid.Empty)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "vehicleIds").WithData("value", missing);
        }
    }

    public async Task ValidateDriversAsync(IReadOnlyList<Guid> driverIds)
    {
        var ids = driverIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var drivers = await _masterData.DriverNamesAsync(ids);
        var missing = ids.FirstOrDefault(id => !drivers.ContainsKey(id));
        if (missing != Guid.Empty)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "driverIds").WithData("value", missing);
        }
    }

    /// <summary>Puts legs on a trip (the orders must not be cancelled), persists, publishes and requests the trip's calculation.</summary>
    public async Task AssignAsync(TransportTrip trip, IReadOnlyList<Leg> legs, bool allowOutsideWindow, Guid? afterStopId, bool moving)
    {
        await EnsureOrdersPlannableAsync(legs);
        var onTrip = await LegsOnTripAsync(trip.Id);
        var sourceTrips = legs.Where(l => l.TripId != null && l.TripId != trip.Id).Select(l => l.TripId!.Value).Distinct().ToList();
        var movingIds = legs.Select(l => l.Id).ToHashSet();

        /* A moved leg leaves its old stops first: a leg's end is at one stop (the unique key of TripStopLegs). */
        var sources = new List<TransportTrip>();
        foreach (var sourceId in sourceTrips)
        {
            var source = await GetWithDetailsAsync(sourceId);
            foreach (var stop in source.Stops)
            {
                foreach (var link in stop.Legs.Where(l => movingIds.Contains(l.LegId)).ToList())
                {
                    stop.Legs.Remove(link);
                }
            }

            await _trips.UpdateAsync(source, autoSave: true);
            sources.Add(source);
        }

        _assignment.Assign(trip, legs, onTrip, allowOutsideWindow, afterStopId, moving, NewId);

        /* Flushed at once: the queries below (legs on the trip, the search key) read the database. */
        await _legs.UpdateManyAsync(legs, autoSave: true);
        foreach (var leg in legs)
        {
            await _events.PublishAsync(new LegAssignedEto { LegId = leg.Id, TripId = trip.Id, OrderId = leg.OrderId });
        }

        await AfterLegsChangedAsync(trip, legs.Select(l => l.OrderId), "LegsAssigned");

        foreach (var source in sources)
        {
            await RecomposeAsync(source, legs.Select(l => l.OrderId), "LegsMovedAway");
            foreach (var leg in legs)
            {
                await _events.PublishAsync(new LegUnassignedEto { LegId = leg.Id, TripId = source.Id, OrderId = leg.OrderId });
            }
        }
    }

    public async Task UnassignAsync(TransportTrip trip, IReadOnlyList<Leg> legs)
    {
        var onTrip = await LegsOnTripAsync(trip.Id);
        _assignment.Unassign(trip, legs, onTrip, NewId);
        await _legs.UpdateManyAsync(onTrip, autoSave: true);
        foreach (var leg in legs)
        {
            await _events.PublishAsync(new LegUnassignedEto { LegId = leg.Id, TripId = trip.Id, OrderId = leg.OrderId });
            /* An unplanned leg's CO₂ falls back to the great-circle estimate. */
            await _scheduler.RequestAsync(PlanningExecutionApplicationModule.LegTargetType, leg.Id, "LegUnassigned");
        }

        await AfterLegsChangedAsync(trip, legs.Select(l => l.OrderId), "LegsUnassigned");
    }

    /// <summary>Rebuilds the stops after legs left the trip elsewhere (regeneration, move, cancel) and publishes the change.</summary>
    public async Task RecomposeAsync(TransportTrip trip, IEnumerable<Guid> orderIds, string reason)
    {
        var onTrip = await LegsOnTripAsync(trip.Id);
        _assignment.Recompose(trip, onTrip, NewId);
        await _legs.UpdateManyAsync(onTrip, autoSave: true);
        await AfterLegsChangedAsync(trip, orderIds, reason);
    }

    public async Task AfterLegsChangedAsync(TransportTrip trip, IEnumerable<Guid> orderIds, string reason)
    {
        var onTrip = await LegsOnTripAsync(trip.Id);
        await RefreshSearchKeyAsync(trip, onTrip);
        await _trips.UpdateAsync(trip, autoSave: true);
        await _events.PublishAsync(new TransportTripLegsChangedEto { TripId = trip.Id, OrderIds = orderIds.Distinct().ToList() });
        await _scheduler.RequestAsync(PlanningExecutionApplicationModule.TripTargetType, trip.Id, reason);
    }

    public async Task ChangedAsync(TransportTrip trip, TripChangeKind change, bool recalculate)
    {
        await RefreshSearchKeyAsync(trip, null);
        await _trips.UpdateAsync(trip, autoSave: true);
        await _events.PublishAsync(new TransportTripChangedEto { TripId = trip.Id, Change = change });
        if (recalculate)
        {
            await _scheduler.RequestAsync(PlanningExecutionApplicationModule.TripTargetType, trip.Id, change.ToString());
        }
    }

    public async Task StatusChangedAsync(TransportTrip trip, IEnumerable<Guid> orderIds)
    {
        await _trips.UpdateAsync(trip);
        await _events.PublishAsync(new TransportTripStatusChangedEto { TripId = trip.Id, Status = trip.Status, OrderIds = orderIds.Distinct().ToList() });
    }

    /// <summary>Requests the trip's calculation (design 49); returns the version the values will be calculated for.</summary>
    public Task<long> RequestRecalculationAsync(Guid tripId, string reason) => _scheduler.RequestAsync(PlanningExecutionApplicationModule.TripTargetType, tripId, reason);

    /// <summary>The searchable text of the trip's row: number, name, carrier, vehicles, customers, stop cities (one lookup each).</summary>
    public async Task RefreshSearchKeyAsync(TransportTrip trip, IReadOnlyList<Leg>? legsOnTrip)
    {
        legsOnTrip ??= await LegsOnTripAsync(trip.Id);
        var parts = new List<string?> { trip.Number, trip.Name };

        var relationNames = await _masterData.RelationNamesAsync([trip.CarrierRelationId]);
        parts.AddRange(relationNames.Values);

        var combinations = await _masterData.CombinationsAsync([trip.VehicleCombinationId]);
        parts.AddRange(combinations.Values.Select(c => c.Name));
        var vehicleIds = trip.VehicleCombinationId != null && combinations.TryGetValue(trip.VehicleCombinationId.Value, out var combination)
            ? combination.VehicleIds
            : trip.Vehicles.Select(v => v.VehicleId).ToList();
        var vehicles = await _masterData.VehiclesAsync(vehicleIds);
        parts.AddRange(vehicles.Values.Select(v => v.Name));
        parts.AddRange(vehicles.Values.Select(v => v.LicencePlate));

        var orders = await OrderReferencesAsync(legsOnTrip.Select(l => l.OrderId));
        parts.AddRange(orders.Values.Select(o => o.CustomerName));
        parts.AddRange(orders.Values.Select(o => o.Number));
        parts.AddRange(trip.Stops.Select(s => s.City));

        trip.SetSearchKey(string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct()));
    }

    public async Task<Dictionary<Guid, TransportOrderReferenceDto>> OrderReferencesAsync(IEnumerable<Guid> orderIds)
    {
        var ids = orderIds.Distinct().ToList();
        var result = new Dictionary<Guid, TransportOrderReferenceDto>();
        foreach (var chunk in ids.Chunk(ITransportOrderLookup.MaxKeys))
        {
            foreach (var reference in await _orders.GetAsync(chunk))
            {
                result[reference.Id] = reference;
            }
        }

        return result;
    }

    private async Task EnsureOrdersPlannableAsync(IReadOnlyList<Leg> legs)
    {
        var orders = await OrderReferencesAsync(legs.Select(l => l.OrderId));
        var cancelled = orders.Values.FirstOrDefault(o => o.Status == Orders.TransportOrders.TransportOrderStatus.Cancelled);
        if (cancelled != null)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.OrderCancelled).WithData("orderId", cancelled.Id);
        }
    }

    private static Volo.Abp.Linq.IAsyncQueryableExecuter AsyncExecuterOf(IRepository<TransportTrip, Guid> repository) => repository.AsyncExecuter;
}
