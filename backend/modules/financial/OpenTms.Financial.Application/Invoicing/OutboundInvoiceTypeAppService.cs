using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Financial.Localization;
using OpenTms.Financial.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Localization;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Financial.Invoicing;

/* Design 52. Pickers (the lookup) need only the app feature; the list's own screen needs its node (PO 2026-10-14). */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class OutboundInvoiceTypeAppService
    : ReferenceDataCrudAppServiceBase<OutboundInvoiceType, OutboundInvoiceTypeRowDto, OutboundInvoiceTypeDto, OutboundInvoiceTypeCreateDto, OutboundInvoiceTypeUpdateDto, ArchivableListRequestDto>,
      IOutboundInvoiceTypeAppService
{
    public OutboundInvoiceTypeAppService(IRepository<OutboundInvoiceType, Guid> repository)
        : base(repository)
    {
        LocalizationResource = typeof(FinancialResource);
    }

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Invoicing.Default, AppFeatures.MasterData.Financial.Invoicing.OutboundInvoiceTypes, RequiresAll = true)]
    [Authorize(FinancialPermissions.OutboundInvoiceTypes.Default)]
    public Task<GridResponse<OutboundInvoiceTypeRowDto>> GetListAsync(ArchivableListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Invoicing.Default, AppFeatures.MasterData.Financial.Invoicing.OutboundInvoiceTypes, RequiresAll = true)]
    [Authorize(FinancialPermissions.OutboundInvoiceTypes.Default)]
    public Task<OutboundInvoiceTypeDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(FinancialPermissions.OutboundInvoiceTypes.Default)]
    public Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Invoicing.Default, AppFeatures.MasterData.Financial.Invoicing.OutboundInvoiceTypes, RequiresAll = true)]
    [Authorize(FinancialPermissions.OutboundInvoiceTypes.Create)]
    public Task<OutboundInvoiceTypeDto> CreateAsync(OutboundInvoiceTypeCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Invoicing.Default, AppFeatures.MasterData.Financial.Invoicing.OutboundInvoiceTypes, RequiresAll = true)]
    [Authorize(FinancialPermissions.OutboundInvoiceTypes.Update)]
    public Task<OutboundInvoiceTypeDto> UpdateAsync(Guid id, OutboundInvoiceTypeUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Invoicing.Default, AppFeatures.MasterData.Financial.Invoicing.OutboundInvoiceTypes, RequiresAll = true)]
    [Authorize(FinancialPermissions.OutboundInvoiceTypes.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        EnsureNotDefault(await Repository.GetAsync(id), "delete");
        await DeleteInternalAsync(id);
    }

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Invoicing.Default, AppFeatures.MasterData.Financial.Invoicing.OutboundInvoiceTypes, RequiresAll = true)]
    [Authorize(FinancialPermissions.OutboundInvoiceTypes.Delete)]
    public Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => input.IncludeArchived ? q : q.Where(t => !t.IsArchived));

    /// <summary>One default per kind (Assumption 5): only the default of the type's own kind moves.</summary>
    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Invoicing.Default, AppFeatures.MasterData.Financial.Invoicing.OutboundInvoiceTypes, RequiresAll = true)]
    [Authorize(FinancialPermissions.OutboundInvoiceTypes.Update)]
    public Task SetDefaultAsync(Guid id) => SetDefaultInternalAsync(id, type => other => other.Kind == type.Kind);

    protected override IQueryable<OutboundInvoiceType> ApplyFilters(IQueryable<OutboundInvoiceType> query, ArchivableListRequestDto request)
        => request.IncludeArchived ? query : query.Where(t => !t.IsArchived);

    protected override IQueryable<OutboundInvoiceType> ApplySearch(IQueryable<OutboundInvoiceType> query, string term)
    {
        var language = UiLanguage;
        return query.Where(t =>
            t.Code.ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(t.Name, language) ?? "").ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(t.Name, LocalizedName.DefaultCulture) ?? "").ToLower().Contains(term));
    }

    protected override IOrderedQueryable<OutboundInvoiceType> ApplySort(IQueryable<OutboundInvoiceType> query, string? sortBy, bool descending)
    {
        var language = UiLanguage;
        return (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "code" => Order(query, t => t.Code, descending),
            "name" => Order(query, t => LocalizedNameQuery.Text(t.Name, language) ?? LocalizedNameQuery.Text(t.Name, LocalizedName.DefaultCulture), descending),
            "kind" => Order(query, t => t.Kind, descending),
            "isdefault" => Order(query, t => t.IsDefault, descending),
            "isarchived" => Order(query, t => t.IsArchived, descending),
            _ => throw UnknownSortColumn(sortBy)
        };
    }

    protected override IQueryable<OutboundInvoiceType> ApplyLookupFilter(IQueryable<OutboundInvoiceType> query) => query.Where(t => !t.IsArchived);

    protected override Task<List<OutboundInvoiceTypeRowDto>> ToRowsAsync(List<OutboundInvoiceType> entities)
        => Task.FromResult(entities.Select(t => new OutboundInvoiceTypeRowDto
        {
            Id = t.Id,
            Code = t.Code,
            Name = Resolve(t.Name),
            Kind = t.Kind,
            NumberingExample = NumberingExampleOf(t.Numbering).Value,
            IsDefault = t.IsDefault,
            IsArchived = t.IsArchived,
            Description = t.Description
        }).ToList());

    protected override async Task<OutboundInvoiceTypeDto> ToDtoAsync(OutboundInvoiceType t)
    {
        var dto = new OutboundInvoiceTypeDto
        {
            Code = t.Code,
            Name = Resolve(t.Name),
            Names = ToNamesDto(t.Name),
            Kind = t.Kind,
            Numbering = ToNumberingDto(t.Numbering),
            NextNumber = await NumberSequences.PeekNextValueAsync(t.SequenceKey, t.Numbering),
            NextNumberPreview = await NumberSequences.PeekNextAsync(t.SequenceKey, t.Numbering),
            IsDefault = t.IsDefault,
            IsArchived = t.IsArchived
        };
        MapAudit(t, dto);
        return dto;
    }

    protected override ReferenceDataLookupDto ToLookup(OutboundInvoiceType t) => new() { Id = t.Id, Name = Resolve(t.Name), Code = t.Code };

    protected override string DisplayNameOf(OutboundInvoiceType t) => Resolve(t.Name);

    protected override async Task<OutboundInvoiceType> CreateEntityAsync(OutboundInvoiceTypeCreateDto input)
    {
        var code = ReferenceDataAggregateRoot.NormalizeCode(input.Code);
        await EnsureUniqueAsync(t => t.Code == code, "code", code);
        var type = new OutboundInvoiceType(GuidGenerator.Create(), CurrentTenant.Id, code, ToLocalizedName(input.Names), input.Kind, ToNumberSequenceFormat(input.Numbering));
        type.SetDescription(input.Description);

        /* The first type of its kind becomes the kind's default (Assumption 5). */
        var kind = type.Kind;
        if (!await Repository.AnyAsync(t => t.Kind == kind))
        {
            type.SetDefault(true);
        }

        type.SetArchived(input.IsArchived);
        return type;
    }

    protected override async Task ApplyUpdateAsync(OutboundInvoiceType type, OutboundInvoiceTypeUpdateDto input)
    {
        var code = ReferenceDataAggregateRoot.NormalizeCode(input.Code);
        if (code != type.Code)
        {
            await EnsureUniqueAsync(t => t.Code == code && t.Id != type.Id, "code", code);
            type.SetCode(code);
        }

        type.SetName(ToLocalizedName(input.Names));
        type.SetKind(input.Kind);
        type.SetNumbering(ToNumberSequenceFormat(input.Numbering));
        type.SetDescription(input.Description);
        type.SetArchived(input.IsArchived);
    }

    protected override Task AfterWriteAsync(OutboundInvoiceType type, OutboundInvoiceTypeCreateDto input)
        => ApplyNextNumberAsync(type.SequenceKey, type.Numbering, input.NextNumber);

    protected override Task<string?> GetDeleteBlockerAsync(OutboundInvoiceType type)
        => Task.FromResult(type.IsDefault ? L["Financial:DefaultInvoiceTypeCannotBeDeleted"].Value : null);
}
