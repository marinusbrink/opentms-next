using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData.Fleet;

public class VehicleTypeRowDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public VehicleTypeKind Kind { get; set; }
    public string? Description { get; set; }
    public double? MaxEpq { get; set; }
    public double? MaxLoadingMeters { get; set; }
    public double? DefaultLengthCm { get; set; }
    public double? DefaultWidthCm { get; set; }
    public double? DefaultHeightCm { get; set; }
    public double? DefaultGrossVehicleWeightKg { get; set; }
}

public class VehicleTypeDto : MasterDataDetailDtoBase
{
    public string Name { get; set; } = string.Empty;
    public VehicleTypeKind Kind { get; set; }
    public double? MaxEpq { get; set; }
    public double? MaxLoadingMeters { get; set; }
    public double? DefaultLengthCm { get; set; }
    public double? DefaultWidthCm { get; set; }
    public double? DefaultHeightCm { get; set; }
    public double? DefaultGrossVehicleWeightKg { get; set; }
    public List<MasterDataLookupDto> TransportCharacteristics { get; set; } = new();
}

public class VehicleTypeCreateDto
{
    [Required, MaxLength(MasterDataConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [Required, EnumDataType(typeof(VehicleTypeKind))]
    public VehicleTypeKind Kind { get; set; }

    [MaxLength(MasterDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    [Range(0, double.MaxValue)]
    public double? MaxEpq { get; set; }

    [Range(0, double.MaxValue)]
    public double? MaxLoadingMeters { get; set; }

    [Range(0, double.MaxValue)]
    public double? DefaultLengthCm { get; set; }

    [Range(0, double.MaxValue)]
    public double? DefaultWidthCm { get; set; }

    [Range(0, double.MaxValue)]
    public double? DefaultHeightCm { get; set; }

    [Range(0, double.MaxValue)]
    public double? DefaultGrossVehicleWeightKg { get; set; }

    public List<Guid> TransportCharacteristicsIds { get; set; } = new();
}

public class VehicleTypeUpdateDto : VehicleTypeCreateDto, IMasterDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface IVehicleTypeAppService : IApplicationService
{
    Task<GridResponse<VehicleTypeRowDto>> GetListAsync(MasterDataListRequestDto input);
    Task<VehicleTypeDto> GetAsync(Guid id);
    Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake);
    Task<VehicleTypeDto> CreateAsync(VehicleTypeCreateDto input);
    Task<VehicleTypeDto> UpdateAsync(Guid id, VehicleTypeUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(MasterDataBulkDeleteRequestDto input);
}
