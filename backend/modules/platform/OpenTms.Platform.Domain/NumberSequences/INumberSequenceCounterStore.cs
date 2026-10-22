using System;
using System.Threading.Tasks;

namespace OpenTms.Platform.NumberSequences;

/// <summary>The two atomic statements behind the generator; implemented over the Platform DbContext.</summary>
public interface INumberSequenceCounterStore
{
    /// <summary>Increments the counter of (current tenant, key, period), creating it at 1, and returns the new value.</summary>
    Task<long> IncrementAsync(string sequenceKey, string period);

    /// <summary>The last issued value of (current tenant, key, period), 0 when none was issued.</summary>
    Task<long> GetLastValueAsync(string sequenceKey, string period);

    /// <summary>Raises the last issued value to <paramref name="lastValue"/>; never lowers it. Returns false when it was higher.</summary>
    Task<bool> RaiseLastValueAsync(string sequenceKey, string period, long lastValue);
}
