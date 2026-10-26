using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.Orders.TransportOrders;

namespace OpenTms.Orders.Published;

/// <summary>
/// Names and statuses of orders for the grids of other modules (Planning's trips show their orders' numbers and
/// customers; design 56 "Ownership"). In-process only; ids in, short references out; at most <see cref="MaxKeys"/> ids.
/// </summary>
public interface ITransportOrderLookup
{
    Task<IReadOnlyList<TransportOrderReferenceDto>> GetAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default);

    /// <summary>The order's "Tevens trip" request (design 58, PO decision Q9), or null when it has none; read by Planning on <c>TransportOrderConfirmed</c>.</summary>
    Task<TripRequestDto?> GetTripRequestAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>The waypoint types by id (activity, successor, default stay), for Planning's via-depot departure (design 59 "Generation"); unknown ids are absent.</summary>
    Task<IReadOnlyList<WaypointTypeReferenceDto>> GetWaypointTypesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    const int MaxKeys = 500;
}

public class WaypointTypeReferenceDto
{
    public Guid Id { get; set; }

    public WaypointActivity Activity { get; set; }

    /// <summary>The type a stop of this type is followed by (the depot's departure activity after its arrival), when the tenant set one.</summary>
    public Guid? SuccessiveWaypointTypeId { get; set; }

    public int? DefaultDurationMinutes { get; set; }
}

public class TripRequestDto
{
    public Guid OrderId { get; set; }

    public Guid TransportTripTypeId { get; set; }

    public Guid DepartureRelationId { get; set; }

    public Guid DepartureAddressId { get; set; }

    public Guid ArrivalRelationId { get; set; }

    public Guid ArrivalAddressId { get; set; }

    public DateTimeOffset PlannedDeparture { get; set; }

    public DateTimeOffset? PlannedArrival { get; set; }

    public Guid? CarrierRelationId { get; set; }

    public Guid? CarrierContactPersonId { get; set; }

    public Guid? VehicleCombinationId { get; set; }

    public List<Guid> VehicleIds { get; set; } = new();

    public List<Guid> DriverIds { get; set; } = new();

    public decimal? AgreedPurchaseAmount { get; set; }

    public string? AgreedPurchaseCurrencyCode { get; set; }

    /// <summary>
    /// The order lines whose legs go on the trip (design 61 point 7): null = every line; otherwise only the legs of these
    /// lines, and an empty list (the selection lost its last line) means no trip.
    /// </summary>
    public List<Guid>? LineIds { get; set; }

    /// <summary>Set by Orders once Planning reported the created trip.</summary>
    public Guid? TripId { get; set; }
}

public class TransportOrderReferenceDto
{
    public Guid Id { get; set; }

    public string? Number { get; set; }

    public TransportOrderStatus Status { get; set; }

    public Guid CustomerRelationId { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public string? CustomerReference { get; set; }
}
