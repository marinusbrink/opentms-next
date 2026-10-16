using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.Platform.NumberSequences;

public interface INumberSequenceCounterRepository : IRepository<NumberSequenceCounter>
{
    /// <summary>
    /// Draws the next counter value of one period in one statement (design 49): inserts the period's row with 1, or adds 1
    /// to it, and returns the new value. Runs in the current unit of work's transaction; the row lock serialises
    /// concurrent callers until commit, and a rollback gives the value back. The caller has read the sequence through the
    /// tenant-filtered repository first, so <paramref name="numberSequenceId"/> is a row of the current tenant.
    /// </summary>
    Task<long> IncrementAsync(Guid? tenantId, Guid numberSequenceId, int periodYear, int periodMonth, CancellationToken cancellationToken = default);
}
