using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace OpenTms.MasterData.Published;

/// <summary>
/// The published, in-process lookup other modules use to validate and name Master Data references (designs 51 and 52,
/// module boundary rule 3: the stated synchronous call). Not exposed over HTTP. Any signed-in user may call it: it
/// returns only the id or code and the display name of rows of the caller's own tenant, so an Orders or Financial
/// administrator needs no Master Data permission to save a type that refers to a relation or a country. Inputs are
/// bounded to <see cref="MaxKeys"/>; unknown keys are simply absent from the result. Soft-deleted rows are absent too.
/// Design 58 adds the address read (a stop's place, its coordinates, zone and regions), the generic reference read
/// for the Master Data rows an order line names, and the own company (the CMR's "myself").
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

    /// <summary>
    /// Relation addresses by id (design 58: a stop's explicit address): the relation, the postal fields, coordinates,
    /// time zone and the geographical regions the address lies in (the relation's own regions plus the postal-code
    /// ranges of the region matcher).
    /// </summary>
    Task<List<MasterDataAddressReferenceDto>> GetRelationAddressesAsync(IReadOnlyCollection<Guid> ids);

    /// <summary>
    /// The address a relation is shown with (the visiting address by kind, else the default address, else the first by
    /// city: the rule of the relation list), one per relation that has any; for the parties of a consignment note.
    /// </summary>
    Task<List<MasterDataAddressReferenceDto>> GetRelationDefaultAddressesAsync(IReadOnlyCollection<Guid> relationIds);

    /// <summary>Id and name of the Master Data rows of one kind (transport units, products, vehicles, …).</summary>
    Task<List<MasterDataReferenceDto>> GetReferencesAsync(MasterDataReferenceKind kind, IReadOnlyCollection<Guid> ids);

    /// <summary>The relation marked as the own company, or null when the tenant has none.</summary>
    Task<MasterDataReferenceDto?> GetOwnCompanyAsync();
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

/// <summary>The Master Data rows other modules reference by id and need named or checked (design 58).</summary>
public enum MasterDataReferenceKind
{
    TransportUnit = 1,
    Product = 2,
    ProductRelationCode = 3,
    Temperature = 4,
    VehicleType = 5,
    TransportCharacteristic = 6,
    Vehicle = 7,
    VehicleCombination = 8,
    Driver = 9,
    ContactPerson = 10
}

public class MasterDataAddressReferenceDto
{
    public Guid Id { get; set; }

    public Guid RelationId { get; set; }

    public string RelationName { get; set; } = string.Empty;

    public Guid? AddressTypeId { get; set; }

    public string Street { get; set; } = string.Empty;

    /// <summary>House number with its suffix ("30A").</summary>
    public string? HouseNumber { get; set; }

    public string PostalCode { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string CountryCode { get; set; } = string.Empty;

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    /// <summary>IANA id; the country's primary zone when the address has none.</summary>
    public string? TimeZoneId { get; set; }

    public List<Guid> GeographicalRegionIds { get; set; } = new();
}
