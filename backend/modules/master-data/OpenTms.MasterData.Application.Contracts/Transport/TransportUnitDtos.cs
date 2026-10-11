using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData.Transport;

public class TransportUnitRowDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsArchived { get; set; }
    public double? LengthCm { get; set; }
    public double? WidthCm { get; set; }
    public double? HeightCm { get; set; }
    public double? TareWeightKg { get; set; }
    public double? MaxLoadKg { get; set; }
    public bool IsStackable { get; set; }
    public int? MaxStackCount { get; set; }
    public double? LoadingMeters { get; set; }
    public double? EpqFactor { get; set; }
    public bool IsExchangeable { get; set; }
}

public class TransportUnitLoadFactorDto
{
    public Guid? Id { get; set; }

    [Required]
    public Guid VehicleTypeId { get; set; }

    public string? VehicleTypeName { get; set; }

    [Range(0, double.MaxValue)]
    public double? LoadingCapacityFactor { get; set; }

    [Range(0, double.MaxValue)]
    public double? MaxCapacity { get; set; }
}

public class TransportUnitDto : MasterDataDetailDtoBase
{
    public string Name { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public double? LengthCm { get; set; }
    public double? WidthCm { get; set; }
    public double? HeightCm { get; set; }
    public double? TareWeightKg { get; set; }
    public double? MaxLoadKg { get; set; }
    public bool IsStackable { get; set; }
    public int? MaxStackCount { get; set; }
    public double? LoadingMeters { get; set; }
    public double? EpqFactor { get; set; }
    public bool IsExchangeable { get; set; }
    public List<TransportUnitLoadFactorDto> LoadFactors { get; set; } = new();
    public List<MasterDataLookupDto> IncompatibleTransportUnits { get; set; } = new();
}

public class TransportUnitCreateDto
{
    [Required, MaxLength(MasterDataConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(MasterDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public bool IsArchived { get; set; }

    [Range(0, double.MaxValue)] public double? LengthCm { get; set; }
    [Range(0, double.MaxValue)] public double? WidthCm { get; set; }
    [Range(0, double.MaxValue)] public double? HeightCm { get; set; }
    [Range(0, double.MaxValue)] public double? TareWeightKg { get; set; }
    [Range(0, double.MaxValue)] public double? MaxLoadKg { get; set; }
    public bool IsStackable { get; set; }
    [Range(1, 100)] public int? MaxStackCount { get; set; }
    [Range(0, double.MaxValue)] public double? LoadingMeters { get; set; }
    [Range(0, double.MaxValue)] public double? EpqFactor { get; set; }

    public bool IsExchangeable { get; set; }

    public List<TransportUnitLoadFactorDto> LoadFactors { get; set; } = new();

    public List<Guid> IncompatibleTransportUnitIds { get; set; } = new();
}

public class TransportUnitUpdateDto : TransportUnitCreateDto, IMasterDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface ITransportUnitAppService : IApplicationService
{
    Task<GridResponse<TransportUnitRowDto>> GetListAsync(ArchivableListRequestDto input);
    Task<TransportUnitDto> GetAsync(Guid id);
    Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake);
    Task<TransportUnitDto> CreateAsync(TransportUnitCreateDto input);
    Task<TransportUnitDto> UpdateAsync(Guid id, TransportUnitUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input);
}
