using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Orders.Events;
using OpenTms.PlanningExecution.Published;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.NumberSequences;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// The one code path that builds, confirms and publishes a transport order (design 58 "Lifecycle"): the app service's
/// Create, the template materialiser and "copy order" all come through here, so the id checks, the number, the events
/// and the derived-value request are written once. Every method runs inside the caller's transactional unit of work.
/// </summary>
public class TransportOrderCommands : ITransientDependency
{
    private readonly IRepository<TransportOrder, Guid> _orders;
    private readonly OrdersReferences _references;
    private readonly INumberSequenceGenerator _numbers;
    private readonly IDistributedEventBus _events;
    private readonly IDerivedValueScheduler _derivedValues;
    private readonly ILegReader _legs;
    private readonly IGuidGenerator _guids;
    private readonly ICurrentTenant _currentTenant;

    public ILogger<TransportOrderCommands> Logger { get; set; } = NullLogger<TransportOrderCommands>.Instance;

    public TransportOrderCommands(
        IRepository<TransportOrder, Guid> orders,
        OrdersReferences references,
        INumberSequenceGenerator numbers,
        IDistributedEventBus events,
        IDerivedValueScheduler derivedValues,
        ILegReader legs,
        IGuidGenerator guids,
        ICurrentTenant currentTenant)
    {
        _orders = orders;
        _references = references;
        _numbers = numbers;
        _events = events;
        _derivedValues = derivedValues;
        _legs = legs;
        _guids = guids;
        _currentTenant = currentTenant;
    }

    /// <summary>
    /// A new order from a draft: ids checked, the pattern from the type when the draft has none, lines without stops
    /// started from the pattern, Draft or (with <paramref name="confirm"/>) Open with its number. Publishes
    /// TransportOrderLinesChanged for a Draft (pricing), the Confirm events for an Open order; requests the summary.
    /// </summary>
    public async Task<TransportOrder> CreateAsync(TransportOrderDraft draft, OrderSource source, decimal? draftConfidence, Guid? sourceTemplateId, int copyIndex, bool confirm)
    {
        Check.NotNull(draft, nameof(draft));
        var type = await _references.GetOrderTypeAsync(draft.Header.TransportOrderTypeId);
        var customerName = await _references.ValidateDraftAsync(draft);

        var order = new TransportOrder(_guids.Create(), _currentTenant.Id, draft.Header with { CustomerName = customerName }, draft.Terms, source, _guids.Create);
        order.SetDraftConfidence(draftConfidence);
        order.SetSourceTemplate(sourceTemplateId, copyIndex);
        order.SetAttributes(draft.Attributes);
        order.SetDefaultStops(draft.DefaultStops.Count > 0
            ? draft.DefaultStops
            : type.DefaultWaypoints.OrderBy(w => w.Position).Select(w => new DefaultStopInput(w.WaypointTypeId, null, null, null, 0, null, null, null, null)).ToList());

        var pattern = order.StopsFromPattern();
        /* Every line of a new order is new: an id posted with a line is only its key in the draft (the trip request's
         * freight items name lines by it, design 61 point 7), so a line without stops starts from the pattern either way. */
        var lines = order.SetLines(draft.Lines.Select(l => l.Stops.Count == 0 ? l with { Stops = pattern } : l).ToList(), _guids.Create);
        order.SetReferences(draft.References, _guids.Create);
        order.SetTripRequest(draft.TripRequest == null
            ? null
            : draft.TripRequest with { LineIds = TripRequestLines.Remap(draft.TripRequest.LineIds, draft.Lines.Select(l => l.Id).ToList(), lines.LineIds) });

        await _orders.InsertAsync(order, autoSave: true);

        if (confirm)
        {
            await ConfirmAsync(order, type);
        }
        else
        {
            await _events.PublishAsync(new TransportOrderLinesChangedEto { OrderId = order.Id, LineIds = order.Lines.Select(l => l.Id).ToList() });
            await RequestSummaryAsync(order, "Created");
        }

        Logger.LogInformation("Transport order {OrderId} created as {Status} from {Source} ({LineCount} lines).", order.Id, order.Status, source, order.Lines.Count);
        return order;
    }

    /// <summary>Draft → Open: the type's rules, the number, the events Planning and Financial react to (design 58 "Confirm").</summary>
    public async Task ConfirmAsync(TransportOrder order, TransportOrderType? type = null)
    {
        type ??= await _references.GetOrderTypeAsync(order.TransportOrderTypeId);
        var waypointTypes = await _references.WaypointTypesAsync(order.Lines.SelectMany(l => l.Stops).Select(s => s.Details.WaypointTypeId), require: false);

        /* The rules first, the number after: a refused confirm draws no number (gapless). */
        order.EnsureComplete(type.RequiredFields, id => waypointTypes.TryGetValue(id, out var w) ? w.Activity : null);
        var number = await _numbers.NextAsync(type.SequenceKey, type.Numbering);
        order.Confirm(number, type.RequiredFields, id => waypointTypes.TryGetValue(id, out var w) ? w.Activity : null);
        await _orders.UpdateAsync(order, autoSave: true);

        var lineIds = order.Lines.Select(l => l.Id).ToList();
        await _events.PublishAsync(new TransportOrderConfirmedEto { OrderId = order.Id });
        await _events.PublishAsync(new TransportOrderLinesChangedEto { OrderId = order.Id, LineIds = lineIds });
        await _events.PublishAsync(new TransportOrderStopsChangedEto { OrderId = order.Id, LineIds = lineIds });
        await RequestSummaryAsync(order, "Confirmed");
        Logger.LogInformation("Transport order {OrderId} confirmed.", order.Id);
    }

    public async Task PublishLinesChangedAsync(TransportOrder order, LineChanges changes)
    {
        await _events.PublishAsync(new TransportOrderLinesChangedEto { OrderId = order.Id, LineIds = changes.LineIds.Concat(changes.RemovedLineIds).Distinct().ToList() });
        if (changes.StopChangedLineIds.Count > 0 || changes.RemovedLineIds.Count > 0)
        {
            await _events.PublishAsync(new TransportOrderStopsChangedEto { OrderId = order.Id, LineIds = changes.StopChangedLineIds.Concat(changes.RemovedLineIds).Distinct().ToList() });
        }

        await RequestSummaryAsync(order, "LinesChanged");
    }

    public Task PublishTermsChangedAsync(TransportOrder order)
        => Task.WhenAll(_events.PublishAsync(new TransportOrderFinancialTermsChangedEto { OrderId = order.Id }), RequestSummaryAsync(order, "TermsChanged"));

    public Task PublishStatusChangedAsync(TransportOrder order)
        => Task.WhenAll(_events.PublishAsync(new TransportOrderStatusChangedEto { OrderId = order.Id, Status = order.Status }), RequestSummaryAsync(order, "StatusChanged"));

    public Task PublishCancelledAsync(TransportOrder order)
        => Task.WhenAll(_events.PublishAsync(new TransportOrderCancelledEto { OrderId = order.Id }), RequestSummaryAsync(order, "Cancelled"));

    public Task RequestSummaryAsync(TransportOrder order, string reason)
        => _derivedValues.RequestAsync(OrdersConsts.OrderSummaryTargetType, order.Id, reason);

    /// <summary>Planning's legs of the order with their trips, for the locks of Update, Cancel and Delete.</summary>
    public Task<IReadOnlyList<LegSnapshotDto>> LegsOfAsync(Guid orderId) => _legs.GetByOrderAsync(orderId);

    /// <summary>Open: a line whose leg rides on a trip in progress keeps its stops (orders.md 21/26 made one rule).</summary>
    public async Task EnsureStopsUnlockedAsync(TransportOrder order, IEnumerable<Guid> changedLineIds)
    {
        if (order.Status != TransportOrderStatus.Open)
        {
            return;
        }

        var changed = changedLineIds.ToHashSet();
        if (changed.Count == 0)
        {
            return;
        }

        var legs = await LegsOfAsync(order.Id);
        var locked = legs.FirstOrDefault(l => changed.Contains(l.OrderLineId) && l.TripStatus == TransportTripStatus.InProgress);
        if (locked != null)
        {
            throw new BusinessException(OrdersErrorCodes.StopLockedByTrip).WithData("lineId", locked.OrderLineId).WithData("tripNumber", locked.TripNumber ?? string.Empty);
        }
    }

    public async Task EnsureCancellableAsync(TransportOrder order)
    {
        var legs = await LegsOfAsync(order.Id);
        var busy = legs.FirstOrDefault(l => l.TripStatus is TransportTripStatus.InProgress or TransportTripStatus.Completed);
        if (busy != null)
        {
            throw new BusinessException(OrdersErrorCodes.CancelRefusedTripInProgress).WithData("tripNumber", busy.TripNumber ?? string.Empty);
        }
    }

    public async Task EnsureDeletableAsync(TransportOrder order)
    {
        order.EnsureDeletable();
        var legs = await LegsOfAsync(order.Id);
        var assigned = legs.FirstOrDefault(l => l.TripId != null);
        if (assigned != null)
        {
            throw new BusinessException(OrdersErrorCodes.DeleteRefusedLegsAssigned).WithData("tripNumber", assigned.TripNumber ?? string.Empty);
        }
    }
}
