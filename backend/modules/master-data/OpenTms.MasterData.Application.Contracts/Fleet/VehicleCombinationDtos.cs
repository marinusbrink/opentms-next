using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.MasterData.Transport;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData.Fleet;

public class VehicleCombinationRowDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>The members' names in position order, joined by " + ".</summary>
    public string Members { get; set; } = string.Empty;
    public string? MotorisedVehicleName { get; set; }
    public bool IsArchived { get; set; }
}

public class VehicleCombinationMemberDto
{
    public Guid VehicleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public VehicleTypeKind Kind { get; set; }
    /// <summary>Formatted for display.</summary>
    public string? LicencePlate { get; set; }
    public int Position { get; set; }
}

public class VehicleCombinationMemberInputDto
{
    [Required]
    public Guid VehicleId { get; set; }

    /// <summary>1-based; the members are ordered by it and renumbered 1, 2, … on save.</summary>
    [Range(1, MasterDataConsts.MaxVehicleCombinationMembers)]
    public int Position { get; set; }
}

public class VehicleCombinationDto : MasterDataDetailDtoBase
{
    public string Name { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public List<VehicleCombinationMemberDto> Members { get; set; } = new();
}

public class VehicleCombinationCreateDto
{
    /// <summary>Empty: the members' names joined by " + ".</summary>
    [MaxLength(MasterDataConsts.MaxVehicleCombinationNameLength)]
    public string? Name { get; set; }

    public bool IsArchived { get; set; }

    [MaxLength(MasterDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    public List<VehicleCombinationMemberInputDto> Members { get; set; } = new();
}

public class VehicleCombinationUpdateDto : VehicleCombinationCreateDto, IMasterDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public interface IVehicleCombinationAppService : IApplicationService
{
    Task<GridResponse<VehicleCombinationRowDto>> GetListAsync(ArchivableListRequestDto input);
    Task<VehicleCombinationDto> GetAsync(Guid id);
    Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake);
    Task<VehicleCombinationDto> CreateAsync(VehicleCombinationCreateDto input);
    Task<VehicleCombinationDto> UpdateAsync(Guid id, VehicleCombinationUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input);
    Task ArchiveAsync(Guid id);
    Task UnarchiveAsync(Guid id);
}
