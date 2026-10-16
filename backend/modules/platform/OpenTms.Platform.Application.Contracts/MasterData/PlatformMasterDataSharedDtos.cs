using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using OpenTms.Platform.Grid;

namespace OpenTms.Platform;

/// <summary>
/// The explicit list contract of every Platform master-data list (design 49; the design-41 list shape of Master Data):
/// paging, one wildcard search over a defined column set, one sort column from the row DTO. Bound from the query string.
/// </summary>
public class PlatformMasterDataListRequestDto : IValidatableObject
{
    [Range(0, int.MaxValue)]
    public int StartRow { get; set; }

    public int EndRow { get; set; } = 100;

    [MaxLength(PlatformMasterDataConsts.MaxSearchLength)]
    public string? WildcardSearch { get; set; }

    [MaxLength(64)]
    public string? SortBy { get; set; }

    /// <summary>"asc" (default) or "desc".</summary>
    [MaxLength(4)]
    public string? SortDir { get; set; }

    public bool Descending => string.Equals(SortDir, "desc", StringComparison.OrdinalIgnoreCase);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndRow <= StartRow)
            yield return new ValidationResult("EndRow must be greater than StartRow.", [nameof(EndRow)]);
        else if (EndRow - StartRow > PlatformMasterDataConsts.MaxPageSize)
            yield return new ValidationResult($"EndRow − StartRow must not exceed {PlatformMasterDataConsts.MaxPageSize}.", [nameof(EndRow)]);

        if (SortDir != null && !string.Equals(SortDir, "asc", StringComparison.OrdinalIgnoreCase) && !string.Equals(SortDir, "desc", StringComparison.OrdinalIgnoreCase))
            yield return new ValidationResult("SortDir must be 'asc' or 'desc'.", [nameof(SortDir)]);
    }
}

/// <summary>The picker shape of every Platform lookup.</summary>
public class PlatformMasterDataLookupDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Code { get; set; }
}

/// <summary>Audit and concurrency fields every detail DTO carries (Record identification tab, update round trip).</summary>
public abstract class PlatformMasterDataDetailDtoBase
{
    public Guid Id { get; set; }

    public string? Description { get; set; }

    public DateTime CreationTime { get; set; }

    public Guid? CreatorId { get; set; }

    public DateTime? LastModificationTime { get; set; }

    public Guid? LastModifierId { get; set; }

    public string ConcurrencyStamp { get; set; } = string.Empty;
}

/// <summary>Bulk delete over a grid selection (explicit ids, or every row of the current filter minus exclusions).</summary>
public class PlatformMasterDataBulkDeleteRequestDto
{
    [Required]
    public GridSelectionDto Selection { get; set; } = new();
}

public class PlatformMasterDataBulkDeleteResultDto
{
    public int DeletedCount { get; set; }

    public List<PlatformMasterDataSkippedRowDto> SkippedRows { get; set; } = new();
}

public class PlatformMasterDataSkippedRowDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Localized, for the result toast.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>A culture → text map as editors send and receive it; the default culture's text is required.</summary>
public class PlatformLocalizedNameDto : Dictionary<string, string?>
{
}

/// <summary>Every update DTO carries the concurrency stamp of the row the editor loaded (assessment §3.14).</summary>
public interface IPlatformMasterDataUpdateDto
{
    string ConcurrencyStamp { get; }
}
