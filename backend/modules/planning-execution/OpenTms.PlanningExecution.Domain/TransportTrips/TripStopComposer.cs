using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.PlanningExecution.Legs;
using Volo.Abp.Domain.Services;

namespace OpenTms.PlanningExecution.TransportTrips;

/// <summary>
/// Design 59 "Stop composition" (OpenTMS stop calculators, trips.md 7–15, 23, 28): after any assignment change the trip's
/// stops are re-derived from its legs. <see cref="StopCompositionMode.MergeByLocationAndActivity"/> lets a leg end share a
/// stop with the same address and activity that has no actuals; <see cref="StopCompositionMode.OneStopPairPerLeg"/> gives
/// every leg its own pair. Manual stops stay, emptied stops go, the first/last stop rule sets departure and arrival, and
/// positions are renumbered. New stops are appended (or inserted after <c>afterStopId</c>) in a deterministic order: by
/// the legs' planned window start, then order of assignment. Pure; the caller persists.
/// </summary>
public class TripStopComposer : DomainService
{
    public void Compose(TransportTrip trip, IReadOnlyList<Leg> legsOnTrip, Func<Guid> newId, Guid? afterStopId = null)
    {
        var legs = legsOnTrip
            .Where(l => l.TripId == trip.Id)
            .OrderBy(l => l.Origin.SortInstant)
            .ThenBy(l => l.TripPosition ?? int.MaxValue)
            .ThenBy(l => l.Id)
            .ToList();
        var legIds = legs.Select(l => l.Id).ToHashSet();

        /* 1. Drop references of legs no longer on the trip. */
        foreach (var stop in trip.Stops)
        {
            foreach (var link in stop.Legs.Where(l => !legIds.Contains(l.LegId)).ToList())
            {
                stop.Legs.Remove(link);
            }
        }

        /* 2. Place every leg end. */
        var insertAt = afterStopId == null ? (int?)null : trip.OrderedStops.FirstOrDefault(s => s.Id == afterStopId)?.Position;
        var created = new List<TripStop>();
        foreach (var leg in legs)
        {
            foreach (var role in new[] { StopLegRole.Origin, StopLegRole.Destination })
            {
                if (trip.Stops.Any(s => s.Legs.Any(l => l.LegId == leg.Id && l.Role == role)))
                {
                    continue;
                }

                var end = leg.End(role);
                var target = trip.StopCompositionMode == StopCompositionMode.MergeByLocationAndActivity && !leg.IsCarriedOutSeparately
                    ? trip.Stops
                        .Where(s => !s.IsManual && !s.HasActuals && s.IsSamePlaceAndActivity(end.RelationId, end.RelationAddressId, end.WaypointTypeId))
                        .Where(s => !s.Legs.Any(l => IsCarriedOutSeparately(l.LegId, legs)))
                        .OrderBy(s => s.Position)
                        .FirstOrDefault()
                    : null;

                if (target == null)
                {
                    target = new TripStop(newId(), trip.TenantId, trip.Id, int.MaxValue, end.WaypointTypeId, end.RelationId, end.RelationAddressId, end.Name, end.City,
                        end.Latitude, end.Longitude, end.TimeZoneId, isManual: false);
                    trip.Stops.Add(target);
                    created.Add(target);
                }

                target.AddLeg(leg.Id, role);
                var stay = Math.Max(target.PlannedDurationMin ?? 0, end.PlannedDurationMin ?? 0);
                target.SetPlannedDurationMin(stay == 0 ? target.PlannedDurationMin : stay);
            }
        }

        /* 3. Emptied stops go; manual stops stay. */
        foreach (var stop in trip.Stops.Where(s => !s.IsManual && s.Legs.Count == 0).ToList())
        {
            trip.Stops.Remove(stop);
        }

        /* 4. Positions: existing stops keep their order; new ones follow afterStopId or the end, in creation order. */
        var kept = trip.Stops.Where(s => !created.Contains(s)).OrderBy(s => s.Position).ToList();
        var index = insertAt == null ? kept.Count : Math.Min(kept.Count, kept.Count(s => s.Position <= insertAt.Value));
        kept.InsertRange(index, created.Where(s => trip.Stops.Contains(s)));
        for (var i = 0; i < kept.Count; i++)
        {
            kept[i].SetPosition(i + 1);
        }

        trip.EnsureStopCount();
        trip.ApplyFirstLastStopRule();
    }

    private static bool IsCarriedOutSeparately(Guid legId, List<Leg> legs) => legs.FirstOrDefault(l => l.Id == legId)?.IsCarriedOutSeparately == true;
}
