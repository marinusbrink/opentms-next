using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.Orders.TransportOrders;

namespace OpenTms.Orders.Published;

/// <summary>
/// What Reporting prepares its order facts from (design 75, module boundary rule 3). In-process only, tenant-filtered, reads
/// stored values and never calculates; it has no permission of its own, because its callers are a derived-value job, two
/// workers and <c>IMetricQuery</c>, which authorizes itself.
/// </summary>
public interface IOrderFactReader
{
    public const int MaxKeys = 500;

    /// <summary>The order's current values; null when it does not exist or is deleted, so the fact goes too.</summary>
    Task<OrderFactSourceDto?> GetAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>All orders that are not deleted: a backfill is finished when the facts count the same.</summary>
    Task<long> GetCountAsync(CancellationToken cancellationToken = default);

    /// <summary>The next ids in id order, after <paramref name="afterId"/>; deleted orders are left out.</summary>
    Task<List<Guid>> GetIdsAfterAsync(Guid? afterId, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// The ids of orders created, changed or deleted since <paramref name="since"/>, in id order. Soft-deleted orders are
    /// included, so a lost delete can be repaired: the calculator finds no order and removes the fact.
    /// </summary>
    Task<List<Guid>> GetIdsModifiedSinceAsync(DateTime since, Guid? afterId, int take, CancellationToken cancellationToken = default);

    /// <summary>The order types' names in the current culture; unknown ids are absent. At most <see cref="MaxKeys"/>.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetTypeNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}

public class OrderFactSourceDto
{
    public Guid Id { get; set; }

    public DateOnly OrderDate { get; set; }

    public TransportOrderStatus Status { get; set; }

    public Guid CustomerRelationId { get; set; }

    public Guid? OrganizationUnitId { get; set; }

    public Guid TransportOrderTypeId { get; set; }

    public string CurrencyCode { get; set; } = string.Empty;

    public decimal? RevenueAmount { get; set; }

    public decimal? CostOfRevenueAmount { get; set; }

    public decimal? MarginAmount { get; set; }

    public decimal? Co2Kg { get; set; }
}
