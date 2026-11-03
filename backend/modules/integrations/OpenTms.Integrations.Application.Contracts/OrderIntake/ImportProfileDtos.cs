using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Integrations.OrderIntake.Profiles;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// Master data → Integrations → Order intake → Import profiles (design 72 "Import profiles"), route root
/// <c>/api/integrations/import-profiles</c>. Rules are read-only in the UI: they change through the review (repair).
/// </summary>
public interface IImportProfileAppService : IApplicationService
{
    Task<GridResponse<ImportProfileRowDto>> GetListAsync(ImportProfileListRequestDto input);

    Task<ImportProfileDto> GetAsync(Guid id);

    Task<ImportProfileDto> RenameAsync(Guid id, ImportProfileRenameDto input);

    Task<ImportProfileDto> ActivateAsync(Guid id);

    Task<ImportProfileDto> RetireAsync(Guid id);

    Task DeleteAsync(Guid id);
}

/// <summary>The design-50 paging; <see cref="ReferenceDataListRequestDto.WildcardSearch"/> searches the name and the sender.</summary>
public class ImportProfileListRequestDto : ReferenceDataListRequestDto
{
    public List<ImportProfileStatus> Status { get; set; } = new();

    public DocumentFormat? Format { get; set; }
}

public class ImportProfileRowDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? SenderKey { get; set; }

    public DocumentFormat Format { get; set; }

    public ImportProfileStatus Status { get; set; }

    public int Version { get; set; }

    public int ConfirmedCount { get; set; }

    public int CorrectedCount { get; set; }

    public DateTime? LastUsedAt { get; set; }
}

public class ImportProfileDto : ReferenceDataDetailDtoBase
{
    public string Name { get; set; } = string.Empty;

    public string? SenderKey { get; set; }

    public DocumentFormat Format { get; set; }

    public string Target { get; set; } = string.Empty;

    public ImportProfileStatus Status { get; set; }

    public int Version { get; set; }

    public ProfileOrigin Origin { get; set; }

    public string? ModelId { get; set; }

    public ImportProfileRules Rules { get; set; } = new();

    public ImportProfileRules? PreviousRules { get; set; }

    public List<string> Signature { get; set; } = new();

    public int ConfirmedCount { get; set; }

    public int CorrectedCount { get; set; }

    public DateTime? LastUsedAt { get; set; }

    /// <summary>The document it was learned from, and its message (the trace page).</summary>
    public Guid SourceDocumentId { get; set; }

    public Guid? SourceInboundMessageId { get; set; }
}

public class ImportProfileRenameDto : IReferenceDataUpdateDto
{
    [Required, MaxLength(OrderIntakeConsts.MaxProfileNameLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}
