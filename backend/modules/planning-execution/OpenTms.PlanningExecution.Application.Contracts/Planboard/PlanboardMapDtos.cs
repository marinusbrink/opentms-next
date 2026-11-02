using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.MasterData.Fleet;
using OpenTms.PlanningExecution.TransportTrips;
using Volo.Abp.Application.Services;

namespace OpenTms.PlanningExecution.Planboard;

/* Design 68 "API contract — reads": the Map pane's reads, the Resources pane's reads, and the stop optimisation (§5). */

/// <summary>A trip on the map: its stops, its stored route and figures (stored values only, AD-07).</summary>
public class PlanboardTripMapDto
{
    public Guid Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public TransportTripStatus Status { get; set; }

    public DateTimeOffset PlannedDeparture { get; set; }

    public DateTimeOffset? PlannedArrival { get; set; }

    /// <summary>The departure address when it is not the first stop (first/last stop rule); null otherwise or without coordinates.</summary>
    public PlanboardMapPointDto? DeparturePoint { get; set; }

    /// <summary>The arrival address when it is not the last stop; null otherwise or without coordinates.</summary>
    public PlanboardMapPointDto? ArrivalPoint { get; set; }

    public List<PlanboardMapStopDto> Stops { get; set; } = new();

    /// <summary>The routed segments; empty when routing is off (the map draws straight lines).</summary>
    public List<PlanboardRouteSegmentDto> Segments { get; set; } = new();

    public decimal? TollAmount { get; set; }

    public string? TollCurrencyCode { get; set; }

    public int? TollDistanceM { get; set; }

    public int? PlannedDistanceM { get; set; }

    public int? PlannedDurationS { get; set; }

    public decimal? Co2Kg { get; set; }

    public string? RouteProvider { get; set; }

    public DateTimeOffset? RoutedAt { get; set; }

    /// <summary>When the derived values were last calculated.</summary>
    public DateTimeOffset? CalculatedAt { get; set; }

    /// <summary>The trip's version (its concurrency stamp).</summary>
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class PlanboardMapPointDto
{
    public double Latitude { get; set; }

    public double Longitude { get; set; }
}

public class PlanboardMapStopDto
{
    public Guid Id { get; set; }

    public int Position { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? City { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public Guid WaypointTypeId { get; set; }

    public bool IsManual { get; set; }

    public DateTimeOffset? PlannedArrival { get; set; }

    public DateTimeOffset? PlannedDeparture { get; set; }

    public DateTimeOffset? EstimatedArrival { get; set; }

    public DateTimeOffset? EstimatedDeparture { get; set; }

    public DateTimeOffset? ActualArrival { get; set; }

    public DateTimeOffset? ActualDeparture { get; set; }

    /// <summary>The window of the stop's legs: the latest window start of the leg ends at this stop.</summary>
    public DateTimeOffset? WindowFrom { get; set; }

    /// <summary>The window of the stop's legs: the earliest window end of the leg ends at this stop.</summary>
    public DateTimeOffset? WindowTill { get; set; }

    /// <summary>Minutes the estimated arrival lies after <see cref="WindowTill"/> (0 = in time); null when either is unknown.</summary>
    public int? LatenessMinutes { get; set; }

    /// <summary>The legs that start or end at this stop.</summary>
    public List<Guid> LegIds { get; set; } = new();
}

/// <summary>One stored route segment (design 68 §4): null stop ids are the trip's departure or arrival address.</summary>
public class PlanboardRouteSegmentDto
{
    public Guid? FromStopId { get; set; }

    public Guid? ToStopId { get; set; }

    public string? EncodedPolyline { get; set; }

    public int DistanceM { get; set; }

    public int DurationS { get; set; }

    public int? TollDistanceM { get; set; }

    public decimal? TollAmount { get; set; }
}

/// <summary>The "all trips" layer of the map (D16): live trips overlapping the window (at most 14 days).</summary>
public class PlanboardMapTripsRequestDto
{
    [Required]
    public DateTimeOffset From { get; set; }

    [Required]
    public DateTimeOffset To { get; set; }

    /// <summary>Trips of any of these organisation units; empty = all.</summary>
    public List<Guid> OrganizationUnitIds { get; set; } = new();
}

public class PlanboardMapTripsDto
{
    public List<PlanboardMapTripDto> Trips { get; set; } = new();

    /// <summary>More than 500 trips matched: the earliest are returned.</summary>
    public bool Truncated { get; set; }
}

public class PlanboardMapTripDto
{
    public Guid Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public TransportTripStatus Status { get; set; }

    public DateTimeOffset PlannedDeparture { get; set; }

    public DateTimeOffset? PlannedArrival { get; set; }

    public double? FirstLatitude { get; set; }

    public double? FirstLongitude { get; set; }

    public double? LastLatitude { get; set; }

    public double? LastLongitude { get; set; }

    public int StopCount { get; set; }

    public int WarningCount { get; set; }
}

/// <summary>Unplanned legs whose origin or destination lies within the radius of a stop of the trip.</summary>
public class PlanboardUnplannedNearRequestDto
{
    [Required]
    public Guid TripId { get; set; }

    /// <summary>1 to 100 km.</summary>
    [Range(1, PlanningExecutionConsts.MaxUnplannedNearRadiusKm)]
    public double RadiusKm { get; set; } = 25;

    /// <summary>The legs' origin dates from (inclusive).</summary>
    [Required]
    public DateOnly From { get; set; }

    /// <summary>The legs' origin dates till (inclusive), at most 31 days after <see cref="From"/>.</summary>
    [Required]
    public DateOnly To { get; set; }
}

public class PlanboardUnplannedNearDto
{
    /// <summary>At most 200, nearest first.</summary>
    public List<PlanboardNearLegDto> Legs { get; set; } = new();

    /// <summary>Customer id → name, one batched lookup.</summary>
    public Dictionary<Guid, string> Names { get; set; } = new();

    /// <summary>More legs matched than returned.</summary>
    public bool Truncated { get; set; }
}

public class PlanboardNearLegDto
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public Guid CustomerRelationId { get; set; }

    public string OriginName { get; set; } = string.Empty;

    public string? OriginCity { get; set; }

    public double? OriginLatitude { get; set; }

    public double? OriginLongitude { get; set; }

    public DateOnly OriginPlannedDate { get; set; }

    public DateTimeOffset? OriginWindowFrom { get; set; }

    public DateTimeOffset? OriginWindowTill { get; set; }

    public string DestinationName { get; set; } = string.Empty;

    public string? DestinationCity { get; set; }

    public double? DestinationLatitude { get; set; }

    public double? DestinationLongitude { get; set; }

    public DateOnly DestinationPlannedDate { get; set; }

    public DateTimeOffset? DestinationWindowFrom { get; set; }

    public DateTimeOffset? DestinationWindowTill { get; set; }

    public decimal Quantity { get; set; }

    public double? WeightKg { get; set; }

    public double? LoadingMeters { get; set; }

    /// <summary>The great-circle distance of the nearer leg end to the nearest stop of the trip, kilometres (one decimal).</summary>
    public double DistanceKm { get; set; }
}

/// <summary>The Resources pane's drivers (design 69) with their blocks and trips in the window (at most 14 days).</summary>
public class PlanboardResourceDriversRequestDto
{
    [Required]
    public DateTimeOffset From { get; set; }

    [Required]
    public DateTimeOffset To { get; set; }

    [MaxLength(Platform.ReferenceData.ReferenceDataConsts.MaxSearchLength)]
    public string? Search { get; set; }

    [Range(0, int.MaxValue)]
    public int Skip { get; set; }

    [Range(1, PlanningExecutionConsts.MaxPlanboardPageSize)]
    public int Take { get; set; } = 50;
}

public class PlanboardResourceDriversDto
{
    public List<PlanboardResourceDriverDto> Items { get; set; } = new();

    public long TotalCount { get; set; }
}

public class PlanboardResourceDriverDto
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>The live trips of the driver overlapping the window, by planned departure.</summary>
    public List<Guid> TripIds { get; set; } = new();

    public List<PlanboardBlockDto> Blocks { get; set; } = new();
}

/// <summary>The Resources pane's Vehicles tab: vehicles of every kind with their blocks and trips in the window.</summary>
public class PlanboardResourceVehiclesRequestDto
{
    [Required]
    public DateTimeOffset From { get; set; }

    [Required]
    public DateTimeOffset To { get; set; }

    public List<Guid> OrganizationUnitIds { get; set; } = new();

    [MaxLength(Platform.ReferenceData.ReferenceDataConsts.MaxSearchLength)]
    public string? Search { get; set; }

    [Range(0, int.MaxValue)]
    public int Skip { get; set; }

    [Range(1, PlanningExecutionConsts.MaxPlanboardPageSize)]
    public int Take { get; set; } = 50;
}

public class PlanboardResourceVehiclesDto
{
    public List<PlanboardResourceVehicleDto> Items { get; set; } = new();

    public long TotalCount { get; set; }
}

public class PlanboardResourceVehicleDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? LicencePlate { get; set; }

    public VehicleTypeKind Kind { get; set; }

    public string TypeName { get; set; } = string.Empty;

    public List<Guid> TripIds { get; set; } = new();

    public List<PlanboardBlockDto> Blocks { get; set; } = new();
}

/// <summary>What <c>POST /api/planning/planboard/{id}/optimize-stops</c> answers with 202: the job is queued.</summary>
public class PlanboardOptimizeStopsDto
{
    public Guid TripId { get; set; }

    public string? JobId { get; set; }
}

/// <summary>
/// Design 68 §5: queues the optimisation of a trip's stop order (a Hangfire job in the tenant's context). Not a
/// conventional controller: <c>OpenTms.HttpApi</c> exposes it as <c>POST /api/planning/planboard/{id}/optimize-stops</c>
/// with 202 Accepted.
/// </summary>
public interface IPlanboardStopOptimizationAppService : IApplicationService
{
    Task<PlanboardOptimizeStopsDto> OptimizeStopsAsync(Guid id);
}
