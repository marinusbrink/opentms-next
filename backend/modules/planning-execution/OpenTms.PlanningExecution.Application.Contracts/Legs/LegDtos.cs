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

    /// <summary>The order's reference (Orders' <c>CustomerReference</c>), editable through <c>PUT /api/orders/planboard-order/{id}/field</c>.</summary>
    public string? CustomerReference { get; set; }

    /// <summary>Design 74 §6: the order's stamp, for the inline edits of the order and its line; every leg of the order shows the same one.</summary>
    public string? OrderConcurrencyStamp { get; set; }

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

    public string? TransportUnitName { get; set; }

    public Guid? ProductId { get; set; }

    public string? ProductName { get; set; }

    public Guid? TemperatureId { get; set; }

    public string? TemperatureName { get; set; }

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

    /// <summary>
    /// Design 68, assumption 1: the organisation units of the unplanned list. A leg is filtered by its order's organisation
    /// unit snapshot where it carries one; legs carry none yet, so every leg shows under every unit (legacy behaviour).
    /// </summary>
    public List<Guid>? OrganizationUnitIds { get; set; }

    /// <summary>Design 68: only these legs (live-update refetch), at most 200.</summary>
    public List<Guid>? Ids { get; set; }

    /// <summary>
    /// Design 68, PO decision D18: the grouping of the unplanned list, one of <c>origindate</c>, <c>customer</c>,
    /// <c>origincity</c>, <c>destinationcity</c>. <c>GET leg/groups</c> returns the groups with their counts; the list itself
    /// returns the legs of the group <see cref="GroupKey"/> names.
    /// </summary>
    [MaxLength(32)]
    public string? GroupBy { get; set; }

    /// <summary>The key of one group as <c>GET leg/groups</c> returns it (a date yyyy-MM-dd, a customer id, a city; empty = none).</summary>
    [MaxLength(PlanningExecutionConsts.MaxGroupKeyLength)]
    public string? GroupKey { get; set; }
}

/// <summary>One group of the unplanned leg list (design 68, D18): its key, a display label and the number of legs.</summary>
public class LegGroupDto
{
    /// <summary>The value to send back as <see cref="LegListRequestDto.GroupKey"/>; empty for the legs without a value.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The customer's name for the customer grouping; the key otherwise; null for the empty group.</summary>
    public string? Label { get; set; }

    public long Count { get; set; }
}

/// <summary>Design 68 §7: part of an unplanned leg's quantity moves to a new unplanned leg of the same line.</summary>
public class LegSplitLineDto
{
    /// <summary>The quantity the new leg takes (0 &lt; q &lt; the leg's quantity).</summary>
    [Required]
    public decimal Quantity { get; set; }
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

/// <summary>Design 74 §7: <c>POST leg/{id}/split-order-line</c>: the amount the new order line takes (0 &lt; q &lt; the line's quantity); the server decides the rest.</summary>
public class LegSplitOrderLineDto
{
    [Required]
    public decimal Quantity { get; set; }
}

/// <summary>Design 74 §7: <c>GET leg/{id}/line-totals</c>: what the leg's order line carries, the base of the split dialog.</summary>
public class LegLineTotalsDto
{
    public decimal Quantity { get; set; }

    public decimal? Colli { get; set; }

    public double? WeightKg { get; set; }

    public double? VolumeM3 { get; set; }

    public double? LoadingMeters { get; set; }

    public double? LoadFactor { get; set; }

    public double? Epq { get; set; }
}

/// <summary>The outcome of a split of an order line: the new line, its legs and the legs of the old line with their new quantities.</summary>
public class LegSplitOrderLineResultDto
{
    public Guid NewLineId { get; set; }

    public decimal RemainingQuantity { get; set; }

    public decimal NewQuantity { get; set; }

    /// <summary>The legs of the new line, each on its original's trip except the clone of the chosen leg and those of a stuck original (on a completed or cancelled trip, or with actuals on its stops).</summary>
    public List<LegDto> NewLegs { get; set; } = new();

    public List<LegDto> RemainingLegs { get; set; } = new();
}

/// <summary>Design 74 §4: the legs of an order line for Manage legs, in route order.</summary>
public class LegChainDto
{
    public Guid OrderId { get; set; }

    public Guid OrderLineId { get; set; }

    /// <summary>False when legs of the line run in parallel (a split part of a leg): the dialog cannot save such a line.</summary>
    public bool IsChain { get; set; }

    public List<LegChainRowDto> Rows { get; set; } = new();
}

public class LegChainRowDto
{
    public LegDto Leg { get; set; } = new();

    public bool HasActuals { get; set; }

    /// <summary>On a trip (<see cref="LegDto.TripNumber"/> names it) or with actuals: every field of the row stays.</summary>
    public bool IsReadOnly { get; set; }
}

/// <summary>
/// Design 74 §4: <c>PUT leg/line/{lineId}</c>. <see cref="Rows"/> is the wanted chain in route order; a row without an id is a new
/// leg, a leg the rows no longer name is removed. <see cref="Stamps"/> is the line as it was read: a leg that was added,
/// changed or removed meanwhile makes the request stale (409).
/// </summary>
public class LegChainUpdateDto
{
    [Required]
    public List<LegChainRowInputDto> Rows { get; set; } = new();

    [Required]
    public List<LegChainStampDto> Stamps { get; set; } = new();
}

public class LegChainStampDto
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class LegChainRowInputDto
{
    /// <summary>The existing leg; null for a new one.</summary>
    public Guid? Id { get; set; }

    [Required]
    public LegChainEndInputDto Origin { get; set; } = new();

    [Required]
    public LegChainEndInputDto Destination { get; set; } = new();

    [MaxLength(PlanningExecutionConsts.MaxRemarksLength)]
    public string? Remarks { get; set; }
}

public class LegChainEndInputDto
{
    public Guid WaypointTypeId { get; set; }

    public Guid RelationId { get; set; }

    /// <summary>Null: the address the line already knows for the relation, else the relation's first.</summary>
    public Guid? RelationAddressId { get; set; }

    public DateOnly PlannedDate { get; set; }

    public DateTimeOffset? WindowFrom { get; set; }

    public DateTimeOffset? WindowTill { get; set; }

    [MaxLength(PlanningExecutionConsts.MaxReferenceLength)]
    public string? Reference { get; set; }
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

    /// <summary><c>GET /api/planning/leg/groups</c>: the groups of the list (design 68, D18), paged by StartRow/EndRow.</summary>
    Task<GridResponse<LegGroupDto>> GetGroupsAsync(LegListRequestDto input);

    /// <summary><c>POST /api/planning/leg/{id}/split-line</c> on an unplanned leg (design 68 §7): returns the new leg.</summary>
    Task<LegDto> SplitLineAsync(Guid id, LegSplitLineDto input);

    /// <summary><c>POST /api/planning/leg/{id}/return-leg</c> on an unplanned leg (design 68 §7): returns the mirrored leg.</summary>
    Task<LegDto> ReturnLegAsync(Guid id);

    /// <summary><c>GET /api/planning/leg/line/{lineId}</c> (design 74 §4).</summary>
    Task<LegChainDto> GetLineAsync(Guid lineId);

    /// <summary><c>PUT /api/planning/leg/line/{lineId}</c> (design 74 §4): one transaction; returns the line as saved.</summary>
    Task<LegChainDto> UpdateLineAsync(Guid lineId, LegChainUpdateDto input);

    /// <summary><c>POST /api/planning/leg/{id}/split-order-line</c> (design 74 §7).</summary>
    Task<LegSplitOrderLineResultDto> SplitOrderLineAsync(Guid id, LegSplitOrderLineDto input);

    /// <summary><c>GET /api/planning/leg/{id}/line-totals</c> (design 74 §7).</summary>
    Task<LegLineTotalsDto> GetLineTotalsAsync(Guid id);
}
