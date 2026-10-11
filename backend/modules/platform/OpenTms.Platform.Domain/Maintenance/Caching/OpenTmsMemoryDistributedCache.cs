using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace OpenTms.Platform.Maintenance.Caching;

/// <summary>What the cache knows about one entry: the key and when and how it was stored.</summary>
public sealed record CacheEntryInfo(string Key, long SizeBytes, DateTime CreatedAt, DateTime? AbsoluteExpiresAt, TimeSpan? SlidingExpiration);

/// <summary>
/// The in-memory <see cref="IDistributedCache"/> of one instance, with what the framework's own
/// <c>MemoryDistributedCache</c> keeps private: the entries, their count and a way to clear them
/// all. The Platform module registers it in place of the framework one (see
/// <c>PlatformDomainModule</c>). It is per instance: clearing it clears this process only, which is
/// exactly what the Maintenance view says until a distributed cache arrives (proposal AP-13).
/// </summary>
public sealed class OpenTmsMemoryDistributedCache : IDistributedCache, IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    /* The entries as the Maintenance view lists them. MemoryCache keeps no per-entry metadata we
     * can read back, so every Set records it here and the eviction callback removes it again. */
    private readonly ConcurrentDictionary<string, CacheEntryInfo> _entries = new(StringComparer.Ordinal);

    /// <summary>The number of entries held right now (expired entries are dropped lazily).</summary>
    public int Count => _cache.Count;

    /// <summary>A snapshot of the entries held right now.</summary>
    public IReadOnlyList<CacheEntryInfo> Entries => _entries.Values.ToList();

    /// <summary>Removes every entry at once.</summary>
    public void Clear()
    {
        _cache.Clear();
        _entries.Clear();
    }

    public byte[]? Get(string key) => _cache.Get<byte[]>(key);

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);

        var now = DateTime.UtcNow;
        var absolute = options.AbsoluteExpiration?.UtcDateTime
                       ?? (options.AbsoluteExpirationRelativeToNow.HasValue ? now + options.AbsoluteExpirationRelativeToNow.Value : null);
        var info = new CacheEntryInfo(key, value.LongLength, now, absolute, options.SlidingExpiration);

        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = options.AbsoluteExpiration,
            AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow,
            SlidingExpiration = options.SlidingExpiration
        };
        /* Remove the record only if it is still the one for the evicted entry: an entry that was
         * replaced by a newer Set is evicted too, and must not take the newer record with it. */
        entryOptions.RegisterPostEvictionCallback(
            static (evictedKey, _, _, state) =>
            {
                var (entries, evicted) = ((ConcurrentDictionary<string, CacheEntryInfo>, CacheEntryInfo))state!;
                ((ICollection<KeyValuePair<string, CacheEntryInfo>>)entries).Remove(new KeyValuePair<string, CacheEntryInfo>((string)evictedKey, evicted));
            },
            (_entries, info));

        _entries[key] = info;
        _cache.Set(key, value, entryOptions);
    }

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        Set(key, value, options);
        return Task.CompletedTask;
    }

    /// <summary>A read restarts a sliding expiration, which is all a refresh has to do here.</summary>
    public void Refresh(string key) => _cache.TryGetValue(key, out _);

    public Task RefreshAsync(string key, CancellationToken token = default)
    {
        Refresh(key);
        return Task.CompletedTask;
    }

    public void Remove(string key)
    {
        _cache.Remove(key);
        _entries.TryRemove(key, out _);
    }

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        Remove(key);
        return Task.CompletedTask;
    }

    public void Dispose() => _cache.Dispose();
}
