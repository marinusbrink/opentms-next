using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.Platform.DocumentTypes;

public class DocumentTypeRowDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    /// <summary>Resolved for the UI language.</summary>
    public string Name { get; set; } = string.Empty;
    public bool IsFreightDocument { get; set; }
    public bool IncludeWithInvoice { get; set; }
    public bool IsArchived { get; set; }
}

public class DocumentTypeTransformationDto
{
    /// <summary>1-based.</summary>
    public int Position { get; set; }
    public DocumentTransformationKind Kind { get; set; }
    public string? Argument { get; set; }
}

public class DocumentTypeDto : PlatformMasterDataDetailDtoBase
{
    public string Code { get; set; } = string.Empty;
    /// <summary>Resolved for the UI language.</summary>
    public string Name { get; set; } = string.Empty;
    public PlatformLocalizedNameDto Names { get; set; } = new();
    public bool IsFreightDocument { get; set; }
    public bool IncludeWithInvoice { get; set; }
    public bool ShowInListIndicator { get; set; }
    public bool IsArchived { get; set; }
    /// <summary>In position order.</summary>
    public List<DocumentTypeTransformationDto> Transformations { get; set; } = new();
}

public class DocumentTypeTransformationInputDto
{
    /// <summary>1-based; the rows are ordered by it (then by list order) and renumbered 1, 2, … on save.</summary>
    [Range(1, PlatformMasterDataConsts.MaxDocumentTypeTransformations)]
    public int Position { get; set; }

    [Required, EnumDataType(typeof(DocumentTransformationKind))]
    public DocumentTransformationKind Kind { get; set; }

    /// <summary>Required for RenameByParentProperty and SplitPdfByReferencePattern (a .NET regular expression).</summary>
    [MaxLength(PlatformMasterDataConsts.MaxTransformationArgumentLength)]
    public string? Argument { get; set; }
}

public class DocumentTypeCreateDto
{
    /// <summary>Stored in upper case.</summary>
    [Required, MaxLength(PlatformMasterDataConsts.MaxCodeLength)]
    public string Code { get; set; } = string.Empty;

    /// <summary>Culture → name; the "en" text is required.</summary>
    [Required]
    public PlatformLocalizedNameDto Names { get; set; } = new();

    public bool IsFreightDocument { get; set; }

    public bool IncludeWithInvoice { get; set; }

    public bool ShowInListIndicator { get; set; }

    public bool IsArchived { get; set; }

    [MaxLength(PlatformMasterDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>Each kind at most once.</summary>
    [MaxLength(PlatformMasterDataConsts.MaxDocumentTypeTransformations)]
    public List<DocumentTypeTransformationInputDto> Transformations { get; set; } = new();
}

public class DocumentTypeUpdateDto : DocumentTypeCreateDto, IPlatformMasterDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class DocumentTypeListRequestDto : PlatformMasterDataListRequestDto
{
    public bool IncludeArchived { get; set; }
}

public class DocumentTypeBulkDeleteRequestDto : PlatformMasterDataBulkDeleteRequestDto
{
    /// <summary>The list's filter, applied in FilterBased mode.</summary>
    public bool IncludeArchived { get; set; }
}

public interface IDocumentTypeAppService : IApplicationService
{
    Task<GridResponse<DocumentTypeRowDto>> GetListAsync(DocumentTypeListRequestDto input);
    Task<DocumentTypeDto> GetAsync(Guid id);
    Task<List<PlatformMasterDataLookupDto>> GetLookupAsync(string? term = null, int take = PlatformMasterDataConsts.MaxLookupTake);
    Task<DocumentTypeDto> CreateAsync(DocumentTypeCreateDto input);
    Task<DocumentTypeDto> UpdateAsync(Guid id, DocumentTypeUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<PlatformMasterDataBulkDeleteResultDto> BulkDeleteAsync(DocumentTypeBulkDeleteRequestDto input);
    Task ArchiveAsync(Guid id);
    Task UnarchiveAsync(Guid id);
}
