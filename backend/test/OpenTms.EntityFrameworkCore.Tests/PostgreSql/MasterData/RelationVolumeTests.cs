using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OpenTms.MasterData;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.EntityFrameworkCore;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Relations;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;
using Xunit;
using Xunit.Abstractions;

namespace OpenTms.PostgreSql.MasterData;

/// <summary>
/// VOLUME TEST (design 43, "Expected volumes" × 5; performance-budgets interactive reads p95 &lt; 300 ms):
/// 100,000 relations with 150,000 addresses, 2,500 drivers with 7,500 qualifications in tenant A, then
/// the relation list, the lookup page by name fragment, the matcher on every rule and the driver list
/// are timed cold (first run, EF Core compiles the query shape) and warm. The two hottest reads of the
/// product, the lookup page by term and the matcher's name-only path, are asserted against the 300 ms
/// budget warm; the rest against the run-1 tripwire (1,500 ms), with every number reported.
/// Risk class: High. The data lives in one rolled-back transaction, so nothing stays behind.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class RelationVolumeTests : MasterDataPostgreSqlTestBase
{
    private const int Relations = 100_000;
    private const int Drivers = 2_500;
    private const int QualificationsPerDriver = 3;
    private const int BatchSize = 5_000;
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan Tripwire = TimeSpan.FromMilliseconds(1_500);

    private static readonly string[] Words =
    [
        "Jansen", "de Vries", "van den Berg", "Bakker", "Visser", "Smit", "Meijer", "de Boer", "Mulder", "Bos", "Vos", "Peters", "Hendriks", "van Leeuwen",
        "Dekker", "Brouwer", "de Wit", "Dijkstra", "Smits", "de Graaf", "van der Meer", "van der Linden", "Kok", "Jacobs", "de Haan", "Vermeulen", "van den Heuvel",
        "van der Veen", "van den Broek", "de Bruijn", "de Bruin", "van der Heijden", "Schouten", "van Beek", "Willems", "van Vliet", "van de Ven", "Hoekstra",
        "Maas", "Verhoeven", "Koster", "van Dam", "van der Wal", "Prins", "Blom", "Huisman", "Peeters", "de Jong", "Kuipers", "van Veen", "Post", "Kuiper",
        "Veenstra", "Kramer", "van de Berg", "Hofman", "Evers", "Müller", "Schäfer", "Ångström", "Łukasz", "Côté", "Nuñez", "García", "Østergaard"
    ];

    private static readonly string[] Cities = ["Amsterdam", "Rotterdam", "Utrecht", "Eindhoven", "Groningen", "Tilburg", "Almere", "Breda", "Nijmegen", "Zwolle", "Apeldoorn", "Haarlem"];

    private readonly ITestOutputHelper _output;
    private readonly IRepository<Relation, Guid> _relationRepository;
    private readonly IRepository<Driver, Guid> _driverRepository;
    private readonly IRepository<Country, Guid> _countries;
    private readonly IRepository<AddressType, Guid> _addressTypes;
    private readonly IDbContextProvider<MasterDataDbContext> _contextProvider;
    private readonly IRelationAppService _relations;
    private readonly IDriverAppService _drivers;
    private readonly IRelationMatcher _matcher;
    private readonly IGuidGenerator _guids;

    public RelationVolumeTests(ITestOutputHelper output)
    {
        _output = output;
        _relationRepository = GetRequiredService<IRepository<Relation, Guid>>();
        _driverRepository = GetRequiredService<IRepository<Driver, Guid>>();
        _countries = GetRequiredService<IRepository<Country, Guid>>();
        _addressTypes = GetRequiredService<IRepository<AddressType, Guid>>();
        _contextProvider = GetRequiredService<IDbContextProvider<MasterDataDbContext>>();
        _relations = GetRequiredService<IRelationAppService>();
        _drivers = GetRequiredService<IDriverAppService>();
        _matcher = GetRequiredService<IRelationMatcher>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    [Fact]
    public async Task List_lookup_page_matcher_and_driver_list_stay_within_the_interactive_read_budget_at_five_times_the_expected_volume()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var nl = await _countries.GetAsync(c => c.IsoAlpha2 == "NL");
            var visiting = await _addressTypes.GetAsync(t => t.Kind == AddressTypeKind.Visiting);
            var postal = await _addressTypes.GetAsync(t => t.Kind == AddressTypeKind.Postal);
            await InsertVolumeAsync(nl.Id, visiting.Id, postal.Id);
            await ReportQueryShapeAsync("transport 0123");

            using var culture = UiCulture("nl");
            var probe = 12_345;
            var cold = new List<(string Name, TimeSpan Elapsed)>();
            var warm = new List<(string Name, TimeSpan Elapsed)>();

            async Task TimeAsync<T>(string name, Func<Task<T>> query)
            {
                var (c, w) = await MeasureAsync(query);
                cold.Add((name, c));
                warm.Add((name, w));
            }

            await TimeAsync("relation list page 1 (100 rows, by name, with city and categories)", () => _relations.GetListAsync(new RelationListRequestDto { StartRow = 0, EndRow = 100 }));
            await TimeAsync("relation list page 500 (rows 49,900-50,000)", () => _relations.GetListAsync(new RelationListRequestDto { StartRow = 49_900, EndRow = 50_000 }));
            await TimeAsync("relation list searched on a name fragment", () => _relations.GetListAsync(new RelationListRequestDto { StartRow = 0, EndRow = 100, WildcardSearch = "transport 0123" }));
            await TimeAsync("relation list searched on a VAT fragment", () => _relations.GetListAsync(new RelationListRequestDto { StartRow = 0, EndRow = 100, WildcardSearch = "NL000012" }));
            await TimeAsync("relation list filtered on carriers", () => _relations.GetListAsync(new RelationListRequestDto { StartRow = 0, EndRow = 100, Roles = RelationRoles.Carrier }));
            await TimeAsync("relation list sorted by visiting city", () => _relations.GetListAsync(new RelationListRequestDto { StartRow = 0, EndRow = 100, SortBy = "city" }));
            await TimeAsync("relation list sorted by code desc", () => _relations.GetListAsync(new RelationListRequestDto { StartRow = 0, EndRow = 100, SortBy = "code", SortDir = "desc" }));
            await TimeAsync("lookup page by term (budget)", () => _relations.GetLookupPageAsync("transport 0123"));
            await TimeAsync("lookup page by rare term", () => _relations.GetLookupPageAsync("muller transport 0999"));
            await TimeAsync("lookup page by term with role filter", () => _relations.GetLookupPageAsync("transport 01", roles: RelationRoles.Carrier));
            await TimeAsync("lookup page without term, page 2", () => _relations.GetLookupPageAsync(skip: 25));
            await TimeAsync("lookup (500, term)", () => _relations.GetLookupAsync("transport 00"));
            await TimeAsync("matcher exact code", () => _matcher.MatchAsync(new RelationMatchRequestDto { Code = Code(probe) }));
            await TimeAsync("matcher exact VAT", () => _matcher.MatchAsync(new RelationMatchRequestDto { VatNumber = Vat(probe * 3) }));
            await TimeAsync("matcher name and postcode", () => _matcher.MatchAsync(new RelationMatchRequestDto { Name = $"Transport {probe:D6}", PostalCode = PostalCode(probe), CountryId = nl.Id }));
            await TimeAsync("matcher name only (budget)", () => _matcher.MatchAsync(new RelationMatchRequestDto { Name = "Transport 01234" }));
            await TimeAsync("matcher name only, common word", () => _matcher.MatchAsync(new RelationMatchRequestDto { Name = "Jansen" }));
            await TimeAsync("matcher no hit on any rule", () => _matcher.MatchAsync(new RelationMatchRequestDto { Code = "NOPE", VatNumber = "NL000000000B00", Name = "Nobody Ever", PostalCode = "0000ZZ", CountryId = nl.Id }));
            await TimeAsync("driver list page 1 (100 rows with employer names and expiring counts)", () => _drivers.GetListAsync(new DriverListRequestDto { StartRow = 0, EndRow = 100 }));
            await TimeAsync("driver list searched", () => _drivers.GetListAsync(new DriverListRequestDto { StartRow = 0, EndRow = 100, WildcardSearch = "driver 012" }));
            await TimeAsync("driver lookup", () => _drivers.GetLookupAsync("driver 01"));

            foreach (var ((name, c), (_, w)) in cold.Zip(warm))
            {
                _output.WriteLine($"{name}: cold {c.TotalMilliseconds:F0} ms, warm {w.TotalMilliseconds:F0} ms (budget {Budget.TotalMilliseconds} ms, tripwire {Tripwire.TotalMilliseconds} ms)");
            }

            var overBudget = warm.Where(m => m.Elapsed > Budget).Select(m => $"{m.Name}: {m.Elapsed.TotalMilliseconds:F0} ms").ToList();
            _output.WriteLine(overBudget.Count == 0 ? "every warm read within the 300 ms budget" : "warm reads over the 300 ms budget: " + string.Join("; ", overBudget));

            /* The two hottest paths must meet the budget warm; everything else the tripwire. */
            warm.Where(m => m.Name.EndsWith("(budget)", StringComparison.Ordinal)).Select(m => $"{m.Name}: {m.Elapsed.TotalMilliseconds:F0} ms").Where(_ => false).ShouldBeEmpty();
            foreach (var (name, elapsed) in warm.Where(m => m.Name.EndsWith("(budget)", StringComparison.Ordinal)))
            {
                elapsed.ShouldBeLessThanOrEqualTo(Budget, $"{name} warm: {elapsed.TotalMilliseconds:F0} ms over the {Budget.TotalMilliseconds} ms interactive-read budget");
            }

            var overTripwire = warm.Where(m => m.Elapsed > Tripwire).Select(m => $"{m.Name}: {m.Elapsed.TotalMilliseconds:F0} ms").ToList();
            overTripwire.ShouldBeEmpty($"warm queries over the {Tripwire.TotalMilliseconds} ms tripwire (budget {Budget.TotalMilliseconds} ms)");

            /* The results are the right ones, not just fast. */
            var page = await _relations.GetListAsync(new RelationListRequestDto { StartRow = 0, EndRow = 100 });
            page.TotalCount.ShouldBe(Relations);
            page.Rows.Count.ShouldBe(100);
            page.Rows.ShouldAllBe(r => r.City != null && r.CountryCode == "NL");

            var lookup = await _relations.GetLookupPageAsync("transport 0123");
            lookup.TotalCount.ShouldBe(100, "relations 012300..012399");
            lookup.Items.Count.ShouldBe(MasterDataConsts.LookupPageSize);
            lookup.Items.ShouldAllBe(i => i.City != null);
            (await _relations.GetLookupPageAsync("muller transport 0999")).Items.ShouldAllBe(i => i.Name.StartsWith("Müller", StringComparison.Ordinal));
            (await _relations.GetLookupAsync("transport 00")).Count.ShouldBe(MasterDataConsts.MaxLookupTake);

            (await _matcher.MatchAsync(new RelationMatchRequestDto { Code = Code(probe) })).Matches.ShouldHaveSingleItem().Kind.ShouldBe(RelationMatchKind.Exact);
            (await _matcher.MatchAsync(new RelationMatchRequestDto { VatNumber = Vat(probe * 3) })).Matches.ShouldHaveSingleItem().Code.ShouldBe(Code(probe * 3));
            var byPostcode = (await _matcher.MatchAsync(new RelationMatchRequestDto { Name = $"Transport {probe:D6}", PostalCode = PostalCode(probe), CountryId = nl.Id })).Matches.ShouldHaveSingleItem();
            byPostcode.Kind.ShouldBe(RelationMatchKind.NameAndPostalCode);
            byPostcode.Code.ShouldBe(Code(probe));
            var byName = await _matcher.MatchAsync(new RelationMatchRequestDto { Name = "Transport 01234" });
            byName.Matches.Count.ShouldBe(MasterDataConsts.MaxMatchCandidates);
            byName.Matches.ShouldAllBe(m => m.Kind == RelationMatchKind.NameOnly && m.Name.Contains("Transport 01234"));
            (await _matcher.MatchAsync(new RelationMatchRequestDto { Name = "Jansen" })).Matches.Count.ShouldBe(MasterDataConsts.MaxMatchCandidates);
            (await _matcher.MatchAsync(new RelationMatchRequestDto { Code = "NOPE", Name = "Nobody Ever" })).Matches.ShouldBeEmpty();

            var drivers = await _drivers.GetListAsync(new DriverListRequestDto { StartRow = 0, EndRow = 100 });
            drivers.TotalCount.ShouldBe(Drivers);
            drivers.Rows.ShouldAllBe(r => r.EmployerName != null);
            drivers.Rows.ShouldContain(r => r.QualificationsExpiring > 0);
        });
    }

    // ── data ─────────────────────────────────────────────────────────────────

    private static string Code(int i) => $"VR{i:D6}";

    private static string Vat(int i) => $"NL{i:D9}B01";

    private static string Name(int i) => $"{Words[i % Words.Length]} {Words[(i / Words.Length) % Words.Length]} Transport {i:D6}";

    private static string PostalCode(int i) => $"{1000 + i % 9000} {(char)('A' + i % 26)}{(char)('A' + (i / 26) % 26)}";

    private static RelationRoles Roles(int i) => (i % 10) switch
    {
        0 => RelationRoles.Carrier,
        1 => RelationRoles.Customer | RelationRoles.Carrier,
        2 => RelationRoles.Depot,
        3 => RelationRoles.Creditor,
        _ => RelationRoles.Customer
    };

    private async Task InsertVolumeAsync(Guid countryId, Guid visitingTypeId, Guid postalTypeId)
    {
        var insert = Stopwatch.StartNew();
        var context = await _contextProvider.GetDbContextAsync();
        var carriers = new List<Guid>();
        var addresses = 0;

        for (var start = 0; start < Relations; start += BatchSize)
        {
            var batch = new List<Relation>(BatchSize);
            for (var i = start; i < Math.Min(start + BatchSize, Relations); i++)
            {
                var relation = new Relation(_guids.Create(), TenantA, Code(i), Name(i), Roles(i));
                relation.SetLegal(i % 5 == 0 ? $"{i:D8}" : null, i % 3 == 0 ? Vat(i) : null);
                relation.SetContact(null, null, i % 4 == 0 ? $"planning{i}@volume.test" : null, null);
                var inputs = new List<RelationAddressInput>
                {
                    new(null, visitingTypeId, countryId, $"Volumestraat {i % 300}", $"{i % 150 + 1}", null, PostalCode(i), Cities[i % Cities.Length], null, null, "Europe/Amsterdam")
                };
                if (i % 2 == 0)
                {
                    inputs.Add(new(null, postalTypeId, countryId, "Postbus", $"{i % 9000}", null, PostalCode(i + 1), Cities[(i + 1) % Cities.Length], null, null, "Europe/Amsterdam"));
                }

                relation.SetAddresses(inputs, () => _guids.Create());
                addresses += inputs.Count;
                if (relation.HasRole(RelationRoles.Carrier))
                {
                    carriers.Add(relation.Id);
                }

                batch.Add(relation);
            }

            await _relationRepository.InsertManyAsync(batch, autoSave: true);
            context.ChangeTracker.Clear();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var drivers = new List<Driver>(Drivers);
        for (var d = 0; d < Drivers; d++)
        {
            var driver = new Driver(_guids.Create(), TenantA, $"DRV{d:D5}", Words[d % Words.Length].Split(' ').Last(), $"Driver {d:D5}");
            driver.SetContact(null, $"06{d:D8}", $"driver{d}@volume.test");
            driver.SetEmployer(carriers[d % carriers.Count]);
            driver.SetQualifications(
                Enumerable.Range(0, QualificationsPerDriver).Select(q => new DriverQualificationInput(null, (DriverQualificationKind)(q + 1), $"Q{d}-{q}", today.AddYears(-5), today.AddDays((d * 7 + q * 100) % 1_000 - 100), null)),
                () => _guids.Create());
            drivers.Add(driver);
        }

        await _driverRepository.InsertManyAsync(drivers, autoSave: true);
        context.ChangeTracker.Clear();

        _output.WriteLine($"inserted {Relations} relations with {addresses} addresses and {Drivers} drivers with {Drivers * QualificationsPerDriver} qualifications in {insert.Elapsed.TotalSeconds:F1} s");
    }

    /// <summary>
    /// Reports (never asserts) the SQL EF Core emits for the lookup's contains search and the planner's choice
    /// for it. Runs under a savepoint so a failure of the probe itself can never abort the test's transaction.
    /// </summary>
    private async Task ReportQueryShapeAsync(string term)
    {
        var context = await _contextProvider.GetDbContextAsync();
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction();
        await transaction!.SaveAsync("explain_probe");
        try
        {
            var key = MasterDataValues.SearchKey(term);
            var sql = context.Set<Relation>().Where(r => !r.IsArchived && r.SearchKey.Contains(key)).Select(r => r.Id).ToQueryString();
            _output.WriteLine("lookup contains-search SQL:\n" + sql);

            /* The statement ends at the first blank line; EF Core appends a prose note about split queries after it. */
            var statement = string.Join("\n", sql.Split('\n').Select(l => l.TrimEnd('\r')).TakeWhile(l => l.Length > 0).Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal)));
            foreach (System.Text.RegularExpressions.Match parameter in System.Text.RegularExpressions.Regex.Matches(sql, @"--\s*(@\w+)='([^']*)'"))
            {
                statement = statement.Replace(parameter.Groups[1].Value, $"'{parameter.Groups[2].Value}'");
            }

            var plan = new List<string>();
            await using (var command = new NpgsqlCommand("EXPLAIN " + statement, connection, transaction))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    plan.Add(reader.GetString(0));
                }
            }

            _output.WriteLine("planner (no statistics inside the rolled-back transaction, so the estimate is not the production plan):\n" + string.Join("\n", plan));
            await transaction.ReleaseAsync("explain_probe");
        }
        catch (Exception ex)
        {
            _output.WriteLine("query shape report skipped: " + ex.Message);
            await transaction.RollbackAsync("explain_probe");
        }
    }

    /// <summary>Cold: the first run (query compilation included). Warm: the second run of the same query.</summary>
    private static async Task<(TimeSpan Cold, TimeSpan Warm)> MeasureAsync<T>(Func<Task<T>> query)
    {
        var cold = Stopwatch.StartNew();
        await query();
        cold.Stop();
        var warm = Stopwatch.StartNew();
        await query();
        warm.Stop();
        return (cold.Elapsed, warm.Elapsed);
    }
}
