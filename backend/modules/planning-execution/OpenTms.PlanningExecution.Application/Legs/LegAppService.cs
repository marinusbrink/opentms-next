using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.Orders.Published;
using OpenTms.PlanningExecution.Events;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Features;

namespace OpenTms.PlanningExecution.Legs;

/// <summary>
/// Design 59 "LegAppService": the legs of an order, of a trip, or the unplanned list with its filters; the planner's
/// editable fields; the split of a leg at an address and its removal (design 61 "Order page review" point 5). Reads never
/// calculate (AD-07); the Transport app's Trips node gates the endpoints.
/// </summary>
[RequiresFeature(AppFeatures.Transport)]
public class LegAppService : PlanningExecutionAppServiceBase, ILegAppService
{
    private readonly IRepository<Leg, Guid> _legs;
    private readonly LegDtoMapper _mapper;
    private readonly IDerivedValueScheduler _scheduler;
    private readonly LegAssignmentService _assignment;
    private readonly MasterDataReferences _masterData;
    private readonly ITransportOrderLookup _orderLookup;
    private readonly TripOperations _ops;
    private readonly OrderLegGeneration _generation;
    private readonly IDistributedEventBus _events;

    public LegAppService(
        IRepository<Leg, Guid> legs,
        LegDtoMapper mapper,
        IDerivedValueScheduler scheduler,
        LegAssignmentService assignment,
        MasterDataReferences masterData,
        ITransportOrderLookup orderLookup,
        TripOperations ops,
        OrderLegGeneration generation,
        IDistributedEventBus events)
    {
        _legs = legs;
        _mapper = mapper;
        _scheduler = scheduler;
        _assignment = assignment;
        _masterData = masterData;
        _orderLookup = orderLookup;
        _ops = ops;
        _generation = generation;
        _events = events;
    }

    [RequiresFeature(AppFeatures.Transport, AppFeatures.TransportApp.Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.Legs.Default)]
    public virtual async Task<GridResponse<LegDto>> GetListAsync(LegListRequestDto input)
    {
        var query = await _legs.GetQueryableAsync();
        if (input.OrderId != null)
        {
            query = query.Where(l => l.OrderId == input.OrderId);
        }
        else if (input.TripId != null)
        {
            query = query.Where(l => l.TripId == input.TripId);
        }
        else
        {
            query = query.Where(l => l.TripId == null);
        }

        var total = await AsyncExecuter.LongCountAsync(query);

        if (input.DateFrom != null)
        {
            query = query.Where(l => l.OriginPlannedDate >= input.DateFrom);
        }

        if (input.DateTo != null)
        {
            query = query.Where(l => l.OriginPlannedDate <= input.DateTo);
        }

        if (input.CustomerRelationId != null)
        {
            query = query.Where(l => l.CustomerRelationId == input.CustomerRelationId);
        }

        var term = input.WildcardSearch?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(l =>
                l.OriginName.ToLower().Contains(term) || l.DestinationName.ToLower().Contains(term) ||
                (l.OriginCity ?? "").ToLower().Contains(term) || (l.DestinationCity ?? "").ToLower().Contains(term) ||
                (l.OriginReference ?? "").ToLower().Contains(term) || (l.DestinationReference ?? "").ToLower().Contains(term) ||
                (l.Remarks ?? "").ToLower().Contains(term));
        }

        var filtered = await AsyncExecuter.LongCountAsync(query);
        /* The legs of a trip come in their order of assignment unless the grid sorts otherwise. */
        var sortBy = string.IsNullOrEmpty(input.SortBy) && input.TripId != null && input.OrderId == null ? "tripposition" : input.SortBy;
        var sorted = Sort(query, sortBy, input.Descending);
        var page = await AsyncExecuter.ToListAsync(sorted.Skip(input.StartRow).Take(input.EndRow - input.StartRow));
        return new GridResponse<LegDto> { Rows = await _mapper.MapAsync(page), TotalCount = total, FilteredCount = filtered };
    }

    private static IOrderedQueryable<Leg> Sort(IQueryable<Leg> query, string? sortBy, bool descending)
    {
        IOrderedQueryable<Leg> Order<TKey>(System.Linq.Expressions.Expression<Func<Leg, TKey>> key)
            => descending ? query.OrderByDescending(key).ThenBy(l => l.Id) : query.OrderBy(key).ThenBy(l => l.Id);

        return (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "origindate" or "originplanneddate" => descending
                ? query.OrderByDescending(l => l.OriginPlannedDate).ThenBy(l => l.OrderId).ThenBy(l => l.OrderLineId).ThenBy(l => l.Position)
                : query.OrderBy(l => l.OriginPlannedDate).ThenBy(l => l.OrderId).ThenBy(l => l.OrderLineId).ThenBy(l => l.Position),
            "destinationdate" or "destinationplanneddate" => Order(l => l.DestinationPlannedDate),
            "origin" or "originname" => Order(l => l.OriginName),
            "destination" or "destinationname" => Order(l => l.DestinationName),
            "origincity" => Order(l => l.OriginCity),
            "destinationcity" => Order(l => l.DestinationCity),
            "position" => Order(l => l.Position),
            "tripposition" => Order(l => l.TripPosition),
            "quantity" => Order(l => l.Quantity),
            "weightkg" => Order(l => l.WeightKg),
            "loadingmeters" => Order(l => l.LoadingMeters),
            "distancem" => Order(l => l.DistanceM),
            "allocatedcostamount" => Order(l => l.AllocatedCostAmount),
            "allocatedrevenueamount" => Order(l => l.AllocatedRevenueAmount),
            "customerrelationid" => Order(l => l.CustomerRelationId),
            _ => throw new BusinessException(ReferenceDataErrorCodes.UnknownSortColumn).WithData("sortBy", sortBy ?? "")
        };
    }

    [RequiresFeature(AppFeatures.Transport, AppFeatures.TransportApp.Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.Legs.Default)]
    public virtual async Task<LegDto> GetAsync(Guid id)
    {
        var leg = await _legs.GetAsync(id);
        return (await _mapper.MapAsync([leg])).Single();
    }

    [RequiresFeature(AppFeatures.Transport, AppFeatures.TransportApp.Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.Legs.Update)]
    public virtual async Task<LegDto> UpdateAsync(Guid id, LegUpdateDto input)
    {
        var leg = await _legs.GetAsync(id);
        var before = (leg.OriginPlannedDate, leg.DestinationPlannedDate, leg.OriginWindowFrom, leg.OriginWindowTill, leg.DestinationWindowFrom, leg.DestinationWindowTill);
        leg.SetEditable(input.OriginReference, input.DestinationReference, input.Remarks, input.OriginPlannedDate, input.DestinationPlannedDate,
            input.OriginWindow == null ? null : new LegWindow(input.OriginWindow.From, input.OriginWindow.Till),
            input.DestinationWindow == null ? null : new LegWindow(input.DestinationWindow.From, input.DestinationWindow.Till));
        leg.ConcurrencyStamp = input.ConcurrencyStamp;
        /* Saved now, so the answer carries the new concurrency stamp the next edit sends back. */
        await _legs.UpdateAsync(leg, autoSave: true);

        /* Dates and windows feed the trip's stop times, window warnings and trip-window check: the trip recalculates.
         * The edit is the planner's: the leg stays on its trip (design 59 unassigns only when the order's stop changes). */
        var after = (leg.OriginPlannedDate, leg.DestinationPlannedDate, leg.OriginWindowFrom, leg.OriginWindowTill, leg.DestinationWindowFrom, leg.DestinationWindowTill);
        if (before != after && leg.TripId != null)
        {
            await _scheduler.RequestAsync(PlanningExecutionApplicationModule.TripTargetType, leg.TripId.Value, "LegDatesChanged");
        }

        return (await _mapper.MapAsync([leg])).Single();
    }

    /// <summary>
    /// Design 61 "Order page review" point 5: A→B becomes A→X and X→B (<see cref="LegAssignmentService.SplitAtAddress"/>). The
    /// address facts come through Master Data's published lookup (as a via depot's), the activities through Orders' published
    /// waypoint types. The leg is soft-deleted (restored by <see cref="UndoSplitAtAddressAsync"/>); the line now counts as
    /// reshaped, so regeneration keeps the halves. Publishes <c>LegsGenerated</c> (revenue and the order's summary follow)
    /// and requests the halves' routes. Refused on a cancelled order and while the leg is on a trip.
    /// </summary>
    [RequiresFeature(AppFeatures.Transport, AppFeatures.TransportApp.Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.Legs.Update)]
    public virtual async Task<List<LegDto>> SplitAtAddressAsync(Guid id, LegSplitAtAddressDto input)
    {
        var leg = await _legs.GetAsync(id);
        await EnsureOrderNotCancelledAsync(leg.OrderId);
        if (leg.TripId != null)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.LegOnTrip).WithData("legId", leg.Id).WithData("tripId", leg.TripId.Value);
        }

        var address = await _masterData.AddressOfRelationAsync(input.RelationId, input.RelationAddressId)
                      ?? throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "relationAddressId").WithData("value", input.RelationAddressId);
        var typeIds = new[] { (Guid?)input.ArrivalWaypointTypeId, input.DepartureWaypointTypeId }.Where(t => t != null).Select(t => t!.Value).Distinct().ToList();
        var types = (await _orderLookup.GetWaypointTypesAsync(typeIds)).ToDictionary(t => t.Id);
        if (!types.TryGetValue(input.ArrivalWaypointTypeId, out var arrivalType))
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "arrivalWaypointTypeId").WithData("value", input.ArrivalWaypointTypeId);
        }

        if (input.DepartureWaypointTypeId != null && !types.ContainsKey(input.DepartureWaypointTypeId.Value))
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "departureWaypointTypeId").WithData("value", input.DepartureWaypointTypeId.Value);
        }

        /* As the via depot (design 59 "Generation", design 51): the departure follows the arrival's successive activity. */
        var departureTypeId = input.DepartureWaypointTypeId ?? arrivalType.SuccessiveWaypointTypeId ?? input.ArrivalWaypointTypeId;
        var legsOfLine = await _legs.GetListAsync(l => l.OrderLineId == leg.OrderLineId);
        var (first, second) = _assignment.SplitAtAddress(leg, MasterDataReferences.ToDepot(address), input.ArrivalWaypointTypeId, departureTypeId,
            input.Date ?? leg.OriginPlannedDate, legsOfLine, GuidGenerator.Create);

        await _legs.DeleteAsync(leg, autoSave: true);
        await _legs.InsertManyAsync([first, second], autoSave: true);
        var others = legsOfLine.Where(l => l.Id != leg.Id).ToList();
        if (others.Count > 0)
        {
            await _legs.UpdateManyAsync(others, autoSave: true);
        }

        await AfterReshapeAsync(leg.OrderId, [first.Id, second.Id], "LegSplitAtAddress");
        Logger.LogInformation("Leg {LegId} of order {OrderId} split at an address into {FirstLegId} and {SecondLegId}.", leg.Id, leg.OrderId, first.Id, second.Id);
        return await _mapper.MapAsync([first, second]);
    }

    /// <summary>
    /// Design 61 point 5, the reverse, on either half: the leg the split replaced comes back as A→B
    /// (<see cref="LegAssignmentService.RemoveSplitAtAddress"/>) with its own id and source, so a generated or via-depot leg is
    /// the generator's again. Its soft-deleted row is read with the soft-delete filter off (the tenant filter stays on); a
    /// line whose legs are all the generator's again is regenerated from the order, which refreshes the quantities a
    /// reshaped line does not follow. Publishes <c>LegsGenerated</c> and requests the route. Refused on a cancelled order,
    /// while a half is on a trip, on a leg that is not a half, and when the other half was split again.
    /// </summary>
    [RequiresFeature(AppFeatures.Transport, AppFeatures.TransportApp.Trips, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.Legs.Update)]
    public virtual async Task<LegDto> UndoSplitAtAddressAsync(Guid id)
    {
        var half = await _legs.GetAsync(id);
        await EnsureOrderNotCancelledAsync(half.OrderId);
        var legsOfLine = await _legs.GetListAsync(l => l.OrderLineId == half.OrderLineId);
        var (first, second) = LegAssignmentService.HalvesOf(half, legsOfLine);

        Leg? original;
        /* Soft-deleted rows only (the leg the split replaced); the multi-tenancy filter stays on. */
        using (DataFilter.Disable<ISoftDelete>())
        {
            original = await _legs.FindAsync(half.SplitFromLegId!.Value);
        }

        var existed = original != null;
        original ??= new Leg(GuidGenerator.Create(), half.TenantId, half.OrderId, half.OrderLineId, half.CustomerRelationId, first.Position, LegSource.Generated,
            first.Origin, second.Destination, first.Quantities);
        var (restored, a, b) = _assignment.RemoveSplitAtAddress(half, legsOfLine, original);

        await _legs.DeleteManyAsync([a, b], autoSave: true);
        if (existed)
        {
            await _legs.UpdateAsync(restored, autoSave: true);
        }
        else
        {
            await _legs.InsertAsync(restored, autoSave: true);
        }

        var others = legsOfLine.Where(l => l.Id != a.Id && l.Id != b.Id).ToList();
        if (others.Count > 0)
        {
            await _legs.UpdateManyAsync(others, autoSave: true);
        }

        if (others.Append(restored).All(l => l.IsGeneratorOwned))
        {
            await _generation.RegenerateAsync(half.OrderId, "LegSplitRemoved");
        }

        await AfterReshapeAsync(half.OrderId, [restored.Id], "LegSplitRemoved");
        Logger.LogInformation("Split of leg {LegId} of order {OrderId} removed ({FirstLegId}, {SecondLegId}).", restored.Id, half.OrderId, a.Id, b.Id);
        var current = await _legs.FindAsync(restored.Id) ?? restored;
        return (await _mapper.MapAsync([current])).Single();
    }

    /// <summary>The order's legs changed shape: <c>LegsGenerated</c> as regeneration publishes it, and the new legs' routes requested.</summary>
    private async Task AfterReshapeAsync(Guid orderId, List<Guid> toRoute, string reason)
    {
        var live = await _legs.GetListAsync(l => l.OrderId == orderId);
        await _events.PublishAsync(new LegsGeneratedEto { OrderId = orderId, LegIds = live.Select(l => l.Id).ToList() });
        var route = toRoute.Where(id => live.Any(l => l.Id == id)).ToList();
        if (route.Count > 0)
        {
            await _scheduler.RequestManyAsync(PlanningExecutionApplicationModule.LegTargetType, route, reason);
        }
    }

    private async Task EnsureOrderNotCancelledAsync(Guid orderId)
    {
        var orders = await _ops.OrderReferencesAsync([orderId]);
        if (orders.TryGetValue(orderId, out var order) && order.Status == Orders.TransportOrders.TransportOrderStatus.Cancelled)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.OrderCancelled).WithData("orderId", orderId);
        }
    }
}
