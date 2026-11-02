using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// The Planboard's Orders pane (design 70): the order rows of the board's date range — on the first or last stop's
/// planned date, not the order date — and organisation units. Read-only; the same rows as the Transport list.
/// </summary>
public class PlanboardOrderListRequestDto : ReferenceDataListRequestDto
{
    public List<TransportOrderStatus> Status { get; set; } = new();

    /// <summary>Orders whose first-to-last stop dates overlap this range.</summary>
    public DateOnly? PlannedFrom { get; set; }

    public DateOnly? PlannedTo { get; set; }

    /// <summary>Orders of any of these units; empty = all.</summary>
    public List<Guid> OrganizationUnitIds { get; set; } = new();
}

/// <summary>Design 66 D7: gated on the Planboard's Orders pane, not on the Transport app.</summary>
public interface IPlanboardOrderAppService : IApplicationService
{
    Task<GridResponse<TransportOrderRowDto>> GetListAsync(PlanboardOrderListRequestDto input);
}
