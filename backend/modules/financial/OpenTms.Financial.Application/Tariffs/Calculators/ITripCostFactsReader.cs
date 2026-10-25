using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.PlanningExecution.Published;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Financial.Tariffs.Calculators;

/// <summary>The facts of one trip the cost calculator prices: its date, its carrier (null = the own fleet), its legs.</summary>
public sealed record TripCostFacts(Guid TripId, DateOnly TripDate, Guid? CarrierRelationId, bool IsOwnFleet, IReadOnlyList<LegSnapshotDto> Legs);

/// <summary>
/// Financial's port onto Planning's published surface for the trip cost calculation (design 57 "TripRateLineCalculator").
/// The default reads <see cref="ITransportTripLookup"/> and <see cref="ILegReader"/>; "own fleet" is a trip without a
/// carrier relation (design 59: null = own company). The vehicles' cost price per kilometre is not on any published
/// contract yet (build note): the own-fleet line prices from the tenant setting until Planning and Master Data publish it.
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
        return new TripCostFacts(tripId, trip.TripDate, trip.CarrierRelationId, trip.CarrierRelationId == null, legs);
    }
}
