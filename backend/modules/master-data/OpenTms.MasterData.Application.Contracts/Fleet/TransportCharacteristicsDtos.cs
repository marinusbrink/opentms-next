using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData.Fleet;

public class TransportCharacteristicsRowDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>One page of the server-searched characteristics picker (UI-LOOK-002); the items carry code and description for the relation's grid.</summary>
public class TransportCharacteristicsLookupPageDto
{
    public List<TransportCharacteristicsRowDto> Items { get; set; } = new();
    public long TotalCount { get; set; }
}

public class TransportCharacteristicsDto : MasterDataDetailDtoBase
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class TransportCharacteristicsCreateDto
{
    [Required, MaxLength(MasterDataConsts.MaxCodeLength)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(MasterDataConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(MasterDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}

public class TransportCharacteristicsUpdateDto : TransportCharacteristicsCreateDto, IMasterDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface ITransportCharacteristicsAppService : IApplicationService
{
    Task<GridResponse<TransportCharacteristicsRowDto>> GetListAsync(MasterDataListRequestDto input);
    Task<TransportCharacteristicsDto> GetAsync(Guid id);
    Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake);
    Task<TransportCharacteristicsLookupPageDto> GetLookupPageAsync(string? term = null, int skip = 0, int take = MasterDataConsts.LookupPageSize);
    Task<TransportCharacteristicsDto> CreateAsync(TransportCharacteristicsCreateDto input);
    Task<TransportCharacteristicsDto> UpdateAsync(Guid id, TransportCharacteristicsUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(MasterDataBulkDeleteRequestDto input);
}
