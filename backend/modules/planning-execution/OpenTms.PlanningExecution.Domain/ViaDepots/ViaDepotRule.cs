using System;
using System.Linq;
using OpenTms.PlanningExecution.Legs;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;

namespace OpenTms.PlanningExecution.ViaDepots;

/// <summary>
/// A rule that routes a stop pair through a depot (design 59 "ViaDepotRule"; OpenTMS's relation and region default via
/// waypoints, made explicit): a stop whose relation (or, for the Customer scope, the order's customer) or geographical
/// region is the rule's, and whose waypoint type is the rule's <see cref="StopWaypointTypeId"/>, sends the generated leg
/// through the depot (design 61 "Via-depot rules on the stop's waypoint type"). The depot's address is a Master Data id
/// (no foreign key); the waypoint types are Orders ids. Rules of one scope apply in <see cref="Position"/> order; per stop
/// the first match wins.
/// </summary>
public class ViaDepotRule : ReferenceDataAggregateRoot
{
    public ViaDepotScope Scope { get; private set; }

    public Guid? RelationId { get; private set; }

    public Guid? GeographicalRegionId { get; private set; }

    /// <summary>
    /// The waypoint type of the order's stop that triggers the rule (e.g. unload): OpenTMS's <c>WaypointTypeId</c>. Required
    /// for every rule saved through the API; null only on a rule stored before the field existed, which then applies to a
    /// stop of any type until it is edited.
    /// </summary>
    public Guid? StopWaypointTypeId { get; private set; }

    public Guid DepotRelationId { get; private set; }

    public Guid DepotAddressId { get; private set; }

    /// <summary>The activity at the depot on arrival (e.g. unload at depot).</summary>
    public Guid ArrivalWaypointTypeId { get; private set; }

    /// <summary>The activity at the depot on departure; null = the arrival type's successor (Orders) or, until Orders publishes it, the arrival type itself.</summary>
    public Guid? DepartureWaypointTypeId { get; private set; }

    /// <summary>The onward leg departs the day after arrival at the depot.</summary>
    public bool Overnight { get; private set; }

    public int Position { get; private set; } = 1;

    public bool IsArchived { get; private set; }

    protected ViaDepotRule()
    {
    }

    public ViaDepotRule(Guid id, Guid? tenantId, ViaDepotScope scope, Guid? relationId, Guid? geographicalRegionId, Guid depotRelationId, Guid depotAddressId, Guid arrivalWaypointTypeId)
        : base(id, tenantId)
    {
        SetScope(scope, relationId, geographicalRegionId);
        SetDepot(depotRelationId, depotAddressId);
        SetWaypointTypes(arrivalWaypointTypeId, null);
    }

    public void SetScope(ViaDepotScope scope, Guid? relationId, Guid? geographicalRegionId)
    {
        if (!Enum.IsDefined(scope))
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "scope").WithData("value", (int)scope);
        }

        var valid = scope switch
        {
            ViaDepotScope.Relation => relationId != null && geographicalRegionId == null,
            ViaDepotScope.Customer => relationId != null && geographicalRegionId == null,
            ViaDepotScope.Region => relationId == null && geographicalRegionId != null,
            _ => false
        };
        if (!valid)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.InvalidViaDepotScope).WithData("scope", scope.ToString());
        }

        Scope = scope;
        RelationId = relationId;
        GeographicalRegionId = geographicalRegionId;
    }

    public void SetDepot(Guid depotRelationId, Guid depotAddressId)
    {
        if (depotRelationId == Guid.Empty || depotAddressId == Guid.Empty)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "depot");
        }

        DepotRelationId = depotRelationId;
        DepotAddressId = depotAddressId;
    }

    public void SetWaypointTypes(Guid arrivalWaypointTypeId, Guid? departureWaypointTypeId)
    {
        if (arrivalWaypointTypeId == Guid.Empty)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "arrivalWaypointTypeId");
        }

        ArrivalWaypointTypeId = arrivalWaypointTypeId;
        DepartureWaypointTypeId = departureWaypointTypeId;
    }

    public void SetStopWaypointType(Guid stopWaypointTypeId)
    {
        if (stopWaypointTypeId == Guid.Empty)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "stopWaypointTypeId");
        }

        StopWaypointTypeId = stopWaypointTypeId;
    }

    public void SetOvernight(bool overnight) => Overnight = overnight;

    public void SetPosition(int position) => Position = position >= 1 ? position : throw new BusinessException(PlanningExecutionErrorCodes.InvalidPosition).WithData("value", position);

    public void SetArchived(bool archived) => IsArchived = archived;

    /// <summary>
    /// True when the rule applies to one stop of an order (design 59 "Generation", design 61 "Via-depot rules on the stop's
    /// waypoint type"): the stop's waypoint type is the rule's (any type for a rule stored without one) and the stop's
    /// relation (Relation), the order's customer (Customer) or one of the stop's regions (Region) is the rule's.
    /// </summary>
    public bool MatchesStop(Guid customerRelationId, Guid stopRelationId, Guid stopWaypointTypeId, System.Collections.Generic.IReadOnlyCollection<Guid> stopRegionIds)
        => (StopWaypointTypeId == null || StopWaypointTypeId == stopWaypointTypeId)
           && Scope switch
           {
               ViaDepotScope.Relation => RelationId == stopRelationId,
               ViaDepotScope.Customer => RelationId == customerRelationId,
               ViaDepotScope.Region => GeographicalRegionId != null && stopRegionIds.Contains(GeographicalRegionId.Value),
               _ => false
           };
}
