using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Tariffs;
using OpenTms.MasterData.Relations;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Financial;

/// <summary>
/// PriceListAppService on SQLite (design 57). Risk class: High for copy, adjust with every rounding and the xlsx round
/// trip; Critical for the column/bracket rules, the in-use refusal and tenant isolation of lists, rules and cells.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class PriceListAppServiceTests : FinancialAppServiceTestBase
{
    private readonly IPriceListAppService _service;
    private readonly IRelationAppService _relations;

    public PriceListAppServiceTests()
    {
        _service = GetRequiredService<IPriceListAppService>();
        _relations = GetRequiredService<IRelationAppService>();
    }

    private async Task<Guid> RelationAsync(string code)
        => (await _relations.CreateAsync(new RelationCreateDto { Code = code, Name = code + " BV", Roles = RelationRoles.Customer })).Id;

    private Task<PriceListDto> CreateListAsync(string code = "NLBE", RateSide side = RateSide.Revenue, string currency = "EUR", QuantitySource basis = QuantitySource.LineQuantity, bool archived = false, params decimal?[] columns)
        => _service.CreateAsync(new PriceListCreateDto
        {
            Code = code,
            Name = code + " tariff",
            Side = side,
            CurrencyCode = currency,
            Basis = basis,
            ValidFrom = new DateOnly(2026, 1, 1),
            IsArchived = archived,
            Columns = columns.ToList()
        });

    private static PriceListRuleInputDto RuleInput(Guid? from = null, Guid? fromRegion = null, Guid? to = null, Guid? toRegion = null, Guid? unit = null, Guid? product = null, params PriceListBracketInputDto[] brackets)
        => new()
        {
            FromRelationId = from,
            FromRegionId = fromRegion,
            ToRelationId = to,
            ToRegionId = toRegion,
            TransportUnitId = unit,
            ProductId = product,
            RowQuantity = brackets.Length > 1 ? QuantitySource.WeightKg : QuantitySource.Manual,
            Brackets = brackets.Length == 0 ? [new PriceListBracketInputDto { UpperBound = null, UnitPrices = [10m] }] : brackets.ToList()
        };

    private static PriceListBracketInputDto Bracket(decimal? upperBound, params decimal?[] prices) => new() { UpperBound = upperBound, UnitPrices = prices.ToList() };

    private static PriceListUpdateDto UpdateFrom(PriceListDto dto) => new()
    {
        Code = dto.Code,
        Name = dto.Name,
        Description = dto.Description,
        Side = dto.Side,
        CurrencyCode = dto.CurrencyCode,
        Basis = dto.Basis,
        Multiplier = dto.Multiplier,
        ColumnQuantity = dto.ColumnQuantity,
        StopMatching = dto.StopMatching,
        UnitConversionFactor = dto.UnitConversionFactor,
        ValidFrom = dto.ValidFrom,
        ValidTill = dto.ValidTill,
        IsArchived = dto.IsArchived,
        Columns = dto.Columns.Select(c => c.UpperBound).ToList(),
        ConcurrencyStamp = dto.ConcurrencyStamp
    };

    [Fact]
    public async Task Create_stores_the_header_and_columns_and_refuses_bad_columns_codes_and_currencies()
    {
        using var _ = InTenantA();
        await CurrencyAsync("EUR");
        await CurrencyAsync("USD", active: false);

        var created = await CreateListAsync("nl-be", columns: [100m, 500m, null]);
        created.Code.ShouldBe("NL-BE");
        created.Columns.Select(c => (c.Position, c.UpperBound)).ShouldBe([(1, 100m), (2, 500m), (3, (decimal?)null)]);
        created.RuleCount.ShouldBe(0);
        created.UnitConversionFactor.ShouldBe(1m);

        var noColumns = await CreateListAsync("ONE");
        noColumns.Columns.ShouldHaveSingleItem().UpperBound.ShouldBeNull("no columns given = one open column");

        (await Should.ThrowAsync<BusinessException>(() => CreateListAsync("DUP", columns: [500m, 100m, null]))).Code.ShouldBe(FinancialErrorCodes.ColumnsNotAscending);
        (await Should.ThrowAsync<BusinessException>(() => CreateListAsync("DUP", columns: [100m, 500m]))).Code.ShouldBe(FinancialErrorCodes.OpenRowRequired);
        (await Should.ThrowAsync<BusinessException>(() => CreateListAsync("NL-BE"))).Code.ShouldBe(ReferenceDataErrorCodes.DuplicateKey);
        (await Should.ThrowAsync<BusinessException>(() => CreateListAsync("USD", currency: "USD"))).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        (await Should.ThrowAsync<BusinessException>(() => CreateListAsync("GBP", currency: "GBP"))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => CreateListAsync("MAN", basis: QuantitySource.Manual))).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
    }

    [Fact]
    public async Task List_filters_by_side_hides_archived_rows_searches_sorts_and_counts_rules_and_the_lookup_takes_a_side()
    {
        using var _ = InTenantA();
        await CurrencyAsync("EUR");
        var revenue = await CreateListAsync("REV");
        await CreateListAsync("COST", RateSide.Cost);
        await CreateListAsync("OLD", archived: true);
        var ams = await RelationAsync("AMS");
        await _service.CreateRuleAsync(revenue.Id, RuleInput(from: ams));
        await _service.CreateRuleAsync(revenue.Id, RuleInput(to: ams));

        var all = await _service.GetListAsync(new PriceListListRequestDto { StartRow = 0, EndRow = 100 });
        all.Rows.Select(r => r.Code).ShouldBe(["COST", "REV"]);
        all.Rows.Single(r => r.Code == "REV").RuleCount.ShouldBe(2);
        all.TotalCount.ShouldBe(2);

        (await _service.GetListAsync(new PriceListListRequestDto { StartRow = 0, EndRow = 100, Side = RateSide.Cost })).Rows.Select(r => r.Code).ShouldBe(["COST"]);
        (await _service.GetListAsync(new PriceListListRequestDto { StartRow = 0, EndRow = 100, IncludeArchived = true })).Rows.Count.ShouldBe(3);
        var searched = await _service.GetListAsync(new PriceListListRequestDto { StartRow = 0, EndRow = 100, WildcardSearch = "rev", SortBy = "name", SortDir = "desc" });
        searched.Rows.Select(r => r.Code).ShouldBe(["REV"]);
        searched.FilteredCount.ShouldBe(1);
        (await Should.ThrowAsync<BusinessException>(() => _service.GetListAsync(new PriceListListRequestDto { StartRow = 0, EndRow = 100, SortBy = "rulecount" }))).Code.ShouldBe(ReferenceDataErrorCodes.UnknownSortColumn);

        (await _service.GetLookupAsync(RateSide.Revenue)).Select(l => l.Code).ShouldBe(["REV"], "archived lists stay out of pickers");
        (await _service.GetLookupAsync(null, "co")).Select(l => l.Code).ShouldBe(["COST"]);
    }

    [Fact]
    public async Task Rules_are_created_paged_filtered_by_tier_updated_and_deleted_with_positions_renumbered()
    {
        using var _ = InTenantA();
        await CurrencyAsync("EUR");
        var list = await CreateListAsync(columns: [100m, null]);
        var ams = await RelationAsync("AMS");
        var bru = await RelationAsync("BRU");
        var be = await RegionAsync("Belgium");
        var pallet = Guid.NewGuid();

        var laneOnly = await _service.CreateRuleAsync(list.Id, RuleInput(from: ams, toRegion: be, brackets: [Bracket(50m, 12m, 11m), Bracket(null, 20m, null)]));
        var byUnit = await _service.CreateRuleAsync(list.Id, RuleInput(from: ams, to: bru, unit: pallet, brackets: [Bracket(null, 9m, 8m)]));
        var third = await _service.CreateRuleAsync(list.Id, RuleInput(fromRegion: be, brackets: [Bracket(null, 30m, 30m)]));

        laneOnly.Position.ShouldBe(1);
        laneOnly.Tier.ShouldBe(0);
        laneOnly.FromRelationName.ShouldBe("AMS BV");
        laneOnly.ToRegionName.ShouldBe("Belgium");
        laneOnly.RowQuantity.ShouldBe(QuantitySource.WeightKg);
        laneOnly.Brackets.Select(b => (b.Position, b.UpperBound)).ShouldBe([(1, 50m), (2, (decimal?)null)]);
        laneOnly.Brackets[1].UnitPrices.ShouldBe([20m, null]);
        byUnit.Tier.ShouldBe(1);
        byUnit.Position.ShouldBe(2);

        var page = await _service.GetRulesAsync(list.Id, new PriceListRuleListRequestDto { StartRow = 0, EndRow = 2 });
        page.TotalCount.ShouldBe(3);
        page.Rows.Select(r => r.Position).ShouldBe([1, 2]);
        page.Rows[0].BracketCount.ShouldBe(2);
        (await _service.GetRulesAsync(list.Id, new PriceListRuleListRequestDto { StartRow = 0, EndRow = 100, Tier = 1 })).Rows.Select(r => r.Id).ShouldBe([byUnit.Id]);
        (await _service.GetRulesAsync(list.Id, new PriceListRuleListRequestDto { StartRow = 0, EndRow = 100, WildcardSearch = bru.ToString() })).Rows.Select(r => r.Id).ShouldBe([byUnit.Id]);
        (await _service.GetRulesAsync(list.Id, new PriceListRuleListRequestDto { StartRow = 0, EndRow = 100, SortBy = "tier", SortDir = "desc" })).Rows.Select(r => r.Position).ShouldBe([2, 1, 3]);
        (await _service.GetAsync(list.Id)).RuleCount.ShouldBe(3);

        var updated = await _service.UpdateRuleAsync(list.Id, laneOnly.Id, RuleInput(from: ams, to: bru, brackets: [Bracket(null, 7m, 6m)]));
        updated.ToRelationId.ShouldBe(bru);
        updated.ToRegionId.ShouldBeNull();
        updated.RowQuantity.ShouldBe(QuantitySource.Manual);
        updated.Brackets.ShouldHaveSingleItem().UnitPrices.ShouldBe([7m, 6m]);
        (await _service.GetRuleAsync(list.Id, laneOnly.Id)).Brackets.Single().Id.ShouldBe(laneOnly.Brackets[0].Id, "the first bracket keeps its id");

        await _service.DeleteRuleAsync(list.Id, laneOnly.Id);
        (await _service.GetRulesAsync(list.Id, new PriceListRuleListRequestDto { StartRow = 0, EndRow = 100 })).Rows.Select(r => (r.Id, r.Position)).ShouldBe([(byUnit.Id, 1), (third.Id, 2)]);
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetRuleAsync(list.Id, laneOnly.Id));

        /* Validation through the entity and the Master Data lookup. */
        (await Should.ThrowAsync<BusinessException>(() => _service.CreateRuleAsync(list.Id, RuleInput()))).Code.ShouldBe(FinancialErrorCodes.LaneRequired);
        (await Should.ThrowAsync<BusinessException>(() => _service.CreateRuleAsync(list.Id, RuleInput(from: ams, fromRegion: be)))).Code.ShouldBe(FinancialErrorCodes.RelationAndRegionOnOneSide);
        (await Should.ThrowAsync<BusinessException>(() => _service.CreateRuleAsync(list.Id, RuleInput(from: Guid.NewGuid(), brackets: [Bracket(null, 1m, 1m)])))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => _service.CreateRuleAsync(list.Id, RuleInput(fromRegion: Guid.NewGuid(), brackets: [Bracket(null, 1m, 1m)])))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await _service.GetAsync(list.Id)).ConcurrencyStamp.ShouldBe(list.ConcurrencyStamp, "rule edits never touch the header's stamp");
        (await Should.ThrowAsync<BusinessException>(() => _service.CreateRuleAsync(list.Id, RuleInput(from: ams, brackets: [Bracket(null, 1m)])))).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        (await Should.ThrowAsync<BusinessException>(() => _service.CreateRuleAsync(list.Id, RuleInput(from: ams, brackets: [Bracket(100m, 1m, 1m), Bracket(50m, 1m, 1m), Bracket(null, 1m, 1m)])))).Code.ShouldBe(FinancialErrorCodes.BracketsNotAscending);
    }

    [Fact]
    public async Task Changing_the_columns_resizes_every_rules_cells()
    {
        using var _ = InTenantA();
        await CurrencyAsync("EUR");
        var list = await CreateListAsync(columns: [100m, null]);
        var ams = await RelationAsync("AMS");
        var rule = await _service.CreateRuleAsync(list.Id, RuleInput(from: ams, brackets: [Bracket(null, 9m, 8m)]));

        var update = UpdateFrom(list);
        update.Columns = [100m, 500m, null];
        var widened = await _service.UpdateAsync(list.Id, update);
        widened.Columns.Count.ShouldBe(3);
        (await _service.GetRuleAsync(list.Id, rule.Id)).Brackets.Single().UnitPrices.ShouldBe([9m, 8m, null]);

        update = UpdateFrom(widened);
        update.Columns = [null];
        await _service.UpdateAsync(list.Id, update);
        (await _service.GetRuleAsync(list.Id, rule.Id)).Brackets.Single().UnitPrices.ShouldBe([9m]);
    }

    [Fact]
    public async Task Copy_duplicates_the_header_the_columns_and_every_rule_under_a_new_code_and_validity()
    {
        using var _ = InTenantA();
        await CurrencyAsync("EUR");
        var source = await CreateListAsync(columns: [100m, null]);
        var ams = await RelationAsync("AMS");
        await _service.CreateRuleAsync(source.Id, RuleInput(from: ams, brackets: [Bracket(50m, 12m, 11m), Bracket(null, 20m, 19m)]));
        await _service.CreateRuleAsync(source.Id, RuleInput(to: ams, brackets: [Bracket(null, 5m, 4m)]));

        var copy = await _service.CopyAsync(source.Id, new PriceListCopyDto { Code = "NLBE27", Name = "2027", ValidFrom = new DateOnly(2027, 1, 1), ValidTill = new DateOnly(2027, 12, 31) });

        copy.Id.ShouldNotBe(source.Id);
        copy.Code.ShouldBe("NLBE27");
        copy.ValidTill.ShouldBe(new DateOnly(2027, 12, 31));
        copy.Columns.Select(c => c.UpperBound).ShouldBe([100m, null]);
        copy.RuleCount.ShouldBe(2);
        var rules = (await _service.GetRulesAsync(copy.Id, new PriceListRuleListRequestDto { StartRow = 0, EndRow = 100 })).Rows;
        var first = await _service.GetRuleAsync(copy.Id, rules[0].Id);
        first.FromRelationId.ShouldBe(ams);
        first.Brackets.Select(b => b.UnitPrices).ShouldBe([new List<decimal?> { 12m, 11m }, new List<decimal?> { 20m, 19m }]);
        (await _service.GetAsync(source.Id)).RuleCount.ShouldBe(2, "the source is untouched");
        (await Should.ThrowAsync<BusinessException>(() => _service.CopyAsync(source.Id, new PriceListCopyDto { Code = "NLBE27", Name = "x", ValidFrom = new DateOnly(2027, 1, 1) }))).Code.ShouldBe(ReferenceDataErrorCodes.DuplicateKey);
    }

    [Theory]
    [InlineData(PriceRounding.None, 10.3333, 10.8500)]      // 10.3333 × 1.05 = 10.849965 → 4 decimals
    [InlineData(PriceRounding.Cent, 10.3333, 10.85)]
    [InlineData(PriceRounding.FiveCents, 10.3333, 10.85)]
    [InlineData(PriceRounding.Quarter, 10.3333, 10.75)]
    [InlineData(PriceRounding.Half, 10.3333, 11.0)]
    [InlineData(PriceRounding.Whole, 10.3333, 11.0)]
    public void Adjusted_rounds_per_kind(PriceRounding rounding, decimal before, decimal after)
    {
        PriceListAppService.Adjusted(before, new PriceListAdjustDto { Percentage = 5m, Rounding = rounding }).ShouldBe(after);
    }

    [Fact]
    public void Adjusted_applies_an_absolute_amount_and_never_goes_below_zero()
    {
        PriceListAppService.Adjusted(10m, new PriceListAdjustDto { Amount = -2.5m }).ShouldBe(7.5m);
        PriceListAppService.Adjusted(1m, new PriceListAdjustDto { Amount = -2.5m }).ShouldBe(0m);
        PriceListAppService.Adjusted(10m, new PriceListAdjustDto { Percentage = -10m, Rounding = PriceRounding.Cent }).ShouldBe(9m);
    }

    [Fact]
    public async Task Adjust_previews_then_applies_and_refuses_an_empty_or_double_adjustment()
    {
        using var _ = InTenantA();
        await CurrencyAsync("EUR");
        var list = await CreateListAsync(columns: [100m, null]);
        var ams = await RelationAsync("AMS");
        var rule = await _service.CreateRuleAsync(list.Id, RuleInput(from: ams, brackets: [Bracket(50m, 12m, null), Bracket(null, 20m, 19m)]));

        var preview = await _service.AdjustAsync(list.Id, new PriceListAdjustDto { Percentage = 10m, Rounding = PriceRounding.Cent, Preview = true });
        preview.ChangedCellCount.ShouldBe(3, "the empty cell is not a price");
        preview.Sample.Select(s => (s.BracketPosition, s.ColumnPosition, s.Before, s.After)).ShouldBe([(1, 1, 12m, 13.2m), (2, 1, 20m, 22m), (2, 2, 19m, 20.9m)]);
        (await _service.GetRuleAsync(list.Id, rule.Id)).Brackets[0].UnitPrices[0].ShouldBe(12m, "a preview saves nothing");

        var applied = await _service.AdjustAsync(list.Id, new PriceListAdjustDto { Percentage = 10m, Rounding = PriceRounding.Half });
        applied.ChangedCellCount.ShouldBe(3);
        applied.Sample.ShouldBeEmpty();
        var after = await _service.GetRuleAsync(list.Id, rule.Id);
        after.Brackets[0].UnitPrices.ShouldBe([13m, null]);
        after.Brackets[1].UnitPrices.ShouldBe([22m, 21m]);

        (await Should.ThrowAsync<BusinessException>(() => _service.AdjustAsync(list.Id, new PriceListAdjustDto()))).Code.ShouldBe(FinancialErrorCodes.AdjustmentRequired);
        (await Should.ThrowAsync<BusinessException>(() => _service.AdjustAsync(list.Id, new PriceListAdjustDto { Percentage = 1m, Amount = 1m }))).Code.ShouldBe(FinancialErrorCodes.AdjustmentRequired);
    }

    [Fact]
    public async Task Simulate_returns_the_winning_rule_the_ranked_candidates_and_the_price()
    {
        using var _ = InTenantA();
        await CurrencyAsync("EUR");
        var list = await CreateListAsync(columns: [100m, 500m, null]);
        var ams = await RelationAsync("AMS");
        var bru = await RelationAsync("BRU");
        var be = await RegionAsync("Belgium");
        var pallet = Guid.NewGuid();
        var region = await _service.CreateRuleAsync(list.Id, RuleInput(from: ams, toRegion: be, brackets: [Bracket(null, 30m, 25m, 20m)]));
        var exact = await _service.CreateRuleAsync(list.Id, RuleInput(from: ams, to: bru, brackets: [Bracket(null, 28m, 23m, 18m)]));
        var byUnit = await _service.CreateRuleAsync(list.Id, RuleInput(from: ams, to: bru, unit: pallet, brackets: [Bracket(null, 26m, 21m, 16m)]));
        var update = UpdateFrom(await _service.GetAsync(list.Id));
        update.ColumnQuantity = ColumnQuantitySource.LineWeightKg;
        await _service.UpdateAsync(list.Id, update);

        var result = await _service.SimulateAsync(list.Id, new PriceListSimulateDto { FromRelationId = ams, ToRelationId = bru, ToRegionIds = [be], TransportUnitId = Guid.NewGuid(), Quantity = 3m, WeightKg = 350m, AsOf = new DateOnly(2026, 6, 1) });

        result.WinningRuleId.ShouldBe(exact.Id);
        result.Candidates.Select(c => (c.RuleId, c.Reason)).ShouldBe([(exact.Id, "Winner"), (region.Id, "Candidate"), (byUnit.Id, "Dimensions")]);
        result.ColumnPosition.ShouldBe(2);
        result.UnitPrice.ShouldBe(23m);
        result.Quantity.ShouldBe(3m);
        result.Amount.ShouldBe(69m);
        result.IsValidOnAsOf.ShouldBeTrue();

        var before = await _service.SimulateAsync(list.Id, new PriceListSimulateDto { FromRelationId = ams, ToRelationId = bru, TransportUnitId = pallet, AsOf = new DateOnly(2025, 1, 1) });
        before.WinningRuleId.ShouldBe(byUnit.Id);
        before.IsValidOnAsOf.ShouldBeFalse();
    }

    [Fact]
    public async Task Export_and_import_round_trip_the_rules_and_the_import_replaces_and_validates()
    {
        using var _ = InTenantA();
        await CurrencyAsync("EUR");
        var list = await CreateListAsync(columns: [100m, null]);
        var ams = await RelationAsync("AMS");
        var be = await RegionAsync("Belgium");
        await _service.CreateRuleAsync(list.Id, RuleInput(from: ams, toRegion: be, brackets: [Bracket(50m, 12.5m, null), Bracket(null, 20m, 19.1234m)]));
        await _service.CreateRuleAsync(list.Id, RuleInput(fromRegion: be, unit: Guid.NewGuid(), brackets: [Bracket(null, 5m, 4m)]));
        var target = await CreateListAsync("TARGET", columns: [100m, null]);
        await _service.CreateRuleAsync(target.Id, RuleInput(from: ams, brackets: [Bracket(null, 1m, 1m)]));

        var export = await _service.ExportAsync(list.Id);
        export.FileName.ShouldBe("NLBE.xlsx");
        using var buffer = new MemoryStream();
        await export.GetStream().CopyToAsync(buffer);
        buffer.Length.ShouldBeGreaterThan(0);

        var result = await _service.ImportAsync(target.Id, new RemoteStreamContent(new MemoryStream(buffer.ToArray()), "rules.xlsx"));
        result.RuleCount.ShouldBe(2);
        result.BracketCount.ShouldBe(3);
        result.CellCount.ShouldBe(6);

        var sourceRules = (await _service.GetRulesAsync(list.Id, new PriceListRuleListRequestDto { StartRow = 0, EndRow = 100 })).Rows;
        var targetRules = (await _service.GetRulesAsync(target.Id, new PriceListRuleListRequestDto { StartRow = 0, EndRow = 100 })).Rows;
        targetRules.Count.ShouldBe(2, "the import replaced the target's single rule");
        for (var i = 0; i < 2; i++)
        {
            var expected = await _service.GetRuleAsync(list.Id, sourceRules[i].Id);
            var actual = await _service.GetRuleAsync(target.Id, targetRules[i].Id);
            (actual.Position, actual.FromRelationId, actual.FromRegionId, actual.ToRegionId, actual.TransportUnitId, actual.RowQuantity)
                .ShouldBe((expected.Position, expected.FromRelationId, expected.FromRegionId, expected.ToRegionId, expected.TransportUnitId, expected.RowQuantity));
            actual.Brackets.Select(b => b.UpperBound).ShouldBe(expected.Brackets.Select(b => b.UpperBound));
            actual.Brackets.SelectMany(b => b.UnitPrices).ShouldBe(expected.Brackets.SelectMany(b => b.UnitPrices));
        }

        /* A list with another column count cannot take the file; garbage is refused with a position. */
        var narrow = await CreateListAsync("NARROW");
        var mismatch = await Should.ThrowAsync<BusinessException>(() => _service.ImportAsync(narrow.Id, new RemoteStreamContent(new MemoryStream(buffer.ToArray()), "rules.xlsx")));
        mismatch.Code.ShouldBe(FinancialErrorCodes.ImportInvalid);
        mismatch.Data["row"].ShouldBe(1);
        (await Should.ThrowAsync<BusinessException>(() => _service.ImportAsync(target.Id, new RemoteStreamContent(new MemoryStream([1, 2, 3]), "x.xlsx")))).Code.ShouldBe(FinancialErrorCodes.ImportInvalid);
    }

    [Fact]
    public async Task Delete_is_refused_while_a_relations_settings_refer_to_the_list_and_bulk_delete_skips_it()
    {
        using var _ = InTenantA();
        await CurrencyAsync("EUR");
        var used = await CreateListAsync("USED");
        var free = await CreateListAsync("FREE");
        var ams = await RelationAsync("AMS");
        await GetRequiredService<IRelationFinancialSettingsAppService>().UpsertByRelationAsync(ams, new RelationFinancialSettingsUpsertDto { RevenuePriceListIds = [used.Id] });

        (await Should.ThrowAsync<BusinessException>(() => _service.DeleteAsync(used.Id))).Code.ShouldBe(FinancialErrorCodes.PriceListInUse);

        var result = await _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = Explicit(used.Id, free.Id) });
        result.DeletedCount.ShouldBe(1);
        result.SkippedRows.ShouldHaveSingleItem().Id.ShouldBe(used.Id);
        (await _service.GetListAsync(new PriceListListRequestDto { StartRow = 0, EndRow = 100 })).Rows.Select(r => r.Code).ShouldBe(["USED"]);

        /* Archiving is the way out. */
        var update = UpdateFrom(await _service.GetAsync(used.Id));
        update.IsArchived = true;
        (await _service.UpdateAsync(used.Id, update)).IsArchived.ShouldBeTrue();
    }

    [Fact]
    public async Task A_stale_stamp_is_a_conflict_and_another_tenant_sees_nothing()
    {
        Guid listId;
        Guid ruleId;
        using (InTenantA())
        {
            await CurrencyAsync("EUR");
            var list = await CreateListAsync();
            listId = list.Id;
            var ams = await RelationAsync("AMS");
            ruleId = (await _service.CreateRuleAsync(listId, RuleInput(from: ams))).Id;

            var stale = UpdateFrom(list);
            stale.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            await Should.ThrowAsync<Volo.Abp.Data.AbpDbConcurrencyException>(() => _service.UpdateAsync(listId, stale));
        }

        using (InTenantB())
        {
            (await _service.GetListAsync(new PriceListListRequestDto { StartRow = 0, EndRow = 100 })).Rows.ShouldBeEmpty();
            (await _service.GetLookupAsync()).ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(listId));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetRulesAsync(listId, new PriceListRuleListRequestDto { StartRow = 0, EndRow = 100 }));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetRuleAsync(listId, ruleId));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.SimulateAsync(listId, new PriceListSimulateDto()));
        }
    }
}
