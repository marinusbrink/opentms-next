using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// Removes expired route-cache rows (design 60, "Route cache"): the host's rows first, then every tenant's under
/// <see cref="ICurrentTenant.Change"/>, so the tenant filter scopes each delete and no filter is ever disabled; a tenant
/// with its own database is visited there, a shared-tier tenant inside the host database. One failing tenant is logged
/// and skipped. Called by <see cref="RouteCachePurgeWorker"/> once a day.
/// </summary>
public class RouteCachePurger : ITransientDependency
{
    private readonly IRepository<RouteCacheEntry, Guid> _cache;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly ITenantStore _tenantStore;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly TimeProvider _time;

    public ILogger<RouteCachePurger> Logger { get; set; } = NullLogger<RouteCachePurger>.Instance;

    public RouteCachePurger(
        IRepository<RouteCacheEntry, Guid> cache,
        IAsyncQueryableExecuter asyncExecuter,
        ITenantStore tenantStore,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        TimeProvider time)
    {
        _cache = cache;
        _asyncExecuter = asyncExecuter;
        _tenantStore = tenantStore;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
        _time = time;
    }

    /// <summary>Purges the host and every tenant; returns the number of rows removed.</summary>
    public async Task<long> PurgeAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var removed = await PurgeTenantAsync(null, now, cancellationToken);
        var tenants = await _tenantStore.GetListAsync(includeDetails: false);
        var failed = 0;
        foreach (var tenant in tenants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                removed += await PurgeTenantAsync(tenant.Id, now, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                failed++;
                Logger.LogError(e, "Route cache purge failed for tenant {TenantId}; continuing with the remaining tenants.", tenant.Id);
            }
        }

        Logger.LogInformation("Route cache purge removed {Count} expired rows over {TenantCount} tenants ({FailedCount} failed).", removed, tenants.Count, failed);
        return removed;
    }

    /// <summary>Purges one tenant's expired rows (null = the host) in a unit of work of its own.</summary>
    public async Task<long> PurgeTenantAsync(Guid? tenantId, DateTime now, CancellationToken cancellationToken = default)
    {
        using (_currentTenant.Change(tenantId))
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = false }, requiresNew: true))
        {
            var expired = await _asyncExecuter.LongCountAsync((await _cache.GetQueryableAsync()).Where(e => e.ExpiresAt <= now), cancellationToken);
            if (expired > 0)
            {
                await _cache.DeleteDirectAsync(e => e.ExpiresAt <= now, cancellationToken);
            }

            await unitOfWork.CompleteAsync(cancellationToken);
            return expired;
        }
    }
}

/// <summary>
/// The daily purge of expired route-cache rows (design 60: "a daily job purges expired rows"), as an ABP periodic
/// worker like Platform's registry heartbeat. Registered by <see cref="IntegrationsApplicationModule"/>, so it runs in
/// every application that loads the Integrations application module (the API host; not the DbMigrator). The first run
/// is one period after start: a restart never triggers a purge, and the rows are small.
/// </summary>
public class RouteCachePurgeWorker : AsyncPeriodicBackgroundWorkerBase, ISingletonDependency
{
    public static readonly TimeSpan PurgePeriod = TimeSpan.FromHours(24);

    public RouteCachePurgeWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)PurgePeriod.TotalMilliseconds;
        Timer.RunOnStart = false;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        try
        {
            await workerContext.ServiceProvider.GetRequiredService<RouteCachePurger>().PurgeAsync(workerContext.CancellationToken);
        }
        catch (OperationCanceledException) when (workerContext.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            /* An unreachable database must not take the worker down; the next period retries. */
            Logger.LogWarning(e, "Route cache purge failed; retrying on the next period.");
        }
    }
}
