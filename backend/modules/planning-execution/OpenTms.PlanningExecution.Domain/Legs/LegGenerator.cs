using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.PlanningExecution.ViaDepots;
using Volo.Abp.Domain.Services;

namespace OpenTms.PlanningExecution.Legs;

/// <summary>The facts of one order the generator needs (mapped from Orders' pricing snapshot by the handler; ids and figures only).</summary>
public sealed class LegGenerationInput
{
    public Guid OrderId { get; init; }

    public Guid CustomerRelationId { get; init; }

    public bool IsCancelled { get; init; }

    public List<LegGenerationLine> Lines { get; init; } = new();
}

public sealed class LegGenerationLine
{
    public Guid LineId { get; init; }

    public LegQuantities Quantities { get; init; } = LegQuantities.Empty(Guid.Empty);

    /// <summary>The stops in position order.</summary>
    public List<LegGenerationStop> Stops { get; init; } = new();
}

public sealed class LegGenerationStop
{
    public LegEndSnapshot Snapshot { get; init; } = default!;

    /// <summary>The geographical regions the stop's address lies in (region-scoped via-depot rules).</summary>
    public List<Guid> RegionIds { get; init; } = new();
}

/// <summary>A depot address as the generator snapshots it (resolved through the Master Data lookup by the handler).</summary>
public sealed record DepotLocation(Guid RelationId, Guid AddressId, string Name, string? City, string? CountryCode, double? Latitude, double? Longitude, string? TimeZoneId);

public sealed class LegGenerationResult
{
    public List<Leg> Created { get; } = new();

    public List<Leg> Updated { get; } = new();

    public List<Leg> Unchanged { get; } = new();

    /// <summary>Legs taken off their trip because their stops changed (design 59: warning <c>LegChangedAfterPlanning</c> on the trip).</summary>
    public List<(Leg Leg, Guid TripId)> Unassigned { get; } = new();

    public List<Leg> Deleted { get; } = new();

    public IEnumerable<Leg> Remaining => Created.Concat(Updated).Concat(Unchanged);
}

/// <summary>
/// Design 59 "Generation" (orders.md §4 made correct): one leg per consecutive stop pair of a line, split at a depot when a
/// via-depot rule matches (relation of either stop → customer → region of either stop; position order within a scope),
/// matched to the existing legs on the unique key (line, origin stop, destination stop). Pure: the handler loads and saves.
/// A line the planner reshaped (a split, return or outsourced leg) keeps its legs; only their stop snapshots are refreshed.
/// </summary>
public class LegGenerator : DomainService
{
    /// <param name="successorOf">The successive waypoint type of a waypoint type (Orders' master data): the onward leg of a via depot departs with the arrival type's successor when the rule names no departure type (design 59), else with the arrival type itself.</param>
    public LegGenerationResult Generate(
        LegGenerationInput input,
        IReadOnlyList<ViaDepotRule> rules,
        Func<Guid, DepotLocation?> depotOf,
        Func<Guid, Guid?> successorOf,
        IReadOnlyList<Leg> existing,
        Guid? tenantId,
        Func<Guid> newId)
    {
        var result = new LegGenerationResult();
        var byLine = existing.GroupBy(l => l.OrderLineId).ToDictionary(g => g.Key, g => g.ToList());

        if (input.IsCancelled)
        {
            foreach (var leg in existing)
            {
                Delete(leg, result);
            }

            return result;
        }

        var orderedRules = rules
            .Where(r => !r.IsArchived)
            .OrderBy(r => r.Scope)
            .ThenBy(r => r.Position)
            .ThenBy(r => r.Id)
            .ToList();

        foreach (var line in input.Lines)
        {
            var current = byLine.TryGetValue(line.LineId, out var legs) ? legs : [];
            byLine.Remove(line.LineId);

            if (current.Any(l => !l.IsGeneratorOwned))
            {
                RefreshReshapedLine(line, current, result);
                continue;
            }

            var wanted = Plan(input, line, orderedRules, depotOf, successorOf);
            Match(input, line, wanted, current, result, tenantId, newId);
        }

        /* Lines the order no longer has. */
        foreach (var leg in byLine.Values.SelectMany(l => l))
        {
            Delete(leg, result);
        }

        return result;
    }

    private sealed record PlannedLeg(LegSource Source, Guid? RuleId, LegEndSnapshot Origin, LegEndSnapshot Destination);

    /// <summary>The legs a line should have: consecutive pairs, each split at its depot when a rule matches.</summary>
    private static List<PlannedLeg> Plan(LegGenerationInput input, LegGenerationLine line, IReadOnlyList<ViaDepotRule> rules, Func<Guid, DepotLocation?> depotOf, Func<Guid, Guid?> successorOf)
    {
        var planned = new List<PlannedLeg>();
        for (var i = 0; i + 1 < line.Stops.Count; i++)
        {
            var a = line.Stops[i];
            var b = line.Stops[i + 1];
            var regions = a.RegionIds.Concat(b.RegionIds).Distinct().ToList();
            var rule = rules.FirstOrDefault(r => r.Matches(input.CustomerRelationId, a.Snapshot.RelationId, b.Snapshot.RelationId, regions));
            var depot = rule == null ? null : depotOf(rule.DepotAddressId);

            if (rule == null || depot == null || IsSamePlace(depot, a.Snapshot) || IsSamePlace(depot, b.Snapshot))
            {
                planned.Add(new PlannedLeg(LegSource.Generated, null, a.Snapshot, b.Snapshot));
                continue;
            }

            var viaStopId = Leg.SyntheticStopId("via", rule.Id, a.Snapshot.StopId, b.Snapshot.StopId);
            var arrival = DepotEnd(depot, viaStopId, rule.ArrivalWaypointTypeId, a.Snapshot.PlannedDate);
            var departureType = rule.DepartureWaypointTypeId ?? successorOf(rule.ArrivalWaypointTypeId) ?? rule.ArrivalWaypointTypeId;
            var departure = DepotEnd(depot, viaStopId, departureType, rule.Overnight ? a.Snapshot.PlannedDate.AddDays(1) : a.Snapshot.PlannedDate);
            planned.Add(new PlannedLeg(LegSource.ViaDepot, rule.Id, a.Snapshot, arrival));
            planned.Add(new PlannedLeg(LegSource.ViaDepot, rule.Id, departure, b.Snapshot));
        }

        return planned;
    }

    private static bool IsSamePlace(DepotLocation depot, LegEndSnapshot stop)
        => stop.RelationId == depot.RelationId && (stop.RelationAddressId == null || stop.RelationAddressId == depot.AddressId);

    private static LegEndSnapshot DepotEnd(DepotLocation depot, Guid stopId, Guid waypointTypeId, DateOnly plannedDate)
        => new(stopId, waypointTypeId, depot.RelationId, depot.AddressId, depot.Name, depot.City, depot.CountryCode, depot.Latitude, depot.Longitude, depot.TimeZoneId,
            plannedDate, null, null, null, null);

    private static void Match(LegGenerationInput input, LegGenerationLine line, List<PlannedLeg> wanted, List<Leg> current, LegGenerationResult result, Guid? tenantId, Func<Guid> newId)
    {
        var remaining = current.ToList();
        for (var position = 1; position <= wanted.Count; position++)
        {
            var plan = wanted[position - 1];
            var match = remaining.FirstOrDefault(l => l.OriginStopId == plan.Origin.StopId && l.DestinationStopId == plan.Destination.StopId);
            if (match == null)
            {
                var leg = new Leg(newId(), tenantId, input.OrderId, line.LineId, input.CustomerRelationId, position, plan.Source, plan.Origin, plan.Destination, line.Quantities);
                if (plan.RuleId != null)
                {
                    leg.MarkViaDepot(plan.RuleId.Value);
                }

                result.Created.Add(leg);
                continue;
            }

            remaining.Remove(match);
            /* Compared with the stops the leg was last generated from: the planner's own edits are not an order change. */
            var originChanged = match.OriginDiffersFrom(plan.Origin);
            var destinationChanged = match.DestinationDiffersFrom(plan.Destination);
            var endsChanged = originChanged || destinationChanged;
            var quantitiesChanged = match.Quantities != line.Quantities;
            var positionChanged = match.Position != position;
            if (!endsChanged && !quantitiesChanged && !positionChanged)
            {
                result.Unchanged.Add(match);
                continue;
            }

            if (endsChanged)
            {
                if (originChanged)
                {
                    match.SetOrigin(plan.Origin);
                }

                if (destinationChanged)
                {
                    match.SetDestination(plan.Destination);
                }

                if (match.TripId is { } tripId)
                {
                    result.Unassigned.Add((match, tripId));
                    match.Unassign();
                }
            }

            match.SetQuantities(line.Quantities);
            match.SetPosition(position);
            result.Updated.Add(match);
        }

        foreach (var leg in remaining)
        {
            Delete(leg, result);
        }
    }

    /// <summary>A reshaped line: the planner's legs stay; ends that still point at an order stop follow the stop's facts.</summary>
    /// <summary>
    /// A return leg (design 59 note 10, trips.md 22) mirrors the original: its ends follow the order stops' places and dates,
    /// but keep their swapped activities, and it never arrives before it departs. The stored source keys stay the order
    /// stops' keys, so the same facts again change nothing.
    /// </summary>
    /// <param name="originSource">The order stop the origin follows, changed or not (the key of a leg stored before source keys).</param>
    private static void RefreshReturnLeg(Leg leg, LegEndSnapshot? origin, LegEndSnapshot? destination, LegEndSnapshot? originSource, LegEndSnapshot? destinationSource)
    {
        var originKey = leg.OriginSourceKey ?? (originSource != null ? Leg.SourceKey(originSource) : null);
        var destinationKey = leg.DestinationSourceKey ?? (destinationSource != null ? Leg.SourceKey(destinationSource) : null);

        if (origin != null)
        {
            leg.SetOrigin(origin with { WaypointTypeId = leg.OriginWaypointTypeId });
            originKey = Leg.SourceKey(origin);
        }

        var target = destination != null ? destination with { WaypointTypeId = leg.DestinationWaypointTypeId } : leg.Destination;
        if (destination != null || target.PlannedDate < leg.OriginPlannedDate)
        {
            leg.SetDestination(target.PlannedDate < leg.OriginPlannedDate ? target with { PlannedDate = leg.OriginPlannedDate } : target);
            if (destination != null)
            {
                destinationKey = Leg.SourceKey(destination);
            }
        }

        leg.InheritSourceKeys(originKey, destinationKey);
    }

    private static void RefreshReshapedLine(LegGenerationLine line, List<Leg> current, LegGenerationResult result)
    {
        var stops = line.Stops.ToDictionary(s => s.Snapshot.StopId, s => s.Snapshot);
        foreach (var leg in current)
        {
            /* Only an end that still points at an order stop follows it; the planner's ends (synthetic stops) stay as they are. */
            var originChanged = stops.TryGetValue(leg.OriginStopId, out var o) && leg.OriginDiffersFrom(o);
            var destinationChanged = stops.TryGetValue(leg.DestinationStopId, out var d) && leg.DestinationDiffersFrom(d);
            if (!originChanged && !destinationChanged)
            {
                result.Unchanged.Add(leg);
                continue;
            }

            if (leg.Source == LegSource.Return)
            {
                RefreshReturnLeg(leg, originChanged ? o : null, destinationChanged ? d : null, o, d);
            }
            else
            {
                if (originChanged)
                {
                    leg.SetOrigin(o!);
                }

                if (destinationChanged)
                {
                    leg.SetDestination(d!);
                }
            }

            if (leg.TripId is { } tripId)
            {
                result.Unassigned.Add((leg, tripId));
                leg.Unassign();
            }

            result.Updated.Add(leg);
        }
    }

    private static void Delete(Leg leg, LegGenerationResult result)
    {
        if (leg.TripId is { } tripId)
        {
            result.Unassigned.Add((leg, tripId));
            leg.Unassign();
        }

        result.Deleted.Add(leg);
    }
}
