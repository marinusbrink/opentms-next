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
    private readonly LegChainService _chain;
    private readonly IOrderLineSplitter _lineSplitter;

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
        IDistributedEventBus events,
        LegChainService chain,
        IOrderLineSplitter lineSplitter)
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
        _chain = chain;
        _lineSplitter = lineSplitter;
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
            if (input.Ids.Count > PlanningExecutionConsts.MaxIdsPerQuery)
            {
                throw new BusinessException(PlanningExecutionErrorCodes.TooManyIds).WithData("max", PlanningExecutionConsts.MaxIdsPerQuery);
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
        await EnsureNotOnFinalTripAsync(leg);
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

    /// <summary>Design 74 §4: the legs of an order line for Manage legs, in route order. Statements: the legs, their actuals, the mapper's batches.</summary>
    [RequiresFeature(AppFeatures.TransportApp.Trips, AppFeatures.Planboard)]
    [Authorize(PlanningExecutionPermissions.Legs.Default)]
    public virtual async Task<LegChainDto> GetLineAsync(Guid lineId) => await ChainDtoAsync(await LegsOfLineAsync(lineId));

    /// <summary>
    /// Design 74 §7: what the leg's order line carries, through Orders' published splitter, so the split dialog needs no Transport
    /// feature nor the right to read orders. The leg itself does not do: a part of a line on a reshaped line carries less.
    /// </summary>
    [RequiresFeature(AppFeatures.TransportApp.Trips, AppFeatures.Planboard)]
    [Authorize(PlanningExecutionPermissions.Legs.Default)]
    public virtual async Task<LegLineTotalsDto> GetLineTotalsAsync(Guid id)
    {
        var leg = await _legs.GetAsync(id);
        var totals = await _lineSplitter.GetLineTotalsAsync(leg.OrderId, leg.OrderLineId);
        return new LegLineTotalsDto
        {
            Quantity = totals.Quantity,
            Colli = totals.Colli,
            WeightKg = totals.WeightKg,
            VolumeM3 = totals.VolumeM3,
            LoadingMeters = totals.LoadingMeters,
            LoadFactor = totals.LoadFactor,
            Epq = totals.Epq
        };
    }

    /// <summary>
    /// Design 74 §4: saves the chain by the rules of <see cref="LegChainService"/>, in one transaction. The line must still be as it
    /// was read (each leg's stamp, none added or removed: 409). Publishes <c>LegsGenerated</c> when anything changed and requests the
    /// route of every new or changed leg. Refused on a cancelled order. Legs on trips stay as they are, so no trip recalculates.
    /// </summary>
    [RequiresFeature(AppFeatures.TransportApp.Trips, AppFeatures.Planboard)]
    [Authorize(PlanningExecutionPermissions.Legs.Update)]
    public virtual async Task<LegChainDto> UpdateLineAsync(Guid lineId, LegChainUpdateDto input)
    {
        var current = await LegsOfLineAsync(lineId);
        await EnsureOrderNotCancelledAsync(current[0].OrderId);
        EnsureAsRead(lineId, current, input);

        var actuals = await _ops.LegsWithActualsAsync(current.Where(l => l.TripId != null).Select(l => l.Id).ToList());
        var readOnly = current.Where(l => l.TripId != null || actuals.Contains(l.Id)).Select(l => l.Id).ToHashSet();
        await EnsureWaypointTypesExistAsync(input.Rows);
        var rows = input.Rows.Select(r => new LegChainRowInput(r.Id, ToEnd(r.Origin), ToEnd(r.Destination), r.Remarks)).ToList();

        /* The places the line does not know yet come from Master Data, one batched lookup. */
        var ends = current.SelectMany(l => new[] { l.Origin, l.Destination }).ToList();
        var newRelations = rows.SelectMany(r => new[] { r.Origin, r.Destination })
            .Where(e => !ends.Any(k => k.RelationId == e.RelationId && (e.RelationAddressId == null || k.RelationAddressId == e.RelationAddressId)))
            .Select(e => e.RelationId);
        var addresses = await _masterData.AddressesOfRelationsAsync(newRelations);
        var result = _chain.Apply(current, rows, readOnly, (relationId, addressId) => MasterDataReferences.PickAddress(addresses[relationId], addressId) is { } address ? MasterDataReferences.ToDepot(address) : null, GuidGenerator.Create);
        if (!result.HasChanges)
        {
            return await ChainDtoAsync(current);
        }

        /* Removed first: a kept leg may take over the stops of the unique key a removed leg held. */
        if (result.Removed.Count > 0)
        {
            await _legs.DeleteManyAsync(result.Removed, autoSave: true);
        }

        var updated = result.Changed.Concat(result.Edited).Concat(result.Repositioned).ToList();
        if (updated.Count > 0)
        {
            await _legs.UpdateManyAsync(updated, autoSave: true);
        }

        if (result.Created.Count > 0)
        {
            await _legs.InsertManyAsync(result.Created, autoSave: true);
        }

        await AfterReshapeAsync(current[0].OrderId, result.Created.Concat(result.Changed).Select(l => l.Id).ToList(), "LegChainChanged");
        await Notices.LegsAsync(result.Created.Concat(updated));
        await Notices.LegsAsync(result.Removed, deleted: true);
        Logger.LogInformation("Legs of line {LineId} of order {OrderId} managed: {Created} created, {Changed} changed, {Removed} removed.", lineId, current[0].OrderId, result.Created.Count, result.Changed.Count, result.Removed.Count);
        return await ChainDtoAsync(await LegsOfLineAsync(lineId));
    }

    /// <summary>
    /// Design 74 §7, the documented synchronous call of CLAUDE.md rule 3: splits the order line in Orders and its legs here, in one
    /// unit of work. A generator-owned line regenerates (the old legs keep their ends and their trips); of a reshaped line every leg
    /// is cloned with its source, order stops mapped to the new line's, synthetic ends kept. Every clone goes on its original's
    /// trip, except the clone of the chosen leg and those of a stuck original (<see cref="TripOperations.StuckLegsAsync"/>: on a
    /// completed or cancelled trip, or with actuals on its stops), which stay unplanned. The later delivery of the line's event
    /// changes nothing.
    /// </summary>
    [RequiresFeature(AppFeatures.TransportApp.Trips, AppFeatures.Planboard)]
    [Authorize(PlanningExecutionPermissions.Legs.Update)]
    public virtual async Task<LegSplitOrderLineResultDto> SplitOrderLineAsync(Guid id, LegSplitOrderLineDto input)
    {
        var chosen = await _legs.GetAsync(id);
        var originals = await LegsOfLineAsync(chosen.OrderLineId);
        var generatorOwned = originals.All(l => l.IsGeneratorOwned);
        var split = await _lineSplitter.SplitAsync(chosen.OrderId, chosen.OrderLineId, input.Quantity);

        var clones = generatorOwned
            ? (await _generation.RegenerateAsync(chosen.OrderId, "OrderLineSplit")).Created.Where(l => l.OrderLineId == split.NewLineId).OrderBy(l => l.Position).ToList()
            : await CloneReshapedLegsAsync(originals, split);

        /* Pairs by position: a reshaped line's clones have their original's; generated legs pair up in route order. */
        var pairs = originals.OrderBy(l => l.Position).ThenBy(l => l.Id).Zip(clones, (original, clone) => (Original: original, Clone: clone)).ToList();
        var (stuck, _) = await _ops.StuckLegsAsync(originals);
        bool JoinsTheTrip(Leg original) => original.TripId != null && original.Id != chosen.Id && !stuck.ContainsKey(original.Id);
        foreach (var (original, clone) in pairs)
        {
            /* Before the assignment, so the composer sees it; the flag only means something on a trip (Leg.Unassign clears it). The
             * reshaped path copied the planner's edits with the ends; generated legs start from the order's stops. Same places only:
             * new via-depot rules may have changed the route meanwhile. */
            clone.SetCarriedOutSeparately(JoinsTheTrip(original) && original.IsCarriedOutSeparately);
            if (generatorOwned && original.OriginRelationId == clone.OriginRelationId && original.DestinationRelationId == clone.DestinationRelationId)
            {
                clone.SetEditable(original.OriginReference, original.DestinationReference, original.Remarks, original.OriginPlannedDate, original.DestinationPlannedDate,
                    new LegWindow(original.OriginWindowFrom, original.OriginWindowTill), new LegWindow(original.DestinationWindowFrom, original.DestinationWindowTill));
            }
        }

        if (clones.Count > 0)
        {
            await _legs.UpdateManyAsync(clones, autoSave: true);
        }

        var receiving = new HashSet<Guid>();
        foreach (var group in pairs.Where(p => JoinsTheTrip(p.Original)).GroupBy(p => p.Original.TripId!.Value))
        {
            var trip = await _ops.GetWithDetailsAsync(group.Key);
            await _ops.AssignAsync(trip, group.Select(p => p.Clone).ToList(), allowOutsideWindow: true, afterStopId: null, moving: false);
            receiving.Add(trip.Id);
        }

        if (!generatorOwned)
        {
            /* The generator asks this itself; here the originals' new quantities must still reach the trips that got no clone. */
            foreach (var tripId in originals.Where(l => l.TripId != null).Select(l => l.TripId!.Value).Distinct().Where(t => !receiving.Contains(t)))
            {
                await _ops.RequestRecalculationAsync(tripId, "OrderLineSplit");
            }
        }

        await CurrentUnitOfWork!.SaveChangesAsync();
        Logger.LogInformation("Line {LineId} of order {OrderId} split into {NewLineId}: {CloneCount} new legs, {OnTrips} trips received one.", chosen.OrderLineId, chosen.OrderId, split.NewLineId, clones.Count, receiving.Count);
        var mapped = await _mapper.MapAsync(originals.Concat(clones).ToList());
        return new LegSplitOrderLineResultDto
        {
            NewLineId = split.NewLineId,
            RemainingQuantity = split.RemainingQuantity,
            NewQuantity = split.NewQuantity,
            NewLegs = mapped.Where(l => l.OrderLineId == split.NewLineId).ToList(),
            RemainingLegs = mapped.Where(l => l.OrderLineId != split.NewLineId).ToList()
        };
    }

    /// <summary>
    /// A reshaped line keeps its shape: each leg is cloned with its source and scaled, the original keeps the rest, so the totals stay
    /// exact. An end at an order stop moves to the new line's stop and follows it from now on (its source key); a synthetic end stays.
    /// </summary>
    private async Task<List<Leg>> CloneReshapedLegsAsync(List<Leg> originals, OrderLineSplitResultDto split)
    {
        var factor = split.NewQuantity / split.TotalQuantity;
        var newStops = await _generation.StopsOfLineAsync(originals[0].OrderId, split.NewLineId);
        var clones = new List<Leg>();
        foreach (var leg in originals)
        {
            var part = leg.Quantities.Scale(factor);
            var originStop = split.StopIdMap.GetValueOrDefault(leg.OriginStopId, leg.OriginStopId);
            var destinationStop = split.StopIdMap.GetValueOrDefault(leg.DestinationStopId, leg.DestinationStopId);
            var clone = new Leg(GuidGenerator.Create(), leg.TenantId, leg.OrderId, split.NewLineId, leg.CustomerRelationId, leg.Position, leg.Source,
                leg.Origin with { StopId = originStop }, leg.Destination with { StopId = destinationStop }, part);
            if (leg.ViaDepotRuleId != null)
            {
                clone.MarkViaDepot(leg.ViaDepotRuleId.Value);
            }

            /* SplitFromLegId stays empty: the leg a split at an address replaced belongs to the old line, so only there can the split be removed. */
            clone.SetEditable(leg.OriginReference, leg.DestinationReference, leg.Remarks, null, null);
            clone.InheritSourceKeys(newStops.TryGetValue(originStop, out var origin) ? Leg.SourceKey(origin) : null, newStops.TryGetValue(destinationStop, out var destination) ? Leg.SourceKey(destination) : null);
            leg.SetQuantities(leg.Quantities.Without(part));
            clones.Add(clone);
        }

        await _legs.InsertManyAsync(clones, autoSave: true);
        await _legs.UpdateManyAsync(originals, autoSave: true);
        await AfterReshapeAsync(originals[0].OrderId, clones.Select(l => l.Id).ToList(), "OrderLineSplit");
        await Notices.LegsAsync(originals.Concat(clones));
        return clones;
    }

    private async Task<List<Leg>> LegsOfLineAsync(Guid lineId)
    {
        var legs = await _legs.GetListAsync(l => l.OrderLineId == lineId);
        return legs.Count == 0
            ? throw new Volo.Abp.Domain.Entities.EntityNotFoundException(typeof(Leg), lineId)
            : legs.OrderBy(l => l.Position).ThenBy(l => l.Id).ToList();
    }

    private async Task<LegChainDto> ChainDtoAsync(List<Leg> legs)
    {
        var actuals = await _ops.LegsWithActualsAsync(legs.Where(l => l.TripId != null).Select(l => l.Id).ToList());
        return new LegChainDto
        {
            OrderId = legs[0].OrderId,
            OrderLineId = legs[0].OrderLineId,
            IsChain = LegChainService.IsChain(legs),
            Rows = (await _mapper.MapAsync(legs)).Select(l => new LegChainRowDto
            {
                Leg = l,
                HasActuals = actuals.Contains(l.Id),
                IsReadOnly = l.TripId != null || actuals.Contains(l.Id)
            }).ToList()
        };
    }

    /// <summary>The stamps name exactly the legs the line has now, each as it was read; every row names one of them or none.</summary>
    private static void EnsureAsRead(Guid lineId, List<Leg> current, LegChainUpdateDto input)
    {
        var stamps = input.Stamps.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().ConcurrencyStamp);
        var named = input.Rows.Where(r => r.Id != null).Select(r => r.Id!.Value).ToList();
        if (stamps.Count != current.Count
            || current.Any(l => !stamps.TryGetValue(l.Id, out var stamp) || !string.Equals(stamp, l.ConcurrencyStamp, StringComparison.Ordinal))
            || named.Distinct().Count() != named.Count
            || named.Any(id => !stamps.ContainsKey(id)))
        {
            throw new AbpDbConcurrencyException($"The legs of order line {lineId} were changed after they were read.");
        }
    }

    private async Task EnsureWaypointTypesExistAsync(List<LegChainRowInputDto> rows)
    {
        if (rows.Count > PlanningExecutionConsts.MaxLegsPerCommand)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.TooManyLegs).WithData("max", PlanningExecutionConsts.MaxLegsPerCommand);
        }

        var ids = rows.SelectMany(r => new[] { r.Origin.WaypointTypeId, r.Destination.WaypointTypeId }).Where(t => t != Guid.Empty).Distinct().ToList();
        var found = (await _orderLookup.GetWaypointTypesAsync(ids)).Select(t => t.Id).ToHashSet();
        var missing = ids.FirstOrDefault(t => !found.Contains(t));
        if (missing != Guid.Empty)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "waypointTypeId").WithData("value", missing);
        }
    }

    private static LegChainEndInput ToEnd(LegChainEndInputDto end)
        => new(end.WaypointTypeId, end.RelationId, end.RelationAddressId, end.PlannedDate, end.WindowFrom, end.WindowTill, end.Reference);

    /// <summary>Design 74 §6: a leg on a completed or cancelled trip is read-only.</summary>
    private async Task EnsureNotOnFinalTripAsync(Leg leg)
    {
        if (leg.TripId == null)
        {
            return;
        }

        var states = await _ops.TripStatesAsync([leg.TripId.Value]);
        if (states.TryGetValue(leg.TripId.Value, out var trip) && trip.Status is TransportTripStatus.Completed or TransportTripStatus.Cancelled)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.LegOnFinalTrip).WithData("legId", leg.Id).WithData("number", trip.Number);
        }
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
