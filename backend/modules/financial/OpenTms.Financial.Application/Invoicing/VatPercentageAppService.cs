using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Financial.Localization;
using OpenTms.Financial.Permissions;
using OpenTms.Financial.Tariffs;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Localization;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Financial.Invoicing;

/* Design 52. Pickers (the lookup) need only the app feature; the list's own screen needs its node (PO 2026-10-14). */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class VatPercentageAppService
    : ReferenceDataCrudAppServiceBase<VatPercentage, VatPercentageRowDto, VatPercentageDto, VatPercentageCreateDto, VatPercentageUpdateDto, ArchivableListRequestDto>,
      IVatPercentageAppService
{
    private readonly IRepository<VatPercentageRate, Guid> _rates;
    private readonly IRepository<AdditionalService, Guid> _services;
    private readonly IRepository<DiscountOrSurcharge, Guid> _discountsOrSurcharges;
    private readonly MasterDataReferences _masterData;
    private readonly FinancialToday _today;

    public VatPercentageAppService(
        IRepository<VatPercentage, Guid> repository,
        IRepository<VatPercentageRate, Guid> rates,
        IRepository<AdditionalService, Guid> services,
        IRepository<DiscountOrSurcharge, Guid> discountsOrSurcharges,
        MasterDataReferences masterData,
        FinancialToday today)
        : base(repository)
    {
        _rates = rates;
        _services = services;
        _discountsOrSurcharges = discountsOrSurcharges;
        _masterData = masterData;
        _today = today;
        LocalizationResource = typeof(FinancialResource);
    }

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Invoicing.Default, AppFeatures.MasterData.Financial.Invoicing.VatPercentages, RequiresAll = true)]
    [Authorize(FinancialPermissions.VatPercentages.Default)]
    public Task<GridResponse<VatPercentageRowDto>> GetListAsync(ArchivableListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Invoicing.Default, AppFeatures.MasterData.Financial.Invoicing.VatPercentages, RequiresAll = true)]
    [Authorize(FinancialPermissions.VatPercentages.Default)]
    public Task<VatPercentageDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(FinancialPermissions.VatPercentages.Default)]
    public Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Invoicing.Default, AppFeatures.MasterData.Financial.Invoicing.VatPercentages, RequiresAll = true)]
    [Authorize(FinancialPermissions.VatPercentages.Create)]
    public Task<VatPercentageDto> CreateAsync(VatPercentageCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Invoicing.Default, AppFeatures.MasterData.Financial.Invoicing.VatPercentages, RequiresAll = true)]
    [Authorize(FinancialPermissions.VatPercentages.Update)]
    public Task<VatPercentageDto> UpdateAsync(Guid id, VatPercentageUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Invoicing.Default, AppFeatures.MasterData.Financial.Invoicing.VatPercentages, RequiresAll = true)]
    [Authorize(FinancialPermissions.VatPercentages.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        EnsureNotDefault(await Repository.GetAsync(id), "delete");
        await DeleteInternalAsync(id);
    }

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Invoicing.Default, AppFeatures.MasterData.Financial.Invoicing.VatPercentages, RequiresAll = true)]
    [Authorize(FinancialPermissions.VatPercentages.Delete)]
    public Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => input.IncludeArchived ? q : q.Where(v => !v.IsArchived));

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Invoicing.Default, AppFeatures.MasterData.Financial.Invoicing.VatPercentages, RequiresAll = true)]
    [Authorize(FinancialPermissions.VatPercentages.Update)]
    public Task SetDefaultAsync(Guid id) => SetDefaultInternalAsync(id);

    protected override IQueryable<VatPercentage> ApplyFilters(IQueryable<VatPercentage> query, ArchivableListRequestDto request)
        => request.IncludeArchived ? query : query.Where(v => !v.IsArchived);

    protected override IQueryable<VatPercentage> ApplySearch(IQueryable<VatPercentage> query, string term)
    {
        var language = UiLanguage;
        return query.Where(v =>
            v.Code.ToLower().Contains(term) ||
            v.CountryCode.ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(v.Name, language) ?? "").ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(v.Name, LocalizedName.DefaultCulture) ?? "").ToLower().Contains(term));
    }

    protected override IOrderedQueryable<VatPercentage> ApplySort(IQueryable<VatPercentage> query, string? sortBy, bool descending)
    {
        var language = UiLanguage;
        return (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "code" => Order(query, v => v.Code, descending),
            "name" => Order(query, v => LocalizedNameQuery.Text(v.Name, language) ?? LocalizedNameQuery.Text(v.Name, LocalizedName.DefaultCulture), descending),
            "kind" => Order(query, v => v.Kind, descending),
            "countrycode" => Order(query, v => v.CountryCode, descending),
            "isdefault" => Order(query, v => v.IsDefault, descending),
            "isarchived" => Order(query, v => v.IsArchived, descending),
            _ => throw UnknownSortColumn(sortBy)
        };
    }

    protected override IQueryable<VatPercentage> ApplyLookupFilter(IQueryable<VatPercentage> query) => query.Where(v => !v.IsArchived);

    protected override async Task<List<VatPercentageRowDto>> ToRowsAsync(List<VatPercentage> entities)
    {
        /* The percentage valid today for the whole page in one query (Cost & SLO: one grouped query per page, not per row). */
        var today = _today.Get();
        var ids = entities.Select(v => v.Id).ToList();
        var rates = ids.Count == 0
            ? []
            : await AsyncExecuter.ToListAsync((await _rates.GetQueryableAsync()).Where(r => ids.Contains(r.VatPercentageId) && r.ValidFrom <= today));
        var current = rates.GroupBy(r => r.VatPercentageId).ToDictionary(g => g.Key, g => g.MaxBy(r => r.ValidFrom)!.Percentage);

        return entities.Select(v => new VatPercentageRowDto
        {
            Id = v.Id,
            Code = v.Code,
            Name = Resolve(v.Name),
            Kind = v.Kind,
            CountryCode = v.CountryCode,
            CurrentPercentage = current.TryGetValue(v.Id, out var percentage) ? percentage : null,
            IsDefault = v.IsDefault,
            IsArchived = v.IsArchived,
            Description = v.Description
        }).ToList();
    }

    protected override Task<VatPercentageDto> ToDtoAsync(VatPercentage v)
    {
        var today = _today.Get();
        var dto = new VatPercentageDto
        {
            Code = v.Code,
            Name = Resolve(v.Name),
            Names = ToNamesDto(v.Name),
            Kind = v.Kind,
            CountryCode = v.CountryCode,
            SalesLedgerAccount = v.SalesLedgerAccount,
            PurchaseLedgerAccount = v.PurchaseLedgerAccount,
            CurrentPercentage = v.RateOn(today)?.Percentage,
            Rates = v.Rates.OrderBy(r => r.ValidFrom)
                .Select(r => new VatPercentageRateDto { ValidFrom = r.ValidFrom, Percentage = r.Percentage, IsFixed = r.ValidFrom <= today })
                .ToList(),
            IsDefault = v.IsDefault,
            IsArchived = v.IsArchived
        };
        MapAudit(v, dto);
        return Task.FromResult(dto);
    }

    protected override ReferenceDataLookupDto ToLookup(VatPercentage v) => new() { Id = v.Id, Name = Resolve(v.Name), Code = v.Code };

    protected override string DisplayNameOf(VatPercentage v) => Resolve(v.Name);

    protected override async Task<VatPercentage> CreateEntityAsync(VatPercentageCreateDto input)
    {
        var code = ReferenceDataAggregateRoot.NormalizeCode(input.Code);
        await EnsureUniqueAsync(v => v.Code == code, "code", code);
        var category = new VatPercentage(GuidGenerator.Create(), CurrentTenant.Id, code, ToLocalizedName(input.Names), input.Kind, input.CountryCode);
        await _masterData.EnsureCountryExistsAsync(category.CountryCode);
        Apply(category, input);

        /* Nothing has taken effect before the category exists: rates of any date are accepted on create. */
        category.SetRates(RatesOf(input), fixedOn: null, GuidGenerator.Create);

        /* A tenant's first category becomes the default (assessment §3.8). */
        if (!await Repository.AnyAsync())
        {
            category.SetDefault(true);
        }

        category.SetArchived(input.IsArchived);
        return category;
    }

    protected override async Task ApplyUpdateAsync(VatPercentage category, VatPercentageUpdateDto input)
    {
        var code = ReferenceDataAggregateRoot.NormalizeCode(input.Code);
        if (code != category.Code)
        {
            await EnsureUniqueAsync(v => v.Code == code && v.Id != category.Id, "code", code);
            category.SetCode(code);
        }

        var countryCode = category.CountryCode;
        category.SetCountryCode(input.CountryCode);
        if (category.CountryCode != countryCode)
        {
            await _masterData.EnsureCountryExistsAsync(category.CountryCode);
        }

        category.SetName(ToLocalizedName(input.Names));
        category.SetKind(input.Kind);
        Apply(category, input);
        category.SetRates(RatesOf(input), fixedOn: _today.Get(), GuidGenerator.Create);
        category.SetArchived(input.IsArchived);
    }

    private static void Apply(VatPercentage category, VatPercentageCreateDto input)
    {
        category.SetDescription(input.Description);
        category.SetLedgerAccounts(input.SalesLedgerAccount, input.PurchaseLedgerAccount);
    }

    private static IEnumerable<(DateOnly ValidFrom, decimal Percentage)> RatesOf(VatPercentageCreateDto input)
        => (input.Rates ?? []).Select(r => (r.ValidFrom, r.Percentage));

    protected override async Task<string?> GetDeleteBlockerAsync(VatPercentage category)
    {
        if (category.IsDefault)
        {
            return L["Financial:DefaultVatPercentageCannotBeDeleted"].Value;
        }

        if (await _services.AnyAsync(s => s.VatPercentageId == category.Id) || await _discountsOrSurcharges.AnyAsync(d => d.VatPercentageId == category.Id))
        {
            return L["Financial:VatPercentageInUse"].Value;
        }

        return null;
    }
}
