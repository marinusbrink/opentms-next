using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.Orders.Published;

/// <summary>See <see cref="IOrderPricingReader"/>: the pricing snapshot of stored orders for Financial's calculator (in-process only).</summary>
// Published in-process reader: outbox handlers and derived-value jobs call it without a user, so it is anonymous; the HTTP-facing caller authorizes.
[RemoteService(IsEnabled = false)]
[AllowAnonymous]
public class OrderPricingReader : OrdersAppServiceBase, IOrderPricingReader
{
    private readonly IRepository<TransportOrder, Guid> _orders;
    private readonly PricingSnapshotBuilder _snapshots;

    public OrderPricingReader(IRepository<TransportOrder, Guid> orders, PricingSnapshotBuilder snapshots)
    {
        _orders = orders;
        _snapshots = snapshots;
    }

    public virtual async Task<PricingSnapshotDto?> GetSnapshotAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FindAsync(orderId, includeDetails: true, cancellationToken);
        return order == null ? null : await _snapshots.BuildAsync(order);
    }

    public virtual async Task<IReadOnlyList<PricingSnapshotDto>> GetSnapshotsAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
    {
        var keys = (orderIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (keys.Count > IOrderPricingReader.MaxOrders)
        {
            throw new BusinessException(ReferenceDataErrorCodes.TooManyRows).WithData("max", IOrderPricingReader.MaxOrders);
        }

        if (keys.Count == 0)
        {
            return [];
        }

        var orders = await _orders.GetListAsync(o => keys.Contains(o.Id), includeDetails: true, cancellationToken);
        return await _snapshots.BuildManyAsync(orders);
    }
}

/// <summary>See <see cref="ITransportOrderLookup"/>: short references and the trip request for Planning (in-process only).</summary>
// Published in-process reader: outbox handlers and derived-value jobs call it without a user, so it is anonymous; the HTTP-facing caller authorizes.
[RemoteService(IsEnabled = false)]
[AllowAnonymous]
public class TransportOrderLookup : OrdersAppServiceBase, ITransportOrderLookup
{
    private readonly IRepository<TransportOrder, Guid> _orders;
    private readonly IRepository<WaypointType, Guid> _waypointTypes;

    public TransportOrderLookup(IRepository<TransportOrder, Guid> orders, IRepository<WaypointType, Guid> waypointTypes)
    {
        _orders = orders;
        _waypointTypes = waypointTypes;
    }

    public virtual async Task<IReadOnlyList<WaypointTypeReferenceDto>> GetWaypointTypesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        var keys = (ids ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (keys.Count > ITransportOrderLookup.MaxKeys)
        {
            throw new BusinessException(ReferenceDataErrorCodes.TooManyRows).WithData("max", ITransportOrderLookup.MaxKeys);
        }

        if (keys.Count == 0)
        {
            return [];
        }

        var types = await _waypointTypes.GetListAsync(w => keys.Contains(w.Id), cancellationToken: cancellationToken);
        return types.Select(w => new WaypointTypeReferenceDto { Id = w.Id, Activity = w.Activity, SuccessiveWaypointTypeId = w.SuccessiveWaypointTypeId, DefaultDurationMinutes = w.DefaultDurationMinutes }).ToList();
    }

    public virtual async Task<IReadOnlyList<TransportOrderReferenceDto>> GetAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
    {
        var keys = (orderIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (keys.Count > ITransportOrderLookup.MaxKeys)
        {
            throw new BusinessException(ReferenceDataErrorCodes.TooManyRows).WithData("max", ITransportOrderLookup.MaxKeys);
        }

        if (keys.Count == 0)
        {
            return [];
        }

        var query = (await _orders.GetQueryableAsync())
            .Where(o => keys.Contains(o.Id))
            .Select(o => new TransportOrderReferenceDto
            {
                Id = o.Id, Number = o.Number, Status = o.Status, CustomerRelationId = o.CustomerRelationId, CustomerName = o.CustomerName, CustomerReference = o.CustomerReference
            });
        return await AsyncExecuter.ToListAsync(query, cancellationToken);
    }

    public virtual async Task<TripRequestDto?> GetTripRequestAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FindAsync(orderId, includeDetails: false, cancellationToken);
        if (order?.TripRequest is not { } t)
        {
            return null;
        }

        return new TripRequestDto
        {
            OrderId = order.Id,
            TransportTripTypeId = t.TransportTripTypeId,
            DepartureRelationId = t.DepartureRelationId,
            DepartureAddressId = t.DepartureAddressId,
            ArrivalRelationId = t.ArrivalRelationId,
            ArrivalAddressId = t.ArrivalAddressId,
            PlannedDeparture = t.PlannedDeparture,
            PlannedArrival = t.PlannedArrival,
            CarrierRelationId = t.CarrierRelationId,
            CarrierContactPersonId = t.CarrierContactPersonId,
            VehicleCombinationId = t.VehicleCombinationId,
            VehicleIds = t.VehicleIds.ToList(),
            DriverIds = t.DriverIds.ToList(),
            AgreedPurchaseAmount = t.AgreedPurchaseAmount,
            AgreedPurchaseCurrencyCode = t.AgreedPurchaseCurrencyCode,
            TripId = t.TripId
        };
    }
}
