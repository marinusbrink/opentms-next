using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Permissions;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Transport;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.MasterData.Fleet;

/* Pickers need only the app feature; the list's own screen needs its node (PO 2026-10-14, design 48). */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class VehicleCombinationAppService
    : MasterDataCrudAppServiceBase<VehicleCombination, VehicleCombinationRowDto, VehicleCombinationDto, VehicleCombinationCreateDto, VehicleCombinationUpdateDto, ArchivableListRequestDto>,
      IVehicleCombinationAppService
{
    private const string NameSeparator = " + ";

    private readonly IRepository<Vehicle, Guid> _vehicles;
    private readonly IRepository<VehicleType, Guid> _vehicleTypes;
    private readonly IRepository<VehicleCombinationMember> _members;
    private readonly IRepository<Country, Guid> _countries;

    public VehicleCombinationAppService(
        IRepository<VehicleCombination, Guid> repository,
        IRepository<Vehicle, Guid> vehicles,
        IRepository<VehicleType, Guid> vehicleTypes,
        IRepository<VehicleCombinationMember> members,
        IRepository<Country, Guid> countries)
        : base(repository)
    {
        _vehicles = vehicles;
        _vehicleTypes = vehicleTypes;
        _members = members;
        _countries = countries;
    }

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Fleet.Default, AppFeatures.MasterData.General.Fleet.VehicleCombinations, RequiresAll = true)]
    [Authorize(MasterDataPermissions.VehicleCombinations.Default)]
    public Task<GridResponse<VehicleCombinationRowDto>> GetListAsync(ArchivableListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Fleet.Default, AppFeatures.MasterData.General.Fleet.VehicleCombinations, RequiresAll = true)]
    [Authorize(MasterDataPermissions.VehicleCombinations.Default)]
    public Task<VehicleCombinationDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(MasterDataPermissions.VehicleCombinations.Default)]
    public Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Fleet.Default, AppFeatures.MasterData.General.Fleet.VehicleCombinations, RequiresAll = true)]
    [Authorize(MasterDataPermissions.VehicleCombinations.Create)]
    public Task<VehicleCombinationDto> CreateAsync(VehicleCombinationCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Fleet.Default, AppFeatures.MasterData.General.Fleet.VehicleCombinations, RequiresAll = true)]
    [Authorize(MasterDataPermissions.VehicleCombinations.Update)]
    public Task<VehicleCombinationDto> UpdateAsync(Guid id, VehicleCombinationUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Fleet.Default, AppFeatures.MasterData.General.Fleet.VehicleCombinations, RequiresAll = true)]
    [Authorize(MasterDataPermissions.VehicleCombinations.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Fleet.Default, AppFeatures.MasterData.General.Fleet.VehicleCombinations, RequiresAll = true)]
    [Authorize(MasterDataPermissions.VehicleCombinations.Delete)]
    public Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => ApplyFilters(q, input.IncludeArchived));

    /// <summary>Archiving frees the members for another combination; twice is one effect.</summary>
    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Fleet.Default, AppFeatures.MasterData.General.Fleet.VehicleCombinations, RequiresAll = true)]
    [Authorize(MasterDataPermissions.VehicleCombinations.Update)]
    public async Task ArchiveAsync(Guid id)
    {
        var combination = await Repository.GetAsync(id, includeDetails: true);
        combination.Archive();
        await Repository.UpdateAsync(combination, autoSave: true);
    }

    /// <summary>Refused while a member is in another active combination.</summary>
    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Fleet.Default, AppFeatures.MasterData.General.Fleet.VehicleCombinations, RequiresAll = true)]
    [Authorize(MasterDataPermissions.VehicleCombinations.Update)]
    public async Task UnarchiveAsync(Guid id)
    {
        var combination = await Repository.GetAsync(id, includeDetails: true);
        await EnsureNotInOtherActiveCombinationAsync(combination.Id, combination.Members.Select(m => m.VehicleId).ToList());
        combination.Unarchive();
        await Repository.UpdateAsync(combination, autoSave: true);
    }

    // ── list hooks ────────────────────────────────────────────────────────────

    protected override IQueryable<VehicleCombination> ApplyFilters(IQueryable<VehicleCombination> query, ArchivableListRequestDto request)
        => ApplyFilters(query, request.IncludeArchived);

    private static IQueryable<VehicleCombination> ApplyFilters(IQueryable<VehicleCombination> query, bool includeArchived)
        => includeArchived ? query : query.Where(c => !c.IsArchived);

    protected override IQueryable<VehicleCombination> ApplySearch(IQueryable<VehicleCombination> query, string term)
        => query.Where(c => c.Name.ToLower().Contains(term) || (c.Description != null && c.Description.ToLower().Contains(term)));

    protected override IOrderedQueryable<VehicleCombination> ApplySort(IQueryable<VehicleCombination> query, string? sortBy, bool descending)
        => (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "name" => Order(query, c => c.Name, descending),
            "isarchived" => Order(query, c => c.IsArchived, descending),
            _ => throw UnknownSortColumn(sortBy)
        };

    protected override IQueryable<VehicleCombination> ApplyLookupFilter(IQueryable<VehicleCombination> query) => query.Where(c => !c.IsArchived);

    protected override async Task<List<VehicleCombinationRowDto>> ToRowsAsync(List<VehicleCombination> entities)
    {
        var ids = entities.Select(c => c.Id).ToList();
        var members = ids.Count == 0 ? [] : await _members.GetListAsync(m => ids.Contains(m.VehicleCombinationId));
        var vehicleIds = members.Select(m => m.VehicleId).Distinct().ToList();
        var names = vehicleIds.Count == 0 ? new Dictionary<Guid, string>() : (await _vehicles.GetListAsync(v => vehicleIds.Contains(v.Id))).ToDictionary(v => v.Id, v => v.Name);
        var byCombination = members.GroupBy(m => m.VehicleCombinationId).ToDictionary(g => g.Key, g => g.OrderBy(m => m.Position).Select(m => names.GetValueOrDefault(m.VehicleId) ?? string.Empty).ToList());

        return entities.Select(c =>
        {
            var memberNames = byCombination.GetValueOrDefault(c.Id) ?? [];
            return new VehicleCombinationRowDto
            {
                Id = c.Id,
                Name = c.Name,
                Members = string.Join(NameSeparator, memberNames),
                MotorisedVehicleName = memberNames.FirstOrDefault(),
                IsArchived = c.IsArchived
            };
        }).ToList();
    }

    protected override async Task<VehicleCombinationDto> ToDtoAsync(VehicleCombination c)
    {
        var vehicleIds = c.Members.Select(m => m.VehicleId).ToList();
        var vehicles = vehicleIds.Count == 0 ? new Dictionary<Guid, Vehicle>() : (await _vehicles.GetListAsync(v => vehicleIds.Contains(v.Id))).ToDictionary(v => v.Id);
        var kinds = await KindsAsync(vehicles.Values);
        var countryIds = vehicles.Values.Where(v => v.RegistrationCountryId != null).Select(v => v.RegistrationCountryId!.Value).Distinct().ToList();
        var countries = countryIds.Count == 0 ? new Dictionary<Guid, string>() : (await _countries.GetListAsync(x => countryIds.Contains(x.Id))).ToDictionary(x => x.Id, x => x.IsoAlpha2);

        var dto = new VehicleCombinationDto
        {
            Name = c.Name,
            IsArchived = c.IsArchived,
            Members = c.Members.OrderBy(m => m.Position).Select(m =>
            {
                var vehicle = vehicles.GetValueOrDefault(m.VehicleId);
                return new VehicleCombinationMemberDto
                {
                    VehicleId = m.VehicleId,
                    Name = vehicle?.Name ?? string.Empty,
                    Kind = kinds.GetValueOrDefault(m.VehicleId),
                    LicencePlate = vehicle == null ? null : VehicleRegistration.Format(vehicle.LicencePlate, vehicle.RegistrationCountryId == null ? null : countries.GetValueOrDefault(vehicle.RegistrationCountryId.Value)),
                    Position = m.Position
                };
            }).ToList()
        };
        MapAudit(c, dto);
        return dto;
    }

    protected override MasterDataLookupDto ToLookup(VehicleCombination c) => new() { Id = c.Id, Name = c.Name };

    protected override string DisplayNameOf(VehicleCombination c) => c.Name;

    // ── write hooks ───────────────────────────────────────────────────────────

    protected override async Task<VehicleCombination> CreateEntityAsync(VehicleCombinationCreateDto input)
    {
        var (ordered, vehicles) = await ResolveMembersAsync(input);
        var name = NameOf(input, ordered, vehicles);
        await EnsureUniqueAsync(c => c.Name == name, "name", name);

        var combination = new VehicleCombination(GuidGenerator.Create(), CurrentTenant.Id, name);
        await ApplyAsync(combination, input, ordered);
        return combination;
    }

    protected override async Task ApplyUpdateAsync(VehicleCombination combination, VehicleCombinationUpdateDto input)
    {
        var (ordered, vehicles) = await ResolveMembersAsync(input);
        var name = NameOf(input, ordered, vehicles);
        if (name != combination.Name)
        {
            await EnsureUniqueAsync(c => c.Name == name && c.Id != combination.Id, "name", name);
            combination.SetName(name);
        }

        await ApplyAsync(combination, input, ordered);
    }

    private async Task ApplyAsync(VehicleCombination combination, VehicleCombinationCreateDto input, List<(Guid VehicleId, VehicleTypeKind Kind)> ordered)
    {
        combination.SetDescription(input.Description);

        /* Archive first, so the members take the right active state; an active combination claims its vehicles. */
        if (input.IsArchived)
        {
            combination.Archive();
        }
        else
        {
            await EnsureNotInOtherActiveCombinationAsync(combination.Id, ordered.Select(m => m.VehicleId).ToList());
            combination.Unarchive();
        }

        combination.SetMembers(ordered);
    }

    /// <summary>The members in position order with their kinds; every vehicle must be a row of this tenant.</summary>
    private async Task<(List<(Guid VehicleId, VehicleTypeKind Kind)> Ordered, Dictionary<Guid, Vehicle> Vehicles)> ResolveMembersAsync(VehicleCombinationCreateDto input)
    {
        if (input.Members.Count < MasterDataConsts.MinVehicleCombinationMembers || input.Members.Count > MasterDataConsts.MaxVehicleCombinationMembers)
        {
            throw new BusinessException(MasterDataErrorCodes.CombinationMemberCount)
                .WithData("min", MasterDataConsts.MinVehicleCombinationMembers)
                .WithData("max", MasterDataConsts.MaxVehicleCombinationMembers);
        }

        var ids = input.Members.Select(m => m.VehicleId).Distinct().ToList();
        var vehicles = (await _vehicles.GetListAsync(v => ids.Contains(v.Id))).ToDictionary(v => v.Id);
        if (vehicles.Count != ids.Count)
        {
            throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "members");
        }

        var kinds = await KindsAsync(vehicles.Values);
        var ordered = input.Members
            .Select((m, index) => (m, index))
            .OrderBy(x => x.m.Position).ThenBy(x => x.index)
            .Select(x => (x.m.VehicleId, kinds.GetValueOrDefault(x.m.VehicleId)))
            .ToList();
        return (ordered, vehicles);
    }

    private static string NameOf(VehicleCombinationCreateDto input, List<(Guid VehicleId, VehicleTypeKind Kind)> ordered, Dictionary<Guid, Vehicle> vehicles)
    {
        var typed = input.Name?.Trim();
        if (!string.IsNullOrEmpty(typed))
        {
            return VehicleCombination.NormalizeCombinationName(typed);
        }

        var joined = string.Join(NameSeparator, ordered.Select(m => vehicles[m.VehicleId].Name));
        return joined.Length > MasterDataConsts.MaxVehicleCombinationNameLength ? joined[..MasterDataConsts.MaxVehicleCombinationNameLength].TrimEnd() : joined;
    }

    private async Task<Dictionary<Guid, VehicleTypeKind>> KindsAsync(IEnumerable<Vehicle> vehicles)
    {
        var list = vehicles.ToList();
        var typeIds = list.Select(v => v.VehicleTypeId).Distinct().ToList();
        var types = typeIds.Count == 0 ? new Dictionary<Guid, VehicleTypeKind>() : (await _vehicleTypes.GetListAsync(t => typeIds.Contains(t.Id))).ToDictionary(t => t.Id, t => t.Kind);
        return list.ToDictionary(v => v.Id, v => types.GetValueOrDefault(v.VehicleTypeId));
    }

    /// <summary>The friendly check before the partial unique index on the active members (the index remains the truth).</summary>
    private async Task EnsureNotInOtherActiveCombinationAsync(Guid combinationId, List<Guid> vehicleIds)
    {
        if (vehicleIds.Count == 0)
        {
            return;
        }

        var taken = await _members.GetListAsync(m => vehicleIds.Contains(m.VehicleId) && m.IsActive && m.VehicleCombinationId != combinationId);
        if (taken.Count > 0)
        {
            var vehicleId = taken[0].VehicleId;
            var vehicle = await _vehicles.FindAsync(vehicleId);
            throw new BusinessException(MasterDataErrorCodes.VehicleAlreadyInCombination).WithData("name", vehicle?.Name ?? string.Empty);
        }
    }

    /// <summary>The member rows stay behind on a soft delete; clear them so the vehicles are free again.</summary>
    protected override Task BeforeDeleteAsync(VehicleCombination combination)
    {
        combination.ClearMembers();
        return Task.CompletedTask;
    }
}
