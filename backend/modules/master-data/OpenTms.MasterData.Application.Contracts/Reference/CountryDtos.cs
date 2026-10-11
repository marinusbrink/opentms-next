using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData.Reference;

public class CountryRowDto
{
    public Guid Id { get; set; }
    public string IsoAlpha2 { get; set; } = string.Empty;
    public string IsoAlpha3 { get; set; } = string.Empty;
    public int? IsoNumeric { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? DefaultCurrencyId { get; set; }
    public string? DefaultCurrencyCode { get; set; }
    public string? PrimaryTimeZoneId { get; set; }
}

public class CountryDto : MasterDataDetailDtoBase
{
    public string IsoAlpha2 { get; set; } = string.Empty;
    public string IsoAlpha3 { get; set; } = string.Empty;
    public int? IsoNumeric { get; set; }
    public string Name { get; set; } = string.Empty;
    public LocalizedNameDto Names { get; set; } = new();
    public Guid? DefaultCurrencyId { get; set; }
    public string? DefaultCurrencyCode { get; set; }
    public string? PrimaryTimeZoneId { get; set; }
}

public class CountryCreateDto
{
    [Required, RegularExpression("^[A-Za-z]{2}$")]
    public string IsoAlpha2 { get; set; } = string.Empty;

    [Required, RegularExpression("^[A-Za-z]{3}$")]
    public string IsoAlpha3 { get; set; } = string.Empty;

    [Range(0, 999)]
    public int? IsoNumeric { get; set; }

    [Required]
    public LocalizedNameDto Names { get; set; } = new();

    [MaxLength(MasterDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public Guid? DefaultCurrencyId { get; set; }

    [MaxLength(MasterDataConsts.MaxTimeZoneIdLength)]
    public string? PrimaryTimeZoneId { get; set; }
}

public class CountryUpdateDto : CountryCreateDto, IMasterDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface ICountryAppService : IApplicationService
{
    Task<GridResponse<CountryRowDto>> GetListAsync(MasterDataListRequestDto input);
    Task<CountryDto> GetAsync(Guid id);
    Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake);
    Task<CountryDto> CreateAsync(CountryCreateDto input);
    Task<CountryDto> UpdateAsync(Guid id, CountryUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(MasterDataBulkDeleteRequestDto input);
}
