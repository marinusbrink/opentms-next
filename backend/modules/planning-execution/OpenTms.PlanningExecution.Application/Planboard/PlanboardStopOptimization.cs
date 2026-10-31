using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.PlanningExecution.TransportTrips;

namespace OpenTms.PlanningExecution.Planboard;

/// <summary>
/// Which stops of a trip the optimiser may reorder (design 68 §5): a stop with actual times stays where it is, and so does
/// every stop before it (a fixed prefix: what was driven is history); a manual stop in the first or last position stays
/// there (the depot the planner put at the start or end, where the first/last stop rule puts the departure and arrival).
/// The free stops lie between the prefix and the suffix, so moving them to their new positions with
/// <see cref="TransportTrip.MoveStop"/> never shifts a fixed stop.
/// </summary>
public sealed class PlanboardStopOptimization
{
    private PlanboardStopOptimization(List<TripStop> prefix, List<TripStop> free, List<TripStop> suffix)
    {
        Prefix = prefix;
        Free = free;
        Suffix = suffix;
    }

    public IReadOnlyList<TripStop> Prefix { get; }

    public IReadOnlyList<TripStop> Free { get; }

    public IReadOnlyList<TripStop> Suffix { get; }

    /// <summary>At least two free stops on a trip that takes changes.</summary>
    public bool CanOptimize(TransportTrip trip) => trip.IsAssignable && Free.Count >= 2;

    public static PlanboardStopOptimization Of(TransportTrip trip)
    {
        var ordered = trip.OrderedStops.ToList();
        var prefixLength = 0;
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].HasActuals)
            {
                prefixLength = i + 1;
            }
        }

        if (prefixLength == 0 && ordered.Count > 0 && ordered[0].IsManual)
        {
            prefixLength = 1;
        }

        var suffixLength = ordered.Count > prefixLength && ordered[^1].IsManual ? 1 : 0;
        return new PlanboardStopOptimization(
            ordered.Take(prefixLength).ToList(),
            ordered.Skip(prefixLength).Take(ordered.Count - prefixLength - suffixLength).ToList(),
            ordered.Skip(ordered.Count - suffixLength).ToList());
    }
}
