using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Permissions;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Relations;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.MasterData.Transport;

/* Pickers need only the app feature; the list's own screen needs its node (PO 2026-10-14, design 48). */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class ProductRelationCodeAppService
    : MasterDataCrudAppServiceBase<ProductRelationCode, ProductRelationCodeRowDto, ProductRelationCodeDto, ProductRelationCodeCreateDto, ProductRelationCodeUpdateDto, ArchivableListRequestDto>,
      IProductRelationCodeAppService
{
    private readonly IRepository<Relation, Guid> _relations;
    private readonly IRepository<RelationAddress, Guid> _addresses;
    private readonly IRepository<AddressType, Guid> _addressTypes;
    private readonly IRepository<Product, Guid> _products;
    private readonly IRepository<TransportUnit, Guid> _transportUnits;

    /* The relation and product queries the search and the name sorts correlate with; same DbContext. */
    private IQueryable<Relation>? _relationQuery;
    private IQueryable<Product>? _productQuery;
    private IQueryable<TransportUnit>? _transportUnitQuery;

    public ProductRelationCodeAppService(
        IRepository<ProductRelationCode, Guid> repository,
        IRepository<Relation, Guid> relations,
        IRepository<RelationAddress, Guid> addresses,
        IRepository<AddressType, Guid> addressTypes,
        IRepository<Product, Guid> products,
        IRepository<TransportUnit, Guid> transportUnits)
        : base(repository)
    {
        _relations = relations;
        _addresses = addresses;
        _addressTypes = addressTypes;
        _products = products;
        _transportUnits = transportUnits;
    }

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Transport.Default, AppFeatures.MasterData.General.Transport.ProductRelationCodes, RequiresAll = true)]
    [Authorize(MasterDataPermissions.ProductRelationCodes.Default)]
    public async Task<GridResponse<ProductRelationCodeRowDto>> GetListAsync(ArchivableListRequestDto input)
    {
        await PrepareQueriesAsync();
        return await GetListInternalAsync(input);
    }

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Transport.Default, AppFeatures.MasterData.General.Transport.ProductRelationCodes, RequiresAll = true)]
    [Authorize(MasterDataPermissions.ProductRelationCodes.Default)]
    public Task<ProductRelationCodeDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(MasterDataPermissions.ProductRelationCodes.Default)]
    public async Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake)
    {
        await PrepareQueriesAsync();
        return await GetLookupInternalAsync(term, take);
    }

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Transport.Default, AppFeatures.MasterData.General.Transport.ProductRelationCodes, RequiresAll = true)]
    [Authorize(MasterDataPermissions.ProductRelationCodes.Create)]
    public Task<ProductRelationCodeDto> CreateAsync(ProductRelationCodeCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Transport.Default, AppFeatures.MasterData.General.Transport.ProductRelationCodes, RequiresAll = true)]
    [Authorize(MasterDataPermissions.ProductRelationCodes.Update)]
    public Task<ProductRelationCodeDto> UpdateAsync(Guid id, ProductRelationCodeUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Transport.Default, AppFeatures.MasterData.General.Transport.ProductRelationCodes, RequiresAll = true)]
    [Authorize(MasterDataPermissions.ProductRelationCodes.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Transport.Default, AppFeatures.MasterData.General.Transport.ProductRelationCodes, RequiresAll = true)]
    [Authorize(MasterDataPermissions.ProductRelationCodes.Delete)]
    public async Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input)
    {
        await PrepareQueriesAsync();
        return await BulkDeleteInternalAsync(input.Selection, q => ApplyFilters(q, input.IncludeArchived));
    }

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Transport.Default, AppFeatures.MasterData.General.Transport.ProductRelationCodes, RequiresAll = true)]
    [Authorize(MasterDataPermissions.ProductRelationCodes.Update)]
    public async Task ArchiveAsync(Guid id)
    {
        var code = await Repository.GetAsync(id);
        code.Archive();
        await Repository.UpdateAsync(code, autoSave: true);
    }

    [RequiresFeature(AppFeatures.MasterData.General.Default, AppFeatures.MasterData.General.Transport.Default, AppFeatures.MasterData.General.Transport.ProductRelationCodes, RequiresAll = true)]
    [Authorize(MasterDataPermissions.ProductRelationCodes.Update)]
    public async Task UnarchiveAsync(Guid id)
    {
        var code = await Repository.GetAsync(id);
        code.Unarchive();
        await Repository.UpdateAsync(code, autoSave: true);
    }

    // ── list hooks ────────────────────────────────────────────────────────────

    private async Task PrepareQueriesAsync()
    {
        _relationQuery = await _relations.GetQueryableAsync();
        _productQuery = await _products.GetQueryableAsync();
        _transportUnitQuery = await _transportUnits.GetQueryableAsync();
    }

    protected override IQueryable<ProductRelationCode> ApplyFilters(IQueryable<ProductRelationCode> query, ArchivableListRequestDto request)
        => ApplyFilters(query, request.IncludeArchived);

    private static IQueryable<ProductRelationCode> ApplyFilters(IQueryable<ProductRelationCode> query, bool includeArchived)
        => includeArchived ? query : query.Where(c => !c.IsArchived);

    /// <summary>The wildcard over the code (trigram index on the normalised code), the relation's name and the product's name.</summary>
    protected override IQueryable<ProductRelationCode> ApplySearch(IQueryable<ProductRelationCode> query, string term)
    {
        var code = term.ToUpperInvariant();
        var key = MasterDataValues.SearchKey(term);
        var hasKey = key.Length > 0;
        var relations = _relationQuery!;
        var products = _productQuery!;
        return query.Where(c =>
            c.NormalizedCode.Contains(code) ||
            (hasKey && c.RelationId != null && relations.Any(r => r.Id == c.RelationId && r.SearchKey.Contains(key))) ||
            (c.ProductId != null && products.Any(p => p.Id == c.ProductId && p.Name.ToLower().Contains(term))));
    }

    protected override IOrderedQueryable<ProductRelationCode> ApplySort(IQueryable<ProductRelationCode> query, string? sortBy, bool descending)
    {
        var key = sortBy?.ToLowerInvariant();
        switch (key)
        {
            case "relationname":
            {
                var relations = _relationQuery ?? throw UnknownSortColumn(sortBy!);
                return OrderByJoined(query, relations, c => c.RelationId, r => (Guid?)r.Id, j => j.Other!.Name, descending);
            }
            case "productname":
            {
                var products = _productQuery ?? throw UnknownSortColumn(sortBy!);
                return OrderByJoined(query, products, c => c.ProductId, p => (Guid?)p.Id, j => j.Other!.Name, descending);
            }
            case "transportunitname":
            {
                var units = _transportUnitQuery ?? throw UnknownSortColumn(sortBy!);
                return OrderByJoined(query, units, c => c.TransportUnitId, u => (Guid?)u.Id, j => j.Other!.Name, descending);
            }
            case null or "" or "code":
                return Order(query, c => c.NormalizedCode, descending);
            case "isarchived":
                return Order(query, c => c.IsArchived, descending);
            default:
                throw UnknownSortColumn(sortBy!);
        }
    }

    protected override IQueryable<ProductRelationCode> ApplyLookupFilter(IQueryable<ProductRelationCode> query) => query.Where(c => !c.IsArchived);

    protected override async Task<List<ProductRelationCodeRowDto>> ToRowsAsync(List<ProductRelationCode> entities)
    {
        var relationIds = entities.Where(c => c.RelationId != null).Select(c => c.RelationId!.Value).Distinct().ToList();
        var relations = relationIds.Count == 0 ? new Dictionary<Guid, string>() : (await _relations.GetListAsync(r => relationIds.Contains(r.Id))).ToDictionary(r => r.Id, r => r.Name);
        var cities = await RelationPlaces.CitiesAsync(_addresses, _addressTypes, relationIds);
        var products = await NamesAsync(_products, entities.Select(c => c.ProductId), p => p.Name);
        var units = await NamesAsync(_transportUnits, entities.Select(c => c.TransportUnitId), u => u.Name);

        return entities.Select(c => new ProductRelationCodeRowDto
        {
            Id = c.Id,
            Code = c.Code,
            RelationId = c.RelationId,
            RelationName = c.RelationId == null ? null : relations.GetValueOrDefault(c.RelationId.Value),
            RelationCity = c.RelationId == null ? null : cities.GetValueOrDefault(c.RelationId.Value),
            ProductName = c.ProductId == null ? null : products.GetValueOrDefault(c.ProductId.Value),
            TransportUnitName = c.TransportUnitId == null ? null : units.GetValueOrDefault(c.TransportUnitId.Value),
            IsArchived = c.IsArchived
        }).ToList();
    }

    protected override async Task<ProductRelationCodeDto> ToDtoAsync(ProductRelationCode c)
    {
        var relation = c.RelationId == null ? null : await _relations.FindAsync(c.RelationId.Value);
        var cities = c.RelationId == null ? new Dictionary<Guid, string>() : await RelationPlaces.CitiesAsync(_addresses, _addressTypes, [c.RelationId.Value]);
        var product = c.ProductId == null ? null : await _products.FindAsync(c.ProductId.Value);
        var unit = c.TransportUnitId == null ? null : await _transportUnits.FindAsync(c.TransportUnitId.Value);

        var dto = new ProductRelationCodeDto
        {
            Code = c.Code,
            RelationId = c.RelationId,
            RelationCode = relation?.Code,
            RelationName = relation?.Name,
            RelationCity = c.RelationId == null ? null : cities.GetValueOrDefault(c.RelationId.Value),
            ProductId = c.ProductId,
            ProductName = product?.Name,
            TransportUnitId = c.TransportUnitId,
            TransportUnitName = unit?.Name,
            IsArchived = c.IsArchived
        };
        MapAudit(c, dto);
        return dto;
    }

    protected override MasterDataLookupDto ToLookup(ProductRelationCode c) => new() { Id = c.Id, Name = c.Code, Code = c.NormalizedCode };

    protected override string DisplayNameOf(ProductRelationCode c) => c.Code;

    // ── write hooks ───────────────────────────────────────────────────────────

    protected override async Task<ProductRelationCode> CreateEntityAsync(ProductRelationCodeCreateDto input)
    {
        var entity = new ProductRelationCode(GuidGenerator.Create(), CurrentTenant.Id, input.Code);
        var normalized = entity.NormalizedCode;
        await EnsureUniqueAsync(c => c.NormalizedCode == normalized, "code", entity.Code);
        await ApplyAsync(entity, input);
        return entity;
    }

    protected override async Task ApplyUpdateAsync(ProductRelationCode entity, ProductRelationCodeUpdateDto input)
    {
        var previous = entity.NormalizedCode;
        entity.SetCode(input.Code);
        if (entity.NormalizedCode != previous)
        {
            var normalized = entity.NormalizedCode;
            await EnsureUniqueAsync(c => c.NormalizedCode == normalized && c.Id != entity.Id, "code", entity.Code);
        }

        await ApplyAsync(entity, input);
    }

    private async Task ApplyAsync(ProductRelationCode entity, ProductRelationCodeCreateDto input)
    {
        /* Referenced rows of this tenant only (constitution rule 1: the filtered repositories see one tenant). */
        if (input.RelationId != null && !await _relations.AnyAsync(r => r.Id == input.RelationId.Value))
        {
            throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "relationId");
        }

        if (input.ProductId != null && !await _products.AnyAsync(p => p.Id == input.ProductId.Value))
        {
            throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "productId");
        }

        if (input.TransportUnitId != null && !await _transportUnits.AnyAsync(u => u.Id == input.TransportUnitId.Value))
        {
            throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "transportUnitId");
        }

        entity.SetTargets(input.RelationId, input.ProductId, input.TransportUnitId);
        entity.SetDescription(input.Description);
        if (input.IsArchived)
        {
            entity.Archive();
        }
        else
        {
            entity.Unarchive();
        }
    }

    private static async Task<Dictionary<Guid, string>> NamesAsync<TEntity>(IRepository<TEntity, Guid> repository, IEnumerable<Guid?> ids, Func<TEntity, string> name)
        where TEntity : MasterDataAggregateRoot
    {
        var distinct = ids.Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        return distinct.Count == 0 ? [] : (await repository.GetListAsync(e => distinct.Contains(e.Id))).ToDictionary(e => e.Id, name);
    }
}
