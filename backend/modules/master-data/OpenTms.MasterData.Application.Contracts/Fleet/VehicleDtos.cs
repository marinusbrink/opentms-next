using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData.Fleet;

public class VehicleRowDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Formatted for display (Dutch plates by sidecode, other countries as stored).</summary>
    public string? LicencePlate { get; set; }
    public string? RegistrationCountryCode { get; set; }
    public string? Vin { get; set; }
    public Guid VehicleTypeId { get; set; }
    public string? VehicleTypeName { get; set; }
    public VehicleTypeKind Kind { get; set; }
    public Guid OwnerRelationId { get; set; }
    public string? OwnerName { get; set; }
    public string? HomeDepotName { get; set; }
    public FuelType? FuelType { get; set; }
    public EuroClass? EuroClass { get; set; }
    /// <summary>The active combination the vehicle is in, if any.</summary>
    public string? CombinationName { get; set; }
    public bool IsArchived { get; set; }
}

/// <summary>One effective characteristic of a vehicle: from its type (read-only) or its own.</summary>
public class VehicleCharacteristicDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public VehicleCharacteristicOrigin Origin { get; set; }
}

public class VehicleDefaultDriverDto
{
    [Required]
    public Guid DriverId { get; set; }

    /// <summary>Read-only: the driver's full name.</summary>
    public string? Name { get; set; }

    /// <summary>1-based order; the input is sorted by it.</summary>
    public int SortOrder { get; set; }
}

public class VehicleExternalIdentifierDto
{
    public Guid? Id { get; set; }

    [Required, MaxLength(MasterDataConsts.MaxExternalSourceLength)]
    public string Source { get; set; } = string.Empty;

    [Required, MaxLength(MasterDataConsts.MaxExternalIdentifierLength)]
    public string Identifier { get; set; } = string.Empty;
}

public class VehicleDto : MasterDataDetailDtoBase
{
    public string Name { get; set; } = string.Empty;
    public Guid VehicleTypeId { get; set; }
    public string? VehicleTypeName { get; set; }
    public VehicleTypeKind Kind { get; set; }
    /// <summary>As stored: upper case, letters and digits only.</summary>
    public string? LicencePlate { get; set; }
    public Guid? RegistrationCountryId { get; set; }
    public string? RegistrationCountryCode { get; set; }
    public string? Vin { get; set; }
    public Guid OwnerRelationId { get; set; }
    public string? OwnerName { get; set; }
    public Guid? HomeDepotRelationId { get; set; }
    public string? HomeDepotName { get; set; }
    public double? LengthCm { get; set; }
    public double? WidthCm { get; set; }
    public double? HeightCm { get; set; }
    public double? GrossVehicleWeightKg { get; set; }
    public double? EmptyWeightKg { get; set; }
    public double? MaxEpq { get; set; }
    public double? MaxLoadingMeters { get; set; }
    public double? MaxPayloadKg { get; set; }
    public FuelType? FuelType { get; set; }
    public EuroClass? EuroClass { get; set; }
    public bool IsArchived { get; set; }
    /// <summary>The type's characteristics and the vehicle's own, marked by origin.</summary>
    public List<VehicleCharacteristicDto> Characteristics { get; set; } = new();
    public List<VehicleDefaultDriverDto> DefaultDrivers { get; set; } = new();
    public List<Guid> OrganizationUnitIds { get; set; } = new();
    public List<VehicleExternalIdentifierDto> ExternalIdentifiers { get; set; } = new();
    public string? CombinationName { get; set; }
}

public class VehicleCreateDto
{
    [Required, MaxLength(MasterDataConsts.MaxVehicleNameLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid VehicleTypeId { get; set; }

    /// <summary>As typed; stored normalised.</summary>
    [MaxLength(MasterDataConsts.MaxLicencePlateLength * 2)]
    public string? LicencePlate { get; set; }

    public Guid? RegistrationCountryId { get; set; }

    [MaxLength(MasterDataConsts.VinLength * 2)]
    public string? Vin { get; set; }

    [Required]
    public Guid OwnerRelationId { get; set; }

    public Guid? HomeDepotRelationId { get; set; }

    /// <summary>On create, an empty value takes the type's default (design 48); on update it is the vehicle's own.</summary>
    public double? LengthCm { get; set; }
    public double? WidthCm { get; set; }
    public double? HeightCm { get; set; }
    public double? GrossVehicleWeightKg { get; set; }
    public double? EmptyWeightKg { get; set; }
    public double? MaxEpq { get; set; }
    public double? MaxLoadingMeters { get; set; }
    public double? MaxPayloadKg { get; set; }

    [EnumDataType(typeof(FuelType))]
    public FuelType? FuelType { get; set; }

    [EnumDataType(typeof(EuroClass))]
    public EuroClass? EuroClass { get; set; }

    public bool IsArchived { get; set; }

    [MaxLength(MasterDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>The vehicle's own characteristics; the type's come along without being listed.</summary>
    public List<Guid> TransportCharacteristicIds { get; set; } = new();

    public List<VehicleDefaultDriverDto> DefaultDrivers { get; set; } = new();

    public List<Guid> OrganizationUnitIds { get; set; } = new();

    public List<VehicleExternalIdentifierDto> ExternalIdentifiers { get; set; } = new();
}

public class VehicleUpdateDto : VehicleCreateDto, IMasterDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class VehicleListRequestDto : MasterDataListRequestDto
{
    public bool IncludeArchived { get; set; }
    /// <summary>Vehicles whose type has one of these kinds.</summary>
    public List<VehicleTypeKind>? Kinds { get; set; }
    public Guid? VehicleTypeId { get; set; }
    public Guid? OwnerRelationId { get; set; }
}

public class VehicleBulkDeleteRequestDto : MasterDataBulkDeleteRequestDto
{
    public bool IncludeArchived { get; set; }
    public List<VehicleTypeKind>? Kinds { get; set; }
    public Guid? VehicleTypeId { get; set; }
    public Guid? OwnerRelationId { get; set; }
}

/// <summary>The option row of the vehicle EntityLookup (UI-LOOK-003).</summary>
public class VehicleLookupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LicencePlate { get; set; }
    public VehicleTypeKind Kind { get; set; }
    public string? VehicleTypeName { get; set; }
}

public class VehicleLookupPageDto
{
    public List<VehicleLookupDto> Items { get; set; } = new();
    public long TotalCount { get; set; }
}

/// <summary>The values a new vehicle of this type takes (design 48): the type's dimensions, its kind and characteristics.</summary>
public class VehicleTypeDefaultsDto
{
    public Guid VehicleTypeId { get; set; }
    public VehicleTypeKind Kind { get; set; }
    public double? LengthCm { get; set; }
    public double? WidthCm { get; set; }
    public double? HeightCm { get; set; }
    public double? GrossVehicleWeightKg { get; set; }
    public double? MaxEpq { get; set; }
    public double? MaxLoadingMeters { get; set; }
    public List<VehicleCharacteristicDto> Characteristics { get; set; } = new();
}

public interface IVehicleAppService : IApplicationService
{
    Task<GridResponse<VehicleRowDto>> GetListAsync(VehicleListRequestDto input);
    Task<VehicleDto> GetAsync(Guid id);
    Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake);
    /// <summary>Server-searched pages for EntityLookup (UI-LOOK-002): pages of 25, optional kind and owner filters.</summary>
    Task<VehicleLookupPageDto> GetLookupPageAsync(string? term = null, int skip = 0, int take = MasterDataConsts.LookupPageSize, List<VehicleTypeKind>? kinds = null, Guid? ownerRelationId = null);
    Task<VehicleTypeDefaultsDto> GetTypeDefaultsAsync(Guid vehicleTypeId);
    Task<VehicleDto> CreateAsync(VehicleCreateDto input);
    Task<VehicleDto> UpdateAsync(Guid id, VehicleUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(VehicleBulkDeleteRequestDto input);
    Task ArchiveAsync(Guid id);
    Task UnarchiveAsync(Guid id);
}
