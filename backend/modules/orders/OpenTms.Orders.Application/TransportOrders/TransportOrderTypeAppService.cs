using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Orders.Localization;
using OpenTms.Orders.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Localization;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Orders.TransportOrders;

/* Design 51. Pickers (the lookup) need only the app feature; the list's own screen needs its node (PO 2026-10-14). */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class TransportOrderTypeAppService
    : ReferenceDataCrudAppServiceBase<TransportOrderType, TransportOrderTypeRowDto, TransportOrderTypeDto, TransportOrderTypeCreateDto, TransportOrderTypeUpdateDto, ArchivableListRequestDto>,
      ITransportOrderTypeAppService
{
    private readonly IRepository<WaypointType, Guid> _waypointTypes;
    private readonly IRepository<TransportOrderTypeDefaultWaypoint> _defaultWaypoints;

    public TransportOrderTypeAppService(
        IRepository<TransportOrderType, Guid> repository,
        IRepository<WaypointType, Guid> waypointTypes,
        IRepository<TransportOrderTypeDefaultWaypoint> defaultWaypoints)
        : base(repository)
    {
        _waypointTypes = waypointTypes;
        _defaultWaypoints = defaultWaypoints;
        LocalizationResource = typeof(OrdersResource);
    }

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.TransportOrderTypes, RequiresAll = true)]
    [Authorize(OrdersPermissions.TransportOrderTypes.Default)]
    public Task<GridResponse<TransportOrderTypeRowDto>> GetListAsync(ArchivableListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.TransportOrderTypes, RequiresAll = true)]
    [Authorize(OrdersPermissions.TransportOrderTypes.Default)]
    public Task<TransportOrderTypeDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(OrdersPermissions.TransportOrderTypes.Default)]
    public Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.TransportOrderTypes, RequiresAll = true)]
    [Authorize(OrdersPermissions.TransportOrderTypes.Create)]
    public Task<TransportOrderTypeDto> CreateAsync(TransportOrderTypeCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.TransportOrderTypes, RequiresAll = true)]
    [Authorize(OrdersPermissions.TransportOrderTypes.Update)]
    public Task<TransportOrderTypeDto> UpdateAsync(Guid id, TransportOrderTypeUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.TransportOrderTypes, RequiresAll = true)]
    [Authorize(OrdersPermissions.TransportOrderTypes.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        EnsureNotDefault(await Repository.GetAsync(id), "delete");
        await DeleteInternalAsync(id);
    }

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.TransportOrderTypes, RequiresAll = true)]
    [Authorize(OrdersPermissions.TransportOrderTypes.Delete)]
    public Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => input.IncludeArchived ? q : q.Where(t => !t.IsArchived));

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.TransportOrderTypes, RequiresAll = true)]
    [Authorize(OrdersPermissions.TransportOrderTypes.Update)]
    public Task SetDefaultAsync(Guid id) => SetDefaultInternalAsync(id);

    protected override IQueryable<TransportOrderType> ApplyFilters(IQueryable<TransportOrderType> query, ArchivableListRequestDto request)
        => request.IncludeArchived ? query : query.Where(t => !t.IsArchived);

    protected override IQueryable<TransportOrderType> ApplySearch(IQueryable<TransportOrderType> query, string term)
    {
        var language = UiLanguage;
        return query.Where(t =>
            t.Code.ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(t.Name, language) ?? "").ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(t.Name, LocalizedName.DefaultCulture) ?? "").ToLower().Contains(term));
    }

    protected override IOrderedQueryable<TransportOrderType> ApplySort(IQueryable<TransportOrderType> query, string? sortBy, bool descending)
    {
        var language = UiLanguage;
        return (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "code" => Order(query, t => t.Code, descending),
            "name" => Order(query, t => LocalizedNameQuery.Text(t.Name, language) ?? LocalizedNameQuery.Text(t.Name, LocalizedName.DefaultCulture), descending),
            "isdefault" => Order(query, t => t.IsDefault, descending),
            "isarchived" => Order(query, t => t.IsArchived, descending),
            _ => throw UnknownSortColumn(sortBy)
        };
    }

    protected override IQueryable<TransportOrderType> ApplyLookupFilter(IQueryable<TransportOrderType> query) => query.Where(t => !t.IsArchived);

    protected override async Task<List<TransportOrderTypeRowDto>> ToRowsAsync(List<TransportOrderType> entities)
    {
        /* The list loads without details: the page's stops and their types' names, one query each. */
        var typeIds = entities.Select(t => t.Id).ToList();
        var stops = typeIds.Count == 0 ? [] : await _defaultWaypoints.GetListAsync(w => typeIds.Contains(w.TransportOrderTypeId));
        var names = await WaypointTypeNamesAsync(stops.Select(w => w.WaypointTypeId));
        var stopsByType = stops.ToLookup(w => w.TransportOrderTypeId);

        return entities.Select(t => new TransportOrderTypeRowDto
        {
            Id = t.Id,
            Code = t.Code,
            Name = Resolve(t.Name),
            NumberingExample = NumberingExampleOf(t.Numbering).Value,
            DefaultWaypoints = string.Join(", ", stopsByType[t.Id].OrderBy(w => w.Position).Select(w => names(w.WaypointTypeId))),
            IsDefault = t.IsDefault,
            IsArchived = t.IsArchived,
            Description = t.Description
        }).ToList();
    }

    protected override async Task<TransportOrderTypeDto> ToDtoAsync(TransportOrderType t)
    {
        var names = await WaypointTypeNamesAsync(t.DefaultWaypoints.Select(w => w.WaypointTypeId));
        var dto = new TransportOrderTypeDto
        {
            Code = t.Code,
            Name = Resolve(t.Name),
            Names = ToNamesDto(t.Name),
            RequiredFields = t.RequiredFields,
            DefaultWaypoints = t.DefaultWaypoints
                .OrderBy(w => w.Position)
                .Select(w => new TransportOrderTypeDefaultWaypointDto { WaypointTypeId = w.WaypointTypeId, Name = names(w.WaypointTypeId), Position = w.Position })
                .ToList(),
            Numbering = ToNumberingDto(t.Numbering),
            NextNumber = await NumberSequences.PeekNextValueAsync(t.SequenceKey, t.Numbering),
            NextNumberPreview = await NumberSequences.PeekNextAsync(t.SequenceKey, t.Numbering),
            IsDefault = t.IsDefault,
            IsArchived = t.IsArchived
        };
        MapAudit(t, dto);
        return dto;
    }

    protected override ReferenceDataLookupDto ToLookup(TransportOrderType t) => new() { Id = t.Id, Name = Resolve(t.Name), Code = t.Code };

    protected override string DisplayNameOf(TransportOrderType t) => Resolve(t.Name);

    protected override async Task<TransportOrderType> CreateEntityAsync(TransportOrderTypeCreateDto input)
    {
        var code = ReferenceDataAggregateRoot.NormalizeCode(input.Code);
        await EnsureUniqueAsync(t => t.Code == code, "code", code);
        var type = new TransportOrderType(GuidGenerator.Create(), CurrentTenant.Id, code, ToLocalizedName(input.Names), ToNumberSequenceFormat(input.Numbering));
        await ApplyAsync(type, input);

        /* A tenant's first type becomes the default (design 51, as design 53). */
        if (!await Repository.AnyAsync())
        {
            type.SetDefault(true);
        }

        type.SetArchived(input.IsArchived);
        return type;
    }

    protected override async Task ApplyUpdateAsync(TransportOrderType type, TransportOrderTypeUpdateDto input)
    {
        var code = ReferenceDataAggregateRoot.NormalizeCode(input.Code);
        if (code != type.Code)
        {
            await EnsureUniqueAsync(t => t.Code == code && t.Id != type.Id, "code", code);
            type.SetCode(code);
        }

        type.SetName(ToLocalizedName(input.Names));
        type.SetNumbering(ToNumberSequenceFormat(input.Numbering));
        await ApplyAsync(type, input);
        type.SetArchived(input.IsArchived);
    }

    private async Task ApplyAsync(TransportOrderType type, TransportOrderTypeCreateDto input)
    {
        type.SetDescription(input.Description);
        type.SetRequiredFields(input.RequiredFields);

        var stops = (input.DefaultWaypoints ?? []).OrderBy(w => w.Position).Select(w => w.WaypointTypeId).ToList();
        if (stops.Count > OrdersConsts.MaxDefaultWaypoints)
        {
            throw new BusinessException(ReferenceDataErrorCodes.TooManyChildren).WithData("max", OrdersConsts.MaxDefaultWaypoints).WithData("field", "defaultWaypoints");
        }

        var distinct = stops.Distinct().ToList();
        if (distinct.Count > 0 && await _waypointTypes.CountAsync(w => distinct.Contains(w.Id)) != distinct.Count)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "defaultWaypoints");
        }

        type.SetDefaultWaypoints(stops);
    }

    protected override Task AfterWriteAsync(TransportOrderType type, TransportOrderTypeCreateDto input)
        => ApplyNextNumberAsync(type.SequenceKey, type.Numbering, input.NextNumber);

    protected override Task<string?> GetDeleteBlockerAsync(TransportOrderType type)
        => Task.FromResult(type.IsDefault ? L["Orders:DefaultOrderTypeCannotBeDeleted"].Value : null);

    /* The stops go with the type, so a deleted type keeps no waypoint type in use. */
    protected override Task BeforeDeleteAsync(TransportOrderType type)
    {
        type.ClearDefaultWaypoints();
        return Task.CompletedTask;
    }

    private async Task<Func<Guid, string>> WaypointTypeNamesAsync(IEnumerable<Guid> ids)
    {
        var keys = ids.Distinct().ToList();
        var names = keys.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _waypointTypes.GetListAsync(w => keys.Contains(w.Id))).ToDictionary(w => w.Id, w => Resolve(w.Name));
        var notFound = L["Orders:NotFound"].Value;
        return id => names.TryGetValue(id, out var name) ? name : notFound;
    }
}
