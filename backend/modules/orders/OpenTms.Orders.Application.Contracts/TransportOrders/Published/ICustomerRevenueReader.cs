using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Orders.TransportOrders.Published;

/// <summary>
/// The revenue of one customer in one SQL aggregate, for the AI connector (design 74 "get_customer_revenue"). In-process
/// only, tenant-filtered, reads the stored <c>TransportOrder.RevenueAmount</c> (AD-07) and never calculates. It has no
/// permission of its own: its caller authorizes.
/// Definition: calculated revenue on orders, not invoiced revenue. Orders count by order date with status Open,
/// ReadyForInvoicing, Invoiced or Archived; Draft and Cancelled do not count and are counted separately, and so is a counted
/// order without an amount.
/// </summary>
public interface ICustomerRevenueReader
{
    /// <param name="customerRelationId">The customer.</param>
    /// <param name="dateFrom">First order date, inclusive.</param>
    /// <param name="dateTo">Last order date, inclusive.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<CustomerRevenueDto> GetAsync(Guid customerRelationId, DateOnly dateFrom, DateOnly dateTo, CancellationToken cancellationToken = default);
}

public class CustomerRevenueDto
{
    /// <summary>One row per currency, in currency order; only orders with an amount.</summary>
    public List<CustomerRevenueTotalDto> Totals { get; set; } = new();

    /// <summary>Counted orders whose revenue is not calculated yet: in no total, never as zero.</summary>
    public int OrdersWithoutAmount { get; set; }

    public int ExcludedDraftCount { get; set; }

    public int ExcludedCancelledCount { get; set; }
}

public class CustomerRevenueTotalDto
{
    public string CurrencyCode { get; set; } = string.Empty;

    public decimal RevenueAmount { get; set; }

    public int OrderCount { get; set; }
}
