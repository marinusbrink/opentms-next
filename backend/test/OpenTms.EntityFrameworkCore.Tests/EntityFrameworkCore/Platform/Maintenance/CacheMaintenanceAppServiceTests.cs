using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using OpenTms.Platform.Administration.Maintenance;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Maintenance.Caching;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
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
    public async Task Entries_are_listed_per_named_cache_with_the_abp_key_parsed_filtered_and_removable()
    {
        var marker = Guid.NewGuid().ToString("N");
        var cacheName = "MaintenanceProbe_" + marker;
        await _cache.SetAsync($"c:{cacheName},k:item-1", Encoding.UTF8.GetBytes("abc"), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });
        await _cache.SetAsync($"c:{cacheName},k:item-2", Encoding.UTF8.GetBytes("abcdef"), new DistributedCacheEntryOptions());
        await _cache.SetAsync("plain-" + marker, Encoding.UTF8.GetBytes("x"), new DistributedCacheEntryOptions());

        var names = await _service.GetNamesAsync();
        var probe = names.Single(n => n.Name == cacheName);
        probe.EntryCount.ShouldBe(2);
        probe.SizeBytes.ShouldBe(9);
        names.ShouldContain(n => n.Name == "plain-" + marker && n.EntryCount == 1);

        var page = await _service.GetEntriesAsync(new GridRequest { StartRow = 0, EndRow = 50 }, cacheName);
        page.FilteredCount.ShouldBe(2);
        page.TotalCount.ShouldBeGreaterThanOrEqualTo(3);
        var first = page.Rows.Single(r => r.ItemKey == "item-1");
        first.Key.ShouldBe($"c:{cacheName},k:item-1");
        first.CacheName.ShouldBe(cacheName);
        first.SizeBytes.ShouldBe(3);
        first.AbsoluteExpiresAt.ShouldNotBeNull();
        page.Rows.Single(r => r.ItemKey == "item-2").AbsoluteExpiresAt.ShouldBeNull();

        var searched = await _service.GetEntriesAsync(new GridRequest { StartRow = 0, EndRow = 50, WildcardSearch = "item-2" }, null);
        searched.Rows.ShouldHaveSingleItem().ItemKey.ShouldBe("item-2");

        var sorted = await _service.GetEntriesAsync(new GridRequest { StartRow = 0, EndRow = 50, SortModels = [new SortModel("sizeBytes", "desc")] }, cacheName);
        sorted.Rows.Select(r => r.SizeBytes).ShouldBe([6, 3]);

        await _service.RemoveEntryAsync($"c:{cacheName},k:item-1");
        (await _cache.GetAsync($"c:{cacheName},k:item-1")).ShouldBeNull();
        (await _service.GetEntriesAsync(new GridRequest { StartRow = 0, EndRow = 50 }, cacheName)).FilteredCount.ShouldBe(1);

        await Should.ThrowAsync<EntityNotFoundException>(() => _service.RemoveEntryAsync("c:nothing,k:here"));
    }

    [Fact]
    public async Task A_tenant_can_neither_read_nor_clear_the_cache()
    {
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetStatusAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetNamesAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetEntriesAsync(new GridRequest { StartRow = 0, EndRow = 50 }, null));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.ClearAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.RemoveEntryAsync("c:x,k:y"));
        }
    }
}
