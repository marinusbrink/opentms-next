using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.Financial.Invoicing;

public class VatPercentageRowDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public VatKind Kind { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    /// <summary>The percentage valid today; null when the first rate lies in the future.</summary>
    public decimal? CurrentPercentage { get; set; }
    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }
    public string? Description { get; set; }
}

/// <summary>A rate as the editor sends it (§3.4: replace semantics; fixed rows must come back unchanged).</summary>
public class VatPercentageRateInputDto
{
    public DateOnly ValidFrom { get; set; }

    public decimal Percentage { get; set; }
}

public class VatPercentageRateDto : VatPercentageRateInputDto
{
    /// <summary>True when the rate has taken effect (dated today or earlier): it can no longer change.</summary>
    public bool IsFixed { get; set; }
}

public class VatPercentageDto : ReferenceDataDetailDtoBase
{
    public string Code { get; set; } = string.Empty;
    /// <summary>The name in the request culture.</summary>
    public string Name { get; set; } = string.Empty;
    public LocalizedNameDto Names { get; set; } = new();
    public VatKind Kind { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string? SalesLedgerAccount { get; set; }
    public string? PurchaseLedgerAccount { get; set; }
    public decimal? CurrentPercentage { get; set; }
    /// <summary>Ordered by date.</summary>
    public List<VatPercentageRateDto> Rates { get; set; } = new();
    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }
}

public class VatPercentageCreateDto
{
    [Required, MaxLength(ReferenceDataConsts.MaxCodeLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    public LocalizedNameDto Names { get; set; } = new();

    [MaxLength(ReferenceDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public VatKind Kind { get; set; } = VatKind.Standard;

    /// <summary>ISO 3166-1 alpha-2; must exist in Master Data.</summary>
    [Required, StringLength(ReferenceDataConsts.IsoAlpha2Length, MinimumLength = ReferenceDataConsts.IsoAlpha2Length)]
    public string CountryCode { get; set; } = string.Empty;

    [MaxLength(ReferenceDataConsts.MaxLedgerAccountLength)]
    public string? SalesLedgerAccount { get; set; }

    [MaxLength(ReferenceDataConsts.MaxLedgerAccountLength)]
    public string? PurchaseLedgerAccount { get; set; }

    /// <summary>At least one, at most 50, one per date.</summary>
    [Required]
    public List<VatPercentageRateInputDto> Rates { get; set; } = new();

    public bool IsArchived { get; set; }
}

public class VatPercentageUpdateDto : VatPercentageCreateDto, IReferenceDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface IVatPercentageAppService : IApplicationService
{
    Task<GridResponse<VatPercentageRowDto>> GetListAsync(ArchivableListRequestDto input);
    Task<VatPercentageDto> GetAsync(Guid id);
    Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake);
    Task<VatPercentageDto> CreateAsync(VatPercentageCreateDto input);
    Task<VatPercentageDto> UpdateAsync(Guid id, VatPercentageUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input);
    Task SetDefaultAsync(Guid id);
}
