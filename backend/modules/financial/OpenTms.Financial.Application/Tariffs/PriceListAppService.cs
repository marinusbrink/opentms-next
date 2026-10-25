using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Localization;
using OpenTms.Financial.Permissions;
using OpenTms.Financial.Tariffs.Calculators;
using OpenTms.Financial.Tariffs.Pricing;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Financial.Tariffs;

/// <summary>
/// Design 57 "PriceListAppService": the design-50 list shape for the header and columns, the rules paged and edited
/// row by row (a list holds up to 10,000), copy, adjust (with preview and rounding), simulate (the cascade trace) and
/// the xlsx export and import. Deleting a list a relation or a rate line refers to is refused (archive it).
/// </summary>
[RequiresFeature(AppFeatures.MasterData.Default)]
public class PriceListAppService
    : ReferenceDataCrudAppServiceBase<PriceList, PriceListRowDto, PriceListDto, PriceListCreateDto, PriceListUpdateDto, PriceListListRequestDto>,
      IPriceListAppService
{
    private const int AdjustSampleSize = 20;

    private readonly IRepository<PriceListRule, Guid> _rules;
    private readonly IRepository<PriceListCell> _cells;
    private readonly IRepository<RelationPriceList, Guid> _relationPriceLists;
    private readonly IRepository<RateLine, Guid> _rateLines;
    private readonly MasterDataReferences _masterData;

    public PriceListAppService(
        IRepository<PriceList, Guid> repository,
        IRepository<PriceListRule, Guid> rules,
        IRepository<PriceListCell> cells,
        IRepository<RelationPriceList, Guid> relationPriceLists,
        IRepository<RateLine, Guid> rateLines,
        MasterDataReferences masterData)
        : base(repository)
    {
        _rules = rules;
        _cells = cells;
        _relationPriceLists = relationPriceLists;
        _rateLines = rateLines;
        _masterData = masterData;
        LocalizationResource = typeof(FinancialResource);
    }

    // ── the design-50 list ─────────────────────────────────────────────────────

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.PriceLists, RequiresAll = true)]
    [Authorize(FinancialPermissions.PriceLists.Default)]
    public Task<GridResponse<PriceListRowDto>> GetListAsync(PriceListListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.PriceLists, RequiresAll = true)]
    [Authorize(FinancialPermissions.PriceLists.Default)]
    public Task<PriceListDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(FinancialPermissions.PriceLists.Default)]
    public async Task<List<ReferenceDataLookupDto>> GetLookupAsync(RateSide? side = null, string? term = null, int take = ReferenceDataConsts.MaxLookupTake)
    {
        var all = await Repository.GetQueryableAsync();
        var query = ApplyLookupFilter(all);
        if (side != null)
        {
            query = query.Where(p => p.Side == side.Value);
        }

        var normalized = NormalizeTerm(term);
        if (normalized != null)
        {
            query = ApplySearch(query, normalized);
        }

        var entities = await AsyncExecuter.ToListAsync(ApplySort(query, null, false).Take(Math.Clamp(take, 1, ReferenceDataConsts.MaxLookupTake)));
        return entities.Select(ToLookup).ToList();
    }

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.PriceLists, RequiresAll = true)]
    [Authorize(FinancialPermissions.PriceLists.Create)]
    public Task<PriceListDto> CreateAsync(PriceListCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.PriceLists, RequiresAll = true)]
    [Authorize(FinancialPermissions.PriceLists.Update)]
    public Task<PriceListDto> UpdateAsync(Guid id, PriceListUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.PriceLists, RequiresAll = true)]
    [Authorize(FinancialPermissions.PriceLists.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        var list = await Repository.GetAsync(id);
        if (await GetDeleteBlockerAsync(list) != null)
        {
            throw new BusinessException(FinancialErrorCodes.PriceListInUse).WithData("code", list.Code);
        }

        await DeleteInternalAsync(id);
    }

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.PriceLists, RequiresAll = true)]
    [Authorize(FinancialPermissions.PriceLists.Delete)]
    public Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => input.IncludeArchived ? q : q.Where(p => !p.IsArchived));

    protected override IQueryable<PriceList> ApplyFilters(IQueryable<PriceList> query, PriceListListRequestDto request)
    {
        if (!request.IncludeArchived)
        {
            query = query.Where(p => !p.IsArchived);
        }

        return request.Side == null ? query : query.Where(p => p.Side == request.Side.Value);
    }

    protected override IQueryable<PriceList> ApplySearch(IQueryable<PriceList> query, string term)
        => query.Where(p => p.Code.ToLower().Contains(term) || p.Name.ToLower().Contains(term));

    protected override IOrderedQueryable<PriceList> ApplySort(IQueryable<PriceList> query, string? sortBy, bool descending)
        => (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "code" => Order(query, p => p.Code, descending),
            "name" => Order(query, p => p.Name, descending),
            "side" => Order(query, p => p.Side, descending),
            "currencycode" => Order(query, p => p.CurrencyCode, descending),
            "basis" => Order(query, p => p.Basis, descending),
            "validfrom" => Order(query, p => p.ValidFrom, descending),
            "validtill" => Order(query, p => p.ValidTill, descending),
            "isarchived" => Order(query, p => p.IsArchived, descending),
            _ => throw UnknownSortColumn(sortBy)
        };

    protected override IQueryable<PriceList> ApplyLookupFilter(IQueryable<PriceList> query) => query.Where(p => !p.IsArchived);

    protected override async Task<List<PriceListRowDto>> ToRowsAsync(List<PriceList> entities)
    {
        var counts = await RuleCountsAsync(entities.Select(p => p.Id).ToList());
        return entities.Select(p => new PriceListRowDto
        {
            Id = p.Id,
            Code = p.Code,
            Name = p.Name,
            Side = p.Side,
            CurrencyCode = p.CurrencyCode,
            Basis = p.Basis,
            ValidFrom = p.ValidFrom,
            ValidTill = p.ValidTill,
            RuleCount = counts.GetValueOrDefault(p.Id),
            IsArchived = p.IsArchived,
            Description = p.Description
        }).ToList();
    }

    protected override async Task<PriceListDto> ToDtoAsync(PriceList p)
    {
        var dto = new PriceListDto
        {
            Code = p.Code,
            Name = p.Name,
            Side = p.Side,
            CurrencyCode = p.CurrencyCode,
            Basis = p.Basis,
            Multiplier = p.Multiplier,
            ColumnQuantity = p.ColumnQuantity,
            StopMatching = p.StopMatching,
            UnitConversionFactor = p.UnitConversionFactor,
            ValidFrom = p.ValidFrom,
            ValidTill = p.ValidTill,
            IsArchived = p.IsArchived,
            Columns = p.Columns.OrderBy(c => c.Position).Select(c => new PriceListColumnDto { Position = c.Position, UpperBound = c.UpperBound }).ToList(),
            RuleCount = (await RuleCountsAsync([p.Id])).GetValueOrDefault(p.Id)
        };
        MapAudit(p, dto);
        return dto;
    }

    protected override ReferenceDataLookupDto ToLookup(PriceList p) => new() { Id = p.Id, Name = p.Name, Code = p.Code };

    protected override string DisplayNameOf(PriceList p) => p.Name;

    protected override async Task<PriceList> CreateEntityAsync(PriceListCreateDto input)
    {
        var code = ReferenceDataAggregateRoot.NormalizeCode(input.Code);
        await EnsureUniqueAsync(p => p.Code == code, "code", code);
        var currency = input.CurrencyCode.Trim().ToUpperInvariant();
        await _masterData.EnsureCurrenciesActiveAsync([currency]);

        var list = new PriceList(GuidGenerator.Create(), CurrentTenant.Id, code, input.Name, input.Side, currency, input.Basis, input.ValidFrom);
        Apply(list, input);
        return list;
    }

    protected override async Task ApplyUpdateAsync(PriceList list, PriceListUpdateDto input)
    {
        var code = ReferenceDataAggregateRoot.NormalizeCode(input.Code);
        if (code != list.Code)
        {
            await EnsureUniqueAsync(p => p.Code == code && p.Id != list.Id, "code", code);
            list.SetCode(code);
        }

        var currency = input.CurrencyCode.Trim().ToUpperInvariant();
        if (currency != list.CurrencyCode)
        {
            await _masterData.EnsureCurrenciesActiveAsync([currency]);
            list.SetCurrencyCode(currency);
        }

        list.SetSide(input.Side);
        list.SetBasis(input.Basis);
        var columnsBefore = list.Columns.Count;
        Apply(list, input);
        if (list.Columns.Count != columnsBefore)
        {
            await ResizeCellsAsync(list.Id, list.Columns.Count);
        }
    }

    private static void Apply(PriceList list, PriceListCreateDto input)
    {
        list.SetName(input.Name);
        list.SetMultiplier(input.Multiplier);
        list.SetColumnQuantity(input.ColumnQuantity);
        list.SetStopMatching(input.StopMatching);
        list.SetUnitConversionFactor(input.UnitConversionFactor);
        list.SetValidity(input.ValidFrom, input.ValidTill);
        list.SetDescription(input.Description);
        list.IsArchived = input.IsArchived;
        list.SetColumns(input.Columns.Count == 0 ? [null] : input.Columns);
    }

    /// <summary>A list a relation's settings or a rate line refers to cannot be deleted (archive it); bulk delete skips it.</summary>
    protected override async Task<string?> GetDeleteBlockerAsync(PriceList list)
    {
        if (await _relationPriceLists.AnyAsync(r => r.PriceListId == list.Id))
        {
            return L["Financial:PriceListInUseByRelations"];
        }

        if (await _rateLines.AnyAsync(r => r.PriceListId == list.Id))
        {
            return L["Financial:PriceListInUseByRateLines"];
        }

        return null;
    }

    // ── rules ──────────────────────────────────────────────────────────────────

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.PriceLists, RequiresAll = true)]
    [Authorize(FinancialPermissions.PriceLists.Default)]
    public virtual async Task<GridResponse<PriceListRuleRowDto>> GetRulesAsync(Guid id, PriceListRuleListRequestDto input)
    {
        await ColumnsOfAsync(id);
        var all = (await _rules.GetQueryableAsync()).Where(r => r.PriceListId == id);
        var filtered = input.Tier == null ? all : all.Where(r => ((r.TransportUnitId == null ? 0 : 1) + (r.ProductId == null ? 0 : 1) + (r.VehicleTypeId == null ? 0 : 1)) == input.Tier.Value);

        /* The rule's names live in Master Data: the search takes an id (relation, region, unit, product, vehicle type). */
        var searched = filtered;
        if (Guid.TryParse(input.WildcardSearch?.Trim(), out var key))
        {
            searched = filtered.Where(r => r.FromRelationId == key || r.FromRegionId == key || r.ToRelationId == key || r.ToRegionId == key
                || r.TransportUnitId == key || r.ProductId == key || r.VehicleTypeId == key);
        }
        else if (int.TryParse(input.WildcardSearch?.Trim(), out var position))
        {
            searched = filtered.Where(r => r.Position == position);
        }

        var totalCount = await AsyncExecuter.LongCountAsync(filtered);
        var filteredCount = ReferenceEquals(searched, filtered) ? totalCount : await AsyncExecuter.LongCountAsync(searched);
        var sorted = (input.SortBy?.ToLowerInvariant()) switch
        {
            null or "" or "position" => input.Descending ? searched.OrderByDescending(r => r.Position) : searched.OrderBy(r => r.Position),
            "tier" => input.Descending
                ? searched.OrderByDescending(r => (r.TransportUnitId == null ? 0 : 1) + (r.ProductId == null ? 0 : 1) + (r.VehicleTypeId == null ? 0 : 1)).ThenBy(r => r.Position)
                : searched.OrderBy(r => (r.TransportUnitId == null ? 0 : 1) + (r.ProductId == null ? 0 : 1) + (r.VehicleTypeId == null ? 0 : 1)).ThenBy(r => r.Position),
            "rowquantity" => input.Descending ? searched.OrderByDescending(r => r.RowQuantity).ThenBy(r => r.Position) : searched.OrderBy(r => r.RowQuantity).ThenBy(r => r.Position),
            _ => throw UnknownSortColumn(input.SortBy!)
        };

        var page = await AsyncExecuter.ToListAsync(sorted.Skip(input.StartRow).Take(input.EndRow - input.StartRow));
        var pageIds = page.Select(r => r.Id).ToList();
        var bracketCounts = pageIds.Count == 0 ? new Dictionary<Guid, int>() : (await AsyncExecuter.ToListAsync(
                (await _rules.WithDetailsAsync()).Where(r => pageIds.Contains(r.Id)).Select(r => new { r.Id, Count = r.Brackets.Count })))
            .ToDictionary(r => r.Id, r => r.Count);
        var names = await NamesAsync(page);

        return new GridResponse<PriceListRuleRowDto>
        {
            Rows = page.Select(r => ToRow(r, names, bracketCounts.GetValueOrDefault(r.Id))).ToList(),
            TotalCount = totalCount,
            FilteredCount = filteredCount
        };
    }

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.PriceLists, RequiresAll = true)]
    [Authorize(FinancialPermissions.PriceLists.Default)]
    public virtual async Task<PriceListRuleDto> GetRuleAsync(Guid id, Guid ruleId)
    {
        var columns = await ColumnsOfAsync(id);
        return await ToRuleDtoAsync(columns, await RuleAsync(id, ruleId));
    }

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.PriceLists, RequiresAll = true)]
    [Authorize(FinancialPermissions.PriceLists.Update)]
    public virtual async Task<PriceListRuleDto> CreateRuleAsync(Guid id, PriceListRuleInputDto input)
    {
        /* The list is read as a projection, never tracked: a rule edit must not bump the header's concurrency stamp. */
        var columns = await ColumnsOfAsync(id);
        var count = await _rules.CountAsync(r => r.PriceListId == id);
        if (count >= FinancialConsts.MaxPriceListRules)
        {
            throw new BusinessException(FinancialErrorCodes.TooManyRules).WithData("max", FinancialConsts.MaxPriceListRules);
        }

        var rule = new PriceListRule(GuidGenerator.Create(), CurrentTenant.Id, id, count + 1);
        await ApplyRuleAsync(columns.Count, rule, input);
        await _rules.InsertAsync(rule, autoSave: true);
        return await ToRuleDtoAsync(columns, rule);
    }

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.PriceLists, RequiresAll = true)]
    [Authorize(FinancialPermissions.PriceLists.Update)]
    public virtual async Task<PriceListRuleDto> UpdateRuleAsync(Guid id, Guid ruleId, PriceListRuleInputDto input)
    {
        var columns = await ColumnsOfAsync(id);
        var rule = await RuleAsync(id, ruleId);
        await ApplyRuleAsync(columns.Count, rule, input);
        await _rules.UpdateAsync(rule, autoSave: true);
        return await ToRuleDtoAsync(columns, rule);
    }

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.PriceLists, RequiresAll = true)]
    [Authorize(FinancialPermissions.PriceLists.Update)]
    public virtual async Task DeleteRuleAsync(Guid id, Guid ruleId)
    {
        var rule = await RuleAsync(id, ruleId);
        await _rules.DeleteAsync(rule, autoSave: true);

        /* Positions stay 1..n without gaps (design 56 "Positions"). */
        var later = await _rules.GetListAsync(r => r.PriceListId == id && r.Position > rule.Position);
        foreach (var r in later)
        {
            r.Position--;
        }

        if (later.Count > 0)
        {
            await _rules.UpdateManyAsync(later, autoSave: true);
        }
    }

    private async Task<PriceListRule> RuleAsync(Guid listId, Guid ruleId)
    {
        var rule = await _rules.FindAsync(ruleId, includeDetails: true);
        if (rule == null || rule.PriceListId != listId)
        {
            throw new Volo.Abp.Domain.Entities.EntityNotFoundException(typeof(PriceListRule), ruleId);
        }

        return rule;
    }

    /// <summary>The column positions of a list (a projection, so the header is not tracked); EntityNotFound for an unknown or foreign id.</summary>
    private async Task<List<int>> ColumnsOfAsync(Guid id)
    {
        var columns = await AsyncExecuter.FirstOrDefaultAsync((await Repository.GetQueryableAsync())
            .Where(p => p.Id == id)
            .Select(p => p.Columns.OrderBy(c => c.Position).Select(c => c.Position).ToList()));
        return columns ?? throw new Volo.Abp.Domain.Entities.EntityNotFoundException(typeof(PriceList), id);
    }

    private async Task ApplyRuleAsync(int columnCount, PriceListRule rule, PriceListRuleInputDto input)
    {
        rule.SetLane(input.FromRelationId, input.FromRegionId, input.ToRelationId, input.ToRegionId);
        rule.SetDimensions(input.TransportUnitId, input.ProductId, input.VehicleTypeId);
        rule.SetRowQuantity(input.RowQuantity);
        rule.SetMinMax(input.MinAmount, input.MaxAmount);
        rule.SetBrackets(input.Brackets.Select(b => ((decimal?)b.UpperBound, (IReadOnlyList<decimal?>)b.UnitPrices)).ToList(), columnCount, GuidGenerator.Create);

        /* Relations and regions are checked through the Master Data lookup; units, products and vehicle types have no published lookup yet (build note). */
        await _masterData.EnsureRelationsExistAsync(new[] { input.FromRelationId, input.ToRelationId }.Where(x => x != null).Select(x => x!.Value).ToList());
        await _masterData.EnsureRegionsExistAsync(new[] { input.FromRegionId, input.ToRegionId }.Where(x => x != null).Select(x => x!.Value).ToList());
    }

    private async Task<Dictionary<Guid, int>> RuleCountsAsync(List<Guid> listIds)
    {
        if (listIds.Count == 0)
        {
            return [];
        }

        var counts = await AsyncExecuter.ToListAsync((await _rules.GetQueryableAsync())
            .Where(r => listIds.Contains(r.PriceListId))
            .GroupBy(r => r.PriceListId)
            .Select(g => new { Id = g.Key, Count = g.Count() }));
        return counts.ToDictionary(c => c.Id, c => c.Count);
    }

    private async Task<(Dictionary<Guid, string> Relations, Dictionary<Guid, string> Regions)> NamesAsync(IReadOnlyCollection<PriceListRule> rules)
    {
        var relationIds = rules.SelectMany(r => new[] { r.FromRelationId, r.ToRelationId }).Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
        var regionIds = rules.SelectMany(r => new[] { r.FromRegionId, r.ToRegionId }).Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
        return (await _masterData.GetRelationNamesAsync(relationIds), await _masterData.GetRegionNamesAsync(regionIds));
    }

    private static PriceListRuleRowDto ToRow(PriceListRule r, (Dictionary<Guid, string> Relations, Dictionary<Guid, string> Regions) names, int bracketCount) => Fill(new PriceListRuleRowDto(), r, names, bracketCount);

    private static T Fill<T>(T dto, PriceListRule r, (Dictionary<Guid, string> Relations, Dictionary<Guid, string> Regions) names, int bracketCount)
        where T : PriceListRuleRowDto
    {
        dto.Id = r.Id;
        dto.Position = r.Position;
        dto.Tier = r.Tier;
        dto.FromRelationId = r.FromRelationId;
        dto.FromRelationName = r.FromRelationId != null ? names.Relations.GetValueOrDefault(r.FromRelationId.Value) : null;
        dto.FromRegionId = r.FromRegionId;
        dto.FromRegionName = r.FromRegionId != null ? names.Regions.GetValueOrDefault(r.FromRegionId.Value) : null;
        dto.ToRelationId = r.ToRelationId;
        dto.ToRelationName = r.ToRelationId != null ? names.Relations.GetValueOrDefault(r.ToRelationId.Value) : null;
        dto.ToRegionId = r.ToRegionId;
        dto.ToRegionName = r.ToRegionId != null ? names.Regions.GetValueOrDefault(r.ToRegionId.Value) : null;
        dto.TransportUnitId = r.TransportUnitId;
        dto.ProductId = r.ProductId;
        dto.VehicleTypeId = r.VehicleTypeId;
        dto.RowQuantity = r.RowQuantity;
        dto.MinAmount = r.MinAmount;
        dto.MaxAmount = r.MaxAmount;
        dto.BracketCount = bracketCount;
        return dto;
    }

    private async Task<PriceListRuleDto> ToRuleDtoAsync(List<int> columns, PriceListRule rule)
    {
        var names = await NamesAsync([rule]);
        var dto = Fill(new PriceListRuleDto(), rule, names, rule.Brackets.Count);
        dto.Brackets = rule.Brackets.OrderBy(b => b.Position).Select(b => new PriceListBracketDto
        {
            Id = b.Id,
            Position = b.Position,
            UpperBound = b.UpperBound,
            UnitPrices = columns.Select(b.UnitPriceAt).ToList()
        }).ToList();
        return dto;
    }

    /// <summary>After a change of the column count every bracket gets a cell per column: missing ones null, extra ones removed.</summary>
    private async Task ResizeCellsAsync(Guid listId, int columnCount)
    {
        var rules = await AsyncExecuter.ToListAsync((await _rules.WithDetailsAsync()).Where(r => r.PriceListId == listId));
        foreach (var rule in rules)
        {
            rule.SetBrackets(
                rule.Brackets.OrderBy(b => b.Position).Select(b => ((decimal?)b.UpperBound, (IReadOnlyList<decimal?>)Enumerable.Range(1, columnCount).Select(b.UnitPriceAt).ToList())).ToList(),
                columnCount,
                GuidGenerator.Create);
        }

        if (rules.Count > 0)
        {
            await _rules.UpdateManyAsync(rules);
        }
    }

    // ── copy, adjust, simulate ─────────────────────────────────────────────────

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.PriceLists, RequiresAll = true)]
    [Authorize(FinancialPermissions.PriceLists.Create)]
    public virtual async Task<PriceListDto> CopyAsync(Guid id, PriceListCopyDto input)
    {
        var source = await Repository.GetAsync(id, includeDetails: true);
        var code = ReferenceDataAggregateRoot.NormalizeCode(input.Code);
        await EnsureUniqueAsync(p => p.Code == code, "code", code);

        var copy = new PriceList(GuidGenerator.Create(), CurrentTenant.Id, code, input.Name, source.Side, source.CurrencyCode, source.Basis, input.ValidFrom);
        copy.SetMultiplier(source.Multiplier);
        copy.SetColumnQuantity(source.ColumnQuantity);
        copy.SetStopMatching(source.StopMatching);
        copy.SetUnitConversionFactor(source.UnitConversionFactor);
        copy.SetValidity(input.ValidFrom, input.ValidTill);
        copy.SetDescription(source.Description);
        copy.SetColumns(source.Columns.OrderBy(c => c.Position).Select(c => c.UpperBound).ToList());
        await Repository.InsertAsync(copy, autoSave: true);

        var rules = await AsyncExecuter.ToListAsync((await _rules.WithDetailsAsync()).Where(r => r.PriceListId == id).OrderBy(r => r.Position));
        var copies = new List<PriceListRule>();
        foreach (var rule in rules)
        {
            var clone = new PriceListRule(GuidGenerator.Create(), CurrentTenant.Id, copy.Id, rule.Position);
            clone.SetLane(rule.FromRelationId, rule.FromRegionId, rule.ToRelationId, rule.ToRegionId);
            clone.SetDimensions(rule.TransportUnitId, rule.ProductId, rule.VehicleTypeId);
            clone.SetRowQuantity(rule.RowQuantity);
            clone.SetMinMax(rule.MinAmount, rule.MaxAmount);
            clone.SetBrackets(
                rule.Brackets.OrderBy(b => b.Position).Select(b => ((decimal?)b.UpperBound, (IReadOnlyList<decimal?>)Enumerable.Range(1, copy.Columns.Count).Select(b.UnitPriceAt).ToList())).ToList(),
                copy.Columns.Count,
                GuidGenerator.Create);
            copies.Add(clone);
        }

        if (copies.Count > 0)
        {
            await _rules.InsertManyAsync(copies, autoSave: true);
        }

        Logger.LogInformation("Price list {SourceId} copied to {CopyId} with {Rules} rules.", id, copy.Id, copies.Count);
        return await ToDtoAsync(await Repository.GetAsync(copy.Id, includeDetails: true));
    }

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.PriceLists, RequiresAll = true)]
    [Authorize(FinancialPermissions.PriceLists.Adjust)]
    public virtual async Task<PriceListAdjustResultDto> AdjustAsync(Guid id, PriceListAdjustDto input)
    {
        if ((input.Percentage == null) == (input.Amount == null))
        {
            throw new BusinessException(FinancialErrorCodes.AdjustmentRequired);
        }

        FinancialValues.Defined(input.Rounding, "rounding");
        await ColumnsOfAsync(id);

        var rules = await AsyncExecuter.ToListAsync((await _rules.WithDetailsAsync()).Where(r => r.PriceListId == id));
        var result = new PriceListAdjustResultDto();
        foreach (var rule in rules.OrderBy(r => r.Position))
        {
            foreach (var bracket in rule.Brackets.OrderBy(b => b.Position))
            {
                foreach (var cell in bracket.Cells.OrderBy(c => c.ColumnPosition))
                {
                    if (cell.UnitPrice == null)
                    {
                        continue;
                    }

                    var adjusted = Adjusted(cell.UnitPrice.Value, input);
                    if (adjusted == cell.UnitPrice.Value)
                    {
                        continue;
                    }

                    result.ChangedCellCount++;
                    if (input.Preview)
                    {
                        if (result.Sample.Count < AdjustSampleSize)
                        {
                            result.Sample.Add(new PriceListAdjustSampleDto { RuleId = rule.Id, BracketPosition = bracket.Position, ColumnPosition = cell.ColumnPosition, Before = cell.UnitPrice.Value, After = adjusted });
                        }
                    }
                    else
                    {
                        cell.SetUnitPrice(adjusted);
                    }
                }
            }
        }

        if (!input.Preview && result.ChangedCellCount > 0)
        {
            await _rules.UpdateManyAsync(rules, autoSave: true);
            Logger.LogInformation("Price list {Id} adjusted: {Cells} cells changed.", id, result.ChangedCellCount);
        }

        return result;
    }

    /// <summary>The adjusted unit price: relative or absolute, never below zero, rounded per kind (cent, 0.05, 0.25, 0.50, whole).</summary>
    public static decimal Adjusted(decimal unitPrice, PriceListAdjustDto input)
    {
        var raw = input.Percentage != null ? unitPrice * (1m + input.Percentage.Value / 100m) : unitPrice + input.Amount!.Value;
        if (raw < 0)
        {
            raw = 0m;
        }

        decimal? step = input.Rounding switch
        {
            PriceRounding.Cent => 0.01m,
            PriceRounding.FiveCents => 0.05m,
            PriceRounding.Quarter => 0.25m,
            PriceRounding.Half => 0.5m,
            PriceRounding.Whole => 1m,
            _ => null
        };
        return step == null ? RateLine.Round4(raw) : RateLine.Round4(Math.Round(raw / step.Value, 0, MidpointRounding.AwayFromZero) * step.Value);
    }

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.PriceLists, RequiresAll = true)]
    [Authorize(FinancialPermissions.PriceLists.Simulate)]
    public virtual async Task<PriceListSimulateResultDto> SimulateAsync(Guid id, PriceListSimulateDto input)
    {
        var list = await Repository.GetAsync(id, includeDetails: true);
        var rules = await AsyncExecuter.ToListAsync((await _rules.WithDetailsAsync()).Where(r => r.PriceListId == id).OrderBy(r => r.Position));
        var asOf = input.AsOf ?? DateOnly.FromDateTime(Clock.Now);
        var line = new PricingLine
        {
            AnchorId = PricingLines.SyntheticId(1),
            Position = 1,
            TransportUnitId = input.TransportUnitId,
            ProductId = input.ProductId,
            VehicleTypeId = input.VehicleTypeId,
            Quantity = input.Quantity,
            Positions = input.Positions,
            Colli = input.Colli,
            WeightKg = input.WeightKg,
            VolumeM3 = input.VolumeM3,
            LoadingMeters = input.LoadingMeters,
            LoadFactor = input.LoadFactor,
            Epq = input.Epq,
            DistanceKm = input.DistanceKm,
            TollDistanceKm = input.TollDistanceKm,
            DurationH = input.DurationH,
            First = new PricingStop { RelationId = input.FromRelationId, RegionIds = input.FromRegionIds, Date = asOf },
            Last = new PricingStop { RelationId = input.ToRelationId, RegionIds = input.ToRegionIds, Date = asOf },
            AsOf = asOf
        };

        var resolution = PriceResolver.Explain(new PriceListWithRules(list, rules), line, [line]);
        return new PriceListSimulateResultDto
        {
            WinningRuleId = resolution.WinningRule?.Id,
            Candidates = resolution.Candidates.Select(c => new PriceListRuleCandidateDto { RuleId = c.RuleId, Tier = c.Tier, LaneScore = c.LaneScore, Position = c.Position, Reason = c.Reason }).ToList(),
            BracketId = resolution.Bracket?.Id,
            ColumnPosition = resolution.ColumnPosition,
            UnitPrice = resolution.UnitPrice,
            Quantity = resolution.Quantity,
            Amount = resolution.Amount,
            Warning = resolution.Warning,
            IsValidOnAsOf = list.IsValidOn(asOf)
        };
    }

    // ── export and import ──────────────────────────────────────────────────────

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.PriceLists, RequiresAll = true)]
    [Authorize(FinancialPermissions.PriceLists.Default)]
    public virtual async Task<IRemoteStreamContent> ExportAsync(Guid id)
    {
        var list = await Repository.GetAsync(id, includeDetails: true);
        var rules = await AsyncExecuter.ToListAsync((await _rules.WithDetailsAsync()).Where(r => r.PriceListId == id).OrderBy(r => r.Position));
        var bytes = PriceListWorkbook.Export(list, rules);
        return new RemoteStreamContent(new MemoryStream(bytes), list.Code + ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    [RequiresFeature(AppFeatures.MasterData.Financial.Default, AppFeatures.MasterData.Financial.Pricing.Default, AppFeatures.MasterData.Financial.Pricing.PriceLists, RequiresAll = true)]
    [Authorize(FinancialPermissions.PriceLists.Update)]
    public virtual async Task<PriceListImportResultDto> ImportAsync(Guid id, IRemoteStreamContent file)
    {
        var columnCount = (await ColumnsOfAsync(id)).Count;
        if (file.ContentLength is > FinancialConsts.MaxImportBytes)
        {
            throw new BusinessException(FinancialErrorCodes.ImportInvalid).WithData("row", 0).WithData("column", 0).WithData("problem", "TooLarge");
        }

        using var buffer = new MemoryStream();
        await file.GetStream().CopyToAsync(buffer);
        if (buffer.Length > FinancialConsts.MaxImportBytes)
        {
            throw new BusinessException(FinancialErrorCodes.ImportInvalid).WithData("row", 0).WithData("column", 0).WithData("problem", "TooLarge");
        }

        buffer.Position = 0;
        var imported = PriceListWorkbook.Import(buffer, columnCount);
        if (imported.Count > FinancialConsts.MaxPriceListRules)
        {
            throw new BusinessException(FinancialErrorCodes.TooManyRules).WithData("max", FinancialConsts.MaxPriceListRules);
        }

        var rules = new List<PriceListRule>();
        var position = 0;
        foreach (var row in imported.OrderBy(r => r.Position))
        {
            var rule = new PriceListRule(GuidGenerator.Create(), CurrentTenant.Id, id, ++position);
            rule.SetLane(row.FromRelationId, row.FromRegionId, row.ToRelationId, row.ToRegionId);
            rule.SetDimensions(row.TransportUnitId, row.ProductId, row.VehicleTypeId);
            rule.SetRowQuantity(row.RowQuantity);
            rule.SetMinMax(row.MinAmount, row.MaxAmount);
            rule.SetBrackets(row.Brackets.Select(b => (b.UpperBound, (IReadOnlyList<decimal?>)b.UnitPrices)).ToList(), columnCount, GuidGenerator.Create);
            rules.Add(rule);
        }

        await _masterData.EnsureRelationsExistAsync(rules.SelectMany(r => new[] { r.FromRelationId, r.ToRelationId }).Where(x => x != null).Select(x => x!.Value).Distinct().ToList());
        await _masterData.EnsureRegionsExistAsync(rules.SelectMany(r => new[] { r.FromRegionId, r.ToRegionId }).Where(x => x != null).Select(x => x!.Value).Distinct().ToList());

        /* Replace: the file is the whole rule set. */
        var existing = await _rules.GetListAsync(r => r.PriceListId == id);
        if (existing.Count > 0)
        {
            await _rules.DeleteManyAsync(existing, autoSave: true);
        }

        if (rules.Count > 0)
        {
            await _rules.InsertManyAsync(rules, autoSave: true);
        }

        var result = new PriceListImportResultDto
        {
            RuleCount = rules.Count,
            BracketCount = rules.Sum(r => r.Brackets.Count),
            CellCount = rules.Sum(r => r.Brackets.Sum(b => b.Cells.Count))
        };
        Logger.LogInformation("Price list {Id} imported: {Rules} rules, {Brackets} brackets, {Cells} cells.", id, result.RuleCount, result.BracketCount, result.CellCount);
        return result;
    }
}
