using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Permissions;
using OpenTms.MasterData.Transport;
using OpenTms.Platform.Grid;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using OpenTms.Platform.Features;

namespace OpenTms.MasterData.Fleet;

/* Pickers need only the app feature; the list's own screen needs its node (PO 2026-10-14). */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class VehicleTypeAppService
    : MasterDataCrudAppServiceBase<VehicleType, VehicleTypeRowDto, VehicleTypeDto, VehicleTypeCreateDto, VehicleTypeUpdateDto, MasterDataListRequestDto>,
      IVehicleTypeAppService
{
    private readonly IRepository<TransportCharacteristics, Guid> _characteristics;
    private readonly IRepository<TransportUnitLoadFactor, Guid> _loadFactors;

    public VehicleTypeAppService(
        IRepository<VehicleType, Guid> repository,
        IRepository<TransportCharacteristics, Guid> characteristics,
        IRepository<TransportUnitLoadFactor, Guid> loadFactors)
        : base(repository)
    {
        _characteristics = characteristics;
        _loadFactors = loadFactors;
    }

    [RequiresFeature(AppFeatures.MasterData.Fleet.Default, AppFeatures.MasterData.Fleet.VehicleTypes, RequiresAll = true)]
    [Authorize(MasterDataPermissions.VehicleTypes.Default)]
    public Task<GridResponse<VehicleTypeRowDto>> GetListAsync(MasterDataListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Fleet.Default, AppFeatures.MasterData.Fleet.VehicleTypes, RequiresAll = true)]
    [Authorize(MasterDataPermissions.VehicleTypes.Default)]
    public Task<VehicleTypeDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(MasterDataPermissions.VehicleTypes.Default)]
    public Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.Fleet.Default, AppFeatures.MasterData.Fleet.VehicleTypes, RequiresAll = true)]
    [Authorize(MasterDataPermissions.VehicleTypes.Create)]
    public Task<VehicleTypeDto> CreateAsync(VehicleTypeCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Fleet.Default, AppFeatures.MasterData.Fleet.VehicleTypes, RequiresAll = true)]
    [Authorize(MasterDataPermissions.VehicleTypes.Update)]
    public Task<VehicleTypeDto> UpdateAsync(Guid id, VehicleTypeUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.Fleet.Default, AppFeatures.MasterData.Fleet.VehicleTypes, RequiresAll = true)]
    [Authorize(MasterDataPermissions.VehicleTypes.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.Fleet.Default, AppFeatures.MasterData.Fleet.VehicleTypes, RequiresAll = true)]
    [Authorize(MasterDataPermissions.VehicleTypes.Delete)]
    public Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(MasterDataBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => q);

    protected override IQueryable<VehicleType> ApplySearch(IQueryable<VehicleType> query, string term)
        => query.Where(v => v.Name.ToLower().Contains(term) || (v.Description != null && v.Description.ToLower().Contains(term)));

    protected override IOrderedQueryable<VehicleType> ApplySort(IQueryable<VehicleType> query, string? sortBy, bool descending)
        => (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "name" => Order(query, v => v.Name, descending),
            "kind" => Order(query, v => v.Kind, descending),
            "description" => Order(query, v => v.Description, descending),
            "maxepq" => Order(query, v => v.MaxEpq, descending),
            "maxloadingmeters" => Order(query, v => v.MaxLoadingMeters, descending),
            "defaultlengthcm" => Order(query, v => v.DefaultLengthCm, descending),
            "defaultwidthcm" => Order(query, v => v.DefaultWidthCm, descending),
            "defaultheightcm" => Order(query, v => v.DefaultHeightCm, descending),
            "defaultgrossvehicleweightkg" => Order(query, v => v.DefaultGrossVehicleWeightKg, descending),
            _ => throw UnknownSortColumn(sortBy)
        };

    protected override Task<List<VehicleTypeRowDto>> ToRowsAsync(List<VehicleType> entities)
        => Task.FromResult(entities.Select(v => new VehicleTypeRowDto
        {
            Id = v.Id,
            Name = v.Name,
            Kind = v.Kind,
            Description = v.Description,
            MaxEpq = v.MaxEpq,
            MaxLoadingMeters = v.MaxLoadingMeters,
            DefaultLengthCm = v.DefaultLengthCm,
            DefaultWidthCm = v.DefaultWidthCm,
            DefaultHeightCm = v.DefaultHeightCm,
            DefaultGrossVehicleWeightKg = v.DefaultGrossVehicleWeightKg
        }).ToList());

    protected override async Task<VehicleTypeDto> ToDtoAsync(VehicleType v)
    {
        var ids = v.Characteristics.Select(c => c.TransportCharacteristicsId).ToList();
        var characteristics = ids.Count == 0 ? [] : await _characteristics.GetListAsync(c => ids.Contains(c.Id));

        var dto = new VehicleTypeDto
        {
            Name = v.Name,
            Kind = v.Kind,
            MaxEpq = v.MaxEpq,
            MaxLoadingMeters = v.MaxLoadingMeters,
            DefaultLengthCm = v.DefaultLengthCm,
            DefaultWidthCm = v.DefaultWidthCm,
            DefaultHeightCm = v.DefaultHeightCm,
            DefaultGrossVehicleWeightKg = v.DefaultGrossVehicleWeightKg,
            TransportCharacteristics = characteristics
                .Select(c => new MasterDataLookupDto { Id = c.Id, Name = c.Name, Code = c.Code })
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
        };
        MapAudit(v, dto);
        return dto;
    }

    protected override MasterDataLookupDto ToLookup(VehicleType v) => new() { Id = v.Id, Name = v.Name, Code = v.Kind.ToString() };

    protected override string DisplayNameOf(VehicleType v) => v.Name;

    protected override async Task<VehicleType> CreateEntityAsync(VehicleTypeCreateDto input)
    {
        var name = input.Name.Trim();
        await EnsureUniqueAsync(v => v.Name == name, "name", name);
        var vehicleType = new VehicleType(GuidGenerator.Create(), CurrentTenant.Id, name, input.Kind);
        await ApplyAsync(vehicleType, input);
        return vehicleType;
    }

    protected override async Task ApplyUpdateAsync(VehicleType vehicleType, VehicleTypeUpdateDto input)
    {
        var name = input.Name.Trim();
        if (name != vehicleType.Name)
        {
            await EnsureUniqueAsync(v => v.Name == name && v.Id != vehicleType.Id, "name", name);
            vehicleType.SetName(name);
        }

        vehicleType.SetKind(input.Kind);
        await ApplyAsync(vehicleType, input);
    }

    private async Task ApplyAsync(VehicleType vehicleType, VehicleTypeCreateDto input)
    {
        var ids = input.TransportCharacteristicsIds.Distinct().ToList();
        if (ids.Count > 0 && await _characteristics.CountAsync(c => ids.Contains(c.Id)) != ids.Count)
        {
            throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "transportCharacteristicsIds");
        }

        vehicleType.SetDescription(input.Description);
        vehicleType.MaxEpq = input.MaxEpq;
        vehicleType.MaxLoadingMeters = input.MaxLoadingMeters;
        vehicleType.DefaultLengthCm = input.DefaultLengthCm;
        vehicleType.DefaultWidthCm = input.DefaultWidthCm;
        vehicleType.DefaultHeightCm = input.DefaultHeightCm;
        vehicleType.DefaultGrossVehicleWeightKg = input.DefaultGrossVehicleWeightKg;
        vehicleType.SetCharacteristics(ids);
    }

    protected override async Task<string?> GetDeleteBlockerAsync(VehicleType vehicleType)
    {
        if (await _loadFactors.AnyAsync(f => f.VehicleTypeId == vehicleType.Id))
        {
            return L["MasterData:InUseByTransportUnits"];
        }

        return null;
    }

    protected override Task BeforeDeleteAsync(VehicleType vehicleType)
    {
        vehicleType.SetCharacteristics([]);
        return Task.CompletedTask;
    }
}
