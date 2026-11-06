using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Orders.Published;

/// <summary>
/// Design 74 §7, the documented exception of CLAUDE.md rule 3: Planning's <c>POST leg/{id}/split-order-line</c> splits the order line
/// and its legs in one unit of work, which an event could not make atomic. In-process only, inside the caller's unit of work and
/// tenant; the caller authorizes.
/// </summary>
public interface IOrderLineSplitter
{
    /// <summary>
    /// Clones the line at the end of the order: new line and stop ids, the new line takes <paramref name="quantity"/>, the quantities and
    /// measurements scale pro rata with the total exact, and <c>TransportOrderLinesChanged</c> is published for both lines. Refused
    /// when the order cannot be edited or either part would not be above zero.
    /// </summary>
    Task<OrderLineSplitResultDto> SplitAsync(Guid orderId, Guid lineId, decimal quantity, CancellationToken cancellationToken = default);
}

public class OrderLineSplitResultDto
{
    public Guid NewLineId { get; set; }

    /// <summary>The line's quantity before the split.</summary>
    public decimal TotalQuantity { get; set; }

    public decimal RemainingQuantity { get; set; }

    public decimal NewQuantity { get; set; }

    /// <summary>Old stop id → new stop id, for every stop of the line.</summary>
    public Dictionary<Guid, Guid> StopIdMap { get; set; } = new();
}
