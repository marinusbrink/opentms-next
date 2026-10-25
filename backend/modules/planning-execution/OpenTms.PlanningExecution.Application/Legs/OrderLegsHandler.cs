using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Orders.Events;
using OpenTms.Orders.Published;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;

namespace OpenTms.PlanningExecution.Legs;

/// <summary>
/// Planning's reaction to the Orders facts (design 59 "Generation", design 58 "Events"): a confirmed order gets its legs
/// and, with a trip request, its trip ("Tevens trip", PO Q9, created once); changed stops or lines regenerate the legs;
/// a cancelled order loses them. At-least-once delivery: every step is idempotent on the order's current facts.
/// </summary>
public class OrderLegsHandler :
    IDistributedEventHandler<TransportOrderConfirmedEto>,
    IDistributedEventHandler<TransportOrderStopsChangedEto>,
    IDistributedEventHandler<TransportOrderLinesChangedEto>,
    IDistributedEventHandler<TransportOrderCancelledEto>,
    ITransientDependency
{
    public ILogger<OrderLegsHandler> Logger { get; set; } = NullLogger<OrderLegsHandler>.Instance;

    private readonly OrderLegGeneration _generation;
    private readonly ITransportOrderLookup _orders;
    private readonly IRepository<TransportTrip, Guid> _trips;
    private readonly IRepository<Leg, Guid> _legs;
    private readonly TripOperations _tripOperations;

    public OrderLegsHandler(OrderLegGeneration generation, ITransportOrderLookup orders, IRepository<TransportTrip, Guid> trips, IRepository<Leg, Guid> legs, TripOperations tripOperations)
    {
        _generation = generation;
        _orders = orders;
        _trips = trips;
        _legs = legs;
        _tripOperations = tripOperations;
    }

    [UnitOfWork]
    public virtual async Task HandleEventAsync(TransportOrderConfirmedEto eventData)
    {
        await _generation.RegenerateAsync(eventData.OrderId, "OrderConfirmed");
        await CreateRequestedTripAsync(eventData.OrderId);
    }

    [UnitOfWork]
    public virtual Task HandleEventAsync(TransportOrderStopsChangedEto eventData) => _generation.RegenerateAsync(eventData.OrderId, "StopsChanged");

    [UnitOfWork]
    public virtual Task HandleEventAsync(TransportOrderLinesChangedEto eventData) => _generation.RegenerateAsync(eventData.OrderId, "LinesChanged");

    [UnitOfWork]
    public virtual Task HandleEventAsync(TransportOrderCancelledEto eventData) => _generation.RemoveAllAsync(eventData.OrderId, "OrderCancelled");

    /// <summary>The order's trip request, honoured once: an existing trip created from the order is the natural key.</summary>
    private async Task CreateRequestedTripAsync(Guid orderId)
    {
        var request = await _orders.GetTripRequestAsync(orderId);
        if (request == null)
        {
            return;
        }

        if (request.TripId != null || await _trips.AnyAsync(t => t.CreatedFromOrderId == orderId))
        {
            Logger.LogDebug("Trip request of order {OrderId} was handled before; skipped.", orderId);
            return;
        }

        TransportTrip trip;
        try
        {
            trip = await _tripOperations.CreateAsync(new TransportTripCreateDto
        {
            TransportTripTypeId = request.TransportTripTypeId,
            TripDate = DateOnly.FromDateTime(request.PlannedDeparture.Date),
            DepartureRelationId = request.DepartureRelationId,
            DepartureAddressId = request.DepartureAddressId,
            ArrivalRelationId = request.ArrivalRelationId,
            ArrivalAddressId = request.ArrivalAddressId,
            PlannedDeparture = request.PlannedDeparture,
            PlannedArrival = request.PlannedArrival,
            CarrierRelationId = request.CarrierRelationId,
            CarrierContactPersonId = request.CarrierContactPersonId,
            VehicleCombinationId = request.VehicleCombinationId,
            VehicleIds = request.VehicleIds.ToList(),
            DriverIds = request.DriverIds.ToList(),
            Execution = request.CarrierRelationId == null ? null : TripExecution.Subcontracted
        }, orderId, request.AgreedPurchaseAmount, request.AgreedPurchaseCurrencyCode);
        }
        catch (BusinessException ex) when (ex.Code == ReferenceDataErrorCodes.ReferenceNotFound)
        {
            /* Orders stores the request's ids unchecked against Planning (design 58 build note 8): a trip type, relation,
             * vehicle or driver that does not resolve here is a request Planning cannot honour. TripOperations validates
             * before it writes, so nothing is left behind; the legs stay unplanned. Failing instead would make the
             * at-least-once delivery retry the event for ever and block the tenant's outbox. */
            Logger.LogWarning("Trip request of order {OrderId} not honoured: {Field} does not resolve; the legs stay unplanned.", orderId, ex.Data["field"]);
            return;
        }

        var legs = await _legs.GetListAsync(l => l.OrderId == orderId && l.TripId == null);
        if (legs.Count > 0)
        {
            await _tripOperations.AssignAsync(trip, legs, allowOutsideWindow: true, afterStopId: null, moving: false);
        }

        Logger.LogInformation("Trip {TripId} created from the request of order {OrderId} with {LegCount} legs.", trip.Id, orderId, legs.Count);
    }
}
