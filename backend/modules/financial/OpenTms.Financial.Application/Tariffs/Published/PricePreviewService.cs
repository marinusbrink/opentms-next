using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Financial.Tariffs.Calculators;
using OpenTms.Financial.Tariffs.Pricing;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Timing;

namespace OpenTms.Financial.Tariffs.Published;

/// <summary>
/// <see cref="IPricePreviewService"/>: the one synchronous Financial call a request may make (design 56). The same
/// resolver as the calculator over the posted snapshot: nothing is stored, nothing is published, ids of unsaved lines
/// are synthetic. Any signed-in user of the tenant may ask (the screen that holds the order already shows its lines).
/// </summary>
[RemoteService(IsEnabled = false)]
[Authorize]
public class PricePreviewService : FinancialAppServiceBase, IPricePreviewService
{
    private readonly PricingDataLoader _loader;

    public PricePreviewService(PricingDataLoader loader)
    {
        _loader = loader;
    }

    [Authorize]
    public virtual async Task<PricePreviewDto> PreviewAsync(PricingSnapshotDto snapshot, CancellationToken cancellationToken = default)
    {
        var rootId = snapshot.OrderId ?? PricingLines.SyntheticId(0);
        var root = RateLineAnchor.Order(rootId);
        var lines = PricingLines.OfOrder(snapshot, []);
        var request = new PricingRequest
        {
            Side = RateSide.Revenue,
            CurrencyCode = snapshot.CurrencyCode,
            AsOf = PricingLines.AsOfOf(snapshot, lines),
            Lines = lines,
            PriceLists = await _loader.GetPriceListsAsync(snapshot.PriceListIds, PricingDataLoader.LanesOf(lines)),
            MultipleMatches = snapshot.MultipleMatches,
            DiscountsOrSurcharges = await _loader.GetDiscountsOrSurchargesAsync(snapshot.DiscountOrSurchargeIds),
            Vat = await _loader.GetVatAsync(snapshot.VatPercentageId),
            FixedPriceAmount = snapshot.FixedPriceAmount,
            RootAnchor = root,
            LineAnchor = lineId => RateLineAnchor.OrderLine(rootId, lineId),
            TenantId = CurrentTenant.Id,
            NewId = GuidGenerator.Create,
            Now = Clock.Now,
            Version = 0
        };

        var resolution = PriceResolver.Resolve(request);
        var totals = RateLineTotals.Of(rootId, resolution.NewLines);
        var synthetic = lines.Where(l => snapshot.Lines.All(s => s.LineId != l.AnchorId)).Select(l => l.AnchorId).ToHashSet();

        return new PricePreviewDto
        {
            CurrencyCode = snapshot.CurrencyCode,
            Lines = resolution.NewLines.OrderBy(l => l.Position).Select(l => new PricePreviewLineDto
            {
                OrderLineId = l.OrderLineId != null && !synthetic.Contains(l.OrderLineId.Value) ? l.OrderLineId : null,
                Kind = l.Kind,
                DescriptionKey = l.DescriptionKey,
                Quantity = l.Quantity,
                QuantitySource = l.QuantitySource,
                UnitPrice = l.UnitPrice,
                Amount = l.Amount,
                PriceListId = l.PriceListId,
                PriceListRuleId = l.PriceListRuleId,
                DiscountOrSurchargeId = l.DiscountOrSurchargeId
            }).ToList(),
            LinesAmount = totals.LinesAmount,
            DiscountsAndSurchargesAmount = totals.DiscountsAndSurchargesAmount,
            TotalAmount = totals.TotalAmount,
            Warnings = resolution.Warnings.ToList()
        };
    }
}
