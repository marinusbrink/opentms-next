using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Volo.Abp.Caching;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;

namespace OpenTms.Platform.Tenants;

/// <summary>The edition a tenant is on, as the feature checker needs it on every call.</summary>
public interface ITenantEditionStore
{
    Task<Guid?> GetEditionIdAsync(Guid tenantId);

    /// <summary>Forgets the cached edition of a tenant; call after changing it.</summary>
    Task RemoveAsync(Guid tenantId);
}

/* Keyed by tenant id and read from any context: without IgnoreMultiTenancy ABP prefixes the key with the current
 * tenant, so the entry a feature check caches inside the tenant would never be removed by the host-side update. */
[CacheName("Platform.TenantEdition")]
[IgnoreMultiTenancy]
public class TenantEditionCacheItem
{
    public Guid? EditionId { get; set; }
}

/// <summary>
/// Reads <c>AbpTenants.EditionId</c> in the host database and caches it per tenant for ten minutes, so a
/// feature check costs a cache hit. The cache is per instance today (AP-13); another instance sees an
/// edition change within the expiry.
/// </summary>
public class TenantEditionStore : ITenantEditionStore, ITransientDependency
{
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(10);

    private readonly IDistributedCache<TenantEditionCacheItem, Guid> _cache;
    private readonly ITenantRepository _tenantRepository;
    private readonly ICurrentTenant _currentTenant;

    public TenantEditionStore(
        IDistributedCache<TenantEditionCacheItem, Guid> cache,
        ITenantRepository tenantRepository,
        ICurrentTenant currentTenant)
    {
        _cache = cache;
        _tenantRepository = tenantRepository;
        _currentTenant = currentTenant;
    }

    public async Task<Guid?> GetEditionIdAsync(Guid tenantId)
    {
        var item = await _cache.GetOrAddAsync(
            tenantId,
            async () =>
            {
                /* The tenant store is host data: read it in host context, whatever tenant is current. */
                using (_currentTenant.Change(null))
                {
                    var tenant = await _tenantRepository.FindAsync(tenantId, includeDetails: false);
                    return new TenantEditionCacheItem { EditionId = tenant?.GetProperty<Guid?>(TenantProperties.EditionId) };
                }
            },
            () => new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Expiry });

        return item?.EditionId;
    }

    public Task RemoveAsync(Guid tenantId) => _cache.RemoveAsync(tenantId);
}
