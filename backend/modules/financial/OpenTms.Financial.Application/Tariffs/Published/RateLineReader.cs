using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.Financial.Tariffs.Published;

/// <summary>
/// <see cref="IRateLineReader"/>: the stored lines and AD-08 totals of orders and trips for Orders (revenue, margin) and
/// Planning (allocation over legs, AD-20). In-process only, tenant-filtered, reads stored columns, never calculates.
/// </summary>
// Published in-process reader: outbox handlers and derived-value jobs call it without a user, so it is anonymous; the HTTP-facing caller authorizes.
[RemoteService(IsEnabled = false)]
[AllowAnonymous]
public class RateLineReader : FinancialAppServiceBase, IRateLineReader
{
    private readonly IRepository<RateLine, Guid> _lines;

    public RateLineReader(IRepository<RateLine, Guid> lines)
    {
        _lines = lines;
    }

    public virtual async Task<RateLineTotalsDto> GetOrderTotalsAsync(Guid orderId, CancellationToken cancellationToken = default)
        => (await GetOrderTotalsAsync([orderId], cancellationToken)).GetValueOrDefault(orderId) ?? Empty(orderId);

    public virtual async Task<IReadOnlyDictionary<Guid, RateLineTotalsDto>> GetOrderTotalsAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
    {
        var ids = Bounded(orderIds);
        var lines = ids.Count == 0 ? [] : await _lines.GetListAsync(l => l.AnchorOrderId != null && ids.Contains(l.AnchorOrderId.Value) && l.Side == RateSide.Revenue, cancellationToken: cancellationToken);
        var byOrder = lines.GroupBy(l => l.AnchorOrderId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        return ids.ToDictionary(id => id, id => byOrder.TryGetValue(id, out var own) ? ToDto(id, own) : Empty(id));
    }

    public virtual async Task<RateLineTotalsDto> GetTripTotalsAsync(Guid tripId, CancellationToken cancellationToken = default)
    {
        var lines = await _lines.GetListAsync(l => l.AnchorTripId == tripId && l.Side == RateSide.Cost, cancellationToken: cancellationToken);
        return lines.Count == 0 ? Empty(tripId) : ToDto(tripId, lines);
    }

    public virtual async Task<IReadOnlyList<RateLineSnapshotDto>> GetLegLinesAsync(IReadOnlyCollection<Guid> legIds, CancellationToken cancellationToken = default)
    {
        var ids = Bounded(legIds);
        if (ids.Count == 0)
        {
            return [];
        }

        var lines = await _lines.GetListAsync(l => l.LegId != null && ids.Contains(l.LegId.Value), cancellationToken: cancellationToken);
        return lines.OrderBy(l => l.Position).Select(l => new RateLineSnapshotDto
        {
            Id = l.Id,
            Side = l.Side,
            Kind = l.Kind,
            OrderId = l.OrderId,
            OrderLineId = l.OrderLineId,
            LegId = l.LegId,
            StopId = l.StopId,
            TripId = l.TripId,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            Amount = l.Amount,
            CurrencyCode = l.CurrencyCode,
            OverridesChildren = l.OverridesChildren
        }).ToList();
    }

    private static List<Guid> Bounded(IReadOnlyCollection<Guid> ids)
    {
        var distinct = ids.Distinct().ToList();
        if (distinct.Count > IRateLineReader.MaxKeys)
        {
            throw new BusinessException(Platform.ReferenceData.ReferenceDataErrorCodes.TooManyRows).WithData("max", IRateLineReader.MaxKeys);
        }

        return distinct;
    }

    private static RateLineTotalsDto ToDto(Guid rootId, List<RateLine> lines)
    {
        var totals = RateLineTotals.Of(rootId, lines);
        return new RateLineTotalsDto
        {
            AnchorId = rootId,
            CurrencyCode = lines[0].CurrencyCode,
            LinesAmount = totals.LinesAmount,
            DiscountsAndSurchargesAmount = totals.DiscountsAndSurchargesAmount,
            AdditionalServicesAmount = totals.AdditionalServicesAmount,
            TotalAmount = totals.TotalAmount,
            HasFixedPrice = totals.HasOverridingLine,
            CalculationVersion = totals.CalculationVersion,
            ByChild = totals.ByChild.ToDictionary(p => p.Key, p => p.Value)
        };
    }

    private static RateLineTotalsDto Empty(Guid rootId) => new() { AnchorId = rootId };
}
