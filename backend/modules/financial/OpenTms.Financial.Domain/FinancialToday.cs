using System;
using Microsoft.Extensions.Options;
using OpenTms.Platform.NumberSequences;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Timing;

namespace OpenTms.Financial;

/// <summary>
/// Today's date as the validity rows read it (design 52, §3.4: rows dated today or earlier are fixed): the date in the
/// installation's local time zone (<c>NumberSequences:TimeZone</c>, Europe/Amsterdam by default; design 50 Assumption 2),
/// the same rule as the number sequences, so a rate fixes at Dutch midnight, not UTC midnight.
/// </summary>
public class FinancialToday : ITransientDependency
{
    private readonly IClock _clock;
    private readonly NumberSequenceOptions _options;

    public FinancialToday(IClock clock, IOptions<NumberSequenceOptions> options)
    {
        _clock = clock;
        _options = options.Value;
    }

    public virtual DateOnly Get()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZone);
        var utc = _clock.Now.Kind == DateTimeKind.Local ? _clock.Now.ToUniversalTime() : DateTime.SpecifyKind(_clock.Now, DateTimeKind.Utc);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));
    }
}
