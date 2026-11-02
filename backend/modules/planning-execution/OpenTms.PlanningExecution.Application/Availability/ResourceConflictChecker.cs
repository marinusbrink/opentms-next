using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.PlanningExecution.TransportTrips;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.PlanningExecution.Availability;

/// <summary>
/// Design 68 §3: the resource warnings <see cref="TripCalculator"/> writes (never computed on read).
/// <list type="bullet">
/// <item><c>ResourceDoubleBooked</c> (Data = vehicle or driver id): another live trip uses the resource and the planned
/// intervals overlap (<see cref="TripInterval"/>).</item>
/// <item><c>ResourceUnavailable</c> (Data = resource id): an unavailability occurrence of the resource overlaps the trip.</item>
/// <item><c>OutsideWorkingTime</c> (Data = driver id): part of the trip lies outside the driver's working pattern.</item>
/// </list>
/// A cancelled or archived trip blocks nothing and gets none. Four statements: the other trips per vehicle, per driver, the
/// unavailabilities, the drivers' patterns.
/// </summary>
public class ResourceConflictChecker : ITransientDependency
{
    private readonly IRepository<TransportTrip, Guid> _trips;
    private readonly IRepository<TripVehicle> _tripVehicles;
    private readonly IRepository<TripDriver> _tripDrivers;
    private readonly IRepository<ResourceUnavailability, Guid> _unavailabilities;
    private readonly IRepository<ResourceWorkingPattern, Guid> _patterns;
    private readonly AvailabilityExpander _expander;
    private readonly PlanningExecutionClock _clock;

    public ResourceConflictChecker(
        IRepository<TransportTrip, Guid> trips,
        IRepository<TripVehicle> tripVehicles,
        IRepository<TripDriver> tripDrivers,
        IRepository<ResourceUnavailability, Guid> unavailabilities,
        IRepository<ResourceWorkingPattern, Guid> patterns,
        AvailabilityExpander expander,
        PlanningExecutionClock clock)
    {
        _trips = trips;
        _tripVehicles = tripVehicles;
        _tripDrivers = tripDrivers;
        _unavailabilities = unavailabilities;
        _patterns = patterns;
        _expander = expander;
        _clock = clock;
    }

    /// <param name="vehicleIds">The trip's vehicles as the calculator resolved them (a combination's members included).</param>
    public async Task<List<TripWarning>> CheckAsync(TransportTrip trip, IReadOnlyList<Guid> vehicleIds)
    {
        var warnings = new List<TripWarning>();
        if (trip.IsArchived || trip.Status == TransportTripStatus.Cancelled)
        {
            return warnings;
        }

        var vehicles = vehicleIds.Where(id => id != Guid.Empty).Distinct().ToList();
        var drivers = trip.Drivers.Select(d => d.DriverId).Where(id => id != Guid.Empty).Distinct().ToList();
        if (vehicles.Count == 0 && drivers.Count == 0)
        {
            return warnings;
        }

        var startUtc = trip.PlannedDepartureUtc;
        var endUtc = TripInterval.End(trip.PlannedDepartureUtc, trip.PlannedArrivalUtc);

        await AddDoubleBookingsAsync(trip.Id, vehicles, drivers, startUtc, endUtc, warnings);
        await AddUnavailabilityAsync(vehicles, drivers, startUtc, endUtc, warnings);
        await AddOutsideWorkingTimeAsync(drivers, startUtc, endUtc, warnings);
        return warnings;
    }

    private async Task AddDoubleBookingsAsync(Guid tripId, List<Guid> vehicles, List<Guid> drivers, DateTime startUtc, DateTime endUtc, List<TripWarning> warnings)
    {
        var earliestEnd = startUtc - TripInterval.MinimumLength;
        var others = (await _trips.GetQueryableAsync())
            .Where(t => t.Id != tripId && !t.IsArchived && t.Status != TransportTripStatus.Cancelled)
            .Where(t => t.PlannedDepartureUtc < endUtc && (t.PlannedArrivalUtc ?? t.PlannedDepartureUtc) > earliestEnd);

        var booked = new List<(Guid ResourceId, DateTime DepartureUtc, DateTime? ArrivalUtc)>();
        if (vehicles.Count > 0)
        {
            var rows = await _trips.AsyncExecuter.ToListAsync(
                from v in await _tripVehicles.GetQueryableAsync()
                where vehicles.Contains(v.VehicleId)
                join t in others on v.TripId equals t.Id
                select new { v.VehicleId, t.PlannedDepartureUtc, t.PlannedArrivalUtc });
            booked.AddRange(rows.Select(r => (r.VehicleId, r.PlannedDepartureUtc, r.PlannedArrivalUtc)));
        }

        if (drivers.Count > 0)
        {
            var rows = await _trips.AsyncExecuter.ToListAsync(
                from d in await _tripDrivers.GetQueryableAsync()
                where drivers.Contains(d.DriverId)
                join t in others on d.TripId equals t.Id
                select new { d.DriverId, t.PlannedDepartureUtc, t.PlannedArrivalUtc });
            booked.AddRange(rows.Select(r => (r.DriverId, r.PlannedDepartureUtc, r.PlannedArrivalUtc)));
        }

        foreach (var resourceId in booked
                     .Where(b => TripInterval.Overlaps(startUtc, endUtc, b.DepartureUtc, TripInterval.End(b.DepartureUtc, b.ArrivalUtc)))
                     .Select(b => b.ResourceId).Distinct().OrderBy(id => id))
        {
            warnings.Add(new TripWarning(TripWarningCode.ResourceDoubleBooked, null, null, resourceId.ToString()));
        }
    }

    private async Task AddUnavailabilityAsync(List<Guid> vehicles, List<Guid> drivers, DateTime startUtc, DateTime endUtc, List<TripWarning> warnings)
    {
        var rows = await _unavailabilities.AsyncExecuter.ToListAsync((await _unavailabilities.GetQueryableAsync())
            .Where(u => (u.ResourceKind == ResourceKind.Vehicle && vehicles.Contains(u.ResourceId)) || (u.ResourceKind == ResourceKind.Driver && drivers.Contains(u.ResourceId)))
            .Where(u => u.FromUtc < endUtc && u.SpanTillUtc > startUtc));
        if (rows.Count == 0)
        {
            return;
        }

        var from = new DateTimeOffset(startUtc, TimeSpan.Zero);
        var till = new DateTimeOffset(endUtc, TimeSpan.Zero);
        var zone = _clock.Zone;
        foreach (var resourceId in rows.Where(u => _expander.Expand(u, from, till, zone).Any()).Select(u => u.ResourceId).Distinct().OrderBy(id => id))
        {
            warnings.Add(new TripWarning(TripWarningCode.ResourceUnavailable, null, null, resourceId.ToString()));
        }
    }

    private async Task AddOutsideWorkingTimeAsync(List<Guid> drivers, DateTime startUtc, DateTime endUtc, List<TripWarning> warnings)
    {
        if (drivers.Count == 0)
        {
            return;
        }

        var patterns = await _patterns.AsyncExecuter.ToListAsync((await _patterns.GetQueryableAsync())
            .Where(p => p.ResourceKind == ResourceKind.Driver && drivers.Contains(p.ResourceId)));
        if (patterns.Count == 0)
        {
            return;
        }

        var from = new DateTimeOffset(startUtc, TimeSpan.Zero);
        var till = new DateTimeOffset(endUtc, TimeSpan.Zero);
        var zone = _clock.Zone;
        foreach (var driverId in drivers.OrderBy(id => id))
        {
            if (_expander.OutsideWorkingTime(ResourceKind.Driver, driverId, patterns, from, till, zone).Count > 0)
            {
                warnings.Add(new TripWarning(TripWarningCode.OutsideWorkingTime, null, null, driverId.ToString()));
            }
        }
    }
}
