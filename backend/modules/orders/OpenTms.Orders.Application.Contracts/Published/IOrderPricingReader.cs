using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.Financial.Tariffs.Published;

namespace OpenTms.Orders.Published;

/// <summary>
/// The facts of an order a price calculation needs (design 56 "The three flows, applied": a synchronous cross-module
/// read inside Financial's calculator job and the price preview; design 58 "Published contract"). In-process only,
/// tenant-filtered like every repository read; returns no names, only ids, codes and quantities.
/// </summary>
public interface IOrderPricingReader
{
    /// <summary>The snapshot of one order, or null when it does not exist in the current tenant.</summary>
    Task<PricingSnapshotDto?> GetSnapshotAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>The snapshots of several orders (at most <see cref="MaxOrders"/>), missing ones absent.</summary>
    Task<IReadOnlyList<PricingSnapshotDto>> GetSnapshotsAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default);

    const int MaxOrders = 1_000;
}
