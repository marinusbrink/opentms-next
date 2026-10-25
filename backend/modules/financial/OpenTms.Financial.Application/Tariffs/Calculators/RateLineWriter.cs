using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Financial.Events;
using OpenTms.Financial.Tariffs.Pricing;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;

namespace OpenTms.Financial.Tariffs.Calculators;

/// <summary>
/// Writes a resolution for one root (design 57 "OrderRateLineCalculator"): the root's calculated lines are replaced
/// (delete + insert, inside the job's transaction), the kept lines (manual, adjusted, the untouched overriding lines)
/// stay, and <see cref="RateLinesChangedEto"/> goes to the outbox. When the new set equals the stored set nothing is
/// written and nothing is published: a repeated delivery or a round trip through Planning's trip calculator is one
/// effect (rule 3), which also keeps the request chain finite.
/// </summary>
public class RateLineWriter : ITransientDependency
{
    private readonly IRepository<RateLine, Guid> _lines;
    private readonly IDistributedEventBus _eventBus;

    public ILogger<RateLineWriter> Logger { get; set; } = NullLogger<RateLineWriter>.Instance;

    public RateLineWriter(IRepository<RateLine, Guid> lines, IDistributedEventBus eventBus)
    {
        _lines = lines;
        _eventBus = eventBus;
    }

    /// <summary>The stored lines of a root tree on one side.</summary>
    public Task<List<RateLine>> GetTreeAsync(RateLineAnchor root, RateSide side)
        => root.Kind == RateLineAnchorKind.Order
            ? _lines.GetListAsync(l => l.AnchorOrderId == root.Id && l.Side == side)
            : _lines.GetListAsync(l => l.AnchorTripId == root.Id && l.Side == side);

    /// <summary>The lines a calculation never replaces: typed, adjusted, and the overriding lines it does not emit itself.</summary>
    public static bool IsKept(RateLine line, bool resolutionEmitsFixedPrice)
        => line.IsManual
        || line.IsAdjusted
        || line.Kind == RateLineKind.AgreedPrice
        || (line.Kind == RateLineKind.FixedPrice && !resolutionEmitsFixedPrice);

    /// <summary>Replaces the tree with the resolution; returns true when something changed and the event was published.</summary>
    public async Task<bool> ReplaceAsync(RateLineAnchor root, RateSide side, IReadOnlyList<RateLine> existing, PriceResolution resolution, bool resolutionEmitsFixedPrice, long version)
    {
        var kept = existing.Where(l => IsKept(l, resolutionEmitsFixedPrice)).ToList();
        var replaced = existing.Except(kept).ToList();
        var untouched = kept.Where(l => !resolution.KeptLines.Contains(l)).ToList();

        var next = resolution.NewLines.Concat(resolution.KeptLines).Concat(untouched).ToList();
        if (next.Count > FinancialConsts.MaxRateLinesPerAnchor)
        {
            throw new BusinessException(FinancialErrorCodes.TooManyRateLines).WithData("max", FinancialConsts.MaxRateLinesPerAnchor);
        }

        var before = existing.Select(Signature).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var after = next.Select(Signature).OrderBy(s => s, StringComparer.Ordinal).ToList();
        if (before.SequenceEqual(after))
        {
            Logger.LogDebug("Rate lines of {Root} v{Version}: unchanged ({Count} lines); nothing written.", root.Id, version, next.Count);
            return false;
        }

        if (replaced.Count > 0)
        {
            await _lines.DeleteManyAsync(replaced);
        }

        foreach (var line in resolution.KeptLines)
        {
            await _lines.UpdateAsync(line);
        }

        if (resolution.NewLines.Count > 0)
        {
            await _lines.InsertManyAsync(resolution.NewLines);
        }

        await _eventBus.PublishAsync(root.Kind == RateLineAnchorKind.Order
            ? new RateLinesChangedEto { OrderId = root.Id, Version = version }
            : new RateLinesChangedEto { TripId = root.Id, Version = version });

        Logger.LogInformation("Rate lines of {Root} v{Version}: {Replaced} replaced by {New}, {Kept} kept.", root.Id, version, replaced.Count, resolution.NewLines.Count, resolution.KeptLines.Count + untouched.Count);
        return true;
    }

    /// <summary>What makes two line sets the same money: kind, anchor, numbers, flags and provenance; not ids or timestamps.</summary>
    private static string Signature(RateLine line) => string.Join("|",
        (int)line.Kind, (int)line.Side, (int)line.Anchor.Kind, line.Anchor.Id,
        line.Quantity, (int)line.QuantitySource, line.UnitPrice, line.Amount, line.CurrencyCode,
        line.OverridesChildren, line.IsManual, line.IsAdjusted, line.AppliesAfterOthers,
        line.PriceListId, line.PriceListRuleId, line.PriceListBracketId, line.ColumnPosition,
        line.DiscountOrSurchargeId, line.DiscountOrSurchargeValueId, line.AdditionalServiceId,
        line.VatPercentageId, line.VatPercentageRateId, line.Description);
}
