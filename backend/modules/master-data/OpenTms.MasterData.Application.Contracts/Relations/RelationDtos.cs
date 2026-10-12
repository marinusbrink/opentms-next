using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData.Relations;

public class RelationRowDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public RelationRoles Roles { get; set; }
    public bool IsArchived { get; set; }
    public string? VatNumber { get; set; }
    public string? ChamberOfCommerceNumber { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    /// <summary>City and country of the visiting address (by kind), else the first address.</summary>
    public string? City { get; set; }
    public string? CountryCode { get; set; }
    public List<string> CategoryNames { get; set; } = new();
}

public class RelationAddressDto
{
    public Guid? Id { get; set; }

    [Required]
    public Guid AddressTypeId { get; set; }

    public string? AddressTypeName { get; set; }

    [Required]
    public Guid CountryId { get; set; }

    public string? CountryCode { get; set; }

    [Required, MaxLength(MasterDataConsts.MaxStreetLength)]
    public string Street { get; set; } = string.Empty;

    [MaxLength(MasterDataConsts.MaxHouseNumberLength)]
    public string? HouseNumber { get; set; }

    [MaxLength(MasterDataConsts.MaxAddressLine2Length)]
    public string? AddressLine2 { get; set; }

    [Required, MaxLength(MasterDataConsts.MaxPostalCodeLength)]
    public string PostalCode { get; set; } = string.Empty;

    [Required, MaxLength(MasterDataConsts.MaxCityLength)]
    public string City { get; set; } = string.Empty;

    [Range(-90, 90)]
    public double? Latitude { get; set; }

    [Range(-180, 180)]
    public double? Longitude { get; set; }

    [MaxLength(MasterDataConsts.MaxTimeZoneIdLength)]
    public string? TimeZoneId { get; set; }
}

public class RelationContactPersonDto
{
    public Guid? Id { get; set; }

    [Required, MaxLength(MasterDataConsts.MaxPersonNameLength)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(MasterDataConsts.MaxMiddleNameLength)]
    public string? MiddleName { get; set; }

    [Required, MaxLength(MasterDataConsts.MaxPersonNameLength)]
    public string LastName { get; set; } = string.Empty;

    [EnumDataType(typeof(ContactPersonRole))]
    public ContactPersonRole Role { get; set; }

    [MaxLength(MasterDataConsts.MaxJobTitleLength)]
    public string? JobTitle { get; set; }

    [MaxLength(MasterDataConsts.MaxJobTitleLength)]
    public string? Department { get; set; }

    [MaxLength(MasterDataConsts.MaxEmailLength)]
    public string? Email { get; set; }

    [MaxLength(MasterDataConsts.MaxPhoneLength)]
    public string? Phone { get; set; }

    [MaxLength(MasterDataConsts.MaxPhoneLength)]
    public string? Mobile { get; set; }
}

public class RelationBankAccountDto
{
    public Guid? Id { get; set; }

    [Required, MaxLength(MasterDataConsts.MaxIbanLength)]
    public string Iban { get; set; } = string.Empty;

    [MaxLength(MasterDataConsts.MaxBicLength)]
    public string? Bic { get; set; }

    [MaxLength(MasterDataConsts.MaxBankNameLength)]
    public string? BankName { get; set; }

    [MaxLength(MasterDataConsts.MaxAccountHolderLength)]
    public string? AccountHolder { get; set; }

    public bool IsDefault { get; set; }
}

public class RelationDto : MasterDataDetailDtoBase
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public RelationRoles Roles { get; set; }
    public bool IsArchived { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? ChamberOfCommerceNumber { get; set; }
    public string? VatNumber { get; set; }
    public string? PreferredCulture { get; set; }
    public List<RelationAddressDto> Addresses { get; set; } = new();
    public List<RelationContactPersonDto> ContactPersons { get; set; } = new();
    public List<RelationBankAccountDto> BankAccounts { get; set; } = new();
    public List<Guid> CategoryIds { get; set; } = new();
    public List<Guid> GeographicalRegionIds { get; set; } = new();
    public List<Guid> OrganizationUnitIds { get; set; } = new();
}

public class RelationCreateDto
{
    [Required, MaxLength(MasterDataConsts.MaxCodeLength)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(MasterDataConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    public RelationRoles Roles { get; set; }

    public bool IsArchived { get; set; }

    [MaxLength(MasterDataConsts.MaxPhoneLength)] public string? Phone { get; set; }
    [MaxLength(MasterDataConsts.MaxPhoneLength)] public string? Mobile { get; set; }
    [MaxLength(MasterDataConsts.MaxEmailLength)] public string? Email { get; set; }
    [MaxLength(MasterDataConsts.MaxWebsiteLength)] public string? Website { get; set; }
    [MaxLength(MasterDataConsts.MaxLegalNumberLength)] public string? ChamberOfCommerceNumber { get; set; }
    [MaxLength(MasterDataConsts.MaxLegalNumberLength)] public string? VatNumber { get; set; }
    [MaxLength(MasterDataConsts.MaxCultureLength)] public string? PreferredCulture { get; set; }
    [MaxLength(MasterDataConsts.MaxDescriptionLength)] public string? Description { get; set; }

    public List<RelationAddressDto> Addresses { get; set; } = new();
    public List<RelationContactPersonDto> ContactPersons { get; set; } = new();
    public List<RelationBankAccountDto> BankAccounts { get; set; } = new();
    public List<Guid> CategoryIds { get; set; } = new();
    public List<Guid> GeographicalRegionIds { get; set; } = new();
    public List<Guid> OrganizationUnitIds { get; set; } = new();
}

public class RelationUpdateDto : RelationCreateDto, IMasterDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class RelationListRequestDto : MasterDataListRequestDto
{
    /// <summary>Relations carrying any of these roles.</summary>
    public RelationRoles? Roles { get; set; }
    public bool IncludeArchived { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? OrganizationUnitId { get; set; }
}

public class RelationBulkDeleteRequestDto : MasterDataBulkDeleteRequestDto
{
    public RelationRoles? Roles { get; set; }
    public bool IncludeArchived { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? OrganizationUnitId { get; set; }
}

/// <summary>The richer option row of UI-LOOK-003 for EntityLookup.</summary>
public class RelationLookupDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public RelationRoles Roles { get; set; }
    public string? City { get; set; }
    public string? CountryCode { get; set; }
}

public class RelationLookupPageDto
{
    public List<RelationLookupDto> Items { get; set; } = new();
    public long TotalCount { get; set; }
}

/// <summary>The matcher's input (assessment §3.15): exact keys first, then name and postcode, then name.</summary>
public class RelationMatchRequestDto
{
    [MaxLength(MasterDataConsts.MaxCodeLength)] public string? Code { get; set; }
    [MaxLength(MasterDataConsts.MaxLegalNumberLength)] public string? VatNumber { get; set; }
    [MaxLength(MasterDataConsts.MaxLegalNumberLength)] public string? ChamberOfCommerceNumber { get; set; }
    [MaxLength(MasterDataConsts.MaxEmailLength)] public string? Email { get; set; }
    [MaxLength(MasterDataConsts.MaxNameLength)] public string? Name { get; set; }
    [MaxLength(MasterDataConsts.MaxPostalCodeLength)] public string? PostalCode { get; set; }
    public Guid? CountryId { get; set; }
    public RelationRoles? Roles { get; set; }
}

public enum RelationMatchKind
{
    Exact = 0,
    NameAndPostalCode = 1,
    NameOnly = 2
}

public class RelationMatchDto
{
    public Guid RelationId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public RelationMatchKind Kind { get; set; }
    /// <summary>100 for an exact key, 80 for name and postcode, 40–60 for name only.</summary>
    public int Score { get; set; }
}

public class RelationMatchResultDto
{
    public List<RelationMatchDto> Matches { get; set; } = new();
}

public interface IRelationAppService : IApplicationService
{
    Task<GridResponse<RelationRowDto>> GetListAsync(RelationListRequestDto input);
    Task<RelationDto> GetAsync(Guid id);
    Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake);
    /// <summary>Server-searched pages for EntityLookup (UI-LOOK-002): pages of 25, optional role filter.</summary>
    Task<RelationLookupPageDto> GetLookupPageAsync(string? term = null, int skip = 0, int take = MasterDataConsts.LookupPageSize, RelationRoles? roles = null);
    Task<List<RelationAddressDto>> GetAddressesAsync(Guid id);
    Task<RelationDto> CreateAsync(RelationCreateDto input);
    Task<RelationDto> UpdateAsync(Guid id, RelationUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(RelationBulkDeleteRequestDto input);
    Task ArchiveAsync(Guid id);
    Task UnarchiveAsync(Guid id);
    Task<RelationMatchResultDto> MatchAsync(RelationMatchRequestDto input);
}

/// <summary>
/// The published matcher other modules and the intake agent call (assessment §3.15). It never creates
/// anything. A synchronous call into Master Data is a design decision of the calling module (module rule 3).
/// </summary>
public interface IRelationMatcher
{
    Task<RelationMatchResultDto> MatchAsync(RelationMatchRequestDto request);
}
