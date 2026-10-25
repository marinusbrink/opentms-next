using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using OpenTms.Financial.EntityFrameworkCore;
using OpenTms.Financial.Tariffs;
using OpenTms.Financial.Tariffs.Calculators;
using OpenTms.Financial.Tariffs.Pricing;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.PostgreSql.ReferenceData;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;
using Xunit;
using Xunit.Abstractions;

namespace OpenTms.PostgreSql.Financial;

/// <summary>
/// VOLUME TEST (design 57 "Expected volumes", "Test risk analysis": a 5,000-rule list of 10 brackets × 20 columns — one
/// million cells — loads a rule page in under 300 ms and the resolver prices a 50-line order in under 200 ms; design 56
/// Assumption 3: the preview, load included, under 500 ms). The rules, brackets and cells go in with COPY inside one rolled-back transaction, so
/// nothing stays behind; the reads are timed cold and warm and the warm reads are held to the budgets. Risk class: High.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class FinancialTariffsVolumeTests : ReferenceDataPostgreSqlTestBase
{
    private const int RuleCount = 5_000;
    private const int BracketCount = 10;
    private const int ColumnCount = 20;
    private const int RelationCount = 100;
    private const int LineCount = 50;
    private static readonly TimeSpan PageBudget = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ResolveBudget = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan PreviewBudget = TimeSpan.FromMilliseconds(500);

    private readonly ITestOutputHelper _output;
    private readonly IGuidGenerator _guids;

    public FinancialTariffsVolumeTests(ITestOutputHelper output)
    {
        _output = output;
        _guids = GetRequiredService<IGuidGenerator>();
    }

    [Fact]
    public async Task A_five_thousand_rule_list_pages_its_rules_and_prices_a_fifty_line_order_within_budget()
    {
        try
        {
            await RunAsync();
        }
        finally
        {
            await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(PostgreSqlTestDatabases.TenantADatabase));
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("VACUUM ANALYZE financial.\"TariffsPriceListCells\", financial.\"TariffsPriceListBrackets\", financial.\"TariffsPriceListRules\"", connection) { CommandTimeout = 300 };
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task RunAsync()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var relations = Enumerable.Range(0, RelationCount).Select(_ => Guid.NewGuid()).ToList();
            var lists = GetRequiredService<IRepository<PriceList, Guid>>();
            var list = new PriceList(_guids.Create(), TenantA, "VOLUME", "Volume", RateSide.Revenue, "EUR", QuantitySource.LineQuantity, new DateOnly(2026, 1, 1));
            list.SetColumnQuantity(ColumnQuantitySource.LineWeightKg);
            list.SetColumns(Enumerable.Range(1, ColumnCount).Select(i => i == ColumnCount ? null : (decimal?)(i * 100m)).ToList());
            await lists.InsertAsync(list, autoSave: true);

            var context = await GetRequiredService<IDbContextProvider<FinancialDbContext>>().GetDbContextAsync();
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            var load = Stopwatch.StartNew();
            await CopyRulesAsync(connection, list.Id, relations);
            load.Stop();
            _output.WriteLine($"COPY of {RuleCount} rules, {RuleCount * BracketCount} brackets and {RuleCount * BracketCount * ColumnCount} cells: {load.Elapsed}");
            await context.Database.ExecuteSqlRawAsync("ANALYZE financial.\"TariffsPriceListRules\"; ANALYZE financial.\"TariffsPriceListBrackets\"; ANALYZE financial.\"TariffsPriceListCells\"");

            var service = GetRequiredService<IPriceListAppService>();
            var preview = GetRequiredService<IPricePreviewService>();
            var snapshot = Snapshot(list.Id, relations);
            var timings = new List<(string Name, TimeSpan Cold, TimeSpan Warm)>();

            async Task TimeAsync<T>(string name, Func<Task<T>> read, Action<T> check)
            {
                var cold = Stopwatch.StartNew();
                check(await read());
                cold.Stop();
                var warm = Stopwatch.StartNew();
                check(await read());
                warm.Stop();
                timings.Add((name, cold.Elapsed, warm.Elapsed));
            }

            await TimeAsync("rule page 1", () => service.GetRulesAsync(list.Id, new PriceListRuleListRequestDto { StartRow = 0, EndRow = 100 }), page =>
            {
                page.TotalCount.ShouldBe(RuleCount);
                page.Rows.Count.ShouldBe(100);
                page.Rows[0].BracketCount.ShouldBe(BracketCount);
            });
            await TimeAsync("rule page 40 by tier", () => service.GetRulesAsync(list.Id, new PriceListRuleListRequestDto { StartRow = 3_900, EndRow = 4_000, Tier = 0, SortBy = "tier" }), page => page.Rows.Count.ShouldBe(100));
            await TimeAsync("rule page by relation", () => service.GetRulesAsync(list.Id, new PriceListRuleListRequestDto { StartRow = 0, EndRow = 100, WildcardSearch = relations[3].ToString() }), page => page.FilteredCount.ShouldBeGreaterThan(0));
            await TimeAsync("list detail", () => service.GetAsync(list.Id), detail => detail.RuleCount.ShouldBe(RuleCount));
            await TimeAsync("rule detail", async () => await service.GetRuleAsync(list.Id, (await service.GetRulesAsync(list.Id, new PriceListRuleListRequestDto { StartRow = 0, EndRow = 1 })).Rows[0].Id), rule => rule.Brackets.Count.ShouldBe(BracketCount));
            await TimeAsync("preview of 50 lines", () => preview.PreviewAsync(snapshot), result =>
            {
                result.Lines.Count.ShouldBe(LineCount, "every line finds its lane");
                result.Warnings.ShouldBeEmpty();
                result.TotalAmount.ShouldBeGreaterThan(0m);
            });

            /* The resolver alone, over the lists the loader read for these lanes. */
            var pricingLines = PricingLines.OfOrder(snapshot, []);
            var loaded = await GetRequiredService<PricingDataLoader>().GetPriceListsAsync(snapshot.PriceListIds, PricingDataLoader.LanesOf(pricingLines));
            loaded.Single().Rules.Count.ShouldBe(LineCount, "one rule per lane of the order; the other 4,950 stay in the database");
            var request = new PricingRequest
            {
                CurrencyCode = "EUR",
                AsOf = new DateOnly(2026, 10, 26),
                Lines = pricingLines,
                PriceLists = loaded,
                RootAnchor = RateLineAnchor.Order(snapshot.OrderId!.Value),
                LineAnchor = id => RateLineAnchor.OrderLine(snapshot.OrderId!.Value, id),
                TenantId = TenantA,
                Now = DateTimeOffset.UtcNow,
                Version = 1
            };
            await TimeAsync("resolver of 50 lines", () => Task.FromResult(PriceResolver.Resolve(request)), resolution => resolution.NewLines.Count.ShouldBe(LineCount));

            foreach (var (name, cold, warm) in timings)
            {
                _output.WriteLine($"{name}: cold {cold.TotalMilliseconds:F0} ms, warm {warm.TotalMilliseconds:F0} ms");
            }

            foreach (var (name, _, warm) in timings.Where(t => t.Name.StartsWith("rule", StringComparison.Ordinal) || t.Name.StartsWith("list", StringComparison.Ordinal)))
            {
                warm.ShouldBeLessThan(PageBudget, $"{name} warm");
            }

            timings.Single(t => t.Name.StartsWith("preview", StringComparison.Ordinal)).Warm.ShouldBeLessThan(PreviewBudget, "preview warm");
            timings.Single(t => t.Name.StartsWith("resolver", StringComparison.Ordinal)).Warm.ShouldBeLessThan(ResolveBudget, "resolver warm");
        });
    }

    /// <summary>The lines of the 50-line order: ten relations on each side, weights across the columns.</summary>
    private static PricingSnapshotDto Snapshot(Guid listId, List<Guid> relations) => new()
    {
        OrderId = Guid.NewGuid(),
        CustomerRelationId = Guid.NewGuid(),
        OrderDate = new DateOnly(2026, 10, 26),
        CurrencyCode = "EUR",
        PriceListIds = [listId],
        Lines = Enumerable.Range(0, LineCount).Select(i => new PricingLineDto
        {
            LineId = Guid.NewGuid(),
            Position = i + 1,
            TransportUnitId = Guid.NewGuid(),
            Quantity = 1 + i % 5,
            WeightKg = 50 + i * 40,
            Stops =
            [
                new PricingStopDto { StopId = Guid.NewGuid(), Position = 1, RelationId = relations[i % 10], Date = new DateOnly(2026, 10, 26) },
                new PricingStopDto { StopId = Guid.NewGuid(), Position = 2, RelationId = relations[(i * 7) % 50], Date = new DateOnly(2026, 10, 27) }
            ]
        }).ToList()
    };

    /// <summary>Rule i goes from relation i % 100 to relation i / 100 (every pair of the first 100 × 50), graded on weight.</summary>
    private async Task CopyRulesAsync(NpgsqlConnection connection, Guid listId, List<Guid> relations)
    {
        var ruleIds = Enumerable.Range(0, RuleCount).Select(_ => Guid.NewGuid()).ToList();
        var bracketIds = Enumerable.Range(0, RuleCount * BracketCount).Select(_ => Guid.NewGuid()).ToList();

        await using (var rules = await connection.BeginBinaryImportAsync(
            "COPY financial.\"TariffsPriceListRules\" (\"Id\", \"TenantId\", \"PriceListId\", \"Position\", \"FromRelationId\", \"ToRelationId\", \"RowQuantity\") FROM STDIN (FORMAT BINARY)"))
        {
            for (var i = 0; i < RuleCount; i++)
            {
                await rules.StartRowAsync();
                await rules.WriteAsync(ruleIds[i], NpgsqlDbType.Uuid);
                await rules.WriteAsync(TenantA, NpgsqlDbType.Uuid);
                await rules.WriteAsync(listId, NpgsqlDbType.Uuid);
                await rules.WriteAsync(i + 1, NpgsqlDbType.Integer);
                await rules.WriteAsync(relations[i % RelationCount], NpgsqlDbType.Uuid);
                await rules.WriteAsync(relations[i / RelationCount], NpgsqlDbType.Uuid);
                await rules.WriteAsync((int)QuantitySource.WeightKg, NpgsqlDbType.Integer);
            }

            await rules.CompleteAsync();
        }

        await using (var brackets = await connection.BeginBinaryImportAsync(
            "COPY financial.\"TariffsPriceListBrackets\" (\"Id\", \"TenantId\", \"PriceListRuleId\", \"Position\", \"UpperBound\") FROM STDIN (FORMAT BINARY)"))
        {
            for (var i = 0; i < RuleCount; i++)
            {
                for (var b = 0; b < BracketCount; b++)
                {
                    await brackets.StartRowAsync();
                    await brackets.WriteAsync(bracketIds[i * BracketCount + b], NpgsqlDbType.Uuid);
                    await brackets.WriteAsync(TenantA, NpgsqlDbType.Uuid);
                    await brackets.WriteAsync(ruleIds[i], NpgsqlDbType.Uuid);
                    await brackets.WriteAsync(b + 1, NpgsqlDbType.Integer);
                    if (b == BracketCount - 1)
                    {
                        await brackets.WriteNullAsync();
                    }
                    else
                    {
                        await brackets.WriteAsync((decimal)((b + 1) * 100), NpgsqlDbType.Numeric);
                    }
                }
            }

            await brackets.CompleteAsync();
        }

        await using (var cells = await connection.BeginBinaryImportAsync(
            "COPY financial.\"TariffsPriceListCells\" (\"PriceListBracketId\", \"ColumnPosition\", \"TenantId\", \"UnitPrice\") FROM STDIN (FORMAT BINARY)"))
        {
            for (var k = 0; k < RuleCount * BracketCount; k++)
            {
                for (var c = 1; c <= ColumnCount; c++)
                {
                    await cells.StartRowAsync();
                    await cells.WriteAsync(bracketIds[k], NpgsqlDbType.Uuid);
                    await cells.WriteAsync(c, NpgsqlDbType.Integer);
                    await cells.WriteAsync(TenantA, NpgsqlDbType.Uuid);
                    await cells.WriteAsync(10m + c + (k % BracketCount) * 0.5m, NpgsqlDbType.Numeric);
                }
            }

            await cells.CompleteAsync();
        }
    }
}
