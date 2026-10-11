using System;
using System.Linq;
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

    [Fact]
    public void Entries_record_key_size_and_expiry_and_follow_set_replace_remove_and_clear()
    {
        _cache.Set("a", Bytes("12345"), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
        _cache.Set("b", Bytes("1"), new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(1) });

        var a = _cache.Entries.Single(e => e.Key == "a");
        a.SizeBytes.ShouldBe(5);
        a.AbsoluteExpiresAt.ShouldNotBeNull();
        a.AbsoluteExpiresAt.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(9));
        a.SlidingExpiration.ShouldBeNull();
        _cache.Entries.Single(e => e.Key == "b").SlidingExpiration.ShouldBe(TimeSpan.FromMinutes(1));

        /* A replaced entry keeps exactly one record, the newer one. */
        _cache.Set("a", Bytes("1234567"), new DistributedCacheEntryOptions());
        _cache.Entries.Count(e => e.Key == "a").ShouldBe(1);
        _cache.Entries.Single(e => e.Key == "a").SizeBytes.ShouldBe(7);

        _cache.Remove("b");
        _cache.Entries.Select(e => e.Key).ShouldBe(["a"]);

        _cache.Clear();
        _cache.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_expired_entry_leaves_the_entry_list_once_the_cache_drops_it()
    {
        _cache.Set("short", Bytes("1"), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(30) });
        await Task.Delay(120);

        _cache.Get("short").ShouldBeNull(); // the read evicts the expired entry
        await Task.Delay(50); // the eviction callback runs on the thread pool
        _cache.Entries.ShouldNotContain(e => e.Key == "short");
    }

    public void Dispose() => _cache.Dispose();
}
