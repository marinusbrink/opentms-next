using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace OpenTms.Orders.Published;

/// <summary>See <see cref="IOrderFactReader"/>: projections only, so no order is tracked; every query runs under the tenant filter.</summary>
// Published in-process reader: a derived-value job and two workers call it without a user, so it is anonymous; IMetricQuery authorizes for the one user-facing path.
[RemoteService(IsEnabled = false)]
[AllowAnonymous]
public class OrderFactReader : OrdersAppServiceBase, IOrderFactReader
{
    private readonly IRepository<TransportOrder, Guid> _orders;
    private readonly IRepository<TransportOrderType, Guid> _types;

    public OrderFactReader(IRepository<TransportOrder, Guid> orders, IRepository<TransportOrderType, Guid> types)
    {
        _orders = orders;
        _types = types;
    }

    [UnitOfWork(isTransactional: false)]
    public virtual async Task<OrderFactSourceDto?> GetAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var query = (await _orders.GetQueryableAsync())
            .Where(o => o.Id == orderId)
            .Select(o => new OrderFactSourceDto
            {
                Id = o.Id,
                OrderDate = o.OrderDate,
                Status = o.Status,
                CustomerRelationId = o.CustomerRelationId,
                OrganizationUnitId = o.OrganizationUnitId,
                TransportOrderTypeId = o.TransportOrderTypeId,
                CurrencyCode = o.FinancialTerms.CurrencyCode,
                RevenueAmount = o.RevenueAmount,
                CostOfRevenueAmount = o.CostOfRevenueAmount,
                MarginAmount = o.MarginAmount,
                Co2Kg = o.Co2Kg,
            });
        return await AsyncExecuter.FirstOrDefaultAsync(query, cancellationToken);
    }

    [UnitOfWork(isTransactional: false)]
    public virtual async Task<long> GetCountAsync(CancellationToken cancellationToken = default)
        => await AsyncExecuter.LongCountAsync(await _orders.GetQueryableAsync(), cancellationToken);

    [UnitOfWork(isTransactional: false)]
    public virtual async Task<List<Guid>> GetIdsAfterAsync(Guid? afterId, int take, CancellationToken cancellationToken = default)
    {
        var query = await _orders.GetQueryableAsync();
        if (afterId != null)
        {
            var after = afterId.Value;
            query = query.Where(o => o.Id.CompareTo(after) > 0);
        }

        return await AsyncExecuter.ToListAsync(query.OrderBy(o => o.Id).Select(o => o.Id).Take(take), cancellationToken);
    }

    [UnitOfWork(isTransactional: false)]
    public virtual async Task<List<Guid>> GetIdsModifiedSinceAsync(DateTime since, Guid? afterId, int take, CancellationToken cancellationToken = default)
    {
        /* Only the soft-delete filter is off, on purpose (design 75): a lost delete has to reach the calculator, and a soft delete moves
         * LastModificationTime (measured at build). A record never saved again has only its CreationTime. The tenant filter stays on. */
        using (DataFilter.Disable<ISoftDelete>())
        {
            var query = (await _orders.GetQueryableAsync()).Where(o => o.LastModificationTime >= since || o.CreationTime >= since);
            if (afterId != null)
            {
                var after = afterId.Value;
                query = query.Where(o => o.Id.CompareTo(after) > 0);
            }

            return await AsyncExecuter.ToListAsync(query.OrderBy(o => o.Id).Select(o => o.Id).Take(take), cancellationToken);
        }
    }

    [UnitOfWork(isTransactional: false)]
    public virtual async Task<IReadOnlyDictionary<Guid, string>> GetTypeNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        var keys = (ids ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (keys.Count > IOrderFactReader.MaxKeys)
        {
            throw new BusinessException(ReferenceDataErrorCodes.TooManyRows).WithData("max", IOrderFactReader.MaxKeys);
        }

        if (keys.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var types = await _types.GetListAsync(t => keys.Contains(t.Id), includeDetails: false, cancellationToken);
        return types.ToDictionary(t => t.Id, t => t.Name.Resolve(CultureInfo.CurrentUICulture));
    }
}
