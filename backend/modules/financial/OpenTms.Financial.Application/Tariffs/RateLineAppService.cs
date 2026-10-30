using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Financial.Permissions;
using OpenTms.Financial.Tariffs.Calculators;
using OpenTms.PlanningExecution.Published;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.Features;
using OpenTms.Platform.Permissions;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Financial.Tariffs;

/// <summary>
/// Design 57 "RateLineAppService": the stored lines of an order or trip with their AD-08 totals and the calculation
/// state; manual lines (QP6, flags set here, never by the client); the unit-price adjustment of a calculated line; the
/// fixed price; the bulk re-run. Every command that changes a line requests the root's recalculation, so the
/// discounts and surcharges follow and <c>RateLinesChanged</c> travels with the new version — one path for all effects.
/// </summary>
[RequiresFeature(AppFeatures.Transport)]
public class RateLineAppService : FinancialAppServiceBase, IRateLineAppService
{
    private readonly IRepository<RateLine, Guid> _lines;
    private readonly IRepository<PriceList, Guid> _priceLists;
    private readonly IRepository<DiscountOrSurcharge, Guid> _discounts;
    private readonly IRepository<AdditionalService, Guid> _services;
    private readonly IDerivedValueStateStore _states;
    private readonly IDerivedValueScheduler _scheduler;
    private readonly ILegReader _legs;

    public RateLineAppService(
        IRepository<RateLine, Guid> lines,
        IRepository<PriceList, Guid> priceLists,
        IRepository<DiscountOrSurcharge, Guid> discounts,
        IRepository<AdditionalService, Guid> services,
        IDerivedValueStateStore states,
        IDerivedValueScheduler scheduler,
        ILegReader legs)
    {
        _lines = lines;
        _priceLists = priceLists;
        _discounts = discounts;
        _services = services;
        _states = states;
        _scheduler = scheduler;
        _legs = legs;
    }

    [Authorize(FinancialPermissions.RateLines.Default)]
    public virtual async Task<RateLineSetDto> GetAsync(RateLineGetRequestDto input)
    {
        var root = RootOf(input.OrderId, input.TripId);
        if (root.Kind == RateLineAnchorKind.Order && input.Side == RateSide.Cost)
        {
            return await GetOrderCostAsync(root.Id);
        }

        var lines = await TreeAsync(root);
        var state = await _states.FindAsync(root.Kind == RateLineAnchorKind.Order ? OrderRateLineCalculator.TargetType : TripRateLineCalculator.TargetType, root.Id);
        return await SetOfAsync(root.Id, lines.OrderBy(l => l.Position).ToList(), state);
    }

    /// <summary>
    /// Design 61 "Order page review" point 4: the purchase prices of an order's freight items. The order's legs come through
    /// Planning's published reader (tenant-filtered there); the lines are the cost lines anchored on those legs within the trip
    /// each leg is on now (a line left behind on a trip the leg moved away from is that trip's, not the item's). Bounded:
    /// one query per thousand legs, an order's legs are few.
    /// </summary>
    private async Task<RateLineSetDto> GetOrderCostAsync(Guid orderId)
    {
        var legs = (await _legs.GetByOrderAsync(orderId)).Where(l => l.TripId != null).ToDictionary(l => l.Id);
        var lines = new List<RateLine>();
        foreach (var chunk in legs.Keys.Chunk(1_000))
        {
            lines.AddRange(await _lines.GetListAsync(l => l.Side == RateSide.Cost && l.LegId != null && chunk.Contains(l.LegId.Value)));
        }

        lines = lines.Where(l => legs[l.LegId!.Value].TripId == l.AnchorTripId)
            .OrderBy(l => legs[l.LegId!.Value].Position).ThenBy(l => legs[l.LegId!.Value].TripNumber).ThenBy(l => l.Position)
            .ToList();

        /* The calculation state shown is the least advanced of the trips' cost calculations. */
        DerivedValueState? state = null;
        foreach (var tripId in legs.Values.Select(l => l.TripId!.Value).Distinct())
        {
            var tripState = await _states.FindAsync(TripRateLineCalculator.TargetType, tripId);
            if (tripState != null && (state == null || tripState.CalculatedVersion < tripState.RequestedVersion))
            {
                state = tripState;
            }
        }

        var set = await SetOfAsync(orderId, lines, state);
        foreach (var dto in set.Lines)
        {
            var leg = legs[dto.LegId!.Value];
            dto.TripNumber = leg.TripNumber;
            dto.LegOrderLineId = leg.OrderLineId;
        }

        return set;
    }

    private async Task<RateLineSetDto> SetOfAsync(Guid rootId, List<RateLine> lines, DerivedValueState? state)
    {
        var totals = lines.Count == 0 ? null : RateLineTotals.Of(rootId, lines);
        return new RateLineSetDto
        {
            Lines = await ToDtosAsync(lines),
            Totals = totals == null ? new RateLineSetTotalsDto() : new RateLineSetTotalsDto
            {
                ByAnchor = totals.ByChild.Select(p => new RateLineAnchorTotalsDto { AnchorId = p.Key, Amount = p.Value }).ToList(),
                LinesAmount = totals.LinesAmount,
                DiscountsAndSurchargesAmount = totals.DiscountsAndSurchargesAmount,
                AdditionalServicesAmount = totals.AdditionalServicesAmount,
                Total = totals.TotalAmount,
                HasFixedPrice = totals.HasOverridingLine
            },
            CurrencyCode = lines.FirstOrDefault()?.CurrencyCode,
            State = state == null ? null : new DerivedValueStateDto
            {
                TargetId = state.TargetId,
                RequestedVersion = state.RequestedVersion,
                CalculatedVersion = state.CalculatedVersion,
                Status = state.Status,
                LastErrorCode = state.LastErrorCode,
                CalculatedAt = state.CalculatedAt
            }
        };
    }

    [Authorize(FinancialPermissions.RateLines.Update)]
    public virtual async Task<RateLineDto> CreateManualAsync(ManualRateLineCreateDto input)
    {
        if (input.Kind is not (RateLineKind.Manual or RateLineKind.AdditionalService))
        {
            throw FinancialValues.Invalid("kind", ((int)input.Kind).ToString());
        }

        if (input.UnitPrice == null)
        {
            throw new BusinessException(FinancialErrorCodes.ManualLineRequiresUnitPrice);
        }

        var root = RootOf(input.OrderId, input.TripId);
        var anchor = AnchorOf(root, input.OrderLineId, input.StopId, input.LegId);
        var side = root.Kind == RateLineAnchorKind.Trip ? RateSide.Cost : input.Side;
        if (anchor.Kind == RateLineAnchorKind.Leg)
        {
            await EnsureLegOnTripAsync(root.Id, anchor.Id, input.OrderLineId);
        }

        Guid? serviceId = null;
        if (input.Kind == RateLineKind.AdditionalService)
        {
            if (input.AdditionalServiceId == null || !await _services.AnyAsync(s => s.Id == input.AdditionalServiceId.Value))
            {
                throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "additionalServiceId");
            }

            serviceId = input.AdditionalServiceId;
        }

        var tree = await TreeAsync(root);
        if (tree.Count >= FinancialConsts.MaxRateLinesPerAnchor)
        {
            throw new BusinessException(FinancialErrorCodes.TooManyRateLines).WithData("max", FinancialConsts.MaxRateLinesPerAnchor);
        }

        var currency = input.CurrencyCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(currency))
        {
            currency = tree.FirstOrDefault()?.CurrencyCode ?? "EUR";
        }

        var line = RateLine.Manual(GuidGenerator.Create(), CurrentTenant.Id, side, anchor, input.Kind, currency, input.Description, input.Quantity, input.QuantitySource, input.UnitPrice.Value, serviceId);
        line.Position = tree.Count == 0 ? 1 : tree.Max(l => l.Position) + 1;
        await _lines.InsertAsync(line, autoSave: true);
        await RequestAsync(root, "ManualLine");
        return (await ToDtosAsync([line])).Single();
    }

    [Authorize(FinancialPermissions.RateLines.Update)]
    public virtual async Task<RateLineDto> UpdateAsync(Guid id, RateLineUpdateDto input)
    {
        var line = await _lines.GetAsync(id);
        line.ConcurrencyStamp = input.ConcurrencyStamp;

        if (line.IsManual)
        {
            line.SetManual(input.Description ?? line.Description, input.Quantity ?? line.Quantity, input.QuantitySource ?? line.QuantitySource, input.UnitPrice ?? line.UnitPrice);
        }
        else
        {
            /* A calculated line takes the unit price only; its quantity and description stay the calculator's (QP6). */
            if (input.UnitPrice == null || input.Quantity != null || input.QuantitySource != null || input.Description != null)
            {
                throw new BusinessException(FinancialErrorCodes.CalculatedLineReadOnly);
            }

            line.AdjustUnitPrice(input.UnitPrice.Value);
        }

        await _lines.UpdateAsync(line, autoSave: true);
        await RequestAsync(line.Anchor, "LineEdited");
        return (await ToDtosAsync([line])).Single();
    }

    [Authorize(FinancialPermissions.RateLines.Update)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var line = await _lines.GetAsync(id);
        if (!line.IsManual && !IsOpenLine(line))
        {
            throw new BusinessException(FinancialErrorCodes.CalculatedLineReadOnly);
        }

        /* Design 61 point 3, decided in the build (build note 5): an open line may be removed, adjusted or not. The next calculation writes a
         * fresh unadjusted open line while its basis is still charged without a list (legacy behaviour). */
        await _lines.DeleteAsync(line, autoSave: true);
        await RequestAsync(line.Anchor, "LineDeleted");
    }

    [Authorize(FinancialPermissions.RateLines.Update)]
    public virtual async Task<RateLineDto> SetFixedPriceAsync(FixedPriceDto input)
    {
        var root = RateLineAnchor.Order(RootOf(input.OrderId, null).Id);
        var existing = await _lines.FirstOrDefaultAsync(l => l.OrderId == input.OrderId && l.Kind == RateLineKind.FixedPrice);
        if (existing != null)
        {
            existing.SetAmount(RateLine.Round2(input.Amount));
            await _lines.UpdateAsync(existing, autoSave: true);
        }
        else
        {
            var tree = await TreeAsync(root);
            var currency = input.CurrencyCode?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(currency))
            {
                currency = tree.FirstOrDefault()?.CurrencyCode ?? "EUR";
            }

            existing = RateLine.Overriding(GuidGenerator.Create(), CurrentTenant.Id, RateSide.Revenue, root, RateLineKind.FixedPrice, currency, RateLine.Round2(input.Amount));
            existing.Position = tree.Count == 0 ? 1 : tree.Max(l => l.Position) + 1;
            await _lines.InsertAsync(existing, autoSave: true);
        }

        await RequestAsync(root, "FixedPrice");
        return (await ToDtosAsync([existing])).Single();
    }

    [Authorize(FinancialPermissions.RateLines.Update)]
    public virtual async Task RemoveFixedPriceAsync(Guid orderId)
    {
        var root = RateLineAnchor.Order(RootOf(orderId, null).Id);
        var existing = await _lines.GetListAsync(l => l.OrderId == orderId && l.Kind == RateLineKind.FixedPrice);
        if (existing.Count == 0)
        {
            return;
        }

        await _lines.DeleteManyAsync(existing, autoSave: true);
        await RequestAsync(root, "FixedPriceRemoved");
    }

    /// <summary>The bulk re-run (a retroactive rate change): requests every named order and trip for a new version.</summary>
    [Authorize(PlatformPermissions.DerivedValues.Recalculate)]
    public virtual async Task<int> RecalculateAsync(RateLineRecalculateDto input)
    {
        var count = 0;
        var orders = input.OrderIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (orders.Count > 0)
        {
            count += await _scheduler.RequestManyAsync(OrderRateLineCalculator.TargetType, orders, "Recalculate");
        }

        var trips = input.TripIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (trips.Count > 0)
        {
            count += await _scheduler.RequestManyAsync(TripRateLineCalculator.TargetType, trips, "Recalculate");
        }

        return count;
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static RateLineAnchor RootOf(Guid? orderId, Guid? tripId)
    {
        if ((orderId == null) == (tripId == null) || orderId == Guid.Empty || tripId == Guid.Empty)
        {
            throw new BusinessException(FinancialErrorCodes.AnchorRequired);
        }

        return orderId != null ? RateLineAnchor.Order(orderId.Value) : RateLineAnchor.Trip(tripId!.Value);
    }

    /// <summary>
    /// The anchor beneath the root: on an order its line or stop (one of them); on a trip its leg, where the leg's order line
    /// may be named alongside (design 61 point 4; checked by <see cref="EnsureLegOnTripAsync"/>), never an order line alone.
    /// </summary>
    private static RateLineAnchor AnchorOf(RateLineAnchor root, Guid? orderLineId, Guid? stopId, Guid? legId)
    {
        if (root.Kind == RateLineAnchorKind.Order)
        {
            var children = new[] { orderLineId, stopId, legId }.Count(id => id != null);
            if (children > 1 || legId != null)
            {
                throw new BusinessException(FinancialErrorCodes.AnchorRequired);
            }

            return orderLineId != null ? RateLineAnchor.OrderLine(root.Id, orderLineId.Value)
                : stopId != null ? RateLineAnchor.Stop(root.Id, stopId.Value)
                : root;
        }

        if (stopId != null || (orderLineId != null && legId == null))
        {
            throw new BusinessException(FinancialErrorCodes.AnchorRequired);
        }

        return legId != null ? RateLineAnchor.Leg(root.Id, legId.Value) : root;
    }

    /// <summary>A trip's line on a leg: the leg is on that trip now and, when named, of that order line (through Planning's published reader).</summary>
    private async Task EnsureLegOnTripAsync(Guid tripId, Guid legId, Guid? orderLineId)
    {
        var leg = (await _legs.GetAsync([legId])).FirstOrDefault(l => l.Id == legId);
        if (leg == null || leg.TripId != tripId || (orderLineId != null && leg.OrderLineId != orderLineId.Value))
        {
            throw new BusinessException(FinancialErrorCodes.LegNotOnTrip).WithData("legId", legId).WithData("tripId", tripId);
        }
    }

    /// <summary>An open line (design 61 point 3): a calculated revenue line of kind PriceList without a price list.</summary>
    private static bool IsOpenLine(RateLine line)
        => !line.IsManual && line.Side == RateSide.Revenue && line.Kind == RateLineKind.PriceList && line.PriceListId == null;

    private Task<List<RateLine>> TreeAsync(RateLineAnchor root)
        => root.Kind == RateLineAnchorKind.Order
            ? _lines.GetListAsync(l => l.AnchorOrderId == root.Id)
            : _lines.GetListAsync(l => l.AnchorTripId == root.Id);

    private Task RequestAsync(RateLineAnchor anchor, string reason)
        => anchor.OrderId != null
            ? _scheduler.RequestAsync(OrderRateLineCalculator.TargetType, anchor.OrderId.Value, reason)
            : _scheduler.RequestAsync(TripRateLineCalculator.TargetType, anchor.TripId!.Value, reason);

    private async Task<List<RateLineDto>> ToDtosAsync(List<RateLine> lines)
    {
        /* Names for the page: one query per referenced table, never per line. */
        var listIds = lines.Where(l => l.PriceListId != null).Select(l => l.PriceListId!.Value).Distinct().ToList();
        var discountIds = lines.Where(l => l.DiscountOrSurchargeId != null).Select(l => l.DiscountOrSurchargeId!.Value).Distinct().ToList();
        var serviceIds = lines.Where(l => l.AdditionalServiceId != null).Select(l => l.AdditionalServiceId!.Value).Distinct().ToList();
        var listCodes = listIds.Count == 0 ? [] : (await _priceLists.GetListAsync(p => listIds.Contains(p.Id))).ToDictionary(p => p.Id, p => p.Code);
        var discountNames = discountIds.Count == 0 ? [] : (await _discounts.GetListAsync(d => discountIds.Contains(d.Id))).ToDictionary(d => d.Id, d => d.Name);
        var serviceNames = serviceIds.Count == 0 ? [] : (await _services.GetListAsync(s => serviceIds.Contains(s.Id))).ToDictionary(s => s.Id, s => s.Name);

        return lines.Select(l => new RateLineDto
        {
            Id = l.Id,
            Side = l.Side,
            Kind = l.Kind,
            OrderId = l.OrderId,
            OrderLineId = l.OrderLineId,
            LegId = l.LegId,
            StopId = l.StopId,
            TripId = l.TripId,
            Position = l.Position,
            DescriptionKey = l.DescriptionKey,
            Description = l.Description,
            Quantity = l.Quantity,
            QuantitySource = l.QuantitySource,
            UnitPrice = l.UnitPrice,
            Amount = l.Amount,
            CurrencyCode = l.CurrencyCode,
            OverridesChildren = l.OverridesChildren,
            IsManual = l.IsManual,
            IsAdjusted = l.IsAdjusted,
            AppliesAfterOthers = l.AppliesAfterOthers,
            PriceListId = l.PriceListId,
            PriceListCode = l.PriceListId != null ? listCodes.GetValueOrDefault(l.PriceListId.Value) : null,
            PriceListRuleId = l.PriceListRuleId,
            PriceListBracketId = l.PriceListBracketId,
            ColumnPosition = l.ColumnPosition,
            DiscountOrSurchargeId = l.DiscountOrSurchargeId,
            DiscountOrSurchargeName = l.DiscountOrSurchargeId != null ? discountNames.GetValueOrDefault(l.DiscountOrSurchargeId.Value) : null,
            AdditionalServiceId = l.AdditionalServiceId,
            AdditionalServiceName = l.AdditionalServiceId != null ? serviceNames.GetValueOrDefault(l.AdditionalServiceId.Value) : null,
            VatPercentageId = l.VatPercentageId,
            CalculatedAt = l.CalculatedAt,
            CalculationVersion = l.CalculationVersion,
            ConcurrencyStamp = l.ConcurrencyStamp,
            AnchorTripId = l.AnchorTripId
        }).ToList();
    }
}
