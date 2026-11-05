using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace OpenTms.Orders.TransportOrders.Published;

/// <summary><see cref="ICustomerRevenueReader"/>: one grouped query per call, over the index (TenantId, CustomerRelationId, OrderDate).</summary>
// Published in-process reader: the AI connector's tool authorizes (design 74 "get_customer_revenue"), so it is anonymous here and has no HTTP route.
[RemoteService(IsEnabled = false)]
[AllowAnonymous]
public class CustomerRevenueReader : OrdersAppServiceBase, ICustomerRevenueReader
{
    private readonly IRepository<TransportOrder, Guid> _orders;

    public CustomerRevenueReader(IRepository<TransportOrder, Guid> orders)
    {
        _orders = orders;
    }

    [UnitOfWork(isTransactional: false)]
    public virtual async Task<CustomerRevenueDto> GetAsync(Guid customerRelationId, DateOnly dateFrom, DateOnly dateTo, CancellationToken cancellationToken = default)
    {
        var query = (await _orders.GetQueryableAsync())
            .Where(o => o.CustomerRelationId == customerRelationId && o.OrderDate >= dateFrom && o.OrderDate <= dateTo)
            .GroupBy(o => new { o.Status, o.FinancialTerms.CurrencyCode })
            .Select(g => new
            {
                g.Key.Status,
                g.Key.CurrencyCode,
                Revenue = g.Sum(o => o.RevenueAmount),
                WithAmount = g.Count(o => o.RevenueAmount != null),
                WithoutAmount = g.Count(o => o.RevenueAmount == null),
                All = g.Count(),
            });
        var rows = await AsyncExecuter.ToListAsync(query, cancellationToken);

        var counted = rows.Where(r => r.Status is TransportOrderStatus.Open or TransportOrderStatus.ReadyForInvoicing or TransportOrderStatus.Invoiced or TransportOrderStatus.Archived).ToList();
        return new CustomerRevenueDto
        {
            Totals = counted
                .Where(r => r.WithAmount > 0)
                .GroupBy(r => r.CurrencyCode)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new CustomerRevenueTotalDto
                {
                    CurrencyCode = g.Key,
                    RevenueAmount = g.Sum(r => r.Revenue ?? 0m),
                    OrderCount = g.Sum(r => r.WithAmount),
                })
                .ToList(),
            OrdersWithoutAmount = counted.Sum(r => r.WithoutAmount),
            ExcludedDraftCount = rows.Where(r => r.Status == TransportOrderStatus.Draft).Sum(r => r.All),
            ExcludedCancelledCount = rows.Where(r => r.Status == TransportOrderStatus.Cancelled).Sum(r => r.All),
        };
    }
}
