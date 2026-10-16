using System.Collections.Generic;
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
    /// <summary>
    /// Locks the sequence rows of the current tenant until the unit of work commits (design 49, "Guards"): <c>FOR UPDATE</c>
    /// for an edit or delete, which then checks "in use"; <c>FOR SHARE</c> for a draw, which then reads the format. An edit
    /// therefore waits for running draws to commit and sees their counter rows, and a draw never uses a format whose in-use
    /// check missed it. PostgreSQL only; other providers (SQLite unit tests, no transactions) skip it.
    /// </summary>
    Task LockSequencesAsync(Guid? tenantId, IReadOnlyCollection<Guid> numberSequenceIds, bool exclusive, CancellationToken cancellationToken = default);

    Task<long> IncrementAsync(Guid? tenantId, Guid numberSequenceId, int periodYear, int periodMonth, CancellationToken cancellationToken = default);
}
