using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.Orders.ConsignmentNotes;
using OpenTms.Orders.TransportOrders.Approval;
using OpenTms.PlanningExecution.Published;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Settings;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// Gathers the facts the approval rules need (design 58 "Approval") in batched reads and hands them to the pure
/// <see cref="ApprovalEvaluator"/>: the stored revenue (IRateLineReader), the printed documents, duplicate customer
/// references, Planning's legs, and whether the referenced relations still exist. The active rules come from the
/// tenant setting Orders:ActiveApprovalRules.
/// </summary>
public class ApprovalService : ITransientDependency
{
    private readonly IRepository<TransportOrder, Guid> _orders;
    private readonly IRepository<ConsignmentNoteDocument, Guid> _documents;
    private readonly IRateLineReader _rateLines;
    private readonly ILegReader _legs;
    private readonly OrdersReferences _references;
    private readonly ISettingProvider _settings;

    public ApprovalService(
        IRepository<TransportOrder, Guid> orders,
        IRepository<ConsignmentNoteDocument, Guid> documents,
        IRateLineReader rateLines,
        ILegReader legs,
        OrdersReferences references,
        ISettingProvider settings)
    {
        _orders = orders;
        _documents = documents;
        _rateLines = rateLines;
        _legs = legs;
        _references = references;
        _settings = settings;
    }

    public async Task<IReadOnlyList<ApprovalRule>> ActiveRulesAsync()
        => ApprovalEvaluator.ParseActiveRules(await _settings.GetOrNullAsync(OrdersSettings.ActiveApprovalRules) ?? OrdersSettings.DefaultActiveApprovalRules);

    public async Task<IReadOnlyList<OrderApproval>> EvaluateAsync(IReadOnlyList<TransportOrder> orders)
    {
        if (orders.Count == 0)
        {
            return [];
        }

        var rules = await ActiveRulesAsync();
        var ids = orders.Select(o => o.Id).ToList();
        var totals = await _rateLines.GetOrderTotalsAsync(ids);
        var legs = (await _legs.GetByOrdersAsync(ids)).ToLookup(l => l.OrderId);
        var documented = (await _documents.GetListAsync(d => ids.Contains(d.OrderId))).Select(d => d.OrderId).ToHashSet();
        var duplicates = await DuplicateReferencesAsync(orders);
        var relationIds = orders.Select(o => o.CustomerRelationId).Concat(orders.SelectMany(o => o.Lines).SelectMany(l => l.Stops).Select(s => s.Details.RelationId).OfType<Guid>()).Distinct().ToList();
        var relations = await _references.RelationsAsync(relationIds, require: false);

        return orders.Select(order =>
        {
            totals.TryGetValue(order.Id, out var total);
            var referenced = new[] { order.CustomerRelationId }.Concat(order.Lines.SelectMany(l => l.Stops).Select(s => s.Details.RelationId).OfType<Guid>());
            var facts = new ApprovalFacts(
                order,
                total?.CalculationVersion == null ? null : total.TotalAmount,
                total?.CalculationVersion,
                total?.ByChild ?? new Dictionary<Guid, decimal>(),
                documented.Contains(order.Id),
                duplicates.Contains(order.Id),
                legs[order.Id].Select(l => new ApprovalLegFact(l.OrderLineId, l.DistanceM, l.Destination.ActualAt != null)).ToList(),
                referenced.All(relations.ContainsKey));
            return ApprovalEvaluator.Evaluate(facts, rules);
        }).ToList();
    }

    /// <summary>The orders whose customer reference another live order of the same customer carries too.</summary>
    private async Task<HashSet<Guid>> DuplicateReferencesAsync(IReadOnlyList<TransportOrder> orders)
    {
        var withReference = orders.Where(o => !string.IsNullOrEmpty(o.CustomerReference)).ToList();
        if (withReference.Count == 0)
        {
            return [];
        }

        var customers = withReference.Select(o => o.CustomerRelationId).Distinct().ToList();
        var references = withReference.Select(o => o.CustomerReference!).Distinct().ToList();
        var others = await _orders.GetListAsync(o =>
            customers.Contains(o.CustomerRelationId) && o.CustomerReference != null && references.Contains(o.CustomerReference) && o.Status != TransportOrderStatus.Cancelled);
        return withReference
            .Where(o => others.Any(x => x.Id != o.Id && x.CustomerRelationId == o.CustomerRelationId && x.CustomerReference == o.CustomerReference))
            .Select(o => o.Id)
            .ToHashSet();
    }
}
