using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Platform.Maintenance.Caching;

/// <summary>
/// <see cref="ICacheMaintenance"/> over whatever <see cref="IDistributedCache"/> is registered. It
/// can count and clear only the <see cref="OpenTmsMemoryDistributedCache"/>; any other provider is
/// reported by name and refuses to clear, so a future distributed cache has to bring its own
/// implementation instead of silently doing nothing.
/// </summary>
public class MemoryCacheMaintenance : ICacheMaintenance, ITransientDependency
{
    private readonly IDistributedCache _distributedCache;
    private readonly AbpDistributedCacheOptions _options;

    public MemoryCacheMaintenance(IDistributedCache distributedCache, IOptions<AbpDistributedCacheOptions> options)
    {
        _distributedCache = distributedCache;
        _options = options.Value;
    }

    public Task<CacheStatus> GetStatusAsync()
    {
        var keyPrefix = string.IsNullOrEmpty(_options.KeyPrefix) ? null : _options.KeyPrefix;

        if (_distributedCache is OpenTmsMemoryDistributedCache memoryCache)
        {
            return Task.FromResult(new CacheStatus("InMemory", IsPerInstance: true, memoryCache.Count, keyPrefix));
        }

        return Task.FromResult(new CacheStatus(_distributedCache.GetType().Name, IsPerInstance: false, EntryCount: null, keyPrefix));
    }

    public Task<int?> ClearAsync()
    {
        if (_distributedCache is not OpenTmsMemoryDistributedCache memoryCache)
        {
            throw new NotSupportedException(
                $"Clearing the cache provider '{_distributedCache.GetType().Name}' is not supported; register an ICacheMaintenance for it.");
        }

        var count = memoryCache.Count;
        memoryCache.Clear();
        return Task.FromResult<int?>(count);
    }
}
