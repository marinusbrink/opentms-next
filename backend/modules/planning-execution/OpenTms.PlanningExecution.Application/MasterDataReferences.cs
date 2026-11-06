using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Integrations.Routing;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Published;
using OpenTms.PlanningExecution.Legs;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace OpenTms.PlanningExecution;

/// <summary>
/// Planning's view on the Master Data published lookup (design 56: the stated synchronous call; design 59): names for
/// grids, the depot and hub places the legs snapshot, the vehicle facts the calculators need. Every call is one
/// batched lookup; nothing is cached across requests.
/// </summary>
public class MasterDataReferences : ITransientDependency
{
    private readonly IMasterDataReferenceLookup _lookup;

    public MasterDataReferences(IMasterDataReferenceLookup lookup)
    {
        _lookup = lookup;
    }

    public async Task<Dictionary<Guid, string>> RelationNamesAsync(IEnumerable<Guid?> ids)
    {
        var keys = ids.Where(id => id is { } g && g != Guid.Empty).Select(id => id!.Value).Distinct().ToList();
        if (keys.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var rows = new List<MasterDataReferenceDto>();
        foreach (var chunk in keys.Chunk(IMasterDataReferenceLookup.MaxKeys))
        {
            rows.AddRange(await _lookup.GetRelationsAsync(chunk));
        }

        return rows.ToDictionary(r => r.Id, r => r.Name);
    }

    /// <summary>Relations by id with their archived flag (design 61 "Trip type defaults": a vanished default is skipped).</summary>
    public async Task<Dictionary<Guid, MasterDataReferenceDto>> RelationsAsync(IEnumerable<Guid?> ids)
    {
        var keys = ids.Where(id => id is { } g && g != Guid.Empty).Select(id => id!.Value).Distinct().ToList();
        var rows = new List<MasterDataReferenceDto>();
        foreach (var chunk in keys.Chunk(IMasterDataReferenceLookup.MaxKeys))
        {
            rows.AddRange(await _lookup.GetRelationsAsync(chunk));
        }

        return rows.ToDictionary(r => r.Id);
    }

    /// <summary>Contact persons by id with the relation they belong to.</summary>
    public async Task<Dictionary<Guid, MasterDataContactPersonReferenceDto>> ContactPersonsAsync(IEnumerable<Guid?> ids)
    {
        var keys = ids.Where(id => id is { } g && g != Guid.Empty).Select(id => id!.Value).Distinct().ToList();
        var rows = new List<MasterDataContactPersonReferenceDto>();
        foreach (var chunk in keys.Chunk(IMasterDataReferenceLookup.MaxKeys))
        {
            rows.AddRange(await _lookup.GetContactPersonsAsync(chunk));
        }

        return rows.ToDictionary(r => r.Id);
    }

    /// <summary>Drivers by id with their archived flag.</summary>
    public async Task<Dictionary<Guid, MasterDataReferenceDto>> DriversAsync(IEnumerable<Guid> ids)
    {
        var keys = ids.Where(id => id != Guid.Empty).Distinct().ToList();
        var rows = new List<MasterDataReferenceDto>();
        foreach (var chunk in keys.Chunk(IMasterDataReferenceLookup.MaxKeys))
        {
            rows.AddRange(await _lookup.GetDriversAsync(chunk));
        }

        return rows.ToDictionary(d => d.Id);
    }

    public async Task EnsureRelationsExistAsync(IEnumerable<Guid?> ids, string field)
    {
        var keys = ids.Where(id => id is { } g && g != Guid.Empty).Select(id => id!.Value).Distinct().ToList();
        if (keys.Count == 0)
        {
            return;
        }

        var found = (await _lookup.GetRelationsAsync(keys)).Select(r => r.Id).ToHashSet();
        var missing = keys.FirstOrDefault(id => !found.Contains(id));
        if (missing != Guid.Empty)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", field).WithData("value", missing);
        }
    }

    public async Task<Dictionary<Guid, MasterDataAddressReferenceDto>> AddressesAsync(IEnumerable<Guid?> ids)
    {
        var keys = ids.Where(id => id is { } g && g != Guid.Empty).Select(id => id!.Value).Distinct().ToList();
        if (keys.Count == 0)
        {
            return new Dictionary<Guid, MasterDataAddressReferenceDto>();
        }

        var rows = new List<MasterDataAddressReferenceDto>();
        foreach (var chunk in keys.Chunk(IMasterDataReferenceLookup.MaxKeys))
        {
            rows.AddRange(await _lookup.GetRelationAddressesAsync(chunk));
        }

        return rows.ToDictionary(a => a.Id);
    }

    /// <summary>The address of a relation: the given one when it belongs to the relation, else the relation's first address.</summary>
    public async Task<MasterDataAddressReferenceDto?> AddressOfRelationAsync(Guid relationId, Guid? addressId)
    {
        var addresses = await _lookup.GetAddressesOfRelationsAsync([relationId]);
        return addressId != null
            ? addresses.FirstOrDefault(a => a.Id == addressId)
            : addresses.OrderBy(a => a.AddressTypeId == null ? 0 : 1).ThenBy(a => a.Id).FirstOrDefault();
    }

    /// <summary>Every address of the relations, one batched lookup per 500 relations (design 74 §4: the new places of a leg chain).</summary>
    public async Task<ILookup<Guid, MasterDataAddressReferenceDto>> AddressesOfRelationsAsync(IEnumerable<Guid> relationIds)
    {
        var rows = new List<MasterDataAddressReferenceDto>();
        foreach (var chunk in relationIds.Where(id => id != Guid.Empty).Distinct().Chunk(IMasterDataReferenceLookup.MaxKeys))
        {
            rows.AddRange(await _lookup.GetAddressesOfRelationsAsync(chunk));
        }

        return rows.ToLookup(a => a.RelationId);
    }

    /// <summary>The given address when it belongs to the addresses, else without an address the first (as <see cref="AddressOfRelationAsync"/>).</summary>
    public static MasterDataAddressReferenceDto? PickAddress(IEnumerable<MasterDataAddressReferenceDto> addresses, Guid? addressId)
        => addressId != null
            ? addresses.FirstOrDefault(a => a.Id == addressId)
            : addresses.OrderBy(a => a.AddressTypeId == null ? 0 : 1).ThenBy(a => a.Id).FirstOrDefault();

    /// <summary>Names of Master Data rows of one kind (design 74 §6: the unit, product and temperature of the leg grids), one batched lookup per 500 ids.</summary>
    public async Task<Dictionary<Guid, string>> ReferenceNamesAsync(MasterDataReferenceKind kind, IEnumerable<Guid> ids)
    {
        var rows = new List<MasterDataReferenceDto>();
        foreach (var chunk in ids.Where(id => id != Guid.Empty).Distinct().Chunk(IMasterDataReferenceLookup.MaxKeys))
        {
            rows.AddRange(await _lookup.GetReferencesAsync(kind, chunk));
        }

        return rows.ToDictionary(r => r.Id, r => r.Name);
    }

    public static DepotLocation ToDepot(MasterDataAddressReferenceDto address)
        => new(address.RelationId, address.Id, address.RelationName, address.City, address.CountryCode, address.Latitude, address.Longitude, address.TimeZoneId);

    public async Task<Dictionary<Guid, MasterDataVehicleReferenceDto>> VehiclesAsync(IEnumerable<Guid> ids)
    {
        var keys = ids.Where(id => id != Guid.Empty).Distinct().ToList();
        if (keys.Count == 0)
        {
            return new Dictionary<Guid, MasterDataVehicleReferenceDto>();
        }

        var rows = new List<MasterDataVehicleReferenceDto>();
        foreach (var chunk in keys.Chunk(IMasterDataReferenceLookup.MaxKeys))
        {
            rows.AddRange(await _lookup.GetVehiclesAsync(chunk));
        }

        return rows.ToDictionary(v => v.Id);
    }

    public async Task<Dictionary<Guid, MasterDataVehicleCombinationReferenceDto>> CombinationsAsync(IEnumerable<Guid?> ids)
    {
        var keys = ids.Where(id => id is { } g && g != Guid.Empty).Select(id => id!.Value).Distinct().ToList();
        if (keys.Count == 0)
        {
            return new Dictionary<Guid, MasterDataVehicleCombinationReferenceDto>();
        }

        var rows = new List<MasterDataVehicleCombinationReferenceDto>();
        foreach (var chunk in keys.Chunk(IMasterDataReferenceLookup.MaxKeys))
        {
            rows.AddRange(await _lookup.GetVehicleCombinationsAsync(chunk));
        }

        return rows.ToDictionary(c => c.Id);
    }

    public async Task<Dictionary<Guid, string>> DriverNamesAsync(IEnumerable<Guid> ids)
    {
        var keys = ids.Where(id => id != Guid.Empty).Distinct().ToList();
        if (keys.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var rows = new List<MasterDataReferenceDto>();
        foreach (var chunk in keys.Chunk(IMasterDataReferenceLookup.MaxKeys))
        {
            rows.AddRange(await _lookup.GetDriversAsync(chunk));
        }

        return rows.ToDictionary(d => d.Id, d => d.Name);
    }

    /// <summary>Vehicle display names ("name (plate)"), one batched lookup per 500 ids (design 68 Scheduler names).</summary>
    public async Task<Dictionary<Guid, string>> VehicleNamesAsync(IEnumerable<Guid> ids)
    {
        var keys = ids.Where(id => id != Guid.Empty).Distinct().ToList();
        var rows = new List<MasterDataReferenceDto>();
        foreach (var chunk in keys.Chunk(IMasterDataReferenceLookup.MaxKeys))
        {
            rows.AddRange(await _lookup.GetReferencesAsync(MasterDataReferenceKind.Vehicle, chunk));
        }

        return rows.ToDictionary(r => r.Id, r => r.Name);
    }

    /// <summary>The relation marked as the own company, or null.</summary>
    public async Task<Guid?> OwnCompanyIdAsync() => (await _lookup.GetOwnCompanyAsync())?.Id;

    public Task<MasterDataIncompatibilitiesDto> IncompatibilitiesAsync(IReadOnlyCollection<Guid> temperatureIds, IReadOnlyCollection<Guid> productIds, IReadOnlyCollection<Guid> transportUnitIds)
        => _lookup.GetIncompatibilitiesAsync(temperatureIds, productIds, transportUnitIds);

    /// <summary>
    /// The vehicle ids of a trip: its stored vehicle rows (design 68 §1: a combination's members are stored too), else, for a
    /// trip saved with a combination before its members were stored, the combination's active members.
    /// </summary>
    public async Task<List<Guid>> TripVehicleIdsAsync(Guid? combinationId, IEnumerable<Guid> vehicleIds)
    {
        var stored = vehicleIds.ToList();
        if (stored.Count > 0)
        {
            return stored;
        }

        if (combinationId != null)
        {
            var combinations = await CombinationsAsync([combinationId]);
            return combinations.TryGetValue(combinationId.Value, out var combination) ? combination.VehicleIds : [];
        }

        return stored;
    }

    /// <summary>
    /// The routing profile of a set of vehicles (design 60 "Vehicle spec source"): the gross weights summed, the engine
    /// of the first vehicle that has one, the largest dimensions; the OpenTMS default when nothing is known.
    /// </summary>
    public static VehicleSpec SpecOf(IReadOnlyCollection<MasterDataVehicleReferenceDto> vehicles)
    {
        if (vehicles.Count == 0)
        {
            return VehicleSpec.Default;
        }

        var weights = vehicles.Where(v => v.GrossVehicleWeightKg is > 0).Select(v => v.GrossVehicleWeightKg!.Value).ToList();
        var grossWeight = weights.Count > 0 ? (int)Math.Round(weights.Sum()) : VehicleSpec.Default.GrossWeightKg;
        var engine = vehicles.FirstOrDefault(v => v.EuroClass != null || v.FuelType != null);
        return new VehicleSpec(
            grossWeight,
            engine?.EuroClass ?? VehicleSpec.Default.EuroClass,
            engine?.FuelType ?? VehicleSpec.Default.FuelType,
            null,
            Max(vehicles.Select(v => v.HeightCm)),
            Max(vehicles.Select(v => v.WidthCm)),
            vehicles.Any(v => v.LengthCm != null) ? (int)Math.Round(vehicles.Sum(v => v.LengthCm ?? 0)) : null);
    }

    private static int? Max(IEnumerable<double?> values)
    {
        var known = values.Where(v => v != null).Select(v => v!.Value).ToList();
        return known.Count == 0 ? null : (int)Math.Round(known.Max());
    }
}
