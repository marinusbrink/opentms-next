using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.PlanningExecution.Planboard;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.PlanningExecution.Availability;

/// <summary>
/// Design 68 §2: the availability of a set of resources of one kind in a window, for the Scheduler's blocks, the Resources
/// pane and the availability list. Two statements whatever the number of resources (the unavailabilities that may overlap
/// the window, the working patterns), then the expansion in the tenant's time zone in memory. Nothing is stored.
/// </summary>
public class AvailabilityReader : ITransientDependency
{
    private readonly IRepository<ResourceUnavailability, Guid> _unavailabilities;
    private readonly IRepository<ResourceWorkingPattern, Guid> _patterns;
    private readonly AvailabilityExpander _expander;
    private readonly PlanningExecutionClock _clock;

    public AvailabilityReader(
        IRepository<ResourceUnavailability, Guid> unavailabilities,
        IRepository<ResourceWorkingPattern, Guid> patterns,
        AvailabilityExpander expander,
        PlanningExecutionClock clock)
    {
        _unavailabilities = unavailabilities;
        _patterns = patterns;
        _expander = expander;
        _clock = clock;
    }

    public TimeZoneInfo Zone => _clock.Zone;

    /// <summary>The unavailabilities of the resources whose span reaches into the window, and the resources' patterns.</summary>
    public async Task<(List<ResourceUnavailability> Unavailabilities, List<ResourceWorkingPattern> Patterns)> LoadAsync(
        ResourceKind kind, IReadOnlyCollection<Guid> resourceIds, DateTimeOffset from, DateTimeOffset to)
    {
        var ids = resourceIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0 || to <= from)
        {
            return ([], []);
        }

        var fromUtc = from.UtcDateTime;
        var toUtc = to.UtcDateTime;
        var unavailabilities = await _unavailabilities.AsyncExecuter.ToListAsync((await _unavailabilities.GetQueryableAsync())
            .Where(u => u.ResourceKind == kind && ids.Contains(u.ResourceId) && u.FromUtc < toUtc && u.SpanTillUtc > fromUtc)
            .OrderBy(u => u.FromUtc).ThenBy(u => u.Id));
        var patterns = await _patterns.AsyncExecuter.ToListAsync((await _patterns.GetQueryableAsync())
            .Where(p => p.ResourceKind == kind && ids.Contains(p.ResourceId))
            .OrderBy(p => p.ResourceId).ThenBy(p => p.Weekday));
        return (unavailabilities, patterns);
    }

    /// <summary>The blocks of the resources overlapping the window: unavailability occurrences (unclipped) and outside working time (clipped).</summary>
    public async Task<List<PlanboardBlockDto>> BlocksAsync(ResourceKind kind, IReadOnlyCollection<Guid> resourceIds, DateTimeOffset from, DateTimeOffset to)
    {
        var (unavailabilities, patterns) = await LoadAsync(kind, resourceIds, from, to);
        return Blocks(kind, resourceIds, unavailabilities, patterns, from, to);
    }

    public List<PlanboardBlockDto> Blocks(ResourceKind kind, IReadOnlyCollection<Guid> resourceIds, IReadOnlyList<ResourceUnavailability> unavailabilities,
        IReadOnlyList<ResourceWorkingPattern> patterns, DateTimeOffset from, DateTimeOffset to)
    {
        var zone = Zone;
        var blocks = new List<AvailabilityBlock>();
        foreach (var unavailability in unavailabilities)
        {
            blocks.AddRange(_expander.Expand(unavailability, from, to, zone));
        }

        foreach (var resourceId in resourceIds.Distinct())
        {
            blocks.AddRange(_expander.OutsideWorkingTime(kind, resourceId, patterns, from, to, zone));
        }

        return blocks
            .OrderBy(b => b.ResourceId).ThenBy(b => b.From).ThenBy(b => b.UnavailabilityId)
            .Select(ToDto)
            .ToList();
    }

    public static PlanboardBlockDto ToDto(AvailabilityBlock block) => new()
    {
        ResourceId = block.ResourceId,
        From = block.From,
        Till = block.Till,
        UnavailabilityId = block.UnavailabilityId,
        Reason = block.Reason == null ? null : (int)block.Reason.Value
    };
}
