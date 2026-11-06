using System;
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
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace OpenTms.Reporting.Facts;

/// <summary>
/// One backfill pass for the current tenant (design 75 "Backfill"): the first pass clears its facts; each pass requests the next
/// batch of orders and trips through the derived-value scheduler and stores its position in the same transaction, so an
/// interrupted pass repeats. The backfill is finished only when the position ran out <b>and</b> the fact counts equal the
/// sources' counts, because requesting is not the same as having run.
/// </summary>
public class ReportingFactBackfillRunner : ITransientDependency
{
    private readonly ReportingFactState _state;
    private readonly IOrderFactReader _orders;
    private readonly ITripFactReader _trips;
    private readonly IRepository<OrderFact, Guid> _orderFacts;
    private readonly IRepository<TripFact, Guid> _tripFacts;
    private readonly IDerivedValueScheduler _scheduler;
    private readonly IFeatureChecker _features;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IClock _clock;
    private readonly ReportingMetricsOptions _options;

    public ILogger<ReportingFactBackfillRunner> Logger { get; set; } = NullLogger<ReportingFactBackfillRunner>.Instance;

    public ReportingFactBackfillRunner(
        ReportingFactState state,
        IOrderFactReader orders,
        ITripFactReader trips,
        IRepository<OrderFact, Guid> orderFacts,
        IRepository<TripFact, Guid> tripFacts,
        IDerivedValueScheduler scheduler,
        IFeatureChecker features,
        IUnitOfWorkManager unitOfWorkManager,
        IClock clock,
        IOptions<ReportingMetricsOptions> options)
    {
        _state = state;
        _orders = orders;
        _trips = trips;
        _orderFacts = orderFacts;
        _tripFacts = tripFacts;
        _scheduler = scheduler;
        _features = features;
        _unitOfWorkManager = unitOfWorkManager;
        _clock = clock;
        _options = options.Value;
    }

    /// <summary>Does nothing for a tenant without the feature or with a finished backfill. Returns whether it did work.</summary>
    public virtual async Task<bool> RunForCurrentTenantAsync(CancellationToken cancellationToken = default)
    {
        if (!await _features.IsEnabledAsync(ReportingFeatures.Metrics))
        {
            return false;
        }

        using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
        if (await _state.GetBackfillCompletedAtAsync() != null)
        {
            await unitOfWork.CompleteAsync(cancellationToken);
            return false;
        }

        var position = await _state.GetBackfillPositionAsync();
        if (position == null)
        {
            /* Facts may be stale from an earlier time the feature was on. */
            await _state.ClearFactsAsync();
            position = new ReportingBackfillPosition();
            Logger.LogInformation("Metric facts: backfill started, facts cleared.");
        }

        await RequestNextOrdersAsync(position, cancellationToken);
        await RequestNextTripsAsync(position, cancellationToken);

        if (position.OrdersDone && position.TripsDone)
        {
            position.ExhaustedAt ??= _clock.Now;
            if (await CountsMatchAsync(cancellationToken))
            {
                await _state.SetBackfillCompletedAtAsync(_clock.Now);
                /* The first repair is due a full interval from now: feature off and on within a day keeps these facts. */
                await _state.SetRepairLastRunAtAsync(_clock.Now);
                Logger.LogInformation("Metric facts: backfill finished.");
            }
            else if (_clock.Now - position.ExhaustedAt.Value >= _options.BackfillResweepAfter)
            {
                /* A calculation failed for good, or records came in behind the position: ask for everything once more, without clearing. */
                position = new ReportingBackfillPosition();
                Logger.LogWarning("Metric facts: the fact counts still differ from the sources' after the sweep; sweeping again.");
            }
        }

        await _state.SetBackfillPositionAsync(position);
        await unitOfWork.CompleteAsync(cancellationToken);
        return true;
    }

    private async Task RequestNextOrdersAsync(ReportingBackfillPosition position, CancellationToken cancellationToken)
    {
        if (position.OrdersDone)
        {
            return;
        }

        var ids = await _orders.GetIdsAfterAsync(position.OrdersAfter, _options.BackfillBatchSize, cancellationToken);
        if (ids.Count > 0)
        {
            await _scheduler.RequestManyAsync(ReportingFactConsts.OrderFactTargetType, ids, "Backfill");
            position.OrdersAfter = ids[^1];
        }

        position.OrdersDone = ids.Count < _options.BackfillBatchSize;
    }

    private async Task RequestNextTripsAsync(ReportingBackfillPosition position, CancellationToken cancellationToken)
    {
        if (position.TripsDone)
        {
            return;
        }

        var ids = await _trips.GetIdsAfterAsync(position.TripsAfter, _options.BackfillBatchSize, cancellationToken);
        if (ids.Count > 0)
        {
            await _scheduler.RequestManyAsync(ReportingFactConsts.TripFactTargetType, ids, "Backfill");
            position.TripsAfter = ids[^1];
        }

        position.TripsDone = ids.Count < _options.BackfillBatchSize;
    }

    private async Task<bool> CountsMatchAsync(CancellationToken cancellationToken)
        => await _orderFacts.LongCountAsync(cancellationToken) == await _orders.GetCountAsync(cancellationToken)
           && await _tripFacts.LongCountAsync(cancellationToken) == await _trips.GetCountAsync(cancellationToken);
}

/// <summary>
/// Visits every tenant in the tenant store, shared-tier ones too, each in its own context, the way
/// <c>TemplateMaterialisationWorker</c> does; a tenant without the feature costs one feature check. One failing tenant never
/// stops the others. Registered by <c>ReportingApplicationModule</c> when <c>Reporting:Metrics:WorkersEnabled</c>.
/// </summary>
public class ReportingFactBackfillWorker : AsyncPeriodicBackgroundWorkerBase
{
    public ReportingFactBackfillWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory, IOptions<ReportingMetricsOptions> options)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)Math.Clamp(options.Value.BackfillPeriod.TotalMilliseconds, 1_000, int.MaxValue);
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
                    await workerContext.ServiceProvider.GetRequiredService<ReportingFactBackfillRunner>().RunForCurrentTenantAsync(workerContext.CancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "Metric fact backfill failed for tenant {TenantId}; continuing with the remaining tenants.", tenant.Id);
            }
        }
    }
}
