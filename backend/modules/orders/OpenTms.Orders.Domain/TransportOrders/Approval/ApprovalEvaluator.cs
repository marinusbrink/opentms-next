using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Volo.Abp;

namespace OpenTms.Orders.TransportOrders.Approval;

/// <summary>What one order's approval needs beyond the aggregate: the stored revenue, documents, duplicates and legs.</summary>
/// <param name="RevenueTotal">The rate-line total (AD-08), null when nothing was ever calculated.</param>
/// <param name="CalculationVersion">The derived-value version the rate lines carry; null = never calculated.</param>
/// <param name="RevenueByLine">The revenue per line id, as the rate-line reader reports it.</param>
/// <param name="HasConsignmentNoteDocument">A CMR was printed for the order.</param>
/// <param name="DuplicateCustomerReference">Another live order of the customer carries the same customer reference.</param>
/// <param name="Legs">Planning's legs of the order with their line, distance and arrival.</param>
/// <param name="ReferencesExist">The order's customer and stop relations still exist in Master Data.</param>
public sealed record ApprovalFacts(
    TransportOrder Order,
    decimal? RevenueTotal,
    long? CalculationVersion,
    IReadOnlyDictionary<Guid, decimal> RevenueByLine,
    bool HasConsignmentNoteDocument,
    bool DuplicateCustomerReference,
    IReadOnlyList<ApprovalLegFact> Legs,
    bool ReferencesExist);

public sealed record ApprovalLegFact(Guid LineId, int? DistanceM, bool DestinationArrived);

/// <summary>The finding of one rule on one order (design 58 "Approval": rule, severity, proposed status, message key, data).</summary>
public sealed record ApprovalResult(ApprovalRule Rule, ApprovalSeverity Severity, TransportOrderStatus ProposedStatus, string MessageKey, IReadOnlyDictionary<string, string> Data);

/// <summary>The evaluation of one order: the status an approval would give it and whether an error blocks it.</summary>
public sealed record OrderApproval(Guid OrderId, TransportOrderStatus CurrentStatus, TransportOrderStatus ProposedStatus, bool CanApprove, IReadOnlyList<ApprovalResult> Results);

/// <summary>
/// The approval rules (design 58 "Approval", PO decision Q6): one explicit switch per <see cref="ApprovalRule"/>, pure,
/// over <see cref="ApprovalFacts"/> the application service gathered. A warning lets an approval pass, an error keeps
/// the order Open. The proposed status is Archived when nothing is left to invoice, else ReadyForInvoicing.
/// </summary>
public static class ApprovalEvaluator
{
    /// <summary>Below this amount nothing is invoiced (orders.md scenario 37: revenue delta &lt; 0.50 → Archived).</summary>
    public const decimal InvoiceThreshold = 0.50m;

    public static IReadOnlyList<ApprovalRule> ParseActiveRules(string? csv)
    {
        var rules = new List<ApprovalRule>();
        foreach (var part in (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<ApprovalRule>(part, ignoreCase: true, out var rule) && Enum.IsDefined(rule) && !rules.Contains(rule))
            {
                rules.Add(rule);
            }
        }

        return rules;
    }

    public static OrderApproval Evaluate(ApprovalFacts facts, IReadOnlyCollection<ApprovalRule> activeRules)
    {
        Check.NotNull(facts, nameof(facts));
        var results = new List<ApprovalResult>();
        foreach (var rule in activeRules.Distinct().OrderBy(r => (int)r))
        {
            var result = Evaluate(rule, facts);
            if (result != null)
            {
                results.Add(result);
            }
        }

        var hasError = results.Any(r => r.Severity == ApprovalSeverity.Error);
        var proposed = results.Any(r => r.ProposedStatus == TransportOrderStatus.Archived) ? TransportOrderStatus.Archived : TransportOrderStatus.ReadyForInvoicing;
        return new OrderApproval(facts.Order.Id, facts.Order.Status, proposed, !hasError, results);
    }

    /// <summary>The finding of one rule, or null when the rule is satisfied.</summary>
    public static ApprovalResult? Evaluate(ApprovalRule rule, ApprovalFacts facts)
    {
        var order = facts.Order;
        switch (rule)
        {
            case ApprovalRule.OrderMustBeOpen:
                return order.Status == TransportOrderStatus.Open
                    ? null
                    : Error(rule, "Orders:Approval.OrderMustBeOpen", ("status", order.Status.ToString()));

            case ApprovalRule.ReferencesMustExist:
                return facts.ReferencesExist ? null : Error(rule, "Orders:Approval.ReferencesMustExist");

            case ApprovalRule.NextInvoicePrice:
                /* Nothing (left) to invoice: the order is complete, so it goes to the archive instead of the invoicing run. */
                return facts.RevenueTotal is { } total && total >= InvoiceThreshold
                    ? null
                    : new ApprovalResult(rule, ApprovalSeverity.Warning, TransportOrderStatus.Archived, "Orders:Approval.NextInvoicePrice",
                        Data(("amount", (facts.RevenueTotal ?? 0m).ToString("0.00", CultureInfo.InvariantCulture))));

            case ApprovalRule.FreightDocumentsAvailable:
                return !order.FinancialTerms.SendFreightDocumentsWithInvoice || facts.HasConsignmentNoteDocument
                    ? null
                    : Warning(rule, "Orders:Approval.FreightDocumentsAvailable");

            case ApprovalRule.PriceListApplied:
                return order.PriceLists.Count > 0 || order.FinancialTerms.FixedPriceAmount != null
                    ? null
                    : Error(rule, "Orders:Approval.PriceListApplied");

            case ApprovalRule.PriceCalculationCorrect:
                return facts.CalculationVersion != null ? null : Error(rule, "Orders:Approval.PriceCalculationCorrect");

            case ApprovalRule.DuplicateCustomerReference:
                return facts.DuplicateCustomerReference
                    ? Warning(rule, "Orders:Approval.DuplicateCustomerReference", ("reference", order.CustomerReference ?? string.Empty))
                    : null;

            case ApprovalRule.EmptyLinePrice:
            {
                if (order.FinancialTerms.FixedPriceAmount != null)
                {
                    return null;
                }

                var empty = order.Lines.OrderBy(l => l.Position).Where(l => !facts.RevenueByLine.TryGetValue(l.Id, out var amount) || amount == 0m).ToList();
                return empty.Count == 0
                    ? null
                    : Warning(rule, "Orders:Approval.EmptyLinePrice", ("positions", string.Join(", ", empty.Select(l => l.Position))));
            }

            case ApprovalRule.FinancialDefaultsMismatch:
                /* The customer's financial defaults have no published reader yet (design 57 exposes none); the rule
                 * cannot find a mismatch and passes. Build note of design 58. */
                return null;

            case ApprovalRule.FixedPriceFilled:
                return order.FinancialTerms.FixedPriceAmount is null or > 0m ? null : Error(rule, "Orders:Approval.FixedPriceFilled");

            case ApprovalRule.LegDistanceMatchesLine:
            {
                var mismatched = new List<int>();
                foreach (var line in order.Lines.OrderBy(l => l.Position))
                {
                    var legs = facts.Legs.Where(l => l.LineId == line.Id).ToList();
                    if (legs.Count == 0 || legs.Any(l => l.DistanceM == null))
                    {
                        continue;
                    }

                    if (line.DistanceM != legs.Sum(l => l.DistanceM!.Value))
                    {
                        mismatched.Add(line.Position);
                    }
                }

                return mismatched.Count == 0 ? null : Warning(rule, "Orders:Approval.LegDistanceMatchesLine", ("positions", string.Join(", ", mismatched)));
            }

            case ApprovalRule.AllStopsArrived:
                return facts.Legs.Count > 0 && facts.Legs.All(l => l.DestinationArrived)
                    ? null
                    : Error(rule, "Orders:Approval.AllStopsArrived");

            default:
                throw new ArgumentOutOfRangeException(nameof(rule), rule, "Unknown approval rule.");
        }
    }

    /// <summary>
    /// A stable hash over the evaluations (rule, severity, proposed status, message key, data per order, orders by id):
    /// the client sends it back with Approve; a different evaluation meanwhile is ApprovalResultsStale.
    /// </summary>
    public static string Hash(IEnumerable<OrderApproval> approvals)
    {
        var text = new StringBuilder();
        foreach (var approval in approvals.OrderBy(a => a.OrderId))
        {
            text.Append(approval.OrderId.ToString("N")).Append('|').Append((int)approval.ProposedStatus).Append('|').Append(approval.CanApprove ? 1 : 0);
            foreach (var result in approval.Results.OrderBy(r => (int)r.Rule))
            {
                text.Append('|').Append((int)result.Rule).Append(':').Append((int)result.Severity).Append(':').Append((int)result.ProposedStatus).Append(':').Append(result.MessageKey);
                foreach (var (key, value) in result.Data.OrderBy(d => d.Key, StringComparer.Ordinal))
                {
                    text.Append(':').Append(key).Append('=').Append(value);
                }
            }

            text.Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }

    private static ApprovalResult Error(ApprovalRule rule, string messageKey, params (string Key, string Value)[] data)
        => new(rule, ApprovalSeverity.Error, TransportOrderStatus.Open, messageKey, Data(data));

    private static ApprovalResult Warning(ApprovalRule rule, string messageKey, params (string Key, string Value)[] data)
        => new(rule, ApprovalSeverity.Warning, TransportOrderStatus.ReadyForInvoicing, messageKey, Data(data));

    private static IReadOnlyDictionary<string, string> Data(params (string Key, string Value)[] data)
        => data.ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);
}
