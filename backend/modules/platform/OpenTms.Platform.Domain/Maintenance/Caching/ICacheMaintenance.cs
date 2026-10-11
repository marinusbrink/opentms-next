using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTms.Platform.Maintenance.Caching;

/// <summary>What the maintenance view knows about the application cache.</summary>
/// <param name="ProviderName">A short name of the cache implementation, e.g. "InMemory".</param>
/// <param name="IsPerInstance">True when the cache lives inside one process: clearing it clears this instance only.</param>
/// <param name="EntryCount">The number of entries, when the implementation can count them.</param>
/// <param name="KeyPrefix">ABP's configured cache key prefix, if any.</param>
public sealed record CacheStatus(string ProviderName, bool IsPerInstance, int? EntryCount, string? KeyPrefix);

/// <summary>
/// The port behind the Caches tab. The in-memory implementation is the default; a distributed
/// cache (proposal AP-13) replaces it with one that clears the shared store.
/// </summary>
public interface ICacheMaintenance
{
    Task<CacheStatus> GetStatusAsync();

    /// <summary>The entries held right now, when the implementation can list them; otherwise empty.</summary>
    Task<IReadOnlyList<CacheEntryInfo>> GetEntriesAsync();

    /// <summary>Removes every cached entry. Returns the number of entries removed when known.</summary>
    Task<int?> ClearAsync();

    /// <summary>Removes one entry by its full key. Returns false when there was no such entry.</summary>
    Task<bool> RemoveAsync(string key);
}
