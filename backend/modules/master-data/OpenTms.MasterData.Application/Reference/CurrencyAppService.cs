using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Features;
using OpenTms.MasterData.Permissions;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Localization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.MasterData.Reference;

[RequiresFeature(MasterDataFeatures.FirstRun)]
public class CurrencyAppService
    : MasterDataCrudAppServiceBase<Currency, CurrencyRowDto, CurrencyDto, CurrencyCreateDto, CurrencyUpdateDto, CurrencyListRequestDto>,
      ICurrencyAppService
{
    private readonly IRepository<Country, Guid> _countries;

    public CurrencyAppService(IRepository<Currency, Guid> repository, IRepository<Country, Guid> countries)
        : base(repository)
    {
        _countries = countries;
    }

    [Authorize(MasterDataPermissions.Currencies.Default)]
    public Task<GridResponse<CurrencyRowDto>> GetListAsync(CurrencyListRequestDto input) => GetListInternalAsync(input);

    [Authorize(MasterDataPermissions.Currencies.Default)]
    public Task<CurrencyDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(MasterDataPermissions.Currencies.Default)]
    public Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [Authorize(MasterDataPermissions.Currencies.Create)]
    public Task<CurrencyDto> CreateAsync(CurrencyCreateDto input) => CreateInternalAsync(input);

    [Authorize(MasterDataPermissions.Currencies.Update)]
    public Task<CurrencyDto> UpdateAsync(Guid id, CurrencyUpdateDto input) => UpdateInternalAsync(id, input);

    [Authorize(MasterDataPermissions.Currencies.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [Authorize(MasterDataPermissions.Currencies.Delete)]
    public Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(CurrencyBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => ApplyActiveFilter(q, input.IsActive));

    /// <summary>The one place that changes the tenant default (assessment §3.8): clears the old, marks the new, one transaction.</summary>
    [Authorize(MasterDataPermissions.Currencies.Update)]
    public async Task SetDefaultAsync(Guid id)
    {
        var currency = await Repository.GetAsync(id);
        if (currency.IsDefault)
        {
            return;
        }

        foreach (var previous in await Repository.GetListAsync(c => c.IsDefault))
        {
            previous.ClearDefault();
            await Repository.UpdateAsync(previous);
        }

        /* The partial unique index checks every statement: the old default must be cleared in the
         * database before the new one is marked, still inside the same transaction. */
        await FlushAsync();

        currency.MarkDefault();
        await Repository.UpdateAsync(currency, autoSave: true);
    }

    protected override IQueryable<Currency> ApplyFilters(IQueryable<Currency> query, CurrencyListRequestDto request)
        => ApplyActiveFilter(query, request.IsActive);

    private static IQueryable<Currency> ApplyActiveFilter(IQueryable<Currency> query, bool? isActive)
        => isActive == null ? query : query.Where(c => c.IsActive == isActive.Value);

    protected override IQueryable<Currency> ApplySearch(IQueryable<Currency> query, string term)
    {
        var language = UiLanguage;
        return query.Where(c =>
            c.IsoCode.ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(c.Name, language) ?? "").ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(c.Name, LocalizedName.DefaultCulture) ?? "").ToLower().Contains(term));
    }

    protected override IOrderedQueryable<Currency> ApplySort(IQueryable<Currency> query, string? sortBy, bool descending)
    {
        var language = UiLanguage;
        return (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "name" => Order(query, c => LocalizedNameQuery.Text(c.Name, language) ?? LocalizedNameQuery.Text(c.Name, LocalizedName.DefaultCulture), descending),
            "isocode" => Order(query, c => c.IsoCode, descending),
            "isonumeric" => Order(query, c => c.IsoNumeric, descending),
            "minorunits" => Order(query, c => c.MinorUnits, descending),
            "symbol" => Order(query, c => c.Symbol, descending),
            "isactive" => Order(query, c => c.IsActive, descending),
            "isdefault" => Order(query, c => c.IsDefault, descending),
            "description" => Order(query, c => c.Description, descending),
            _ => throw UnknownSortColumn(sortBy)
        };
    }

    protected override IQueryable<Currency> ApplyLookupFilter(IQueryable<Currency> query) => query.Where(c => c.IsActive);

    protected override Task<List<CurrencyRowDto>> ToRowsAsync(List<Currency> entities)
        => Task.FromResult(entities.Select(c => new CurrencyRowDto
        {
            Id = c.Id,
            IsoCode = c.IsoCode,
            IsoNumeric = c.IsoNumeric,
            MinorUnits = c.MinorUnits,
            Symbol = c.Symbol,
            Name = Resolve(c.Name),
            Description = c.Description,
            IsActive = c.IsActive,
            IsDefault = c.IsDefault
        }).ToList());

    protected override Task<CurrencyDto> ToDtoAsync(Currency c)
    {
        var dto = new CurrencyDto
        {
            IsoCode = c.IsoCode,
            IsoNumeric = c.IsoNumeric,
            MinorUnits = c.MinorUnits,
            Symbol = c.Symbol,
            Name = Resolve(c.Name),
            Names = ToNamesDto(c.Name),
            IsActive = c.IsActive,
            IsDefault = c.IsDefault
        };
        MapAudit(c, dto);
        return Task.FromResult(dto);
    }

    protected override MasterDataLookupDto ToLookup(Currency c) => new() { Id = c.Id, Name = Resolve(c.Name), Code = c.IsoCode };

    protected override string DisplayNameOf(Currency c) => c.IsoCode;

    protected override async Task<Currency> CreateEntityAsync(CurrencyCreateDto input)
    {
        var code = MasterDataCodes.NormalizeIsoAlpha3(input.IsoCode, "isoCode");
        await EnsureUniqueAsync(c => c.IsoCode == code, "isoCode", code);

        var currency = new Currency(GuidGenerator.Create(), CurrentTenant.Id, code, ToLocalizedName(input.Names), input.IsActive);
        Apply(currency, input);
        return currency;
    }

    protected override async Task ApplyUpdateAsync(Currency currency, CurrencyUpdateDto input)
    {
        var code = MasterDataCodes.NormalizeIsoAlpha3(input.IsoCode, "isoCode");
        if (code != currency.IsoCode)
        {
            await EnsureUniqueAsync(c => c.IsoCode == code && c.Id != currency.Id, "isoCode", code);
            currency.SetIsoCode(code);
        }

        currency.SetName(ToLocalizedName(input.Names));
        currency.SetActive(input.IsActive);
        Apply(currency, input);
    }

    private static void Apply(Currency currency, CurrencyCreateDto input)
    {
        currency.SetIsoNumeric(input.IsoNumeric);
        currency.SetMinorUnits(input.MinorUnits);
        currency.SetSymbol(input.Symbol);
        currency.SetDescription(input.Description);
    }

    protected override async Task<string?> GetDeleteBlockerAsync(Currency currency)
    {
        if (currency.IsDefault)
        {
            return L[MasterDataErrorCodes.DefaultCannotBeDeleted];
        }

        if (await _countries.AnyAsync(c => c.DefaultCurrencyId == currency.Id))
        {
            return L["MasterData:InUseByCountries"];
        }

        return null;
    }
}
