using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData.Reference;

public class AddressTypeRowDto
{
    public Guid Id { get; set; }
    public AddressTypeKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class AddressTypeDto : MasterDataDetailDtoBase
{
    public AddressTypeKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public LocalizedNameDto Names { get; set; } = new();
}

public class AddressTypeCreateDto
{
    [Required, EnumDataType(typeof(AddressTypeKind))]
    public AddressTypeKind Kind { get; set; }

    [Required]
    public LocalizedNameDto Names { get; set; } = new();

    [MaxLength(MasterDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}

public class AddressTypeUpdateDto : AddressTypeCreateDto, IMasterDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface IAddressTypeAppService : IApplicationService
{
    Task<GridResponse<AddressTypeRowDto>> GetListAsync(MasterDataListRequestDto input);
    Task<AddressTypeDto> GetAsync(Guid id);
    Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake);
    Task<AddressTypeDto> CreateAsync(AddressTypeCreateDto input);
    Task<AddressTypeDto> UpdateAsync(Guid id, AddressTypeUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(MasterDataBulkDeleteRequestDto input);
}
