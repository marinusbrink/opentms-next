using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using OpenTms.Platform.Grid;

namespace OpenTms.MasterData;

/// <summary>
/// The explicit list contract of every Master Data list (assessment §3.13): paging, one wildcard search over a
/// defined column set, one sort column from the row DTO. Bound from the query string on GET.
/// </summary>
public class MasterDataListRequestDto : IValidatableObject
{
    [Range(0, int.MaxValue)]
    public int StartRow { get; set; }

    public int EndRow { get; set; } = 100;

    [MaxLength(MasterDataConsts.MaxSearchLength)]
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
        else if (EndRow - StartRow > MasterDataConsts.MaxPageSize)
            yield return new ValidationResult($"EndRow − StartRow must not exceed {MasterDataConsts.MaxPageSize}.", [nameof(EndRow)]);

        if (SortDir != null && !string.Equals(SortDir, "asc", StringComparison.OrdinalIgnoreCase) && !string.Equals(SortDir, "desc", StringComparison.OrdinalIgnoreCase))
            yield return new ValidationResult("SortDir must be 'asc' or 'desc'.", [nameof(SortDir)]);
    }
}

/// <summary>The picker shape shared by every lookup, incompatibility list and region match.</summary>
public class MasterDataLookupDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Code { get; set; }
}

/// <summary>Audit and concurrency fields every detail DTO carries (Record identification tab, update round trip).</summary>
public abstract class MasterDataDetailDtoBase
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
public class MasterDataBulkDeleteRequestDto
{
    [Required]
    public GridSelectionDto Selection { get; set; } = new();
}

public class MasterDataBulkDeleteResultDto
{
    public int DeletedCount { get; set; }

    public List<MasterDataSkippedRowDto> SkippedRows { get; set; } = new();
}

public class MasterDataSkippedRowDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Localized, for the result toast.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>A culture → text map as editors send and receive it; the default culture's text is required.</summary>
public class LocalizedNameDto : Dictionary<string, string?>
{
}

/// <summary>Every update DTO carries the concurrency stamp of the row the editor loaded (assessment §3.14).</summary>
public interface IMasterDataUpdateDto
{
    string ConcurrencyStamp { get; }
}
