using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Orders.TestData;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;

namespace OpenTms.PlanningExecution.TestData;

/// <summary>An own-fleet unit a test trip can drive with: a vehicle combination, or one vehicle that is in none.</summary>
public sealed record TestFleetUnit(Guid? CombinationId, Guid? VehicleId, IReadOnlyList<string> SearchParts);

public sealed record TestCarrier(Guid Id, string Name);

/// <summary>The own company's main address: where own-fleet trips depart from and return to.</summary>
public sealed record TestHomeBase(Guid RelationId, Guid AddressId, double Latitude, double Longitude);

public sealed class TestPlanningFleet
{
    public List<TestFleetUnit> Units { get; } = new();

    public List<Guid> DriverIds { get; } = new();

    public List<TestCarrier> Carriers { get; } = new();

    public TestHomeBase? Home { get; set; }
}

/// <summary>
/// The planner's day for "Reset to dataset" (docs/designs/65-tenant-test-datasets.md): takes one day's legs (by origin date),
/// plans a share that fits the date into trips and leaves the rest unplanned on the planboard. Trips group legs on a lane
/// (loading area, then delivery area, along a Z-order curve, broken where the next leg starts or ends too far away or the
/// load would no longer fit a trailer), 2–10 legs each. Every trip is built through the aggregate and the domain services the
/// application uses: <see cref="LegAssignmentService.Assign"/> (which composes the stops through <see cref="TripStopComposer"/>),
/// <see cref="TransportTrip.MoveStop"/> to sequence the stops as a planner would (nearest next stop whose legs' origins were
/// visited), then <see cref="TransportTrip.Start"/>, <see cref="TransportTrip.RecordStopActual"/> and
/// <see cref="TransportTrip.Complete"/> along a simulated drive, so the status fits the clock. Pure: the caller persists.
/// </summary>
public sealed class PlanningTestTripPlanner
{
    public const int MinLegsPerTrip = 2;
    public const int MaxLegsPerTrip = 10;

    private const double MaxLoadingMeters = 13.6;
    private const double MaxWeightKg = 24_000;
    private const double OriginRadiusKm = 120;
    private const double DestinationRadiusKm = 200;
    private const double CharterShare = 0.15;
    private const double RoadFactor = 1.25;
    private const int DefaultStayMinutes = 30;

    private readonly Random _random;
    private readonly DateOnly _today;
    private readonly DateTimeOffset _now;
    private readonly TimeZoneInfo _zone;
    private readonly Guid? _tenantId;
    private readonly Func<Guid> _newId;
    private readonly LegAssignmentService _assignment;
    private readonly TestPlanningFleet _fleet;
    private readonly TransportTripType _ownType;
    private readonly TransportTripType _charterType;
    private readonly Func<TransportTripType, DateOnly, string> _nextNumber;
    private readonly IReadOnlyDictionary<Guid, TestTransportOrder> _orders;
    private readonly List<TestCarrier> _favouriteCarriers;

    public PlanningTestTripPlanner(
        Random random,
        DateOnly today,
        DateTimeOffset now,
        TimeZoneInfo zone,
        Guid? tenantId,
        Func<Guid> newId,
        LegAssignmentService assignment,
        TestPlanningFleet fleet,
        TransportTripType ownType,
        TransportTripType charterType,
        Func<TransportTripType, DateOnly, string> nextNumber,
        IReadOnlyDictionary<Guid, TestTransportOrder> orders)
    {
        _random = random;
        _today = today;
        _now = now;
        _zone = zone;
        _tenantId = tenantId;
        _newId = newId;
        _assignment = assignment;
        _fleet = fleet;
        _ownType = ownType;
        _charterType = charterType;
        _nextNumber = nextNumber;
        _orders = orders;
        /* A forwarder works with a core of regular subcontractors and the occasional other one. */
        _favouriteCarriers = fleet.Carriers.Take(15).ToList();
    }

    public int PlannedLegs { get; private set; }
    public int UnplannedLegs { get; private set; }
    public int PlannedTrips { get; private set; }
    public int InProgressTrips { get; private set; }
    public int CompletedTrips { get; private set; }
    public int CharterTrips { get; private set; }

    /// <summary>Plans the share of <paramref name="legs"/> (all with origin date <paramref name="day"/>) that fits the date; returns the new trips.</summary>
    public List<TransportTrip> PlanDay(DateOnly day, IReadOnlyList<Leg> legs)
    {
        var share = PlannedShare(day.DayNumber - _today.DayNumber);
        var toPlan = new List<(Leg Leg, int Index)>();
        for (var i = 0; i < legs.Count; i++)
        {
            if (_random.NextDouble() < share)
            {
                toPlan.Add((legs[i], i));
            }
        }

        UnplannedLegs += legs.Count - toPlan.Count;
        if (toPlan.Count == 0)
        {
            return [];
        }

        var sorted = toPlan
            .OrderBy(x => CellKey(x.Leg.OriginLatitude, x.Leg.OriginLongitude, 1.0))
            .ThenBy(x => CellKey(x.Leg.DestinationLatitude, x.Leg.DestinationLongitude, 1.0))
            .ThenBy(x => CellKey(x.Leg.OriginLatitude, x.Leg.OriginLongitude, 0.05))
            .ThenBy(x => x.Index)
            .Select(x => x.Leg)
            .ToList();
        var chunks = Chunk(sorted);
        MergeSingles(chunks);

        var units = _fleet.Units.ToArray();
        _random.Shuffle(units);
        var drivers = _fleet.DriverIds.ToArray();
        _random.Shuffle(drivers);
        int nextUnit = 0, nextDriver = 0;

        var trips = new List<TransportTrip>(chunks.Count);
        foreach (var chunk in chunks)
        {
            var fleetLeft = nextUnit < units.Length && nextDriver < drivers.Length;
            var charter = _fleet.Carriers.Count > 0 && (!fleetLeft || _random.NextDouble() < CharterShare);
            TestFleetUnit? unit = null;
            var tripDrivers = new List<Guid>();
            TestCarrier? carrier = null;
            if (charter)
            {
                carrier = _favouriteCarriers.Count > 0 && _random.NextDouble() < 0.75
                    ? _favouriteCarriers[_random.Next(_favouriteCarriers.Count)]
                    : _fleet.Carriers[_random.Next(_fleet.Carriers.Count)];
            }
            else if (fleetLeft)
            {
                unit = units[nextUnit++];
                tripDrivers.Add(drivers[nextDriver++]);
                if (nextDriver < drivers.Length && _random.NextDouble() < 0.06)
                {
                    tripDrivers.Add(drivers[nextDriver++]);
                }
            }

            trips.Add(BuildTrip(day, chunk, carrier, unit, tripDrivers));
            PlannedLegs += chunk.Count;
        }

        return trips;
    }

    /// <summary>All of the past is planned; today mostly; the coming days less and less (the rest waits on the planboard).</summary>
    private static double PlannedShare(int daysAhead) => daysAhead switch
    {
        < 0 => 1.0,
        0 => 0.92,
        1 => 0.70,
        2 => 0.45,
        3 => 0.25,
        <= 6 => 0.10,
        _ => 0.03
    };

    private List<List<Leg>> Chunk(List<Leg> sorted)
    {
        var chunks = new List<List<Leg>>();
        List<Leg>? current = null;
        var target = 0;
        double loadingMeters = 0, weightKg = 0;
        foreach (var leg in sorted)
        {
            if (current != null && (current.Count >= target || !Fits(current, leg, 1.0, loadingMeters, weightKg)))
            {
                current = null;
            }

            if (current == null)
            {
                current = new List<Leg>();
                chunks.Add(current);
                target = _random.Next(MinLegsPerTrip, MaxLegsPerTrip + 1);
                loadingMeters = 0;
                weightKg = 0;
            }

            current.Add(leg);
            loadingMeters += leg.LoadingMeters ?? 0;
            weightKg += leg.WeightKg ?? 0;
        }

        return chunks;
    }

    /// <summary>A leg left alone joins another trip of the day on a nearby lane with room, where there is one.</summary>
    private static void MergeSingles(List<List<Leg>> chunks)
    {
        foreach (var single in chunks.Where(c => c.Count == 1).ToList())
        {
            if (single.Count != 1)
            {
                continue;
            }

            var leg = single[0];
            var host = chunks.FirstOrDefault(c => c != single && c.Count > 0 && c.Count < MaxLegsPerTrip
                && Fits(c, leg, 1.5, c.Sum(l => l.LoadingMeters ?? 0), c.Sum(l => l.WeightKg ?? 0)));
            if (host != null)
            {
                host.Add(leg);
                single.Clear();
            }
        }

        chunks.RemoveAll(c => c.Count == 0);
    }

    private static bool Fits(List<Leg> chunk, Leg leg, double radiusFactor, double loadingMeters, double weightKg)
        => KmOrZero(chunk[0].OriginLatitude, chunk[0].OriginLongitude, leg.OriginLatitude, leg.OriginLongitude) <= OriginRadiusKm * radiusFactor
           && KmOrZero(chunk[0].DestinationLatitude, chunk[0].DestinationLongitude, leg.DestinationLatitude, leg.DestinationLongitude) <= DestinationRadiusKm * radiusFactor
           && loadingMeters + (leg.LoadingMeters ?? 0) <= MaxLoadingMeters
           && weightKg + (leg.WeightKg ?? 0) <= MaxWeightKg;

    private TransportTrip BuildTrip(DateOnly day, List<Leg> legs, TestCarrier? carrier, TestFleetUnit? unit, List<Guid> drivers)
    {
        var type = carrier != null ? _charterType : _ownType;
        var execution = carrier != null ? TripExecution.Subcontracted : TripExecution.OwnFleet;
        var home = carrier == null ? _fleet.Home : null;
        var mode = type.DefaultStopCompositionMode;
        var rule = type.DefaultFirstLastStopRule;

        var trip = new TransportTrip(_newId(), _tenantId, _nextNumber(type, day), type.Id, day, execution, mode, rule, Local(day, new TimeOnly(6, 0)));
        if (carrier != null)
        {
            trip.SetCarrier(carrier.Id, null);
        }

        if (unit != null)
        {
            trip.SetVehicles(unit.CombinationId, unit.VehicleId is { } vehicleId ? [vehicleId] : []);
            trip.SetDrivers(drivers);
        }

        _assignment.Assign(trip, legs, [], allowOutsideWindow: true, afterStopId: null, moving: false, _newId);
        var legsById = legs.ToDictionary(l => l.Id);
        var start = home == null ? ((double, double)?)null : (home.Latitude, home.Longitude);
        Sequence(trip, legsById, start);

        var departure = PlannedDeparture(day, trip, legsById, start);
        trip.SetHeader(null, day, departure, null, home?.RelationId, home?.AddressId, home?.RelationId, home?.AddressId, null, null, null,
            execution, mode, rule, null);
        trip.ApplyFirstLastStopRule();

        Execute(trip, legsById, start, departure.AddMinutes(_random.Next(-5, 21)));
        trip.SetSearchKey(SearchKey(trip, legs, carrier, unit));

        if (carrier != null)
        {
            CharterTrips++;
        }

        return trip;
    }

    /// <summary>
    /// The planner's sequence: from the home base (or the earliest pickup), the nearest stop whose legs' origins were all
    /// visited, so every leg loads before it unloads. Applied with <see cref="TransportTrip.MoveStop"/>.
    /// </summary>
    private static void Sequence(TransportTrip trip, Dictionary<Guid, Leg> legsById, (double Lat, double Lon)? start)
    {
        var stops = trip.OrderedStops.ToList();
        var originStopOf = new Dictionary<Guid, Guid>();
        foreach (var stop in stops)
        {
            foreach (var link in stop.Legs.Where(l => l.Role == StopLegRole.Origin))
            {
                originStopOf[link.LegId] = stop.Id;
            }
        }

        var visited = new HashSet<Guid>();
        var order = new List<TripStop>(stops.Count);
        var position = start;
        while (order.Count < stops.Count)
        {
            var remaining = stops.Where(s => !visited.Contains(s.Id)).ToList();
            var available = remaining
                .Where(s => s.Legs.Where(l => l.Role == StopLegRole.Destination)
                    .All(l => !originStopOf.TryGetValue(l.LegId, out var origin) || origin == s.Id || visited.Contains(origin)))
                .ToList();
            if (available.Count == 0)
            {
                available = remaining;
            }

            var next = position == null
                ? available.OrderBy(s => EarliestWindow(s, legsById) ?? DateTimeOffset.MaxValue).ThenBy(s => s.Position).First()
                : available.OrderBy(s => KmTo(position.Value, s)).ThenBy(s => s.Position).First();
            visited.Add(next.Id);
            order.Add(next);
            if (next.HasCoordinates)
            {
                position = (next.Latitude!.Value, next.Longitude!.Value);
            }
        }

        for (var i = 0; i < order.Count; i++)
        {
            if (order[i].Position != i + 1)
            {
                trip.MoveStop(order[i].Id, i + 1);
            }
        }
    }

    /// <summary>Early enough to make the first stop's window, else a usual morning start (05:00–07:30 in the planner's zone).</summary>
    private DateTimeOffset PlannedDeparture(DateOnly day, TransportTrip trip, Dictionary<Guid, Leg> legsById, (double Lat, double Lon)? start)
    {
        var first = trip.OrderedStops.FirstOrDefault();
        var open = first == null ? null : EarliestWindow(first, legsById);
        var earliest = Local(day, new TimeOnly(3, 0));
        if (first != null && open != null && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(open.Value, _zone).DateTime) == day)
        {
            var drive = start == null ? TimeSpan.Zero : Drive(KmTo(start.Value, first) * RoadFactor);
            var departure = open.Value - drive - TimeSpan.FromMinutes(15 + _random.Next(0, 16));
            return departure < earliest ? earliest : departure;
        }

        return Local(day, new TimeOnly(5, 0)).AddMinutes(_random.Next(0, 151));
    }

    /// <summary>
    /// Drives the trip from <paramref name="actualDeparture"/>: per stop the drive (great circle × road factor, 45 or 70 km/h,
    /// a little traffic), waiting for a window that opens later the same day, and the stay (its planned duration, give or take).
    /// What lies before the clock is recorded as actuals; a trip back home before now is completed.
    /// </summary>
    private void Execute(TransportTrip trip, Dictionary<Guid, Leg> legsById, (double Lat, double Lon)? start, DateTimeOffset actualDeparture)
    {
        if (actualDeparture > _now)
        {
            PlannedTrips++;
            return;
        }

        trip.Start(actualDeparture);
        var t = actualDeparture;
        var position = start;
        double cumulativeKm = 0;
        foreach (var stop in trip.OrderedStops.ToList())
        {
            var km = position == null || !stop.HasCoordinates ? 0 : KmTo(position.Value, stop) * RoadFactor;
            cumulativeKm += km;
            var arrival = t + Drive(km) * (1 + _random.NextDouble() * 0.2);
            var open = EarliestWindow(stop, legsById);
            if (open != null && open > arrival && open - arrival < TimeSpan.FromHours(8))
            {
                arrival = open.Value.AddMinutes(-_random.Next(0, 11));
            }

            var stay = TimeSpan.FromMinutes((stop.PlannedDurationMin ?? DefaultStayMinutes) * (0.8 + _random.NextDouble() * 0.7));
            var departure = arrival + stay;
            if (arrival > _now)
            {
                InProgressTrips++;
                return;
            }

            trip.RecordStopActual(stop.Id, arrival, departure <= _now ? departure : null, (int)Math.Round(cumulativeKm * 1000));
            if (departure > _now)
            {
                InProgressTrips++;
                return;
            }

            t = departure;
            if (stop.HasCoordinates)
            {
                position = (stop.Latitude!.Value, stop.Longitude!.Value);
            }
        }

        if (start != null && position != null)
        {
            var homeKm = Km(position.Value.Lat, position.Value.Lon, start.Value.Lat, start.Value.Lon) * RoadFactor;
            cumulativeKm += homeKm;
            t += Drive(homeKm);
        }

        if (t > _now)
        {
            InProgressTrips++;
            return;
        }

        trip.SetActualDistance((int)Math.Round(cumulativeKm * 1000));
        trip.Complete(t);
        CompletedTrips++;
    }

    /// <summary>The trip row's search text as <c>TripOperations.RefreshSearchKeyAsync</c> builds it: number, name, carrier, vehicles, customers, stop cities.</summary>
    private string SearchKey(TransportTrip trip, List<Leg> legs, TestCarrier? carrier, TestFleetUnit? unit)
    {
        var parts = new List<string?> { trip.Number, trip.Name, carrier?.Name };
        if (unit != null)
        {
            parts.AddRange(unit.SearchParts);
        }

        var orders = legs.Select(l => l.OrderId).Distinct().Select(id => _orders.GetValueOrDefault(id)).Where(o => o != null).ToList();
        parts.AddRange(orders.Select(o => o!.CustomerName));
        parts.AddRange(orders.Select(o => o!.Number));
        parts.AddRange(trip.Stops.Select(s => s.City));
        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct());
    }

    private static DateTimeOffset? EarliestWindow(TripStop stop, Dictionary<Guid, Leg> legsById)
        => stop.Legs
            .Select(l => legsById.TryGetValue(l.LegId, out var leg) ? leg.End(l.Role).WindowFrom : null)
            .Where(w => w != null)
            .DefaultIfEmpty(null)
            .Min();

    private DateTimeOffset Local(DateOnly day, TimeOnly time)
    {
        var local = day.ToDateTime(time);
        return new DateTimeOffset(local, _zone.GetUtcOffset(local));
    }

    private static TimeSpan Drive(double km) => TimeSpan.FromHours(km / (km > 50 ? 70.0 : 45.0));

    private static double KmTo((double Lat, double Lon) from, TripStop stop)
        => stop.HasCoordinates ? Km(from.Lat, from.Lon, stop.Latitude!.Value, stop.Longitude!.Value) : 10_000;

    private static double KmOrZero(double? lat1, double? lon1, double? lat2, double? lon2)
        => lat1 == null || lon1 == null || lat2 == null || lon2 == null ? 0 : Km(lat1.Value, lon1.Value, lat2.Value, lon2.Value);

    /// <summary>Great-circle distance in kilometres.</summary>
    private static double Km(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>A Z-order (Morton) key of the cell a point lies in; cells of <paramref name="degrees"/> latitude by 1.5× that longitude.</summary>
    private static ulong CellKey(double? latitude, double? longitude, double degrees)
    {
        if (latitude == null || longitude == null)
        {
            return ulong.MaxValue;
        }

        var y = (uint)Math.Clamp((latitude.Value + 90) / degrees, 0, 65_535);
        var x = (uint)Math.Clamp((longitude.Value + 180) / (degrees * 1.5), 0, 65_535);
        ulong key = 0;
        for (var bit = 0; bit < 16; bit++)
        {
            key |= ((ulong)((x >> bit) & 1) << (2 * bit)) | ((ulong)((y >> bit) & 1) << (2 * bit + 1));
        }

        return key;
    }
}
