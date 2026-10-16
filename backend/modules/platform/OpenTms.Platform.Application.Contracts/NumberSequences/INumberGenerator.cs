using System;
using System.Threading.Tasks;

namespace OpenTms.Platform.NumberSequences;

/// <summary>
/// The published number generator (design 49): draws the next number of a number sequence for a record dated
/// <c>date</c>. Not an application service (no HTTP endpoint) and not feature-gated: other modules' services call it
/// while they create an order, trip, invoice or purchase order.
/// <para>
/// Runs in the caller's unit of work, which must be transactional
/// (<see cref="NumberGenerationRequiresTransactionException"/> otherwise): the counter row stays locked until the
/// caller commits, so concurrent callers never get the same number, and a rollback gives the number back. The period
/// comes from <c>date</c> (the record's date), not from the clock. A sequence id that is not a row of the current
/// tenant throws <c>EntityNotFoundException</c>.
/// </para>
/// </summary>
public interface INumberGenerator
{
    Task<string> GetNextAsync(Guid numberSequenceId, DateOnly date);
}
