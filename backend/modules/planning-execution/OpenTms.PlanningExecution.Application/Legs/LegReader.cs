using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.PlanningExecution.Published;
using OpenTms.PlanningExecution.TransportTrips;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.PlanningExecution.Legs;

/// <summary>See <see cref="ILegReader"/>: stored values only, tenant-filtered, one query for the legs and one for their trips.</summary>
// Published in-process reader: outbox handlers and derived-value jobs call it without a user, so it is anonymous; the HTTP-facing caller authorizes.
[RemoteService(IsEnabled = false)]
[AllowAnonymous]
public class LegReader : PlanningExecutionAppServiceBase, ILegReader
{
    private readonly IRepository<Leg, Guid> _legs;
    private readonly IRepository<TransportTrip, Guid> _trips;

    public LegReader(IRepository<Leg, Guid> legs, IRepository<TransportTrip, Guid> trips)
    {
        _legs = legs;
        _trips = trips;
    }

    public virtual async Task<IReadOnlyList<LegSnapshotDto>> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await MapAsync(await _legs.GetListAsync(l => l.OrderId == orderId, cancellationToken: cancellationToken), cancellationToken);

    public virtual async Task<IReadOnlyList<LegSnapshotDto>> GetByOrdersAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
    {
        var keys = Bound(orderIds);
        return keys.Count == 0 ? [] : await MapAsync(await _legs.GetListAsync(l => keys.Contains(l.OrderId), cancellationToken: cancellationToken), cancellationToken);
    }

    public virtual async Task<IReadOnlyList<LegSnapshotDto>> GetByTripAsync(Guid tripId, CancellationToken cancellationToken = default)
        => await MapAsync(await _legs.GetListAsync(l => l.TripId == tripId, cancellationToken: cancellationToken), cancellationToken);

    public virtual async Task<IReadOnlyList<LegSnapshotDto>> GetAsync(IReadOnlyCollection<Guid> legIds, CancellationToken cancellationToken = default)
    {
        var keys = Bound(legIds);
        return keys.Count == 0 ? [] : await MapAsync(await _legs.GetListAsync(l => keys.Contains(l.Id), cancellationToken: cancellationToken), cancellationToken);
    }

    private async Task<IReadOnlyList<LegSnapshotDto>> MapAsync(List<Leg> legs, CancellationToken cancellationToken)
    {
        var tripIds = legs.Where(l => l.TripId != null).Select(l => l.TripId!.Value).Distinct().ToList();
        var trips = tripIds.Count == 0
            ? new Dictionary<Guid, TransportTrip>()
            : (await _trips.GetListAsync(t => tripIds.Contains(t.Id), cancellationToken: cancellationToken)).ToDictionary(t => t.Id);
        var stops = new Dictionary<(Guid LegId, StopLegRole Role), TripStop>();
        if (tripIds.Count > 0)
        {
            var tripsWithStops = await AsyncExecuter.ToListAsync((await _trips.WithDetailsAsync()).Where(t => tripIds.Contains(t.Id)), cancellationToken);
            foreach (var stop in tripsWithStops.SelectMany(t => t.Stops))
            {
                foreach (var link in stop.Legs)
                {
                    stops[(link.LegId, link.Role)] = stop;
                }
            }
        }

        return legs
            .OrderBy(l => l.OrderId).ThenBy(l => l.OrderLineId).ThenBy(l => l.Position)
            .Select(l =>
            {
                var trip = l.TripId != null && trips.TryGetValue(l.TripId.Value, out var t) ? t : null;
                return new LegSnapshotDto
                {
                    Id = l.Id,
                    OrderId = l.OrderId,
                    OrderLineId = l.OrderLineId,
                    Position = l.Position,
                    Origin = End(l.Origin, stops.GetValueOrDefault((l.Id, StopLegRole.Origin))?.ActualArrival),
                    Destination = End(l.Destination, stops.GetValueOrDefault((l.Id, StopLegRole.Destination))?.ActualArrival),
                    IsViaLeg = l.IsViaLeg,
                    TripId = l.TripId,
                    TripNumber = trip?.Number,
                    TripStatus = trip?.Status,
                    DistanceM = l.DistanceM,
                    DurationS = l.DurationS,
                    TollDistanceM = l.TollDistanceM,
                    EstimatedCo2Kg = l.EstimatedCo2Kg,
                    AllocatedCostAmount = l.AllocatedCostAmount,
                    AllocatedRevenueAmount = l.AllocatedRevenueAmount,
                    WeightKg = l.WeightKg,
                    Quantity = l.Quantity
                };
            })
            .ToList();
    }

    private static LegEndDto End(LegEndSnapshot e, DateTimeOffset? actualAt) => new()
    {
        StopId = e.StopId,
        WaypointTypeId = e.WaypointTypeId,
        RelationId = e.RelationId,
        RelationAddressId = e.RelationAddressId ?? Guid.Empty,
        Name = e.Name,
        City = e.City,
        CountryCode = e.CountryCode,
        Latitude = e.Latitude,
        Longitude = e.Longitude,
        TimeZoneId = e.TimeZoneId,
        PlannedDate = e.PlannedDate,
        WindowFrom = e.WindowFrom,
        WindowTill = e.WindowTill,
        Reference = e.Reference,
        ActualAt = actualAt
    };

    private static List<Guid> Bound(IReadOnlyCollection<Guid> ids)
    {
        var keys = (ids ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        return keys.Count > ILegReader.MaxKeys
            ? throw new BusinessException(Platform.ReferenceData.ReferenceDataErrorCodes.TooManyRows).WithData("max", ILegReader.MaxKeys)
            : keys;
    }
}
