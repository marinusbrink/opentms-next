using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Financial.Tariffs;

namespace OpenTms.Orders.TransportOrders;

/// <summary>Builders for the pure entity tests of design 58: an order with lines and stops without a database.</summary>
public static class TransportOrderBuilders
{
    public static readonly Guid TenantA = Guid.NewGuid();
    public static readonly Guid TypeId = Guid.NewGuid();
    public static readonly Guid CustomerId = Guid.NewGuid();
    public static readonly Guid UnitId = Guid.NewGuid();
    public static readonly Guid LoadType = Guid.NewGuid();
    public static readonly Guid UnloadType = Guid.NewGuid();
    public static readonly Guid DepotId = Guid.NewGuid();
    public static readonly Guid DepotAddressId = Guid.NewGuid();
    public static readonly Guid ShopId = Guid.NewGuid();
    public static readonly Guid ShopAddressId = Guid.NewGuid();

    public static OrderHeaderInput Header(DateOnly? date = null, string? customerReference = "PO-1")
        => new(TypeId, CustomerId, null, "Klant Foods B.V.", date ?? new DateOnly(2026, 10, 20), customerReference, null, null, null);

    public static FinancialTermsInput Terms(decimal? fixedPrice = null, IReadOnlyList<Guid>? priceLists = null, bool sendDocuments = false)
        => new("EUR", null, 30, priceLists ?? [], MultipleMatches.All, [], fixedPrice, sendDocuments);

    public static FreightInput Freight(decimal quantity = 2, double? weightKg = 100, string? remarks = null, PaymentInstruction payment = PaymentInstruction.NotSet, Guid? shipperId = null)
        => new(UnitId, null, null, null, null, quantity, null, null, weightKg, null, null, null, null, null, null, null, null, remarks, null, null, null, null, payment, null, null, null, null, shipperId, null);

    public static StopInput Load(DateOnly? date = null, Guid? id = null, string? reference = "LAAD-1", Guid? timeSlotId = null, TimeOnly? from = null, TimeOnly? till = null)
        => new(id, LoadType, DepotId, DepotAddressId, null, date ?? new DateOnly(2026, 10, 20), timeSlotId, from, till, reference, null, 30);

    public static StopInput Unload(DateOnly? date = null, Guid? id = null, string? reference = "LOS-1")
        => new(id, UnloadType, ShopId, ShopAddressId, null, date ?? new DateOnly(2026, 10, 21), null, null, null, reference, null, 20);

    public static LineInput Line(params StopInput[] stops) => Line(Freight(), stops);

    public static LineInput Line(FreightInput freight, params StopInput[] stops) => new(null, freight, [], [], stops, null);

    public static TransportOrder Order(OrderSource source = OrderSource.Screen, FinancialTermsInput? terms = null, params LineInput[] lines)
    {
        var order = new TransportOrder(Guid.NewGuid(), TenantA, Header(), terms ?? Terms(), source, Guid.NewGuid);
        order.SetDefaultStops([new DefaultStopInput(LoadType, null, null, null, 0, null, null, null, 30), new DefaultStopInput(UnloadType, null, null, null, 1, null, null, null, 20)]);
        order.SetLines(lines.Length == 0 ? [Line(Load(), Unload())] : lines, Guid.NewGuid);
        return order;
    }

    public static WaypointActivity? ActivityOf(Guid waypointTypeId)
        => waypointTypeId == LoadType ? WaypointActivity.Load : waypointTypeId == UnloadType ? WaypointActivity.Unload : null;

    public static TransportOrder Confirmed(string number = "TO-2026-00001", params LineInput[] lines)
    {
        var order = Order(OrderSource.Screen, null, lines);
        order.Confirm(number, TransportOrderRequiredFields.None, ActivityOf);
        return order;
    }

    public static TransportOrder InStatus(TransportOrderStatus status)
    {
        var order = Confirmed();
        switch (status)
        {
            case TransportOrderStatus.Draft:
                return Order();
            case TransportOrderStatus.Open:
                return order;
            case TransportOrderStatus.ReadyForInvoicing:
                order.Approve(TransportOrderStatus.ReadyForInvoicing);
                return order;
            case TransportOrderStatus.Invoiced:
                order.Approve(TransportOrderStatus.ReadyForInvoicing);
                order.MarkInvoiced();
                return order;
            case TransportOrderStatus.Archived:
                order.Approve(TransportOrderStatus.Archived);
                return order;
            case TransportOrderStatus.Cancelled:
                order.Cancel();
                return order;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }
    }

    public static Dictionary<string, string> Attributes(int count, int valueLength = 3)
        => Enumerable.Range(0, count).ToDictionary(i => "key" + i, i => new string('x', valueLength));
}
