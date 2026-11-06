using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Orders.Events;
using OpenTms.PlanningExecution.Events;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;

namespace OpenTms.TestDoubles;

/// <summary>Design 74: the Planning and Orders facts the review's commands raise, with the tenant the handler ran in.</summary>
public class PlanboardReviewEventRecorder : ISingletonDependency
{
    private readonly ConcurrentQueue<(Guid? TenantId, object Event)> _events = new();

    public IReadOnlyList<T> Of<T>() => _events.Select(e => e.Event).OfType<T>().ToList();

    public void Record(Guid? tenantId, object e) => _events.Enqueue((tenantId, e));

    public void Clear() => _events.Clear();
}

public class PlanboardReviewEventRecordingHandler :
    IDistributedEventHandler<LegUnassignedEto>, IDistributedEventHandler<TransportTripChangedEto>, IDistributedEventHandler<TransportOrderLinesChangedEto>,
    ITransientDependency
{
    private readonly PlanboardReviewEventRecorder _recorder;
    private readonly ICurrentTenant _currentTenant;

    public PlanboardReviewEventRecordingHandler(PlanboardReviewEventRecorder recorder, ICurrentTenant currentTenant)
    {
        _recorder = recorder;
        _currentTenant = currentTenant;
    }

    public Task HandleEventAsync(LegUnassignedEto e) => Record(e);
    public Task HandleEventAsync(TransportTripChangedEto e) => Record(e);
    public Task HandleEventAsync(TransportOrderLinesChangedEto e) => Record(e);

    private Task Record(object e)
    {
        _recorder.Record(_currentTenant.Id, e);
        return Task.CompletedTask;
    }
}
