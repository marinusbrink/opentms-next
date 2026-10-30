using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Financial.Tariffs.Pricing;
using OpenTms.Orders.Published;
using OpenTms.Platform.DerivedValues;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Settings;
using Volo.Abp.Timing;

namespace OpenTms.Financial.Tariffs.Calculators;

/// <summary>
/// The calculator of <c>Financial.TripRateLines</c> (design 57, QP7, QP10): per segment (one order line on the trip)
/// the carrier's cost lists and default discounts over the segment, anchored to the segment's first leg; the own-fleet
/// distance line on the trip when its execution is the own fleet (unit price: the trip's vehicles' cost price per
/// kilometre, else the tenant setting; quantity the summed leg distance); the agreed price line untouched. Publishes
/// <c>RateLinesChanged { TripId }</c> when anything changed.
/// </summary>
public class TripRateLineCalculator : IDerivedValueCalculator, ITransientDependency
{
    public const string TargetType = "Financial.TripRateLines";
    public const string OwnFleetDescriptionKey = "Financial:RateLine.OwnFleetDistance";

    private readonly ITripCostFactsReader _trips;
    private readonly IOrderPricingReader _orders;
    private readonly PricingDataLoader _loader;
    private readonly RateLineWriter _writer;
    private readonly ISettingProvider _settings;
    private readonly IGuidGenerator _guids;
    private readonly ICurrentTenant _currentTenant;
    private readonly IClock _clock;

    public ILogger<TripRateLineCalculator> Logger { get; set; } = NullLogger<TripRateLineCalculator>.Instance;

    public TripRateLineCalculator(ITripCostFactsReader trips, IOrderPricingReader orders, PricingDataLoader loader, RateLineWriter writer, ISettingProvider settings, IGuidGenerator guids, ICurrentTenant currentTenant, IClock clock)
    {
        _trips = trips;
        _orders = orders;
        _loader = loader;
        _writer = writer;
        _settings = settings;
        _guids = guids;
        _currentTenant = currentTenant;
        _clock = clock;
    }

    public async Task CalculateAsync(DerivedValueCalculationContext context)
    {
        var facts = await _trips.GetAsync(context.TargetId, context.CancellationToken);
        if (facts == null)
        {
            Logger.LogInformation("Trip rate lines of {TripId} v{Version}: no trip; lines left as they are.", context.TargetId, context.Version);
            return;
        }

        var root = RateLineAnchor.Trip(context.TargetId);
        var orderIds = facts.Legs.Select(l => l.OrderId).Distinct().ToList();
        var orders = orderIds.Count == 0 ? [] : await _orders.GetSnapshotsAsync(orderIds, context.CancellationToken);
        var segments = PricingLines.OfTrip(facts.Legs, orders, facts.TripDate);

        var carrierSettings = await _loader.GetSettingsAsync(facts.CarrierRelationId);
        var priceLists = carrierSettings == null ? [] : await _loader.GetPriceListsAsync(carrierSettings.PriceListIdsFor(RateSide.Cost), PricingDataLoader.LanesOf(segments));
        var discounts = carrierSettings == null ? [] : await _loader.GetDiscountsOrSurchargesAsync(carrierSettings.DiscountOrSurchargeIdsOn(facts.TripDate));
        var currency = carrierSettings?.CurrencyCode ?? priceLists.FirstOrDefault()?.List.CurrencyCode ?? "EUR";
        var tree = await _writer.GetTreeAsync(root, RateSide.Cost);

        var request = new PricingRequest
        {
            Side = RateSide.Cost,
            CurrencyCode = currency,
            AsOf = facts.TripDate,
            Lines = segments,
            PriceLists = priceLists,
            MultipleMatches = carrierSettings?.CostMultipleMatches ?? MultipleMatches.All,
            DiscountsOrSurcharges = discounts,
            KeptLines = tree.Lines.Where(l => l.IsManual || l.IsAdjusted).ToList(),
            RootAnchor = root,
            LineAnchor = legId => RateLineAnchor.Leg(context.TargetId, legId),
            TenantId = _currentTenant.Id,
            NewId = _guids.Create,
            Now = _clock.Now,
            Version = context.Version
        };

        var resolution = PriceResolver.Resolve(request);

        if (facts.IsOwnFleet)
        {
            await AddOwnFleetLineAsync(context, facts, root, currency, resolution);
        }

        foreach (var warning in resolution.Warnings)
        {
            Logger.LogDebug("Trip rate lines of {TripId} v{Version}: {Warning}.", context.TargetId, context.Version, warning);
        }

        await _writer.ReplaceAsync(root, RateSide.Cost, tree, resolution, resolutionEmitsFixedPrice: false, context.Version);
    }

    /// <summary>
    /// QP10: the own-fleet cost per kilometre × the trip's distance. The cascade: the trip's vehicles (the vehicle's own
    /// price, else its type's, summed by Planning), else the tenant setting; without either only a warning (Assumption 5).
    /// </summary>
    private async Task AddOwnFleetLineAsync(DerivedValueCalculationContext context, TripCostFacts facts, RateLineAnchor root, string currency, PriceResolution resolution)
    {
        var distanceM = facts.Legs.Sum(l => l.DistanceM ?? 0);
        if (distanceM <= 0)
        {
            return;
        }

        var costPerKm = facts.OwnFleetCostPerKm ?? await TenantCostPerKmAsync();
        if (costPerKm is not > 0)
        {
            resolution.Warnings.Add(FinancialErrorCodes.NoCostPriceSourceWarning);
            return;
        }

        var quantity = RateLine.Round4(distanceM / 1000m);
        var unitPrice = RateLine.Round4(costPerKm.Value);
        var line = RateLine.Calculated(_guids.Create(), _currentTenant.Id, RateSide.Cost, root, RateLineKind.OwnFleetDistance, currency,
            OwnFleetDescriptionKey, quantity, QuantitySource.TripDistanceKm, unitPrice, RateLine.Round2(quantity * unitPrice));
        line.SetCalculation(_clock.Now, context.Version);
        line.Position = resolution.AllLines.Count() + 1;
        resolution.NewLines.Add(line);
    }

    private async Task<decimal?> TenantCostPerKmAsync()
    {
        var setting = await _settings.GetOrNullAsync(FinancialSettings.OwnFleetCostPerKm);
        return decimal.TryParse(setting, NumberStyles.Number, CultureInfo.InvariantCulture, out var costPerKm) ? costPerKm : null;
    }
}
