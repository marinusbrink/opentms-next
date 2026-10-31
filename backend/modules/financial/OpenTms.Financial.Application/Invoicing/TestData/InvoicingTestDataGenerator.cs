using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial.Tariffs;
using OpenTms.Financial.Tariffs.TestData;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.TestData;
using OpenTms.Platform.TestData;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace OpenTms.Financial.Invoicing.TestData;

/// <summary>
/// The Invoicing part of Financial's "Reset to dataset" contribution (docs/designs/65-tenant-test-datasets.md): the
/// financial settings of every customer and carrier relation — its price lists, default discounts and surcharges,
/// payment term and VAT treatment by country (NL: the tenant's default domestic rate; another EU country: reverse
/// charge; outside the EU: export). VAT percentages and invoice types are reference data: the seed owns them and they
/// are neither wiped nor generated.
/// </summary>
public class InvoicingTestDataGenerator : ITransientDependency
{
    private static readonly HashSet<string> EuCountries = new(StringComparer.Ordinal)
    {
        "AT", "BE", "BG", "CY", "CZ", "DE", "DK", "EE", "ES", "FI", "FR", "GR", "HR", "HU", "IE", "IT", "LT", "LU",
        "LV", "MT", "NL", "PL", "PT", "RO", "SE", "SI", "SK"
    };

    private readonly TestDatasetWriter _writer;
    private readonly IRepository<RelationFinancialSettings, Guid> _settings;
    private readonly IRepository<VatPercentage, Guid> _vatPercentages;
    private readonly IGuidGenerator _guidGenerator;

    public InvoicingTestDataGenerator(
        TestDatasetWriter writer,
        IRepository<RelationFinancialSettings, Guid> settings,
        IRepository<VatPercentage, Guid> vatPercentages,
        IGuidGenerator guidGenerator)
    {
        _writer = writer;
        _settings = settings;
        _vatPercentages = vatPercentages;
        _guidGenerator = guidGenerator;
    }

    /// <summary>The settings with their price-list and discount links (cascading keys). Runs before the Tariffs wipe: the links restrict-reference price lists and discounts or surcharges.</summary>
    public Task WipeAsync() => _writer.DeleteAllAsync(_settings);

    public async Task<int> GenerateAsync(TestDatasetContext context, MasterDataTestDataset masterData, TariffsTestData tariffs)
    {
        /* The seeded VAT categories of the tenant: a handful of reference rows. */
        var vat = await _vatPercentages.GetListAsync(v => !v.IsArchived);
        var reverseCharge = vat.FirstOrDefault(v => v.Kind == VatKind.ReverseCharge)?.Id;
        var export = vat.FirstOrDefault(v => v.Kind == VatKind.Export)?.Id;

        return await _writer.InsertAsync(_settings, Build(context, masterData, tariffs, reverseCharge, export));
    }

    private IEnumerable<RelationFinancialSettings> Build(
        TestDatasetContext context, MasterDataTestDataset masterData, TariffsTestData tariffs, Guid? reverseCharge, Guid? export)
    {
        var random = context.Random;
        var today = DateOnly.FromDateTime(context.Today);
        var thisYear = new DateOnly(today.Year, 1, 1);
        var lastYear = thisYear.AddYears(-1);

        foreach (var relation in masterData.Relations)
        {
            var isCustomer = relation.Roles.HasFlag(RelationRoles.Customer);
            var isCarrier = relation.Roles.HasFlag(RelationRoles.Carrier);
            if (!isCustomer && !isCarrier)
            {
                continue;
            }

            var settings = new RelationFinancialSettings(_guidGenerator.Create(), context.TenantId, relation.Id);
            settings.SetRelationName(relation.Name);
            settings.VatPercentageId = relation.CountryCode == "NL"
                ? null // the tenant's default: the domestic high rate
                : EuCountries.Contains(relation.CountryCode) ? reverseCharge : export;
            settings.SetPaymentTermDays(PaymentTerm(random, relation.CountryCode, isCustomer));

            var surcharges = new List<(Guid DiscountOrSurchargeId, DateOnly ValidFrom, DateOnly? ValidTill)>();
            void Default(string code, double chance, DateOnly validFrom, DateOnly? validTill = null)
            {
                if (random.NextDouble() < chance
                    && tariffs.SurchargeIdsByCode.TryGetValue(code, out var id)
                    && surcharges.All(s => s.DiscountOrSurchargeId != id))
                {
                    surcharges.Add((id, validFrom, validTill));
                }
            }

            var revenueMatches = MultipleMatches.All;
            if (isCustomer)
            {
                var lists = tariffs.RevenueListsByRelation.TryGetValue(relation.Id, out var own)
                    ? own
                    : tariffs.GeneralRevenueLists.Count > 0
                        ? [tariffs.GeneralRevenueLists[random.Next(tariffs.GeneralRevenueLists.Count)]]
                        : new List<TestPriceList>();
                settings.SetPriceLists(RateSide.Revenue, OrderedIds(lists), _guidGenerator.Create);

                var bases = lists.Select(l => l.Basis).Where(ChargedBases.IsRevenueBasis).Distinct().ToList();
                settings.SetRevenueChargedBases(bases.Count > 0 ? bases : [QuantitySource.LineQuantity]);
                revenueMatches = random.Next(100) switch
                {
                    < 85 => MultipleMatches.All,
                    < 95 => MultipleMatches.HighestWins,
                    _ => MultipleMatches.LowestWins
                };
                settings.SendFreightDocumentsWithInvoice = random.NextDouble() < 0.4;
                settings.ChargeFixedPriceByDefault = random.NextDouble() < 0.08;

                if (tariffs.SurchargesByRelation.TryGetValue(relation.Id, out var negotiated))
                {
                    /* A negotiated diesel percentage replaces the general one. */
                    surcharges.AddRange(negotiated.Distinct().Select(id => (id, thisYear, (DateOnly?)null)));
                }
                else
                {
                    Default("DIESEL", 0.8, lastYear);
                }

                Default(relation.CountryCode switch { "DE" => "MAUT-DE", "BE" => "TOL-BE", _ => "TOL-NL" }, 0.4, thisYear);
                Default("LAADKLEP", 0.1, lastYear);
                Default("WEEKEND", 0.08, lastYear);
                Default("ADMIN", 0.15, thisYear);
                Default("CONTRKORT", 0.07, thisYear, thisYear.AddYears(1).AddDays(-1));
            }

            if (isCarrier)
            {
                var lists = tariffs.CostListsByRelation.TryGetValue(relation.Id, out var own)
                    ? own
                    : tariffs.GeneralCostLists.Count > 0 && random.NextDouble() < 0.7
                        ? [tariffs.GeneralCostLists[random.Next(tariffs.GeneralCostLists.Count)]]
                        : new List<TestPriceList>();
                settings.SetPriceLists(RateSide.Cost, OrderedIds(lists), _guidGenerator.Create);

                Default("DIESEL-INK", 0.6, lastYear);
                Default("LEEGKM", 0.2, thisYear);
                Default("INKKORT", 0.1, thisYear);
            }

            settings.SetMultipleMatches(revenueMatches, MultipleMatches.All);
            settings.SetDiscountsOrSurcharges(
                surcharges.Take(FinancialConsts.MaxRelationDiscountsOrSurcharges).ToList(), _guidGenerator.Create);
            yield return settings;
        }
    }

    /// <summary>The current agreement first, next year's prepared tariff after it; within the bound per side.</summary>
    private static List<Guid> OrderedIds(List<TestPriceList> lists)
        => lists.OrderBy(l => l.ValidFrom).Select(l => l.Id).Distinct().Take(FinancialConsts.MaxRelationPriceLists).ToList();

    /// <summary>14, 30 or 60 days: domestic customers mostly 30, foreign ones 30 or 60, carriers per their terms.</summary>
    private static int PaymentTerm(Random random, string countryCode, bool isCustomer)
    {
        var roll = random.Next(100);
        if (!isCustomer)
        {
            return roll < 20 ? 14 : roll < 70 ? 30 : 60;
        }

        return countryCode == "NL"
            ? roll < 25 ? 14 : roll < 85 ? 30 : 60
            : roll < 55 ? 30 : 60;
    }
}
