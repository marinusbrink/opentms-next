using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTms.MasterData.Fleet;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData.Published;

/// <summary>
/// The published, in-process lookup other modules use to validate and name Master Data references (designs 51 and 52,
/// module boundary rule 3: the stated synchronous call). Not exposed over HTTP. Any signed-in user may call it: it
/// returns only the id or code and the display name of rows of the caller's own tenant, so an Orders or Financial
/// administrator needs no Master Data permission to save a type that refers to a relation or a country. Inputs are
/// bounded to <see cref="MaxKeys"/>; unknown keys are simply absent from the result. Soft-deleted rows are absent too.
/// Design 59 adds the fleet, driver, address and incompatibility reads Planning's trip calculator needs.
/// </summary>
public interface IMasterDataReferenceLookup : IApplicationService
{
    public const int MaxKeys = 500;

    Task<List<MasterDataReferenceDto>> GetRelationsAsync(IReadOnlyCollection<Guid> ids);

    Task<List<MasterDataReferenceDto>> GetRelationCategoriesAsync(IReadOnlyCollection<Guid> ids);

    Task<List<MasterDataReferenceDto>> GetGeographicalRegionsAsync(IReadOnlyCollection<Guid> ids);

    /// <summary>Countries by ISO 3166-1 alpha-2 code (assessment §3.7: other modules match on ISO codes).</summary>
    Task<List<MasterDataCodeReferenceDto>> GetCountriesAsync(IReadOnlyCollection<string> isoAlpha2Codes);

    /// <summary>Currencies by ISO 4217 code, with their active flag.</summary>
    Task<List<MasterDataCodeReferenceDto>> GetCurrenciesAsync(IReadOnlyCollection<string> isoCodes);

    /* Design 59: the facts Planning snapshots on stops and feeds its calculators. */

    /// <summary>Relation addresses by id: the place a stop or depot is at (relation name, city, coordinates, zone).</summary>
    Task<List<MasterDataAddressReferenceDto>> GetRelationAddressesAsync(IReadOnlyCollection<Guid> addressIds);

    /// <summary>The addresses of relations (all of them, in id order), for a hub or depot named by relation only.</summary>
    Task<List<MasterDataAddressReferenceDto>> GetAddressesOfRelationsAsync(IReadOnlyCollection<Guid> relationIds);

    /// <summary>Vehicles with the specification and capacity figures the trip calculator uses (design 48 fields).</summary>
    Task<List<MasterDataVehicleReferenceDto>> GetVehiclesAsync(IReadOnlyCollection<Guid> ids);

    /// <summary>Vehicle combinations with their active member vehicles in position order.</summary>
    Task<List<MasterDataVehicleCombinationReferenceDto>> GetVehicleCombinationsAsync(IReadOnlyCollection<Guid> ids);

    /// <summary>Drivers by id: display name only (design 43; no contact data leaves Master Data).</summary>
    Task<List<MasterDataReferenceDto>> GetDriversAsync(IReadOnlyCollection<Guid> ids);

    /// <summary>The "do not combine" pairs among the given temperatures, products and transport units (both sides within the input).</summary>
    Task<MasterDataIncompatibilitiesDto> GetIncompatibilitiesAsync(IReadOnlyCollection<Guid> temperatureIds, IReadOnlyCollection<Guid> productIds, IReadOnlyCollection<Guid> transportUnitIds);
}

public class MasterDataReferenceDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsArchived { get; set; }
}

public class MasterDataCodeReferenceDto
{
    public Guid Id { get; set; }

    /// <summary>The ISO code, upper case.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public class MasterDataAddressReferenceDto
{
    public Guid Id { get; set; }

    public Guid RelationId { get; set; }

    public string RelationName { get; set; } = string.Empty;

    public Guid? AddressTypeId { get; set; }

    public string Street { get; set; } = string.Empty;

    public string? HouseNumber { get; set; }

    public string PostalCode { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string? CountryCode { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? TimeZoneId { get; set; }
}

public class MasterDataVehicleReferenceDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? LicencePlate { get; set; }

    public Guid VehicleTypeId { get; set; }

    public string VehicleTypeName { get; set; } = string.Empty;

    public VehicleTypeKind Kind { get; set; }

    public double? GrossVehicleWeightKg { get; set; }

    public EuroClass? EuroClass { get; set; }

    public FuelType? FuelType { get; set; }

    public double? LengthCm { get; set; }

    public double? WidthCm { get; set; }

    public double? HeightCm { get; set; }

    /// <summary>The vehicle's own figure, else its type's.</summary>
    public double? MaxEpq { get; set; }

    public double? MaxLoadingMeters { get; set; }

    public double? MaxPayloadKg { get; set; }

    /// <summary>The union of the type's and the vehicle's own characteristics.</summary>
    public List<Guid> TransportCharacteristicIds { get; set; } = new();

    public bool IsArchived { get; set; }
}

public class MasterDataVehicleCombinationReferenceDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The active members in position order.</summary>
    public List<Guid> VehicleIds { get; set; } = new();

    public bool IsArchived { get; set; }
}

public class MasterDataIncompatibilitiesDto
{
    public List<MasterDataIncompatiblePairDto> Temperatures { get; set; } = new();

    public List<MasterDataIncompatiblePairDto> Products { get; set; } = new();

    public List<MasterDataIncompatiblePairDto> TransportUnits { get; set; } = new();
}

public class MasterDataIncompatiblePairDto
{
    public Guid LeftId { get; set; }

    public Guid RightId { get; set; }
}
