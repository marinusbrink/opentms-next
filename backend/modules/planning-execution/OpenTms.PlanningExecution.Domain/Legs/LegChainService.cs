using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Services;

namespace OpenTms.PlanningExecution.Legs;

/// <summary>One end of a row of Manage legs. A null address means the relation's own (the known end of the line, else its first address).</summary>
public sealed record LegChainEndInput(Guid WaypointTypeId, Guid RelationId, Guid? RelationAddressId, DateOnly PlannedDate, DateTimeOffset? WindowFrom, DateTimeOffset? WindowTill, string? Reference);

/// <summary>One row of Manage legs, in route order: an existing leg (<see cref="Id"/>) or a new one.</summary>
public sealed record LegChainRowInput(Guid? Id, LegChainEndInput Origin, LegChainEndInput Destination, string? Remarks);

/// <summary>What <see cref="LegChainService.Apply"/> did to the line's legs; the caller persists.</summary>
public sealed class LegChainResult
{
    public List<Leg> Created { get; } = new();

    /// <summary>The legs whose ends, dates, windows, references or remarks changed: the planner's from now on.</summary>
    public List<Leg> Changed { get; } = new();

    /// <summary>Only dates, windows, references or remarks changed: the leg keeps its source, so the line keeps following the order, and needs no new route.</summary>
    public List<Leg> Edited { get; } = new();

    /// <summary>The legs that only moved to another position: their source stays.</summary>
    public List<Leg> Repositioned { get; } = new();

    public List<Leg> Removed { get; } = new();

    public bool HasChanges => Created.Count + Changed.Count + Edited.Count + Repositioned.Count + Removed.Count > 0;
}

/// <summary>
/// Design 74 §4, the rules of Manage legs (legacy's client-side rules made correct, on the server): one chain per order line.
/// The first origin and the last destination stay; a leg on a trip or with actuals stays exactly as it is; each origin
/// equals the previous destination; dates do not run backwards; activities and relations are present. New and changed legs
/// become the planner's (<see cref="Leg.MarkShapedByPlanner"/>), so the generator treats the line as reshaped (design 59)
/// and keeps them. An end that keeps its place keeps its stop id, so it goes on following the order's stop; a place that is
/// new to the line gets a stop that exists only in Planning (<see cref="Leg.SyntheticStopId"/>). Pure: the application
/// service loads, persists, publishes and requests the routes.
/// </summary>
public class LegChainService : DomainService
{
    /// <summary>Whether the legs, in position order, form one route: a line with a split part of a leg runs in parallel and does not.</summary>
    public static bool IsChain(IReadOnlyList<Leg> legs)
    {
        for (var i = 1; i < legs.Count; i++)
        {
            if (!SamePlace(legs[i - 1].Destination, legs[i].Origin))
            {
                return false;
            }
        }

        return true;
    }

    /// <param name="current">The line's live legs in position order (at least one).</param>
    /// <param name="rows">The wanted legs in route order.</param>
    /// <param name="readOnlyLegIds">The legs on a trip or with actuals.</param>
    /// <param name="placeOf">An address of a relation from Master Data: the given one when it belongs to the relation, else its first; null when there is none.</param>
    public LegChainResult Apply(IReadOnlyList<Leg> current, IReadOnlyList<LegChainRowInput> rows, IReadOnlySet<Guid> readOnlyLegIds, Func<Guid, Guid?, DepotLocation?> placeOf, Func<Guid> newId)
    {
        if (!IsChain(current))
        {
            throw new BusinessException(PlanningExecutionErrorCodes.LegLineNotAChain).WithData("lineId", current[0].OrderLineId);
        }

        if (rows.Count == 0)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.LegChainEmpty);
        }

        if (rows.Count > PlanningExecutionConsts.MaxLegsPerCommand)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.TooManyLegs).WithData("max", PlanningExecutionConsts.MaxLegsPerCommand);
        }

        var knownEnds = current.SelectMany(l => new[] { new KnownEnd(l.Origin, l.OriginSourceKey), new KnownEnd(l.Destination, l.DestinationSourceKey) }).ToList();
        var existing = rows.Select(r => r.Id == null ? null : current.First(l => l.Id == r.Id)).ToList();
        var origins = new ResolvedEnd[rows.Count];
        var destinations = new ResolvedEnd[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            origins[i] = Resolve(rows[i].Origin, i + 1, "origin", knownEnds, placeOf);
            destinations[i] = Resolve(rows[i].Destination, i + 1, "destination", knownEnds, placeOf);
        }

        EnsureEndsFixed(current, origins[0], destinations[^1]);
        EnsureContiguousAndForward(origins, destinations);
        EnsureReadOnlyLegsUntouched(current, rows, existing, origins, destinations, readOnlyLegIds);

        var stops = StopsOfTheChain(current, existing, origins, destinations, knownEnds);
        EnsureNoRepeatedLeg(stops);

        var result = new LegChainResult();
        foreach (var leg in current.Where(l => rows.All(r => r.Id != l.Id)))
        {
            result.Removed.Add(leg);
        }

        var quantities = current[0].Quantities;
        for (var i = 0; i < rows.Count; i++)
        {
            var (from, till, remarks) = (origins[i], destinations[i], rows[i].Remarks);
            var leg = existing[i];
            if (leg == null)
            {
                leg = new Leg(newId(), current[0].TenantId, current[0].OrderId, current[0].OrderLineId, current[0].CustomerRelationId, i + 1, LegSource.Split,
                    Snapshot(from, stops[i].StopId, null), Snapshot(till, stops[i + 1].StopId, null), quantities);
                leg.InheritSourceKeys(stops[i].SourceKey, stops[i + 1].SourceKey);
                ApplyEditable(leg, from, till, remarks);
                result.Created.Add(leg);
                continue;
            }

            /* The dialog keeps windows at the minute: a window equal at the minute is unchanged, and keeps its seconds. */
            from = KeepSeconds(from, leg.Origin);
            till = KeepSeconds(till, leg.Destination);
            var reshaped = false;
            if (leg.OriginStopId != stops[i].StopId || leg.OriginWaypointTypeId != from.Input.WaypointTypeId || !SamePlace(leg.Origin, from))
            {
                leg.SetOrigin(Snapshot(from, stops[i].StopId, leg.OriginPlannedDurationMin));
                leg.InheritSourceKeys(stops[i].SourceKey, null);
                reshaped = true;
            }

            if (leg.DestinationStopId != stops[i + 1].StopId || leg.DestinationWaypointTypeId != till.Input.WaypointTypeId || !SamePlace(leg.Destination, till))
            {
                leg.SetDestination(Snapshot(till, stops[i + 1].StopId, leg.DestinationPlannedDurationMin));
                leg.InheritSourceKeys(null, stops[i + 1].SourceKey);
                reshaped = true;
            }

            /* Dates, windows and texts leave the source keys alone: they are the planner's, not the order's. They do not
             * reshape the line either: only another place or activity does, so the line keeps following the order's quantities. */
            var before = (leg.Origin, leg.Destination, leg.Remarks);
            ApplyEditable(leg, from, till, remarks);
            var moved = leg.Position != i + 1;
            leg.SetPosition(i + 1);
            if (reshaped)
            {
                leg.MarkShapedByPlanner();
                result.Changed.Add(leg);
            }
            else if ((leg.Origin, leg.Destination, leg.Remarks) != before)
            {
                result.Edited.Add(leg);
            }
            else if (moved)
            {
                result.Repositioned.Add(leg);
            }
        }

        return result;
    }

    private sealed record KnownEnd(LegEndSnapshot End, Guid? SourceKey);

    private sealed record ChainStop(Guid StopId, Guid? SourceKey);

    /// <summary>An end of a row with its place resolved: from an end the line knows (no lookup), else from Master Data.</summary>
    private sealed record ResolvedEnd(LegChainEndInput Input, string Name, string? City, string? CountryCode, double? Latitude, double? Longitude, string? TimeZoneId);

    private static ResolvedEnd Resolve(LegChainEndInput input, int position, string side, List<KnownEnd> knownEnds, Func<Guid, Guid?, DepotLocation?> placeOf)
    {
        if (input.WaypointTypeId == Guid.Empty)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.LegChainIncomplete).WithData("position", position).WithData("field", side + "WaypointTypeId");
        }

        if (input.RelationId == Guid.Empty)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.LegChainIncomplete).WithData("position", position).WithData("field", side + "RelationId");
        }

        var known = knownEnds.FirstOrDefault(k => k.End.RelationId == input.RelationId && (input.RelationAddressId == null || k.End.RelationAddressId == input.RelationAddressId));
        if (known != null)
        {
            var e = known.End;
            return new ResolvedEnd(input with { RelationAddressId = e.RelationAddressId }, e.Name, e.City, e.CountryCode, e.Latitude, e.Longitude, e.TimeZoneId);
        }

        var place = placeOf(input.RelationId, input.RelationAddressId)
                    ?? throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", side + "RelationAddressId").WithData("value", input.RelationAddressId?.ToString() ?? input.RelationId.ToString());
        return new ResolvedEnd(input with { RelationAddressId = place.AddressId }, place.Name, place.City, place.CountryCode, place.Latitude, place.Longitude, place.TimeZoneId);
    }

    private static void EnsureEndsFixed(IReadOnlyList<Leg> current, ResolvedEnd firstOrigin, ResolvedEnd lastDestination)
    {
        var origin = current[0].Origin;
        if (!SamePlace(origin, firstOrigin) || origin.WaypointTypeId != firstOrigin.Input.WaypointTypeId)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.LegChainEndsFixed).WithData("field", "origin");
        }

        var destination = current[^1].Destination;
        if (!SamePlace(destination, lastDestination) || destination.WaypointTypeId != lastDestination.Input.WaypointTypeId)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.LegChainEndsFixed).WithData("field", "destination");
        }
    }

    private static void EnsureContiguousAndForward(ResolvedEnd[] origins, ResolvedEnd[] destinations)
    {
        for (var i = 0; i < origins.Length; i++)
        {
            if (destinations[i].Input.PlannedDate < origins[i].Input.PlannedDate)
            {
                throw new BusinessException(PlanningExecutionErrorCodes.LegChainDatesBackwards).WithData("position", i + 1);
            }

            if (i == 0)
            {
                continue;
            }

            var previous = destinations[i - 1].Input;
            var origin = origins[i].Input;
            if (previous.RelationId != origin.RelationId || previous.RelationAddressId != origin.RelationAddressId)
            {
                throw new BusinessException(PlanningExecutionErrorCodes.LegChainNotContiguous).WithData("position", i + 1);
            }

            if (origin.PlannedDate < previous.PlannedDate)
            {
                throw new BusinessException(PlanningExecutionErrorCodes.LegChainDatesBackwards).WithData("position", i + 1);
            }
        }
    }

    /// <summary>A leg on a trip or with actuals is read-only: gone or changed in any field is refused, so add and remove around it are too when they would change its ends.</summary>
    private static void EnsureReadOnlyLegsUntouched(IReadOnlyList<Leg> current, IReadOnlyList<LegChainRowInput> rows, IReadOnlyList<Leg?> existing, ResolvedEnd[] origins, ResolvedEnd[] destinations, IReadOnlySet<Guid> readOnlyLegIds)
    {
        foreach (var leg in current.Where(l => readOnlyLegIds.Contains(l.Id)))
        {
            var index = rows.ToList().FindIndex(r => r.Id == leg.Id);
            if (index < 0 || !Same(leg.Origin, origins[index]) || !Same(leg.Destination, destinations[index]) || Text(rows[index].Remarks) != leg.Remarks)
            {
                throw new BusinessException(PlanningExecutionErrorCodes.LegChainReadOnlyLeg).WithData("legId", leg.Id).WithData("position", leg.Position);
            }
        }
    }

    /// <summary>
    /// The stop of every point of the chain (n + 1 for n rows): the outer two are the line's own; a meeting point keeps the
    /// stop of the leg that already ends or starts there, else of any end the line has at that place (an order's stop keeps
    /// being followed), else it is a new Planning-only stop, derived from the line, the address and the point.
    /// </summary>
    private static ChainStop[] StopsOfTheChain(IReadOnlyList<Leg> current, IReadOnlyList<Leg?> existing, ResolvedEnd[] origins, ResolvedEnd[] destinations, List<KnownEnd> knownEnds)
    {
        var stops = new ChainStop[origins.Length + 1];
        stops[0] = new ChainStop(current[0].OriginStopId, current[0].OriginSourceKey);
        stops[^1] = new ChainStop(current[^1].DestinationStopId, current[^1].DestinationSourceKey);
        for (var point = 1; point < origins.Length; point++)
        {
            var place = destinations[point - 1];
            var before = existing[point - 1];
            var after = existing[point];
            if (before != null && SamePlace(before.Destination, place))
            {
                stops[point] = new ChainStop(before.DestinationStopId, before.DestinationSourceKey);
            }
            else if (after != null && SamePlace(after.Origin, place))
            {
                stops[point] = new ChainStop(after.OriginStopId, after.OriginSourceKey);
            }
            else if (knownEnds.FirstOrDefault(k => SamePlace(k.End, place)) is { } known)
            {
                stops[point] = new ChainStop(known.End.StopId, known.SourceKey);
            }
            else
            {
                stops[point] = new ChainStop(Leg.SyntheticStopId("chain", current[0].OrderLineId, place.Input.RelationAddressId ?? place.Input.RelationId, new Guid(point, 0, 0, new byte[8])), null);
            }
        }

        return stops;
    }

    /// <summary>The unique key of a leg is (line, origin stop, destination stop): a route over the same two stops twice has no second key.</summary>
    private static void EnsureNoRepeatedLeg(ChainStop[] stops)
    {
        var seen = new HashSet<(Guid, Guid)>();
        for (var i = 0; i + 1 < stops.Length; i++)
        {
            if (!seen.Add((stops[i].StopId, stops[i + 1].StopId)))
            {
                throw new BusinessException(PlanningExecutionErrorCodes.LegChainDuplicateLeg).WithData("position", i + 1);
            }
        }
    }

    private static void ApplyEditable(Leg leg, ResolvedEnd origin, ResolvedEnd destination, string? remarks)
        => leg.SetEditable(origin.Input.Reference, destination.Input.Reference, remarks, origin.Input.PlannedDate, destination.Input.PlannedDate,
            new LegWindow(origin.Input.WindowFrom, origin.Input.WindowTill), new LegWindow(destination.Input.WindowFrom, destination.Input.WindowTill));

    private static LegEndSnapshot Snapshot(ResolvedEnd end, Guid stopId, int? plannedDurationMin)
        => new(stopId, end.Input.WaypointTypeId, end.Input.RelationId, end.Input.RelationAddressId, end.Name, end.City, end.CountryCode, end.Latitude, end.Longitude, end.TimeZoneId,
            end.Input.PlannedDate, end.Input.WindowFrom, end.Input.WindowTill, end.Input.Reference, plannedDurationMin);

    private static bool SamePlace(LegEndSnapshot a, LegEndSnapshot b) => a.RelationId == b.RelationId && a.RelationAddressId == b.RelationAddressId;

    private static bool SamePlace(LegEndSnapshot end, ResolvedEnd place) => end.RelationId == place.Input.RelationId && end.RelationAddressId == place.Input.RelationAddressId;

    private static bool Same(LegEndSnapshot end, ResolvedEnd row)
        => SamePlace(end, row) && end.WaypointTypeId == row.Input.WaypointTypeId && end.PlannedDate == row.Input.PlannedDate
           && SameMinute(end.WindowFrom, row.Input.WindowFrom) && SameMinute(end.WindowTill, row.Input.WindowTill) && end.Reference == Text(row.Input.Reference);

    private static bool SameMinute(DateTimeOffset? a, DateTimeOffset? b)
        => a == null || b == null ? a == b : a.Value.UtcTicks / TimeSpan.TicksPerMinute == b.Value.UtcTicks / TimeSpan.TicksPerMinute;

    private static ResolvedEnd KeepSeconds(ResolvedEnd row, LegEndSnapshot end) => row with
    {
        Input = row.Input with
        {
            WindowFrom = SameMinute(end.WindowFrom, row.Input.WindowFrom) ? end.WindowFrom : row.Input.WindowFrom,
            WindowTill = SameMinute(end.WindowTill, row.Input.WindowTill) ? end.WindowTill : row.Input.WindowTill
        }
    };

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
