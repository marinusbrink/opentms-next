using System;
using System.Collections.Generic;
using OpenTms.MasterData.Relations;

namespace OpenTms.MasterData.TestData;

/// <summary>
/// What Master Data generated in a "Reset to dataset" run (docs/designs/65-tenant-test-datasets.md), published
/// for the contributors after it (Financial, Orders, Planning &amp; Execution). Ids only, plus what a later module
/// needs to make realistic choices (roles, country, coordinates).
/// </summary>
public sealed class MasterDataTestDataset
{
    public List<TestRelation> Relations { get; } = new();

    /// <summary>ISO alpha-2 → id of the seeded country.</summary>
    public Dictionary<string, Guid> CountryIds { get; } = new(StringComparer.Ordinal);

    /// <summary>ISO 4217 → id of the seeded currency.</summary>
    public Dictionary<string, Guid> CurrencyIds { get; } = new(StringComparer.Ordinal);

    public List<Guid> VehicleIds { get; } = new();
    public List<Guid> VehicleTypeIds { get; } = new();
    public List<Guid> VehicleCombinationIds { get; } = new();
    public List<Guid> DriverIds { get; } = new();
    public List<Guid> TransportUnitIds { get; } = new();
    public List<Guid> ProductIds { get; } = new();
    public List<Guid> TemperatureIds { get; } = new();
    public List<Guid> TransportCharacteristicsIds { get; } = new();
    public List<Guid> GeographicalRegionIds { get; } = new();
    public List<Guid> RelationCategoryIds { get; } = new();
}

public sealed record TestRelation(Guid Id, string Code, string Name, RelationRoles Roles, string CountryCode, IReadOnlyList<TestRelationAddress> Addresses);

/// <summary>A relation's address; the first of a relation is its main (visiting) address.</summary>
public sealed record TestRelationAddress(
    Guid Id,
    string CountryCode,
    string Street,
    string? HouseNumber,
    string PostalCode,
    string City,
    double Latitude,
    double Longitude);
