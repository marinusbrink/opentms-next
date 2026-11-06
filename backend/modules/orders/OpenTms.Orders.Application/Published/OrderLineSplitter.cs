using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.Orders.TransportOrders;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.Orders.Published;

/// <summary>See <see cref="IOrderLineSplitter"/>: the Orders half of a split of an order line.</summary>
// Published in-process service: the HTTP-facing caller (Planning's leg service, in the same unit of work) authorizes, so it is anonymous.
[RemoteService(IsEnabled = false)]
[AllowAnonymous]
public class OrderLineSplitter : OrdersAppServiceBase, IOrderLineSplitter
{
    private readonly IRepository<TransportOrder, Guid> _orders;
    private readonly TransportOrderCommands _commands;

    public OrderLineSplitter(IRepository<TransportOrder, Guid> orders, TransportOrderCommands commands)
    {
        _orders = orders;
        _commands = commands;
    }

    public virtual async Task<OrderLineSplitResultDto> SplitAsync(Guid orderId, Guid lineId, decimal quantity, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetAsync(orderId, includeDetails: true, cancellationToken);
        order.BeginEdit();
        var total = order.GetLine(lineId).Freight.Quantity;
        var (newLine, stopIds) = order.SplitLine(lineId, quantity, GuidGenerator.Create);
        await _orders.UpdateAsync(order, autoSave: true, cancellationToken);

        var changes = new LineChanges();
        changes.LineIds.Add(lineId);
        changes.LineIds.Add(newLine.Id);
        await _commands.PublishLinesChangedAsync(order, changes);
        Logger.LogInformation("Line {LineId} of order {OrderId} split into {NewLineId}.", lineId, orderId, newLine.Id);
        return new OrderLineSplitResultDto
        {
            NewLineId = newLine.Id,
            TotalQuantity = total,
            RemainingQuantity = order.GetLine(lineId).Freight.Quantity,
            NewQuantity = newLine.Freight.Quantity,
            StopIdMap = stopIds.ToDictionary(p => p.Key, p => p.Value)
        };
    }
}
