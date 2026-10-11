using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData.Relations;

public class RelationCategoryRowDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class RelationCategoryDto : MasterDataDetailDtoBase
{
    public string Name { get; set; } = string.Empty;
}

public class RelationCategoryCreateDto
{
    [Required, MaxLength(MasterDataConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(MasterDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}

public class RelationCategoryUpdateDto : RelationCategoryCreateDto, IMasterDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface IRelationCategoryAppService : IApplicationService
{
    Task<GridResponse<RelationCategoryRowDto>> GetListAsync(MasterDataListRequestDto input);
    Task<RelationCategoryDto> GetAsync(Guid id);
    Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake);
    Task<RelationCategoryDto> CreateAsync(RelationCategoryCreateDto input);
    Task<RelationCategoryDto> UpdateAsync(Guid id, RelationCategoryUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(MasterDataBulkDeleteRequestDto input);
}
