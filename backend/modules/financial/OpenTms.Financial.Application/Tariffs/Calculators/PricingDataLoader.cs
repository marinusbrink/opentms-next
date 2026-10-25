using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Tariffs.Pricing;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.ChangeTracking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace OpenTms.Financial.Tariffs.Calculators;

/// <summary>The lines a calculation prices: only the rules whose lane can match one of them are read with their ladders (step 2).</summary>
public sealed record LaneFilter(IReadOnlyList<PricingLine> Lines)
{
    public IReadOnlyCollection<Guid> RelationIds { get; } = Lines.SelectMany(l => new[] { l.First.RelationId, l.Last.RelationId }).Distinct().ToList();

    public IReadOnlyCollection<Guid> RegionIds { get; } = Lines.SelectMany(l => l.First.RegionIds.Concat(l.Last.RegionIds)).Distinct().ToList();
}

/// <summary>
/// Loads what one resolution needs from Financial's own tables, one query per table (Cost &amp; SLO: never per line):
/// the price lists with their columns and rules (brackets and cells included), the discounts and surcharges with
/// their value rows and regions, the VAT category with its rates, a relation's settings. Tenant-filtered like every
/// repository read.
/// </summary>
public class PricingDataLoader : ITransientDependency
{
    private readonly IRepository<PriceList, Guid> _priceLists;
    private readonly IRepository<PriceListRule, Guid> _rules;
    private readonly IRepository<DiscountOrSurcharge, Guid> _discounts;
    private readonly IRepository<VatPercentage, Guid> _vat;
    private readonly IRepository<RelationFinancialSettings, Guid> _settings;
    private readonly IAsyncQueryableExecuter _executer;
    private readonly IEntityChangeTrackingProvider _changeTracking;

    public PricingDataLoader(
        IRepository<PriceList, Guid> priceLists,
        IRepository<PriceListRule, Guid> rules,
        IRepository<DiscountOrSurcharge, Guid> discounts,
        IRepository<VatPercentage, Guid> vat,
        IRepository<RelationFinancialSettings, Guid> settings,
        IAsyncQueryableExecuter executer,
        IEntityChangeTrackingProvider changeTracking)
    {
        _priceLists = priceLists;
        _rules = rules;
        _discounts = discounts;
        _vat = vat;
        _settings = settings;
        _executer = executer;
        _changeTracking = changeTracking;
    }

    /// <summary>
    /// The lists in the given order (QP5), each with its rules; unknown ids are skipped. With <paramref name="lanes"/>
    /// only the rules whose lane can match one of the lines are read with their ladders: the light rule rows of the
    /// stops' relations and regions come first, the exact lane match runs in memory, then the brackets and cells of the
    /// candidates (a 5,000-rule list prices a 50-line order in two bounded queries, Cost &amp; SLO). Without it every
    /// rule comes (copy, adjust, export).
    /// </summary>
    public async Task<IReadOnlyList<PriceListWithRules>> GetPriceListsAsync(IReadOnlyList<Guid> ids, LaneFilter? lanes = null)
    {
        var keys = ids.Distinct().ToList();
        if (keys.Count == 0)
        {
            return [];
        }

        /* Inputs of a calculation, never written here: no change tracking (a 50-line order touches thousands of cells). */
        using var untracked = _changeTracking.Change(false);
        var lists = await _priceLists.GetListAsync(l => keys.Contains(l.Id), includeDetails: true);
        List<PriceListRule> rules;
        if (lanes == null)
        {
            rules = await _executer.ToListAsync((await _rules.WithDetailsAsync()).Where(r => keys.Contains(r.PriceListId)));
        }
        else
        {
            var relations = lanes.RelationIds.ToList();
            var regions = lanes.RegionIds.ToList();
            var light = await _rules.GetListAsync(r => keys.Contains(r.PriceListId)
                && (r.FromRelationId == null || relations.Contains(r.FromRelationId.Value))
                && (r.ToRelationId == null || relations.Contains(r.ToRelationId.Value))
                && (r.FromRegionId == null || regions.Contains(r.FromRegionId.Value))
                && (r.ToRegionId == null || regions.Contains(r.ToRegionId.Value)));
            var matching = lists.ToDictionary(l => l.Id, l => l.StopMatching);
            var candidateIds = light
                .Where(r => matching.TryGetValue(r.PriceListId, out var stopMatching) && lanes.Lines.Any(line => PriceResolver.LaneCanMatch(stopMatching, r, line)))
                .Select(r => r.Id)
                .ToList();
            rules = [];
            foreach (var chunk in candidateIds.Chunk(1_000))
            {
                rules.AddRange(await _executer.ToListAsync((await _rules.WithDetailsAsync()).Where(r => chunk.Contains(r.Id))));
            }
        }
        var byList = rules.GroupBy(r => r.PriceListId).ToDictionary(g => g.Key, g => (IReadOnlyList<PriceListRule>)g.OrderBy(r => r.Position).ToList());

        return keys
            .Select(id => lists.FirstOrDefault(l => l.Id == id))
            .Where(l => l != null)
            .Select(l => new PriceListWithRules(l!, byList.GetValueOrDefault(l!.Id) ?? []))
            .ToList();
    }

    public async Task<PriceListWithRules?> GetPriceListAsync(Guid id)
        => (await GetPriceListsAsync([id])).FirstOrDefault();

    /// <summary>The lanes of the lines' first and last stops: what a rule must be able to match to be read.</summary>
    public static LaneFilter LanesOf(IReadOnlyList<PricingLine> lines) => new(lines);

    /// <summary>The discounts and surcharges in the given order, with values and regions; unknown ids are skipped.</summary>
    public async Task<IReadOnlyList<DiscountOrSurcharge>> GetDiscountsOrSurchargesAsync(IReadOnlyList<Guid> ids)
    {
        var keys = ids.Distinct().ToList();
        if (keys.Count == 0)
        {
            return [];
        }

        using var untracked = _changeTracking.Change(false);
        var items = await _discounts.GetListAsync(d => keys.Contains(d.Id), includeDetails: true);
        return keys.Select(id => items.FirstOrDefault(d => d.Id == id)).Where(d => d != null).Select(d => d!).ToList();
    }

    public async Task<VatPercentage?> GetVatAsync(Guid? id)
    {
        if (id == null)
        {
            return null;
        }

        using var untracked = _changeTracking.Change(false);
        return await _vat.FindAsync(id.Value, includeDetails: true);
    }

    public async Task<RelationFinancialSettings?> GetSettingsAsync(Guid? relationId)
    {
        if (relationId == null)
        {
            return null;
        }

        using var untracked = _changeTracking.Change(false);
        return await _settings.FindAsync(s => s.RelationId == relationId.Value, includeDetails: true);
    }
}
