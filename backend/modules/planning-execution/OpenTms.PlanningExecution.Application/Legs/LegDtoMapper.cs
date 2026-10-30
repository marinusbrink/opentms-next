using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Orders.Published;
using OpenTms.PlanningExecution.TransportTrips;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.PlanningExecution.Legs;

/// <summary>Maps legs to their DTO with the order references (Orders lookup, one batch) and trip numbers (one query).</summary>
public class LegDtoMapper : ITransientDependency
{
    private readonly ITransportOrderLookup _orders;
    private readonly IRepository<TransportTrip, Guid> _trips;

    public LegDtoMapper(ITransportOrderLookup orders, IRepository<TransportTrip, Guid> trips)
    {
        _orders = orders;
        _trips = trips;
    }

    public async Task<List<LegDto>> MapAsync(IReadOnlyList<Leg> legs)
    {
        if (legs.Count == 0)
        {
            return [];
        }

        var orders = await OrderReferencesAsync(legs.Select(l => l.OrderId));
        var tripIds = legs.Where(l => l.TripId != null).Select(l => l.TripId!.Value).Distinct().ToList();
        var trips = tripIds.Count == 0
            ? new Dictionary<Guid, (string Number, TransportTripStatus Status)>()
            : (await _trips.GetListAsync(t => tripIds.Contains(t.Id))).ToDictionary(t => t.Id, t => (t.Number, t.Status));

        return legs.Select(l => Map(l, orders.GetValueOrDefault(l.OrderId), trips.TryGetValue(l.TripId ?? Guid.Empty, out var trip) ? trip : null)).ToList();
    }

    public async Task<Dictionary<Guid, TransportOrderReferenceDto>> OrderReferencesAsync(IEnumerable<Guid> orderIds)
    {
        var ids = orderIds.Distinct().ToList();
        var result = new Dictionary<Guid, TransportOrderReferenceDto>();
        foreach (var chunk in ids.Chunk(ITransportOrderLookup.MaxKeys))
        {
            foreach (var reference in await _orders.GetAsync(chunk))
            {
                result[reference.Id] = reference;
            }
        }

        return result;
    }

    public static LegDto Map(Leg l, TransportOrderReferenceDto? order, (string Number, TransportTripStatus Status)? trip) => new()
    {
        Id = l.Id,
        OrderId = l.OrderId,
        OrderNumber = order?.Number,
        OrderLineId = l.OrderLineId,
        CustomerRelationId = l.CustomerRelationId,
        CustomerName = order?.CustomerName,
        CustomerReference = order?.CustomerReference,
        Position = l.Position,
        Source = l.Source,
        IsViaLeg = l.IsViaLeg,
        ViaDepotRuleId = l.ViaDepotRuleId,
        SplitFromLegId = l.SplitFromLegId,
        Origin = MapEnd(l.Origin),
        Destination = MapEnd(l.Destination),
        TripId = l.TripId,
        TripNumber = trip?.Number,
        TripStatus = trip?.Status,
        TripPosition = l.TripPosition,
        IsCarriedOutSeparately = l.IsCarriedOutSeparately,
        Remarks = l.Remarks,
        DistanceM = l.DistanceM,
        DurationS = l.DurationS,
        TollDistanceM = l.TollDistanceM,
        RouteProvider = l.RouteProvider,
        RoutedAt = l.RoutedAt,
        EstimatedCo2Kg = l.EstimatedCo2Kg,
        Co2Provider = l.Co2Provider,
        AllocatedCostAmount = l.AllocatedCostAmount,
        AllocatedRevenueAmount = l.AllocatedRevenueAmount,
        Quantity = l.Quantity,
        WeightKg = l.WeightKg,
        VolumeM3 = l.VolumeM3,
        LoadingMeters = l.LoadingMeters,
        Epq = l.Epq,
        LoadFactor = l.LoadFactor,
        TransportUnitId = l.TransportUnitId,
        ProductId = l.ProductId,
        TemperatureId = l.TemperatureId,
        RequiredTransportCharacteristicIds = l.RequiredTransportCharacteristicIds.ToList(),
        CreationTime = l.CreationTime,
        LastModificationTime = l.LastModificationTime,
        ConcurrencyStamp = l.ConcurrencyStamp
    };

    public static LegEndSnapshotDto MapEnd(LegEndSnapshot e) => new()
    {
        StopId = e.StopId,
        WaypointTypeId = e.WaypointTypeId,
        RelationId = e.RelationId,
        RelationAddressId = e.RelationAddressId,
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
        PlannedDurationMin = e.PlannedDurationMin
    };
}
