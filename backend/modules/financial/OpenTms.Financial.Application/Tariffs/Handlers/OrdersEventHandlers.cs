using System.Threading.Tasks;
using OpenTms.Financial.Tariffs.Calculators;
using OpenTms.Orders.Events;
using OpenTms.Platform.DerivedValues;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;

namespace OpenTms.Financial.Tariffs.Handlers;

/// <summary>
/// Financial's reactions to Orders' facts (design 57 "OrderRateLineCalculator", design 56 flow 2): every change of an
/// order's lines, stops or financial terms requests its revenue lines for a new version. Idempotent (rule 3): a
/// repeated delivery requests once more and the calculator writes the same lines, which it reports as unchanged.
/// Handlers run in the raising tenant's context inside the dispatcher's unit of work per event (a direct call opens its own).
/// </summary>
public class TransportOrderConfirmedHandler : IDistributedEventHandler<TransportOrderConfirmedEto>, ITransientDependency
{
    private readonly IDerivedValueScheduler _scheduler;

    public TransportOrderConfirmedHandler(IDerivedValueScheduler scheduler) => _scheduler = scheduler;

    [UnitOfWork]
    public virtual Task HandleEventAsync(TransportOrderConfirmedEto eventData)
        => _scheduler.RequestAsync(OrderRateLineCalculator.TargetType, eventData.OrderId, "OrderConfirmed");
}

public class TransportOrderLinesChangedHandler : IDistributedEventHandler<TransportOrderLinesChangedEto>, ITransientDependency
{
    private readonly IDerivedValueScheduler _scheduler;

    public TransportOrderLinesChangedHandler(IDerivedValueScheduler scheduler) => _scheduler = scheduler;

    [UnitOfWork]
    public virtual Task HandleEventAsync(TransportOrderLinesChangedEto eventData)
        => _scheduler.RequestAsync(OrderRateLineCalculator.TargetType, eventData.OrderId, "OrderLinesChanged");
}

public class TransportOrderStopsChangedHandler : IDistributedEventHandler<TransportOrderStopsChangedEto>, ITransientDependency
{
    private readonly IDerivedValueScheduler _scheduler;

    public TransportOrderStopsChangedHandler(IDerivedValueScheduler scheduler) => _scheduler = scheduler;

    [UnitOfWork]
    public virtual Task HandleEventAsync(TransportOrderStopsChangedEto eventData)
        => _scheduler.RequestAsync(OrderRateLineCalculator.TargetType, eventData.OrderId, "OrderStopsChanged");
}

public class TransportOrderFinancialTermsChangedHandler : IDistributedEventHandler<TransportOrderFinancialTermsChangedEto>, ITransientDependency
{
    private readonly IDerivedValueScheduler _scheduler;

    public TransportOrderFinancialTermsChangedHandler(IDerivedValueScheduler scheduler) => _scheduler = scheduler;

    [UnitOfWork]
    public virtual Task HandleEventAsync(TransportOrderFinancialTermsChangedEto eventData)
        => _scheduler.RequestAsync(OrderRateLineCalculator.TargetType, eventData.OrderId, "OrderTermsChanged");
}
