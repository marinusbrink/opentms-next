using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Orders.Published;
using OpenTms.PlanningExecution.Availability;
using OpenTms.PlanningExecution.Events;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.Planboard;
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
    /// <summary>What a command needs to know about a trip a leg is on.</summary>
    public sealed record TripState(Guid Id, string Number, TransportTripStatus Status);

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
    private readonly PlanningRecordNotices _notices;
    private readonly TripOverlapFanOut _overlaps;
    private readonly IRepository<TripStop, Guid> _stops;
    private readonly IRepository<TripStopLeg> _stopLegs;

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
        TripTypeDefaults typeDefaults,
        PlanningRecordNotices notices,
        TripOverlapFanOut overlaps,
        IRepository<TripStop, Guid> stops,
        IRepository<TripStopLeg> stopLegs)
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
        _notices = notices;
        _overlaps = overlaps;
        _stops = stops;
        _stopLegs = stopLegs;
    }

    /// <summary>The record channel (design 68 "Live updates"): every trip and leg change this class persists is noticed here.</summary>
    public PlanningRecordNotices Notices => _notices;

    /// <summary>Design 68 §3: the recalculation of the trips a change of time, resources or liveness affects.</summary>
    public TripOverlapFanOut Overlaps => _overlaps;

    public Guid NewId() => _guids.Create();

    public DateTimeOffset Now => new(_clock.Now, _clock.Kind == DateTimeKind.Utc ? TimeSpan.Zero : TimeZoneInfo.Local.GetUtcOffset(_clock.Now));

    public async Task<TransportTrip> GetWithDetailsAsync(Guid id)
    {
        var trip = await AsyncExecuterOf(_trips).FirstOrDefaultAsync((await _trips.WithDetailsAsync()).Where(t => t.Id == id));
        return trip ?? throw new EntityNotFoundException(typeof(TransportTrip), id);
    }

    public Task<List<Leg>> LegsOnTripAsync(Guid tripId) => _legs.GetListAsync(l => l.TripId == tripId);

    /// <summary>Number and status of the trips (one statement, design 74 §4 and §8).</summary>
    public async Task<Dictionary<Guid, TripState>> TripStatesAsync(IEnumerable<Guid> tripIds)
    {
        var ids = tripIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await AsyncExecuterOf(_trips).ToListAsync((await _trips.GetQueryableAsync()).Where(t => ids.Contains(t.Id)).Select(t => new { t.Id, t.Number, t.Status }));
        return rows.ToDictionary(t => t.Id, t => new TripState(t.Id, t.Number, t.Status));
    }

    /// <summary>
    /// Design 74 §7 and §8, the one stuck check: the legs of these that stay where they are, because their trip is completed or
    /// cancelled or actual times are recorded on their stops, with the trips they are on. Two statements; unplanned legs are never stuck.
    /// </summary>
    public async Task<(Dictionary<Guid, PlanboardStuckReason> Stuck, Dictionary<Guid, TripState> Trips)> StuckLegsAsync(IEnumerable<Leg> legs)
    {
        var planned = legs.Where(l => l.TripId != null).ToList();
        var trips = await TripStatesAsync(planned.Select(l => l.TripId!.Value));
        var actuals = await LegsWithActualsAsync(planned.Select(l => l.Id).ToList());
        var stuck = new Dictionary<Guid, PlanboardStuckReason>();
        foreach (var leg in planned)
        {
            if (trips.TryGetValue(leg.TripId!.Value, out var trip) && trip.Status is TransportTripStatus.Completed or TransportTripStatus.Cancelled)
            {
                stuck[leg.Id] = PlanboardStuckReason.TripFinal;
            }
            else if (actuals.Contains(leg.Id))
            {
                stuck[leg.Id] = PlanboardStuckReason.HasActuals;
            }
        }

        return (stuck, trips);
    }

    /// <summary>The legs of these with actual times on a stop they are at (one statement, design 74 §4 and §8).</summary>
    public async Task<HashSet<Guid>> LegsWithActualsAsync(IReadOnlyCollection<Guid> legIds)
    {
        var ids = legIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var query = from link in await _stopLegs.GetQueryableAsync()
                    join stop in await _stops.GetQueryableAsync() on link.TripStopId equals stop.Id
                    where ids.Contains(link.LegId) && (stop.ActualArrival != null || stop.ActualDeparture != null)
                    select link.LegId;
        return (await AsyncExecuterOf(_trips).ToListAsync(query.Distinct())).ToHashSet();
    }

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
    /// sequence, the Master Data ids validated. <paramref name="afterDefaults"/> (design 68 "Create trip" from a Scheduler
    /// cell) adjusts the filled request before it is validated: the row's resource goes in its slot after the type's
    /// defaults filled the empty groups.
    /// </summary>
    public async Task<TransportTrip> CreateAsync(TransportTripCreateDto request, Guid? createdFromOrderId, decimal? agreedPurchaseAmount, string? agreedPurchaseCurrencyCode,
        Func<TransportTripCreateDto, Task>? afterDefaults = null)
    {
        var type = await _types.FindAsync(request.TransportTripTypeId)
                   ?? throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "transportTripTypeId").WithData("value", request.TransportTripTypeId);
        var input = await _typeDefaults.FillAsync(request, type);
        if (afterDefaults != null)
        {
            await afterDefaults(input);
        }

        var execution = input.Execution!.Value;
        var stopCompositionMode = input.StopCompositionMode!.Value;
        var firstLastStopRule = input.FirstLastStopRule!.Value;
        await _masterData.EnsureRelationsExistAsync([input.CarrierRelationId, input.DepartureRelationId, input.ArrivalRelationId], "relations");

        var members = await ResolveVehiclesAsync(input.VehicleCombinationId, input.VehicleIds);
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
        trip.SetVehicles(input.VehicleCombinationId, input.VehicleIds, members);
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
        /* Design 68 §3: a new trip may double-book a resource of trips already planned. */
        await _overlaps.RequestForTripAsync(trip, "OverlappingTripCreated", force: true);
        await _notices.TripAsync(trip);
        Logger.LogInformation("Transport trip {TripId} created for tenant {TenantId} (from order {OrderId}).", trip.Id, _currentTenant.Id, createdFromOrderId);
        return trip;
    }

    public Task ValidateVehiclesAsync(Guid? combinationId, IReadOnlyList<Guid> vehicleIds) => ResolveVehiclesAsync(combinationId, vehicleIds);

    /// <summary>
    /// Validates a combination or vehicle ids against Master Data (one lookup each) and returns the combination's active
    /// members in member order (design 68 §1: they become the trip's vehicle rows); empty without a combination.
    /// </summary>
    public async Task<List<Guid>> ResolveVehiclesAsync(Guid? combinationId, IReadOnlyList<Guid> vehicleIds)
    {
        var members = new List<Guid>();
        if (combinationId != null)
        {
            var combinations = await _masterData.CombinationsAsync([combinationId]);
            if (!combinations.TryGetValue(combinationId.Value, out var combination))
            {
                throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "vehicleCombinationId").WithData("value", combinationId.Value);
            }

            members = combination.VehicleIds.ToList();
        }

        var ids = vehicleIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return members;
        }

        var vehicles = await _masterData.VehiclesAsync(ids);
        var missing = ids.FirstOrDefault(id => !vehicles.ContainsKey(id));
        if (missing != Guid.Empty)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "vehicleIds").WithData("value", missing);
        }

        return members;
    }

    /// <summary>Validates and sets the trip's vehicles, a combination with its members (design 68 §1).</summary>
    public async Task SetVehiclesAsync(TransportTrip trip, Guid? combinationId, IReadOnlyList<Guid> vehicleIds)
    {
        var members = await ResolveVehiclesAsync(combinationId, vehicleIds);
        trip.SetVehicles(combinationId, vehicleIds, members);
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
        /* Design 74 §8: the legs of an order can leave several trips at once; each trip hears only of its own. */
        var sourceOf = legs.Where(l => l.TripId != null && l.TripId != trip.Id).ToDictionary(l => l.Id, l => l.TripId!.Value);
        var sourceTrips = sourceOf.Values.Distinct().ToList();
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

        await _notices.LegsAsync(legs);

        await AfterLegsChangedAsync(trip, legs.Select(l => l.OrderId), "LegsAssigned");

        foreach (var source in sources)
        {
            var left = legs.Where(l => sourceOf.GetValueOrDefault(l.Id) == source.Id).ToList();
            await RecomposeAsync(source, left.Select(l => l.OrderId), "LegsMovedAway");
            foreach (var leg in left)
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
        }

        /* An unplanned leg's CO₂ falls back to the great-circle estimate: its own calculation, requested as one batch (design 74 §9).
         * The trip itself is requested once, in AfterLegsChangedAsync; an unplanned leg's route asks no trip again. */
        if (legs.Count > 0)
        {
            await _scheduler.RequestManyAsync(PlanningExecutionApplicationModule.LegTargetType, legs.Select(l => l.Id).ToList(), "LegUnassigned");
        }

        await _notices.LegsAsync(legs);

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
        await _notices.TripAsync(trip);
    }

    /// <summary>Design 75: Reporting's trip fact goes with the trip.</summary>
    public Task DeletedAsync(TransportTrip trip) => _events.PublishAsync(new TransportTripDeletedEto { TripId = trip.Id });

    public async Task ChangedAsync(TransportTrip trip, TripChangeKind change, bool recalculate)
    {
        await RefreshSearchKeyAsync(trip, null);
        await _trips.UpdateAsync(trip, autoSave: true);
        await _events.PublishAsync(new TransportTripChangedEto { TripId = trip.Id, Change = change });
        if (recalculate)
        {
            await _scheduler.RequestAsync(PlanningExecutionApplicationModule.TripTargetType, trip.Id, change.ToString());
        }

        if (change is TripChangeKind.Header or TripChangeKind.Vehicles or TripChangeKind.Drivers)
        {
            /* Before the notice below: without a notice on load, the state before is unknown and the fan-out always runs. */
            await _overlaps.RequestForTripAsync(trip, "Overlapping" + change);
        }

        await _notices.TripAsync(trip);
    }

    /// <summary>
    /// The trip's vehicles changed: persisted, published, the trip's calculation requested, and the routes of its legs too
    /// (they depend on the vehicle profile: toll, truck routing).
    /// </summary>
    public async Task VehiclesChangedAsync(TransportTrip trip)
    {
        await ChangedAsync(trip, TripChangeKind.Vehicles, recalculate: true);
        var legIds = await AsyncExecuterOf(_trips).ToListAsync((await _legs.GetQueryableAsync()).Where(l => l.TripId == trip.Id).Select(l => l.Id));
        if (legIds.Count > 0)
        {
            await _scheduler.RequestManyAsync(PlanningExecutionApplicationModule.LegTargetType, legIds, "VehiclesChanged");
        }
    }

    public async Task StatusChangedAsync(TransportTrip trip, IEnumerable<Guid> orderIds)
    {
        await _trips.UpdateAsync(trip);
        await _events.PublishAsync(new TransportTripStatusChangedEto { TripId = trip.Id, Status = trip.Status, OrderIds = orderIds.Distinct().ToList() });
        if (trip.Status == TransportTripStatus.Cancelled)
        {
            /* Design 68 §3: a cancelled trip blocks nothing any more. */
            await _overlaps.RequestForTripAsync(trip, "OverlappingTripCancelled", force: true);
        }

        await _notices.TripAsync(trip);
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
        /* The stored rows (design 68 §1: a combination's members too); the combination's current members for a trip saved
         * before they were stored. */
        var vehicleIds = trip.Vehicles.Count == 0 && trip.VehicleCombinationId != null && combinations.TryGetValue(trip.VehicleCombinationId.Value, out var combination)
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
