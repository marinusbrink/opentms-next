using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

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

/// <summary>
/// What the report definition editor needs to enable its template commands (design 55): whether the deployment holds
/// the provider's configuration, whether the feature is on, and the template as the provider knows it. The template is
/// only looked up when both are true.
/// </summary>
public class ReportRenderingStatusDto
{
    /// <summary>A provider implementation for the definition's provider is registered in this deployment.</summary>
    public bool ProviderAvailable { get; set; }

    /// <summary>The deployment holds the provider's configuration (key and secret).</summary>
    public bool ProviderConfigured { get; set; }

    /// <summary>The feature Platform.DocumentRendering is on for the current tenant.</summary>
    public bool FeatureEnabled { get; set; }

    public bool SupportsTemplateEditor { get; set; }

    public bool SupportsTemplateCopy { get; set; }

    /// <summary>The provider knows the template; null when it was not looked up or the provider failed to answer.</summary>
    public bool? TemplateFound { get; set; }

    /// <summary>The template's name at the provider.</summary>
    public string? TemplateName { get; set; }

    /// <summary>The tenant's workspace owns the template; false for FiLogic's standard templates (copy first).</summary>
    public bool? TemplateOwnedByWorkspace { get; set; }

    /// <summary>The localized reason the template could not be looked up, when the provider failed to answer.</summary>
    public string? Message { get; set; }
}

/// <summary>The one-time URL of the provider's template editor, opened in a new tab.</summary>
public class ReportTemplateEditorDto
{
    public string Url { get; set; } = string.Empty;
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

    /// <summary>GET /api/platform/report-definition/{id}/rendering-status (design 55).</summary>
    Task<ReportRenderingStatusDto> GetRenderingStatusAsync(Guid id);

    /// <summary>POST /api/platform/report-definition/{id}/template-editor: refuses a template the workspace does not own.</summary>
    Task<ReportTemplateEditorDto> OpenTemplateEditorAsync(Guid id);

    /// <summary>POST /api/platform/report-definition/{id}/copy-template: a copy in the tenant's workspace and a new definition pointing at it.</summary>
    Task<ReportDefinitionDto> CopyTemplateAsync(Guid id);

    /// <summary>POST /api/platform/report-definition/{id}/test-render: a test PDF from sample data, not billed.</summary>
    Task<IRemoteStreamContent> TestRenderAsync(Guid id);
}
