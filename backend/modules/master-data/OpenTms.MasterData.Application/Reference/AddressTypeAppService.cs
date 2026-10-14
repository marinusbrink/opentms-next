using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Permissions;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Localization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using OpenTms.Platform.Features;

namespace OpenTms.MasterData.Reference;

/* Pickers need only the app feature; the list's own screen needs its node (PO 2026-10-14). */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class AddressTypeAppService
    : MasterDataCrudAppServiceBase<AddressType, AddressTypeRowDto, AddressTypeDto, AddressTypeCreateDto, AddressTypeUpdateDto, MasterDataListRequestDto>,
      IAddressTypeAppService
{
    public AddressTypeAppService(IRepository<AddressType, Guid> repository)
        : base(repository)
    {
    }

    [RequiresFeature(AppFeatures.MasterData.Relations.Default, AppFeatures.MasterData.Relations.AddressTypes, RequiresAll = true)]
    [Authorize(MasterDataPermissions.AddressTypes.Default)]
    public Task<GridResponse<AddressTypeRowDto>> GetListAsync(MasterDataListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Relations.Default, AppFeatures.MasterData.Relations.AddressTypes, RequiresAll = true)]
    [Authorize(MasterDataPermissions.AddressTypes.Default)]
    public Task<AddressTypeDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(MasterDataPermissions.AddressTypes.Default)]
    public Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.Relations.Default, AppFeatures.MasterData.Relations.AddressTypes, RequiresAll = true)]
    [Authorize(MasterDataPermissions.AddressTypes.Create)]
    public Task<AddressTypeDto> CreateAsync(AddressTypeCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Relations.Default, AppFeatures.MasterData.Relations.AddressTypes, RequiresAll = true)]
    [Authorize(MasterDataPermissions.AddressTypes.Update)]
    public Task<AddressTypeDto> UpdateAsync(Guid id, AddressTypeUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.Relations.Default, AppFeatures.MasterData.Relations.AddressTypes, RequiresAll = true)]
    [Authorize(MasterDataPermissions.AddressTypes.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.Relations.Default, AppFeatures.MasterData.Relations.AddressTypes, RequiresAll = true)]
    [Authorize(MasterDataPermissions.AddressTypes.Delete)]
    public Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(MasterDataBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => q);

    protected override IQueryable<AddressType> ApplySearch(IQueryable<AddressType> query, string term)
    {
        var language = UiLanguage;
        return query.Where(t =>
            (LocalizedNameQuery.Text(t.Name, language) ?? "").ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(t.Name, LocalizedName.DefaultCulture) ?? "").ToLower().Contains(term) ||
            (t.Description != null && t.Description.ToLower().Contains(term)));
    }

    protected override IOrderedQueryable<AddressType> ApplySort(IQueryable<AddressType> query, string? sortBy, bool descending)
    {
        var language = UiLanguage;
        return (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "name" => Order(query, t => LocalizedNameQuery.Text(t.Name, language) ?? LocalizedNameQuery.Text(t.Name, LocalizedName.DefaultCulture), descending),
            "kind" => Order(query, t => t.Kind, descending),
            "description" => Order(query, t => t.Description, descending),
            _ => throw UnknownSortColumn(sortBy)
        };
    }

    protected override Task<List<AddressTypeRowDto>> ToRowsAsync(List<AddressType> entities)
        => Task.FromResult(entities.Select(t => new AddressTypeRowDto
        {
            Id = t.Id,
            Kind = t.Kind,
            Name = Resolve(t.Name),
            Description = t.Description
        }).ToList());

    protected override Task<AddressTypeDto> ToDtoAsync(AddressType t)
    {
        var dto = new AddressTypeDto { Kind = t.Kind, Name = Resolve(t.Name), Names = ToNamesDto(t.Name) };
        MapAudit(t, dto);
        return Task.FromResult(dto);
    }

    protected override MasterDataLookupDto ToLookup(AddressType t) => new() { Id = t.Id, Name = Resolve(t.Name), Code = t.Kind == AddressTypeKind.Other ? null : t.Kind.ToString() };

    protected override string DisplayNameOf(AddressType t) => Resolve(t.Name);

    protected override async Task<AddressType> CreateEntityAsync(AddressTypeCreateDto input)
    {
        await EnsureKindUniqueAsync(input.Kind, null);
        var type = new AddressType(GuidGenerator.Create(), CurrentTenant.Id, input.Kind, ToLocalizedName(input.Names));
        type.SetDescription(input.Description);
        return type;
    }

    protected override async Task ApplyUpdateAsync(AddressType type, AddressTypeUpdateDto input)
    {
        if (input.Kind != type.Kind)
        {
            await EnsureKindUniqueAsync(input.Kind, type.Id);
            type.SetKind(input.Kind);
        }

        type.SetName(ToLocalizedName(input.Names));
        type.SetDescription(input.Description);
    }

    /// <summary>At most one Visiting and one Postal type per tenant (partial unique index); Other is free.</summary>
    private async Task EnsureKindUniqueAsync(AddressTypeKind kind, Guid? exceptId)
    {
        if (kind == AddressTypeKind.Other)
        {
            return;
        }

        await EnsureUniqueAsync(t => t.Kind == kind && (exceptId == null || t.Id != exceptId.Value), "kind", kind.ToString());
    }
}
