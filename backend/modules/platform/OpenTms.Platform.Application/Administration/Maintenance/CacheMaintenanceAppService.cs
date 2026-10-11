using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.Platform.Maintenance.Caching;
using OpenTms.Platform.Permissions;
using Volo.Abp;

namespace OpenTms.Platform.Administration.Maintenance;

/* The Caches tab. Host only: the cache is process-wide and holds every tenant's entries. */
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

    [Authorize(PlatformPermissions.Administration.Maintenance.CachesClear)]
    public async Task<CacheClearResultDto> ClearAsync()
    {
        EnsureHostSide();
        var cleared = await _cacheMaintenance.ClearAsync();
        Logger.LogInformation("Application cache cleared by user {UserId}: {Count} entries.", CurrentUser.Id, cleared);
        return new CacheClearResultDto { ClearedEntryCount = cleared, InstanceName = Environment.MachineName };
    }
}
