using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.PlanningExecution.Legs;

/// <summary>
/// One snapshot of a stop as a leg's origin or destination (design 59 "Leg": copied from the order's stop and its
/// address at generation, refreshed on regeneration). A value, compared as a whole.
/// </summary>
public sealed record LegEndSnapshot(
    Guid StopId,
    Guid WaypointTypeId,
    Guid RelationId,
    Guid? RelationAddressId,
    string Name,
    string? City,
    string? CountryCode,
    double? Latitude,
    double? Longitude,
    string? TimeZoneId,
    DateOnly PlannedDate,
    DateTimeOffset? WindowFrom,
    DateTimeOffset? WindowTill,
    string? Reference,
    int? PlannedDurationMin)
{
    public bool HasCoordinates => Latitude != null && Longitude != null;

    /// <summary>The instant the planning orders stops by: the window start, else local midnight of the planned date (UTC when the zone is unknown).</summary>
    public DateTimeOffset SortInstant
    {
        get
        {
            if (WindowFrom != null)
            {
                return WindowFrom.Value;
            }

            var midnight = PlannedDate.ToDateTime(TimeOnly.MinValue);
            if (TimeZoneId != null && TimeZoneInfo.TryFindSystemTimeZoneById(TimeZoneId, out var zone))
            {
                return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
            }

            return new DateTimeOffset(midnight, TimeSpan.Zero);
        }
    }
}

/// <summary>The quantities and goods facts a leg carries for allocation, capacity and the compatibility warnings.</summary>
public sealed record LegQuantities(
    decimal Quantity,
    double? WeightKg,
    double? VolumeM3,
    double? LoadingMeters,
    double? Epq,
    double? LoadFactor,
    Guid TransportUnitId,
    Guid? ProductId,
    Guid? TemperatureId,
    IReadOnlyList<Guid> RequiredTransportCharacteristicIds)
{
    public bool Equals(LegQuantities? other)
        => other != null
           && Quantity == other.Quantity && WeightKg.Equals(other.WeightKg) && VolumeM3.Equals(other.VolumeM3)
           && LoadingMeters.Equals(other.LoadingMeters) && Epq.Equals(other.Epq) && LoadFactor.Equals(other.LoadFactor)
           && TransportUnitId == other.TransportUnitId && ProductId == other.ProductId && TemperatureId == other.TemperatureId
           && RequiredTransportCharacteristicIds.OrderBy(id => id).SequenceEqual(other.RequiredTransportCharacteristicIds.OrderBy(id => id));

    public override int GetHashCode() => HashCode.Combine(Quantity, WeightKg, TransportUnitId, ProductId, TemperatureId);

    /// <summary>The quantities scaled by <paramref name="factor"/> (a split): the decimal quantity at four decimals, the measures at five.</summary>
    public LegQuantities Scale(decimal factor)
    {
        var f = (double)factor;
        return this with
        {
            Quantity = Math.Round(Quantity * factor, 4, MidpointRounding.AwayFromZero),
            WeightKg = WeightKg == null ? null : Math.Round(WeightKg.Value * f, 5),
            VolumeM3 = VolumeM3 == null ? null : Math.Round(VolumeM3.Value * f, 5),
            LoadingMeters = LoadingMeters == null ? null : Math.Round(LoadingMeters.Value * f, 5),
            Epq = Epq == null ? null : Math.Round(Epq.Value * f, 5),
            LoadFactor = LoadFactor == null ? null : Math.Round(LoadFactor.Value * f, 5)
        };
    }

    public static LegQuantities Empty(Guid transportUnitId) => new(0, null, null, null, null, null, transportUnitId, null, null, []);
}

/// <summary>
/// One movement of one order line between two stops (design 59 "Leg"; PO decision Q2). Generated from the order's stops
/// by <see cref="LegGenerator"/>, planned on a trip, routed and allocated by the calculators (AD-07, AD-20). The
/// origin and destination snapshots are flat columns so the unique key and the indexes can span them; <see cref="Origin"/>
/// and <see cref="Destination"/> are the value views. Cross-module ids carry no foreign key (design 56 "Ownership").
/// </summary>
public class Leg : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid OrderLineId { get; private set; }

    /// <summary>The order's customer (design 59 build note: the unplanned list filters on it; via-depot customer rules match on it).</summary>
    public Guid CustomerRelationId { get; private set; }

    /// <summary>1..n within the line.</summary>
    public int Position { get; private set; }

    public LegSource Source { get; private set; }

    public bool IsViaLeg { get; private set; }

    public Guid? ViaDepotRuleId { get; private set; }

    /// <summary>The leg this one was split from (trips.md 20).</summary>
    public Guid? SplitFromLegId { get; private set; }

    /* Origin snapshot. */
    public Guid OriginStopId { get; private set; }
    public Guid OriginWaypointTypeId { get; private set; }
    public Guid OriginRelationId { get; private set; }
    public Guid? OriginRelationAddressId { get; private set; }
    public string OriginName { get; private set; } = string.Empty;
    public string? OriginCity { get; private set; }
    public string? OriginCountryCode { get; private set; }
    public double? OriginLatitude { get; private set; }
    public double? OriginLongitude { get; private set; }
    public string? OriginTimeZoneId { get; private set; }
    public DateOnly OriginPlannedDate { get; private set; }
    public DateTimeOffset? OriginWindowFrom { get; private set; }
    public DateTimeOffset? OriginWindowTill { get; private set; }
    public string? OriginReference { get; private set; }
    public int? OriginPlannedDurationMin { get; private set; }

    /* Destination snapshot. */
    public Guid DestinationStopId { get; private set; }
    public Guid DestinationWaypointTypeId { get; private set; }
    public Guid DestinationRelationId { get; private set; }
    public Guid? DestinationRelationAddressId { get; private set; }
    public string DestinationName { get; private set; } = string.Empty;
    public string? DestinationCity { get; private set; }
    public string? DestinationCountryCode { get; private set; }
    public double? DestinationLatitude { get; private set; }
    public double? DestinationLongitude { get; private set; }
    public string? DestinationTimeZoneId { get; private set; }
    public DateOnly DestinationPlannedDate { get; private set; }
    public DateTimeOffset? DestinationWindowFrom { get; private set; }
    public DateTimeOffset? DestinationWindowTill { get; private set; }
    public string? DestinationReference { get; private set; }
    public int? DestinationPlannedDurationMin { get; private set; }

    /* Assignment. */
    public Guid? TripId { get; private set; }

    /// <summary>The order of assignment on the trip (1..n), the second key of the stop composition's order.</summary>
    public int? TripPosition { get; private set; }

    /// <summary>trips.md 23: the leg gets its own stop pair instead of sharing stops.</summary>
    public bool IsCarriedOutSeparately { get; private set; }

    public string? Remarks { get; private set; }

    /* Stored derived values (LegRouteCalculator, TripCalculator). */
    public int? DistanceM { get; private set; }
    public int? DurationS { get; private set; }
    public int? TollDistanceM { get; private set; }
    public string? RouteProvider { get; private set; }
    public DateTimeOffset? RoutedAt { get; private set; }
    public decimal? EstimatedCo2Kg { get; private set; }
    public string? Co2Provider { get; private set; }
    public string? Co2FactorSet { get; private set; }
    public decimal? AllocatedCostAmount { get; private set; }
    public decimal? AllocatedRevenueAmount { get; private set; }
    public long? AllocationVersion { get; private set; }
    public DateTimeOffset? AllocatedAt { get; private set; }

    /* Quantities snapshot. */
    public decimal Quantity { get; private set; }
    public double? WeightKg { get; private set; }
    public double? VolumeM3 { get; private set; }
    public double? LoadingMeters { get; private set; }
    public double? Epq { get; private set; }
    public double? LoadFactor { get; private set; }
    public Guid TransportUnitId { get; private set; }
    public Guid? ProductId { get; private set; }
    public Guid? TemperatureId { get; private set; }
    public List<Guid> RequiredTransportCharacteristicIds { get; private set; } = new();

    protected Leg()
    {
    }

    public Leg(Guid id, Guid? tenantId, Guid orderId, Guid orderLineId, Guid customerRelationId, int position, LegSource source,
        LegEndSnapshot origin, LegEndSnapshot destination, LegQuantities quantities)
        : base(id)
    {
        TenantId = tenantId;
        OrderId = orderId;
        OrderLineId = orderLineId;
        CustomerRelationId = customerRelationId;
        Position = position;
        Source = Enum.IsDefined(source) ? source : throw new ArgumentOutOfRangeException(nameof(source));
        IsViaLeg = source == LegSource.ViaDepot;
        SetOrigin(origin);
        SetDestination(destination);
        SetQuantities(quantities);
    }

    public LegEndSnapshot Origin => new(OriginStopId, OriginWaypointTypeId, OriginRelationId, OriginRelationAddressId, OriginName, OriginCity, OriginCountryCode,
        OriginLatitude, OriginLongitude, OriginTimeZoneId, OriginPlannedDate, OriginWindowFrom, OriginWindowTill, OriginReference, OriginPlannedDurationMin);

    public LegEndSnapshot Destination => new(DestinationStopId, DestinationWaypointTypeId, DestinationRelationId, DestinationRelationAddressId, DestinationName,
        DestinationCity, DestinationCountryCode, DestinationLatitude, DestinationLongitude, DestinationTimeZoneId, DestinationPlannedDate, DestinationWindowFrom,
        DestinationWindowTill, DestinationReference, DestinationPlannedDurationMin);

    public LegQuantities Quantities => new(Quantity, WeightKg, VolumeM3, LoadingMeters, Epq, LoadFactor, TransportUnitId, ProductId, TemperatureId, RequiredTransportCharacteristicIds.ToList());

    public bool IsAssigned => TripId != null;

    /// <summary>The generator owns generated and via legs; the planner's legs (split, return, outsourced) are kept across regenerations.</summary>
    public bool IsGeneratorOwned => Source is LegSource.Generated or LegSource.ViaDepot;

    public bool HasCoordinates => Origin.HasCoordinates && Destination.HasCoordinates;

    /// <summary>The leg's end for a role.</summary>
    public LegEndSnapshot End(StopLegRole role) => role == StopLegRole.Origin ? Origin : Destination;

    public void SetOrigin(LegEndSnapshot end)
    {
        Check.NotNull(end, nameof(end));
        OriginStopId = end.StopId;
        OriginWaypointTypeId = end.WaypointTypeId;
        OriginRelationId = end.RelationId;
        OriginRelationAddressId = end.RelationAddressId;
        OriginName = Text(end.Name, PlanningExecutionConsts.MaxSnapshotNameLength) ?? string.Empty;
        OriginCity = Text(end.City, PlanningExecutionConsts.MaxCityLength);
        OriginCountryCode = Text(end.CountryCode, PlanningExecutionConsts.MaxCountryCodeLength)?.ToUpperInvariant();
        OriginLatitude = end.Latitude;
        OriginLongitude = end.Longitude;
        OriginTimeZoneId = Text(end.TimeZoneId, PlanningExecutionConsts.MaxTimeZoneIdLength);
        OriginPlannedDate = end.PlannedDate;
        OriginWindowFrom = end.WindowFrom;
        OriginWindowTill = end.WindowTill;
        OriginReference = Text(end.Reference, PlanningExecutionConsts.MaxReferenceLength);
        OriginPlannedDurationMin = end.PlannedDurationMin;
    }

    public void SetDestination(LegEndSnapshot end)
    {
        Check.NotNull(end, nameof(end));
        DestinationStopId = end.StopId;
        DestinationWaypointTypeId = end.WaypointTypeId;
        DestinationRelationId = end.RelationId;
        DestinationRelationAddressId = end.RelationAddressId;
        DestinationName = Text(end.Name, PlanningExecutionConsts.MaxSnapshotNameLength) ?? string.Empty;
        DestinationCity = Text(end.City, PlanningExecutionConsts.MaxCityLength);
        DestinationCountryCode = Text(end.CountryCode, PlanningExecutionConsts.MaxCountryCodeLength)?.ToUpperInvariant();
        DestinationLatitude = end.Latitude;
        DestinationLongitude = end.Longitude;
        DestinationTimeZoneId = Text(end.TimeZoneId, PlanningExecutionConsts.MaxTimeZoneIdLength);
        DestinationPlannedDate = end.PlannedDate;
        DestinationWindowFrom = end.WindowFrom;
        DestinationWindowTill = end.WindowTill;
        DestinationReference = Text(end.Reference, PlanningExecutionConsts.MaxReferenceLength);
        DestinationPlannedDurationMin = end.PlannedDurationMin;
    }

    public void SetQuantities(LegQuantities quantities)
    {
        Check.NotNull(quantities, nameof(quantities));
        Quantity = quantities.Quantity;
        WeightKg = quantities.WeightKg;
        VolumeM3 = quantities.VolumeM3;
        LoadingMeters = quantities.LoadingMeters;
        Epq = quantities.Epq;
        LoadFactor = quantities.LoadFactor;
        TransportUnitId = quantities.TransportUnitId;
        ProductId = quantities.ProductId;
        TemperatureId = quantities.TemperatureId;
        RequiredTransportCharacteristicIds = quantities.RequiredTransportCharacteristicIds.Distinct().OrderBy(id => id).ToList();
    }

    public void SetPosition(int position) => Position = position >= 1 ? position : throw new ArgumentOutOfRangeException(nameof(position));

    public void MarkViaDepot(Guid ruleId)
    {
        Source = LegSource.ViaDepot;
        IsViaLeg = true;
        ViaDepotRuleId = ruleId;
    }

    public void MarkSplitFrom(Guid legId)
    {
        Source = LegSource.Split;
        SplitFromLegId = legId;
    }

    public void MarkOutsourced() => Source = LegSource.Outsourced;

    /// <summary>Regeneration: true when the origin or destination snapshot differs (a changed assigned leg leaves its trip).</summary>
    public bool EndsDifferFrom(LegEndSnapshot origin, LegEndSnapshot destination) => Origin != origin || Destination != destination;

    public void AssignTo(Guid tripId, int tripPosition)
    {
        TripId = tripId;
        TripPosition = tripPosition;
    }

    public void Unassign()
    {
        TripId = null;
        TripPosition = null;
        IsCarriedOutSeparately = false;
        AllocatedCostAmount = null;
        AllocatedRevenueAmount = null;
    }

    public void SetTripPosition(int tripPosition) => TripPosition = tripPosition;

    public void SetCarriedOutSeparately(bool value) => IsCarriedOutSeparately = value;

    public void SetRoute(int distanceM, int durationS, int? tollDistanceM, string provider, DateTimeOffset at)
    {
        DistanceM = distanceM;
        DurationS = durationS;
        TollDistanceM = tollDistanceM;
        RouteProvider = Text(provider, PlanningExecutionConsts.MaxProviderLength);
        RoutedAt = at;
    }

    public void ClearRoute()
    {
        DistanceM = null;
        DurationS = null;
        TollDistanceM = null;
        RouteProvider = null;
        RoutedAt = null;
    }

    public void SetCo2(decimal? kg, string? provider, string? factorSet)
    {
        EstimatedCo2Kg = kg == null ? null : Math.Round(kg.Value, 3, MidpointRounding.AwayFromZero);
        Co2Provider = Text(provider, PlanningExecutionConsts.MaxProviderLength);
        Co2FactorSet = Text(factorSet, PlanningExecutionConsts.MaxFactorSetLength);
    }

    public void SetAllocation(decimal? cost, decimal? revenue, long version, DateTimeOffset at)
    {
        AllocatedCostAmount = cost == null ? null : Math.Round(cost.Value, 2, MidpointRounding.AwayFromZero);
        AllocatedRevenueAmount = revenue == null ? null : Math.Round(revenue.Value, 2, MidpointRounding.AwayFromZero);
        AllocationVersion = version;
        AllocatedAt = at;
    }

    /// <summary>The planner's editable fields (OpenTMS's leg grid): references, remarks and planned dates within the stops' windows.</summary>
    public void SetEditable(string? originReference, string? destinationReference, string? remarks, DateOnly? originPlannedDate, DateOnly? destinationPlannedDate)
    {
        OriginReference = Text(originReference, PlanningExecutionConsts.MaxReferenceLength);
        DestinationReference = Text(destinationReference, PlanningExecutionConsts.MaxReferenceLength);
        Remarks = Text(remarks, PlanningExecutionConsts.MaxRemarksLength);
        if (originPlannedDate != null)
        {
            EnsureWithinWindow(originPlannedDate.Value, OriginWindowFrom, OriginWindowTill, OriginTimeZoneId, "originPlannedDate");
            OriginPlannedDate = originPlannedDate.Value;
        }

        if (destinationPlannedDate != null)
        {
            EnsureWithinWindow(destinationPlannedDate.Value, DestinationWindowFrom, DestinationWindowTill, DestinationTimeZoneId, "destinationPlannedDate");
            DestinationPlannedDate = destinationPlannedDate.Value;
        }
    }

    private static void EnsureWithinWindow(DateOnly date, DateTimeOffset? from, DateTimeOffset? till, string? timeZoneId, string field)
    {
        var zone = timeZoneId != null && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var found) ? found : TimeZoneInfo.Utc;
        if (from != null && date < DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from.Value, zone).DateTime))
        {
            throw new BusinessException(PlanningExecutionErrorCodes.LegDateOutsideStopWindow).WithData("field", field).WithData("value", date.ToString("yyyy-MM-dd"));
        }

        if (till != null && date > DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(till.Value, zone).DateTime))
        {
            throw new BusinessException(PlanningExecutionErrorCodes.LegDateOutsideStopWindow).WithData("field", field).WithData("value", date.ToString("yyyy-MM-dd"));
        }
    }

    /// <summary>A leg lies within a trip's window when both planned dates fall in [departure date, arrival date + 1] (OpenTMS warning builder).</summary>
    public bool IsWithinWindow(DateOnly departureDate, DateOnly arrivalDate)
    {
        var last = arrivalDate.AddDays(1);
        return OriginPlannedDate >= departureDate && OriginPlannedDate <= last && DestinationPlannedDate >= departureDate && DestinationPlannedDate <= last;
    }

    /// <summary>
    /// A stable id for a stop that exists only in Planning (a via depot, a split's ends, an outsourcing hub), derived from
    /// its parts so regeneration finds the same leg again (the unique key of design 59).
    /// </summary>
    public static Guid SyntheticStopId(string kind, params Guid[] parts)
    {
        var text = kind + ":" + string.Join(":", parts.Select(p => p.ToString("N")));
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(text));
        return new Guid(hash);
    }

    private static string? Text(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
