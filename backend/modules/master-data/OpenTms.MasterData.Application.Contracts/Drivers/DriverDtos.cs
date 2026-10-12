using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData.Drivers;

public class DriverRowDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public Guid? EmployerRelationId { get; set; }
    public string? EmployerName { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public bool IsArchived { get; set; }
    public bool IsAnonymised { get; set; }
    public bool HasUser { get; set; }
    /// <summary>Qualifications that expire within 90 days or have expired.</summary>
    public int QualificationsExpiring { get; set; }
}

public class DriverQualificationDto
{
    public Guid? Id { get; set; }

    [EnumDataType(typeof(DriverQualificationKind))]
    public DriverQualificationKind Kind { get; set; }

    [MaxLength(MasterDataConsts.MaxQualificationNumberLength)]
    public string? Number { get; set; }

    public DateOnly? ValidFrom { get; set; }

    public DateOnly? ValidUntil { get; set; }

    [MaxLength(MasterDataConsts.MaxQualificationDescriptionLength)]
    public string? Description { get; set; }
}

public class DriverDto : MasterDataDetailDtoBase
{
    public string Code { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string? Initials { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? PreferredCulture { get; set; }
    public Guid? EmployerRelationId { get; set; }
    public string? EmployerName { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public bool IsArchived { get; set; }
    public bool IsAnonymised { get; set; }
    public List<DriverQualificationDto> Qualifications { get; set; } = new();
}

public class DriverCreateDto
{
    [Required, MaxLength(MasterDataConsts.MaxCodeLength)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(MasterDataConsts.MaxPersonNameLength)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(MasterDataConsts.MaxMiddleNameLength)]
    public string? MiddleName { get; set; }

    [Required, MaxLength(MasterDataConsts.MaxPersonNameLength)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(MasterDataConsts.MaxInitialsLength)]
    public string? Initials { get; set; }

    [MaxLength(MasterDataConsts.MaxPhoneLength)] public string? Phone { get; set; }
    [MaxLength(MasterDataConsts.MaxPhoneLength)] public string? Mobile { get; set; }
    [MaxLength(MasterDataConsts.MaxEmailLength)] public string? Email { get; set; }
    [MaxLength(MasterDataConsts.MaxCultureLength)] public string? PreferredCulture { get; set; }
    [MaxLength(MasterDataConsts.MaxDescriptionLength)] public string? Description { get; set; }

    public Guid? EmployerRelationId { get; set; }

    public Guid? UserId { get; set; }

    public bool IsArchived { get; set; }

    public List<DriverQualificationDto> Qualifications { get; set; } = new();
}

public class DriverUpdateDto : DriverCreateDto, IMasterDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class DriverListRequestDto : MasterDataListRequestDto
{
    public bool IncludeArchived { get; set; }
    public Guid? EmployerRelationId { get; set; }
}

public class DriverBulkDeleteRequestDto : MasterDataBulkDeleteRequestDto
{
    public bool IncludeArchived { get; set; }
    public Guid? EmployerRelationId { get; set; }
}

/// <summary>An ABP user of the tenant for the driver's account picker (EntityLookup).</summary>
public class DriverUserLookupDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Email { get; set; }
}

public class DriverUserLookupPageDto
{
    public List<DriverUserLookupDto> Items { get; set; } = new();
    public long TotalCount { get; set; }
}

public interface IDriverAppService : IApplicationService
{
    Task<GridResponse<DriverRowDto>> GetListAsync(DriverListRequestDto input);
    Task<DriverDto> GetAsync(Guid id);
    Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake);
    Task<DriverUserLookupPageDto> GetUserLookupPageAsync(string? term = null, int skip = 0, int take = MasterDataConsts.LookupPageSize);
    Task<DriverDto> CreateAsync(DriverCreateDto input);
    Task<DriverDto> UpdateAsync(Guid id, DriverUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(DriverBulkDeleteRequestDto input);
    Task ArchiveAsync(Guid id);
    Task UnarchiveAsync(Guid id);
    /// <summary>GDPR art. 17: replaces the person fields, clears contact details, user link and qualifications; keeps id and code.</summary>
    Task AnonymiseAsync(Guid id);
}
