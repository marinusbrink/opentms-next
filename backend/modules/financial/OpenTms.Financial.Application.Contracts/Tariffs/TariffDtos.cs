using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.Financial.Tariffs;

// ── Additional services ────────────────────────────────────────────────────────

public class AdditionalServiceRowDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ServiceUnit Unit { get; set; }
    /// <summary>The VAT category's name in the request culture; null = the tenant's default VAT.</summary>
    public string? VatPercentageName { get; set; }
    public bool IsArchived { get; set; }
    public string? Description { get; set; }
}

public class AdditionalServiceDto : ReferenceDataDetailDtoBase
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ServiceUnit Unit { get; set; }
    public Guid? VatPercentageId { get; set; }
    public string? VatPercentageName { get; set; }
    public string? RevenueLedgerAccount { get; set; }
    public string? CostLedgerAccount { get; set; }
    public bool IsArchived { get; set; }
}

public class AdditionalServiceCreateDto
{
    [Required, MaxLength(ReferenceDataConsts.MaxCodeLength)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(ReferenceDataConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(ReferenceDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public ServiceUnit Unit { get; set; } = ServiceUnit.Piece;

    /// <summary>Null = the tenant's default VAT category.</summary>
    public Guid? VatPercentageId { get; set; }

    [MaxLength(ReferenceDataConsts.MaxLedgerAccountLength)]
    public string? RevenueLedgerAccount { get; set; }

    [MaxLength(ReferenceDataConsts.MaxLedgerAccountLength)]
    public string? CostLedgerAccount { get; set; }

    public bool IsArchived { get; set; }
}

public class AdditionalServiceUpdateDto : AdditionalServiceCreateDto, IReferenceDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface IAdditionalServiceAppService : IApplicationService
{
    Task<GridResponse<AdditionalServiceRowDto>> GetListAsync(ArchivableListRequestDto input);
    Task<AdditionalServiceDto> GetAsync(Guid id);
    Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake);
    Task<AdditionalServiceDto> CreateAsync(AdditionalServiceCreateDto input);
    Task<AdditionalServiceDto> UpdateAsync(Guid id, AdditionalServiceUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input);
}

// ── Discounts and surcharges ───────────────────────────────────────────────────

public class DiscountOrSurchargeRowDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DiscountOrSurchargeDirection Direction { get; set; }
    public DiscountOrSurchargeValueKind ValueKind { get; set; }
    public DiscountOrSurchargeSide Side { get; set; }
    /// <summary>The value valid today; null when the first value lies in the future.</summary>
    public decimal? CurrentValue { get; set; }
    public string? CurrentCurrencyCode { get; set; }
    public int RegionCount { get; set; }
    public bool IsArchived { get; set; }
    public string? Description { get; set; }
}

/// <summary>A value as the editor sends it (§3.4: replace semantics; fixed rows must come back unchanged).</summary>
public class DiscountOrSurchargeValueInputDto
{
    public DateOnly ValidFrom { get; set; }

    public decimal Value { get; set; }

    /// <summary>ISO 4217; required for fixed amounts and per kilometre, absent for a percentage.</summary>
    [MaxLength(ReferenceDataConsts.IsoCurrencyCodeLength)]
    public string? CurrencyCode { get; set; }
}

public class DiscountOrSurchargeValueDto : DiscountOrSurchargeValueInputDto
{
    /// <summary>True when the value has taken effect (dated today or earlier): it can no longer change.</summary>
    public bool IsFixed { get; set; }
}

public class DiscountOrSurchargeRegionInputDto
{
    /// <summary>A Master Data geographical region.</summary>
    public Guid GeographicalRegionId { get; set; }

    public RegionAppliesAt AppliesAt { get; set; } = RegionAppliesAt.Either;
}

public class DiscountOrSurchargeRegionDto : DiscountOrSurchargeRegionInputDto
{
    /// <summary>The region's name from Master Data; empty when the region was deleted there.</summary>
    public string Name { get; set; } = string.Empty;
}

public class DiscountOrSurchargeDto : ReferenceDataDetailDtoBase
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DiscountOrSurchargeDirection Direction { get; set; }
    public DiscountOrSurchargeValueKind ValueKind { get; set; }
    public DiscountOrSurchargeSide Side { get; set; }
    public DaysOfWeek DaysOfWeek { get; set; }
    public Guid? VatPercentageId { get; set; }
    public string? VatPercentageName { get; set; }
    public string? LedgerAccount { get; set; }
    public bool AppliesAfterOthers { get; set; }
    public KilometreBasis KilometreBasis { get; set; } = KilometreBasis.Distance;
    public decimal BillableKilometrePercentage { get; set; } = 100m;
    public bool RoundTrip { get; set; }
    /// <summary>Ordered by date.</summary>
    public List<DiscountOrSurchargeValueDto> Values { get; set; } = new();
    public List<DiscountOrSurchargeRegionDto> Regions { get; set; } = new();
    public bool IsArchived { get; set; }
}

public class DiscountOrSurchargeCreateDto
{
    [Required, MaxLength(ReferenceDataConsts.MaxCodeLength)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(ReferenceDataConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(ReferenceDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public DiscountOrSurchargeDirection Direction { get; set; } = DiscountOrSurchargeDirection.Surcharge;

    public DiscountOrSurchargeValueKind ValueKind { get; set; } = DiscountOrSurchargeValueKind.FixedAmount;

    public DiscountOrSurchargeSide Side { get; set; } = DiscountOrSurchargeSide.Revenue;

    /// <summary>Flags, Monday = 1 … Sunday = 64; never empty.</summary>
    public DaysOfWeek DaysOfWeek { get; set; } = DaysOfWeek.All;

    public Guid? VatPercentageId { get; set; }

    [MaxLength(ReferenceDataConsts.MaxLedgerAccountLength)]
    public string? LedgerAccount { get; set; }

    /* Design 57: the second pass and the per-kilometre modifiers (scenarios 41, 47–51). */

    /// <summary>A percentage of the second pass applies over the lines plus the first pass.</summary>
    public bool AppliesAfterOthers { get; set; }

    /// <summary>For a per-kilometre value: the route's kilometres or only the tolled ones.</summary>
    public KilometreBasis KilometreBasis { get; set; } = KilometreBasis.Distance;

    /// <summary>0–100, two decimals: the billable share of the kilometres.</summary>
    public decimal BillableKilometrePercentage { get; set; } = 100m;

    public bool RoundTrip { get; set; }

    /// <summary>At least one, at most 200, one per date.</summary>
    [Required]
    public List<DiscountOrSurchargeValueInputDto> Values { get; set; } = new();

    /// <summary>None = everywhere; at most 100, each region once.</summary>
    public List<DiscountOrSurchargeRegionInputDto> Regions { get; set; } = new();

    public bool IsArchived { get; set; }
}

public class DiscountOrSurchargeUpdateDto : DiscountOrSurchargeCreateDto, IReferenceDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface IDiscountOrSurchargeAppService : IApplicationService
{
    Task<GridResponse<DiscountOrSurchargeRowDto>> GetListAsync(ArchivableListRequestDto input);
    Task<DiscountOrSurchargeDto> GetAsync(Guid id);
    Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake);
    Task<DiscountOrSurchargeDto> CreateAsync(DiscountOrSurchargeCreateDto input);
    Task<DiscountOrSurchargeDto> UpdateAsync(Guid id, DiscountOrSurchargeUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input);
}
