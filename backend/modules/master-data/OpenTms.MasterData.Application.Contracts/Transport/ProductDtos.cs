using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData.Transport;

public class ProductRowDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsArchived { get; set; }
    public bool IsAdr { get; set; }
    public string? AdrUnNumber { get; set; }
    public string? AdrClass { get; set; }
    public string? AdrPackingGroup { get; set; }
    public string? AdrHazardLabels { get; set; }
    public string? AdrTunnelRestrictionCode { get; set; }
    public int? AdrTransportCategory { get; set; }
    public bool AdrIsLimitedQuantity { get; set; }
    public double? DefaultQuantity { get; set; }
    public double? DefaultWeightKg { get; set; }
    public double? DefaultColli { get; set; }
    public double? DefaultVolumeM3 { get; set; }
    public double? DefaultLoadingMeters { get; set; }
    public double? DefaultEpq { get; set; }
    public double? DefaultLengthCm { get; set; }
    public double? DefaultWidthCm { get; set; }
    public double? DefaultHeightCm { get; set; }
}

public class ProductDto : MasterDataDetailDtoBase
{
    public string Name { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public bool IsAdr { get; set; }
    public string? AdrUnNumber { get; set; }
    public string? AdrClass { get; set; }
    public string? AdrPackingGroup { get; set; }
    public string? AdrDescription { get; set; }
    public string? AdrTechnicalName { get; set; }
    public string? AdrClassificationCode { get; set; }
    public string? AdrHazardLabels { get; set; }
    public string? AdrHazardIdentificationNumber { get; set; }
    public string? AdrTunnelRestrictionCode { get; set; }
    public int? AdrTransportCategory { get; set; }
    public bool AdrIsEnvironmentallyHazardous { get; set; }
    public bool AdrIsLimitedQuantity { get; set; }
    public bool AdrIsWaste { get; set; }
    public double? AdrFlashpointCelsius { get; set; }
    public string? AdrSpecialProvisions { get; set; }
    public double? DefaultQuantity { get; set; }
    public double? DefaultWeightKg { get; set; }
    public double? DefaultColli { get; set; }
    public double? DefaultVolumeM3 { get; set; }
    public double? DefaultLoadingMeters { get; set; }
    public double? DefaultEpq { get; set; }
    public double? DefaultLengthCm { get; set; }
    public double? DefaultWidthCm { get; set; }
    public double? DefaultHeightCm { get; set; }
    public List<MasterDataLookupDto> IncompatibleProducts { get; set; } = new();
}

public class ProductCreateDto
{
    [Required, MaxLength(MasterDataConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(MasterDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public bool IsArchived { get; set; }

    public bool IsAdr { get; set; }

    [MaxLength(MasterDataConsts.AdrUnNumberLength)]
    public string? AdrUnNumber { get; set; }

    [MaxLength(MasterDataConsts.MaxAdrClassLength)]
    public string? AdrClass { get; set; }

    [MaxLength(MasterDataConsts.MaxAdrPackingGroupLength)]
    public string? AdrPackingGroup { get; set; }

    [MaxLength(MasterDataConsts.MaxAdrDescriptionLength)]
    public string? AdrDescription { get; set; }

    [MaxLength(MasterDataConsts.MaxAdrTechnicalNameLength)]
    public string? AdrTechnicalName { get; set; }

    [MaxLength(MasterDataConsts.MaxAdrClassificationCodeLength)]
    public string? AdrClassificationCode { get; set; }

    [MaxLength(MasterDataConsts.MaxAdrHazardLabelsLength)]
    public string? AdrHazardLabels { get; set; }

    [MaxLength(MasterDataConsts.AdrHazardIdentificationNumberMaxLength)]
    public string? AdrHazardIdentificationNumber { get; set; }

    [MaxLength(MasterDataConsts.MaxAdrTunnelRestrictionCodeLength)]
    public string? AdrTunnelRestrictionCode { get; set; }

    [Range(MasterDataConsts.MinAdrTransportCategory, MasterDataConsts.MaxAdrTransportCategory)]
    public int? AdrTransportCategory { get; set; }

    public bool AdrIsEnvironmentallyHazardous { get; set; }

    public bool AdrIsLimitedQuantity { get; set; }

    public bool AdrIsWaste { get; set; }

    public double? AdrFlashpointCelsius { get; set; }

    [MaxLength(MasterDataConsts.MaxAdrSpecialProvisionsLength)]
    public string? AdrSpecialProvisions { get; set; }

    [Range(0, double.MaxValue)] public double? DefaultQuantity { get; set; }
    [Range(0, double.MaxValue)] public double? DefaultWeightKg { get; set; }
    [Range(0, double.MaxValue)] public double? DefaultColli { get; set; }
    [Range(0, double.MaxValue)] public double? DefaultVolumeM3 { get; set; }
    [Range(0, double.MaxValue)] public double? DefaultLoadingMeters { get; set; }
    [Range(0, double.MaxValue)] public double? DefaultEpq { get; set; }
    [Range(0, double.MaxValue)] public double? DefaultLengthCm { get; set; }
    [Range(0, double.MaxValue)] public double? DefaultWidthCm { get; set; }
    [Range(0, double.MaxValue)] public double? DefaultHeightCm { get; set; }

    public List<Guid> IncompatibleProductIds { get; set; } = new();
}

public class ProductUpdateDto : ProductCreateDto, IMasterDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface IProductAppService : IApplicationService
{
    Task<GridResponse<ProductRowDto>> GetListAsync(ArchivableListRequestDto input);
    Task<ProductDto> GetAsync(Guid id);
    Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake);
    Task<ProductDto> CreateAsync(ProductCreateDto input);
    Task<ProductDto> UpdateAsync(Guid id, ProductUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input);
}
