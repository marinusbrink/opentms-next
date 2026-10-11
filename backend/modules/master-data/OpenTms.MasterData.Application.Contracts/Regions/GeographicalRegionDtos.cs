using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData.Regions;

public class GeographicalRegionRowDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int RangeCount { get; set; }
}

public class GeographicalRegionPostalCodeRangeDto
{
    public Guid? Id { get; set; }

    [Required]
    public Guid CountryId { get; set; }

    public string? CountryCode { get; set; }

    [Range(0, int.MaxValue)]
    public int? FromNumeric { get; set; }

    [Range(0, int.MaxValue)]
    public int? TillNumeric { get; set; }

    [MaxLength(MasterDataConsts.MaxPostalCodePrefixLength)]
    public string? Prefix { get; set; }
}

public class GeographicalRegionDto : MasterDataDetailDtoBase
{
    public string Name { get; set; } = string.Empty;
    public List<GeographicalRegionPostalCodeRangeDto> PostalCodeRanges { get; set; } = new();
}

public class GeographicalRegionCreateDto
{
    [Required, MaxLength(MasterDataConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(MasterDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public List<GeographicalRegionPostalCodeRangeDto> PostalCodeRanges { get; set; } = new();
}

public class GeographicalRegionUpdateDto : GeographicalRegionCreateDto, IMasterDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class GeographicalRegionListRequestDto : MasterDataListRequestDto
{
    /// <summary>Only regions with at least one range in this country.</summary>
    public Guid? CountryId { get; set; }
}

public class GeographicalRegionBulkDeleteRequestDto : MasterDataBulkDeleteRequestDto
{
    public Guid? CountryId { get; set; }
}

public interface IGeographicalRegionAppService : IApplicationService
{
    Task<GridResponse<GeographicalRegionRowDto>> GetListAsync(GeographicalRegionListRequestDto input);
    Task<GeographicalRegionDto> GetAsync(Guid id);
    Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake);
    Task<GeographicalRegionDto> CreateAsync(GeographicalRegionCreateDto input);
    Task<GeographicalRegionDto> UpdateAsync(Guid id, GeographicalRegionUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(GeographicalRegionBulkDeleteRequestDto input);
    /// <summary>The regions with a range matching the postcode in the country (PostalCodeRangeMatcher).</summary>
    Task<List<MasterDataLookupDto>> GetMatchesAsync(Guid countryId, string postalCode);
}
