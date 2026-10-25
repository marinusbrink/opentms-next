using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Platform.DerivedValues;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.Fakes;

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

/// <summary>
/// Design 49's scheduler refuses a request outside a transactional unit of work. The SQLite tests run with transactions
/// disabled (<c>AddAlwaysDisableUnitOfWorkTransaction</c>), so there the requests are recorded instead of written; the
/// PostgreSQL tests keep the real scheduler (transactions on), so the outbox and version semantics stay under test.
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IDerivedValueScheduler))]
public class TestDerivedValueScheduler : IDerivedValueScheduler, ITransientDependency
{
    private readonly DerivedValueScheduler _real;
    private readonly RecordedDerivedValueRequests _recorded;
    private readonly ICurrentTenant _currentTenant;
    private readonly bool _transactionsDisabled;

    public TestDerivedValueScheduler(DerivedValueScheduler real, RecordedDerivedValueRequests recorded, ICurrentTenant currentTenant, Microsoft.Extensions.Options.IOptions<AbpUnitOfWorkDefaultOptions> unitOfWorkOptions)
    {
        _real = real;
        _recorded = recorded;
        _currentTenant = currentTenant;
        _transactionsDisabled = unitOfWorkOptions.Value.TransactionBehavior == UnitOfWorkTransactionBehavior.Disabled;
    }

    public Task<long> RequestAsync(string targetType, Guid targetId, string? reason = null)
        => _transactionsDisabled
            ? Task.FromResult(_recorded.Record(_currentTenant.Id, targetType, targetId, reason))
            : _real.RequestAsync(targetType, targetId, reason);

    public Task<int> RequestManyAsync(string targetType, IReadOnlyCollection<Guid> targetIds, string? reason = null)
    {
        if (!_transactionsDisabled)
        {
            return _real.RequestManyAsync(targetType, targetIds, reason);
        }

        foreach (var id in targetIds.Distinct())
        {
            _recorded.Record(_currentTenant.Id, targetType, id, reason);
        }

        return Task.FromResult(targetIds.Distinct().Count());
    }
}
