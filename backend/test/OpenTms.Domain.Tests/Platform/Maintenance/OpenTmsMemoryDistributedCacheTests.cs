using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using OpenTms.Platform.Maintenance.Caching;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.Maintenance;

/// <summary>
/// Unit tests for the instance cache the Platform module registers in place of the framework one.
/// Risk class: High — every permission, setting and feature lookup in the process goes through it;
/// a wrong expiration or a key mix-up would be a correctness bug for every tenant.
/// </summary>
public class OpenTmsMemoryDistributedCacheTests : IDisposable
{
    private readonly OpenTmsMemoryDistributedCache _cache = new();

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public async Task Set_get_remove_round_trip_and_count_follow_the_entries()
    {
        _cache.Count.ShouldBe(0);

        await _cache.SetAsync("a", Bytes("1"), new DistributedCacheEntryOptions());
        _cache.Set("b", Bytes("2"), new DistributedCacheEntryOptions());
        _cache.Count.ShouldBe(2);

        (await _cache.GetAsync("a")).ShouldBe(Bytes("1"));
        _cache.Get("b").ShouldBe(Bytes("2"));
        _cache.Get("missing").ShouldBeNull();

        await _cache.RemoveAsync("a");
        _cache.Get("a").ShouldBeNull();
        _cache.Count.ShouldBe(1);
    }

    [Fact]
    public void Clear_removes_every_entry()
    {
        _cache.Set("a", Bytes("1"), new DistributedCacheEntryOptions());
        _cache.Set("b", Bytes("2"), new DistributedCacheEntryOptions());

        _cache.Clear();

        _cache.Count.ShouldBe(0);
        _cache.Get("a").ShouldBeNull();
    }

    [Fact]
    public async Task An_entry_with_an_elapsed_absolute_expiration_is_gone()
    {
        _cache.Set("short", Bytes("1"), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(30) });
        _cache.Set("long", Bytes("2"), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });

        await Task.Delay(120);

        _cache.Get("short").ShouldBeNull();
        _cache.Get("long").ShouldBe(Bytes("2"));
    }

    [Fact]
    public void Refresh_and_remove_of_an_unknown_key_do_not_throw()
    {
        Should.NotThrow(() => _cache.Refresh("missing"));
        Should.NotThrow(() => _cache.Remove("missing"));
    }

    public void Dispose() => _cache.Dispose();
}
