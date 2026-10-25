using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Localization;
using OpenTms.Platform.Permissions;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Platform.DocumentTypes;

/* Pickers need only the app feature; the list's own screen needs its group and node (PO 2026-10-14, design 54). */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class DocumentTypeAppService
    : PlatformCrudAppServiceBase<DocumentType, DocumentTypeRowDto, DocumentTypeDto, DocumentTypeCreateDto, DocumentTypeUpdateDto, DocumentTypeListRequestDto>,
      IDocumentTypeAppService
{
    public DocumentTypeAppService(IRepository<DocumentType, Guid> repository)
        : base(repository)
    {
    }

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.DocumentTypes, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.DocumentTypes.Default)]
    public Task<GridResponse<DocumentTypeRowDto>> GetListAsync(DocumentTypeListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.DocumentTypes, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.DocumentTypes.Default)]
    public Task<DocumentTypeDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(PlatformPermissions.MasterData.DocumentTypes.Default)]
    public Task<List<PlatformMasterDataLookupDto>> GetLookupAsync(string? term = null, int take = PlatformMasterDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.DocumentTypes, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.DocumentTypes.Create)]
    public Task<DocumentTypeDto> CreateAsync(DocumentTypeCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.DocumentTypes, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.DocumentTypes.Update)]
    public Task<DocumentTypeDto> UpdateAsync(Guid id, DocumentTypeUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.DocumentTypes, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.DocumentTypes.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.DocumentTypes, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.DocumentTypes.Delete)]
    public Task<PlatformMasterDataBulkDeleteResultDto> BulkDeleteAsync(DocumentTypeBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => ApplyFilters(q, input.IncludeArchived));

    /// <summary>Hides the type from pickers; twice is one effect.</summary>
    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.DocumentTypes, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.DocumentTypes.Update)]
    public async Task ArchiveAsync(Guid id)
    {
        var type = await Repository.GetAsync(id, includeDetails: true);
        type.Archive();
        await Repository.UpdateAsync(type, autoSave: true);
    }

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.DocumentTypes, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.DocumentTypes.Update)]
    public async Task UnarchiveAsync(Guid id)
    {
        var type = await Repository.GetAsync(id, includeDetails: true);
        type.Unarchive();
        await Repository.UpdateAsync(type, autoSave: true);
    }

    // ── list hooks ────────────────────────────────────────────────────────────

    protected override IQueryable<DocumentType> ApplyFilters(IQueryable<DocumentType> query, DocumentTypeListRequestDto request)
        => ApplyFilters(query, request.IncludeArchived);

    private static IQueryable<DocumentType> ApplyFilters(IQueryable<DocumentType> query, bool includeArchived)
        => includeArchived ? query : query.Where(t => !t.IsArchived);

    protected override IQueryable<DocumentType> ApplySearch(IQueryable<DocumentType> query, string term)
    {
        var language = UiLanguage;
        return query.Where(t =>
            t.Code.ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(t.Name, language) ?? "").ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(t.Name, LocalizedName.DefaultCulture) ?? "").ToLower().Contains(term) ||
            (t.Description != null && t.Description.ToLower().Contains(term)));
    }

    protected override IOrderedQueryable<DocumentType> ApplySort(IQueryable<DocumentType> query, string? sortBy, bool descending)
    {
        var language = UiLanguage;
        return (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "name" => Order(query, t => LocalizedNameQuery.Text(t.Name, language) ?? LocalizedNameQuery.Text(t.Name, LocalizedName.DefaultCulture), descending),
            "code" => Order(query, t => t.Code, descending),
            "isfreightdocument" => Order(query, t => t.IsFreightDocument, descending),
            "includewithinvoice" => Order(query, t => t.IncludeWithInvoice, descending),
            "isarchived" => Order(query, t => t.IsArchived, descending),
            _ => throw UnknownSortColumn(sortBy)
        };
    }

    protected override IQueryable<DocumentType> ApplyLookupFilter(IQueryable<DocumentType> query) => query.Where(t => !t.IsArchived);

    protected override Task<List<DocumentTypeRowDto>> ToRowsAsync(List<DocumentType> entities)
        => Task.FromResult(entities.Select(t => new DocumentTypeRowDto
        {
            Id = t.Id,
            Code = t.Code,
            Name = Resolve(t.Name),
            IsFreightDocument = t.IsFreightDocument,
            IncludeWithInvoice = t.IncludeWithInvoice,
            IsArchived = t.IsArchived
        }).ToList());

    protected override Task<DocumentTypeDto> ToDtoAsync(DocumentType t)
    {
        var dto = new DocumentTypeDto
        {
            Code = t.Code,
            Name = Resolve(t.Name),
            Names = ToNamesDto(t.Name),
            IsFreightDocument = t.IsFreightDocument,
            IncludeWithInvoice = t.IncludeWithInvoice,
            ShowInListIndicator = t.ShowInListIndicator,
            IsArchived = t.IsArchived,
            Transformations = t.Transformations
                .OrderBy(x => x.Position)
                .Select(x => new DocumentTypeTransformationDto { Position = x.Position, Kind = x.Kind, Argument = x.Argument })
                .ToList()
        };
        MapAudit(t, dto);
        return Task.FromResult(dto);
    }

    protected override PlatformMasterDataLookupDto ToLookup(DocumentType t) => new() { Id = t.Id, Name = Resolve(t.Name), Code = t.Code };

    protected override string DisplayNameOf(DocumentType t) => Resolve(t.Name);

    // ── write hooks ───────────────────────────────────────────────────────────

    protected override async Task<DocumentType> CreateEntityAsync(DocumentTypeCreateDto input)
    {
        var code = DocumentType.NormalizeCode(input.Code);
        await EnsureUniqueAsync(t => t.Code == code, "code", code);

        var type = new DocumentType(GuidGenerator.Create(), CurrentTenant.Id, code, ToLocalizedName(input.Names));
        Apply(type, input);
        return type;
    }

    protected override async Task ApplyUpdateAsync(DocumentType type, DocumentTypeUpdateDto input)
    {
        var code = DocumentType.NormalizeCode(input.Code);
        if (code != type.Code)
        {
            await EnsureUniqueAsync(t => t.Code == code && t.Id != type.Id, "code", code);
            type.SetCode(code);
        }

        type.SetName(ToLocalizedName(input.Names));
        Apply(type, input);
    }

    private static void Apply(DocumentType type, DocumentTypeCreateDto input)
    {
        type.SetFlags(input.IsFreightDocument, input.IncludeWithInvoice, input.ShowInListIndicator);
        type.SetDescription(input.Description);
        if (input.IsArchived)
        {
            type.Archive();
        }
        else
        {
            type.Unarchive();
        }

        /* Position order, then list order for equal positions; renumbered 1, 2, … by the aggregate. */
        var ordered = input.Transformations
            .Select((t, index) => (t, index))
            .OrderBy(x => x.t.Position).ThenBy(x => x.index)
            .Select(x => (x.t.Kind, x.t.Argument))
            .ToList();
        type.SetTransformations(ordered);
    }
}
