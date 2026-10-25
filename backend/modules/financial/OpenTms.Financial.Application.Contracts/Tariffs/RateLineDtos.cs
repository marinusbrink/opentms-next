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

public class RateLineGetRequestDto
{
    public Guid? OrderId { get; set; }
    public Guid? TripId { get; set; }
}

/// <summary>The anchor of a new manual line: the root (order or trip) and optionally the child record beneath it.</summary>
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
