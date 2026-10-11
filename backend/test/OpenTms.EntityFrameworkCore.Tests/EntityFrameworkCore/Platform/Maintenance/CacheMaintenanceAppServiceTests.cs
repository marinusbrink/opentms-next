using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using OpenTms.Platform.Administration.Maintenance;
using OpenTms.Platform.Maintenance.Caching;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Maintenance;

/// <summary>
/// Integration tests for the Caches tab through the real DI graph: the IDistributedCache the
/// Platform module registers is the one the service counts and clears.
/// Risk class: Critical — clearing is host-only (the cache holds every tenant's entries);
/// High — the status must describe the real provider.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class CacheMaintenanceAppServiceTests : OpenTmsEntityFrameworkCoreTestBase
{
    private readonly ICacheMaintenanceAppService _service;
    private readonly IDistributedCache _cache;
    private readonly ICurrentTenant _currentTenant;

    public CacheMaintenanceAppServiceTests()
    {
        _service = GetRequiredService<ICacheMaintenanceAppService>();
        _cache = GetRequiredService<IDistributedCache>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    [Fact]
    public void The_registered_distributed_cache_is_the_clearable_instance_cache()
    {
        _cache.ShouldBeOfType<OpenTmsMemoryDistributedCache>();
    }

    [Fact]
    public async Task Status_reports_the_in_memory_provider_and_counts_entries()
    {
        var key = "maintenance-test:" + Guid.NewGuid();
        await _cache.SetAsync(key, Encoding.UTF8.GetBytes("x"), new DistributedCacheEntryOptions());

        var status = await _service.GetStatusAsync();

        status.ProviderName.ShouldBe("InMemory");
        status.IsPerInstance.ShouldBeTrue();
        status.EntryCount.ShouldNotBeNull();
        status.EntryCount.Value.ShouldBeGreaterThanOrEqualTo(1);
        status.InstanceName.ShouldBe(Environment.MachineName);
    }

    [Fact]
    public async Task Clear_removes_the_entries_and_reports_how_many()
    {
        var key = "maintenance-test:" + Guid.NewGuid();
        await _cache.SetAsync(key, Encoding.UTF8.GetBytes("x"), new DistributedCacheEntryOptions());

        var result = await _service.ClearAsync();

        result.ClearedEntryCount.ShouldNotBeNull();
        result.ClearedEntryCount.Value.ShouldBeGreaterThanOrEqualTo(1);
        (await _cache.GetAsync(key)).ShouldBeNull();
        (await _service.GetStatusAsync()).EntryCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_tenant_can_neither_read_nor_clear_the_cache()
    {
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetStatusAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.ClearAsync());
        }
    }
}
