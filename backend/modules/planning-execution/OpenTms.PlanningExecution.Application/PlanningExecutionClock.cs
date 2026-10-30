using System;
using Microsoft.Extensions.Options;
using OpenTms.Platform.NumberSequences;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Timing;

namespace OpenTms.PlanningExecution;

/// <summary>
/// "Today" and "this week" of the planner are dates in the tenant's time zone (<see cref="NumberSequenceOptions.TimeZone"/>,
/// Europe/Amsterdam by default), not UTC: a trip of tomorrow 00:30 local is tomorrow's, whatever the server's clock says.
/// </summary>
public class PlanningExecutionClock : ITransientDependency
{
    private readonly IClock _clock;
    private readonly NumberSequenceOptions _options;

    public PlanningExecutionClock(IClock clock, IOptions<NumberSequenceOptions> options)
    {
        _clock = clock;
        _options = options.Value;
    }

    public virtual TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZone);

    public virtual DateOnly Today()
    {
        var now = _clock.Now;
        var utc = now.Kind == DateTimeKind.Local ? now.ToUniversalTime() : DateTime.SpecifyKind(now, DateTimeKind.Utc);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Zone));
    }
}
