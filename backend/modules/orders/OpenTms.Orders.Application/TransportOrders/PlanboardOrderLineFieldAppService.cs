using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Published;
using OpenTms.Orders.Permissions;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.Features;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// Design 74 §6: the freight fields the Planboard's leg panes edit inline, a narrow command against the order's stamp. Publishes
/// <c>TransportOrderLinesChanged</c>: the legs follow when the event is delivered (a reshaped line too, <c>RefreshReshapedLine</c>).
/// A line with a leg on a completed or cancelled trip is read-only here. The route has the action after the line's id, which a
/// conventional controller cannot express, so <c>PlanboardOrderLineController</c> exposes it; permission and feature are checked here.
/// </summary>
[RemoteService(IsEnabled = false)]
[RequiresFeature(AppFeatures.Planboard, AppFeatures.PlanboardApp.Orders, RequiresAll = true)]
public class PlanboardOrderLineFieldAppService : OrdersAppServiceBase, IPlanboardOrderLineFieldAppService
{
    private readonly IRepository<TransportOrder, Guid> _orders;
    private readonly TransportOrderCommands _commands;
    private readonly OrdersReferences _references;

    public PlanboardOrderLineFieldAppService(IRepository<TransportOrder, Guid> orders, TransportOrderCommands commands, OrdersReferences references)
    {
        _orders = orders;
        _commands = commands;
        _references = references;
    }

    [Authorize(OrdersPermissions.TransportOrders.Update)]
    public virtual async Task<PlanboardOrderLineFieldResultDto> UpdateLineFieldAsync(Guid id, Guid lineId, PlanboardOrderLineFieldUpdateDto input)
    {
        var order = await _orders.GetAsync(id);
        order.BeginEdit();
        var line = order.GetLine(lineId);
        var locked = (await _commands.LegsOfAsync(id)).FirstOrDefault(l => l.OrderLineId == lineId && l.TripStatus is TransportTripStatus.Completed or TransportTripStatus.Cancelled);
        if (locked != null)
        {
            throw new BusinessException(OrdersErrorCodes.LineOnFinalTrip).WithData("lineId", lineId).WithData("tripNumber", locked.TripNumber ?? string.Empty);
        }

        var freight = line.Freight.ToInput();
        switch (input.Field)
        {
            case PlanboardOrderLineField.TransportUnit:
                freight = freight with { TransportUnitId = input.Value ?? Guid.Empty };
                break;
            case PlanboardOrderLineField.Product:
                /* A relation's code for the product belongs to the old product. */
                freight = input.Value == freight.ProductId ? freight : freight with { ProductId = input.Value, ProductRelationCodeId = null };
                break;
            case PlanboardOrderLineField.Temperature:
                freight = freight with { TemperatureId = input.Value };
                break;
            default:
                throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "field").WithData("value", (int)input.Field);
        }

        /* Empty: FreightDetails refuses it. Otherwise the id must exist; the same lookups give the names of the answer. */
        var units = await _references.NamesAsync(MasterDataReferenceKind.TransportUnit, new[] { freight.TransportUnitId }.Where(u => u != Guid.Empty), require: true, "transportUnitId");
        var products = await _references.NamesAsync(MasterDataReferenceKind.Product, new[] { freight.ProductId }.OfType<Guid>(), require: true, "productId");
        var temperatures = await _references.NamesAsync(MasterDataReferenceKind.Temperature, new[] { freight.TemperatureId }.OfType<Guid>(), require: true, "temperatureId");
        line.SetFreight(freight);

        /* ABP compares this stamp with the stored one on save (409). A line has no stamp of its own and the change is on a child, so
         * the order's row is touched too: it makes the save check the stamp and hand out a new one. */
        order.ConcurrencyStamp = input.ConcurrencyStamp;
        order.LastModificationTime = Clock.Now;
        await _orders.UpdateAsync(order, autoSave: true);

        var changes = new LineChanges();
        changes.LineIds.Add(lineId);
        await _commands.PublishLinesChangedAsync(order, changes);
        return new PlanboardOrderLineFieldResultDto
        {
            Id = order.Id,
            LineId = lineId,
            TransportUnitId = freight.TransportUnitId,
            TransportUnitName = units.GetValueOrDefault(freight.TransportUnitId),
            ProductId = freight.ProductId,
            ProductName = freight.ProductId == null ? null : products.GetValueOrDefault(freight.ProductId.Value),
            TemperatureId = freight.TemperatureId,
            TemperatureName = freight.TemperatureId == null ? null : temperatures.GetValueOrDefault(freight.TemperatureId.Value),
            ConcurrencyStamp = order.ConcurrencyStamp,
            LastModificationTime = order.LastModificationTime
        };
    }
}
