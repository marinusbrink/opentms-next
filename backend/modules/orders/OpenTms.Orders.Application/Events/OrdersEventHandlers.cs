using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Financial.Events;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.Events;
using OpenTms.Platform.DerivedValues;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Timing;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;

namespace OpenTms.Orders.Events;

/// <summary>
/// The Orders side of the other modules' facts (design 58 "Events"): Planning's leg and trip events and Financial's rate
/// lines ask the order summary calculator for a new version; TransportTripCreated records the trip on the order's trip
/// request. Every handler is idempotent (at-least-once delivery, constitution rule 3): a request for a record is a version
/// bump, a trip id is written once. Handlers run in the raising tenant's context inside the dispatcher's unit of work per
/// event (a direct call opens its own).
/// </summary>
public class OrdersEventHandlers :
    IDistributedEventHandler<LegAssignedEto>,
    IDistributedEventHandler<LegUnassignedEto>,
    IDistributedEventHandler<LegsGeneratedEto>,
    IDistributedEventHandler<TransportTripCreatedEto>,
    IDistributedEventHandler<TransportTripRequestNotHonouredEto>,
    IDistributedEventHandler<TransportTripStatusChangedEto>,
    IDistributedEventHandler<TransportTripRecalculatedEto>,
    IDistributedEventHandler<RateLinesChangedEto>,
    ITransientDependency
{
    private readonly IRepository<TransportOrder, Guid> _orders;
    private readonly IDerivedValueScheduler _derivedValues;
    private readonly IClock _clock;

    public ILogger<OrdersEventHandlers> Logger { get; set; } = NullLogger<OrdersEventHandlers>.Instance;

    public OrdersEventHandlers(IRepository<TransportOrder, Guid> orders, IDerivedValueScheduler derivedValues, IClock clock)
    {
        _orders = orders;
        _derivedValues = derivedValues;
        _clock = clock;
    }

    [UnitOfWork]
    public virtual Task HandleEventAsync(LegAssignedEto eventData) => RequestAsync([eventData.OrderId], "LegAssigned");

    [UnitOfWork]
    public virtual Task HandleEventAsync(LegUnassignedEto eventData) => RequestAsync([eventData.OrderId], "LegUnassigned");

    [UnitOfWork]
    public virtual Task HandleEventAsync(LegsGeneratedEto eventData) => RequestAsync([eventData.OrderId], "LegsGenerated");

    [UnitOfWork]
    public virtual Task HandleEventAsync(TransportTripStatusChangedEto eventData) => RequestAsync(eventData.OrderIds, "TripStatusChanged");

    [UnitOfWork]
    public virtual Task HandleEventAsync(TransportTripRecalculatedEto eventData) => RequestAsync(eventData.OrderIds, "TripRecalculated");

    [UnitOfWork]
    public virtual Task HandleEventAsync(RateLinesChangedEto eventData)
        => eventData.OrderId == null ? Task.CompletedTask : RequestAsync([eventData.OrderId.Value], "RateLinesChanged");

    [UnitOfWork]
    public virtual async Task HandleEventAsync(TransportTripCreatedEto eventData)
    {
        if (eventData.CreatedFromOrderId == null)
        {
            return;
        }

        var order = await _orders.FindAsync(eventData.CreatedFromOrderId.Value, includeDetails: false);
        if (order != null && order.RecordTrip(eventData.TripId))
        {
            await _orders.UpdateAsync(order, autoSave: true);
            await _derivedValues.RequestAsync(OrdersConsts.OrderSummaryTargetType, order.Id, "TripCreated");
            Logger.LogInformation("Trip {TripId} recorded on the trip request of order {OrderId}.", eventData.TripId, order.Id);
        }
    }

    /// <summary>
    /// Planning could not honour the order's trip request (design 59 build note 13): the order keeps a warning the
    /// screens show. Idempotent: the same refusal again changes nothing; a request already honoured is left alone.
    /// </summary>
    [UnitOfWork]
    public virtual async Task HandleEventAsync(TransportTripRequestNotHonouredEto eventData)
    {
        var order = await _orders.FindAsync(eventData.OrderId, includeDetails: false);
        if (order != null && order.RecordTripRequestNotHonoured(eventData.Field, new DateTimeOffset(_clock.Now)))
        {
            await _orders.UpdateAsync(order, autoSave: true);
            await _derivedValues.RequestAsync(OrdersConsts.OrderSummaryTargetType, order.Id, "TripRequestNotHonoured");
            Logger.LogInformation("Trip request of order {OrderId} recorded as not honoured ({Field}).", order.Id, eventData.Field);
        }
    }

    private async Task RequestAsync(IReadOnlyCollection<Guid> orderIds, string reason)
    {
        var ids = orderIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        /* Only orders this tenant has: a stale id (a deleted order) requests nothing. */
        var existing = await _orders.GetListAsync(o => ids.Contains(o.Id), includeDetails: false);
        foreach (var order in existing)
        {
            await _derivedValues.RequestAsync(OrdersConsts.OrderSummaryTargetType, order.Id, reason);
        }
    }
}
