using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.RecordUpdates;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace OpenTms.PlanningExecution;

/// <summary>
/// Design 68 "Live updates" (design 67, PO decision D4): the one place Planning tells the record channel that a trip or a
/// leg changed. Every committed Planning command calls it; the notifier sends after the commit (nothing on a rollback)
/// and once per record. Notices of the same record within one unit of work are merged here before they reach the
/// notifier (whose last notice wins), so a command that notifies a trip when it loads it and again after the change
/// sends the date range and the resources of both states: a Scheduler row the trip left hears about it too.
/// <list type="bullet">
/// <item>Trip: the planned departure–arrival dates, the organisation unit, the vehicle and driver ids, <c>Removed</c>
/// when the trip was deleted or archived.</item>
/// <item>Leg: the origin date, <c>Removed</c> when it left the unplanned list (it is on a trip, or deleted).</item>
/// </list>
/// Ids and dates only (constitution rule 5).
/// </summary>
public class PlanningRecordNotices : ITransientDependency
{
    public const string TripType = PlanningExecutionApplicationModule.TripTargetType;

    public const string LegType = PlanningExecutionApplicationModule.LegTargetType;

    /// <summary>The unit-of-work item that holds the merged hints per record.</summary>
    private const string PendingItemKey = "OpenTms.PlanningExecution.RecordNotices";

    private readonly IRecordUpdateNotifier _notifier;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public PlanningRecordNotices(IRecordUpdateNotifier notifier, IUnitOfWorkManager unitOfWorkManager)
    {
        _notifier = notifier;
        _unitOfWorkManager = unitOfWorkManager;
    }

    /// <summary>The trip in its current state; call it once on load (the state before) and once after the change.</summary>
    public Task TripAsync(TransportTrip trip, bool deleted = false)
        => NotifyAsync(TripType, trip.Id, new RecordUpdateHints(
            trip.DepartureDate,
            trip.ArrivalDate,
            trip.OrganizationUnitId,
            trip.Vehicles.Select(v => v.VehicleId).Concat(trip.Drivers.Select(d => d.DriverId)).Distinct().ToList(),
            deleted || trip.IsArchived));

    public Task LegAsync(Leg leg, bool deleted = false)
        => NotifyAsync(LegType, leg.Id, new RecordUpdateHints(leg.OriginPlannedDate, leg.OriginPlannedDate, null, null, deleted || leg.TripId != null));

    public async Task LegsAsync(IEnumerable<Leg> legs, bool deleted = false)
    {
        foreach (var leg in legs)
        {
            await LegAsync(leg, deleted);
        }
    }

    private Task NotifyAsync(string targetType, Guid targetId, RecordUpdateHints hints)
    {
        var unitOfWork = _unitOfWorkManager.Current;
        if (unitOfWork != null)
        {
            if (!unitOfWork.Items.TryGetValue(PendingItemKey, out var item) || item is not Dictionary<(string, Guid), RecordUpdateHints> merged)
            {
                merged = new Dictionary<(string, Guid), RecordUpdateHints>();
                unitOfWork.Items[PendingItemKey] = merged;
            }

            if (merged.TryGetValue((targetType, targetId), out var earlier))
            {
                hints = Merge(earlier, hints);
            }

            merged[(targetType, targetId)] = hints;
        }

        return _notifier.NotifyAsync(targetType, targetId, hints);
    }

    /// <summary>The union of both states: the widest date range, every resource, the latest organisation unit and removal.</summary>
    private static RecordUpdateHints Merge(RecordUpdateHints earlier, RecordUpdateHints later)
    {
        var resources = (earlier.ResourceIds ?? []).Concat(later.ResourceIds ?? []).Distinct().ToList();
        return new RecordUpdateHints(
            Min(earlier.From, later.From),
            Max(earlier.Till, later.Till),
            later.OrganizationUnitId ?? earlier.OrganizationUnitId,
            earlier.ResourceIds == null && later.ResourceIds == null ? null : resources,
            later.Removed);
    }

    private static DateOnly? Min(DateOnly? a, DateOnly? b) => a == null ? b : b == null ? a : a < b ? a : b;

    private static DateOnly? Max(DateOnly? a, DateOnly? b) => a == null ? b : b == null ? a : a > b ? a : b;
}
