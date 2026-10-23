using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Financial.Localization;
using OpenTms.Financial.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Financial.Invoicing;

/* Design 52. Pickers (the lookup) need only the app feature; the list's own screen needs its node (PO 2026-10-14). */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class InboundInvoiceTypeAppService
    : ReferenceDataCrudAppServiceBase<InboundInvoiceType, InboundInvoiceTypeRowDto, InboundInvoiceTypeDto, InboundInvoiceTypeCreateDto, InboundInvoiceTypeUpdateDto, ArchivableListRequestDto>,
      IInboundInvoiceTypeAppService
{
    public InboundInvoiceTypeAppService(IRepository<InboundInvoiceType, Guid> repository)
        : base(repository)
    {
        LocalizationResource = typeof(FinancialResource);
    }

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Invoicing.Default, AppFeatures.MasterData.Financial.Invoicing.InboundInvoiceTypes, RequiresAll = true)]
    [Authorize(FinancialPermissions.InboundInvoiceTypes.Default)]
    public Task<GridResponse<InboundInvoiceTypeRowDto>> GetListAsync(ArchivableListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Invoicing.Default, AppFeatures.MasterData.Financial.Invoicing.InboundInvoiceTypes, RequiresAll = true)]
    [Authorize(FinancialPermissions.InboundInvoiceTypes.Default)]
    public Task<InboundInvoiceTypeDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(FinancialPermissions.InboundInvoiceTypes.Default)]
    public Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Invoicing.Default, AppFeatures.MasterData.Financial.Invoicing.InboundInvoiceTypes, RequiresAll = true)]
    [Authorize(FinancialPermissions.InboundInvoiceTypes.Create)]
    public Task<InboundInvoiceTypeDto> CreateAsync(InboundInvoiceTypeCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Invoicing.Default, AppFeatures.MasterData.Financial.Invoicing.InboundInvoiceTypes, RequiresAll = true)]
    [Authorize(FinancialPermissions.InboundInvoiceTypes.Update)]
    public Task<InboundInvoiceTypeDto> UpdateAsync(Guid id, InboundInvoiceTypeUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Invoicing.Default, AppFeatures.MasterData.Financial.Invoicing.InboundInvoiceTypes, RequiresAll = true)]
    [Authorize(FinancialPermissions.InboundInvoiceTypes.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Invoicing.Default, AppFeatures.MasterData.Financial.Invoicing.InboundInvoiceTypes, RequiresAll = true)]
    [Authorize(FinancialPermissions.InboundInvoiceTypes.Delete)]
    public Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => input.IncludeArchived ? q : q.Where(t => !t.IsArchived));

    protected override IQueryable<InboundInvoiceType> ApplyFilters(IQueryable<InboundInvoiceType> query, ArchivableListRequestDto request)
        => request.IncludeArchived ? query : query.Where(t => !t.IsArchived);

    protected override IQueryable<InboundInvoiceType> ApplySearch(IQueryable<InboundInvoiceType> query, string term)
        => query.Where(t => t.Code.ToLower().Contains(term) || t.Name.ToLower().Contains(term));

    protected override IOrderedQueryable<InboundInvoiceType> ApplySort(IQueryable<InboundInvoiceType> query, string? sortBy, bool descending)
        => (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "code" => Order(query, t => t.Code, descending),
            "name" => Order(query, t => t.Name, descending),
            "kind" => Order(query, t => t.Kind, descending),
            "isarchived" => Order(query, t => t.IsArchived, descending),
            _ => throw UnknownSortColumn(sortBy)
        };

    protected override IQueryable<InboundInvoiceType> ApplyLookupFilter(IQueryable<InboundInvoiceType> query) => query.Where(t => !t.IsArchived);

    protected override Task<List<InboundInvoiceTypeRowDto>> ToRowsAsync(List<InboundInvoiceType> entities)
        => Task.FromResult(entities.Select(t => new InboundInvoiceTypeRowDto
        {
            Id = t.Id,
            Code = t.Code,
            Name = t.Name,
            Kind = t.Kind,
            IsArchived = t.IsArchived,
            Description = t.Description
        }).ToList());

    protected override Task<InboundInvoiceTypeDto> ToDtoAsync(InboundInvoiceType t)
    {
        var dto = new InboundInvoiceTypeDto
        {
            Code = t.Code,
            Name = t.Name,
            Kind = t.Kind,
            CostLedgerAccount = t.CostLedgerAccount,
            IsArchived = t.IsArchived
        };
        MapAudit(t, dto);
        return Task.FromResult(dto);
    }

    protected override ReferenceDataLookupDto ToLookup(InboundInvoiceType t) => new() { Id = t.Id, Name = t.Name, Code = t.Code };

    protected override string DisplayNameOf(InboundInvoiceType t) => t.Name;

    protected override async Task<InboundInvoiceType> CreateEntityAsync(InboundInvoiceTypeCreateDto input)
    {
        var code = ReferenceDataAggregateRoot.NormalizeCode(input.Code);
        await EnsureUniqueAsync(t => t.Code == code, "code", code);
        var type = new InboundInvoiceType(GuidGenerator.Create(), CurrentTenant.Id, code, input.Name, input.Kind);
        Apply(type, input);
        return type;
    }

    protected override async Task ApplyUpdateAsync(InboundInvoiceType type, InboundInvoiceTypeUpdateDto input)
    {
        var code = ReferenceDataAggregateRoot.NormalizeCode(input.Code);
        if (code != type.Code)
        {
            await EnsureUniqueAsync(t => t.Code == code && t.Id != type.Id, "code", code);
            type.SetCode(code);
        }

        type.SetName(input.Name);
        type.SetKind(input.Kind);
        Apply(type, input);
    }

    private static void Apply(InboundInvoiceType type, InboundInvoiceTypeCreateDto input)
    {
        type.SetDescription(input.Description);
        type.SetCostLedgerAccount(input.CostLedgerAccount);
        type.IsArchived = input.IsArchived;
    }
}
