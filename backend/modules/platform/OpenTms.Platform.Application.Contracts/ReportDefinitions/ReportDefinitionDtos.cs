using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.Platform.ReportDefinitions;

public class ReportDefinitionRowDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ReportPurpose Purpose { get; set; }
    public string Culture { get; set; } = string.Empty;
    public ReportRenderingProvider Provider { get; set; }
    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }
}

public class ReportDefinitionDto : PlatformMasterDataDetailDtoBase
{
    public string Name { get; set; } = string.Empty;
    public ReportPurpose Purpose { get; set; }
    public string Culture { get; set; } = string.Empty;
    public ReportRenderingProvider Provider { get; set; }
    public string ProviderTemplateId { get; set; } = string.Empty;
    public string FileNamePattern { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }
    public bool AvailableInCustomerPortal { get; set; }
}

public class ReportDefinitionCreateDto : IValidatableObject
{
    [Required, MaxLength(PlatformMasterDataConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [Required, EnumDataType(typeof(ReportPurpose))]
    public ReportPurpose Purpose { get; set; }

    /// <summary>A configured language ("nl", "en").</summary>
    [Required, MaxLength(PlatformMasterDataConsts.MaxCultureLength)]
    public string Culture { get; set; } = string.Empty;

    [Required, EnumDataType(typeof(ReportRenderingProvider))]
    public ReportRenderingProvider Provider { get; set; } = ReportRenderingProvider.PdfGeneratorApi;

    [Required, MaxLength(PlatformMasterDataConsts.MaxProviderTemplateIdLength)]
    public string ProviderTemplateId { get; set; } = string.Empty;

    /// <summary>Scriban, parsed on save; ".pdf" is added when missing.</summary>
    [Required, MaxLength(PlatformMasterDataConsts.MaxFileNamePatternLength)]
    public string FileNamePattern { get; set; } = string.Empty;

    /// <summary>True makes this the default of its purpose and language and clears the previous one.</summary>
    public bool IsDefault { get; set; }

    public bool IsArchived { get; set; }

    public bool AvailableInCustomerPortal { get; set; }

    [MaxLength(PlatformMasterDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var pattern = FileNamePattern?.Trim() ?? string.Empty;
        if (pattern.Length > 0 && !pattern.EndsWith(PlatformMasterDataConsts.PdfExtension, StringComparison.OrdinalIgnoreCase)
            && pattern.Length + PlatformMasterDataConsts.PdfExtension.Length > PlatformMasterDataConsts.MaxFileNamePatternLength)
        {
            yield return new ValidationResult(
                $"FileNamePattern with \"{PlatformMasterDataConsts.PdfExtension}\" added must not exceed {PlatformMasterDataConsts.MaxFileNamePatternLength} characters.",
                [nameof(FileNamePattern)]);
        }
    }
}

public class ReportDefinitionUpdateDto : ReportDefinitionCreateDto, IPlatformMasterDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class ReportDefinitionListRequestDto : PlatformMasterDataListRequestDto
{
    [EnumDataType(typeof(ReportPurpose))]
    public ReportPurpose? Purpose { get; set; }

    public bool IncludeArchived { get; set; }
}

public class ReportDefinitionBulkDeleteRequestDto : PlatformMasterDataBulkDeleteRequestDto
{
    /// <summary>The list's filters, applied in FilterBased mode.</summary>
    [EnumDataType(typeof(ReportPurpose))]
    public ReportPurpose? Purpose { get; set; }

    public bool IncludeArchived { get; set; }
}

public interface IReportDefinitionAppService : IApplicationService
{
    Task<GridResponse<ReportDefinitionRowDto>> GetListAsync(ReportDefinitionListRequestDto input);
    Task<ReportDefinitionDto> GetAsync(Guid id);
    Task<List<PlatformMasterDataLookupDto>> GetLookupAsync(string? term = null, int take = PlatformMasterDataConsts.MaxLookupTake);
    Task<ReportDefinitionDto> CreateAsync(ReportDefinitionCreateDto input);
    Task<ReportDefinitionDto> UpdateAsync(Guid id, ReportDefinitionUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<PlatformMasterDataBulkDeleteResultDto> BulkDeleteAsync(ReportDefinitionBulkDeleteRequestDto input);
    Task ArchiveAsync(Guid id);
    Task UnarchiveAsync(Guid id);
}
