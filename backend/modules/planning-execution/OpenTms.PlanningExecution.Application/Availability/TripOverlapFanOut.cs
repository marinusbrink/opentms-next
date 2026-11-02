using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.DerivedValues;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace OpenTms.PlanningExecution.Availability;

/// <summary>
/// Design 68 §3, the recalculation fan-out: the resource warnings of a trip depend on other trips and on the resources'
/// availability, so
/// <list type="bullet">
/// <item>a trip whose time, resources or liveness changed asks the live trips it overlapped before and after (sharing a
/// vehicle or driver) to recalculate, so the other side of a double booking updates too;</item>
/// <item>an availability change asks the live trips of the resource in the changed period to recalculate.</item>
/// </list>
/// At most <see cref="PlanningExecutionConsts.MaxRecalculationFanOut"/> trips per call (the earliest first), each at most
/// once per unit of work; the requests run in the caller's transaction (design 49), so a rolled-back change asks nothing.
/// The SQL filter is a superset of the exact overlap; a trip asked once too often only recalculates to the same result.
/// </summary>
public class TripOverlapFanOut : ITransientDependency
{
    /// <summary>The unit-of-work item with the trip ids already asked in this unit of work.</summary>
    private const string RequestedItemKey = "OpenTms.PlanningExecution.FanOutRequested";

    public ILogger<TripOverlapFanOut> Logger { get; set; } = NullLogger<TripOverlapFanOut>.Instance;

    private readonly IRepository<TransportTrip, Guid> _trips;
    private readonly IRepository<TripVehicle> _tripVehicles;
    private readonly IRepository<TripDriver> _tripDrivers;
    private readonly IDerivedValueScheduler _scheduler;
    private readonly PlanningRecordNotices _notices;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public TripOverlapFanOut(
        IRepository<TransportTrip, Guid> trips,
        IRepository<TripVehicle> tripVehicles,
        IRepository<TripDriver> tripDrivers,
        IDerivedValueScheduler scheduler,
        PlanningRecordNotices notices,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _trips = trips;
        _tripVehicles = tripVehicles;
        _tripDrivers = tripDrivers;
        _scheduler = scheduler;
        _notices = notices;
        _unitOfWorkManager = unitOfWorkManager;
    }

    /// <summary>
    /// After a command changed <paramref name="trip"/>: compares its footprint with the one noticed when the command loaded
    /// it and, when time, resources or liveness differ (or no earlier state is known, or <paramref name="force"/>), asks the
    /// trips overlapping either state to recalculate.
    /// </summary>
    public Task RequestForTripAsync(TransportTrip trip, string reason, bool force = false)
    {
        var after = TripFootprint.Of(trip);
        var before = _notices.FirstFootprint(trip.Id);
        if (!force && before != null && before.Equals(after))
        {
            return Task.CompletedTask;
        }

        return RequestForFootprintsAsync(trip.Id, [before, after], reason);
    }

    /// <summary>Asks the live trips overlapping any of <paramref name="footprints"/> (sharing a resource) to recalculate, <paramref name="tripId"/> excluded.</summary>
    public async Task RequestForFootprintsAsync(Guid tripId, IEnumerable<TripFootprint?> footprints, string reason)
    {
        var found = new List<(Guid Id, DateTime DepartureUtc)>();
        foreach (var footprint in footprints.Where(f => f is { HasResources: true }).Distinct())
        {
            found.AddRange(await OverlappingAsync(footprint!.VehicleIds, footprint.DriverIds, footprint.StartUtc, footprint.EndUtc, tripId));
        }

        await RequestAsync(found, reason);
    }

    /// <summary>Asks the live trips of a resource that overlap [<paramref name="fromUtc"/>, <paramref name="tillUtc"/>) to recalculate.</summary>
    public async Task RequestForResourceAsync(ResourceKind kind, Guid resourceId, DateTime fromUtc, DateTime tillUtc, string reason)
    {
        var found = kind == ResourceKind.Vehicle
            ? await OverlappingAsync([resourceId], [], fromUtc, tillUtc, null)
            : await OverlappingAsync([], [resourceId], fromUtc, tillUtc, null);
        await RequestAsync(found, reason);
    }

    /// <summary>
    /// The live trips (not <paramref name="excludeTripId"/>) that use one of the vehicles or drivers and whose interval may
    /// overlap the window: two statements, each bounded to the fan-out maximum.
    /// </summary>
    public async Task<List<(Guid Id, DateTime DepartureUtc)>> OverlappingAsync(IReadOnlyCollection<Guid> vehicleIds, IReadOnlyCollection<Guid> driverIds,
        DateTime fromUtc, DateTime tillUtc, Guid? excludeTripId)
    {
        var result = new List<(Guid, DateTime)>();
        if (tillUtc <= fromUtc)
        {
            return result;
        }

        var earliestEnd = fromUtc - TripInterval.MinimumLength;
        var trips = (await _trips.GetQueryableAsync())
            .Where(t => !t.IsArchived && t.Status != TransportTripStatus.Cancelled)
            .Where(t => t.PlannedDepartureUtc < tillUtc && (t.PlannedArrivalUtc ?? t.PlannedDepartureUtc) > earliestEnd);
        if (excludeTripId != null)
        {
            trips = trips.Where(t => t.Id != excludeTripId.Value);
        }

        if (vehicleIds.Count > 0)
        {
            var ids = vehicleIds.ToList();
            var rows = await _trips.AsyncExecuter.ToListAsync(
                (from v in await _tripVehicles.GetQueryableAsync()
                 where ids.Contains(v.VehicleId)
                 join t in trips on v.TripId equals t.Id
                 select new { t.Id, t.PlannedDepartureUtc })
                .Distinct()
                .OrderBy(t => t.PlannedDepartureUtc).ThenBy(t => t.Id)
                .Take(PlanningExecutionConsts.MaxRecalculationFanOut));
            result.AddRange(rows.Select(r => (r.Id, r.PlannedDepartureUtc)));
        }

        if (driverIds.Count > 0)
        {
            var ids = driverIds.ToList();
            var rows = await _trips.AsyncExecuter.ToListAsync(
                (from d in await _tripDrivers.GetQueryableAsync()
                 where ids.Contains(d.DriverId)
                 join t in trips on d.TripId equals t.Id
                 select new { t.Id, t.PlannedDepartureUtc })
                .Distinct()
                .OrderBy(t => t.PlannedDepartureUtc).ThenBy(t => t.Id)
                .Take(PlanningExecutionConsts.MaxRecalculationFanOut));
            result.AddRange(rows.Select(r => (r.Id, r.PlannedDepartureUtc)));
        }

        return result;
    }

    private async Task RequestAsync(List<(Guid Id, DateTime DepartureUtc)> found, string reason)
    {
        if (found.Count == 0)
        {
            return;
        }

        var requested = Requested();
        var ids = found.OrderBy(f => f.DepartureUtc).ThenBy(f => f.Id).Select(f => f.Id).Distinct()
            .Where(id => !requested.Contains(id))
            .Take(Math.Max(0, PlanningExecutionConsts.MaxRecalculationFanOut - requested.Count))
            .ToList();
        if (ids.Count == 0)
        {
            return;
        }

        requested.UnionWith(ids);
        await _scheduler.RequestManyAsync(PlanningExecutionApplicationModule.TripTargetType, ids, reason);
        Logger.LogDebug("{Count} overlapping trips asked to recalculate ({Reason}).", ids.Count, reason);
    }

    private HashSet<Guid> Requested()
    {
        var unitOfWork = _unitOfWorkManager.Current;
        if (unitOfWork == null)
        {
            return new HashSet<Guid>();
        }

        if (!unitOfWork.Items.TryGetValue(RequestedItemKey, out var item) || item is not HashSet<Guid> requested)
        {
            requested = new HashSet<Guid>();
            unitOfWork.Items[RequestedItemKey] = requested;
        }

        return requested;
    }
}
