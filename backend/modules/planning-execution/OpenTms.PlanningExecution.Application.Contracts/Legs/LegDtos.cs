using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.PlanningExecution.Legs;

/// <summary>A leg as the Transport app's grids and editors see it (design 59 "LegAppService"; design 61).</summary>
public class LegDto
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public string? OrderNumber { get; set; }

    public Guid OrderLineId { get; set; }

    public Guid CustomerRelationId { get; set; }

    public string? CustomerName { get; set; }

    public string? CustomerReference { get; set; }

    public int Position { get; set; }

    public LegSource Source { get; set; }

    public bool IsViaLeg { get; set; }

    public Guid? ViaDepotRuleId { get; set; }

    public Guid? SplitFromLegId { get; set; }

    public LegEndSnapshotDto Origin { get; set; } = new();

    public LegEndSnapshotDto Destination { get; set; } = new();

    public Guid? TripId { get; set; }

    public string? TripNumber { get; set; }

    public TransportTripStatus? TripStatus { get; set; }

    public int? TripPosition { get; set; }

    public bool IsCarriedOutSeparately { get; set; }

    public string? Remarks { get; set; }

    public int? DistanceM { get; set; }

    public int? DurationS { get; set; }

    public int? TollDistanceM { get; set; }

    public string? RouteProvider { get; set; }

    public DateTimeOffset? RoutedAt { get; set; }

    public decimal? EstimatedCo2Kg { get; set; }

    public string? Co2Provider { get; set; }

    public decimal? AllocatedCostAmount { get; set; }

    public decimal? AllocatedRevenueAmount { get; set; }

    public decimal Quantity { get; set; }

    public double? WeightKg { get; set; }

    public double? VolumeM3 { get; set; }

    public double? LoadingMeters { get; set; }

    public double? Epq { get; set; }

    public double? LoadFactor { get; set; }

    public Guid TransportUnitId { get; set; }

    public Guid? ProductId { get; set; }

    public Guid? TemperatureId { get; set; }

    public List<Guid> RequiredTransportCharacteristicIds { get; set; } = new();

    public DateTime CreationTime { get; set; }

    public DateTime? LastModificationTime { get; set; }

    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class LegEndSnapshotDto
{
    public Guid StopId { get; set; }

    public Guid WaypointTypeId { get; set; }

    public Guid RelationId { get; set; }

    public Guid? RelationAddressId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? City { get; set; }

    public string? CountryCode { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? TimeZoneId { get; set; }

    public DateOnly PlannedDate { get; set; }

    public DateTimeOffset? WindowFrom { get; set; }

    public DateTimeOffset? WindowTill { get; set; }

    public string? Reference { get; set; }

    public int? PlannedDurationMin { get; set; }
}

/// <summary>The planner's editable fields (OpenTMS leg grid): references, remarks, planned dates within the stops' windows.</summary>
public class LegUpdateDto
{
    [MaxLength(PlanningExecutionConsts.MaxReferenceLength)]
    public string? OriginReference { get; set; }

    [MaxLength(PlanningExecutionConsts.MaxReferenceLength)]
    public string? DestinationReference { get; set; }

    [MaxLength(PlanningExecutionConsts.MaxRemarksLength)]
    public string? Remarks { get; set; }

    public DateOnly? OriginPlannedDate { get; set; }

    public DateOnly? DestinationPlannedDate { get; set; }

    /// <summary>
    /// The origin's planned time window (design 59 leg snapshot <c>WindowFrom/Till</c>; OpenTMS's leg-grid time slots).
    /// Null leaves it unchanged; a window with both ends null clears it (the whole day). The planned date must lie within it.
    /// </summary>
    public LegTimeWindowDto? OriginWindow { get; set; }

    /// <summary>The destination's planned time window; as <see cref="OriginWindow"/>.</summary>
    public LegTimeWindowDto? DestinationWindow { get; set; }

    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

/// <summary>A planned time window of a leg's end: instants with their offset; <see cref="Till"/> not before <see cref="From"/>.</summary>
public class LegTimeWindowDto
{
    public DateTimeOffset? From { get; set; }

    public DateTimeOffset? Till { get; set; }
}

/// <summary>
/// The leg lists: by order, by trip, or the unplanned legs (no trip) with their filters. Exactly one of the views applies:
/// <see cref="OrderId"/>, <see cref="TripId"/>, else the unplanned list.
/// </summary>
public class LegListRequestDto : ReferenceDataListRequestDto
{
    public Guid? OrderId { get; set; }

    public Guid? TripId { get; set; }

    /// <summary>Origin planned date range (unplanned list).</summary>
    public DateOnly? DateFrom { get; set; }

    public DateOnly? DateTo { get; set; }

    public Guid? CustomerRelationId { get; set; }
}

/// <summary>
/// Design 61 "Order page review" point 5: split a leg A→B at an address X into A→X and X→B. The arrival activity at X is
/// required; the departure activity defaults to the arrival's successive activity (design 51), else the arrival activity;
/// the date of both ends at X defaults to the leg's origin planned date (as a via depot's).
/// </summary>
public class LegSplitAtAddressDto
{
    [Required]
    public Guid RelationId { get; set; }

    [Required]
    public Guid RelationAddressId { get; set; }

    [Required]
    public Guid ArrivalWaypointTypeId { get; set; }

    public Guid? DepartureWaypointTypeId { get; set; }

    public DateOnly? Date { get; set; }
}

public interface ILegAppService : IApplicationService
{
    Task<GridResponse<LegDto>> GetListAsync(LegListRequestDto input);

    Task<LegDto> GetAsync(Guid id);

    Task<LegDto> UpdateAsync(Guid id, LegUpdateDto input);

    /// <summary><c>POST /api/planning/leg/{id}/split-at-address</c>: returns the two new legs, A→X then X→B.</summary>
    Task<List<LegDto>> SplitAtAddressAsync(Guid id, LegSplitAtAddressDto input);

    /// <summary><c>POST /api/planning/leg/{id}/remove-split</c> on either half: returns the restored leg A→B.</summary>
    Task<LegDto> UndoSplitAtAddressAsync(Guid id);
}
