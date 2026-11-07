using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenTms.Orders.TransportOrders;

namespace OpenTms.Controllers.Orders;

/// <summary>
/// Design 74 §6: <c>PUT /api/orders/planboard-order/{id}/line/{lineId}/field</c>. A conventional controller cannot put the action
/// after the line's id, so the application service is <c>[RemoteService(IsEnabled = false)]</c> and checks the permission and feature itself.
/// </summary>
[ApiController]
[Route("api/orders/planboard-order")]
[Authorize]
public class PlanboardOrderLineController : OpenTmsController
{
    private readonly IPlanboardOrderLineFieldAppService _lines;

    public PlanboardOrderLineController(IPlanboardOrderLineFieldAppService lines)
    {
        _lines = lines;
    }

    [HttpPut("{id:guid}/line/{lineId:guid}/field")]
    public Task<PlanboardOrderLineFieldResultDto> UpdateLineFieldAsync([FromRoute] Guid id, [FromRoute] Guid lineId, [FromBody] PlanboardOrderLineFieldUpdateDto input)
        => _lines.UpdateLineFieldAsync(id, lineId, input);
}
