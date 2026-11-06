using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Orders.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// The Planboard's Orders pane (design 70; route <c>/api/orders/planboard-order</c>). A separate service because the
/// Transport list is gated on the Transport app and a tenant may have the Planboard without it (design 66 D7). The
/// filters, search and sort are the Transport list's (<see cref="TransportOrderListQuery"/>). Design 74 §6: the pane's inline
/// fields, a narrow command against the order's stamp, as the Transport page's header save: no fact, the summary follows.
/// </summary>
[RequiresFeature(AppFeatures.Planboard, AppFeatures.PlanboardApp.Orders, RequiresAll = true)]
public class PlanboardOrderAppService : OrdersAppServiceBase, IPlanboardOrderAppService
{
    private readonly IRepository<TransportOrder, Guid> _orders;
    private readonly TransportOrderDtoMapper _mapper;
    private readonly OrdersClock _clock;
    private readonly TransportOrderCommands _commands;

    public PlanboardOrderAppService(IRepository<TransportOrder, Guid> orders, TransportOrderDtoMapper mapper, OrdersClock clock, TransportOrderCommands commands)
    {
        _orders = orders;
        _mapper = mapper;
        _clock = clock;
        _commands = commands;
    }

    [Authorize(OrdersPermissions.TransportOrders.Default)]
    public virtual async Task<TransportOrderRowDto> GetAsync(Guid id)
        => (await _mapper.ToRowsAsync([await _orders.GetAsync(id, includeDetails: false)])).Single();

    [Authorize(OrdersPermissions.TransportOrders.Update)]
    public virtual async Task<PlanboardOrderFieldResultDto> UpdateFieldAsync(Guid id, PlanboardOrderFieldUpdateDto input)
    {
        var order = await _orders.GetAsync(id);
        order.BeginEdit();
        /* ABP compares this stamp with the stored one on save and throws AbpDbConcurrencyException (409). */
        order.ConcurrencyStamp = input.ConcurrencyStamp;
        switch (input.Field)
        {
            case PlanboardOrderField.CustomerReference:
                order.SetCustomerReference(input.Value);
                break;
            case PlanboardOrderField.InternalRemarks:
                order.SetInternalRemarks(input.Value);
                break;
            default:
                throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "field").WithData("value", (int)input.Field);
        }

        await _orders.UpdateAsync(order, autoSave: true);
        await _commands.RequestSummaryAsync(order, "HeaderChanged");
        return new PlanboardOrderFieldResultDto
        {
            Id = order.Id,
            CustomerReference = order.CustomerReference,
            InternalRemarks = order.InternalRemarks,
            ConcurrencyStamp = order.ConcurrencyStamp,
            LastModificationTime = order.LastModificationTime
        };
    }

    [Authorize(OrdersPermissions.TransportOrders.Default)]
    public virtual async Task<GridResponse<TransportOrderRowDto>> GetListAsync(PlanboardOrderListRequestDto input)
    {
        var query = TransportOrderListQuery.Filter(await _orders.GetQueryableAsync(), input.Status, TransportOrderView.All, null, null, null, null, null, _clock.Today());
        if (input.PlannedFrom is { } from)
        {
            query = query.Where(o => (o.LastStopDate ?? o.FirstStopDate) >= from);
        }

        if (input.PlannedTo is { } to)
        {
            query = query.Where(o => o.FirstStopDate <= to);
        }

        if (input.OrganizationUnitIds.Count > 0)
        {
            var units = input.OrganizationUnitIds.Distinct().ToList();
            query = query.Where(o => o.OrganizationUnitId != null && units.Contains(o.OrganizationUnitId.Value));
        }

        var term = input.WildcardSearch?.Trim().ToLowerInvariant();
        var searched = string.IsNullOrEmpty(term) ? query : TransportOrderListQuery.Search(query, term);
        var totalCount = await AsyncExecuter.LongCountAsync(query);
        var filteredCount = string.IsNullOrEmpty(term) ? totalCount : await AsyncExecuter.LongCountAsync(searched);
        var page = await AsyncExecuter.ToListAsync(TransportOrderListQuery.Sort(searched, input.SortBy, input.Descending).Skip(input.StartRow).Take(input.EndRow - input.StartRow));
        return new GridResponse<TransportOrderRowDto> { Rows = await _mapper.ToRowsAsync(page), TotalCount = totalCount, FilteredCount = filteredCount };
    }
}
