using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.PlanningExecution.Published;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.PlanningExecution.TransportTrips;

/// <summary>See <see cref="ITransportTripLookup"/>: one query for the trips with their vehicles and drivers, one Master Data lookup per kind of name; the vehicles' cost price per kilometre summed for Financial (QP10).</summary>
// Published in-process reader: outbox handlers and derived-value jobs call it without a user, so it is anonymous; the HTTP-facing caller authorizes.
[RemoteService(IsEnabled = false)]
[AllowAnonymous]
public class TransportTripLookup : PlanningExecutionAppServiceBase, ITransportTripLookup
{
    private readonly IRepository<TransportTrip, Guid> _trips;
    private readonly MasterDataReferences _masterData;

    public TransportTripLookup(IRepository<TransportTrip, Guid> trips, MasterDataReferences masterData)
    {
        _trips = trips;
        _masterData = masterData;
    }

    public virtual async Task<IReadOnlyList<TransportTripReferenceDto>> GetAsync(IReadOnlyCollection<Guid> tripIds, CancellationToken cancellationToken = default)
    {
        var keys = (tripIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (keys.Count > ITransportTripLookup.MaxKeys)
        {
            throw new BusinessException(Platform.ReferenceData.ReferenceDataErrorCodes.TooManyRows).WithData("max", ITransportTripLookup.MaxKeys);
        }

        if (keys.Count == 0)
        {
            return [];
        }

        var trips = await AsyncExecuter.ToListAsync((await _trips.WithDetailsAsync(t => t.Vehicles, t => t.Drivers)).Where(t => keys.Contains(t.Id)), cancellationToken);
        var carriers = await _masterData.RelationNamesAsync(trips.Select(t => t.CarrierRelationId));
        var combinations = await _masterData.CombinationsAsync(trips.Select(t => t.VehicleCombinationId));
        var vehicleIds = trips.SelectMany(t => t.Vehicles.Select(v => v.VehicleId)).Concat(combinations.Values.SelectMany(c => c.VehicleIds)).Distinct();
        var vehicles = await _masterData.VehiclesAsync(vehicleIds);
        var drivers = await _masterData.DriverNamesAsync(trips.SelectMany(t => t.Drivers.Select(d => d.DriverId)));

        return trips.OrderBy(t => t.Number).Select(t =>
        {
            var tripVehicleIds = t.VehicleCombinationId != null && combinations.TryGetValue(t.VehicleCombinationId.Value, out var combination)
                ? combination.VehicleIds
                : t.Vehicles.OrderBy(v => v.Position).Select(v => v.VehicleId).ToList();
            var tripVehicles = tripVehicleIds.Select(id => vehicles.GetValueOrDefault(id)).Where(v => v != null).ToList();
            var costPrices = tripVehicles.Where(v => v!.CostPricePerKm != null).Select(v => v!.CostPricePerKm!.Value).ToList();
            return new TransportTripReferenceDto
            {
                Id = t.Id,
                Number = t.Number,
                Status = t.Status,
                TripDate = t.TripDate,
                CarrierRelationId = t.CarrierRelationId,
                CarrierName = t.CarrierRelationId != null ? carriers.GetValueOrDefault(t.CarrierRelationId.Value) : null,
                DepartureRelationId = t.DepartureRelationId,
                ArrivalRelationId = t.ArrivalRelationId,
                VehicleNames = tripVehicles.Select(v => v!.Name).ToList(),
                LicencePlates = tripVehicles.Where(v => v!.LicencePlate != null).Select(v => v!.LicencePlate!).ToList(),
                DriverNames = t.Drivers.OrderBy(d => d.Position).Select(d => drivers.GetValueOrDefault(d.DriverId)).Where(n => n != null).Select(n => n!).ToList(),
                PlannedDeparture = t.PlannedDeparture,
                Execution = t.Execution,
                PlannedDistanceM = t.PlannedDistanceM,
                VehicleCombinationId = t.VehicleCombinationId,
                VehicleIds = tripVehicleIds.ToList(),
                /* QP10: the vehicle → vehicle type cascade is Master Data's (the lookup's CostPricePerKm); the sum over the trip's vehicles is the trip's. */
                OwnFleetCostPerKm = costPrices.Count == 0 ? null : costPrices.Sum()
            };
        }).ToList();
    }
}
