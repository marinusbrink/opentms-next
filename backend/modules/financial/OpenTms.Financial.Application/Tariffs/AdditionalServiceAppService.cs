using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Localization;
using OpenTms.Financial.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Localization;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Financial.Tariffs;

/* Design 52. Pickers (the lookup) need only the app feature; the list's own screen needs its node (PO 2026-10-14). */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class AdditionalServiceAppService
    : ReferenceDataCrudAppServiceBase<AdditionalService, AdditionalServiceRowDto, AdditionalServiceDto, AdditionalServiceCreateDto, AdditionalServiceUpdateDto, ArchivableListRequestDto>,
      IAdditionalServiceAppService
{
    private readonly VatPercentageReferences _vat;

    /* The VAT query the list's VAT-name sort joins with; same DbContext. Set by GetListAsync. */
    private IQueryable<VatPercentage>? _vatQuery;

    public AdditionalServiceAppService(IRepository<AdditionalService, Guid> repository, VatPercentageReferences vat)
        : base(repository)
    {
        _vat = vat;
        LocalizationResource = typeof(FinancialResource);
    }

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.AdditionalServices, RequiresAll = true)]
    [Authorize(FinancialPermissions.AdditionalServices.Default)]
    public async Task<GridResponse<AdditionalServiceRowDto>> GetListAsync(ArchivableListRequestDto input)
    {
        _vatQuery = await _vat.GetQueryableAsync();
        return await GetListInternalAsync(input);
    }

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.AdditionalServices, RequiresAll = true)]
    [Authorize(FinancialPermissions.AdditionalServices.Default)]
    public Task<AdditionalServiceDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(FinancialPermissions.AdditionalServices.Default)]
    public Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.AdditionalServices, RequiresAll = true)]
    [Authorize(FinancialPermissions.AdditionalServices.Create)]
    public Task<AdditionalServiceDto> CreateAsync(AdditionalServiceCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.AdditionalServices, RequiresAll = true)]
    [Authorize(FinancialPermissions.AdditionalServices.Update)]
    public Task<AdditionalServiceDto> UpdateAsync(Guid id, AdditionalServiceUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.AdditionalServices, RequiresAll = true)]
    [Authorize(FinancialPermissions.AdditionalServices.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.AdditionalServices, RequiresAll = true)]
    [Authorize(FinancialPermissions.AdditionalServices.Delete)]
    public Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => input.IncludeArchived ? q : q.Where(s => !s.IsArchived));

    protected override IQueryable<AdditionalService> ApplyFilters(IQueryable<AdditionalService> query, ArchivableListRequestDto request)
        => request.IncludeArchived ? query : query.Where(s => !s.IsArchived);

    protected override IQueryable<AdditionalService> ApplySearch(IQueryable<AdditionalService> query, string term)
        => query.Where(s => s.Code.ToLower().Contains(term) || s.Name.ToLower().Contains(term));

    protected override IOrderedQueryable<AdditionalService> ApplySort(IQueryable<AdditionalService> query, string? sortBy, bool descending)
    {
        switch (sortBy?.ToLowerInvariant())
        {
            case null or "" or "code":
                return Order(query, s => s.Code, descending);
            case "name":
                return Order(query, s => s.Name, descending);
            case "unit":
                return Order(query, s => s.Unit, descending);
            case "isarchived":
                return Order(query, s => s.IsArchived, descending);
            case "vatpercentagename":
            {
                var language = UiLanguage;
                var categories = _vatQuery ?? throw UnknownSortColumn(sortBy!);
                return OrderByJoined(query, categories, s => s.VatPercentageId, v => (Guid?)v.Id, j => LocalizedNameQuery.Text(j.Other!.Name, language) ?? LocalizedNameQuery.Text(j.Other!.Name, LocalizedName.DefaultCulture), descending);
            }
            default:
                throw UnknownSortColumn(sortBy);
        }
    }

    protected override IQueryable<AdditionalService> ApplyLookupFilter(IQueryable<AdditionalService> query) => query.Where(s => !s.IsArchived);

    protected override async Task<List<AdditionalServiceRowDto>> ToRowsAsync(List<AdditionalService> entities)
    {
        var names = await _vat.GetNamesAsync(entities.Select(s => s.VatPercentageId));
        return entities.Select(s => new AdditionalServiceRowDto
        {
            Id = s.Id,
            Code = s.Code,
            Name = s.Name,
            Unit = s.Unit,
            VatPercentageName = s.VatPercentageId != null && names.TryGetValue(s.VatPercentageId.Value, out var name) ? name : null,
            IsArchived = s.IsArchived,
            Description = s.Description
        }).ToList();
    }

    protected override async Task<AdditionalServiceDto> ToDtoAsync(AdditionalService s)
    {
        var names = await _vat.GetNamesAsync([s.VatPercentageId]);
        var dto = new AdditionalServiceDto
        {
            Code = s.Code,
            Name = s.Name,
            Unit = s.Unit,
            VatPercentageId = s.VatPercentageId,
            VatPercentageName = s.VatPercentageId != null && names.TryGetValue(s.VatPercentageId.Value, out var name) ? name : null,
            RevenueLedgerAccount = s.RevenueLedgerAccount,
            CostLedgerAccount = s.CostLedgerAccount,
            IsArchived = s.IsArchived
        };
        MapAudit(s, dto);
        return dto;
    }

    protected override ReferenceDataLookupDto ToLookup(AdditionalService s) => new() { Id = s.Id, Name = s.Name, Code = s.Code };

    protected override string DisplayNameOf(AdditionalService s) => s.Name;

    protected override async Task<AdditionalService> CreateEntityAsync(AdditionalServiceCreateDto input)
    {
        var code = ReferenceDataAggregateRoot.NormalizeCode(input.Code);
        await EnsureUniqueAsync(s => s.Code == code, "code", code);
        var service = new AdditionalService(GuidGenerator.Create(), CurrentTenant.Id, code, input.Name, input.Unit);
        await ApplyAsync(service, input);
        return service;
    }

    protected override async Task ApplyUpdateAsync(AdditionalService service, AdditionalServiceUpdateDto input)
    {
        var code = ReferenceDataAggregateRoot.NormalizeCode(input.Code);
        if (code != service.Code)
        {
            await EnsureUniqueAsync(s => s.Code == code && s.Id != service.Id, "code", code);
            service.SetCode(code);
        }

        service.SetName(input.Name);
        service.SetUnit(input.Unit);
        await ApplyAsync(service, input);
    }

    private async Task ApplyAsync(AdditionalService service, AdditionalServiceCreateDto input)
    {
        if (input.VatPercentageId != service.VatPercentageId)
        {
            await _vat.EnsureExistsAsync(input.VatPercentageId);
            service.VatPercentageId = input.VatPercentageId;
        }

        service.SetDescription(input.Description);
        service.SetLedgerAccounts(input.RevenueLedgerAccount, input.CostLedgerAccount);
        service.IsArchived = input.IsArchived;
    }
}
