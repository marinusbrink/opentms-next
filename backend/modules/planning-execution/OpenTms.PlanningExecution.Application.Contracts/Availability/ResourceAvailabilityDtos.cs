using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.PlanningExecution.Planboard;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace OpenTms.PlanningExecution.Availability;

/* Design 68 §2 and "API contract — reads": the unavailability and working times of vehicles and drivers. */

/// <summary>The availability of up to 500 resources of one kind in a window (at most 62 days).</summary>
public class ResourceAvailabilityListRequestDto
{
    [Required]
    public ResourceKind ResourceKind { get; set; }

    [Required]
    public List<Guid> ResourceIds { get; set; } = new();

    [Required]
    public DateTimeOffset From { get; set; }

    [Required]
    public DateTimeOffset To { get; set; }
}

public class ResourceAvailabilityListDto
{
    /// <summary>The unavailability occurrences and outside-working-time blocks overlapping the window, per resource and time.</summary>
    public List<PlanboardBlockDto> Blocks { get; set; } = new();

    /// <summary>The unavailability records behind the blocks (for editing a repetition).</summary>
    public List<ResourceUnavailabilityDto> Unavailabilities { get; set; } = new();

    /// <summary>The working patterns of the resources.</summary>
    public List<ResourceWorkingPatternDto> Patterns { get; set; } = new();
}

public class ResourceUnavailabilityDto : EntityDto<Guid>
{
    public ResourceKind ResourceKind { get; set; }

    public Guid ResourceId { get; set; }

    public DateTimeOffset From { get; set; }

    public DateTimeOffset Till { get; set; }

    public UnavailabilityReason Reason { get; set; }

    public string? Remarks { get; set; }

    /// <summary>The weekdays of a weekly repetition as flags (Mon=1 … Sun=64); null = a single period.</summary>
    public int? RecurrenceWeekdays { get; set; }

    public DateOnly? RecurrenceUntil { get; set; }

    public string ConcurrencyStamp { get; set; } = string.Empty;

    public DateTime? LastModificationTime { get; set; }
}

/// <summary>A new unavailability; the resource is validated against Master Data.</summary>
public class ResourceUnavailabilityCreateDto
{
    [Required]
    public ResourceKind ResourceKind { get; set; }

    [Required]
    public Guid ResourceId { get; set; }

    [Required]
    public DateTimeOffset From { get; set; }

    [Required]
    public DateTimeOffset Till { get; set; }

    [Required]
    public UnavailabilityReason Reason { get; set; }

    [MaxLength(PlanningExecutionConsts.MaxUnavailabilityRemarksLength)]
    public string? Remarks { get; set; }

    /// <summary>Weekly repetition: the weekdays as flags (Mon=1 … Sun=64), with <see cref="RecurrenceUntil"/>; the period is at most 24 hours.</summary>
    [Range(1, AvailabilityWeekdays.All)]
    public int? RecurrenceWeekdays { get; set; }

    public DateOnly? RecurrenceUntil { get; set; }
}

/// <summary>Changes an unavailability's period, reason, remarks and repetition; the resource stays.</summary>
public class ResourceUnavailabilityUpdateDto
{
    [Required]
    public DateTimeOffset From { get; set; }

    [Required]
    public DateTimeOffset Till { get; set; }

    [Required]
    public UnavailabilityReason Reason { get; set; }

    [MaxLength(PlanningExecutionConsts.MaxUnavailabilityRemarksLength)]
    public string? Remarks { get; set; }

    [Range(1, AvailabilityWeekdays.All)]
    public int? RecurrenceWeekdays { get; set; }

    public DateOnly? RecurrenceUntil { get; set; }

    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

/// <summary>A resource's working time on one weekday (0 = Monday … 6 = Sunday), local time in the tenant's time zone.</summary>
public class ResourceWorkingPatternDto
{
    public ResourceKind ResourceKind { get; set; }

    public Guid ResourceId { get; set; }

    public int Weekday { get; set; }

    public TimeOnly Start { get; set; }

    public TimeOnly End { get; set; }
}

/// <summary>Replaces a resource's working pattern: the weekdays named are set, the others lose their constraint.</summary>
public class ResourceWorkingPatternsUpdateDto
{
    [Required]
    public ResourceKind ResourceKind { get; set; }

    [Required]
    public Guid ResourceId { get; set; }

    /// <summary>At most one entry per weekday; empty removes the pattern.</summary>
    [MaxLength(7)]
    public List<ResourceWorkingDayDto> Days { get; set; } = new();
}

public class ResourceWorkingDayDto
{
    /// <summary>0 = Monday … 6 = Sunday.</summary>
    [Range(0, 6)]
    public int Weekday { get; set; }

    [Required]
    public TimeOnly Start { get; set; }

    [Required]
    public TimeOnly End { get; set; }
}

/// <summary>Design 68 §2, route root <c>/api/planning/resource-availability</c>.</summary>
public interface IResourceAvailabilityAppService : IApplicationService
{
    /// <summary><c>GET /api/planning/resource-availability</c>: blocks expanded for the window, the records behind them, the patterns.</summary>
    Task<ResourceAvailabilityListDto> GetListAsync(ResourceAvailabilityListRequestDto input);

    /// <summary><c>GET /api/planning/resource-availability/{id}</c>.</summary>
    Task<ResourceUnavailabilityDto> GetAsync(Guid id);

    /// <summary><c>POST /api/planning/resource-availability</c>.</summary>
    Task<ResourceUnavailabilityDto> CreateAsync(ResourceUnavailabilityCreateDto input);

    /// <summary><c>PUT /api/planning/resource-availability/{id}</c>.</summary>
    Task<ResourceUnavailabilityDto> UpdateAsync(Guid id, ResourceUnavailabilityUpdateDto input);

    /// <summary><c>DELETE /api/planning/resource-availability/{id}</c>.</summary>
    Task DeleteAsync(Guid id);

    /// <summary><c>PUT /api/planning/resource-availability/patterns</c>: the resource's whole pattern.</summary>
    Task<List<ResourceWorkingPatternDto>> UpdatePatternsAsync(ResourceWorkingPatternsUpdateDto input);
}
