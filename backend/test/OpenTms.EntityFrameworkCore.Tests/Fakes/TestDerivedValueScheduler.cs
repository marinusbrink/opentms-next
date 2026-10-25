using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.NumberSequences;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Fakes;

/// <summary>
/// The SQLite test module runs without database transactions (<c>AddAlwaysDisableUnitOfWorkTransaction</c>); design 49's
/// scheduler and design 50's number generator refuse to work outside one. The test doubles below replace them with
/// in-memory behaviour when the default connection is SQLite, and delegate to the real services on PostgreSQL, where the
/// outbox, version and gapless-number semantics stay under test.
/// </summary>
public static class TestDatabase
{
    public static bool IsSqlite(IOptions<AbpDbConnectionOptions> options)
    {
        var connection = options.Value.ConnectionStrings.Default ?? string.Empty;
        return !connection.StartsWith("Host=", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Every derived-value request a service made during a SQLite test, by tenant (the Planning service tests assert on them).</summary>
public class RecordedDerivedValueRequests : ISingletonDependency
{
    private readonly ConcurrentQueue<(Guid? TenantId, string TargetType, Guid TargetId, string? Reason)> _requests = new();
    private long _version;

    public IReadOnlyList<(Guid? TenantId, string TargetType, Guid TargetId, string? Reason)> All => _requests.ToList();

    public IReadOnlyList<(Guid? TenantId, string TargetType, Guid TargetId, string? Reason)> For(string targetType, Guid targetId)
        => _requests.Where(r => r.TargetType == targetType && r.TargetId == targetId).ToList();

    public long Record(Guid? tenantId, string targetType, Guid targetId, string? reason)
    {
        _requests.Enqueue((tenantId, targetType, targetId, reason));
        return System.Threading.Interlocked.Increment(ref _version);
    }
}

[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IDerivedValueScheduler))]
public class TestDerivedValueScheduler : IDerivedValueScheduler, ITransientDependency
{
    private readonly DerivedValueScheduler _real;
    private readonly RecordedDerivedValueRequests _recorded;
    private readonly ICurrentTenant _currentTenant;
    private readonly bool _sqlite;

    public TestDerivedValueScheduler(DerivedValueScheduler real, RecordedDerivedValueRequests recorded, ICurrentTenant currentTenant, IOptions<AbpDbConnectionOptions> connections)
    {
        _real = real;
        _recorded = recorded;
        _currentTenant = currentTenant;
        _sqlite = TestDatabase.IsSqlite(connections);
    }

    public Task<long> RequestAsync(string targetType, Guid targetId, string? reason = null)
        => _sqlite
            ? Task.FromResult(_recorded.Record(_currentTenant.Id, targetType, targetId, reason))
            : _real.RequestAsync(targetType, targetId, reason);

    public Task<int> RequestManyAsync(string targetType, IReadOnlyCollection<Guid> targetIds, string? reason = null)
    {
        if (!_sqlite)
        {
            return _real.RequestManyAsync(targetType, targetIds, reason);
        }

        var ids = targetIds.Distinct().ToList();
        foreach (var id in ids)
        {
            _recorded.Record(_currentTenant.Id, targetType, id, reason);
        }

        return Task.FromResult(ids.Count);
    }
}

/// <summary>In-memory gapless counters per tenant, key and period on SQLite; the real generator on PostgreSQL.</summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(INumberSequenceGenerator))]
public class TestNumberSequenceGenerator : INumberSequenceGenerator, ITransientDependency
{
    private static readonly ConcurrentDictionary<(Guid? TenantId, string Key, string Period), long> Counters = new();

    private readonly NumberSequenceGenerator _real;
    private readonly ICurrentTenant _currentTenant;
    private readonly bool _sqlite;

    public TestNumberSequenceGenerator(NumberSequenceGenerator real, ICurrentTenant currentTenant, IOptions<AbpDbConnectionOptions> connections)
    {
        _real = real;
        _currentTenant = currentTenant;
        _sqlite = TestDatabase.IsSqlite(connections);
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public Task<string> NextAsync(string sequenceKey, NumberSequenceFormat format)
    {
        if (!_sqlite)
        {
            return _real.NextAsync(sequenceKey, format);
        }

        var value = Counters.AddOrUpdate((_currentTenant.Id, sequenceKey, format.Period(Today)), 1, (_, current) => current + 1);
        return Task.FromResult(format.Format(value, Today));
    }

    public Task<long> PeekNextValueAsync(string sequenceKey, NumberSequenceFormat format)
        => _sqlite && Counters.TryGetValue((_currentTenant.Id, sequenceKey, format.Period(Today)), out var value)
            ? Task.FromResult(value + 1)
            : _real.PeekNextValueAsync(sequenceKey, format);

    public async Task<string> PeekNextAsync(string sequenceKey, NumberSequenceFormat format)
        => _sqlite ? format.Format(await PeekNextValueAsync(sequenceKey, format), Today) : await _real.PeekNextAsync(sequenceKey, format);

    public Task SetNextValueAsync(string sequenceKey, NumberSequenceFormat format, long nextValue) => _real.SetNextValueAsync(sequenceKey, format, nextValue);
}
