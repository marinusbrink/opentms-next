using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.Platform.NumberSequences;

public class NumberSequenceRowDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public NumberSequenceScheme Scheme { get; set; }
    /// <summary>The first number of today's period (counter 1), e.g. "O-26-0001".</summary>
    public string ExampleNumber { get; set; } = string.Empty;
    /// <summary>True once a number has been drawn: the numbering fields are frozen and the sequence cannot be deleted.</summary>
    public bool IsInUse { get; set; }
}

public class NumberSequenceDto : PlatformMasterDataDetailDtoBase
{
    public string Name { get; set; } = string.Empty;
    public NumberSequenceScheme Scheme { get; set; }
    public string? Prefix { get; set; }
    public string? YearPrefix { get; set; }
    public string? YearSuffix { get; set; }
    public string? MonthPrefix { get; set; }
    public string? MonthSuffix { get; set; }
    public int MinimumDigits { get; set; }
    /// <summary>The first number of today's period (counter 1), e.g. "O-26-0001".</summary>
    public string ExampleNumber { get; set; } = string.Empty;
    /// <summary>True once a number has been drawn: the numbering fields are frozen and the sequence cannot be deleted.</summary>
    public bool IsInUse { get; set; }
}

public class NumberSequenceCreateDto
{
    [Required, MaxLength(PlatformMasterDataConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [Required, EnumDataType(typeof(NumberSequenceScheme))]
    public NumberSequenceScheme Scheme { get; set; }

    [MaxLength(PlatformMasterDataConsts.MaxAffixLength)]
    public string? Prefix { get; set; }

    [MaxLength(PlatformMasterDataConsts.MaxAffixLength)]
    public string? YearPrefix { get; set; }

    [MaxLength(PlatformMasterDataConsts.MaxAffixLength)]
    public string? YearSuffix { get; set; }

    [MaxLength(PlatformMasterDataConsts.MaxAffixLength)]
    public string? MonthPrefix { get; set; }

    [MaxLength(PlatformMasterDataConsts.MaxAffixLength)]
    public string? MonthSuffix { get; set; }

    [Range(PlatformMasterDataConsts.MinMinimumDigits, PlatformMasterDataConsts.MaxMinimumDigits)]
    public int MinimumDigits { get; set; } = 4;

    [MaxLength(PlatformMasterDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}

public class NumberSequenceUpdateDto : NumberSequenceCreateDto, IPlatformMasterDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface INumberSequenceAppService : IApplicationService
{
    Task<GridResponse<NumberSequenceRowDto>> GetListAsync(PlatformMasterDataListRequestDto input);
    Task<NumberSequenceDto> GetAsync(Guid id);
    Task<List<PlatformMasterDataLookupDto>> GetLookupAsync(string? term = null, int take = PlatformMasterDataConsts.MaxLookupTake);
    Task<NumberSequenceDto> CreateAsync(NumberSequenceCreateDto input);
    Task<NumberSequenceDto> UpdateAsync(Guid id, NumberSequenceUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<PlatformMasterDataBulkDeleteResultDto> BulkDeleteAsync(PlatformMasterDataBulkDeleteRequestDto input);
}
