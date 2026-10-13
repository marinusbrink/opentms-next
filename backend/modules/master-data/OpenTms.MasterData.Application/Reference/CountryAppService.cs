using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Features;
using OpenTms.MasterData.Permissions;
using OpenTms.MasterData.Regions;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Localization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.MasterData.Reference;

[RequiresFeature(MasterDataFeatures.FirstRun)]
public class CountryAppService
    : MasterDataCrudAppServiceBase<Country, CountryRowDto, CountryDto, CountryCreateDto, CountryUpdateDto, MasterDataListRequestDto>,
      ICountryAppService
{
    private readonly IRepository<Currency, Guid> _currencies;
    private readonly IRepository<GeographicalRegionPostalCodeRange, Guid> _ranges;

    public CountryAppService(
        IRepository<Country, Guid> repository,
        IRepository<Currency, Guid> currencies,
        IRepository<GeographicalRegionPostalCodeRange, Guid> ranges)
        : base(repository)
    {
        _currencies = currencies;
        _ranges = ranges;
    }

    [Authorize(MasterDataPermissions.Countries.Default)]
    public Task<GridResponse<CountryRowDto>> GetListAsync(MasterDataListRequestDto input) => GetListInternalAsync(input);

    [Authorize(MasterDataPermissions.Countries.Default)]
    public Task<CountryDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(MasterDataPermissions.Countries.Default)]
    public Task<List<MasterDataLookupDto>> GetLookupAsync(string? term = null, int take = MasterDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [Authorize(MasterDataPermissions.Countries.Create)]
    public Task<CountryDto> CreateAsync(CountryCreateDto input) => CreateInternalAsync(input);

    [Authorize(MasterDataPermissions.Countries.Update)]
    public Task<CountryDto> UpdateAsync(Guid id, CountryUpdateDto input) => UpdateInternalAsync(id, input);

    [Authorize(MasterDataPermissions.Countries.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [Authorize(MasterDataPermissions.Countries.Delete)]
    public Task<MasterDataBulkDeleteResultDto> BulkDeleteAsync(MasterDataBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => q);

    /// <summary>The one place that changes the tenant default: clears the old, marks the new, one transaction (the currency pattern).</summary>
    [Authorize(MasterDataPermissions.Countries.Update)]
    public async Task SetDefaultAsync(Guid id)
    {
        var country = await Repository.GetAsync(id);
        if (country.IsDefault)
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
        country.MarkDefault();
        await Repository.UpdateAsync(country, autoSave: true);
    }

    [Authorize(MasterDataPermissions.Countries.Default)]
    public async Task<CountryDto?> GetDefaultAsync()
    {
        var country = await Repository.FirstOrDefaultAsync(c => c.IsDefault);
        return country == null ? null : await ToDtoAsync(country);
    }

    protected override IQueryable<Country> ApplySearch(IQueryable<Country> query, string term)
    {
        var language = UiLanguage;
        return query.Where(c =>
            c.IsoAlpha2.ToLower().Contains(term) ||
            c.IsoAlpha3.ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(c.Name, language) ?? "").ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(c.Name, LocalizedName.DefaultCulture) ?? "").ToLower().Contains(term));
    }

    protected override IOrderedQueryable<Country> ApplySort(IQueryable<Country> query, string? sortBy, bool descending)
    {
        var language = UiLanguage;
        return (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "name" => Order(query, c => LocalizedNameQuery.Text(c.Name, language) ?? LocalizedNameQuery.Text(c.Name, LocalizedName.DefaultCulture), descending),
            "isoalpha2" => Order(query, c => c.IsoAlpha2, descending),
            "isoalpha3" => Order(query, c => c.IsoAlpha3, descending),
            "isonumeric" => Order(query, c => c.IsoNumeric, descending),
            "primarytimezoneid" => Order(query, c => c.PrimaryTimeZoneId, descending),
            "description" => Order(query, c => c.Description, descending),
            "defaultcurrencycode" => Order(query, c => c.DefaultCurrencyId, descending),
            "isdefault" => Order(query, c => c.IsDefault, descending),
            _ => throw UnknownSortColumn(sortBy)
        };
    }

    protected override async Task<List<CountryRowDto>> ToRowsAsync(List<Country> entities)
    {
        var codes = await CurrencyCodesAsync(entities.Select(c => c.DefaultCurrencyId));
        return entities.Select(c => new CountryRowDto
        {
            Id = c.Id,
            IsoAlpha2 = c.IsoAlpha2,
            IsoAlpha3 = c.IsoAlpha3,
            IsoNumeric = c.IsoNumeric,
            Name = Resolve(c.Name),
            Description = c.Description,
            DefaultCurrencyId = c.DefaultCurrencyId,
            DefaultCurrencyCode = c.DefaultCurrencyId == null ? null : codes.GetValueOrDefault(c.DefaultCurrencyId.Value),
            PrimaryTimeZoneId = c.PrimaryTimeZoneId,
            IsDefault = c.IsDefault
        }).ToList();
    }

    private async Task<Dictionary<Guid, string>> CurrencyCodesAsync(IEnumerable<Guid?> ids)
    {
        var wanted = ids.Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        var currencies = await _currencies.GetListAsync(c => wanted.Contains(c.Id));
        return currencies.ToDictionary(c => c.Id, c => c.IsoCode);
    }

    protected override async Task<CountryDto> ToDtoAsync(Country c)
    {
        var codes = await CurrencyCodesAsync([c.DefaultCurrencyId]);
        var dto = new CountryDto
        {
            IsoAlpha2 = c.IsoAlpha2,
            IsoAlpha3 = c.IsoAlpha3,
            IsoNumeric = c.IsoNumeric,
            Name = Resolve(c.Name),
            Names = ToNamesDto(c.Name),
            DefaultCurrencyId = c.DefaultCurrencyId,
            DefaultCurrencyCode = c.DefaultCurrencyId == null ? null : codes.GetValueOrDefault(c.DefaultCurrencyId.Value),
            PrimaryTimeZoneId = c.PrimaryTimeZoneId,
            IsDefault = c.IsDefault
        };
        MapAudit(c, dto);
        return dto;
    }

    protected override MasterDataLookupDto ToLookup(Country c) => new() { Id = c.Id, Name = Resolve(c.Name), Code = c.IsoAlpha2 };

    protected override string DisplayNameOf(Country c) => c.IsoAlpha2;

    protected override async Task<Country> CreateEntityAsync(CountryCreateDto input)
    {
        var alpha2 = MasterDataCodes.NormalizeIsoAlpha2(input.IsoAlpha2);
        var alpha3 = MasterDataCodes.NormalizeIsoAlpha3(input.IsoAlpha3, "isoAlpha3");
        await EnsureUniqueAsync(c => c.IsoAlpha2 == alpha2, "isoAlpha2", alpha2);
        await EnsureUniqueAsync(c => c.IsoAlpha3 == alpha3, "isoAlpha3", alpha3);

        var country = new Country(GuidGenerator.Create(), CurrentTenant.Id, alpha2, alpha3, ToLocalizedName(input.Names));
        await ApplyAsync(country, input);
        return country;
    }

    protected override async Task ApplyUpdateAsync(Country country, CountryUpdateDto input)
    {
        var alpha2 = MasterDataCodes.NormalizeIsoAlpha2(input.IsoAlpha2);
        var alpha3 = MasterDataCodes.NormalizeIsoAlpha3(input.IsoAlpha3, "isoAlpha3");
        if (alpha2 != country.IsoAlpha2)
        {
            await EnsureUniqueAsync(c => c.IsoAlpha2 == alpha2 && c.Id != country.Id, "isoAlpha2", alpha2);
        }

        if (alpha3 != country.IsoAlpha3)
        {
            await EnsureUniqueAsync(c => c.IsoAlpha3 == alpha3 && c.Id != country.Id, "isoAlpha3", alpha3);
        }

        country.SetIsoCodes(alpha2, alpha3);
        country.SetName(ToLocalizedName(input.Names));
        await ApplyAsync(country, input);
    }

    private async Task ApplyAsync(Country country, CountryCreateDto input)
    {
        if (input.DefaultCurrencyId != null && !await _currencies.AnyAsync(c => c.Id == input.DefaultCurrencyId.Value))
        {
            throw new BusinessException(MasterDataErrorCodes.ReferenceNotFound).WithData("field", "defaultCurrencyId");
        }

        country.SetIsoNumeric(input.IsoNumeric);
        country.SetDefaultCurrency(input.DefaultCurrencyId);
        country.SetPrimaryTimeZone(input.PrimaryTimeZoneId);
        country.SetDescription(input.Description);
    }

    protected override async Task<string?> GetDeleteBlockerAsync(Country country)
    {
        if (country.IsDefault)
        {
            return L["MasterData:DefaultCountryCannotBeDeleted"];
        }

        if (await _ranges.AnyAsync(r => r.CountryId == country.Id))
        {
            return L["MasterData:InUseByGeographicalRegions"];
        }

        return null;
    }
}
