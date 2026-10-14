using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Incompatibilities;
using OpenTms.MasterData.Permissions;
using OpenTms.Platform.Grid;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using OpenTms.Platform.Features;

namespace OpenTms.MasterData.Transport;

/* Pickers need only the app feature; the list's own screen needs its node (PO 2026-10-14). */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class TemperatureAppService
    : MasterDataCrudAppServiceBase<Temperature, TemperatureRowDto, TemperatureDto, TemperatureCreateDto, TemperatureUpdateDto, ArchivableListRequestDto>,
      ITemperatureAppService
{
    private readonly IRepository<TemperatureIncompatibility> _pairs;

    public TemperatureAppService(IRepository<Temperature, Guid> repository, IRepository<TemperatureIncompatibility> pairs)
        : base(repository)
    {
        _pairs = pairs;
    }

    [RequiresFeature(AppFeatures.MasterData.Transport.Default, AppFeatures.MasterData.Transport.Temperatures, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Temperatures.Default)]
    public Task<GridResponse<TemperatureRowDto>> GetListAsync(ArchivableListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Transport.Default, AppFeatures.MasterData.Transport.Temperatures, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Temperatures.Default)]
    public Task<TemperatureDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(MasterDataPermissions.Temperatures.Default)]
    public Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.Transport.Default, AppFeatures.MasterData.Transport.Temperatures, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Temperatures.Create)]
    public Task<TemperatureDto> CreateAsync(TemperatureCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Transport.Default, AppFeatures.MasterData.Transport.Temperatures, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Temperatures.Update)]
    public Task<TemperatureDto> UpdateAsync(Guid id, TemperatureUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.Transport.Default, AppFeatures.MasterData.Transport.Temperatures, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Temperatures.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.Transport.Default, AppFeatures.MasterData.Transport.Temperatures, RequiresAll = true)]
    [Authorize(MasterDataPermissions.Temperatures.Delete)]
    public Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => input.IncludeArchived ? q : q.Where(t => !t.IsArchived));

    protected override IQueryable<Temperature> ApplyFilters(IQueryable<Temperature> query, ArchivableListRequestDto request)
        => request.IncludeArchived ? query : query.Where(t => !t.IsArchived);

    protected override IQueryable<Temperature> ApplySearch(IQueryable<Temperature> query, string term)
        => query.Where(t => t.Name.ToLower().Contains(term) || (t.Description != null && t.Description.ToLower().Contains(term)));

    protected override IOrderedQueryable<Temperature> ApplySort(IQueryable<Temperature> query, string? sortBy, bool descending)
        => (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "name" => Order(query, t => t.Name, descending),
            "description" => Order(query, t => t.Description, descending),
            "mincelsius" => Order(query, t => t.MinCelsius, descending),
            "maxcelsius" => Order(query, t => t.MaxCelsius, descending),
            "isarchived" => Order(query, t => t.IsArchived, descending),
            _ => throw UnknownSortColumn(sortBy)
        };

    protected override IQueryable<Temperature> ApplyLookupFilter(IQueryable<Temperature> query) => query.Where(t => !t.IsArchived);

    protected override Task<List<TemperatureRowDto>> ToRowsAsync(List<Temperature> entities)
        => Task.FromResult(entities.Select(t => new TemperatureRowDto
        {
            Id = t.Id,
            Name = t.Name,
            Description = t.Description,
            MinCelsius = t.MinCelsius,
            MaxCelsius = t.MaxCelsius,
            IsArchived = t.IsArchived
        }).ToList());

    protected override async Task<TemperatureDto> ToDtoAsync(Temperature t)
    {
        var dto = new TemperatureDto
        {
            Name = t.Name,
            MinCelsius = t.MinCelsius,
            MaxCelsius = t.MaxCelsius,
            IsArchived = t.IsArchived,
            IncompatibleTemperatures = await GetIncompatiblesAsync(_pairs, t.Id)
        };
        MapAudit(t, dto);
        return dto;
    }

    protected override MasterDataLookupDto ToLookup(Temperature t) => new() { Id = t.Id, Name = t.Name };

    protected override string DisplayNameOf(Temperature t) => t.Name;

    protected override async Task<Temperature> CreateEntityAsync(TemperatureCreateDto input)
    {
        var name = input.Name.Trim();
        await EnsureUniqueAsync(t => t.Name == name, "name", name);
        var temperature = new Temperature(GuidGenerator.Create(), CurrentTenant.Id, name);
        Apply(temperature, input);
        return temperature;
    }

    protected override async Task ApplyUpdateAsync(Temperature temperature, TemperatureUpdateDto input)
    {
        var name = input.Name.Trim();
        if (name != temperature.Name)
        {
            await EnsureUniqueAsync(t => t.Name == name && t.Id != temperature.Id, "name", name);
            temperature.SetName(name);
        }

        Apply(temperature, input);
    }

    private static void Apply(Temperature temperature, TemperatureCreateDto input)
    {
        temperature.SetDescription(input.Description);
        temperature.SetRange(input.MinCelsius, input.MaxCelsius);
        temperature.IsArchived = input.IsArchived;
    }

    protected override Task AfterWriteAsync(Temperature temperature, TemperatureCreateDto input)
        => SyncIncompatibilitiesAsync(_pairs, temperature.Id, input.IncompatibleTemperatureIds, (tenantId, a, b) => new TemperatureIncompatibility(tenantId, a, b));

    protected override Task BeforeDeleteAsync(Temperature temperature) => RemoveIncompatibilitiesAsync(_pairs, temperature.Id);
}
