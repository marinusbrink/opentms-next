using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData;
using OpenTms.MasterData.Reference;
using OpenTms.Platform.Localization;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Xunit;

namespace OpenTms.PostgreSql.MasterData;

/// <summary>
/// The LocalizedName SQL translation on PostgreSQL (design 41, "LocalizedName": sort and search on
/// jsonb_extract_path_text) through the real currency and country services on the seeded tenant.
/// Risk class: High (LocalizedName) inside the Platform change the design marks critical.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class LocalizedNameSqlTests : MasterDataPostgreSqlTestBase
{
    private readonly ICurrencyAppService _currencies;
    private readonly ICountryAppService _countries;
    private readonly IRepository<Currency, Guid> _currencyRepository;
    private readonly IGuidGenerator _guids;

    public LocalizedNameSqlTests()
    {
        _currencies = GetRequiredService<ICurrencyAppService>();
        _countries = GetRequiredService<ICountryAppService>();
        _currencyRepository = GetRequiredService<IRepository<Currency, Guid>>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    [Fact]
    public async Task The_currency_list_sorts_on_the_resolved_name_in_nl_and_in_en()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            using (UiCulture("nl-NL"))
            {
                var ascending = (await _currencies.GetListAsync(List<CurrencyListRequestDto>(end: 200))).Rows;
                ascending.Count.ShouldBe(178);
                /* "ADB-rekeneenheid" (XUA) sorts first under the database collation; the names are the Dutch ones. */
                ascending.First().IsoCode.ShouldBe("XUA");
                ascending.First().Name.ShouldBe("ADB-rekeneenheid");
                ascending.Select(r => r.Name).ShouldContain("Euro");
                ascending.Select(r => r.Name).ShouldContain("Amerikaanse dollar");

                var descending = (await _currencies.GetListAsync(List<CurrencyListRequestDto>(end: 200, sortBy: "name", sortDir: "desc"))).Rows;
                descending.Select(r => r.IsoCode).ShouldBe(ascending.Select(r => r.IsoCode).Reverse());
            }

            using (UiCulture("en-GB"))
            {
                var ascending = (await _currencies.GetListAsync(List<CurrencyListRequestDto>(end: 200))).Rows;
                ascending.Count.ShouldBe(178);
                ascending.First().IsoCode.ShouldBe("XUA");
                ascending.First().Name.ShouldBe("ADB Unit of Account");
                ascending.Select(r => r.Name).ShouldContain("US Dollar");
                ascending.Select(r => r.Name).ShouldNotContain("Amerikaanse dollar");

                /* The order differs between the languages because the names do. */
                var usd = ascending.FindIndex(r => r.IsoCode == "USD");
                var aud = ascending.FindIndex(r => r.IsoCode == "AUD");
                aud.ShouldBeLessThan(usd);
            }

            using (UiCulture("nl"))
            {
                var rows = (await _currencies.GetListAsync(List<CurrencyListRequestDto>(end: 200))).Rows;
                rows.FindIndex(r => r.IsoCode == "USD").ShouldBeLessThan(rows.FindIndex(r => r.IsoCode == "AUD"));
            }
        });
    }

    [Fact]
    public async Task Search_finds_the_name_in_the_ui_language_and_in_the_default_culture()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            using (UiCulture("nl"))
            {
                /* "Nederland" and "Caribisch Nederland" (BQ) both match the contains search. */
                var dutch = await _countries.GetListAsync(List<MasterDataListRequestDto>(search: "Nederland"));
                dutch.FilteredCount.ShouldBe(2);
                dutch.TotalCount.ShouldBe(249);
                dutch.Rows.Select(r => r.IsoAlpha2).ShouldBe(["BQ", "NL"]);
                dutch.Rows.Single(r => r.IsoAlpha2 == "NL").Name.ShouldBe("Nederland");

                (await _countries.GetListAsync(List<MasterDataListRequestDto>(search: "netherlands"))).Rows.Select(r => r.IsoAlpha2).ShouldBe(["BQ", "NL"]);
                (await _countries.GetListAsync(List<MasterDataListRequestDto>(search: "duitsland"))).Rows.ShouldHaveSingleItem().IsoAlpha2.ShouldBe("DE");
                (await _countries.GetLookupAsync("nederl")).Select(l => l.Code).ShouldBe(["BQ", "NL"]);
                (await _currencies.GetListAsync(List<CurrencyListRequestDto>(search: "amerikaanse"))).Rows.Select(r => r.IsoCode).ShouldBe(["USD", "USN"]);
            }

            using (UiCulture("en"))
            {
                var english = await _countries.GetListAsync(List<MasterDataListRequestDto>(search: "Netherlands"));
                english.Rows.Select(r => r.IsoAlpha2).ShouldBe(["BQ", "NL"]);
                english.Rows.Single(r => r.IsoAlpha2 == "NL").Name.ShouldBe("Netherlands");

                /* Search covers the UI language and the default culture only: Dutch is not searched from an English session. */
                (await _countries.GetListAsync(List<MasterDataListRequestDto>(search: "Nederland"))).Rows.ShouldBeEmpty();
                (await _countries.GetListAsync(List<MasterDataListRequestDto>(search: "nl"))).Rows.ShouldContain(r => r.IsoAlpha2 == "NL");
            }
        });
    }

    [Fact]
    public async Task The_country_list_and_lookup_order_by_the_resolved_name()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            using (UiCulture("nl"))
            {
                var rows = (await _countries.GetListAsync(List<MasterDataListRequestDto>(end: 200))).Rows;
                rows.First().Name.ShouldBe("Afghanistan");
                rows.Select(r => r.Name).ShouldContain("Duitsland");
                var lookup = await _countries.GetLookupAsync(take: 500);
                lookup.Count.ShouldBe(249);
                lookup.Select(l => l.Name).Take(200).ShouldBe(rows.Select(r => r.Name));
            }

            using (UiCulture("en"))
            {
                var lookup = await _currencies.GetLookupAsync();
                lookup.ShouldHaveSingleItem().Code.ShouldBe("EUR");
            }
        });
    }

    /// <summary>
    /// Design 41, "LocalizedName": the list sorts on Text(name, uiCulture) ?? Text(name, DefaultCulture).
    /// A tenant-created row with an English text only must sort by that text when the UI is Dutch
    /// ("AAA coin" first), not as a NULL key (which PostgreSQL puts last).
    /// </summary>
    [Fact]
    public async Task Sorting_falls_back_to_the_default_culture_text_for_a_row_without_a_text_in_the_ui_language()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var englishOnly = new Currency(_guids.Create(), TenantA, "QQE", LocalizedName.Create("en", "AAA coin"), true);
            await _currencyRepository.InsertAsync(englishOnly, autoSave: true);

            using (UiCulture("nl"))
            {
                var rows = (await _currencies.GetListAsync(List<CurrencyListRequestDto>(end: 200))).Rows;
                rows.Count.ShouldBe(179);
                rows.Single(r => r.IsoCode == "QQE").Name.ShouldBe("AAA coin");
                rows.First().IsoCode.ShouldBe("QQE", $"the English-only row sorted at position {rows.FindIndex(r => r.IsoCode == "QQE")} of {rows.Count}");
            }
        });
    }
}
