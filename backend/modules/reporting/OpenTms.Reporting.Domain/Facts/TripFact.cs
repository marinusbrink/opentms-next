using System;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Reporting.Metrics;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Reporting.Facts;

/// <summary>
/// One trip's numbers, prepared for grouping (design 75, AD-23): a derived record owned by Reporting, written only by its
/// calculator from Planning's published reader. Its id is the trip's id. It holds ids and numbers, no names.
/// </summary>
public class TripFact : BasicAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public DateOnly TripDate { get; private set; }

    public DateOnly TripWeek { get; private set; }

    public DateOnly TripMonth { get; private set; }

    public TransportTripStatus Status { get; private set; }

    public TripExecution Execution { get; private set; }

    public Guid? CarrierRelationId { get; private set; }

    /// <summary>The first vehicle by position; a trip counts once.</summary>
    public Guid? PrimaryVehicleId { get; private set; }

    public Guid? OrganizationUnitId { get; private set; }

    public Guid TransportTripTypeId { get; private set; }

    public decimal? CostAmount { get; private set; }

    /// <summary>Stored as 0 while no allocation exists, which is why trip_margin leaves such a trip out.</summary>
    public decimal? RevenueAmount { get; private set; }

    public decimal? Co2Kg { get; private set; }

    public decimal? LoadFactor { get; private set; }

    public int? PlannedDistanceM { get; private set; }

    public int? ActualDistanceM { get; private set; }

    /// <summary>Stops with both a planned and an actual arrival.</summary>
    public int StopsMeasured { get; private set; }

    /// <summary>Of <see cref="StopsMeasured"/>, those on time.</summary>
    public int StopsOnTime { get; private set; }

    public DateTime FactUpdatedAt { get; private set; }

    protected TripFact()
    {
    }

    public TripFact(Guid id, Guid? tenantId)
        : base(id)
    {
        TenantId = tenantId;
    }

    /// <summary>
    /// Writes the source's current values; returns whether anything changed, and only then moves <see cref="FactUpdatedAt"/>,
    /// so a duplicate event changes nothing (constitution rule 3).
    /// </summary>
    public bool Apply(
        DateOnly tripDate,
        TransportTripStatus status,
        TripExecution execution,
        Guid? carrierRelationId,
        Guid? primaryVehicleId,
        Guid? organizationUnitId,
        Guid transportTripTypeId,
        decimal? costAmount,
        decimal? revenueAmount,
        decimal? co2Kg,
        decimal? loadFactor,
        int? plannedDistanceM,
        int? actualDistanceM,
        int stopsMeasured,
        int stopsOnTime,
        DateTime now)
    {
        if (FactUpdatedAt != default
            && TripDate == tripDate
            && Status == status
            && Execution == execution
            && CarrierRelationId == carrierRelationId
            && PrimaryVehicleId == primaryVehicleId
            && OrganizationUnitId == organizationUnitId
            && TransportTripTypeId == transportTripTypeId
            && CostAmount == costAmount
            && RevenueAmount == revenueAmount
            && Co2Kg == co2Kg
            && LoadFactor == loadFactor
            && PlannedDistanceM == plannedDistanceM
            && ActualDistanceM == actualDistanceM
            && StopsMeasured == stopsMeasured
            && StopsOnTime == stopsOnTime)
        {
            return false;
        }

        TripDate = tripDate;
        TripWeek = MetricPeriods.WeekOf(tripDate);
        TripMonth = MetricPeriods.MonthOf(tripDate);
        Status = status;
        Execution = execution;
        CarrierRelationId = carrierRelationId;
        PrimaryVehicleId = primaryVehicleId;
        OrganizationUnitId = organizationUnitId;
        TransportTripTypeId = transportTripTypeId;
        CostAmount = costAmount;
        RevenueAmount = revenueAmount;
        Co2Kg = co2Kg;
        LoadFactor = loadFactor;
        PlannedDistanceM = plannedDistanceM;
        ActualDistanceM = actualDistanceM;
        StopsMeasured = stopsMeasured;
        StopsOnTime = stopsOnTime;
        FactUpdatedAt = now;
        return true;
    }
}
