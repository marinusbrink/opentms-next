using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Tariffs.Pricing;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace OpenTms.Financial.Tariffs.Calculators;

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

    public PricingDataLoader(
        IRepository<PriceList, Guid> priceLists,
        IRepository<PriceListRule, Guid> rules,
        IRepository<DiscountOrSurcharge, Guid> discounts,
        IRepository<VatPercentage, Guid> vat,
        IRepository<RelationFinancialSettings, Guid> settings,
        IAsyncQueryableExecuter executer)
    {
        _priceLists = priceLists;
        _rules = rules;
        _discounts = discounts;
        _vat = vat;
        _settings = settings;
        _executer = executer;
    }

    /// <summary>The lists in the given order (QP5), each with its rules; unknown ids are skipped.</summary>
    public async Task<IReadOnlyList<PriceListWithRules>> GetPriceListsAsync(IReadOnlyList<Guid> ids)
    {
        var keys = ids.Distinct().ToList();
        if (keys.Count == 0)
        {
            return [];
        }

        var lists = await _priceLists.GetListAsync(l => keys.Contains(l.Id), includeDetails: true);
        var rules = await _executer.ToListAsync((await _rules.WithDetailsAsync()).Where(r => keys.Contains(r.PriceListId)));
        var byList = rules.GroupBy(r => r.PriceListId).ToDictionary(g => g.Key, g => (IReadOnlyList<PriceListRule>)g.OrderBy(r => r.Position).ToList());

        return keys
            .Select(id => lists.FirstOrDefault(l => l.Id == id))
            .Where(l => l != null)
            .Select(l => new PriceListWithRules(l!, byList.GetValueOrDefault(l!.Id) ?? []))
            .ToList();
    }

    public async Task<PriceListWithRules?> GetPriceListAsync(Guid id)
        => (await GetPriceListsAsync([id])).FirstOrDefault();

    /// <summary>The discounts and surcharges in the given order, with values and regions; unknown ids are skipped.</summary>
    public async Task<IReadOnlyList<DiscountOrSurcharge>> GetDiscountsOrSurchargesAsync(IReadOnlyList<Guid> ids)
    {
        var keys = ids.Distinct().ToList();
        if (keys.Count == 0)
        {
            return [];
        }

        var items = await _discounts.GetListAsync(d => keys.Contains(d.Id), includeDetails: true);
        return keys.Select(id => items.FirstOrDefault(d => d.Id == id)).Where(d => d != null).Select(d => d!).ToList();
    }

    public async Task<VatPercentage?> GetVatAsync(Guid? id)
        => id == null ? null : await _vat.FindAsync(id.Value, includeDetails: true);

    public async Task<RelationFinancialSettings?> GetSettingsAsync(Guid? relationId)
        => relationId == null ? null : await _settings.FindAsync(s => s.RelationId == relationId.Value, includeDetails: true);
}
