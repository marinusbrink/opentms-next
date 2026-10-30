using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.Platform.DerivedValues;

/// <summary>
/// Flow 2 B (design 49): turns a request into a background job in the Jobs role (today the API host's Hangfire server,
/// AD-13). Runs in the requesting tenant's context (the outbox dispatcher switches to it) and inside a unit of work: the
/// state read needs a DbContext, which the dispatcher's unit of work per event provides (a direct call opens its own).
/// Idempotent: a duplicate delivery, or a request a newer one has overtaken, enqueues nothing or a job that skips.
/// </summary>
public class DerivedValuesRequestedHandler : IDistributedEventHandler<DerivedValuesRequestedEto>, ITransientDependency
{
    public ILogger<DerivedValuesRequestedHandler> Logger { get; set; } = NullLogger<DerivedValuesRequestedHandler>.Instance;

    private readonly IDerivedValueStateStore _store;
    private readonly IBackgroundJobManager _jobs;
    private readonly ICurrentTenant _currentTenant;

    public DerivedValuesRequestedHandler(IDerivedValueStateStore store, IBackgroundJobManager jobs, ICurrentTenant currentTenant)
    {
        _store = store;
        _jobs = jobs;
        _currentTenant = currentTenant;
    }

    [UnitOfWork]
    public virtual async Task HandleEventAsync(DerivedValuesRequestedEto eventData)
    {
        var states = (await _store.GetListAsync(eventData.TargetType, eventData.Targets.Select(t => t.TargetId).ToList()))
            .ToDictionary(s => s.TargetId);
        var enqueued = 0;

        foreach (var target in eventData.Targets)
        {
            /* A newer request has its own event; a calculated version needs no job. */
            if (!states.TryGetValue(target.TargetId, out var state) || state.RequestedVersion != target.Version || state.CalculatedVersion >= target.Version)
            {
                continue;
            }

            await _jobs.EnqueueAsync(new DerivedValueCalculationArgs
            {
                TenantId = _currentTenant.Id,
                TargetType = eventData.TargetType,
                TargetId = target.TargetId,
                Version = target.Version
            });
            enqueued++;
        }

        Logger.LogDebug("Derived values of {TargetType}: {Enqueued} of {Count} requests enqueued.", eventData.TargetType, enqueued, eventData.Targets.Count);
    }
}
