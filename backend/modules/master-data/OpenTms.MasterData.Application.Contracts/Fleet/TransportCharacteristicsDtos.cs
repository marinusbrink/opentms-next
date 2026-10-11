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
    Task<TransportCharacteristicsDto> CreateAsync(TransportCharacteristicsCreateDto input);
    Task<TransportCharacteristicsDto> UpdateAsync(Guid id, TransportCharacteristicsUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(MasterDataBulkDeleteRequestDto input);
}
