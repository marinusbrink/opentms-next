using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTms.Financial.Invoicing.TestData;
using OpenTms.Financial.Tariffs.TestData;
using OpenTms.MasterData.TestData;
using OpenTms.Platform.TestData;
using Volo.Abp.Auditing;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Financial.TestData;

/// <summary>
/// Financial's part of "Reset to dataset" (docs/designs/65-tenant-test-datasets.md). Shared plumbing over the
/// submodules: the Tariffs part builds services, discounts and surcharges and price lists; the Invoicing part links them
/// to the Master Data relations through their financial settings. Reads <see cref="MasterDataTestDataset"/>, publishes
/// <see cref="FinancialTestDataset"/>. Rate lines are wiped with the orders and trips they belong to but not generated:
/// they are calculated from orders (AD-07), which do not exist yet at this point of the run.
/// </summary>
[DisableAuditing]
public class FinancialTestDatasetContributor : ITestDatasetContributor, ITransientDependency
{
    private readonly TariffsTestDataGenerator _tariffs;
    private readonly InvoicingTestDataGenerator _invoicing;
    private readonly ILogger<FinancialTestDatasetContributor> _logger;

    public FinancialTestDatasetContributor(
        TariffsTestDataGenerator tariffs,
        InvoicingTestDataGenerator invoicing,
        ILogger<FinancialTestDatasetContributor> logger)
    {
        _tariffs = tariffs;
        _invoicing = invoicing;
        _logger = logger;
    }

    public int Order => 200;

    /// <summary>Invoicing first: the relation settings restrict-reference the price lists and discounts or surcharges.</summary>
    public async Task WipeAsync(TestDatasetContext context)
    {
        await _invoicing.WipeAsync();
        await _tariffs.WipeAsync();
    }

    public async Task GenerateAsync(TestDatasetContext context)
    {
        var masterData = context.Get<MasterDataTestDataset>();
        var tariffs = await _tariffs.GenerateAsync(context, masterData);
        var settings = await _invoicing.GenerateAsync(context, masterData, tariffs);

        var output = new FinancialTestDataset();
        output.PriceListIds.AddRange(tariffs.PriceListIds);
        output.DiscountOrSurchargeIds.AddRange(tariffs.DiscountOrSurchargeIds);
        output.AdditionalServiceIds.AddRange(tariffs.AdditionalServiceIds);
        context.Publish(output);

        _logger.LogInformation(
            "Tenant {TenantId}: Financial dataset generated: {PriceLists} price lists with {Rules} rules, {Surcharges} discounts or surcharges, {Services} additional services, {Settings} relation financial settings",
            context.TenantId, tariffs.PriceListCount, tariffs.RuleCount, tariffs.DiscountOrSurchargeIds.Count,
            tariffs.AdditionalServiceIds.Count, settings);
    }
}
