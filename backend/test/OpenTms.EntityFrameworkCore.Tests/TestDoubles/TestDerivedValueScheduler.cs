using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OpenTms.Platform.DerivedValues;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace OpenTms.TestDoubles;

/// <summary>
/// The SQLite test module runs without database transactions (<c>AddAlwaysDisableUnitOfWorkTransaction</c>); design 49's
/// scheduler and design 50's number generator refuse to work outside one. The test doubles of this folder replace them
/// with in-memory behaviour when the default connection is SQLite, and delegate to the real services on PostgreSQL, where
/// the outbox, version and gapless-number semantics stay under test. One pair for every module's tests.
/// </summary>
public static class TestDatabase
{
    public static bool IsSqlite(IOptions<AbpDbConnectionOptions> options)
    {
        var connection = options.Value.ConnectionStrings.Default ?? string.Empty;
        return !connection.StartsWith("Host=", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Every derived-value request a service made during a SQLite test, by tenant; the service tests assert on them.</summary>
public class RecordedDerivedValueRequests : ISingletonDependency
{
    private readonly ConcurrentQueue<(Guid? TenantId, string TargetType, Guid TargetId, string? Reason)> _requests = new();
    private long _version;

    public IReadOnlyList<(Guid? TenantId, string TargetType, Guid TargetId, string? Reason)> All => _requests.ToList();

    public IReadOnlyList<(Guid? TenantId, string TargetType, Guid TargetId, string? Reason)> For(string targetType, Guid targetId)
        => _requests.Where(r => r.TargetType == targetType && r.TargetId == targetId).ToList();

    /// <summary>The reasons requested for one record of one target type, in order.</summary>
    public IReadOnlyList<string> ReasonsFor(string targetType, Guid targetId)
        => For(targetType, targetId).Select(r => r.Reason ?? string.Empty).ToList();

    public long Record(Guid? tenantId, string targetType, Guid targetId, string? reason)
    {
        _requests.Enqueue((tenantId, targetType, targetId, reason));
        return Interlocked.Increment(ref _version);
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
