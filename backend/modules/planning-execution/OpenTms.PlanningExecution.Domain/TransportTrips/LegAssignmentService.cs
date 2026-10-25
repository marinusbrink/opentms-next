using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.PlanningExecution.Legs;
using Volo.Abp;
using Volo.Abp.Domain.Services;

namespace OpenTms.PlanningExecution.TransportTrips;

/// <summary>
/// Design 59 "Lifecycle and commands": the rules of putting legs on and off trips (trips.md 7–9, 20–24, 28) and the four
/// leg operations. Validates and mutates the aggregates in memory and recomposes the stops; the application service
/// loads, persists, publishes and requests the calculations. The refusals carry the codes of
/// <see cref="PlanningExecutionErrorCodes"/>.
/// </summary>
public class LegAssignmentService : DomainService
{
    private readonly TripStopComposer _composer;

    public LegAssignmentService(TripStopComposer composer)
    {
        _composer = composer;
    }

    /// <param name="trip">The target trip.</param>
    /// <param name="legs">The legs to assign.</param>
    /// <param name="legsOnTrip">Every leg already on the trip (for the composition).</param>
    /// <param name="allowOutsideWindow">Overrides <c>LegOutsideTripWindow</c>.</param>
    /// <param name="afterStopId">New stops are inserted after this stop instead of at the end.</param>
    /// <param name="moving">The legs come from other trips (a move): <c>LegOnOtherTrip</c> is not raised.</param>
    public void Assign(TransportTrip trip, IReadOnlyList<Leg> legs, IReadOnlyList<Leg> legsOnTrip, bool allowOutsideWindow, Guid? afterStopId, bool moving, Func<Guid> newId)
    {
        trip.EnsureAssignable();
        var departure = trip.DepartureDate;
        var arrival = trip.ArrivalDate;
        var position = legsOnTrip.Where(l => l.TripId == trip.Id).Select(l => l.TripPosition ?? 0).DefaultIfEmpty(0).Max();

        foreach (var leg in legs)
        {
            if (leg.TripId == trip.Id)
            {
                continue;
            }

            if (leg.TripId != null && !moving)
            {
                throw new BusinessException(PlanningExecutionErrorCodes.LegOnOtherTrip).WithData("legId", leg.Id).WithData("tripId", leg.TripId.Value);
            }

            if (!allowOutsideWindow && !leg.IsWithinWindow(departure, arrival))
            {
                throw new BusinessException(PlanningExecutionErrorCodes.LegOutsideTripWindow)
                    .WithData("legId", leg.Id)
                    .WithData("from", departure.ToString("yyyy-MM-dd"))
                    .WithData("till", arrival.AddDays(1).ToString("yyyy-MM-dd"));
            }

            leg.AssignTo(trip.Id, ++position);
        }

        var all = legsOnTrip.Where(l => l.TripId == trip.Id).Concat(legs.Where(l => l.TripId == trip.Id)).DistinctBy(l => l.Id).ToList();
        _composer.Compose(trip, all, newId, afterStopId);
    }

    public void Unassign(TransportTrip trip, IReadOnlyList<Leg> legs, IReadOnlyList<Leg> legsOnTrip, Func<Guid> newId)
    {
        foreach (var leg in legs)
        {
            if (leg.TripId != trip.Id)
            {
                throw new BusinessException(PlanningExecutionErrorCodes.LegNotOnTrip).WithData("legId", leg.Id).WithData("number", trip.Number);
            }

            leg.Unassign();
        }

        Recompose(trip, legsOnTrip, newId);
    }

    /// <summary>Recomposes after legs left the trip (a cancel, a regeneration, a move away): remaining legs are renumbered.</summary>
    public void Recompose(TransportTrip trip, IReadOnlyList<Leg> legsOnTrip, Func<Guid> newId)
    {
        var remaining = legsOnTrip.Where(l => l.TripId == trip.Id).OrderBy(l => l.TripPosition ?? int.MaxValue).ThenBy(l => l.Id).ToList();
        for (var i = 0; i < remaining.Count; i++)
        {
            remaining[i].SetTripPosition(i + 1);
        }

        _composer.Compose(trip, remaining, newId);
    }

    /// <summary>trips.md 20–21: part of the quantity moves to a new leg of the same line, on the target trip when given.</summary>
    public Leg SplitLine(Leg leg, decimal quantityToNewLeg, TransportTrip? targetTrip, IReadOnlyList<Leg> legsOnTargetTrip, bool allowOutsideWindow, Func<Guid> newId)
    {
        if (quantityToNewLeg <= 0 || quantityToNewLeg >= leg.Quantity)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.InvalidSplit).WithData("quantity", quantityToNewLeg).WithData("max", leg.Quantity);
        }

        var factor = quantityToNewLeg / leg.Quantity;
        var id = newId();
        var origin = leg.Origin with { StopId = Leg.SyntheticStopId("split", leg.OriginStopId, id) };
        var destination = leg.Destination with { StopId = Leg.SyntheticStopId("split", leg.DestinationStopId, id) };
        var clone = new Leg(id, leg.TenantId, leg.OrderId, leg.OrderLineId, leg.CustomerRelationId, leg.Position, LegSource.Split, origin, destination, leg.Quantities.Scale(factor));
        clone.MarkSplitFrom(leg.Id);
        clone.SetEditable(leg.OriginReference, leg.DestinationReference, leg.Remarks, null, null);

        var remainder = leg.Quantities with
        {
            Quantity = leg.Quantity - clone.Quantity,
            WeightKg = Minus(leg.WeightKg, clone.WeightKg),
            VolumeM3 = Minus(leg.VolumeM3, clone.VolumeM3),
            LoadingMeters = Minus(leg.LoadingMeters, clone.LoadingMeters),
            Epq = Minus(leg.Epq, clone.Epq),
            LoadFactor = Minus(leg.LoadFactor, clone.LoadFactor)
        };
        leg.SetQuantities(remainder);

        if (targetTrip != null)
        {
            Assign(targetTrip, [clone], legsOnTargetTrip, allowOutsideWindow, null, moving: false, newId);
        }

        return clone;
    }

    /// <summary>trips.md 22: a mirrored leg of the same line, on the same trip as the original when it has one.</summary>
    public Leg CreateReturnLeg(Leg leg, TransportTrip? trip, IReadOnlyList<Leg> legsOnTrip, IReadOnlyList<Leg> legsOfLine, Func<Guid> newId)
    {
        if (legsOfLine.Any(l => l.OriginStopId == leg.DestinationStopId && l.DestinationStopId == leg.OriginStopId))
        {
            throw new BusinessException(PlanningExecutionErrorCodes.ReturnLegExists).WithData("legId", leg.Id);
        }

        var origin = leg.Destination with { Reference = leg.Destination.Reference };
        var destination = leg.Origin with { PlannedDate = leg.Destination.PlannedDate < leg.Origin.PlannedDate ? leg.Origin.PlannedDate : leg.Destination.PlannedDate };
        var position = legsOfLine.Select(l => l.Position).DefaultIfEmpty(0).Max() + 1;
        var returnLeg = new Leg(newId(), leg.TenantId, leg.OrderId, leg.OrderLineId, leg.CustomerRelationId, position, LegSource.Return, origin, destination, leg.Quantities);

        if (trip != null && leg.TripId == trip.Id)
        {
            Assign(trip, [returnLeg], legsOnTrip, allowOutsideWindow: true, null, moving: false, newId);
        }

        return returnLeg;
    }

    /// <summary>trips.md 23: the leg leaves its shared stops and gets its own pair at the end.</summary>
    public void CarryOutSeparately(TransportTrip trip, Leg leg, IReadOnlyList<Leg> legsOnTrip, Func<Guid> newId)
    {
        trip.EnsureAssignable();
        if (leg.TripId != trip.Id)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.LegNotOnTrip).WithData("legId", leg.Id).WithData("number", trip.Number);
        }

        leg.SetCarriedOutSeparately(true);
        foreach (var stop in trip.Stops)
        {
            stop.RemoveLeg(leg.Id);
        }

        _composer.Compose(trip, legsOnTrip.Where(l => l.TripId == trip.Id).ToList(), newId);
    }

    /// <summary>
    /// trips.md 24: the origin activity moves to a hub trip. A new leg origin → hub lands on the target trip; the original
    /// leg now starts at the hub (same activity, same date) and stays where it was. Both are the planner's legs from now on.
    /// </summary>
    public Leg OutsourceOriginActivity(Leg leg, DepotLocation hub, TransportTrip targetTrip, IReadOnlyList<Leg> legsOnTargetTrip, TransportTrip? currentTrip, IReadOnlyList<Leg> legsOnCurrentTrip, bool allowOutsideWindow, Func<Guid> newId)
    {
        if (leg.TripId == targetTrip.Id)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.SameTrip).WithData("tripId", targetTrip.Id);
        }

        var hubStopId = Leg.SyntheticStopId("hub", leg.Id, hub.AddressId);
        var hubArrival = new LegEndSnapshot(hubStopId, leg.DestinationWaypointTypeId, hub.RelationId, hub.AddressId, hub.Name, hub.City, hub.CountryCode, hub.Latitude, hub.Longitude,
            hub.TimeZoneId, leg.OriginPlannedDate, null, null, null, null);
        var hubDeparture = hubArrival with { WaypointTypeId = leg.OriginWaypointTypeId };

        var hubLeg = new Leg(newId(), leg.TenantId, leg.OrderId, leg.OrderLineId, leg.CustomerRelationId, leg.Position, LegSource.Outsourced, leg.Origin, hubArrival, leg.Quantities);
        hubLeg.SetEditable(leg.OriginReference, null, leg.Remarks, null, null);

        leg.SetOrigin(hubDeparture);
        leg.MarkOutsourced();

        Assign(targetTrip, [hubLeg], legsOnTargetTrip, allowOutsideWindow, null, moving: false, newId);
        if (currentTrip != null && leg.TripId == currentTrip.Id)
        {
            /* The original leg's origin stop changed: its stop links are rebuilt. */
            foreach (var stop in currentTrip.Stops)
            {
                stop.RemoveLeg(leg.Id, StopLegRole.Origin);
            }

            _composer.Compose(currentTrip, legsOnCurrentTrip.Where(l => l.TripId == currentTrip.Id).ToList(), newId);
        }

        return hubLeg;
    }

    private static double? Minus(double? a, double? b) => a == null ? null : Math.Round(a.Value - (b ?? 0), 5);
}
