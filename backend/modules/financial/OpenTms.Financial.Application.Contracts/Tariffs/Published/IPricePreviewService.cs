using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Financial.Tariffs.Published;

/// <summary>
/// The one synchronous Financial call a request may make (design 56, pricing assessment §4.3): the would-be revenue rate
/// lines of a pricing snapshot, so the order screen shows a price while the clerk types. Pure: the same resolver as the
/// calculator, no persistence, no event. Budget p95 &lt; 500 ms for an order of 20 lines.
/// </summary>
public interface IPricePreviewService
{
    Task<PricePreviewDto> PreviewAsync(PricingSnapshotDto snapshot, CancellationToken cancellationToken = default);
}

public class PricePreviewDto
{
    public string CurrencyCode { get; set; } = "EUR";

    public List<PricePreviewLineDto> Lines { get; set; } = new();

    public decimal LinesAmount { get; set; }

    public decimal DiscountsAndSurchargesAmount { get; set; }

    public decimal TotalAmount { get; set; }

    /// <summary>Warning codes of the resolver (a list skipped for its currency, a line without distance on a per-km list).</summary>
    public List<string> Warnings { get; set; } = new();
}

public class PricePreviewLineDto
{
    public System.Guid? OrderLineId { get; set; }

    /// <summary>The 1-based position of the freight item in the posted snapshot, saved or not; null for an order-level line.</summary>
    public int? LinePosition { get; set; }

    public RateLineKind Kind { get; set; }

    public string? DescriptionKey { get; set; }

    public decimal Quantity { get; set; }

    public QuantitySource QuantitySource { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal Amount { get; set; }

    public System.Guid? PriceListId { get; set; }

    public System.Guid? PriceListRuleId { get; set; }

    public System.Guid? DiscountOrSurchargeId { get; set; }
}
