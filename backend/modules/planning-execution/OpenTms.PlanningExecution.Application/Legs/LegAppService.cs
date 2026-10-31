using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.Orders.Published;
using OpenTms.PlanningExecution.Events;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.PlanningExecution.Planboard;
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
/// calculate (AD-07). Design 68: the unplanned list's organisation units, refetch by id and grouping (D18), the split of a
/// line and the return leg without a trip (§7); every committed command notifies the record channel. Feature: the
/// Transport app's Trips node or the Planboard app (D7): (Transport or Planboard) on the class and (Trips or Planboard)
/// on each method, which ABP both requires, mean (Transport and Trips) or Planboard.
/// </summary>
[RequiresFeature(AppFeatures.Transport, AppFeatures.Planboard)]
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

    private PlanningRecordNotices Notices => _ops.Notices;

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

    [RequiresFeature(AppFeatures.TransportApp.Trips, AppFeatures.Planboard)]
    [Authorize(PlanningExecutionPermissions.Legs.Default)]
    public virtual async Task<GridResponse<LegDto>> GetListAsync(LegListRequestDto input)
    {
        var query = View(await _legs.GetQueryableAsync(), input);
        var total = await AsyncExecuter.LongCountAsync(query);

        query = Filter(query, input);
        if (!string.IsNullOrEmpty(input.GroupBy) && input.GroupKey != null)
        {
            query = InGroup(query, Grouping(input.GroupBy), input.GroupKey);
        }

        var filtered = await AsyncExecuter.LongCountAsync(query);
        /* The legs of a trip come in their order of assignment unless the grid sorts otherwise. */
        var sortBy = string.IsNullOrEmpty(input.SortBy) && input.TripId != null && input.OrderId == null ? "tripposition" : input.SortBy;
        var sorted = Sort(query, sortBy, input.Descending);
        var page = await AsyncExecuter.ToListAsync(sorted.Skip(input.StartRow).Take(input.EndRow - input.StartRow));
        return new GridResponse<LegDto> { Rows = await _mapper.MapAsync(page), TotalCount = total, FilteredCount = filtered };
    }

    /// <summary>
    /// Design 68, PO decision D18: the groups of the list (the same view and filters as <see cref="GetListAsync"/>), each
    /// with its number of legs, in key order and paged; one grouped SQL statement plus its count, and one batched name
    /// lookup for the customer grouping.
    /// </summary>
    [RequiresFeature(AppFeatures.TransportApp.Trips, AppFeatures.Planboard)]
    [Authorize(PlanningExecutionPermissions.Legs.Default)]
    public virtual async Task<GridResponse<LegGroupDto>> GetGroupsAsync(LegListRequestDto input)
    {
        var grouping = Grouping(input.GroupBy);
        var view = View(await _legs.GetQueryableAsync(), input);
        var query = Filter(view, input);
        var total = await AsyncExecuter.LongCountAsync(view);
        var skip = input.StartRow;
        var take = input.EndRow - input.StartRow;

        List<LegGroupDto> rows;
        long groupCount;
        switch (grouping)
        {
            case LegGroupings.OriginDate:
            {
                groupCount = await AsyncExecuter.LongCountAsync(query.Select(l => l.OriginPlannedDate).Distinct());
                var groups = await AsyncExecuter.ToListAsync(query.GroupBy(l => l.OriginPlannedDate)
                    .Select(g => new { g.Key, Count = g.LongCount() })
                    .OrderBy(g => g.Key).Skip(skip).Take(take));
                rows = groups.Select(g =>
                {
                    var key = g.Key.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                    return new LegGroupDto { Key = key, Label = key, Count = g.Count };
                }).ToList();
                break;
            }
            case LegGroupings.Customer:
            {
                groupCount = await AsyncExecuter.LongCountAsync(query.Select(l => l.CustomerRelationId).Distinct());
                var groups = await AsyncExecuter.ToListAsync(query.GroupBy(l => l.CustomerRelationId)
                    .Select(g => new { g.Key, Count = g.LongCount() })
                    .OrderBy(g => g.Key).Skip(skip).Take(take));
                var names = await _masterData.RelationNamesAsync(groups.Select(g => (Guid?)g.Key));
                rows = groups.Select(g => new LegGroupDto
                {
                    Key = g.Key.ToString(),
                    Label = names.GetValueOrDefault(g.Key, L["PlanningExecution:NotFound"].Value),
                    Count = g.Count
                }).ToList();
                break;
            }
            default:
            {
                var origin = grouping == LegGroupings.OriginCity;
                var cities = origin ? query.Select(l => l.OriginCity ?? "") : query.Select(l => l.DestinationCity ?? "");
                groupCount = await AsyncExecuter.LongCountAsync(cities.Distinct());
                var groups = await AsyncExecuter.ToListAsync(cities.GroupBy(c => c)
                    .Select(g => new { g.Key, Count = g.LongCount() })
                    .OrderBy(g => g.Key).Skip(skip).Take(take));
                rows = groups.Select(g => new LegGroupDto { Key = g.Key, Label = g.Key.Length == 0 ? null : g.Key, Count = g.Count }).ToList();
                break;
            }
        }

        return new GridResponse<LegGroupDto> { Rows = rows, TotalCount = total, FilteredCount = groupCount };
    }

    /// <summary>The view of the list: an order's legs, a trip's legs, else the unplanned legs.</summary>
    private static IQueryable<Leg> View(IQueryable<Leg> query, LegListRequestDto input)
    {
        if (input.OrderId != null)
        {
            return query.Where(l => l.OrderId == input.OrderId);
        }

        if (input.TripId != null)
        {
            return query.Where(l => l.TripId == input.TripId);
        }

        return query.Where(l => l.TripId == null);
    }

    private static IQueryable<Leg> Filter(IQueryable<Leg> query, LegListRequestDto input)
    {
        if (input.Ids is { Count: > 0 })
        {
            if (input.Ids.Count > PlanningExecutionConsts.MaxPlanboardPageSize)
            {
                throw new BusinessException(PlanningExecutionErrorCodes.TooManyIds).WithData("max", PlanningExecutionConsts.MaxPlanboardPageSize);
            }

            var ids = input.Ids.Distinct().ToList();
            query = query.Where(l => ids.Contains(l.Id));
        }

        /* input.OrganizationUnitIds: design 68 assumption 1 — a leg is filtered by its order's organisation unit snapshot
         * where it carries one. Legs carry no such snapshot, so every leg shows under every unit (legacy behaviour). */

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

        return query;
    }

    /// <summary>The grouping named by <paramref name="groupBy"/>, from the whitelist (D18).</summary>
    private static string Grouping(string? groupBy)
    {
        var key = groupBy?.Trim().ToLowerInvariant();
        return key != null && LegGroupings.All.Contains(key)
            ? key
            : throw new BusinessException(PlanningExecutionErrorCodes.UnknownGroupBy).WithData("groupBy", groupBy ?? "");
    }

    /// <summary>The legs of one group; a key that cannot be one of the grouping's values matches nothing.</summary>
    private static IQueryable<Leg> InGroup(IQueryable<Leg> query, string grouping, string key)
    {
        switch (grouping)
        {
            case LegGroupings.OriginDate:
                return DateOnly.TryParseExact(key, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date)
                    ? query.Where(l => l.OriginPlannedDate == date)
                    : query.Where(l => false);
            case LegGroupings.Customer:
                return Guid.TryParse(key, out var customer) ? query.Where(l => l.CustomerRelationId == customer) : query.Where(l => false);
            case LegGroupings.OriginCity:
                return key.Length == 0 ? query.Where(l => l.OriginCity == null || l.OriginCity == "") : query.Where(l => l.OriginCity == key);
            default:
                return key.Length == 0 ? query.Where(l => l.DestinationCity == null || l.DestinationCity == "") : query.Where(l => l.DestinationCity == key);
        }
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

    [RequiresFeature(AppFeatures.TransportApp.Trips, AppFeatures.Planboard)]
    [Authorize(PlanningExecutionPermissions.Legs.Default)]
    public virtual async Task<LegDto> GetAsync(Guid id)
    {
        var leg = await _legs.GetAsync(id);
        return (await _mapper.MapAsync([leg])).Single();
    }

    [RequiresFeature(AppFeatures.TransportApp.Trips, AppFeatures.Planboard)]
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
        await Notices.LegAsync(leg);

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
    [RequiresFeature(AppFeatures.TransportApp.Trips, AppFeatures.Planboard)]
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
        await Notices.LegAsync(leg, deleted: true);
        await Notices.LegsAsync([first, second]);
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
    [RequiresFeature(AppFeatures.TransportApp.Trips, AppFeatures.Planboard)]
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
        await Notices.LegsAsync([a, b], deleted: true);
        await Notices.LegAsync(restored);
        Logger.LogInformation("Split of leg {LegId} of order {OrderId} removed ({FirstLegId}, {SecondLegId}).", restored.Id, half.OrderId, a.Id, b.Id);
        var current = await _legs.FindAsync(restored.Id) ?? restored;
        return (await _mapper.MapAsync([current])).Single();
    }

    /// <summary>
    /// Design 68 §7 (P:Q11): the split of a line on a leg without a trip, by the rules of the trip's
    /// (<see cref="LegAssignmentService.SplitLine"/>): part of the quantity moves to a new unplanned leg of the same line.
    /// Refused on a cancelled order and while the leg is on a trip (use the trip's split there). Publishes
    /// <c>LegsGenerated</c> and requests the new leg's route.
    /// </summary>
    [RequiresFeature(AppFeatures.TransportApp.Trips, AppFeatures.Planboard)]
    [Authorize(PlanningExecutionPermissions.Legs.Update)]
    public virtual async Task<LegDto> SplitLineAsync(Guid id, LegSplitLineDto input)
    {
        var leg = await GetUnplannedAsync(id);
        var clone = _assignment.SplitLine(leg, input.Quantity, null, [], allowOutsideWindow: false, GuidGenerator.Create);
        await _legs.InsertAsync(clone, autoSave: true);
        await _legs.UpdateAsync(leg, autoSave: true);
        await AfterReshapeAsync(leg.OrderId, [clone.Id, leg.Id], "LegSplit");
        await Notices.LegsAsync([leg, clone]);
        Logger.LogInformation("Unplanned leg {LegId} of order {OrderId} split into {NewLegId}.", leg.Id, leg.OrderId, clone.Id);
        return (await _mapper.MapAsync([clone])).Single();
    }

    /// <summary>
    /// Design 68 §7 (P:Q11): the return leg of a leg without a trip, by the rules of the trip's
    /// (<see cref="LegAssignmentService.CreateReturnLeg"/>): the mirrored leg of the same line, unplanned. Refused on a
    /// cancelled order, while the leg is on a trip and when the mirrored leg exists.
    /// </summary>
    [RequiresFeature(AppFeatures.TransportApp.Trips, AppFeatures.Planboard)]
    [Authorize(PlanningExecutionPermissions.Legs.Update)]
    public virtual async Task<LegDto> ReturnLegAsync(Guid id)
    {
        var leg = await GetUnplannedAsync(id);
        var legsOfLine = await _legs.GetListAsync(l => l.OrderLineId == leg.OrderLineId);
        var returnLeg = _assignment.CreateReturnLeg(leg, null, [], legsOfLine, GuidGenerator.Create);
        await _legs.InsertAsync(returnLeg, autoSave: true);
        await AfterReshapeAsync(leg.OrderId, [returnLeg.Id], "ReturnLegCreated");
        await Notices.LegAsync(returnLeg);
        Logger.LogInformation("Return leg {NewLegId} created for unplanned leg {LegId} of order {OrderId}.", returnLeg.Id, leg.Id, leg.OrderId);
        return (await _mapper.MapAsync([returnLeg])).Single();
    }

    private async Task<Leg> GetUnplannedAsync(Guid id)
    {
        var leg = await _legs.GetAsync(id);
        await EnsureOrderNotCancelledAsync(leg.OrderId);
        if (leg.TripId != null)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.LegOnTrip).WithData("legId", leg.Id).WithData("tripId", leg.TripId.Value);
        }

        return leg;
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
