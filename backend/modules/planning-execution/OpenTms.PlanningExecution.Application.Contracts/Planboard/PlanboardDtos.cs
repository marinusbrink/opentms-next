using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.MasterData.Fleet;
using OpenTms.PlanningExecution.TransportTrips;
using Volo.Abp.Application.Services;

namespace OpenTms.PlanningExecution.Planboard;

/* Design 68 §6 and "API contract — reads": the Planboard's narrow commands and its Scheduler reads. */

/// <summary>What every Planboard command returns (D19): the trip's id (the new one for a create) and its new stamp.</summary>
public class PlanboardChangeDto
{
    public Guid Id { get; set; }

    public string ConcurrencyStamp { get; set; } = string.Empty;

    public DateTime? LastModificationTime { get; set; }
}

/// <summary>Moves a trip in time; the arrival keeps its distance to the departure when omitted.</summary>
public class PlanboardRescheduleDto
{
    [Required]
    public DateTimeOffset PlannedDeparture { get; set; }

    public DateTimeOffset? PlannedArrival { get; set; }

    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

/// <summary>
/// Replaces a resource of the trip or puts one on it (a drop on another Scheduler row): Tractor replaces the motor vehicle,
/// Trailer replaces <see cref="FromResourceId"/> or takes the next free position, Driver likewise (at most three).
/// </summary>
public class PlanboardChangeResourceDto
{
    [Required]
    public PlanboardResourceMode Mode { get; set; }

    /// <summary>The resource of the row the bar was dragged from; null puts the resource on the trip.</summary>
    public Guid? FromResourceId { get; set; }

    [Required]
    public Guid ToResourceId { get; set; }

    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

/// <summary>A driver dropped from the Resources pane onto a trip (S:Q-15): the next position, a driver already on it is a no-op.</summary>
public class PlanboardAddDriverDto
{
    [Required]
    public Guid DriverId { get; set; }

    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

/// <summary>An inline edit of the trips pane: one header field.</summary>
public class PlanboardUpdateFieldDto
{
    [Required]
    public PlanboardTripField Field { get; set; }

    /// <summary>The new text; empty or null clears it. Name keeps its own maximum length.</summary>
    [MaxLength(PlanningExecutionConsts.MaxRemarksLength)]
    public string? Value { get; set; }

    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

/// <summary>
/// A new trip from an empty Scheduler cell or a leg drop on a row: the trip type (default: the tenant's default type,
/// else its first active one by code) fills the empty groups, then the row's resource goes in its mode's slot; the legs
/// are assigned in the same unit of work.
/// </summary>
public class PlanboardCreateTripDto
{
    public Guid? TransportTripTypeId { get; set; }

    /// <summary>The Scheduler mode of the row; required with <see cref="ResourceId"/>.</summary>
    public PlanboardResourceMode? Mode { get; set; }

    public Guid? ResourceId { get; set; }

    [Required]
    public DateTimeOffset PlannedDeparture { get; set; }

    /// <summary>At most 200 unplanned legs; dropped legs go on the trip whatever their window (the planner chose the cell).</summary>
    public List<Guid> LegIds { get; set; } = new();

    /// <summary>
    /// Design 74 §8: an order dropped on an empty cell: all its legs except the stuck go on the new trip, whatever their window,
    /// leaving their old trips. Not together with <see cref="LegIds"/>.
    /// </summary>
    public Guid? OrderId { get; set; }
}

/// <summary>Design 74 §8: <c>GET planboard/{id}/assign-order-preview</c>.</summary>
public class PlanboardAssignOrderPreviewRequestDto
{
    [Required]
    public Guid OrderId { get; set; }
}

/// <summary>Design 74 §8: <c>POST planboard/{id}/assign-order</c>: all legs of the order except the stuck go on the trip, in one unit of work.</summary>
public class PlanboardAssignOrderDto
{
    [Required]
    public Guid OrderId { get; set; }

    /// <summary>Overrides <c>LegOutsideTripWindow</c>.</summary>
    public bool AllowOutsideWindow { get; set; }

    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

/// <summary>What a drop of the order would do. A confirmation is due only when <see cref="PulledFrom"/> is not empty.</summary>
public class PlanboardAssignOrderPreviewDto
{
    /// <summary>Every leg of the order: those that would move, those stuck and those on this trip already.</summary>
    public List<PlanboardOrderLegDto> Legs { get; set; } = new();

    public List<PlanboardPulledFromDto> PulledFrom { get; set; } = new();

    public List<PlanboardOrderLegDto> Stuck { get; set; } = new();

    /// <summary>The legs a drop would move; zero means the command is refused with the reason.</summary>
    public int MovableCount { get; set; }
}

/// <summary>The change of an order drop (or a new trip): the trip's new stamp plus what moved and what stayed.</summary>
public class PlanboardAssignOrderResultDto : PlanboardChangeDto
{
    public List<PlanboardOrderLegDto> Moved { get; set; } = new();

    public List<PlanboardOrderLegDto> Stuck { get; set; } = new();

    public List<PlanboardPulledFromDto> PulledFrom { get; set; } = new();
}

/// <summary>A trip legs leave: its number and how many.</summary>
public class PlanboardPulledFromDto
{
    public Guid TripId { get; set; }

    public string Number { get; set; } = string.Empty;

    public int LegCount { get; set; }
}

/// <summary>A leg of a dragged order, enough to name it in a confirmation or a warning.</summary>
public class PlanboardOrderLegDto
{
    public Guid Id { get; set; }

    public Guid OrderLineId { get; set; }

    public int Position { get; set; }

    public string OriginName { get; set; } = string.Empty;

    public string? OriginCity { get; set; }

    public string DestinationName { get; set; } = string.Empty;

    public string? DestinationCity { get; set; }

    public Guid? TripId { get; set; }

    public string? TripNumber { get; set; }

    /// <summary>Set on a stuck leg: why it stays.</summary>
    public PlanboardStuckReason? StuckReason { get; set; }
}

/// <summary>Legs put on or taken off a trip by a drop.</summary>
public class PlanboardLegsDto
{
    [Required]
    public List<Guid> LegIds { get; set; } = new();

    /// <summary>Assign only: overrides <c>LegOutsideTripWindow</c>.</summary>
    public bool AllowOutsideWindow { get; set; }

    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

/// <summary>P:Q17: completes a running trip with both actual times; stops keep recorded actuals, missing ones get the arrival.</summary>
public class PlanboardCompleteDto
{
    [Required]
    public DateTimeOffset ActualDeparture { get; set; }

    [Required]
    public DateTimeOffset ActualArrival { get; set; }

    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

/// <summary>One Scheduler read: a page of resource rows of the mode and every trip using them in the window (≤ 14 days).</summary>
public class PlanboardSchedulerRequestDto
{
    [Required]
    public PlanboardResourceMode Mode { get; set; }

    [Required]
    public DateTimeOffset From { get; set; }

    [Required]
    public DateTimeOffset To { get; set; }

    /// <summary>Vehicle rows of any of these organisation units; empty = all. Drivers carry no unit and are not filtered.</summary>
    public List<Guid> OrganizationUnitIds { get; set; } = new();

    /// <summary>Name or licence plate (vehicles), code or name (drivers).</summary>
    [MaxLength(Platform.ReferenceData.ReferenceDataConsts.MaxSearchLength)]
    public string? Search { get; set; }

    [Range(0, int.MaxValue)]
    public int Skip { get; set; }

    [Range(1, PlanningExecutionConsts.MaxPlanboardPageSize)]
    public int Take { get; set; } = 50;
}

/// <summary>The bars of given trips, for the live-update refetch (at most 200 ids).</summary>
public class PlanboardBarsRequestDto
{
    [Required]
    public List<Guid> Ids { get; set; } = new();
}

public class PlanboardSchedulerDto
{
    public List<PlanboardResourceRowDto> Rows { get; set; } = new();

    public List<PlanboardBarDto> Bars { get; set; } = new();

    /// <summary>Unavailability and outside-working-time blocks of the rows in the window (design 68 §2).</summary>
    public List<PlanboardBlockDto> Blocks { get; set; } = new();

    /// <summary>Id → display name of every vehicle, driver and carrier on the bars.</summary>
    public Dictionary<Guid, string> Names { get; set; } = new();

    public PlanboardSchedulerKpisDto Kpis { get; set; } = new();

    /// <summary>All rows of the mode, filter and search (the page is <see cref="Rows"/>).</summary>
    public long TotalRows { get; set; }

    /// <summary>More than <see cref="PlanningExecutionConsts.MaxSchedulerBars"/> trips matched: the earliest are returned.</summary>
    public bool BarsTruncated { get; set; }
}

public class PlanboardBarsDto
{
    public List<PlanboardBarDto> Bars { get; set; } = new();

    public Dictionary<Guid, string> Names { get; set; } = new();
}

/// <summary>A Scheduler row: a vehicle of the mode's kinds or a driver (design 69 resource query).</summary>
public class PlanboardResourceRowDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The licence plate of a vehicle, the code of a driver.</summary>
    public string? Code { get; set; }

    /// <summary>The vehicle type kind; null for a driver.</summary>
    public VehicleTypeKind? Kind { get; set; }

    /// <summary>The vehicle type's name; null for a driver.</summary>
    public string? TypeName { get; set; }

    /// <summary>A vehicle's default drivers in their order; empty for a driver.</summary>
    public List<Guid> DefaultDriverIds { get; set; } = new();
}

/// <summary>A trip as a Scheduler bar: stored values only (AD-07).</summary>
public class PlanboardBarDto
{
    public Guid Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public string? Name { get; set; }

    public TransportTripStatus Status { get; set; }

    public TripExecution Execution { get; set; }

    public DateTimeOffset PlannedDeparture { get; set; }

    public DateTimeOffset? PlannedArrival { get; set; }

    public DateTimeOffset? ActualDeparture { get; set; }

    public DateTimeOffset? ActualArrival { get; set; }

    /// <summary>The vehicle ids in position order (1..3).</summary>
    public List<Guid> VehicleIds { get; set; } = new();

    /// <summary>The driver ids in position order (1..3).</summary>
    public List<Guid> DriverIds { get; set; } = new();

    public Guid? CarrierRelationId { get; set; }

    public Guid? OrganizationUnitId { get; set; }

    public string? FirstStopCity { get; set; }

    public string? LastStopCity { get; set; }

    public int StopCount { get; set; }

    public decimal? LoadFactor { get; set; }

    public List<TripWarningCode> WarningCodes { get; set; } = new();

    public string ConcurrencyStamp { get; set; } = string.Empty;
}

/// <summary>A period a row's resource is not available (design 68 §2): an unavailability occurrence or outside working time.</summary>
public class PlanboardBlockDto
{
    public Guid ResourceId { get; set; }

    public DateTimeOffset From { get; set; }

    public DateTimeOffset Till { get; set; }

    /// <summary>The unavailability's id; null for an outside-working-time block.</summary>
    public Guid? UnavailabilityId { get; set; }

    /// <summary>The reason code of an unavailability; null for an outside-working-time block.</summary>
    public int? Reason { get; set; }
}

/// <summary>The Scheduler's figures over the rows of the page and the window.</summary>
public class PlanboardSchedulerKpisDto
{
    public int Rows { get; set; }

    public int Trips { get; set; }

    public int InProgress { get; set; }

    public int RowsWithoutTrip { get; set; }

    /// <summary>Planned hours of the rows' trips inside the window / (rows × window hours) × 100, one decimal.</summary>
    public decimal UtilisationPercent { get; set; }
}

/// <summary>Design 68 §6: the Planboard's reads and narrow commands, route root <c>/api/planning/planboard</c>.</summary>
public interface IPlanboardAppService : IApplicationService
{
    /// <summary><c>GET /api/planning/planboard/scheduler</c>.</summary>
    Task<PlanboardSchedulerDto> GetSchedulerAsync(PlanboardSchedulerRequestDto input);

    /// <summary><c>GET /api/planning/planboard/scheduler-bars</c>.</summary>
    Task<PlanboardBarsDto> GetSchedulerBarsAsync(PlanboardBarsRequestDto input);

    /// <summary><c>POST /api/planning/planboard/{id}/reschedule</c>.</summary>
    Task<PlanboardChangeDto> RescheduleAsync(Guid id, PlanboardRescheduleDto input);

    /// <summary><c>POST /api/planning/planboard/{id}/change-resource</c>.</summary>
    Task<PlanboardChangeDto> ChangeResourceAsync(Guid id, PlanboardChangeResourceDto input);

    /// <summary><c>POST /api/planning/planboard/{id}/driver</c>.</summary>
    Task<PlanboardChangeDto> AddDriverAsync(Guid id, PlanboardAddDriverDto input);

    /// <summary><c>POST /api/planning/planboard/{id}/set-field</c>.</summary>
    Task<PlanboardChangeDto> SetFieldAsync(Guid id, PlanboardUpdateFieldDto input);

    /// <summary><c>POST /api/planning/planboard/trip</c>; with an order, <c>Moved</c>, <c>Stuck</c> and <c>PulledFrom</c> say what it did.</summary>
    Task<PlanboardAssignOrderResultDto> CreateTripAsync(PlanboardCreateTripDto input);

    /// <summary><c>GET /api/planning/planboard/{id}/assign-order-preview?orderId=</c> (design 74 §8).</summary>
    Task<PlanboardAssignOrderPreviewDto> GetAssignOrderPreviewAsync(Guid id, PlanboardAssignOrderPreviewRequestDto input);

    /// <summary><c>POST /api/planning/planboard/{id}/assign-order</c> (design 74 §8).</summary>
    Task<PlanboardAssignOrderResultDto> AssignOrderAsync(Guid id, PlanboardAssignOrderDto input);

    /// <summary><c>POST /api/planning/planboard/{id}/assign-legs</c>.</summary>
    Task<PlanboardChangeDto> AssignLegsAsync(Guid id, PlanboardLegsDto input);

    /// <summary><c>POST /api/planning/planboard/{id}/unassign-legs</c>.</summary>
    Task<PlanboardChangeDto> UnassignLegsAsync(Guid id, PlanboardLegsDto input);

    /// <summary><c>POST /api/planning/planboard/{id}/complete</c>.</summary>
    Task<PlanboardChangeDto> CompleteAsync(Guid id, PlanboardCompleteDto input);

    /// <summary><c>GET /api/planning/planboard/{id}/map</c>.</summary>
    Task<PlanboardTripMapDto> GetMapAsync(Guid id);

    /// <summary><c>GET /api/planning/planboard/map-trips</c>.</summary>
    Task<PlanboardMapTripsDto> GetMapTripsAsync(PlanboardMapTripsRequestDto input);

    /// <summary><c>GET /api/planning/planboard/unplanned-near</c>.</summary>
    Task<PlanboardUnplannedNearDto> GetUnplannedNearAsync(PlanboardUnplannedNearRequestDto input);

    /// <summary><c>GET /api/planning/planboard/resource-drivers</c>.</summary>
    Task<PlanboardResourceDriversDto> GetResourceDriversAsync(PlanboardResourceDriversRequestDto input);

    /// <summary><c>GET /api/planning/planboard/resource-vehicles</c>.</summary>
    Task<PlanboardResourceVehiclesDto> GetResourceVehiclesAsync(PlanboardResourceVehiclesRequestDto input);
}
