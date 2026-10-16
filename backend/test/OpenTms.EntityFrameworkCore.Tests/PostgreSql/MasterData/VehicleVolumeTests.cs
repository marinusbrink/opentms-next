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
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;
using Xunit;
using Xunit.Abstractions;

namespace OpenTms.PostgreSql.MasterData;

/// <summary>
/// VOLUME TEST (design 48, "Expected volumes" × 5; "Cost &amp; SLO impact": list, lookup and resolver p95 &lt; 300 ms):
/// 5,000 vehicles (3 own characteristics, 1 default driver and 1 external id each), 2,500 combinations and 250,000
/// product relation codes in tenant A, then the vehicle list, lookup page and lookup, the combination list, the code
/// list (paged, deep page, contains search, name sort), the code lookup and the resolver of 100 and 500 codes are timed
/// cold (first run, EF Core compiles the query shape) and warm, after ANALYZE gave the planner production-like
/// statistics. Every warm read is asserted against the 300 ms budget (PO 2026-10-16: the non-default sorts and the deep
/// page too), with every number reported. Risk class: High. The data lives in one rolled-back transaction, so nothing stays behind.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class VehicleVolumeTests : MasterDataPostgreSqlTestBase
{
    private const int Vehicles = 5_000;
    private const int Combinations = Vehicles / 2;
    private const int Codes = 250_000;
    private const int Destinations = 1_000;
    private const int ProductCount = 100;
    private const int UnitCount = 10;
    private const int DriverCount = 500;
    private const int CharacteristicCount = 10;
    private const int Carriers = 50;
    private const int BatchSize = 5_000;
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan Tripwire = TimeSpan.FromMilliseconds(1_500);

    private readonly ITestOutputHelper _output;
    private readonly IRepository<Vehicle, Guid> _vehicleRepository;
    private readonly IRepository<VehicleCombination, Guid> _combinationRepository;
    private readonly IRepository<ProductRelationCode, Guid> _codeRepository;
    private readonly IRepository<Relation, Guid> _relationRepository;
    private readonly IRepository<Country, Guid> _countries;
    private readonly IRepository<AddressType, Guid> _addressTypes;
    private readonly IDbContextProvider<MasterDataDbContext> _contextProvider;
    private readonly IGuidGenerator _guids;

    private IVehicleAppService VehicleService => GetRequiredService<IVehicleAppService>();
    private IVehicleCombinationAppService CombinationService => GetRequiredService<IVehicleCombinationAppService>();
    private IProductRelationCodeAppService CodeService => GetRequiredService<IProductRelationCodeAppService>();
    private IProductRelationCodeResolver Resolver => GetRequiredService<IProductRelationCodeResolver>();

    public VehicleVolumeTests(ITestOutputHelper output)
    {
        _output = output;
        _vehicleRepository = GetRequiredService<IRepository<Vehicle, Guid>>();
        _combinationRepository = GetRequiredService<IRepository<VehicleCombination, Guid>>();
        _codeRepository = GetRequiredService<IRepository<ProductRelationCode, Guid>>();
        _relationRepository = GetRequiredService<IRepository<Relation, Guid>>();
        _countries = GetRequiredService<IRepository<Country, Guid>>();
        _addressTypes = GetRequiredService<IRepository<AddressType, Guid>>();
        _contextProvider = GetRequiredService<IDbContextProvider<MasterDataDbContext>>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    [Fact]
    public async Task Vehicle_and_code_lists_lookups_and_the_resolver_stay_within_the_interactive_read_budget_at_five_times_the_expected_volume()
    {
        try
        {
            await RunAsync();
        }
        finally
        {
            await CleanUpStatisticsAsync();
        }
    }

    /// <summary>
    /// The rolled-back quarter million rows leave dead tuples and, once autovacuum's ANALYZE has run, statistics that no
    /// longer describe the (unchanged) tables; the next volume test in this collection would plan its queries on them.
    /// VACUUM ANALYZE here puts tenant A's database back as it was, synchronously.
    /// </summary>
    private async Task CleanUpStatisticsAsync()
    {
        var clean = Stopwatch.StartNew();
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(PostgreSqlTestDatabases.TenantADatabase));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("VACUUM ANALYZE", connection) { CommandTimeout = 300 };
        await command.ExecuteNonQueryAsync();
        _output.WriteLine($"VACUUM ANALYZE of tenant A's database after the rollback: {clean.Elapsed.TotalSeconds:F1} s");
    }

    private async Task RunAsync()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var nl = await _countries.GetAsync(c => c.IsoAlpha2 == "NL");
            var visiting = await _addressTypes.GetAsync(t => t.Kind == AddressTypeKind.Visiting);
            var carriers = await InsertVolumeAsync(nl.Id, visiting.Id);
            await ReportPlanAsync("resolver IN over the unique index", $"SELECT \"Id\" FROM masterdata.\"ProductRelationCodes\" WHERE NOT \"IsDeleted\" AND \"TenantId\" = '{TenantA}' AND NOT \"IsArchived\" AND \"NormalizedCode\" = ANY (ARRAY[{string.Join(", ", Enumerable.Range(0, 100).Select(i => $"'{Code(i * 2_000)}'"))}])");
            await ReportPlanAsync("code contains search", $"SELECT \"Id\" FROM masterdata.\"ProductRelationCodes\" WHERE NOT \"IsDeleted\" AND \"TenantId\" = '{TenantA}' AND \"NormalizedCode\" LIKE '%ART12345%'");

            var hundred = Enumerable.Range(0, 100).Select(i => Code(i * 2_000).ToLowerInvariant()).Append("unknown-code").ToList();
            var fiveHundred = Enumerable.Range(0, 500).Select(i => Code(i * 499)).ToList();
            var cold = new List<(string Name, TimeSpan Elapsed)>();
            var warm = new List<(string Name, TimeSpan Elapsed)>();

            async Task TimeAsync<T>(string name, Func<Task<T>> query)
            {
                var (c, w) = await MeasureAsync(query);
                cold.Add((name, c));
                warm.Add((name, w));
            }

            await TimeAsync("vehicle list page 1 (100 rows with type, owner, depot, country and combination) (budget)", () => VehicleService.GetListAsync(new VehicleListRequestDto { StartRow = 0, EndRow = 100 }));
            await TimeAsync("vehicle list page 40 (rows 3,900-4,000)", () => VehicleService.GetListAsync(new VehicleListRequestDto { StartRow = 3_900, EndRow = 4_000 }));
            await TimeAsync("vehicle list filtered on trailers", () => VehicleService.GetListAsync(new VehicleListRequestDto { StartRow = 0, EndRow = 100, Kinds = [VehicleTypeKind.Trailer] }));
            await TimeAsync("vehicle list filtered on an owner", () => VehicleService.GetListAsync(new VehicleListRequestDto { StartRow = 0, EndRow = 100, OwnerRelationId = carriers[7] }));
            await TimeAsync("vehicle list searched on a plate fragment with dashes", () => VehicleService.GetListAsync(new VehicleListRequestDto { StartRow = 0, EndRow = 100, WildcardSearch = "qv-12" }));
            await TimeAsync("vehicle list searched on a name fragment", () => VehicleService.GetListAsync(new VehicleListRequestDto { StartRow = 0, EndRow = 100, WildcardSearch = "truck 012" }));
            await TimeAsync("vehicle list sorted by owner name", () => VehicleService.GetListAsync(new VehicleListRequestDto { StartRow = 0, EndRow = 100, SortBy = "ownerName" }));
            await TimeAsync("vehicle list sorted by type name desc", () => VehicleService.GetListAsync(new VehicleListRequestDto { StartRow = 0, EndRow = 100, SortBy = "vehicleTypeName", SortDir = "desc" }));
            await TimeAsync("vehicle lookup page by term (budget)", () => VehicleService.GetLookupPageAsync("truck 012"));
            await TimeAsync("vehicle lookup page by term, motorised kinds only", () => VehicleService.GetLookupPageAsync("12", kinds: [VehicleTypeKind.Tractor, VehicleTypeKind.Rigid, VehicleTypeKind.Van]));
            await TimeAsync("vehicle lookup page without term, page 2", () => VehicleService.GetLookupPageAsync(skip: 25));
            await TimeAsync("vehicle lookup (500, term)", () => VehicleService.GetLookupAsync("truck"));
            await TimeAsync("vehicle detail with children, union and combination", () => VehicleService.GetAsync(_probeVehicle));
            await TimeAsync("combination list page 1", () => CombinationService.GetListAsync(new ArchivableListRequestDto { StartRow = 0, EndRow = 100 }));
            await TimeAsync("combination list searched", () => CombinationService.GetListAsync(new ArchivableListRequestDto { StartRow = 0, EndRow = 100, WildcardSearch = "combination 01" }));
            await TimeAsync("code list page 1 (100 rows with destination, city, product and unit) (budget)", () => CodeService.GetListAsync(new ArchivableListRequestDto { StartRow = 0, EndRow = 100 }));
            await TimeAsync("code list page 1,000 (rows 99,900-100,000)", () => CodeService.GetListAsync(new ArchivableListRequestDto { StartRow = 99_900, EndRow = 100_000 }));
            await TimeAsync("code list searched on a code fragment (busiest screen) (budget)", () => CodeService.GetListAsync(new ArchivableListRequestDto { StartRow = 0, EndRow = 100, WildcardSearch = "art12345" }));
            await TimeAsync("code list searched on a destination name", () => CodeService.GetListAsync(new ArchivableListRequestDto { StartRow = 0, EndRow = 100, WildcardSearch = "destination 0042" }));
            await TimeAsync("code list sorted by product name", () => CodeService.GetListAsync(new ArchivableListRequestDto { StartRow = 0, EndRow = 100, SortBy = "productName" }));
            await TimeAsync("code lookup (500, term)", () => CodeService.GetLookupAsync("art0012"));
            await TimeAsync("resolver of 100 codes (budget)", () => Resolver.ResolveAsync(hundred));
            await TimeAsync("resolver of 500 codes", () => Resolver.ResolveAsync(fiveHundred));

            foreach (var ((name, c), (_, w)) in cold.Zip(warm))
            {
                _output.WriteLine($"{name}: cold {c.TotalMilliseconds:F0} ms, warm {w.TotalMilliseconds:F0} ms (budget {Budget.TotalMilliseconds} ms, tripwire {Tripwire.TotalMilliseconds} ms)");
            }

            var overBudget = warm.Where(m => m.Elapsed > Budget).Select(m => $"{m.Name}: {m.Elapsed.TotalMilliseconds:F0} ms").ToList();
            _output.WriteLine(overBudget.Count == 0 ? "every warm read within the 300 ms budget" : "warm reads over the 300 ms budget: " + string.Join("; ", overBudget));

            /* Every read is a list, lookup or resolve of design 48, so every one is held to the budget (PO 2026-10-16). */
            foreach (var (name, elapsed) in warm)
            {
                elapsed.ShouldBeLessThanOrEqualTo(Budget, $"{name} warm: {elapsed.TotalMilliseconds:F0} ms over the {Budget.TotalMilliseconds} ms interactive-read budget");
            }

            var overTripwire = warm.Where(m => m.Elapsed > Tripwire).Select(m => $"{m.Name}: {m.Elapsed.TotalMilliseconds:F0} ms").ToList();
            overTripwire.ShouldBeEmpty($"warm queries over the {Tripwire.TotalMilliseconds} ms tripwire (budget {Budget.TotalMilliseconds} ms)");

            /* The results are the right ones, not just fast. */
            var vehicles = await VehicleService.GetListAsync(new VehicleListRequestDto { StartRow = 0, EndRow = 100 });
            vehicles.TotalCount.ShouldBe(Vehicles);
            vehicles.Rows.Count.ShouldBe(100);
            vehicles.Rows.ShouldAllBe(r => r.OwnerName != null && r.VehicleTypeName != null && r.CombinationName != null && r.LicencePlate!.Contains('-'));
            (await VehicleService.GetListAsync(new VehicleListRequestDto { StartRow = 0, EndRow = 100, Kinds = [VehicleTypeKind.Trailer] })).TotalCount.ShouldBe(Vehicles / 2);
            (await VehicleService.GetListAsync(new VehicleListRequestDto { StartRow = 0, EndRow = 100, WildcardSearch = "qv-12" })).Rows.ShouldAllBe(r => r.LicencePlate!.StartsWith("QV-12", StringComparison.Ordinal));
            var lookup = await VehicleService.GetLookupPageAsync("truck 012");
            lookup.TotalCount.ShouldBe(10, "trucks 0120..0129");
            lookup.Items.ShouldAllBe(i => i.Kind == VehicleTypeKind.Tractor);
            (await VehicleService.GetLookupAsync("truck")).Count.ShouldBe(MasterDataConsts.MaxLookupTake);
            var detail = await VehicleService.GetAsync(_probeVehicle);
            detail.Characteristics.Count.ShouldBeGreaterThanOrEqualTo(3);
            detail.DefaultDrivers.ShouldHaveSingleItem();
            detail.ExternalIdentifiers.ShouldHaveSingleItem();
            detail.CombinationName.ShouldNotBeNull();
            (await CombinationService.GetListAsync(new ArchivableListRequestDto { StartRow = 0, EndRow = 100 })).TotalCount.ShouldBe(Combinations);

            var codes = await CodeService.GetListAsync(new ArchivableListRequestDto { StartRow = 0, EndRow = 100 });
            codes.TotalCount.ShouldBe(Codes);
            codes.Rows.Count.ShouldBe(100);
            codes.Rows.ShouldAllBe(r => r.ProductName != null);
            codes.Rows.Where(r => r.RelationName != null).ShouldAllBe(r => r.RelationCity != null);
            var searched = await CodeService.GetListAsync(new ArchivableListRequestDto { StartRow = 0, EndRow = 100, WildcardSearch = "art12345" });
            searched.FilteredCount.ShouldBe(10, "ART123450..ART123459");
            searched.Rows.ShouldAllBe(r => r.Code.StartsWith("ART12345", StringComparison.Ordinal));
            (await Resolver.ResolveAsync(hundred)).Count.ShouldBe(100, "the unknown code is simply absent");
            (await Resolver.ResolveAsync(fiveHundred)).Count.ShouldBe(500);
            (await _codeRepository.GetCountAsync()).ShouldBe(Codes, "the resolver never creates a code");
        });
    }

    // ── data ─────────────────────────────────────────────────────────────────

    private Guid _probeVehicle;

    private static string Code(int i) => $"ART{i:D6}";

    private async Task<List<Guid>> InsertVolumeAsync(Guid countryId, Guid visitingTypeId)
    {
        var insert = Stopwatch.StartNew();
        var context = await _contextProvider.GetDbContextAsync();

        /* Reference rows: types, carriers and destinations, drivers, characteristics, products, transport units. */
        var tractorTypes = Enumerable.Range(0, 5).Select(i => new VehicleType(_guids.Create(), TenantA, $"Volume tractor type {i}", VehicleTypeKind.Tractor)).ToList();
        var trailerTypes = Enumerable.Range(0, 5).Select(i => new VehicleType(_guids.Create(), TenantA, $"Volume trailer type {i}", VehicleTypeKind.Trailer)).ToList();
        var characteristics = Enumerable.Range(0, CharacteristicCount).Select(i => new TransportCharacteristics(_guids.Create(), TenantA, $"VC{i:D2}", $"Volume characteristic {i}")).ToList();
        foreach (var type in tractorTypes)
        {
            type.SetCharacteristics([characteristics[0].Id, characteristics[1].Id]);
        }

        context.AddRange(characteristics);
        context.AddRange(tractorTypes);
        context.AddRange(trailerTypes);
        var products = Enumerable.Range(0, ProductCount).Select(i => new Product(_guids.Create(), TenantA, $"Volume product {i:D3}")).ToList();
        var units = Enumerable.Range(0, UnitCount).Select(i => new TransportUnit(_guids.Create(), TenantA, $"Volume unit {i:D2}")).ToList();
        context.AddRange(products);
        context.AddRange(units);
        var drivers = Enumerable.Range(0, DriverCount).Select(i => new Driver(_guids.Create(), TenantA, $"VD{i:D4}", "Volume", $"Driver {i:D4}")).ToList();
        context.AddRange(drivers);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var carriers = new List<Relation>();
        for (var i = 0; i < Carriers; i++)
        {
            carriers.Add(new Relation(_guids.Create(), TenantA, $"VCAR{i:D3}", $"Volume carrier {i:D3}", RelationRoles.Carrier | RelationRoles.Depot));
        }

        var destinations = new List<Relation>();
        for (var i = 0; i < Destinations; i++)
        {
            var relation = new Relation(_guids.Create(), TenantA, $"VDST{i:D4}", $"Volume destination {i:D4}", RelationRoles.Customer);
            relation.SetAddresses([new RelationAddressInput(null, visitingTypeId, countryId, "Volumestraat", $"{i % 150 + 1}", null, null, "1234 AB", $"City {i % 40}", null, null, "Europe/Amsterdam")], () => _guids.Create());
            destinations.Add(relation);
        }

        await _relationRepository.InsertManyAsync(carriers.Concat(destinations), autoSave: true);
        context.ChangeTracker.Clear();

        /* Vehicles: tractor 2k and trailer 2k+1, coupled as combination k. */
        var vehicles = new List<Vehicle>(Vehicles);
        for (var i = 0; i < Vehicles; i++)
        {
            var k = i / 2;
            var tractor = i % 2 == 0;
            var vehicle = new Vehicle(_guids.Create(), TenantA, tractor ? $"Truck {k:D4}" : $"Trailer {k:D4}", tractor ? tractorTypes[k % 5].Id : trailerTypes[k % 5].Id, carriers[k % Carriers].Id);
            vehicle.SetRegistration($"QV{i:D4}", countryId, $"WDB9634031L{i:D6}");
            vehicle.SetOwnership(carriers[k % Carriers].Id, carriers[(k + 1) % Carriers].Id);
            vehicle.SetTransportCharacteristics([characteristics[2 + i % 8].Id, characteristics[2 + (i + 1) % 8].Id, characteristics[2 + (i + 2) % 8].Id]);
            vehicle.SetDefaultDrivers([drivers[i % DriverCount].Id]);
            vehicle.SetExternalIdentifiers([new VehicleExternalIdentifierInput(null, "FMS", $"FMS{i:D5}")], () => _guids.Create());
            vehicles.Add(vehicle);
        }

        _probeVehicle = vehicles[2_468].Id;
        await _vehicleRepository.InsertManyAsync(vehicles, autoSave: true);
        context.ChangeTracker.Clear();

        var combinations = new List<VehicleCombination>(Combinations);
        for (var k = 0; k < Combinations; k++)
        {
            var combination = new VehicleCombination(_guids.Create(), TenantA, $"Combination {k:D4}");
            combination.SetMembers([(vehicles[2 * k].Id, VehicleTypeKind.Tractor), (vehicles[2 * k + 1].Id, VehicleTypeKind.Trailer)]);
            combinations.Add(combination);
        }

        await _combinationRepository.InsertManyAsync(combinations, autoSave: true);
        context.ChangeTracker.Clear();

        for (var start = 0; start < Codes; start += BatchSize)
        {
            var batch = new List<ProductRelationCode>(BatchSize);
            for (var i = start; i < Math.Min(start + BatchSize, Codes); i++)
            {
                var code = new ProductRelationCode(_guids.Create(), TenantA, Code(i));
                code.SetTargets(i % 3 == 0 ? null : destinations[i % Destinations].Id, products[i % ProductCount].Id, i % 2 == 0 ? units[i % UnitCount].Id : null);
                batch.Add(code);
            }

            await _codeRepository.InsertManyAsync(batch, autoSave: true);
            context.ChangeTracker.Clear();
        }

        _output.WriteLine($"inserted {Vehicles} vehicles with {Vehicles * 6} child rows, {Combinations} combinations and {Codes} product relation codes in {insert.Elapsed.TotalSeconds:F1} s");
        await AnalyzeAsync();
        return carriers.Select(c => c.Id).ToList();
    }

    /// <summary>
    /// Gives the planner the statistics autovacuum keeps for committed tables in production. Without them it estimates
    /// 49 rows for 250,000 codes and sorts the whole table on disk instead of walking the unique index (the deep page
    /// measured 664 ms that way). ANALYZE may run inside the transaction; the statistics it writes roll back with it.
    /// </summary>
    private async Task AnalyzeAsync()
    {
        var context = await _contextProvider.GetDbContextAsync();
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction();
        foreach (var table in new[] { "Relations", "RelationAddresses", "Products", "TransportUnits", "VehicleTypes", "Drivers", "Vehicles", "VehicleTransportCharacteristics", "VehicleDefaultDrivers", "VehicleExternalIdentifiers", "VehicleCombinations", "VehicleCombinationMembers", "ProductRelationCodes" })
        {
            await using var command = new NpgsqlCommand($"ANALYZE masterdata.\"{table}\"", connection, transaction);
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Reports (never asserts) the planner's choice; under a savepoint so the probe cannot abort the transaction.</summary>
    private async Task ReportPlanAsync(string name, string sql)
    {
        var context = await _contextProvider.GetDbContextAsync();
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction();
        await transaction!.SaveAsync("explain_probe");
        try
        {
            var plan = new List<string>();
            await using (var command = new NpgsqlCommand("EXPLAIN " + sql, connection, transaction))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    plan.Add(reader.GetString(0));
                }
            }

            _output.WriteLine($"planner for the {name} (statistics from ANALYZE inside the rolled-back transaction):\n" + string.Join("\n", plan));
            await transaction.ReleaseAsync("explain_probe");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"plan report for the {name} skipped: " + ex.Message);
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
