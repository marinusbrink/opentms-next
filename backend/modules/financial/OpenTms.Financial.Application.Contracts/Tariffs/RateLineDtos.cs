using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.Financial.Tariffs;

/// <summary>A stored rate line as the order and trip editors show it (design 57, design 61).</summary>
public class RateLineDto
{
    public Guid Id { get; set; }
    public RateSide Side { get; set; }
    public RateLineKind Kind { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? OrderLineId { get; set; }
    public Guid? LegId { get; set; }
    public Guid? StopId { get; set; }
    public Guid? TripId { get; set; }
    public int Position { get; set; }
    public string? DescriptionKey { get; set; }
    public string? Description { get; set; }
    public decimal Quantity { get; set; }
    public QuantitySource QuantitySource { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "EUR";
    public bool OverridesChildren { get; set; }
    public bool IsManual { get; set; }
    public bool IsAdjusted { get; set; }
    public bool AppliesAfterOthers { get; set; }
    public Guid? PriceListId { get; set; }
    public string? PriceListCode { get; set; }
    public Guid? PriceListRuleId { get; set; }
    public Guid? PriceListBracketId { get; set; }
    public int? ColumnPosition { get; set; }
    public Guid? DiscountOrSurchargeId { get; set; }
    public string? DiscountOrSurchargeName { get; set; }
    public Guid? AdditionalServiceId { get; set; }
    public string? AdditionalServiceName { get; set; }
    public Guid? VatPercentageId { get; set; }
    public DateTimeOffset? CalculatedAt { get; set; }
    public long? CalculationVersion { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;

    /// <summary>The trip a cost line belongs to (set for trip and leg anchors; <see cref="TripId"/> is only the trip anchor itself).</summary>
    public Guid? AnchorTripId { get; set; }

    /// <summary>The number of <see cref="AnchorTripId"/>; filled by the order's cost read (<c>side = Cost</c>), else null.</summary>
    public string? TripNumber { get; set; }

    /// <summary>The order line (freight item) of the leg a cost line hangs on; filled by the order's cost read, else null.</summary>
    public Guid? LegOrderLineId { get; set; }
}

public class RateLineAnchorTotalsDto
{
    public Guid AnchorId { get; set; }
    public decimal Amount { get; set; }
}

public class RateLineSetTotalsDto
{
    public List<RateLineAnchorTotalsDto> ByAnchor { get; set; } = new();
    public decimal LinesAmount { get; set; }
    public decimal DiscountsAndSurchargesAmount { get; set; }
    public decimal AdditionalServicesAmount { get; set; }
    public decimal Total { get; set; }
    public bool HasFixedPrice { get; set; }
}

/// <summary>The lines of one order or trip with their totals (AD-08) and the derived-value state the screen shows "calculating" from.</summary>
public class RateLineSetDto
{
    public List<RateLineDto> Lines { get; set; } = new();
    public RateLineSetTotalsDto Totals { get; set; } = new();
    public string? CurrencyCode { get; set; }
    /// <summary>Null when no calculation was requested yet.</summary>
    public DerivedValueStateDto? State { get; set; }
}

/// <summary>
/// The lines of one order or trip. With <see cref="OrderId"/> and <see cref="Side"/> = <c>Cost</c> (design 61 "Order page
/// review" point 4): the cost lines of the order's legs on the trips they are on now (leg-anchored lines of those trips;
/// trip-level lines such as the agreed price are not per freight item and stay out), each with its trip and freight item;
/// the totals then sum those lines per leg, the state is the trips' cost calculation. Without a side, or <c>Revenue</c>, the
/// order's own lines as before; for a trip the side is ignored (a trip's lines are all cost).
/// </summary>
public class RateLineGetRequestDto
{
    public Guid? OrderId { get; set; }
    public Guid? TripId { get; set; }
    public RateSide? Side { get; set; }
}

/// <summary>
/// The anchor of a new manual line: the root (order or trip) and optionally the child record beneath it. A trip's line on a
/// leg may name the leg's order line too (design 61 point 4, a purchase line of a freight item: <c>tripId</c>,
/// <c>orderLineId</c> and the item's first <c>legId</c> on that trip); a leg not on that trip, or not of that order line, is
/// refused with <c>LegNotOnTrip</c>.
/// </summary>
public class ManualRateLineCreateDto
{
    public Guid? OrderId { get; set; }
    public Guid? TripId { get; set; }
    public Guid? OrderLineId { get; set; }
    public Guid? StopId { get; set; }
    public Guid? LegId { get; set; }

    public RateSide Side { get; set; } = RateSide.Revenue;

    /// <summary>Manual or AdditionalService.</summary>
    public RateLineKind Kind { get; set; } = RateLineKind.Manual;

    [MaxLength(FinancialConsts.MaxRateLineDescriptionLength)]
    public string? Description { get; set; }

    public decimal Quantity { get; set; } = 1m;

    /// <summary>Manual = the typed quantity stays; a fact source re-values the line on every calculation (QP6).</summary>
    public QuantitySource QuantitySource { get; set; } = QuantitySource.Manual;

    public decimal? UnitPrice { get; set; }

    public Guid? AdditionalServiceId { get; set; }

    /// <summary>ISO 4217; defaults to the currency of the anchor's stored lines, else EUR.</summary>
    [MaxLength(ReferenceDataConsts.IsoCurrencyCodeLength)]
    public string? CurrencyCode { get; set; }
}

/// <summary>A manual line takes every field; a calculated line takes the unit price only (it becomes adjusted, QP6).</summary>
public class RateLineUpdateDto
{
    [MaxLength(FinancialConsts.MaxRateLineDescriptionLength)]
    public string? Description { get; set; }

    public decimal? Quantity { get; set; }

    public QuantitySource? QuantitySource { get; set; }

    public decimal? UnitPrice { get; set; }

    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class FixedPriceDto
{
    public Guid OrderId { get; set; }

    public decimal Amount { get; set; }

    [MaxLength(ReferenceDataConsts.IsoCurrencyCodeLength)]
    public string? CurrencyCode { get; set; }
}

public class RateLineRecalculateDto
{
    [MaxLength(DerivedValueConsts.MaxTargetsPerRequest)]
    public List<Guid> OrderIds { get; set; } = new();

    [MaxLength(DerivedValueConsts.MaxTargetsPerRequest)]
    public List<Guid> TripIds { get; set; } = new();
}

public interface IRateLineAppService : IApplicationService
{
    Task<RateLineSetDto> GetAsync(RateLineGetRequestDto input);
    Task<RateLineDto> CreateManualAsync(ManualRateLineCreateDto input);
    Task<RateLineDto> UpdateAsync(Guid id, RateLineUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<RateLineDto> SetFixedPriceAsync(FixedPriceDto input);
    Task RemoveFixedPriceAsync(Guid orderId);
    Task<int> RecalculateAsync(RateLineRecalculateDto input);
}
