using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Orders.TransportOrders.Approval;
using Shouldly;
using Xunit;
using static OpenTms.Orders.TransportOrders.TransportOrderBuilders;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// The approval rules (design 58 "Approval", PO decision Q6; orders.md scenarios 33–37): one finding per rule as a table,
/// the proposed status (ReadyForInvoicing, Archived when nothing is left to invoice), errors block while warnings pass,
/// the stable hash the client sends back, and the setting's csv. Risk class: High.
/// </summary>
public class ApprovalEvaluatorTests
{
    private static readonly ApprovalRule[] AllRules = Enum.GetValues<ApprovalRule>();

    /// <summary>An Open order with a price list: the clean case every rule passes.</summary>
    private static TransportOrder Priced()
    {
        var order = Order(OrderSource.Screen, Terms(priceLists: [Guid.NewGuid()]));
        order.Confirm("TO-2026-00001", TransportOrderRequiredFields.None, ActivityOf);
        return order;
    }

    private static ApprovalFacts Facts(
        TransportOrder? order = null, decimal? revenue = 1250m, long? version = 3, Dictionary<Guid, decimal>? byLine = null, bool documented = true,
        bool duplicate = false, IReadOnlyList<ApprovalLegFact>? legs = null, bool referencesExist = true)
    {
        order ??= Priced();
        byLine ??= order.Lines.ToDictionary(l => l.Id, _ => 100m);
        legs ??= order.Lines.Select(l => new ApprovalLegFact(l.Id, null, true)).ToList();
        return new ApprovalFacts(order, revenue, version, byLine, documented, duplicate, legs, referencesExist);
    }

    [Fact]
    public void A_clean_Open_order_passes_every_rule_and_is_proposed_ReadyForInvoicing()
    {
        var evaluation = ApprovalEvaluator.Evaluate(Facts(), AllRules);

        evaluation.Results.ShouldBeEmpty();
        evaluation.CanApprove.ShouldBeTrue();
        evaluation.ProposedStatus.ShouldBe(TransportOrderStatus.ReadyForInvoicing);
        evaluation.CurrentStatus.ShouldBe(TransportOrderStatus.Open);
    }

    [Fact]
    public void OrderMustBeOpen_is_an_error_for_a_Draft()
    {
        var result = ApprovalEvaluator.Evaluate(ApprovalRule.OrderMustBeOpen, Facts(Order())).ShouldNotBeNull();
        result.Severity.ShouldBe(ApprovalSeverity.Error);
        result.MessageKey.ShouldBe("Orders:Approval.OrderMustBeOpen");
        result.Data["status"].ShouldBe("Draft");
    }

    [Fact]
    public void ReferencesMustExist_fails_when_a_relation_disappeared()
    {
        ApprovalEvaluator.Evaluate(ApprovalRule.ReferencesMustExist, Facts(referencesExist: false)).ShouldNotBeNull().Severity.ShouldBe(ApprovalSeverity.Error);
        ApprovalEvaluator.Evaluate(ApprovalRule.ReferencesMustExist, Facts()).ShouldBeNull();
    }

    [Theory]
    [InlineData(0.49, true)]
    [InlineData(0.50, false)]
    [InlineData(1250.00, false)]
    public void NextInvoicePrice_proposes_the_archive_below_fifty_cents_scenario_37(double revenue, bool archived)
    {
        var result = ApprovalEvaluator.Evaluate(ApprovalRule.NextInvoicePrice, Facts(revenue: (decimal)revenue));
        if (archived)
        {
            result.ShouldNotBeNull();
            result.Severity.ShouldBe(ApprovalSeverity.Warning);
            result.ProposedStatus.ShouldBe(TransportOrderStatus.Archived);
            result.Data["amount"].ShouldBe("0.49");
            ApprovalEvaluator.Evaluate(Facts(revenue: (decimal)revenue), AllRules).ProposedStatus.ShouldBe(TransportOrderStatus.Archived);
        }
        else
        {
            result.ShouldBeNull();
        }
    }

    [Fact]
    public void NextInvoicePrice_treats_a_never_calculated_order_as_nothing_to_invoice()
    {
        ApprovalEvaluator.Evaluate(ApprovalRule.NextInvoicePrice, Facts(revenue: null, version: null)).ShouldNotBeNull().ProposedStatus.ShouldBe(TransportOrderStatus.Archived);
    }

    [Fact]
    public void FreightDocumentsAvailable_warns_only_when_documents_go_with_the_invoice_and_none_was_printed()
    {
        var withDocuments = Order(OrderSource.Screen, Terms(sendDocuments: true));
        withDocuments.Confirm("X", TransportOrderRequiredFields.None, ActivityOf);
        ApprovalEvaluator.Evaluate(ApprovalRule.FreightDocumentsAvailable, Facts(withDocuments, documented: false)).ShouldNotBeNull().Severity.ShouldBe(ApprovalSeverity.Warning);
        ApprovalEvaluator.Evaluate(ApprovalRule.FreightDocumentsAvailable, Facts(withDocuments, documented: true)).ShouldBeNull();
        ApprovalEvaluator.Evaluate(ApprovalRule.FreightDocumentsAvailable, Facts(documented: false)).ShouldBeNull("the order does not send documents");
    }

    [Fact]
    public void PriceListApplied_is_an_error_without_list_and_without_fixed_price()
    {
        ApprovalEvaluator.Evaluate(ApprovalRule.PriceListApplied, Facts(Confirmed())).ShouldNotBeNull().Severity.ShouldBe(ApprovalSeverity.Error);
        var listed = Order(OrderSource.Screen, Terms(priceLists: [Guid.NewGuid()]));
        ApprovalEvaluator.Evaluate(ApprovalRule.PriceListApplied, Facts(listed)).ShouldBeNull();
        var fixedPrice = Order(OrderSource.Screen, Terms(fixedPrice: 500m));
        ApprovalEvaluator.Evaluate(ApprovalRule.PriceListApplied, Facts(fixedPrice)).ShouldBeNull();
    }

    [Fact]
    public void PriceCalculationCorrect_is_an_error_while_no_calculation_ran()
    {
        ApprovalEvaluator.Evaluate(ApprovalRule.PriceCalculationCorrect, Facts(version: null)).ShouldNotBeNull().Severity.ShouldBe(ApprovalSeverity.Error);
        ApprovalEvaluator.Evaluate(ApprovalRule.PriceCalculationCorrect, Facts(version: 1)).ShouldBeNull();
    }

    [Fact]
    public void DuplicateCustomerReference_warns_with_the_reference()
    {
        var result = ApprovalEvaluator.Evaluate(ApprovalRule.DuplicateCustomerReference, Facts(duplicate: true)).ShouldNotBeNull();
        result.Severity.ShouldBe(ApprovalSeverity.Warning);
        result.Data["reference"].ShouldBe("PO-1");
    }

    [Fact]
    public void EmptyLinePrice_names_the_positions_without_revenue_unless_a_fixed_price_covers_them()
    {
        var order = Confirmed("X", Line(Load(), Unload()), Line(Load(), Unload()));
        var second = order.Lines.Single(l => l.Position == 2);
        var result = ApprovalEvaluator.Evaluate(ApprovalRule.EmptyLinePrice, Facts(order, byLine: new Dictionary<Guid, decimal> { [second.Id] = 80m })).ShouldNotBeNull();
        result.Severity.ShouldBe(ApprovalSeverity.Warning);
        result.Data["positions"].ShouldBe("1");

        var fixedPrice = Order(OrderSource.Screen, Terms(fixedPrice: 500m));
        ApprovalEvaluator.Evaluate(ApprovalRule.EmptyLinePrice, Facts(fixedPrice, byLine: new Dictionary<Guid, decimal>())).ShouldBeNull();
    }

    [Fact]
    public void FinancialDefaultsMismatch_passes_until_Financial_publishes_the_customer_defaults()
    {
        ApprovalEvaluator.Evaluate(ApprovalRule.FinancialDefaultsMismatch, Facts()).ShouldBeNull();
    }

    [Fact]
    public void FixedPriceFilled_errors_on_a_zero_fixed_price_only()
    {
        ApprovalEvaluator.Evaluate(ApprovalRule.FixedPriceFilled, Facts(Order(OrderSource.Screen, Terms(fixedPrice: 0m)))).ShouldNotBeNull().Severity.ShouldBe(ApprovalSeverity.Error);
        ApprovalEvaluator.Evaluate(ApprovalRule.FixedPriceFilled, Facts(Order(OrderSource.Screen, Terms(fixedPrice: 10m)))).ShouldBeNull();
        ApprovalEvaluator.Evaluate(ApprovalRule.FixedPriceFilled, Facts()).ShouldBeNull();
    }

    [Fact]
    public void LegDistanceMatchesLine_compares_the_lines_stored_distance_with_the_legs()
    {
        var order = Confirmed();
        var line = order.Lines.Single();
        line.WriteLegValues(12_000, null, null);
        var legs = new List<ApprovalLegFact> { new(line.Id, 7_000, true), new(line.Id, 6_000, true) };
        var result = ApprovalEvaluator.Evaluate(ApprovalRule.LegDistanceMatchesLine, Facts(order, legs: legs)).ShouldNotBeNull();
        result.Severity.ShouldBe(ApprovalSeverity.Warning);
        result.Data["positions"].ShouldBe("1");

        line.WriteLegValues(13_000, null, null);
        ApprovalEvaluator.Evaluate(ApprovalRule.LegDistanceMatchesLine, Facts(order, legs: legs)).ShouldBeNull();
        ApprovalEvaluator.Evaluate(ApprovalRule.LegDistanceMatchesLine, Facts(order, legs: [new ApprovalLegFact(line.Id, null, true)])).ShouldBeNull("unrouted legs are not compared");
    }

    [Fact]
    public void AllStopsArrived_is_an_error_until_every_leg_arrived_or_when_there_are_no_legs()
    {
        var order = Confirmed();
        var line = order.Lines.Single();
        ApprovalEvaluator.Evaluate(ApprovalRule.AllStopsArrived, Facts(order, legs: [new ApprovalLegFact(line.Id, null, false)])).ShouldNotBeNull().Severity.ShouldBe(ApprovalSeverity.Error);
        ApprovalEvaluator.Evaluate(ApprovalRule.AllStopsArrived, Facts(order, legs: [])).ShouldNotBeNull();
        ApprovalEvaluator.Evaluate(ApprovalRule.AllStopsArrived, Facts(order, legs: [new ApprovalLegFact(line.Id, null, true)])).ShouldBeNull();
    }

    [Fact]
    public void An_error_blocks_the_approval_while_warnings_let_it_pass_scenarios_35_and_36()
    {
        var warningOnly = ApprovalEvaluator.Evaluate(Facts(duplicate: true), [ApprovalRule.DuplicateCustomerReference, ApprovalRule.OrderMustBeOpen]);
        warningOnly.CanApprove.ShouldBeTrue();
        warningOnly.Results.ShouldHaveSingleItem().Severity.ShouldBe(ApprovalSeverity.Warning);

        var withError = ApprovalEvaluator.Evaluate(Facts(version: null), [ApprovalRule.PriceCalculationCorrect, ApprovalRule.DuplicateCustomerReference]);
        withError.CanApprove.ShouldBeFalse();
        withError.ProposedStatus.ShouldBe(TransportOrderStatus.ReadyForInvoicing, "the proposed status is what approval would give; CanApprove says it cannot");
    }

    [Fact]
    public void Only_the_active_rules_run_in_rule_order_and_the_default_setting_is_the_four_system_rules()
    {
        ApprovalEvaluator.ParseActiveRules(OrdersSettings.DefaultActiveApprovalRules)
            .ShouldBe([ApprovalRule.OrderMustBeOpen, ApprovalRule.ReferencesMustExist, ApprovalRule.NextInvoicePrice, ApprovalRule.FreightDocumentsAvailable]);
        ApprovalEvaluator.ParseActiveRules(" allstopsarrived, Bogus,, EmptyLinePrice ,EmptyLinePrice").ShouldBe([ApprovalRule.AllStopsArrived, ApprovalRule.EmptyLinePrice]);
        ApprovalEvaluator.ParseActiveRules(null).ShouldBeEmpty();

        var evaluation = ApprovalEvaluator.Evaluate(Facts(Order(), version: null, duplicate: true), [ApprovalRule.DuplicateCustomerReference, ApprovalRule.OrderMustBeOpen]);
        evaluation.Results.Select(r => r.Rule).ShouldBe([ApprovalRule.OrderMustBeOpen, ApprovalRule.DuplicateCustomerReference]);
    }

    [Fact]
    public void The_hash_is_stable_for_the_same_evaluation_and_changes_with_it_scenario_34()
    {
        var facts = Facts(duplicate: true);
        var first = ApprovalEvaluator.Hash([ApprovalEvaluator.Evaluate(facts, AllRules)]);
        var again = ApprovalEvaluator.Hash([ApprovalEvaluator.Evaluate(facts, AllRules)]);
        first.ShouldBe(again);
        first.Length.ShouldBe(64);

        var changed = ApprovalEvaluator.Hash([ApprovalEvaluator.Evaluate(Facts(duplicate: false), AllRules)]);
        changed.ShouldNotBe(first);

        var other = Confirmed("TO-2");
        var two = ApprovalEvaluator.Hash([ApprovalEvaluator.Evaluate(facts, AllRules), ApprovalEvaluator.Evaluate(Facts(other), AllRules)]);
        var twoReversed = ApprovalEvaluator.Hash([ApprovalEvaluator.Evaluate(Facts(other), AllRules), ApprovalEvaluator.Evaluate(facts, AllRules)]);
        two.ShouldBe(twoReversed, "order independent");
    }
}
