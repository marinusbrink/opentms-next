using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.PlanningExecution.TransportTrips;

namespace OpenTms.PlanningExecution.Availability;

/// <summary>
/// The time a trip occupies its resources (design 68 §3): planned departure to planned arrival in UTC, as half-open
/// interval. A trip without a later planned arrival (routing off, not calculated yet) occupies one minute from its
/// departure, so two such trips of the same truck at the same moment still count as a double booking.
/// </summary>
public static class TripInterval
{
    public static readonly TimeSpan MinimumLength = TimeSpan.FromMinutes(1);

    public static DateTime End(DateTime departureUtc, DateTime? arrivalUtc)
        => arrivalUtc != null && arrivalUtc.Value > departureUtc ? arrivalUtc.Value : departureUtc + MinimumLength;

    public static bool Overlaps(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd) => aStart < bEnd && bStart < aEnd;
}

/// <summary>
/// What a trip blocks for others: its interval, its vehicles and drivers, and whether it is live (neither cancelled nor
/// archived). Captured when a command loads a trip (the state before) and compared with the state after, so a change of
/// time or resources asks the trips it overlapped before and after to recalculate (design 68 §3).
/// </summary>
public sealed class TripFootprint : IEquatable<TripFootprint>
{
    public TripFootprint(DateTime startUtc, DateTime endUtc, IReadOnlyList<Guid> vehicleIds, IReadOnlyList<Guid> driverIds, bool live)
    {
        StartUtc = startUtc;
        EndUtc = endUtc;
        VehicleIds = vehicleIds;
        DriverIds = driverIds;
        Live = live;
    }

    public DateTime StartUtc { get; }

    public DateTime EndUtc { get; }

    public IReadOnlyList<Guid> VehicleIds { get; }

    public IReadOnlyList<Guid> DriverIds { get; }

    public bool Live { get; }

    public bool HasResources => VehicleIds.Count > 0 || DriverIds.Count > 0;

    public static TripFootprint Of(TransportTrip trip)
        => new(
            trip.PlannedDepartureUtc,
            TripInterval.End(trip.PlannedDepartureUtc, trip.PlannedArrivalUtc),
            trip.Vehicles.Select(v => v.VehicleId).Distinct().OrderBy(id => id).ToList(),
            trip.Drivers.Select(d => d.DriverId).Distinct().OrderBy(id => id).ToList(),
            !trip.IsArchived && trip.Status != TransportTripStatus.Cancelled);

    public bool Equals(TripFootprint? other)
        => other != null && StartUtc == other.StartUtc && EndUtc == other.EndUtc && Live == other.Live
           && VehicleIds.SequenceEqual(other.VehicleIds) && DriverIds.SequenceEqual(other.DriverIds);

    public override bool Equals(object? obj) => obj is TripFootprint other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(StartUtc, EndUtc, Live, VehicleIds.Count, DriverIds.Count);
}
