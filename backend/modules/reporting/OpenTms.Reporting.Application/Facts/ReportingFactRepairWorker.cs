using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTms.Orders.Published;
using OpenTms.PlanningExecution.Published;
using OpenTms.Platform.DerivedValues;
using OpenTms.Reporting.Features;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace OpenTms.Reporting.Facts;

/// <summary>
/// The daily repair for the current tenant (design 75 "Repair"): failing events have no dead-letter store (AO-10), so it requests
/// again every record created, changed or deleted in the last 48 hours, each page in its own transaction. A soft-deleted one
/// reaches the calculator, which removes the fact. For a tenant whose feature is off it clears the backfill's settings instead,
/// so turning the feature on starts a new backfill, which clears the stale facts first. A number left by a backfill that ran out of
/// sweeps is recounted here, so the <c>facts_incomplete</c> warning follows the facts, a day late at most.
/// </summary>
public class ReportingFactRepairRunner : ITransientDependency
{
    private const int PageSize = 1_000;

    private readonly ReportingFactState _state;
    private readonly IOrderFactReader _orders;
    private readonly ITripFactReader _trips;
    private readonly IDerivedValueScheduler _scheduler;
    private readonly IFeatureChecker _features;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IClock _clock;
    private readonly ReportingMetricsOptions _options;

    public ILogger<ReportingFactRepairRunner> Logger { get; set; } = NullLogger<ReportingFactRepairRunner>.Instance;

    public ReportingFactRepairRunner(
        ReportingFactState state,
        IOrderFactReader orders,
        ITripFactReader trips,
        IDerivedValueScheduler scheduler,
        IFeatureChecker features,
        IUnitOfWorkManager unitOfWorkManager,
        IClock clock,
        IOptions<ReportingMetricsOptions> options)
    {
        _state = state;
        _orders = orders;
        _trips = trips;
        _scheduler = scheduler;
        _features = features;
        _unitOfWorkManager = unitOfWorkManager;
        _clock = clock;
        _options = options.Value;
    }

    /// <summary>Runs when this tenant is due: <see cref="ReportingMetricsOptions.RepairInterval"/> after its last run. Returns whether it ran.</summary>
    public virtual async Task<bool> RunIfDueForCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        DateTime? lastRun;
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: false), requiresNew: true))
        {
            lastRun = await _state.GetRepairLastRunAtAsync();
            await unitOfWork.CompleteAsync(cancellationToken);
        }

        if (lastRun != null && _clock.Now - lastRun.Value < _options.RepairInterval)
        {
            return false;
        }

        await RunForCurrentTenantAsync(cancellationToken);
        return true;
    }

    /// <summary>The run itself, whatever the last run was. Idempotent: a repeat asks for a new version of the same records.</summary>
    public virtual async Task RunForCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        var featureOn = await _features.IsEnabledAsync(ReportingFeatures.Metrics);
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true))
        {
            var completedAt = await _state.GetBackfillCompletedAtAsync();
            if (!featureOn && (completedAt != null || await _state.GetBackfillPositionAsync() != null))
            {
                await _state.ClearBackfillAsync();
                await _state.SetRepairLastRunAtAsync(_clock.Now);
                Logger.LogInformation("Metric facts: the feature is off, so the next time it goes on starts a new backfill.");
            }

            await unitOfWork.CompleteAsync(cancellationToken);

            /* Feature off, nothing to repair. Backfill not finished: it requests everything anyway. */
            if (!featureOn || completedAt == null)
            {
                return;
            }
        }

        var since = _clock.Now - _options.RepairWindow;
        var orders = await RequestAllAsync(ReportingFactConsts.OrderFactTargetType, (after, ct) => _orders.GetIdsModifiedSinceAsync(since, after, PageSize, ct), cancellationToken);
        var trips = await RequestAllAsync(ReportingFactConsts.TripFactTargetType, (after, ct) => _trips.GetIdsModifiedSinceAsync(since, after, PageSize, ct), cancellationToken);

        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true))
        {
            await _state.SetRepairLastRunAtAsync(_clock.Now);
            var missing = await _state.RecountFactsMissingAsync(cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            if (missing != null)
            {
                Logger.LogInformation("Metric facts: {MissingOrders} orders and {MissingTrips} trips still differ from their facts.", missing.Orders, missing.Trips);
            }
        }

        Logger.LogInformation("Metric facts: repair requested {OrderCount} orders and {TripCount} trips.", orders, trips);
    }

    private async Task<int> RequestAllAsync(string targetType, Func<Guid?, CancellationToken, Task<List<Guid>>> nextPage, CancellationToken cancellationToken)
    {
        var total = 0;
        Guid? after = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
            var ids = await nextPage(after, cancellationToken);
            if (ids.Count > 0)
            {
                await _scheduler.RequestManyAsync(targetType, ids, "Repair");
                after = ids[^1];
                total += ids.Count;
            }

            await unitOfWork.CompleteAsync(cancellationToken);
            if (ids.Count < PageSize)
            {
                return total;
            }
        }
    }
}

/// <summary>
/// Looks every <see cref="ReportingMetricsOptions.RepairCheckPeriod"/> which tenants are due. A timer of a day would never fire
/// on a host that deploys more often than that: every deploy restarts it (design 75 build notes), so the last run lives in a
/// setting. Registered by <c>ReportingApplicationModule</c> when <c>Reporting:Metrics:WorkersEnabled</c>.
/// </summary>
public class ReportingFactRepairWorker : AsyncPeriodicBackgroundWorkerBase
{
    public ReportingFactRepairWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory, IOptions<ReportingMetricsOptions> options)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)Math.Clamp(options.Value.RepairCheckPeriod.TotalMilliseconds, 1_000, int.MaxValue);
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var tenantStore = workerContext.ServiceProvider.GetRequiredService<ITenantStore>();
        var currentTenant = workerContext.ServiceProvider.GetRequiredService<ICurrentTenant>();
        foreach (var tenant in await tenantStore.GetListAsync())
        {
            workerContext.CancellationToken.ThrowIfCancellationRequested();
            try
            {
                using (currentTenant.Change(tenant.Id))
                {
                    await workerContext.ServiceProvider.GetRequiredService<ReportingFactRepairRunner>().RunIfDueForCurrentTenantAsync(workerContext.CancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "Metric fact repair failed for tenant {TenantId}; continuing with the remaining tenants.", tenant.Id);
            }
        }
    }
}
