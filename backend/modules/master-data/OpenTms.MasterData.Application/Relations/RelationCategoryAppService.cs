using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Features;
using OpenTms.MasterData.Permissions;
using OpenTms.Platform.Grid;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.MasterData.Relations;

[RequiresFeature(MasterDataFeatures.FirstRun)]
public class RelationCategoryAppService
    : MasterDataCrudAppServiceBase<RelationCategory, RelationCategoryRowDto, RelationCategoryDto, RelationCategoryCreateDto, RelationCategoryUpdateDto, MasterDataListRequestDto>,
      IRelationCategoryAppService
{
    public RelationCategoryAppService(IRepository<RelationCategory, Guid> repository)
        : base(repository)
    {
    }

    [Authorize(MasterDataPermissions.RelationCategories.Default)]
    public Task<GridResponse<RelationCategoryRowDto>> GetListAsync(MasterDataListRequestDto input) => GetListInternalAsync(input);

    [Authorize(MasterDataPermissions.RelationCategories.Default)]
    public Task<RelationCategoryDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(MasterDataPermissions.RelationCategories.Default)]
    public Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [Authorize(MasterDataPermissions.RelationCategories.Create)]
    public Task<RelationCategoryDto> CreateAsync(RelationCategoryCreateDto input) => CreateInternalAsync(input);

    [Authorize(MasterDataPermissions.RelationCategories.Update)]
    public Task<RelationCategoryDto> UpdateAsync(Guid id, RelationCategoryUpdateDto input) => UpdateInternalAsync(id, input);

    [Authorize(MasterDataPermissions.RelationCategories.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [Authorize(MasterDataPermissions.RelationCategories.Delete)]
    public Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(MasterDataBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => q);

    protected override IQueryable<RelationCategory> ApplySearch(IQueryable<RelationCategory> query, string term)
        => query.Where(c => c.Name.ToLower().Contains(term) || (c.Description != null && c.Description.ToLower().Contains(term)));

    protected override IOrderedQueryable<RelationCategory> ApplySort(IQueryable<RelationCategory> query, string? sortBy, bool descending)
        => (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "name" => Order(query, c => c.Name, descending),
            "description" => Order(query, c => c.Description, descending),
            _ => throw UnknownSortColumn(sortBy)
        };

    protected override Task<List<RelationCategoryRowDto>> ToRowsAsync(List<RelationCategory> entities)
        => Task.FromResult(entities.Select(c => new RelationCategoryRowDto { Id = c.Id, Name = c.Name, Description = c.Description }).ToList());

    protected override Task<RelationCategoryDto> ToDtoAsync(RelationCategory c)
    {
        var dto = new RelationCategoryDto { Name = c.Name };
        MapAudit(c, dto);
        return Task.FromResult(dto);
    }

    protected override MasterDataLookupDto ToLookup(RelationCategory c) => new() { Id = c.Id, Name = c.Name };

    protected override string DisplayNameOf(RelationCategory c) => c.Name;

    protected override async Task<RelationCategory> CreateEntityAsync(RelationCategoryCreateDto input)
    {
        var name = input.Name.Trim();
        await EnsureUniqueAsync(c => c.Name == name, "name", name);
        var category = new RelationCategory(GuidGenerator.Create(), CurrentTenant.Id, name);
        category.SetDescription(input.Description);
        return category;
    }

    protected override async Task ApplyUpdateAsync(RelationCategory category, RelationCategoryUpdateDto input)
    {
        var name = input.Name.Trim();
        if (name != category.Name)
        {
            await EnsureUniqueAsync(c => c.Name == name && c.Id != category.Id, "name", name);
            category.SetName(name);
        }

        category.SetDescription(input.Description);
    }
}
