using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTms.Financial;
using OpenTms.Financial.EntityFrameworkCore;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Published;
using OpenTms.Financial.Tariffs;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.PostgreSql.ReferenceData;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;
using Xunit;

namespace OpenTms.PostgreSql.Financial;

/// <summary>
/// Design 57 against real PostgreSQL: tenant isolation of lists, rules, cells, rate lines, settings and the published
/// readers and preview (tenant A in its own database, the shared tenant in the host database); the unique indexes, the
/// anchor check constraint, the cascading foreign keys and decimal exactness. Risk class: Critical.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class FinancialTariffsPostgreSqlTests : ReferenceDataPostgreSqlTestBase
{
    private readonly IRepository<PriceList, Guid> _lists;
    private readonly IRepository<PriceListRule, Guid> _rules;
    private readonly IRepository<PriceListCell> _cells;
    private readonly IRepository<RateLine, Guid> _lines;
    private readonly IRepository<RelationFinancialSettings, Guid> _settings;
    private readonly IGuidGenerator _guids;

    public FinancialTariffsPostgreSqlTests()
    {
        _lists = GetRequiredService<IRepository<PriceList, Guid>>();
        _rules = GetRequiredService<IRepository<PriceListRule, Guid>>();
        _cells = GetRequiredService<IRepository<PriceListCell>>();
        _lines = GetRequiredService<IRepository<RateLine, Guid>>();
        _settings = GetRequiredService<IRepository<RelationFinancialSettings, Guid>>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    private PriceList NewList(Guid tenantId, string code, params decimal?[] columns)
    {
        var list = new PriceList(_guids.Create(), tenantId, code, code, RateSide.Revenue, "EUR", QuantitySource.LineQuantity, new DateOnly(2026, 1, 1));
        if (columns.Length > 0)
        {
            list.SetColumns(columns);
        }

        return list;
    }

    private PriceListRule NewRule(Guid tenantId, PriceList list, int position, Guid from, Guid to, params decimal?[] prices)
    {
        var rule = new PriceListRule(_guids.Create(), tenantId, list.Id, position);
        rule.SetLane(from, null, to, null);
        rule.SetBrackets([(null, prices.ToList())], list.Columns.Count, _guids.Create);
        return rule;
    }

    [Fact]
    public async Task A_tenant_never_sees_another_tenants_lists_rules_cells_lines_settings_or_totals()
    {
        var orderId = Guid.NewGuid();
        var relationId = Guid.NewGuid();
        var listIdA = Guid.Empty;

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var list = NewList(TenantA, "ONLYA", 100m, null);
            listIdA = list.Id;
            await _lists.InsertAsync(list, autoSave: true);
            var rule = NewRule(TenantA, list, 1, Guid.NewGuid(), Guid.NewGuid(), 10m, 9m);
            await _rules.InsertAsync(rule, autoSave: true);
            var line = RateLine.Calculated(_guids.Create(), TenantA, RateSide.Revenue, RateLineAnchor.Order(orderId), RateLineKind.PriceList, "EUR", "k", 1m, QuantitySource.LineQuantity, 10m, 10m);
            await _lines.InsertAsync(line, autoSave: true);
            var settings = new RelationFinancialSettings(_guids.Create(), TenantA, relationId);
            settings.SetPriceLists(RateSide.Revenue, [list.Id], _guids.Create);
            await _settings.InsertAsync(settings, autoSave: true);

            var bracketId = rule.Brackets.Single().Id;
            (await _cells.CountAsync(c => c.PriceListBracketId == bracketId)).ShouldBe(2);
            (await GetRequiredService<IRateLineReader>().GetOrderTotalsAsync(orderId)).TotalAmount.ShouldBe(10m);
            (await GetRequiredService<IFinancialReferenceLookup>().GetPriceListsAsync([list.Id])).ShouldHaveSingleItem();
        });

        await InRolledBackTransactionAsync(SharedTenant, async () =>
        {
            await _lists.InsertAsync(NewList(SharedTenant, "SHARED"), autoSave: true);

            (await _lists.GetListAsync()).Select(l => l.Code).ShouldBe(["SHARED"]);
            (await _rules.CountAsync(r => r.PriceListId == listIdA)).ShouldBe(0);
            (await _cells.GetCountAsync()).ShouldBe(0, "the host database holds no cells of tenant A");
            (await _lines.GetListAsync(l => l.AnchorOrderId == orderId)).ShouldBeEmpty();
            (await _settings.FindAsync(s => s.RelationId == relationId)).ShouldBeNull();
            (await GetRequiredService<IRateLineReader>().GetOrderTotalsAsync(orderId)).TotalAmount.ShouldBe(0m);
            (await GetRequiredService<IFinancialReferenceLookup>().GetPriceListsAsync([listIdA])).ShouldBeEmpty();
            (await GetRequiredService<IRateLineAppService>().GetAsync(new RateLineGetRequestDto { OrderId = orderId })).Lines.ShouldBeEmpty();
            (await GetRequiredService<IPricePreviewService>().PreviewAsync(new PricingSnapshotDto
            {
                CurrencyCode = "EUR",
                PriceListIds = [listIdA],
                Lines = [new PricingLineDto { Position = 1, TransportUnitId = Guid.NewGuid(), Quantity = 1m, Stops = [new PricingStopDto { Position = 1, RelationId = Guid.NewGuid() }, new PricingStopDto { Position = 2, RelationId = Guid.NewGuid() }] }]
            })).Lines.ShouldBeEmpty("tenant A's list does not exist here");
        });
    }

    [Fact]
    public async Task Duplicate_codes_relations_bracket_positions_and_relation_lists_are_refused_by_the_indexes()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await _lists.InsertAsync(NewList(TenantA, "DUP"), autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _lists.InsertAsync(NewList(TenantA, "DUP"), autoSave: true), "IX_TariffsPriceLists_TenantId_Code");
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var relationId = Guid.NewGuid();
            await _settings.InsertAsync(new RelationFinancialSettings(_guids.Create(), TenantA, relationId), autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _settings.InsertAsync(new RelationFinancialSettings(_guids.Create(), TenantA, relationId), autoSave: true), "IX_InvoicingRelationFinancialSettings_TenantId_RelationId");
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var list = NewList(TenantA, "LADDER");
            await _lists.InsertAsync(list, autoSave: true);
            var rule = NewRule(TenantA, list, 1, Guid.NewGuid(), Guid.NewGuid(), 10m);
            await _rules.InsertAsync(rule, autoSave: true);
            var brackets = GetRequiredService<IRepository<PriceListBracket, Guid>>();
            var duplicate = (PriceListBracket)Activator.CreateInstance(typeof(PriceListBracket), nonPublic: true)!;
            typeof(PriceListBracket).GetProperty(nameof(PriceListBracket.PriceListRuleId))!.SetValue(duplicate, rule.Id);
            typeof(PriceListBracket).GetProperty(nameof(PriceListBracket.Position))!.SetValue(duplicate, 1);
            typeof(PriceListBracket).GetProperty(nameof(PriceListBracket.TenantId))!.SetValue(duplicate, TenantA);
            await ShouldViolateUniqueIndexAsync(() => brackets.InsertAsync(duplicate, autoSave: true), "IX_TariffsPriceListBrackets_RuleId_Position");
        });
    }

    [Fact]
    public async Task A_rate_line_with_two_anchors_or_none_is_refused_by_the_check_constraint()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var line = RateLine.Manual(_guids.Create(), TenantA, RateSide.Revenue, RateLineAnchor.Order(Guid.NewGuid()), RateLineKind.Manual, "EUR", "x", 1m, QuantitySource.Manual, 1m, null);
            await _lines.InsertAsync(line, autoSave: true);
            var context = await GetRequiredService<IDbContextProvider<FinancialDbContext>>().GetDbContextAsync();

            var two = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE financial.\"TariffsRateLines\" SET \"TripId\" = {0} WHERE \"Id\" = {1}", Guid.NewGuid(), line.Id));
            two.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
            two.ConstraintName.ShouldBe("CK_TariffsRateLines_OneAnchor");
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var line = RateLine.Manual(_guids.Create(), TenantA, RateSide.Revenue, RateLineAnchor.Order(Guid.NewGuid()), RateLineKind.Manual, "EUR", "x", 1m, QuantitySource.Manual, 1m, null);
            await _lines.InsertAsync(line, autoSave: true);
            var context = await GetRequiredService<IDbContextProvider<FinancialDbContext>>().GetDbContextAsync();

            var none = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE financial.\"TariffsRateLines\" SET \"OrderId\" = NULL WHERE \"Id\" = {0}", line.Id));
            none.ConstraintName.ShouldBe("CK_TariffsRateLines_OneAnchor");
        });
    }

    [Fact]
    public async Task Deleting_a_list_cascades_to_its_rules_brackets_and_cells_and_money_round_trips_exactly()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var list = NewList(TenantA, "CASCADE", 100m, null);
            await _lists.InsertAsync(list, autoSave: true);
            var rule = NewRule(TenantA, list, 1, Guid.NewGuid(), Guid.NewGuid(), 12345678901234.1234m, 0.1m + 0.2m);
            await _rules.InsertAsync(rule, autoSave: true);
            var bracketId = rule.Brackets.Single().Id;
            var line = RateLine.Calculated(_guids.Create(), TenantA, RateSide.Revenue, RateLineAnchor.Order(Guid.NewGuid()), RateLineKind.PriceList, "EUR", "k", 1234.5678m, QuantitySource.WeightKg, 0.3333m, 411.48m);
            await _lines.InsertAsync(line, autoSave: true);

            var prices = await (await _cells.GetQueryableAsync()).AsNoTracking().Where(c => c.PriceListBracketId == bracketId).OrderBy(c => c.ColumnPosition).Select(c => c.UnitPrice).ToListAsync();
            prices.ShouldBe([12345678901234.1234m, 0.3m]);
            var stored = await (await _lines.GetQueryableAsync()).AsNoTracking().SingleAsync(l => l.Id == line.Id);
            (stored.Quantity, stored.UnitPrice, stored.Amount).ShouldBe((1234.5678m, 0.3333m, 411.48m));

            /* A hard delete of the list row takes the ladder with it (FK cascade); the rate line keeps its provenance ids. */
            var context = await GetRequiredService<IDbContextProvider<FinancialDbContext>>().GetDbContextAsync();
            await context.Database.ExecuteSqlRawAsync("DELETE FROM financial.\"TariffsPriceLists\" WHERE \"Id\" = {0}", list.Id);
            (await _rules.CountAsync(r => r.PriceListId == list.Id)).ShouldBe(0);
            (await _cells.CountAsync(c => c.PriceListBracketId == bracketId)).ShouldBe(0);
            (await _lines.CountAsync(l => l.Id == line.Id)).ShouldBe(1);
        });
    }
}
