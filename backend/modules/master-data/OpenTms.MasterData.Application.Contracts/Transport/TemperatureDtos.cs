using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData.Transport;

public class TemperatureRowDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public double? MinCelsius { get; set; }
    public double? MaxCelsius { get; set; }
    public bool IsArchived { get; set; }
}

public class TemperatureDto : MasterDataDetailDtoBase
{
    public string Name { get; set; } = string.Empty;
    public double? MinCelsius { get; set; }
    public double? MaxCelsius { get; set; }
    public bool IsArchived { get; set; }
    public List<MasterDataLookupDto> IncompatibleTemperatures { get; set; } = new();
}

public class TemperatureCreateDto
{
    [Required, MaxLength(MasterDataConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(MasterDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public double? MinCelsius { get; set; }

    public double? MaxCelsius { get; set; }

    public bool IsArchived { get; set; }

    public List<Guid> IncompatibleTemperatureIds { get; set; } = new();
}

public class TemperatureUpdateDto : TemperatureCreateDto, IMasterDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

/// <summary>Shared by the three archivable entities: archived rows are hidden unless asked for.</summary>
public class ArchivableListRequestDto : MasterDataListRequestDto
{
    public bool IncludeArchived { get; set; }
}

public class ArchivableBulkDeleteRequestDto : MasterDataBulkDeleteRequestDto
{
    public bool IncludeArchived { get; set; }
}

public interface ITemperatureAppService : IApplicationService
{
    Task<GridResponse<TemperatureRowDto>> GetListAsync(ArchivableListRequestDto input);
    Task<TemperatureDto> GetAsync(Guid id);
    Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake);
    Task<TemperatureDto> CreateAsync(TemperatureCreateDto input);
    Task<TemperatureDto> UpdateAsync(Guid id, TemperatureUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input);
}
