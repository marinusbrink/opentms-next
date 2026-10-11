using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData.Reference;

public class CurrencyRowDto
{
    public Guid Id { get; set; }
    public string IsoCode { get; set; } = string.Empty;
    public int? IsoNumeric { get; set; }
    public int? MinorUnits { get; set; }
    public string? Symbol { get; set; }
    /// <summary>Resolved for the current UI culture.</summary>
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
}

public class CurrencyDto : MasterDataDetailDtoBase
{
    public string IsoCode { get; set; } = string.Empty;
    public int? IsoNumeric { get; set; }
    public int? MinorUnits { get; set; }
    public string? Symbol { get; set; }
    public string Name { get; set; } = string.Empty;
    public LocalizedNameDto Names { get; set; } = new();
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
}

public class CurrencyCreateDto
{
    [Required, RegularExpression("^[A-Za-z]{3}$")]
    public string IsoCode { get; set; } = string.Empty;

    [Range(0, 999)]
    public int? IsoNumeric { get; set; }

    [Range(MasterDataConsts.MinMinorUnits, MasterDataConsts.MaxMinorUnits)]
    public int? MinorUnits { get; set; }

    [MaxLength(MasterDataConsts.MaxSymbolLength)]
    public string? Symbol { get; set; }

    [Required]
    public LocalizedNameDto Names { get; set; } = new();

    [MaxLength(MasterDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
}

public class CurrencyUpdateDto : CurrencyCreateDto, IMasterDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class CurrencyListRequestDto : MasterDataListRequestDto
{
    /// <summary>Null: every currency; true: active only (the list page default); false: inactive only.</summary>
    public bool? IsActive { get; set; }
}

public class CurrencyBulkDeleteRequestDto : MasterDataBulkDeleteRequestDto
{
    public bool? IsActive { get; set; }
}

public interface ICurrencyAppService : IApplicationService
{
    Task<GridResponse<CurrencyRowDto>> GetListAsync(CurrencyListRequestDto input);
    Task<CurrencyDto> GetAsync(Guid id);
    Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake);
    Task<CurrencyDto> CreateAsync(CurrencyCreateDto input);
    Task<CurrencyDto> UpdateAsync(Guid id, CurrencyUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(CurrencyBulkDeleteRequestDto input);
    Task SetDefaultAsync(Guid id);
}
