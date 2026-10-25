using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Published;
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

/* Design 51. Pickers (the lookup) need only the app feature; the list's own screen needs its node (PO 2026-10-14).
 * The relation category and default relation are Master Data ids, validated and named through the published
 * IMasterDataReferenceLookup (module boundary rule 3: the synchronous call design 51 states). */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class WaypointTypeAppService
    : ReferenceDataCrudAppServiceBase<WaypointType, WaypointTypeRowDto, WaypointTypeDto, WaypointTypeCreateDto, WaypointTypeUpdateDto, ArchivableListRequestDto>,
      IWaypointTypeAppService
{
    private readonly IRepository<TransportOrderType, Guid> _orderTypes;
    private readonly IMasterDataReferenceLookup _masterData;
    private IQueryable<WaypointType>? _successorQuery;

    public WaypointTypeAppService(
        IRepository<WaypointType, Guid> repository,
        IRepository<TransportOrderType, Guid> orderTypes,
        IMasterDataReferenceLookup masterData)
        : base(repository)
    {
        _orderTypes = orderTypes;
        _masterData = masterData;
        LocalizationResource = typeof(OrdersResource);
    }

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.WaypointTypes, RequiresAll = true)]
    [Authorize(OrdersPermissions.WaypointTypes.Default)]
    public async Task<GridResponse<WaypointTypeRowDto>> GetListAsync(ArchivableListRequestDto input)
    {
        /* The successor sort joins the table to itself; the queryable is the repository's, so the tenant filter applies. */
        _successorQuery = await Repository.GetQueryableAsync();
        return await GetListInternalAsync(input);
    }

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.WaypointTypes, RequiresAll = true)]
    [Authorize(OrdersPermissions.WaypointTypes.Default)]
    public Task<WaypointTypeDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(OrdersPermissions.WaypointTypes.Default)]
    public Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.WaypointTypes, RequiresAll = true)]
    [Authorize(OrdersPermissions.WaypointTypes.Create)]
    public Task<WaypointTypeDto> CreateAsync(WaypointTypeCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.WaypointTypes, RequiresAll = true)]
    [Authorize(OrdersPermissions.WaypointTypes.Update)]
    public Task<WaypointTypeDto> UpdateAsync(Guid id, WaypointTypeUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.WaypointTypes, RequiresAll = true)]
    [Authorize(OrdersPermissions.WaypointTypes.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.WaypointTypes, RequiresAll = true)]
    [Authorize(OrdersPermissions.WaypointTypes.Delete)]
    public Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => input.IncludeArchived ? q : q.Where(t => !t.IsArchived));

    protected override IQueryable<WaypointType> ApplyFilters(IQueryable<WaypointType> query, ArchivableListRequestDto request)
        => request.IncludeArchived ? query : query.Where(t => !t.IsArchived);

    protected override IQueryable<WaypointType> ApplySearch(IQueryable<WaypointType> query, string term)
    {
        var language = UiLanguage;
        return query.Where(t =>
            t.Code.ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(t.Name, language) ?? "").ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(t.Name, LocalizedName.DefaultCulture) ?? "").ToLower().Contains(term));
    }

    protected override IOrderedQueryable<WaypointType> ApplySort(IQueryable<WaypointType> query, string? sortBy, bool descending)
    {
        var language = UiLanguage;
        return (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "code" => Order(query, t => t.Code, descending),
            "name" => Order(query, t => LocalizedNameQuery.Text(t.Name, language) ?? LocalizedNameQuery.Text(t.Name, LocalizedName.DefaultCulture), descending),
            "activity" => Order(query, t => t.Activity, descending),
            "successivewaypointtypename" => OrderBySuccessor(query, language, descending),
            "defaultdurationminutes" => Order(query, t => t.DefaultDurationMinutes, descending),
            "isarchived" => Order(query, t => t.IsArchived, descending),
            _ => throw UnknownSortColumn(sortBy)
        };
    }

    private IOrderedQueryable<WaypointType> OrderBySuccessor(IQueryable<WaypointType> query, string language, bool descending)
    {
        /* Only the list sorts on it (the lookup and the bulk delete use the default order). */
        var others = _successorQuery ?? throw UnknownSortColumn("successiveWaypointTypeName");
        return OrderByJoined(
            query,
            others,
            t => t.SuccessiveWaypointTypeId,
            o => (Guid?)o.Id,
            row => row.Other == null ? null : LocalizedNameQuery.Text(row.Other.Name, language) ?? LocalizedNameQuery.Text(row.Other.Name, LocalizedName.DefaultCulture),
            descending);
    }

    protected override IQueryable<WaypointType> ApplyLookupFilter(IQueryable<WaypointType> query) => query.Where(t => !t.IsArchived);

    protected override async Task<List<WaypointTypeRowDto>> ToRowsAsync(List<WaypointType> entities)
    {
        var names = await ResolveReferencesAsync(entities);
        return entities.Select(t => new WaypointTypeRowDto
        {
            Id = t.Id,
            Code = t.Code,
            Name = Resolve(t.Name),
            Activity = t.Activity,
            SuccessiveWaypointTypeName = names.Successor(t.SuccessiveWaypointTypeId),
            RelationCategoryName = names.Category(t.RelationCategoryId),
            DefaultRelationName = names.Relation(t.DefaultRelationId),
            DefaultDurationMinutes = t.DefaultDurationMinutes,
            IsArchived = t.IsArchived,
            Description = t.Description
        }).ToList();
    }

    protected override async Task<WaypointTypeDto> ToDtoAsync(WaypointType t)
    {
        var names = await ResolveReferencesAsync([t]);
        var dto = new WaypointTypeDto
        {
            Code = t.Code,
            Name = Resolve(t.Name),
            Names = ToNamesDto(t.Name),
            Activity = t.Activity,
            SuccessiveWaypointTypeId = t.SuccessiveWaypointTypeId,
            SuccessiveWaypointTypeName = names.Successor(t.SuccessiveWaypointTypeId),
            RelationCategoryId = t.RelationCategoryId,
            RelationCategoryName = names.Category(t.RelationCategoryId),
            DefaultRelationId = t.DefaultRelationId,
            DefaultRelationName = names.Relation(t.DefaultRelationId),
            DefaultDurationMinutes = t.DefaultDurationMinutes,
            FleetManagementCodes = t.FleetManagementCodes
                .OrderBy(c => c.Vendor)
                .Select(c => new WaypointTypeFleetManagementCodeDto { Vendor = c.Vendor, Code = c.Code })
                .ToList(),
            IsArchived = t.IsArchived
        };
        MapAudit(t, dto);
        return dto;
    }

    protected override ReferenceDataLookupDto ToLookup(WaypointType t) => new() { Id = t.Id, Name = Resolve(t.Name), Code = t.Code };

    protected override string DisplayNameOf(WaypointType t) => Resolve(t.Name);

    protected override async Task<WaypointType> CreateEntityAsync(WaypointTypeCreateDto input)
    {
        var code = ReferenceDataAggregateRoot.NormalizeCode(input.Code);
        await EnsureUniqueAsync(t => t.Code == code, "code", code);
        var type = new WaypointType(GuidGenerator.Create(), CurrentTenant.Id, code, ToLocalizedName(input.Names), input.Activity);
        await ApplyAsync(type, input);
        return type;
    }

    protected override async Task ApplyUpdateAsync(WaypointType type, WaypointTypeUpdateDto input)
    {
        var code = ReferenceDataAggregateRoot.NormalizeCode(input.Code);
        if (code != type.Code)
        {
            await EnsureUniqueAsync(t => t.Code == code && t.Id != type.Id, "code", code);
            type.SetCode(code);
        }

        type.SetName(ToLocalizedName(input.Names));
        type.SetActivity(input.Activity);
        await ApplyAsync(type, input);
    }

    private async Task ApplyAsync(WaypointType type, WaypointTypeCreateDto input)
    {
        type.SetDescription(input.Description);
        type.SetDefaultDuration(input.DefaultDurationMinutes);

        /* Self first (its own error), then existence in this tenant. */
        type.SetSuccessor(input.SuccessiveWaypointTypeId);
        if (input.SuccessiveWaypointTypeId is { } successor)
        {
            await EnsureAllExistAsync([successor], "successiveWaypointTypeId");
        }

        await EnsureMasterDataExistsAsync(input.RelationCategoryId, input.DefaultRelationId);
        type.SetDefaults(input.RelationCategoryId, input.DefaultRelationId);
        type.SetFleetManagementCodes((input.FleetManagementCodes ?? []).Select(c => (c.Vendor, c.Code)), GuidGenerator.Create);
        type.SetArchived(input.IsArchived);
    }

    /// <summary>A Master Data id that is unknown (or deleted there, Assumption 6) is refused on save.</summary>
    private async Task EnsureMasterDataExistsAsync(Guid? relationCategoryId, Guid? defaultRelationId)
    {
        if (relationCategoryId is { } categoryId && (await _masterData.GetRelationCategoriesAsync([categoryId])).All(c => c.Id != categoryId))
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "relationCategoryId");
        }

        if (defaultRelationId is { } relationId && (await _masterData.GetRelationsAsync([relationId])).All(r => r.Id != relationId))
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "defaultRelationId");
        }
    }

    protected override async Task<string?> GetDeleteBlockerAsync(WaypointType type)
    {
        var id = type.Id;
        var orderType = await AsyncExecuter.FirstOrDefaultAsync((await _orderTypes.GetQueryableAsync())
            .Where(o => o.DefaultWaypoints.Any(w => w.WaypointTypeId == id))
            .OrderBy(o => o.Code));
        if (orderType != null)
        {
            return string.Format(L["Orders:WaypointTypeUsedAsDefaultStop"].Value, Resolve(orderType.Name));
        }

        var predecessor = await AsyncExecuter.FirstOrDefaultAsync((await Repository.GetQueryableAsync())
            .Where(t => t.SuccessiveWaypointTypeId == id && t.Id != id)
            .OrderBy(t => t.Code));
        return predecessor != null ? string.Format(L["Orders:WaypointTypeUsedAsSuccessor"].Value, Resolve(predecessor.Name)) : null;
    }

    /// <summary>The names of the successors (one query) and of the Master Data rows (one lookup call each).</summary>
    private async Task<ReferenceNames> ResolveReferencesAsync(IReadOnlyCollection<WaypointType> types)
    {
        var successorIds = types.Where(t => t.SuccessiveWaypointTypeId != null).Select(t => t.SuccessiveWaypointTypeId!.Value).Distinct().ToList();
        var categoryIds = types.Where(t => t.RelationCategoryId != null).Select(t => t.RelationCategoryId!.Value).Distinct().ToList();
        var relationIds = types.Where(t => t.DefaultRelationId != null).Select(t => t.DefaultRelationId!.Value).Distinct().ToList();

        var successors = successorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await Repository.GetListAsync(t => successorIds.Contains(t.Id))).ToDictionary(t => t.Id, t => Resolve(t.Name));
        var categories = categoryIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _masterData.GetRelationCategoriesAsync(categoryIds)).ToDictionary(c => c.Id, c => c.Name);
        var relations = relationIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _masterData.GetRelationsAsync(relationIds)).ToDictionary(r => r.Id, r => r.Name);

        return new ReferenceNames(successors, categories, relations, L["Orders:NotFound"].Value);
    }

    private sealed record ReferenceNames(
        Dictionary<Guid, string> Successors,
        Dictionary<Guid, string> Categories,
        Dictionary<Guid, string> Relations,
        string NotFound)
    {
        public string? Successor(Guid? id) => Name(Successors, id);

        public string? Category(Guid? id) => Name(Categories, id);

        public string? Relation(Guid? id) => Name(Relations, id);

        private string? Name(Dictionary<Guid, string> names, Guid? id)
            => id == null ? null : names.TryGetValue(id.Value, out var name) ? name : NotFound;
    }
}
