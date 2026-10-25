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

    const int MaxKeys = 500;
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
