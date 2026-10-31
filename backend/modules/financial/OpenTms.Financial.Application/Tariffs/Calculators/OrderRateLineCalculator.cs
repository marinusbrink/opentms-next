using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Financial.Tariffs.Pricing;
using OpenTms.Orders.Published;
using OpenTms.PlanningExecution.Published;
using OpenTms.Platform.DerivedValues;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace OpenTms.Financial.Tariffs.Calculators;

/// <summary>
/// The calculator of <c>Financial.OrderRateLines</c> (design 57, flow 2): loads the order's snapshot through Orders'
/// published reader and its legs through Planning's, the lists, discounts and VAT from Financial's own tables, runs
/// the resolver and replaces the order's non-manual revenue lines in the job's transaction. Runs under the order's
/// tenant; a superseded version is rolled back by the job (design 49).
/// </summary>
public class OrderRateLineCalculator : IDerivedValueCalculator, ITransientDependency
{
    public const string TargetType = FinancialDerivedValueTypes.OrderRateLines;

    private readonly IOrderPricingReader _orders;
    private readonly ILegReader _legs;
    private readonly PricingDataLoader _loader;
    private readonly RateLineWriter _writer;
    private readonly IGuidGenerator _guids;
    private readonly ICurrentTenant _currentTenant;
    private readonly IClock _clock;

    public ILogger<OrderRateLineCalculator> Logger { get; set; } = NullLogger<OrderRateLineCalculator>.Instance;

    public OrderRateLineCalculator(IOrderPricingReader orders, ILegReader legs, PricingDataLoader loader, RateLineWriter writer, IGuidGenerator guids, ICurrentTenant currentTenant, IClock clock)
    {
        _orders = orders;
        _legs = legs;
        _loader = loader;
        _writer = writer;
        _guids = guids;
        _currentTenant = currentTenant;
        _clock = clock;
    }

    public async Task CalculateAsync(DerivedValueCalculationContext context)
    {
        var snapshot = await _orders.GetSnapshotAsync(context.TargetId, context.CancellationToken);
        if (snapshot == null)
        {
            /* A deleted order keeps its lines (design 58: Financial keeps its lines on cancel). */
            Logger.LogInformation("Order rate lines of {OrderId} v{Version}: no snapshot; lines left as they are.", context.TargetId, context.Version);
            return;
        }

        var root = RateLineAnchor.Order(context.TargetId);
        var legs = await _legs.GetByOrderAsync(context.TargetId, context.CancellationToken);
        var lines = PricingLines.OfOrder(snapshot, legs);
        var priceLists = await _loader.GetPriceListsAsync(snapshot.PriceListIds, PricingDataLoader.LanesOf(lines));
        var discounts = await _loader.GetDiscountsOrSurchargesAsync(snapshot.DiscountOrSurchargeIds);
        var vat = await _loader.GetVatAsync(snapshot.VatPercentageId);
        var tree = await _writer.GetTreeAsync(root, RateSide.Revenue);
        var emitsFixedPrice = snapshot.FixedPriceAmount != null;
        var keptLines = tree.Lines.Where(l => l.IsManual || l.IsAdjusted).ToList();
        var keptListBases = await _loader.GetBasesAsync(keptLines.Where(l => l.IsAdjusted && l.PriceListId != null).Select(l => l.PriceListId!.Value).ToList());

        var request = new PricingRequest
        {
            Side = RateSide.Revenue,
            CurrencyCode = snapshot.CurrencyCode,
            AsOf = PricingLines.AsOfOf(snapshot, lines),
            Lines = lines,
            PriceLists = priceLists,
            MultipleMatches = snapshot.MultipleMatches,
            ChargedBases = snapshot.ChargedBases ?? [],
            DiscountsOrSurcharges = discounts,
            Vat = vat,
            FixedPriceAmount = snapshot.FixedPriceAmount,
            KeptLines = keptLines,
            KeptLineListBases = keptListBases,
            RootAnchor = root,
            LineAnchor = lineId => RateLineAnchor.OrderLine(context.TargetId, lineId),
            TenantId = _currentTenant.Id,
            NewId = _guids.Create,
            Now = _clock.Now,
            Version = context.Version
        };

        var resolution = PriceResolver.Resolve(request);
        foreach (var warning in resolution.Warnings)
        {
            Logger.LogDebug("Order rate lines of {OrderId} v{Version}: {Warning}.", context.TargetId, context.Version, warning);
        }

        await _writer.ReplaceAsync(root, RateSide.Revenue, tree, resolution, emitsFixedPrice, context.Version);
    }
}
