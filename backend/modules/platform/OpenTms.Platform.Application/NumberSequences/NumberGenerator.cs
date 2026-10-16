using Volo.Abp.MultiTenancy;
using System;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace OpenTms.Platform.NumberSequences;

/// <summary>
/// The published number generator (design 49, "Generation"): reads the sequence through the tenant-filtered repository
/// (a foreign or deleted id is EntityNotFound), derives the period from the record's date, draws the next counter value
/// with one atomic upsert in the caller's transaction and formats it. Logs nothing: generated numbers are not logged.
/// </summary>
public class NumberGenerator : INumberGenerator, ITransientDependency
{
    private readonly IRepository<NumberSequence, Guid> _sequences;
    private readonly INumberSequenceCounterRepository _counters;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentTenant _currentTenant;

    public NumberGenerator(
        IRepository<NumberSequence, Guid> sequences,
        INumberSequenceCounterRepository counters,
        IUnitOfWorkManager unitOfWorkManager,
        ICurrentTenant currentTenant)
    {
        _currentTenant = currentTenant;
        _sequences = sequences;
        _counters = counters;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public virtual async Task<string> GetNextAsync(Guid numberSequenceId, DateOnly date)
    {
        /* Like the outbox: without the caller's transaction the number would be committed apart from its record. */
        var unitOfWork = _unitOfWorkManager.Current;
        if (unitOfWork == null || !unitOfWork.Options.IsTransactional)
        {
            throw new NumberGenerationRequiresTransactionException();
        }

        /* Shared lock first, then the read: an edit of the format waits for this draw to commit, and this draw never uses a
         * format an edit is changing (design 49, "Guards"). */
        await _counters.LockSequencesAsync(_currentTenant.Id, [numberSequenceId], exclusive: false);
        var sequence = await _sequences.GetAsync(numberSequenceId);
        var format = sequence.Format;
        var (periodYear, periodMonth) = NumberSequenceFormatter.PeriodOf(format.Scheme, date);
        var value = await _counters.IncrementAsync(sequence.TenantId, sequence.Id, periodYear, periodMonth);
        return NumberSequenceFormatter.Format(format, date, value);
    }
}
