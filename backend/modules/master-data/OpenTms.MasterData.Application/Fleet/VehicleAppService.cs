using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Permissions;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Relations;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.Identity;

namespace OpenTms.MasterData.Fleet;

/* Pickers need only the app feature; the list's own screen needs its node (PO 2026-10-14, design 48). */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class VehicleAppService
    : MasterDataCrudAppServiceBase<Vehicle, VehicleRowDto, VehicleDto, VehicleCreateDto, VehicleUpdateDto, VehicleListRequestDto>,
      IVehicleAppService
{
    private readonly IRepository<VehicleType, Guid> _vehicleTypes;
    private readonly IRepository<Relation, Guid> _relations;
    private readonly IRepository<Country, Guid> _countries;
    private readonly IRepository<Driver, Guid> _drivers;
    private readonly IRepository<TransportCharacteristics, Guid> _characteristics;
    private readonly IRepository<VehicleExternalIdentifier, Guid> _externalIds;
    private readonly IRepository<VehicleCombinationMember> _members;
    private readonly IRepository<VehicleCombination, Guid> _combinations;
    private readonly IOrganizationUnitRepository _organizationUnits;

    /* The type and relation queries the list's kind filter and name sorts correlate with; same DbContext. */
    private IQueryable<VehicleType>? _typeQuery;
    private IQueryable<Relation>? _relationQuery;

    public VehicleAppService(
        IRepository<Vehicle, Guid> repository,
        IRepository<VehicleType, Guid> vehicleTypes,
        IRepository<Relation, Guid> relations,
        IRepository<Country, Guid> countries,
        IRepository<Driver, Guid> drivers,
        IRepository<TransportCharacteristics, Guid> characteristics,
        IRepository<VehicleExternalIdentifier, Guid> externalIds,
        IRepository<VehicleCombinationMember> members,
        IRepository<VehicleCombination, Guid> combinations,
        IOrganizationUnitRepository organizationUnits)
        : base(repository)
    {
        _vehicleTypes = vehicleTypes;
        _relations = relations;
        _countries = countries;
        _drivers = drivers;
        _characteristics = characteristics;
        _externalIds = externalIds;
        _members = members;
        _combinations = combinations;
        _organizationUnits = organizationUnits;
    }

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Fleet.Default, AppFeatures.MasterData.General.Fleet.Vehicles, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Vehicles.Default)]
    public async Task<GridResponse<VehicleRowDto>> GetListAsync(VehicleListRequestDto input)
    {
        await PrepareQueriesAsync();
        return await GetListInternalAsync(input);
    }

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Fleet.Default, AppFeatures.MasterData.General.Fleet.Vehicles, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Vehicles.Default)]
    public Task<VehicleDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(MasterDataPermissions.Vehicles.Default)]
    public Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    /// <summary>A page of the vehicle picker: non-archived vehicles, searched over name, plate and VIN, ordered by name.</summary>
    [Authorize(MasterDataPermissions.Vehicles.Default)]
    public async Task<VehicleLookupPageDto> GetLookupPageAsync(string? term = null, int skip = 0, int take = MasterDataConsts.LookupPageSize, List<VehicleTypeKind>? kinds = null, Guid? ownerRelationId = null)
    {
        await PrepareQueriesAsync();
        var all = await Repository.GetQueryableAsync();
        var query = ApplyFilters(all, false, kinds, null, ownerRelationId);
        var normalized = NormalizeTerm(term);
        if (normalized != null)
        {
            query = ApplySearch(query, normalized);
        }

        var totalCount = await AsyncExecuter.LongCountAsync(query);
        var page = await AsyncExecuter.ToListAsync(query.OrderBy(v => v.Name)
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, MasterDataConsts.LookupPageSize)));
        var types = await TypesAsync(page.Select(v => v.VehicleTypeId));
        var countries = await CountryCodesAsync(page.Select(v => v.RegistrationCountryId));

        return new VehicleLookupPageDto
        {
            TotalCount = totalCount,
            Items = page.Select(v => new VehicleLookupDto
            {
                Id = v.Id,
                Name = v.Name,
                LicencePlate = FormatPlate(v, countries),
                Kind = types.GetValueOrDefault(v.VehicleTypeId)?.Kind ?? default,
                VehicleTypeName = types.GetValueOrDefault(v.VehicleTypeId)?.Name
            }).ToList()
        };
    }

    /// <summary>The values a new vehicle of the type takes: dimensions, kind and the type's characteristics.</summary>
    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Fleet.Default, AppFeatures.MasterData.General.Fleet.Vehicles, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Vehicles.Default)]
    public async Task<VehicleTypeDefaultsDto> GetTypeDefaultsAsync(Guid vehicleTypeId)
    {
        var type = await _vehicleTypes.GetAsync(vehicleTypeId, includeDetails: true);
        return new VehicleTypeDefaultsDto
        {
            VehicleTypeId = type.Id,
            Kind = type.Kind,
            LengthCm = type.DefaultLengthCm,
            WidthCm = type.DefaultWidthCm,
            HeightCm = type.DefaultHeightCm,
            GrossVehicleWeightKg = type.DefaultGrossVehicleWeightKg,
            MaxEpq = type.MaxEpq,
            MaxLoadingMeters = type.MaxLoadingMeters,
            CostPricePerKm = type.CostPricePerKm,
            Characteristics = await CharacteristicsAsync(type.Characteristics.Select(c => c.TransportCharacteristicsId).ToList(), [])
        };
    }

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Fleet.Default, AppFeatures.MasterData.General.Fleet.Vehicles, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Vehicles.Create)]
    public Task<VehicleDto> CreateAsync(VehicleCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Fleet.Default, AppFeatures.MasterData.General.Fleet.Vehicles, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Vehicles.Update)]
    public Task<VehicleDto> UpdateAsync(Guid id, VehicleUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Fleet.Default, AppFeatures.MasterData.General.Fleet.Vehicles, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Vehicles.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Fleet.Default, AppFeatures.MasterData.General.Fleet.Vehicles, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Vehicles.Delete)]
    public async Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(VehicleBulkDeleteRequestDto input)
    {
        await PrepareQueriesAsync();
        return await BulkDeleteInternalAsync(input.Selection, q => ApplyFilters(q, input.IncludeArchived, input.Kinds, input.VehicleTypeId, input.OwnerRelationId));
    }

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Fleet.Default, AppFeatures.MasterData.General.Fleet.Vehicles, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Vehicles.Update)]
    public async Task ArchiveAsync(Guid id)
    {
        var vehicle = await Repository.GetAsync(id);
        vehicle.Archive();
        await Repository.UpdateAsync(vehicle, autoSave: true);
    }

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Fleet.Default, AppFeatures.MasterData.General.Fleet.Vehicles, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Vehicles.Update)]
    public async Task UnarchiveAsync(Guid id)
    {
        var vehicle = await Repository.GetAsync(id);
        vehicle.Unarchive();
        await Repository.UpdateAsync(vehicle, autoSave: true);
    }

    // ── list hooks ────────────────────────────────────────────────────────────

    private async Task PrepareQueriesAsync()
    {
        _typeQuery = await _vehicleTypes.GetQueryableAsync();
        _relationQuery = await _relations.GetQueryableAsync();
    }

    protected override IQueryable<Vehicle> ApplyFilters(IQueryable<Vehicle> query, VehicleListRequestDto request)
        => ApplyFilters(query, request.IncludeArchived, request.Kinds, request.VehicleTypeId, request.OwnerRelationId);

    private IQueryable<Vehicle> ApplyFilters(IQueryable<Vehicle> query, bool includeArchived, List<VehicleTypeKind>? kinds, Guid? vehicleTypeId, Guid? ownerRelationId)
    {
        if (!includeArchived)
        {
            query = query.Where(v => !v.IsArchived);
        }

        if (kinds is { Count: > 0 })
        {
            var types = _typeQuery!;
            var wanted = kinds.Distinct().ToList();
            query = query.Where(v => types.Any(t => t.Id == v.VehicleTypeId && wanted.Contains(t.Kind)));
        }

        if (vehicleTypeId != null)
        {
            query = query.Where(v => v.VehicleTypeId == vehicleTypeId.Value);
        }

        if (ownerRelationId != null)
        {
            query = query.Where(v => v.OwnerRelationId == ownerRelationId.Value);
        }

        return query;
    }

    /// <summary>The wildcard over name, licence plate (separators ignored) and VIN.</summary>
    protected override IQueryable<Vehicle> ApplySearch(IQueryable<Vehicle> query, string term)
    {
        var plate = VehicleRegistration.SearchKey(term);
        var upper = term.ToUpperInvariant();
        var hasPlate = plate.Length > 0;
        return query.Where(v =>
            v.Name.ToLower().Contains(term) ||
            (hasPlate && v.LicencePlate != null && v.LicencePlate.Contains(plate)) ||
            (v.Vin != null && v.Vin.Contains(upper)));
    }

    protected override IOrderedQueryable<Vehicle> ApplySort(IQueryable<Vehicle> query, string? sortBy, bool descending)
    {
        var key = sortBy?.ToLowerInvariant();
        if (key is "vehicletypename" or "kind" or "ownername")
        {
            var types = _typeQuery ?? throw UnknownSortColumn(sortBy!);
            var relations = _relationQuery ?? throw UnknownSortColumn(sortBy!);
            return key switch
            {
                "vehicletypename" => OrderByJoined(query, types, v => (Guid?)v.VehicleTypeId, t => (Guid?)t.Id, j => j.Other!.Name, descending),
                "kind" => OrderByJoined(query, types, v => (Guid?)v.VehicleTypeId, t => (Guid?)t.Id, j => j.Other!.Kind, descending),
                _ => OrderByJoined(query, relations, v => (Guid?)v.OwnerRelationId, r => (Guid?)r.Id, j => j.Other!.Name, descending)
            };
        }

        return key switch
        {
            null or "" or "name" => Order(query, v => v.Name, descending),
            "licenceplate" => Order(query, v => v.LicencePlate, descending),
            "isarchived" => Order(query, v => v.IsArchived, descending),
            _ => throw UnknownSortColumn(sortBy!)
        };
    }

    protected override IQueryable<Vehicle> ApplyLookupFilter(IQueryable<Vehicle> query) => query.Where(v => !v.IsArchived);

    protected override async Task<List<VehicleRowDto>> ToRowsAsync(List<Vehicle> entities)
    {
        var types = await TypesAsync(entities.Select(v => v.VehicleTypeId));
        var relations = await RelationNamesAsync(entities.Select(v => (Guid?)v.OwnerRelationId).Concat(entities.Select(v => v.HomeDepotRelationId)));
        var countries = await CountryCodesAsync(entities.Select(v => v.RegistrationCountryId));
        var combinations = await ActiveCombinationNamesAsync(entities.Select(v => v.Id).ToList());

        return entities.Select(v => new VehicleRowDto
        {
            Id = v.Id,
            Name = v.Name,
            LicencePlate = FormatPlate(v, countries),
            RegistrationCountryCode = v.RegistrationCountryId == null ? null : countries.GetValueOrDefault(v.RegistrationCountryId.Value),
            Vin = v.Vin,
            VehicleTypeId = v.VehicleTypeId,
            VehicleTypeName = types.GetValueOrDefault(v.VehicleTypeId)?.Name,
            Kind = types.GetValueOrDefault(v.VehicleTypeId)?.Kind ?? default,
            OwnerRelationId = v.OwnerRelationId,
            OwnerName = relations.GetValueOrDefault(v.OwnerRelationId),
            HomeDepotName = v.HomeDepotRelationId == null ? null : relations.GetValueOrDefault(v.HomeDepotRelationId.Value),
            FuelType = v.FuelType,
            EuroClass = v.EuroClass,
            CombinationName = combinations.GetValueOrDefault(v.Id),
            IsArchived = v.IsArchived
        }).ToList();
    }

    protected override async Task<VehicleDto> ToDtoAsync(Vehicle v)
    {
        var type = await _vehicleTypes.GetAsync(v.VehicleTypeId, includeDetails: true);
        var relations = await RelationNamesAsync([v.OwnerRelationId, v.HomeDepotRelationId]);
        var countries = await CountryCodesAsync([v.RegistrationCountryId]);
        var driverIds = v.DefaultDrivers.Select(d => d.DriverId).ToList();
        var drivers = driverIds.Count == 0 ? new Dictionary<Guid, string>() : (await _drivers.GetListAsync(d => driverIds.Contains(d.Id))).ToDictionary(d => d.Id, d => d.FullName);
        var combinations = await ActiveCombinationNamesAsync([v.Id]);

        var dto = new VehicleDto
        {
            Name = v.Name,
            VehicleTypeId = v.VehicleTypeId,
            VehicleTypeName = type.Name,
            Kind = type.Kind,
            LicencePlate = v.LicencePlate,
            RegistrationCountryId = v.RegistrationCountryId,
            RegistrationCountryCode = v.RegistrationCountryId == null ? null : countries.GetValueOrDefault(v.RegistrationCountryId.Value),
            Vin = v.Vin,
            OwnerRelationId = v.OwnerRelationId,
            OwnerName = relations.GetValueOrDefault(v.OwnerRelationId),
            HomeDepotRelationId = v.HomeDepotRelationId,
            HomeDepotName = v.HomeDepotRelationId == null ? null : relations.GetValueOrDefault(v.HomeDepotRelationId.Value),
            LengthCm = v.LengthCm,
            WidthCm = v.WidthCm,
            HeightCm = v.HeightCm,
            GrossVehicleWeightKg = v.GrossVehicleWeightKg,
            EmptyWeightKg = v.EmptyWeightKg,
            MaxEpq = v.MaxEpq,
            MaxLoadingMeters = v.MaxLoadingMeters,
            MaxPayloadKg = v.MaxPayloadKg,
            CostPricePerKm = v.CostPricePerKm,
            FuelType = v.FuelType,
            EuroClass = v.EuroClass,
            IsArchived = v.IsArchived,
            Characteristics = await CharacteristicsAsync(
                type.Characteristics.Select(c => c.TransportCharacteristicsId).ToList(),
                v.TransportCharacteristics.Select(c => c.TransportCharacteristicsId).ToList()),
            DefaultDrivers = v.DefaultDrivers.OrderBy(d => d.SortOrder)
                .Select(d => new VehicleDefaultDriverDto { DriverId = d.DriverId, Name = drivers.GetValueOrDefault(d.DriverId), SortOrder = d.SortOrder }).ToList(),
            OrganizationUnitIds = v.OrganizationUnits.Select(o => o.OrganizationUnitId).ToList(),
            ExternalIdentifiers = v.ExternalIdentifiers.OrderBy(e => e.Source).ThenBy(e => e.Identifier)
                .Select(e => new VehicleExternalIdentifierDto { Id = e.Id, Source = e.Source, Identifier = e.Identifier }).ToList(),
            CombinationName = combinations.GetValueOrDefault(v.Id)
        };
        MapAudit(v, dto);
        return dto;
    }

    protected override MasterDataLookupDto ToLookup(Vehicle v) => new() { Id = v.Id, Name = v.Name, Code = v.LicencePlate };

    protected override string DisplayNameOf(Vehicle v) => v.Name;

    // ── write hooks ───────────────────────────────────────────────────────────

    protected override async Task<Vehicle> CreateEntityAsync(VehicleCreateDto input)
    {
        var name = Vehicle.NormalizeVehicleName(input.Name);
        var normalized = Vehicle.NormalizedNameOf(name);
        await EnsureUniqueAsync(v => v.NormalizedName == normalized, "name", name);
        var type = await FindTypeAsync(input.VehicleTypeId);

        var vehicle = new Vehicle(GuidGenerator.Create(), CurrentTenant.Id, name, type.Id, input.OwnerRelationId);

        /* Explicit override semantics (design 48): an empty dimension takes the chosen type's default, once. */
        vehicle.SetDimensions(new VehicleDimensions(
            input.LengthCm ?? type.DefaultLengthCm,
            input.WidthCm ?? type.DefaultWidthCm,
            input.HeightCm ?? type.DefaultHeightCm,
            input.GrossVehicleWeightKg ?? type.DefaultGrossVehicleWeightKg,
            input.EmptyWeightKg,
            input.MaxEpq ?? type.MaxEpq,
            input.MaxLoadingMeters ?? type.MaxLoadingMeters,
            input.MaxPayloadKg));
        ApplyCostPrice(vehicle, input);

        await ApplyAsync(vehicle, input);
        return vehicle;
    }

    protected override async Task ApplyUpdateAsync(Vehicle vehicle, VehicleUpdateDto input)
    {
        var name = Vehicle.NormalizeVehicleName(input.Name);
        if (name != vehicle.Name)
        {
            var normalized = Vehicle.NormalizedNameOf(name);
            await EnsureUniqueAsync(v => v.NormalizedName == normalized && v.Id != vehicle.Id, "name", name);
            vehicle.SetName(name);
        }

        var type = await FindTypeAsync(input.VehicleTypeId);
        vehicle.SetVehicleType(type.Id);

        /* Never re-inherited: after create the dimensions are the vehicle's own. */
        vehicle.SetDimensions(new VehicleDimensions(
            input.LengthCm, input.WidthCm, input.HeightCm, input.GrossVehicleWeightKg,
            input.EmptyWeightKg, input.MaxEpq, input.MaxLoadingMeters, input.MaxPayloadKg));
        ApplyCostPrice(vehicle, input);

        await ApplyAsync(vehicle, input);
    }

    /// <summary>QP10: the vehicle's own cost price stays its own (null = the type's); the cascade is resolved on read, never copied.</summary>
    private static void ApplyCostPrice(Vehicle vehicle, VehicleCreateDto input) => vehicle.SetCostPricePerKm(input.CostPricePerKm);

    private async Task<VehicleType> FindTypeAsync(Guid vehicleTypeId)
        => await _vehicleTypes.FindAsync(vehicleTypeId)
           ?? throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "vehicleTypeId");

    private async Task ApplyAsync(Vehicle vehicle, VehicleCreateDto input)
    {
        /* Referenced rows of this tenant only (constitution rule 1: the filtered repositories see one tenant). */
        var owner = await _relations.FindAsync(input.OwnerRelationId)
                    ?? throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "ownerRelationId");
        if (!owner.HasRole(RelationRoles.Carrier) && !owner.HasRole(RelationRoles.OwnCompany))
        {
            throw new BusinessException(MasterDataErrorCodes.OwnerRoleRequired);
        }

        if (input.HomeDepotRelationId != null)
        {
            var depot = await _relations.FindAsync(input.HomeDepotRelationId.Value)
                        ?? throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "homeDepotRelationId");
            if (!depot.HasRole(RelationRoles.Depot) && !depot.HasRole(RelationRoles.OwnCompany))
            {
                throw new BusinessException(MasterDataErrorCodes.HomeDepotRoleRequired);
            }
        }

        vehicle.SetOwnership(input.OwnerRelationId, input.HomeDepotRelationId);

        if (input.RegistrationCountryId != null && !await _countries.AnyAsync(c => c.Id == input.RegistrationCountryId.Value))
        {
            throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "registrationCountryId");
        }

        vehicle.SetRegistration(input.LicencePlate, input.RegistrationCountryId, input.Vin);

        /* No plate or VIN in the error data (rule 5): the field name is enough for the message. */
        if (vehicle.LicencePlate != null)
        {
            var plate = vehicle.LicencePlate;
            var countryId = vehicle.RegistrationCountryId;
            await EnsureUniqueAsync(v => v.LicencePlate == plate && v.RegistrationCountryId == countryId && v.Id != vehicle.Id, "licencePlate", string.Empty);
        }

        if (vehicle.Vin != null)
        {
            var vin = vehicle.Vin;
            await EnsureUniqueAsync(v => v.Vin == vin && v.Id != vehicle.Id, "vin", string.Empty);
        }

        vehicle.SetEngine(input.FuelType, input.EuroClass);
        vehicle.SetDescription(input.Description);
        if (input.IsArchived)
        {
            vehicle.Archive();
        }
        else
        {
            vehicle.Unarchive();
        }

        var characteristicIds = input.TransportCharacteristicIds.Distinct().ToList();
        if (characteristicIds.Count > 0 && await _characteristics.CountAsync(c => characteristicIds.Contains(c.Id)) != characteristicIds.Count)
        {
            throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "transportCharacteristicIds");
        }

        var driverIds = input.DefaultDrivers.OrderBy(d => d.SortOrder).Select(d => d.DriverId).ToList();
        var distinctDrivers = driverIds.Distinct().ToList();
        if (distinctDrivers.Count > 0 && await _drivers.CountAsync(d => distinctDrivers.Contains(d.Id)) != distinctDrivers.Count)
        {
            throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "defaultDrivers");
        }

        var unitIds = input.OrganizationUnitIds.Distinct().ToList();
        if (unitIds.Count > 0 && (await _organizationUnits.GetListAsync(unitIds)).Count != unitIds.Count)
        {
            throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "organizationUnitIds");
        }

        vehicle.SetTransportCharacteristics(characteristicIds);
        vehicle.SetDefaultDrivers(driverIds);
        vehicle.SetOrganizationUnits(unitIds);
        vehicle.SetExternalIdentifiers(input.ExternalIdentifiers.Select(e => new VehicleExternalIdentifierInput(e.Id, e.Source, e.Identifier)), () => GuidGenerator.Create());

        /* An external id names one vehicle per tenant: one query for the other vehicles' rows, pairs matched in memory. */
        if (vehicle.ExternalIdentifiers.Count > 0)
        {
            var identifiers = vehicle.ExternalIdentifiers.Select(e => e.Identifier).Distinct().ToList();
            var taken = (await _externalIds.GetListAsync(e => e.VehicleId != vehicle.Id && identifiers.Contains(e.Identifier)))
                .Select(e => (e.Source, e.Identifier)).ToHashSet();
            var clash = vehicle.ExternalIdentifiers.FirstOrDefault(e => taken.Contains((e.Source, e.Identifier)));
            if (clash != null)
            {
                throw new BusinessException(MasterDataErrorCodes.DuplicateKey).WithData("field", "externalIdentifiers").WithData("value", clash.Source + " " + clash.Identifier);
            }
        }
    }

    /// <summary>A vehicle in a combination (active or archived) is in use (design 48).</summary>
    protected override async Task<string?> GetDeleteBlockerAsync(Vehicle vehicle)
    {
        if (await _members.AnyAsync(m => m.VehicleId == vehicle.Id))
        {
            return L["MasterData:InUseByVehicleCombinations"];
        }

        return null;
    }

    /// <summary>Children and join rows stay behind on a soft delete; clear them so nothing points at a deleted vehicle.</summary>
    protected override Task BeforeDeleteAsync(Vehicle vehicle)
    {
        vehicle.SetTransportCharacteristics([]);
        vehicle.SetDefaultDrivers([]);
        vehicle.SetOrganizationUnits([]);
        vehicle.SetExternalIdentifiers([], () => GuidGenerator.Create());
        return Task.CompletedTask;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<Dictionary<Guid, VehicleType>> TypesAsync(IEnumerable<Guid> ids)
    {
        var distinct = ids.Distinct().ToList();
        return distinct.Count == 0 ? [] : (await _vehicleTypes.GetListAsync(t => distinct.Contains(t.Id))).ToDictionary(t => t.Id);
    }

    private async Task<Dictionary<Guid, string>> RelationNamesAsync(IEnumerable<Guid?> ids)
    {
        var distinct = ids.Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        return distinct.Count == 0 ? [] : (await _relations.GetListAsync(r => distinct.Contains(r.Id))).ToDictionary(r => r.Id, r => r.Name);
    }

    private async Task<Dictionary<Guid, string>> CountryCodesAsync(IEnumerable<Guid?> ids)
    {
        var distinct = ids.Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        return distinct.Count == 0 ? [] : (await _countries.GetListAsync(c => distinct.Contains(c.Id))).ToDictionary(c => c.Id, c => c.IsoAlpha2);
    }

    private static string? FormatPlate(Vehicle v, Dictionary<Guid, string> countryCodes)
        => VehicleRegistration.Format(v.LicencePlate, v.RegistrationCountryId == null ? null : countryCodes.GetValueOrDefault(v.RegistrationCountryId.Value));

    /// <summary>The name of the active combination of each vehicle (at most one, by the partial unique index).</summary>
    private async Task<Dictionary<Guid, string>> ActiveCombinationNamesAsync(List<Guid> vehicleIds)
    {
        if (vehicleIds.Count == 0)
        {
            return [];
        }

        var members = await _members.GetListAsync(m => vehicleIds.Contains(m.VehicleId) && m.IsActive);
        var combinationIds = members.Select(m => m.VehicleCombinationId).Distinct().ToList();
        var names = combinationIds.Count == 0 ? new Dictionary<Guid, string>() : (await _combinations.GetListAsync(c => combinationIds.Contains(c.Id))).ToDictionary(c => c.Id, c => c.Name);
        return members
            .Where(m => names.ContainsKey(m.VehicleCombinationId))
            .GroupBy(m => m.VehicleId)
            .ToDictionary(g => g.Key, g => names[g.First().VehicleCombinationId]);
    }

    /// <summary>The type's characteristics (origin Type) and the vehicle's own that the type lacks (origin Vehicle), by name.</summary>
    private async Task<List<VehicleCharacteristicDto>> CharacteristicsAsync(List<Guid> typeIds, List<Guid> ownIds)
    {
        var all = typeIds.Concat(ownIds).Distinct().ToList();
        if (all.Count == 0)
        {
            return [];
        }

        var fromType = typeIds.ToHashSet();
        var rows = await _characteristics.GetListAsync(c => all.Contains(c.Id));
        return rows
            .OrderBy(c => fromType.Contains(c.Id) ? 0 : 1).ThenBy(c => c.Name).ThenBy(c => c.Code)
            .Select(c => new VehicleCharacteristicDto
            {
                Id = c.Id,
                Code = c.Code,
                Name = c.Name,
                Origin = fromType.Contains(c.Id) ? VehicleCharacteristicOrigin.Type : VehicleCharacteristicOrigin.Vehicle
            }).ToList();
    }
}
