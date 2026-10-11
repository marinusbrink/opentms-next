using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Features;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Incompatibilities;
using OpenTms.MasterData.Permissions;
using OpenTms.Platform.Grid;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.MasterData.Transport;

[RequiresFeature(MasterDataFeatures.FirstRun)]
public class TransportUnitAppService
    : MasterDataCrudAppServiceBase<TransportUnit, TransportUnitRowDto, TransportUnitDto, TransportUnitCreateDto, TransportUnitUpdateDto, ArchivableListRequestDto>,
      ITransportUnitAppService
{
    private readonly IRepository<TransportUnitIncompatibility> _pairs;
    private readonly IRepository<VehicleType, Guid> _vehicleTypes;

    public TransportUnitAppService(
        IRepository<TransportUnit, Guid> repository,
        IRepository<TransportUnitIncompatibility> pairs,
        IRepository<VehicleType, Guid> vehicleTypes)
        : base(repository)
    {
        _pairs = pairs;
        _vehicleTypes = vehicleTypes;
    }

    [Authorize(MasterDataPermissions.TransportUnits.Default)]
    public Task<GridResponse<TransportUnitRowDto>> GetListAsync(ArchivableListRequestDto input) => GetListInternalAsync(input);

    [Authorize(MasterDataPermissions.TransportUnits.Default)]
    public Task<TransportUnitDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(MasterDataPermissions.TransportUnits.Default)]
    public Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [Authorize(MasterDataPermissions.TransportUnits.Create)]
    public Task<TransportUnitDto> CreateAsync(TransportUnitCreateDto input) => CreateInternalAsync(input);

    [Authorize(MasterDataPermissions.TransportUnits.Update)]
    public Task<TransportUnitDto> UpdateAsync(Guid id, TransportUnitUpdateDto input) => UpdateInternalAsync(id, input);

    [Authorize(MasterDataPermissions.TransportUnits.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [Authorize(MasterDataPermissions.TransportUnits.Delete)]
    public Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => input.IncludeArchived ? q : q.Where(u => !u.IsArchived));

    protected override IQueryable<TransportUnit> ApplyFilters(IQueryable<TransportUnit> query, ArchivableListRequestDto request)
        => request.IncludeArchived ? query : query.Where(u => !u.IsArchived);

    protected override IQueryable<TransportUnit> ApplySearch(IQueryable<TransportUnit> query, string term)
        => query.Where(u =>
            u.Name.ToLower().Contains(term) ||
            (u.Description != null && u.Description.ToLower().Contains(term)));

    protected override IOrderedQueryable<TransportUnit> ApplySort(IQueryable<TransportUnit> query, string? sortBy, bool descending)
        => (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "name" => Order(query, u => u.Name, descending),
            "description" => Order(query, u => u.Description, descending),
            "isarchived" => Order(query, u => u.IsArchived, descending),
            "lengthcm" => Order(query, u => u.LengthCm, descending),
            "widthcm" => Order(query, u => u.WidthCm, descending),
            "heightcm" => Order(query, u => u.HeightCm, descending),
            "tareweightkg" => Order(query, u => u.TareWeightKg, descending),
            "maxloadkg" => Order(query, u => u.MaxLoadKg, descending),
            "isstackable" => Order(query, u => u.IsStackable, descending),
            "maxstackcount" => Order(query, u => u.MaxStackCount, descending),
            "loadingmeters" => Order(query, u => u.LoadingMeters, descending),
            "epqfactor" => Order(query, u => u.EpqFactor, descending),
            "isexchangeable" => Order(query, u => u.IsExchangeable, descending),
            _ => throw UnknownSortColumn(sortBy)
        };

    protected override IQueryable<TransportUnit> ApplyLookupFilter(IQueryable<TransportUnit> query) => query.Where(u => !u.IsArchived);

    protected override Task<List<TransportUnitRowDto>> ToRowsAsync(List<TransportUnit> entities)
        => Task.FromResult(entities.Select(u => new TransportUnitRowDto
        {
            Id = u.Id,
            Name = u.Name,
            Description = u.Description,
            IsArchived = u.IsArchived,
            LengthCm = u.LengthCm,
            WidthCm = u.WidthCm,
            HeightCm = u.HeightCm,
            TareWeightKg = u.TareWeightKg,
            MaxLoadKg = u.MaxLoadKg,
            IsStackable = u.IsStackable,
            MaxStackCount = u.MaxStackCount,
            LoadingMeters = u.LoadingMeters,
            EpqFactor = u.EpqFactor,
            IsExchangeable = u.IsExchangeable
        }).ToList());

    protected override async Task<TransportUnitDto> ToDtoAsync(TransportUnit u)
    {
        var vehicleTypeIds = u.LoadFactors.Select(f => f.VehicleTypeId).Distinct().ToList();
        var vehicleTypes = vehicleTypeIds.Count == 0 ? [] : await _vehicleTypes.GetListAsync(v => vehicleTypeIds.Contains(v.Id));
        var names = vehicleTypes.ToDictionary(v => v.Id, v => v.Name);

        var dto = new TransportUnitDto
        {
            Name = u.Name,
            IsArchived = u.IsArchived,
            LengthCm = u.LengthCm,
            WidthCm = u.WidthCm,
            HeightCm = u.HeightCm,
            TareWeightKg = u.TareWeightKg,
            MaxLoadKg = u.MaxLoadKg,
            IsStackable = u.IsStackable,
            MaxStackCount = u.MaxStackCount,
            LoadingMeters = u.LoadingMeters,
            EpqFactor = u.EpqFactor,
            IsExchangeable = u.IsExchangeable,
            LoadFactors = u.LoadFactors
                .Select(f => new TransportUnitLoadFactorDto
                {
                    Id = f.Id,
                    VehicleTypeId = f.VehicleTypeId,
                    VehicleTypeName = names.GetValueOrDefault(f.VehicleTypeId),
                    LoadingCapacityFactor = f.LoadingCapacityFactor,
                    MaxCapacity = f.MaxCapacity
                })
                .OrderBy(f => f.VehicleTypeName, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            IncompatibleTransportUnits = await GetIncompatiblesAsync(_pairs, u.Id)
        };
        MapAudit(u, dto);
        return dto;
    }

    protected override MasterDataLookupDto ToLookup(TransportUnit u) => new() { Id = u.Id, Name = u.Name };

    protected override string DisplayNameOf(TransportUnit u) => u.Name;

    protected override async Task<TransportUnit> CreateEntityAsync(TransportUnitCreateDto input)
    {
        var name = input.Name.Trim();
        await EnsureUniqueAsync(u => u.Name == name, "name", name);
        var unit = new TransportUnit(GuidGenerator.Create(), CurrentTenant.Id, name);
        await ApplyAsync(unit, input);
        return unit;
    }

    protected override async Task ApplyUpdateAsync(TransportUnit unit, TransportUnitUpdateDto input)
    {
        var name = input.Name.Trim();
        if (name != unit.Name)
        {
            await EnsureUniqueAsync(u => u.Name == name && u.Id != unit.Id, "name", name);
            unit.SetName(name);
        }

        await ApplyAsync(unit, input);
    }

    private async Task ApplyAsync(TransportUnit unit, TransportUnitCreateDto input)
    {
        var vehicleTypeIds = input.LoadFactors.Select(f => f.VehicleTypeId).Distinct().ToList();
        if (vehicleTypeIds.Count > 0 && await _vehicleTypes.CountAsync(v => vehicleTypeIds.Contains(v.Id)) != vehicleTypeIds.Count)
        {
            throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "loadFactors.vehicleTypeId");
        }

        unit.SetDescription(input.Description);
        unit.IsArchived = input.IsArchived;
        unit.LengthCm = input.LengthCm;
        unit.WidthCm = input.WidthCm;
        unit.HeightCm = input.HeightCm;
        unit.TareWeightKg = input.TareWeightKg;
        unit.MaxLoadKg = input.MaxLoadKg;
        unit.IsStackable = input.IsStackable;
        unit.MaxStackCount = input.IsStackable ? input.MaxStackCount : null;
        unit.LoadingMeters = input.LoadingMeters;
        unit.EpqFactor = input.EpqFactor;
        unit.IsExchangeable = input.IsExchangeable;
        unit.SetLoadFactors(
            input.LoadFactors.Select(f => (f.Id, f.VehicleTypeId, f.LoadingCapacityFactor, f.MaxCapacity)),
            () => GuidGenerator.Create());
    }

    protected override Task AfterWriteAsync(TransportUnit unit, TransportUnitCreateDto input)
        => SyncIncompatibilitiesAsync(_pairs, unit.Id, input.IncompatibleTransportUnitIds, (tenantId, a, b) => new TransportUnitIncompatibility(tenantId, a, b));

    /// <summary>Children stay behind on a soft delete, and would block deleting their vehicle type; clear them.</summary>
    protected override async Task BeforeDeleteAsync(TransportUnit unit)
    {
        unit.SetLoadFactors([], () => GuidGenerator.Create());
        await RemoveIncompatibilitiesAsync(_pairs, unit.Id);
    }
}
