using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Orders.Events;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;

namespace OpenTms.TestDoubles;

/// <summary>Records every Orders fact that was published, with the tenant it was raised in.</summary>
public class OrdersEventRecorder : ISingletonDependency
{
    private readonly ConcurrentQueue<(Guid? TenantId, object Event)> _events = new();

    public IReadOnlyList<(Guid? TenantId, object Event)> All => _events.ToList();

    public IReadOnlyList<T> Of<T>(Guid orderId) => _events.Select(e => e.Event).OfType<T>().Where(e => OrderIdOf(e) == orderId).ToList();

    public void Record(Guid? tenantId, object eventData) => _events.Enqueue((tenantId, eventData));

    private static Guid OrderIdOf(object e) => e switch
    {
        TransportOrderConfirmedEto x => x.OrderId,
        TransportOrderLinesChangedEto x => x.OrderId,
        TransportOrderStopsChangedEto x => x.OrderId,
        TransportOrderFinancialTermsChangedEto x => x.OrderId,
        TransportOrderCancelledEto x => x.OrderId,
        TransportOrderStatusChangedEto x => x.OrderId,
        _ => Guid.Empty
    };
}

public class OrdersEventRecordingHandler :
    IDistributedEventHandler<TransportOrderConfirmedEto>, IDistributedEventHandler<TransportOrderLinesChangedEto>, IDistributedEventHandler<TransportOrderStopsChangedEto>,
    IDistributedEventHandler<TransportOrderFinancialTermsChangedEto>, IDistributedEventHandler<TransportOrderCancelledEto>, IDistributedEventHandler<TransportOrderStatusChangedEto>,
    ITransientDependency
{
    private readonly OrdersEventRecorder _recorder;
    private readonly ICurrentTenant _currentTenant;

    public OrdersEventRecordingHandler(OrdersEventRecorder recorder, ICurrentTenant currentTenant)
    {
        _recorder = recorder;
        _currentTenant = currentTenant;
    }

    public Task HandleEventAsync(TransportOrderConfirmedEto e) => Record(e);
    public Task HandleEventAsync(TransportOrderLinesChangedEto e) => Record(e);
    public Task HandleEventAsync(TransportOrderStopsChangedEto e) => Record(e);
    public Task HandleEventAsync(TransportOrderFinancialTermsChangedEto e) => Record(e);
    public Task HandleEventAsync(TransportOrderCancelledEto e) => Record(e);
    public Task HandleEventAsync(TransportOrderStatusChangedEto e) => Record(e);

    private Task Record(object e)
    {
        _recorder.Record(_currentTenant.Id, e);
        return Task.CompletedTask;
    }
}
