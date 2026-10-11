using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Maintenance.Caching;
using OpenTms.Platform.Permissions;
using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace OpenTms.Platform.Administration.Maintenance;

/* The Caches tab. Host only: the cache is process-wide and holds every tenant's entries. The entry
 * list is this instance's; it is paged in memory because it only ever exists in memory. */
[RemoteService(IsEnabled = false)]
[Authorize(PlatformPermissions.Administration.Maintenance.Caches)]
public class CacheMaintenanceAppService : MaintenanceAppServiceBase, ICacheMaintenanceAppService
{
    private readonly ICacheMaintenance _cacheMaintenance;

    public CacheMaintenanceAppService(ICacheMaintenance cacheMaintenance)
    {
        _cacheMaintenance = cacheMaintenance;
    }

    public async Task<CacheStatusDto> GetStatusAsync()
    {
        EnsureHostSide();
        var status = await _cacheMaintenance.GetStatusAsync();
        return new CacheStatusDto
        {
            ProviderName = status.ProviderName,
            IsPerInstance = status.IsPerInstance,
            EntryCount = status.EntryCount,
            KeyPrefix = status.KeyPrefix,
            InstanceName = Environment.MachineName
        };
    }

    public async Task<List<CacheNameDto>> GetNamesAsync()
    {
        EnsureHostSide();
        var entries = await _cacheMaintenance.GetEntriesAsync();
        return entries
            .GroupBy(e => ParseKey(e.Key).CacheName, StringComparer.Ordinal)
            .Select(g => new CacheNameDto { Name = g.Key, EntryCount = g.Count(), SizeBytes = g.Sum(e => e.SizeBytes) })
            .OrderByDescending(n => n.EntryCount)
            .ThenBy(n => n.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<GridResponse<CacheEntryRowDto>> GetEntriesAsync(GridRequest request, string? cacheName)
    {
        EnsureHostSide();
        var entries = await _cacheMaintenance.GetEntriesAsync();
        IReadOnlyList<CacheEntryRowDto> all = entries.Select(ToRow).OrderBy(r => r.CacheName, StringComparer.Ordinal).ThenBy(r => r.ItemKey, StringComparer.Ordinal).ToList();

        IReadOnlyList<CacheEntryRowDto> filtered = all;
        if (!string.IsNullOrWhiteSpace(cacheName))
        {
            filtered = filtered.Where(r => string.Equals(r.CacheName, cacheName, StringComparison.Ordinal)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(request.WildcardSearch))
        {
            var search = request.WildcardSearch.Trim();
            filtered = filtered.Where(r => Matches(r.Key, search)).ToList();
        }

        var sort = request.SortModels.FirstOrDefault();
        if (sort != null)
        {
            filtered = sort.ColId switch
            {
                "cacheName" => Sort(filtered, r => r.CacheName, sort.Sort),
                "itemKey" => Sort(filtered, r => r.ItemKey, sort.Sort),
                "sizeBytes" => Sort(filtered, r => r.SizeBytes, sort.Sort),
                "createdAt" => Sort(filtered, r => r.CreatedAt, sort.Sort),
                "absoluteExpiresAt" => Sort(filtered, r => r.AbsoluteExpiresAt ?? DateTime.MaxValue, sort.Sort),
                _ => filtered
            };
        }

        return Page(all, filtered, request);
    }

    [Authorize(PlatformPermissions.Administration.Maintenance.CachesClear)]
    public async Task<CacheClearResultDto> ClearAsync()
    {
        EnsureHostSide();
        var cleared = await _cacheMaintenance.ClearAsync();
        Logger.LogInformation("Application cache cleared by user {UserId}: {Count} entries.", CurrentUser.Id, cleared);
        return new CacheClearResultDto { ClearedEntryCount = cleared, InstanceName = Environment.MachineName };
    }

    [Authorize(PlatformPermissions.Administration.Maintenance.CachesClear)]
    public async Task RemoveEntryAsync(string key)
    {
        EnsureHostSide();
        if (!await _cacheMaintenance.RemoveAsync(key))
        {
            throw new EntityNotFoundException(typeof(CacheEntryRowDto), key);
        }

        /* The key may name a user or tenant id; both are fine in a log line (constitution rule 5). */
        Logger.LogInformation("Cache entry {CacheKey} removed by user {UserId}.", key, CurrentUser.Id);
    }

    /// <summary>
    /// ABP stores an item under "{prefix}c:{CacheName},k:{ItemKey}". Keys in another shape (a
    /// library caching on its own) keep the whole key as their name.
    /// </summary>
    internal static (string CacheName, string ItemKey) ParseKey(string key)
    {
        var nameStart = key.IndexOf("c:", StringComparison.Ordinal);
        var keyStart = key.IndexOf(",k:", StringComparison.Ordinal);
        if (nameStart < 0 || keyStart < nameStart)
        {
            return (key, string.Empty);
        }

        return (key.Substring(nameStart + 2, keyStart - nameStart - 2), key[(keyStart + 3)..]);
    }

    private static CacheEntryRowDto ToRow(CacheEntryInfo info)
    {
        var (cacheName, itemKey) = ParseKey(info.Key);
        return new CacheEntryRowDto
        {
            Key = info.Key,
            CacheName = cacheName,
            ItemKey = itemKey,
            SizeBytes = info.SizeBytes,
            CreatedAt = info.CreatedAt,
            AbsoluteExpiresAt = info.AbsoluteExpiresAt,
            SlidingExpiration = info.SlidingExpiration
        };
    }
}
