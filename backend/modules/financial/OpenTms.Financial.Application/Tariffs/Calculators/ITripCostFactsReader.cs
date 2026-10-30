using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.PlanningExecution.Published;
using OpenTms.PlanningExecution.TransportTrips;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Financial.Tariffs.Calculators;

/// <summary>
/// The facts of one trip the cost calculator prices: its date, its carrier, whether it runs with the own fleet (design 59's
/// <c>TripExecution.OwnFleet</c>), its vehicles' summed cost price per kilometre (QP10, null when none has one) and its legs.
/// </summary>
public sealed record TripCostFacts(Guid TripId, DateOnly TripDate, Guid? CarrierRelationId, bool IsOwnFleet, decimal? OwnFleetCostPerKm, IReadOnlyList<LegSnapshotDto> Legs);

/// <summary>
/// Financial's port onto Planning's published surface for the trip cost calculation (design 57 "TripRateLineCalculator").
/// The default reads <see cref="ITransportTripLookup"/> and <see cref="ILegReader"/>; "own fleet" is the trip's execution,
/// and the vehicles' cost price per kilometre comes with the trip reference (QP10: vehicle → vehicle type on Master Data's
/// side, summed over the trip's vehicles by Planning); the tenant setting is the last step of the cascade.
/// </summary>
public interface ITripCostFactsReader
{
    Task<TripCostFacts?> GetAsync(Guid tripId, CancellationToken cancellationToken = default);
}

public class TripCostFactsReader : ITripCostFactsReader, ITransientDependency
{
    private readonly ITransportTripLookup _trips;
    private readonly ILegReader _legs;

    public TripCostFactsReader(ITransportTripLookup trips, ILegReader legs)
    {
        _trips = trips;
        _legs = legs;
    }

    public async Task<TripCostFacts?> GetAsync(Guid tripId, CancellationToken cancellationToken = default)
    {
        var trip = (await _trips.GetAsync([tripId], cancellationToken)).FirstOrDefault(t => t.Id == tripId);
        if (trip == null)
        {
            return null;
        }

        var legs = await _legs.GetByTripAsync(tripId, cancellationToken);
        return new TripCostFacts(tripId, trip.TripDate, trip.CarrierRelationId, trip.Execution == TripExecution.OwnFleet, trip.OwnFleetCostPerKm, legs);
    }
}
