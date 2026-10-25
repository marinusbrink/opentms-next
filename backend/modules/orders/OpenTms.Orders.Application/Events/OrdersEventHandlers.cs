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
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;

namespace OpenTms.Orders.Events;

/// <summary>
/// The Orders side of the other modules' facts (design 58 "Events"): Planning's leg and trip events and Financial's rate
/// lines ask the order summary calculator for a new version; TransportTripCreated records the trip on the order's trip
/// request. Every handler is idempotent (at-least-once delivery, constitution rule 3): a request for a record is a version
/// bump, a trip id is written once. Handlers run in the raising tenant's context inside their own transaction.
/// </summary>
public class OrdersEventHandlers :
    IDistributedEventHandler<LegAssignedEto>,
    IDistributedEventHandler<LegUnassignedEto>,
    IDistributedEventHandler<LegsGeneratedEto>,
    IDistributedEventHandler<TransportTripCreatedEto>,
    IDistributedEventHandler<TransportTripStatusChangedEto>,
    IDistributedEventHandler<TransportTripRecalculatedEto>,
    IDistributedEventHandler<RateLinesChangedEto>,
    ITransientDependency
{
    private readonly IRepository<TransportOrder, Guid> _orders;
    private readonly IDerivedValueScheduler _derivedValues;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public ILogger<OrdersEventHandlers> Logger { get; set; } = NullLogger<OrdersEventHandlers>.Instance;

    public OrdersEventHandlers(IRepository<TransportOrder, Guid> orders, IDerivedValueScheduler derivedValues, IUnitOfWorkManager unitOfWorkManager)
    {
        _orders = orders;
        _derivedValues = derivedValues;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public Task HandleEventAsync(LegAssignedEto eventData) => RequestAsync([eventData.OrderId], "LegAssigned");

    public Task HandleEventAsync(LegUnassignedEto eventData) => RequestAsync([eventData.OrderId], "LegUnassigned");

    public Task HandleEventAsync(LegsGeneratedEto eventData) => RequestAsync([eventData.OrderId], "LegsGenerated");

    public Task HandleEventAsync(TransportTripStatusChangedEto eventData) => RequestAsync(eventData.OrderIds, "TripStatusChanged");

    public Task HandleEventAsync(TransportTripRecalculatedEto eventData) => RequestAsync(eventData.OrderIds, "TripRecalculated");

    public Task HandleEventAsync(RateLinesChangedEto eventData)
        => eventData.OrderId == null ? Task.CompletedTask : RequestAsync([eventData.OrderId.Value], "RateLinesChanged");

    public async Task HandleEventAsync(TransportTripCreatedEto eventData)
    {
        if (eventData.CreatedFromOrderId == null)
        {
            return;
        }

        using var uow = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
        var order = await _orders.FindAsync(eventData.CreatedFromOrderId.Value, includeDetails: false);
        if (order != null && order.RecordTrip(eventData.TripId))
        {
            await _orders.UpdateAsync(order, autoSave: true);
            await _derivedValues.RequestAsync(OrdersConsts.OrderSummaryTargetType, order.Id, "TripCreated");
            Logger.LogInformation("Trip {TripId} recorded on the trip request of order {OrderId}.", eventData.TripId, order.Id);
        }

        await uow.CompleteAsync();
    }

    private async Task RequestAsync(IReadOnlyCollection<Guid> orderIds, string reason)
    {
        var ids = orderIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        using var uow = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
        /* Only orders this tenant has: a stale id (a deleted order) requests nothing. */
        var existing = await _orders.GetListAsync(o => ids.Contains(o.Id), includeDetails: false);
        foreach (var order in existing)
        {
            await _derivedValues.RequestAsync(OrdersConsts.OrderSummaryTargetType, order.Id, reason);
        }

        await uow.CompleteAsync();
    }
}
