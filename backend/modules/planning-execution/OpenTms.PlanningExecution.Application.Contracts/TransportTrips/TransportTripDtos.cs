using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.PlanningExecution.Legs;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.PlanningExecution.TransportTrips;

/// <summary>A trip row of the Transport app's trip grids (design 59 "TransportTripAppService" GET list; design 61).</summary>
public class TransportTripRowDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string? Name { get; set; }
    public DateOnly TripDate { get; set; }
    public TransportTripStatus Status { get; set; }
    public bool IsArchived { get; set; }
    public TripExecution Execution { get; set; }
    public Guid TransportTripTypeId { get; set; }
    public string? TransportTripTypeName { get; set; }
    public Guid? CarrierRelationId { get; set; }
    public string? CarrierName { get; set; }
    public Guid? OrganizationUnitId { get; set; }
    public DateTimeOffset PlannedDeparture { get; set; }
    public DateTimeOffset? PlannedArrival { get; set; }
    public DateTimeOffset? ActualDeparture { get; set; }
    public DateTimeOffset? ActualArrival { get; set; }
    public int? PlannedDistanceM { get; set; }
    public int? ActualDistanceM { get; set; }
    public int? PlannedDurationS { get; set; }
    public int StopCount { get; set; }
    public int LegCount { get; set; }
    public List<string> Vehicles { get; set; } = new();
    public List<string> Drivers { get; set; } = new();
    public List<string> Customers { get; set; } = new();
    public List<string> OrderNumbers { get; set; } = new();
    public string? FirstStopCity { get; set; }
    public string? LastStopCity { get; set; }
    public decimal? LoadFactor { get; set; }
    public double? LoadingMeters { get; set; }
    public decimal? Co2Kg { get; set; }
    public decimal? CostAmount { get; set; }
    public decimal? RevenueAmount { get; set; }
    public List<TripWarningDto> Warnings { get; set; } = new();
    public DateTimeOffset? CalculatedAt { get; set; }
}

public class TripWarningDto
{
    public TripWarningCode Code { get; set; }
    public Guid? LegId { get; set; }
    public Guid? StopId { get; set; }
    public string? Data { get; set; }
}

public class TripVehicleDto
{
    public int Position { get; set; }
    public Guid VehicleId { get; set; }
    public string? Name { get; set; }
    public string? LicencePlate { get; set; }
}

public class TripDriverDto
{
    public int Position { get; set; }
    public Guid DriverId { get; set; }
    public string? Name { get; set; }
}

public class TripStopLegDto
{
    public Guid LegId { get; set; }
    public StopLegRole Role { get; set; }
    public Guid OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public Guid CustomerRelationId { get; set; }
    public string? CustomerName { get; set; }
    public Guid OrderLineId { get; set; }
    public int Position { get; set; }
    public decimal Quantity { get; set; }
    public double? WeightKg { get; set; }
    public double? LoadingMeters { get; set; }
    public string? Reference { get; set; }
    public DateOnly PlannedDate { get; set; }
    public DateTimeOffset? WindowFrom { get; set; }
    public DateTimeOffset? WindowTill { get; set; }
    public int? DistanceM { get; set; }
    public decimal? AllocatedCostAmount { get; set; }
    public decimal? AllocatedRevenueAmount { get; set; }
    public bool IsCarriedOutSeparately { get; set; }
}

public class TripStopDto
{
    public Guid Id { get; set; }
    public int Position { get; set; }
    public Guid WaypointTypeId { get; set; }
    public Guid RelationId { get; set; }
    public string? RelationName { get; set; }
    public Guid? RelationAddressId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? City { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? TimeZoneId { get; set; }
    public bool IsManual { get; set; }
    public int? PlannedDurationMin { get; set; }
    public DateTimeOffset? PlannedArrival { get; set; }
    public DateTimeOffset? PlannedDeparture { get; set; }
    public DateTimeOffset? EstimatedArrival { get; set; }
    public DateTimeOffset? EstimatedDeparture { get; set; }
    public DateTimeOffset? ActualArrival { get; set; }
    public DateTimeOffset? ActualDeparture { get; set; }
    public int? PlannedCumulativeDistanceM { get; set; }
    public int? ActualCumulativeDistanceM { get; set; }
    public string? Remarks { get; set; }
    public List<TripStopLegDto> Legs { get; set; } = new();
}

/// <summary>The trip editor's detail (header, vehicles, drivers, stops with their legs, derived values).</summary>
public class TransportTripDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string? Name { get; set; }
    public Guid TransportTripTypeId { get; set; }
    public string? TransportTripTypeName { get; set; }
    public DateOnly TripDate { get; set; }
    public TransportTripStatus Status { get; set; }
    public bool IsArchived { get; set; }
    public TripExecution Execution { get; set; }
    public Guid? CarrierRelationId { get; set; }
    public string? CarrierName { get; set; }
    public Guid? CarrierContactPersonId { get; set; }
    public Guid? DepartureRelationId { get; set; }
    public string? DepartureRelationName { get; set; }
    public Guid? DepartureAddressId { get; set; }
    public Guid? ArrivalRelationId { get; set; }
    public string? ArrivalRelationName { get; set; }
    public Guid? ArrivalAddressId { get; set; }
    public DateTimeOffset PlannedDeparture { get; set; }
    public DateTimeOffset? PlannedArrival { get; set; }
    public DateTimeOffset? ActualDeparture { get; set; }
    public DateTimeOffset? ActualArrival { get; set; }
    public StopCompositionMode StopCompositionMode { get; set; }
    public FirstLastStopRule FirstLastStopRule { get; set; }
    public Guid? VehicleCombinationId { get; set; }
    public string? VehicleCombinationName { get; set; }
    public List<TripVehicleDto> Vehicles { get; set; } = new();
    public List<TripDriverDto> Drivers { get; set; } = new();
    public string? Remarks { get; set; }
    public string? DriverInstructions { get; set; }
    public Guid? OrganizationUnitId { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = new();
    public Guid? CreatedFromOrderId { get; set; }
    public int? PlannedDistanceM { get; set; }
    public int? ActualDistanceM { get; set; }
    public int? PlannedDurationS { get; set; }
    public decimal? LoadFactor { get; set; }
    public double? LoadingMeters { get; set; }
    public decimal? Co2Kg { get; set; }
    public List<TripWarningDto> Warnings { get; set; } = new();
    public decimal? CostAmount { get; set; }
    public decimal? RevenueAmount { get; set; }
    public DateTimeOffset? CalculatedAt { get; set; }
    public List<TripStopDto> Stops { get; set; } = new();
    public DateTime CreationTime { get; set; }
    public Guid? CreatorId { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public Guid? LastModifierId { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

/// <summary>The totals of a trip for the editor's summary tab (GET /{id}/summary).</summary>
public class TransportTripSummaryDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public TransportTripStatus Status { get; set; }
    public int StopCount { get; set; }
    public int LegCount { get; set; }
    public int OrderCount { get; set; }
    public int? PlannedDistanceM { get; set; }
    public int? ActualDistanceM { get; set; }
    public int? PlannedDurationS { get; set; }
    public decimal TotalQuantity { get; set; }
    public double TotalWeightKg { get; set; }
    public double TotalLoadingMeters { get; set; }
    public decimal? LoadFactor { get; set; }
    public decimal? Co2Kg { get; set; }
    public decimal? CostAmount { get; set; }
    public decimal? RevenueAmount { get; set; }
    public decimal? MarginAmount { get; set; }
    public List<TripWarningDto> Warnings { get; set; } = new();
    public DateTimeOffset? CalculatedAt { get; set; }
}

public class TransportTripCreateDto
{
    [Required]
    public Guid TransportTripTypeId { get; set; }

    /// <summary>Defaults to the planned departure's date.</summary>
    public DateOnly? TripDate { get; set; }

    [MaxLength(PlanningExecutionConsts.MaxNameLength)]
    public string? Name { get; set; }

    /// <summary>Defaults to the type's.</summary>
    public TripExecution? Execution { get; set; }

    public StopCompositionMode StopCompositionMode { get; set; } = StopCompositionMode.MergeByLocationAndActivity;

    public FirstLastStopRule FirstLastStopRule { get; set; } = FirstLastStopRule.None;

    public Guid? CarrierRelationId { get; set; }

    public Guid? CarrierContactPersonId { get; set; }

    public Guid? DepartureRelationId { get; set; }

    public Guid? DepartureAddressId { get; set; }

    public Guid? ArrivalRelationId { get; set; }

    public Guid? ArrivalAddressId { get; set; }

    [Required]
    public DateTimeOffset PlannedDeparture { get; set; }

    public DateTimeOffset? PlannedArrival { get; set; }

    public Guid? VehicleCombinationId { get; set; }

    [MaxLength(PlanningExecutionConsts.MaxVehiclesPerTrip)]
    public List<Guid> VehicleIds { get; set; } = new();

    [MaxLength(PlanningExecutionConsts.MaxDriversPerTrip)]
    public List<Guid> DriverIds { get; set; } = new();

    [MaxLength(PlanningExecutionConsts.MaxRemarksLength)]
    public string? Remarks { get; set; }

    [MaxLength(PlanningExecutionConsts.MaxRemarksLength)]
    public string? DriverInstructions { get; set; }

    public Guid? OrganizationUnitId { get; set; }

    public Dictionary<string, string> Attributes { get; set; } = new();
}

/// <summary>PUT /{id}: the header (name, dates, places, remarks, composition, attributes); vehicles, drivers and carrier have their own commands.</summary>
public class TransportTripUpdateDto
{
    [MaxLength(PlanningExecutionConsts.MaxNameLength)]
    public string? Name { get; set; }

    [Required]
    public DateOnly TripDate { get; set; }

    public TripExecution Execution { get; set; }

    public StopCompositionMode StopCompositionMode { get; set; }

    public FirstLastStopRule FirstLastStopRule { get; set; }

    public Guid? DepartureRelationId { get; set; }

    public Guid? DepartureAddressId { get; set; }

    public Guid? ArrivalRelationId { get; set; }

    public Guid? ArrivalAddressId { get; set; }

    [Required]
    public DateTimeOffset PlannedDeparture { get; set; }

    public DateTimeOffset? PlannedArrival { get; set; }

    public int? ActualDistanceM { get; set; }

    [MaxLength(PlanningExecutionConsts.MaxRemarksLength)]
    public string? Remarks { get; set; }

    [MaxLength(PlanningExecutionConsts.MaxRemarksLength)]
    public string? DriverInstructions { get; set; }

    public Guid? OrganizationUnitId { get; set; }

    public Dictionary<string, string> Attributes { get; set; } = new();

    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class TripCarrierDto
{
    public Guid? CarrierRelationId { get; set; }

    public Guid? CarrierContactPersonId { get; set; }
}

public class TripVehiclesDto
{
    public Guid? VehicleCombinationId { get; set; }

    [MaxLength(PlanningExecutionConsts.MaxVehiclesPerTrip)]
    public List<Guid> VehicleIds { get; set; } = new();
}

public class TripDriversDto
{
    [MaxLength(PlanningExecutionConsts.MaxDriversPerTrip)]
    public List<Guid> DriverIds { get; set; } = new();
}

public class AssignLegsDto
{
    [Required, MinLength(1), MaxLength(PlanningExecutionConsts.MaxLegsPerCommand)]
    public List<Guid> LegIds { get; set; } = new();

    public Guid? AfterStopId { get; set; }

    public bool AllowOutsideWindow { get; set; }
}

public class UnassignLegsDto
{
    [Required, MinLength(1), MaxLength(PlanningExecutionConsts.MaxLegsPerCommand)]
    public List<Guid> LegIds { get; set; } = new();
}

public class MoveLegsDto
{
    [Required, MinLength(1), MaxLength(PlanningExecutionConsts.MaxLegsPerCommand)]
    public List<Guid> LegIds { get; set; } = new();

    [Required]
    public Guid ToTripId { get; set; }

    public bool AllowOutsideWindow { get; set; }
}

public class AddManualStopDto
{
    [Required]
    public Guid WaypointTypeId { get; set; }

    [Required]
    public Guid RelationId { get; set; }

    public Guid? RelationAddressId { get; set; }

    /// <summary>1..n; null appends.</summary>
    public int? Position { get; set; }

    [Range(0, 1440)]
    public int? PlannedDurationMin { get; set; }

    [MaxLength(PlanningExecutionConsts.MaxRemarksLength)]
    public string? Remarks { get; set; }
}

/// <summary>PUT /{id}/stops/{stopId}: move (position) and/or remarks.</summary>
public class UpdateStopDto
{
    public int? Position { get; set; }

    [MaxLength(PlanningExecutionConsts.MaxRemarksLength)]
    public string? Remarks { get; set; }

    [Range(0, 1440)]
    public int? PlannedDurationMin { get; set; }
}

public class StopActualsDto
{
    public DateTimeOffset? ActualArrival { get; set; }

    public DateTimeOffset? ActualDeparture { get; set; }

    [Range(0, int.MaxValue)]
    public int? CumulativeDistanceM { get; set; }
}

public class TripTimestampDto
{
    /// <summary>The instant; null = now.</summary>
    public DateTimeOffset? At { get; set; }
}

public class SplitLineDto
{
    [Required]
    public Guid LegId { get; set; }

    /// <summary>The quantity the new leg takes (0 &lt; q &lt; the leg's quantity).</summary>
    [Required]
    public decimal Quantity { get; set; }

    /// <summary>The trip the new leg goes to; null leaves it unplanned.</summary>
    public Guid? ToTripId { get; set; }

    public bool AllowOutsideWindow { get; set; }
}

public class LegReferenceDto
{
    [Required]
    public Guid LegId { get; set; }
}

public class OutsourceOriginDto
{
    [Required]
    public Guid LegId { get; set; }

    [Required]
    public Guid HubRelationId { get; set; }

    public Guid? HubAddressId { get; set; }

    [Required]
    public Guid ToTripId { get; set; }

    public bool AllowOutsideWindow { get; set; }
}

/// <summary>The trip list's filters (design 59 API contract). <c>View</c>: today, thisWeek, open, all (null = all).</summary>
public class TransportTripListRequestDto : ReferenceDataListRequestDto
{
    public List<TransportTripStatus>? Status { get; set; }

    public DateOnly? DateFrom { get; set; }

    public DateOnly? DateTo { get; set; }

    public DateTimeOffset? ExecutionFrom { get; set; }

    public DateTimeOffset? ExecutionTo { get; set; }

    public Guid? CarrierRelationId { get; set; }

    public Guid? OrganizationUnitId { get; set; }

    /// <summary>Null: only trips that are not archived; true: only archived; false: not archived.</summary>
    public bool? IsArchived { get; set; }

    public bool HideEmpty { get; set; }

    [MaxLength(16)]
    public string? View { get; set; }
}

/// <summary>
/// One column of a list export (UI-GRID-013): the grid's column id (<c>field</c> or <c>colId</c>, e.g. "number",
/// "vehicles", "route") and the header text the user sees. A missing header is localized on the server in the UI culture.
/// </summary>
public class ExportColumnDto
{
    [Required, MaxLength(PlanningExecutionConsts.MaxExportFieldLength)]
    public string Field { get; set; } = string.Empty;

    [MaxLength(PlanningExecutionConsts.MaxExportHeaderLength)]
    public string? Header { get; set; }
}

/// <summary>
/// The trip list's export (UI-GRID-013, design 61): the list's own request (filters, view, search, sort; paging is
/// ignored) plus the columns in display order (empty = every exportable column) and <see cref="AllRows"/> (true = the
/// rows the list's TotalCount counts: every trip of the archived state asked for, ignoring the other filters, the view and
/// the search; the sort still applies). The answer is an xlsx file; more rows than the setting
/// <c>Planning:Export:MaxRows</c> (default 50,000) are refused. Times are written in the tenant's time zone.
/// </summary>
public class TransportTripExportRequestDto : TransportTripListRequestDto
{
    [MaxLength(PlanningExecutionConsts.MaxExportColumns)]
    public List<ExportColumnDto> Columns { get; set; } = new();

    public bool AllRows { get; set; }
}

/// <summary>
/// The trip picker's request (design 61 "Outsource origin" target trip): <c>GET /api/planning/transport-trip/picker</c>,
/// gated by the Trips node like the list (not a master-data <c>GetLookupAsync</c>). Paged search over the trip's search
/// key (number, name, carrier, vehicles, customers, order numbers, stop cities), the statuses (default Planned and
/// InProgress), the trip date range and one trip to leave out (the leg's own trip). Archived trips never appear.
/// </summary>
public class TransportTripPickerRequestDto : ReferenceDataListRequestDto
{
    /// <summary>Empty = Planned and InProgress (the trips a leg can still go to).</summary>
    public List<TransportTripStatus>? Status { get; set; }

    public DateOnly? DateFrom { get; set; }

    public DateOnly? DateTo { get; set; }

    public Guid? ExcludeTripId { get; set; }
}

/// <summary>A trip as the pickers show it.</summary>
public class TransportTripPickerDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string? Name { get; set; }
    public DateOnly TripDate { get; set; }
    public TransportTripStatus Status { get; set; }
    public Guid? CarrierRelationId { get; set; }
    public string? CarrierName { get; set; }
    public DateTimeOffset PlannedDeparture { get; set; }
}

public interface ITransportTripAppService : IApplicationService
{
    Task<GridResponse<TransportTripRowDto>> GetListAsync(TransportTripListRequestDto input);
    Task<Volo.Abp.Content.IRemoteStreamContent> ExportAsync(TransportTripExportRequestDto input);
    Task<GridResponse<TransportTripPickerDto>> GetPickerAsync(TransportTripPickerRequestDto input);
    Task<TransportTripDto> GetAsync(Guid id);
    Task<TransportTripSummaryDto> GetSummaryAsync(Guid id);
    Task<TransportTripDto> CreateAsync(TransportTripCreateDto input);
    Task<TransportTripDto> UpdateAsync(Guid id, TransportTripUpdateDto input);
    Task<TransportTripDto> SetCarrierAsync(Guid id, TripCarrierDto input);
    Task<TransportTripDto> SetVehiclesAsync(Guid id, TripVehiclesDto input);
    Task<TransportTripDto> SetDriversAsync(Guid id, TripDriversDto input);
    Task<TransportTripDto> AssignLegsAsync(Guid id, AssignLegsDto input);
    Task<TransportTripDto> UnassignLegsAsync(Guid id, UnassignLegsDto input);
    Task<TransportTripDto> MoveLegsAsync(Guid id, MoveLegsDto input);
    Task<TransportTripDto> AddStopAsync(Guid id, AddManualStopDto input);
    Task<TransportTripDto> UpdateStopAsync(Guid id, Guid stopId, UpdateStopDto input);
    Task<TransportTripDto> DeleteStopAsync(Guid id, Guid stopId);
    Task<TransportTripDto> RecordStopActualsAsync(Guid id, Guid stopId, StopActualsDto input);
    Task<TransportTripDto> StartAsync(Guid id, TripTimestampDto input);
    Task<TransportTripDto> CompleteAsync(Guid id, TripTimestampDto input);
    Task<TransportTripDto> CancelAsync(Guid id);
    Task<TransportTripDto> ArchiveAsync(Guid id);
    Task<TransportTripDto> UnarchiveAsync(Guid id);
    Task DeleteAsync(Guid id);
    Task<LegDto> SplitLineAsync(Guid id, SplitLineDto input);
    Task<LegDto> ReturnLegAsync(Guid id, LegReferenceDto input);
    Task<TransportTripDto> CarryOutSeparatelyAsync(Guid id, LegReferenceDto input);
    Task<LegDto> OutsourceOriginAsync(Guid id, OutsourceOriginDto input);
}
