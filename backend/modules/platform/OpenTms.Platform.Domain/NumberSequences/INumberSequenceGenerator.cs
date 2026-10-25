using System.Threading.Tasks;

namespace OpenTms.Platform.NumberSequences;

/// <summary>
/// The one number-sequence service of assessment §3.9 (design 50). Numbers are gapless per sequence and period: the
/// counter is taken inside the caller's transaction, so a rollback returns the number and two saves on one sequence
/// get consecutive numbers.
/// </summary>
public interface INumberSequenceGenerator
{
    /// <summary>The next number; refused outside a transactional unit of work.</summary>
    Task<string> NextAsync(string sequenceKey, NumberSequenceFormat format);

    /// <summary>The counter value the next number would get (no lock, nothing written).</summary>
    Task<long> PeekNextValueAsync(string sequenceKey, NumberSequenceFormat format);

    /// <summary>The number the next save would get, for the editor's preview.</summary>
    Task<string> PeekNextAsync(string sequenceKey, NumberSequenceFormat format);

    /// <summary>Continues numbering at <paramref name="nextValue"/> in the current period; only upwards.</summary>
    Task SetNextValueAsync(string sequenceKey, NumberSequenceFormat format, long nextValue);
}
