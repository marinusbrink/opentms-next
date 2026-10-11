using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Regions;
using OpenTms.MasterData.Transport;
using OpenTms.Platform.Localization;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Xunit;
using Xunit.Abstractions;

namespace OpenTms.PostgreSql.MasterData;

/// <summary>
/// VOLUME TEST (design 41, "Expected volumes" × 5; performance-budgets interactive reads p95 &lt; 300 ms):
/// 5,000 products, 1,000 regions with 50,000 postal-code ranges in one country and 900 currencies in
/// tenant A, then the paged list, the lookup and the match are timed. The assertion carries a CI
/// margin (1,500 ms; design assumption 14: a tripwire, the budget itself is measured by the nightly
/// k6 run). Each query is run once untimed to warm EF Core's query cache, then timed.
/// Risk class: High. The data lives in one rolled-back transaction, so nothing stays behind.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class MasterDataVolumeTests : MasterDataPostgreSqlTestBase
{
    private const int Products = 5_000;
    private const int Regions = 1_000;
    private const int RangesPerRegion = 50;
    private const int Currencies = 900;
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan Tripwire = TimeSpan.FromMilliseconds(1_500);

    private readonly ITestOutputHelper _output;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<GeographicalRegion, Guid> _regionRepository;
    private readonly IRepository<Currency, Guid> _currencyRepository;
    private readonly IRepository<Country, Guid> _countryRepository;
    private readonly IProductAppService _products;
    private readonly IGeographicalRegionAppService _regions;
    private readonly ICurrencyAppService _currencies;
    private readonly IGuidGenerator _guids;

    public MasterDataVolumeTests(ITestOutputHelper output)
    {
        _output = output;
        _productRepository = GetRequiredService<IRepository<Product, Guid>>();
        _regionRepository = GetRequiredService<IRepository<GeographicalRegion, Guid>>();
        _currencyRepository = GetRequiredService<IRepository<Currency, Guid>>();
        _countryRepository = GetRequiredService<IRepository<Country, Guid>>();
        _products = GetRequiredService<IProductAppService>();
        _regions = GetRequiredService<IGeographicalRegionAppService>();
        _currencies = GetRequiredService<ICurrencyAppService>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    [Fact]
    public async Task Paged_list_lookup_and_match_stay_within_the_interactive_read_budget_at_five_times_the_expected_volume()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var nl = await _countryRepository.GetAsync(c => c.IsoAlpha2 == "NL");
            await InsertVolumeAsync(nl.Id);

            using var culture = UiCulture("nl");
            var measurements = new List<(string Name, TimeSpan Elapsed)>
            {
                await MeasureAsync("product list page 1 (100 rows, sorted by name)", () => _products.GetListAsync(List<ArchivableListRequestDto>(end: 100))),
                await MeasureAsync("product list page 25 with search", () => _products.GetListAsync(List<ArchivableListRequestDto>(start: 2400, end: 2500, search: "product 1"))),
                await MeasureAsync("product list sorted by weight desc", () => _products.GetListAsync(List<ArchivableListRequestDto>(end: 100, sortBy: "defaultWeightKg", sortDir: "desc"))),
                await MeasureAsync("product lookup (500, term)", () => _products.GetLookupAsync("product 12")),
                await MeasureAsync("product lookup (500, no term)", () => _products.GetLookupAsync()),
                await MeasureAsync("region list page 1 (100 rows with range counts, cold)", () => _regions.GetListAsync(List<GeographicalRegionListRequestDto>(end: 100))),
                await MeasureAsync("region list page 1 (100 rows with range counts, warm)", () => _regions.GetListAsync(List<GeographicalRegionListRequestDto>(end: 100))),
                await MeasureAsync("region list filtered on country (cold: first compile of the query shape)", () => _regions.GetListAsync(new GeographicalRegionListRequestDto { CountryId = nl.Id, StartRow = 0, EndRow = 100 })),
                await MeasureAsync("region list filtered on country (warm)", () => _regions.GetListAsync(new GeographicalRegionListRequestDto { CountryId = nl.Id, StartRow = 0, EndRow = 100 })),
                await MeasureAsync("region lookup", () => _regions.GetLookupAsync("region 5")),
                await MeasureAsync("region match over 50,000 ranges", () => _regions.GetMatchesAsync(nl.Id, "1234 AB")),
                await MeasureAsync("region match without hit", () => _regions.GetMatchesAsync(nl.Id, "SW1A 1AA")),
                await MeasureAsync("currency list sorted by resolved name (nl)", () => _currencies.GetListAsync(List<CurrencyListRequestDto>(end: 100))),
                await MeasureAsync("currency list searched on name", () => _currencies.GetListAsync(List<CurrencyListRequestDto>(end: 100, search: "volume"))),
                await MeasureAsync("currency lookup (active only)", () => _currencies.GetLookupAsync())
            };

            foreach (var (name, elapsed) in measurements)
            {
                _output.WriteLine($"{name}: {elapsed.TotalMilliseconds:F0} ms (budget {Budget.TotalMilliseconds} ms, tripwire {Tripwire.TotalMilliseconds} ms)");
            }

            var overTripwire = measurements.Where(m => m.Elapsed > Tripwire).Select(m => $"{m.Name}: {m.Elapsed.TotalMilliseconds:F0} ms").ToList();
            overTripwire.ShouldBeEmpty($"queries over the {Tripwire.TotalMilliseconds} ms tripwire (budget {Budget.TotalMilliseconds} ms)");

            /* The results are the right ones, not just fast. */
            (await _products.GetListAsync(List<ArchivableListRequestDto>(end: 100))).TotalCount.ShouldBe(Products);
            (await _products.GetLookupAsync()).Count.ShouldBe(MasterDataConsts.MaxLookupTake);
            var page = await _regions.GetListAsync(List<GeographicalRegionListRequestDto>(end: 100));
            page.TotalCount.ShouldBe(Regions);
            page.Rows.ShouldAllBe(r => r.RangeCount == RangesPerRegion);
            var matches = await _regions.GetMatchesAsync(nl.Id, "1234 AB");
            matches.ShouldHaveSingleItem().Name.ShouldBe("Volume region 0123");
            (await _regions.GetMatchesAsync(nl.Id, "SW1A 1AA")).ShouldBeEmpty();
            (await _currencies.GetListAsync(List<CurrencyListRequestDto>(end: 100))).TotalCount.ShouldBe(Currencies);
        });
    }

    private async Task InsertVolumeAsync(Guid countryId)
    {
        var insert = Stopwatch.StartNew();

        var products = new List<Product>(Products);
        for (var i = 0; i < Products; i++)
        {
            var product = new Product(_guids.Create(), TenantA, $"Volume product {i:D5}");
            product.DefaultWeightKg = i % 997;
            product.SetDescription(i % 7 == 0 ? "heavy" : null);
            products.Add(product);
        }

        await _productRepository.InsertManyAsync(products, autoSave: true);

        /* 1,000 regions × 50 ranges, all in one country (the worst case for the match). Region r owns the
         * numeric ranges [r*10 + k*10,000, r*10 + k*10,000 + 9] for k = 0..49: disjoint, and "1234 AB"
         * (1234) falls in [1230, 1239] of region 123 only. */
        var regions = new List<GeographicalRegion>(Regions);
        for (var r = 0; r < Regions; r++)
        {
            var region = new GeographicalRegion(_guids.Create(), TenantA, $"Volume region {r:D4}");
            var ranges = new List<(Guid? Id, Guid CountryId, int? FromNumeric, int? TillNumeric, string? Prefix)>(RangesPerRegion);
            for (var k = 0; k < RangesPerRegion; k++)
            {
                var from = r * 10 + k * 10_000;
                ranges.Add((null, countryId, from, from + 9, null));
            }

            region.SetPostalCodeRanges(ranges, () => _guids.Create());
            regions.Add(region);
        }

        await _regionRepository.InsertManyAsync(regions, autoSave: true);

        var existing = (await _currencyRepository.GetListAsync()).Select(c => c.IsoCode).ToHashSet(StringComparer.Ordinal);
        var currencies = new List<Currency>();
        for (var a = 'A'; a <= 'Z' && existing.Count + currencies.Count < Currencies; a++)
        {
            for (var b = 'A'; b <= 'Z' && existing.Count + currencies.Count < Currencies; b++)
            {
                for (var c = 'A'; c <= 'Z' && existing.Count + currencies.Count < Currencies; c++)
                {
                    var code = $"{a}{b}{c}";
                    if (existing.Contains(code))
                    {
                        continue;
                    }

                    var currency = new Currency(_guids.Create(), TenantA, code, LocalizedName.Create(new Dictionary<string, string?>
                    {
                        ["en"] = $"Volume currency {code}",
                        ["nl"] = $"Volumevaluta {code}"
                    }), isActive: currencies.Count % 10 == 0);
                    currency.SetMinorUnits(2);
                    currencies.Add(currency);
                }
            }
        }

        await _currencyRepository.InsertManyAsync(currencies, autoSave: true);

        _output.WriteLine($"inserted {Products} products, {Regions} regions with {Regions * RangesPerRegion} ranges and {currencies.Count} currencies in {insert.Elapsed.TotalSeconds:F1} s");
    }

    private static async Task<(string Name, TimeSpan Elapsed)> MeasureAsync<T>(string name, Func<Task<T>> query)
    {
        await query();
        var stopwatch = Stopwatch.StartNew();
        await query();
        stopwatch.Stop();
        return (name, stopwatch.Elapsed);
    }
}
