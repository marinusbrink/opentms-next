using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.Orders.Published;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.Events;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.PlanningExecution.ViaDepots;
using OpenTms.Platform.DerivedValues;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace OpenTms.PlanningExecution.Legs;

/// <summary>
/// Runs the leg generation for one order (design 59 "Generation"): reads the order's facts through Orders' published
/// reader, the via-depot rules and their depot addresses, hands them to <see cref="LegGenerator"/>, persists the outcome,
/// takes changed legs off their trips (warning <c>LegChangedAfterPlanning</c>), publishes <c>LegsGenerated</c> and
/// requests the routing of every new or changed leg. Idempotent: the same facts twice change nothing.
/// </summary>
public class OrderLegGeneration : ITransientDependency
{
    public ILogger<OrderLegGeneration> Logger { get; set; } = NullLogger<OrderLegGeneration>.Instance;

    private readonly IOrderPricingReader _orders;
    private readonly ITransportOrderLookup _orderLookup;
    private readonly IRepository<Leg, Guid> _legs;
    private readonly IRepository<ViaDepotRule, Guid> _rules;
    private readonly LegGenerator _generator;
    private readonly MasterDataReferences _masterData;
    private readonly TripOperations _trips;
    private readonly IDistributedEventBus _events;
    private readonly IDerivedValueScheduler _scheduler;
    private readonly IGuidGenerator _guids;
    private readonly ICurrentTenant _currentTenant;

    public OrderLegGeneration(
        IOrderPricingReader orders,
        ITransportOrderLookup orderLookup,
        IRepository<Leg, Guid> legs,
        IRepository<ViaDepotRule, Guid> rules,
        LegGenerator generator,
        MasterDataReferences masterData,
        TripOperations trips,
        IDistributedEventBus events,
        IDerivedValueScheduler scheduler,
        IGuidGenerator guids,
        ICurrentTenant currentTenant)
    {
        _orders = orders;
        _orderLookup = orderLookup;
        _legs = legs;
        _rules = rules;
        _generator = generator;
        _masterData = masterData;
        _trips = trips;
        _events = events;
        _scheduler = scheduler;
        _guids = guids;
        _currentTenant = currentTenant;
    }

    /// <summary>Regenerates the legs of an order from its current facts; a missing or cancelled order loses its legs.</summary>
    public async Task<LegGenerationResult> RegenerateAsync(Guid orderId, string reason)
    {
        var snapshot = await _orders.GetSnapshotAsync(orderId);
        var existing = await _legs.GetListAsync(l => l.OrderId == orderId);
        var input = snapshot == null
            ? new LegGenerationInput { OrderId = orderId, IsCancelled = true }
            : ToInput(snapshot);

        var rules = input.IsCancelled ? [] : await _rules.GetListAsync(r => !r.IsArchived);
        var depots = await _masterData.AddressesAsync(rules.Select(r => (Guid?)r.DepotAddressId));
        var successors = await SuccessorsAsync(rules);

        var result = _generator.Generate(input, rules, id => depots.TryGetValue(id, out var a) ? MasterDataReferences.ToDepot(a) : null,
            id => successors.GetValueOrDefault(id), existing, _currentTenant.Id, _guids.Create);
        await PersistAsync(orderId, result, reason);
        return result;
    }

    /// <summary>The order was cancelled or deleted (the fact itself, whatever the reader still shows): every leg goes.</summary>
    public async Task<LegGenerationResult> RemoveAllAsync(Guid orderId, string reason)
    {
        var existing = await _legs.GetListAsync(l => l.OrderId == orderId);
        var result = _generator.Generate(new LegGenerationInput { OrderId = orderId, IsCancelled = true }, [], _ => null, _ => null, existing, _currentTenant.Id, _guids.Create);
        await PersistAsync(orderId, result, reason);
        return result;
    }

    /// <summary>The arrival types' successors (Orders' waypoint types) for the rules that name no departure type (design 59 "Generation").</summary>
    private async Task<Dictionary<Guid, Guid?>> SuccessorsAsync(IReadOnlyList<ViaDepotRule> rules)
    {
        var arrivalTypeIds = rules.Where(r => r.DepartureWaypointTypeId == null).Select(r => r.ArrivalWaypointTypeId).Distinct().ToList();
        if (arrivalTypeIds.Count == 0)
        {
            return new Dictionary<Guid, Guid?>();
        }

        var result = new Dictionary<Guid, Guid?>();
        foreach (var chunk in arrivalTypeIds.Chunk(ITransportOrderLookup.MaxKeys))
        {
            foreach (var type in await _orderLookup.GetWaypointTypesAsync(chunk))
            {
                result[type.Id] = type.SuccessiveWaypointTypeId;
            }
        }

        return result;
    }

    public static LegGenerationInput ToInput(PricingSnapshotDto snapshot) => new()
    {
        OrderId = snapshot.OrderId ?? Guid.Empty,
        CustomerRelationId = snapshot.CustomerRelationId,
        IsCancelled = snapshot.Status == (int)TransportOrderStatus.Cancelled,
        Lines = snapshot.Lines
            .Where(l => l.LineId != null)
            .OrderBy(l => l.Position)
            .Select(l => new LegGenerationLine
            {
                LineId = l.LineId!.Value,
                Quantities = new LegQuantities(l.Quantity, l.WeightKg, l.VolumeM3, l.LoadingMeters, l.Epq, l.LoadFactor, l.TransportUnitId, l.ProductId, l.TemperatureId,
                    l.RequiredTransportCharacteristicIds.ToList()),
                Stops = l.Stops
                    .Where(s => s.StopId != null)
                    .OrderBy(s => s.Position)
                    .Select(s => new LegGenerationStop
                    {
                        Snapshot = new LegEndSnapshot(s.StopId!.Value, s.WaypointTypeId, s.RelationId, s.RelationAddressId, s.AddressName ?? string.Empty, s.City, s.CountryCode,
                            s.Latitude, s.Longitude, s.TimeZoneId, s.Date, s.WindowFrom, s.WindowTill, s.Reference, s.PlannedDurationMin),
                        RegionIds = s.GeographicalRegionIds.ToList()
                    })
                    .ToList()
            })
            .ToList()
    };

    private async Task PersistAsync(Guid orderId, LegGenerationResult result, string reason)
    {
        if (result.Created.Count > 0)
        {
            await _legs.InsertManyAsync(result.Created, autoSave: true);
        }

        if (result.Updated.Count > 0)
        {
            await _legs.UpdateManyAsync(result.Updated, autoSave: true);
        }

        if (result.Deleted.Count > 0)
        {
            await _legs.DeleteManyAsync(result.Deleted, autoSave: true);
        }

        /* Changed or removed legs leave their trips: the stops are rebuilt and the trip warns (design 59 "Generation"). */
        foreach (var group in result.Unassigned.GroupBy(u => u.TripId))
        {
            var trip = await _trips.GetWithDetailsAsync(group.Key);
            foreach (var (leg, _) in group)
            {
                trip.AddWarning(new TripWarning(TripWarningCode.LegChangedAfterPlanning, leg.Id));
                await _events.PublishAsync(new LegUnassignedEto { LegId = leg.Id, TripId = trip.Id, OrderId = orderId });
            }

            await _trips.RecomposeAsync(trip, [orderId], "LegChangedAfterPlanning");
        }

        var changed = result.Created.Count + result.Updated.Count + result.Deleted.Count;
        if (changed == 0)
        {
            Logger.LogDebug("Legs of order {OrderId}: unchanged ({Reason}).", orderId, reason);
            return;
        }

        var remaining = result.Remaining.ToList();
        await _events.PublishAsync(new LegsGeneratedEto { OrderId = orderId, LegIds = remaining.Select(l => l.Id).ToList() });

        var toRoute = result.Created.Concat(result.Updated).Select(l => l.Id).Distinct().ToList();
        if (toRoute.Count > 0)
        {
            await _scheduler.RequestManyAsync(PlanningExecutionApplicationModule.LegTargetType, toRoute, reason);
        }

        /* Quantity changes on legs that stayed planned: their trips recalculate load and allocation. */
        foreach (var tripId in result.Updated.Where(l => l.TripId != null).Select(l => l.TripId!.Value).Distinct())
        {
            await _trips.RequestRecalculationAsync(tripId, "LegsGenerated");
        }

        Logger.LogInformation("Legs of order {OrderId} regenerated ({Reason}): {Created} created, {Updated} updated, {Deleted} deleted, {Unassigned} unassigned.",
            orderId, reason, result.Created.Count, result.Updated.Count, result.Deleted.Count, result.Unassigned.Count);
    }
}
