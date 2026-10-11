using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace OpenTms.Platform.Maintenance.Caching;

/// <summary>
/// The in-memory <see cref="IDistributedCache"/> of one instance, with what the framework's own
/// <c>MemoryDistributedCache</c> keeps private: the number of entries and a way to clear them all.
/// The Platform module registers it in place of the framework one (see
/// <c>PlatformDomainModule</c>). It is per instance: clearing it clears this process only, which is
/// exactly what the Maintenance view says until a distributed cache arrives (proposal AP-13).
/// </summary>
public sealed class OpenTmsMemoryDistributedCache : IDistributedCache, IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    /// <summary>The number of entries held right now (expired entries are dropped lazily).</summary>
    public int Count => _cache.Count;

    /// <summary>Removes every entry at once.</summary>
    public void Clear() => _cache.Clear();

    public byte[]? Get(string key) => _cache.Get<byte[]>(key);

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);

        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = options.AbsoluteExpiration,
            AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow,
            SlidingExpiration = options.SlidingExpiration
        };
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

    public void Remove(string key) => _cache.Remove(key);

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        Remove(key);
        return Task.CompletedTask;
    }

    public void Dispose() => _cache.Dispose();
}
