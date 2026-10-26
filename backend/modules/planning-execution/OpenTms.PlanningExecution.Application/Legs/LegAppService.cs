using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.PlanningExecution.Legs;

/// <summary>
/// Design 59 "LegAppService": the legs of an order, of a trip, or the unplanned list with its filters; the planner's
/// editable fields. Reads never calculate (AD-07); the Transport app's Trips node gates the endpoints.
/// </summary>
[RequiresFeature(AppFeatures.Transport)]
public class LegAppService : PlanningExecutionAppServiceBase, ILegAppService
{
    private readonly IRepository<Leg, Guid> _legs;
    private readonly LegDtoMapper _mapper;
    private readonly IDerivedValueScheduler _scheduler;

    public LegAppService(IRepository<Leg, Guid> legs, LegDtoMapper mapper, IDerivedValueScheduler scheduler)
    {
        _legs = legs;
        _mapper = mapper;
        _scheduler = scheduler;
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
}
