using System;
using System.Threading.Tasks;
using OpenTms.Orders.Events;
using OpenTms.PlanningExecution.Events;
using OpenTms.Platform.DerivedValues;
using OpenTms.Reporting.Features;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Features;
using Volo.Abp.Uow;

namespace OpenTms.Reporting.Facts;

/// <summary>
/// Keeps the facts current (design 75 "How a fact stays current", flow 2): every event of the design's list asks for a new
/// version of the record's fact, which a job calculates. A tenant without the feature costs one feature check and writes
/// nothing. Idempotent (at-least-once delivery, constitution rule 3): a request is a version bump, and the calculator only
/// reads the source's current state. Runs in the raising tenant's context inside the dispatcher's unit of work per event.
/// </summary>
public class ReportingFactEventHandlers :
    IDistributedEventHandler<TransportOrderRecalculatedEto>,
    IDistributedEventHandler<TransportOrderStatusChangedEto>,
    IDistributedEventHandler<TransportOrderCancelledEto>,
    IDistributedEventHandler<TransportTripCreatedEto>,
    IDistributedEventHandler<TransportTripRecalculatedEto>,
    IDistributedEventHandler<TransportTripStatusChangedEto>,
    IDistributedEventHandler<TransportTripChangedEto>,
    IDistributedEventHandler<TransportTripDeletedEto>,
    ITransientDependency
{
    private readonly IDerivedValueScheduler _scheduler;
    private readonly IFeatureChecker _features;

    public ReportingFactEventHandlers(IDerivedValueScheduler scheduler, IFeatureChecker features)
    {
        _scheduler = scheduler;
        _features = features;
    }

    [UnitOfWork]
    public virtual Task HandleEventAsync(TransportOrderRecalculatedEto eventData)
        => RequestAsync(ReportingFactConsts.OrderFactTargetType, eventData.OrderId, "OrderRecalculated");

    [UnitOfWork]
    public virtual Task HandleEventAsync(TransportOrderStatusChangedEto eventData)
        => RequestAsync(ReportingFactConsts.OrderFactTargetType, eventData.OrderId, "OrderStatusChanged");

    /// <summary>Also fires on both order delete paths: the calculator then finds no order and deletes the fact.</summary>
    [UnitOfWork]
    public virtual Task HandleEventAsync(TransportOrderCancelledEto eventData)
        => RequestAsync(ReportingFactConsts.OrderFactTargetType, eventData.OrderId, "OrderCancelled");

    /// <summary>Needed because the recalculated event never fires while routing fails: such a trip still gets its fact.</summary>
    [UnitOfWork]
    public virtual Task HandleEventAsync(TransportTripCreatedEto eventData)
        => RequestAsync(ReportingFactConsts.TripFactTargetType, eventData.TripId, "TripCreated");

    [UnitOfWork]
    public virtual Task HandleEventAsync(TransportTripRecalculatedEto eventData)
        => RequestAsync(ReportingFactConsts.TripFactTargetType, eventData.TripId, "TripRecalculated");

    [UnitOfWork]
    public virtual Task HandleEventAsync(TransportTripStatusChangedEto eventData)
        => RequestAsync(ReportingFactConsts.TripFactTargetType, eventData.TripId, "TripStatusChanged");

    [UnitOfWork]
    public virtual Task HandleEventAsync(TransportTripChangedEto eventData)
        => RequestAsync(ReportingFactConsts.TripFactTargetType, eventData.TripId, "TripChanged");

    [UnitOfWork]
    public virtual Task HandleEventAsync(TransportTripDeletedEto eventData)
        => RequestAsync(ReportingFactConsts.TripFactTargetType, eventData.TripId, "TripDeleted");

    private async Task RequestAsync(string targetType, Guid id, string reason)
    {
        if (await _features.IsEnabledAsync(ReportingFeatures.Metrics))
        {
            await _scheduler.RequestAsync(targetType, id, reason);
        }
    }
}
