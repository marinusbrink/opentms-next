using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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

/// <summary>Design 74 §6: <c>PUT /api/orders/planboard-order/{id}/field</c>: one order field; empty or null clears it.</summary>
public class PlanboardOrderFieldUpdateDto
{
    [Required]
    public PlanboardOrderField Field { get; set; }

    [MaxLength(OrdersConsts.MaxRemarksLength)]
    public string? Value { get; set; }

    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

/// <summary>Design 74 §6: <c>PUT /api/orders/planboard-order/{id}/line/{lineId}/field</c>: one freight field; null clears a product or temperature, a transport unit is required.</summary>
public class PlanboardOrderLineFieldUpdateDto
{
    [Required]
    public PlanboardOrderLineField Field { get; set; }

    public Guid? Value { get; set; }

    /// <summary>The order's stamp: a line has none of its own.</summary>
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

/// <summary>The saved order fields and the new stamp, to patch every row of the order.</summary>
public class PlanboardOrderFieldResultDto
{
    public Guid Id { get; set; }

    public string? CustomerReference { get; set; }

    public string? InternalRemarks { get; set; }

    public string ConcurrencyStamp { get; set; } = string.Empty;

    public DateTime? LastModificationTime { get; set; }
}

/// <summary>The saved freight fields with their names and the order's new stamp, to patch the rows of the line.</summary>
public class PlanboardOrderLineFieldResultDto
{
    public Guid Id { get; set; }

    public Guid LineId { get; set; }

    public Guid TransportUnitId { get; set; }

    public string? TransportUnitName { get; set; }

    public Guid? ProductId { get; set; }

    public string? ProductName { get; set; }

    public Guid? TemperatureId { get; set; }

    public string? TemperatureName { get; set; }

    public string ConcurrencyStamp { get; set; } = string.Empty;

    public DateTime? LastModificationTime { get; set; }
}

/// <summary>Design 66 D7: gated on the Planboard's Orders pane, not on the Transport app. Design 74 §6: its inline order fields too.</summary>
public interface IPlanboardOrderAppService : IApplicationService
{
    Task<GridResponse<TransportOrderRowDto>> GetListAsync(PlanboardOrderListRequestDto input);

    /// <summary><c>PUT /api/orders/planboard-order/{id}/field</c>.</summary>
    Task<PlanboardOrderFieldResultDto> UpdateFieldAsync(Guid id, PlanboardOrderFieldUpdateDto input);
}

/// <summary>
/// Design 74 §6: <c>PUT /api/orders/planboard-order/{id}/line/{lineId}/field</c>. Its own service because a conventional route cannot
/// put the action after the line's id; reached only through <c>PlanboardOrderLineController</c>.
/// </summary>
public interface IPlanboardOrderLineFieldAppService : IApplicationService
{
    Task<PlanboardOrderLineFieldResultDto> UpdateLineFieldAsync(Guid id, Guid lineId, PlanboardOrderLineFieldUpdateDto input);
}
