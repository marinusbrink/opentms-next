using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace OpenTms.Platform.NumberSequences;

public class NumberSequenceGenerator : INumberSequenceGenerator, ITransientDependency
{
    private readonly INumberSequenceCounterStore _store;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IClock _clock;
    private readonly NumberSequenceOptions _options;

    public NumberSequenceGenerator(
        INumberSequenceCounterStore store,
        IUnitOfWorkManager unitOfWorkManager,
        IClock clock,
        IOptions<NumberSequenceOptions> options)
    {
        _store = store;
        _unitOfWorkManager = unitOfWorkManager;
        _clock = clock;
        _options = options.Value;
    }

    public virtual async Task<string> NextAsync(string sequenceKey, NumberSequenceFormat format)
    {
        CheckKey(sequenceKey);
        Check.NotNull(format, nameof(format));

        /* Gapless only holds when the counter commits or rolls back with the fact (design 50, Assumption 1). */
        if (_unitOfWorkManager.Current?.Options.IsTransactional != true)
        {
            throw new BusinessException(ReferenceDataErrorCodes.RequiresTransaction);
        }

        var today = LocalToday();
        var value = await _store.IncrementAsync(sequenceKey, format.Period(today));
        return format.Format(value, today);
    }

    public virtual async Task<long> PeekNextValueAsync(string sequenceKey, NumberSequenceFormat format)
    {
        CheckKey(sequenceKey);
        return await _store.GetLastValueAsync(sequenceKey, format.Period(LocalToday())) + 1;
    }

    public virtual async Task<string> PeekNextAsync(string sequenceKey, NumberSequenceFormat format)
        => format.Format(await PeekNextValueAsync(sequenceKey, format), LocalToday());

    public virtual async Task SetNextValueAsync(string sequenceKey, NumberSequenceFormat format, long nextValue)
    {
        CheckKey(sequenceKey);
        var period = format.Period(LocalToday());
        var last = await _store.GetLastValueAsync(sequenceKey, period);
        if (nextValue <= last)
        {
            throw new BusinessException(ReferenceDataErrorCodes.NextNumberTooLow).WithData("minimum", last + 1);
        }

        if (!await _store.RaiseLastValueAsync(sequenceKey, period, nextValue - 1))
        {
            throw new BusinessException(ReferenceDataErrorCodes.NextNumberTooLow).WithData("minimum", last + 1);
        }
    }

    /// <summary>The date in the configured time zone (Europe/Amsterdam by default): the year turns at Dutch midnight.</summary>
    protected virtual DateOnly LocalToday()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZone);
        var utc = _clock.Now.Kind == DateTimeKind.Local ? _clock.Now.ToUniversalTime() : DateTime.SpecifyKind(_clock.Now, DateTimeKind.Utc);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));
    }

    private static void CheckKey(string sequenceKey)
        => Check.NotNullOrWhiteSpace(sequenceKey, nameof(sequenceKey), NumberSequenceCounter.MaxSequenceKeyLength);
}
