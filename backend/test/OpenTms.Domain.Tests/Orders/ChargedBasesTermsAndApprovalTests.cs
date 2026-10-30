using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Financial.Tariffs;
using OpenTms.Orders.TransportOrders.Approval;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Xunit;
using static OpenTms.Orders.TransportOrders.TransportOrderBuilders;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// Design 61 "Order page review" point 3, build notes 1 and 6, on the Orders entities: the order's and the template's terms
/// carry <c>chargedBases</c> (validated: revenue bases only, each once; read back in the legacy order; a legacy order reads
/// none), the template and the template made from an order copy them; the approval rule <c>FinancialDefaultsMismatch</c>
/// compares the effective sets (charged bases plus the bases of the lists) on both sides, so old settings with lists but
/// no stored bases give no false warning. Risk class: High (order chain), Critical where it feeds pricing.
/// </summary>
public class ChargedBasesTermsAndApprovalTests
{
    private static FinancialTermsInput TermsWith(IReadOnlyList<Guid>? priceLists = null, params QuantitySource[] bases)
        => Terms(priceLists: priceLists) with { ChargedBases = bases };

    [Fact]
    public void The_order_stores_the_charged_bases_and_reads_them_in_the_legacy_order()
    {
        var order = Order(OrderSource.Screen, TermsWith(null, QuantitySource.DistanceKm, QuantitySource.LineQuantity, QuantitySource.Epq));

        order.FinancialTerms.ChargedBases.ShouldBe([QuantitySource.LineQuantity, QuantitySource.Epq, QuantitySource.DistanceKm]);
        order.FinancialTermsInput().ChargedBases.ShouldBe([QuantitySource.LineQuantity, QuantitySource.Epq, QuantitySource.DistanceKm]);

        order.SetFinancialTerms(TermsWith(null, QuantitySource.WeightKg), Guid.NewGuid);
        order.FinancialTerms.ChargedBases.ShouldBe([QuantitySource.WeightKg], "a save replaces the bases");

        order.SetFinancialTerms(Terms(), Guid.NewGuid);
        order.FinancialTerms.ChargedBases.ShouldBeEmpty("terms without bases (a legacy client) switch every basis off");
        TransportOrderFinancialTerms.Default().ChargedBases.ShouldNotBeNull().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(QuantitySource.Manual)]
    [InlineData(QuantitySource.TripDistanceKm)]
    public void The_order_refuses_a_basis_that_is_not_a_revenue_basis(QuantitySource basis)
    {
        var refusal = Should.Throw<BusinessException>(() => Order(OrderSource.Screen, TermsWith(null, QuantitySource.LineQuantity, basis)));

        refusal.Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        refusal.Data["field"].ShouldBe("chargedBases");
    }

    [Fact]
    public void The_order_refuses_a_repeated_basis_and_keeps_its_terms()
    {
        var order = Order(OrderSource.Screen, TermsWith(null, QuantitySource.Colli));

        var refusal = Should.Throw<BusinessException>(() => order.SetFinancialTerms(TermsWith(null, QuantitySource.WeightKg, QuantitySource.WeightKg), Guid.NewGuid));

        refusal.Code.ShouldBe(ReferenceDataErrorCodes.DuplicateKey);
        refusal.Data["field"].ShouldBe("chargedBases");
        order.FinancialTerms.ChargedBases.ShouldBe([QuantitySource.Colli]);
    }

    [Fact]
    public void The_template_stores_the_charged_bases_and_a_template_from_an_order_copies_them()
    {
        var template = new TransportOrderTemplate(Guid.NewGuid(), TenantA, "Weekly Venlo", Header(), TermsWith(null, QuantitySource.TollDistanceKm, QuantitySource.Positions), TemplateSchedule.None(), Guid.NewGuid);
        template.FinancialTermsInput().ChargedBases.ShouldBe([QuantitySource.Positions, QuantitySource.TollDistanceKm], "materialisation starts from these terms");

        Should.Throw<BusinessException>(() => new TransportOrderTemplate(Guid.NewGuid(), TenantA, "Bad", Header(), TermsWith(null, QuantitySource.Manual), TemplateSchedule.None(), Guid.NewGuid))
            .Data["field"].ShouldBe("chargedBases");

        var order = Order(OrderSource.Screen, TermsWith(null, QuantitySource.VolumeM3, QuantitySource.LoadingMeters));
        var fromOrder = TransportOrderTemplate.FromOrder(Guid.NewGuid(), order, "From order", TimeZoneInfo.Utc, Guid.NewGuid);
        fromOrder.FinancialTermsInput().ChargedBases.ShouldBe([QuantitySource.VolumeM3, QuantitySource.LoadingMeters]);
    }

    // ── approval (build note 6) ─────────────────────────────────────────────────

    private static ApprovalFacts Facts(TransportOrder order, CustomerFinancialDefaults defaults, IReadOnlyDictionary<Guid, QuantitySource> bases)
        => new(order, 100m, 1, order.Lines.ToDictionary(l => l.Id, _ => 100m), true, false,
            order.Lines.Select(l => new ApprovalLegFact(l.Id, null, true)).ToList(), true, defaults, bases);

    private static TransportOrder Open(FinancialTermsInput terms)
    {
        var order = Order(OrderSource.Screen, terms);
        order.Confirm("TO-2026-00001", TransportOrderRequiredFields.None, ActivityOf);
        return order;
    }

    private static CustomerFinancialDefaults Defaults(IReadOnlyList<Guid> priceLists, params QuantitySource[] bases)
        => new("EUR", null, 30, priceLists, MultipleMatches.All, [], bases);

    private static ApprovalResult? Mismatch(ApprovalFacts facts) => ApprovalEvaluator.Evaluate(ApprovalRule.FinancialDefaultsMismatch, facts);

    [Fact]
    public void Old_settings_with_a_list_but_no_stored_bases_give_no_false_warning_for_an_order_with_that_list()
    {
        var quantityList = Guid.NewGuid();
        var bases = new Dictionary<Guid, QuantitySource> { [quantityList] = QuantitySource.LineQuantity };

        Mismatch(Facts(Open(TermsWith([quantityList])), Defaults([quantityList]), bases)).ShouldBeNull();
        Mismatch(Facts(Open(TermsWith([quantityList], QuantitySource.LineQuantity)), Defaults([quantityList]), bases))
            .ShouldBeNull("a basis with a list counts as on: switching its row on explicitly is the same terms");
        Mismatch(Facts(Open(TermsWith([quantityList])), Defaults([quantityList], QuantitySource.LineQuantity), bases)).ShouldBeNull();
    }

    [Fact]
    public void An_extra_charged_basis_on_the_order_warns_with_the_field_chargedBases()
    {
        var quantityList = Guid.NewGuid();
        var bases = new Dictionary<Guid, QuantitySource> { [quantityList] = QuantitySource.LineQuantity };

        var result = Mismatch(Facts(Open(TermsWith([quantityList], QuantitySource.WeightKg)), Defaults([quantityList]), bases)).ShouldNotBeNull();

        result.Severity.ShouldBe(ApprovalSeverity.Warning);
        result.Data["fields"].ShouldBe("chargedBases");
    }

    [Fact]
    public void A_basis_the_customer_charges_that_the_order_switched_off_warns()
    {
        var result = Mismatch(Facts(Open(TermsWith(null)), Defaults([], QuantitySource.DistanceKm), new Dictionary<Guid, QuantitySource>())).ShouldNotBeNull();

        result.Data["fields"].ShouldBe("chargedBases");
    }

    [Fact]
    public void The_same_effective_basis_reached_through_a_list_on_one_side_and_a_switch_on_the_other_agrees_on_the_bases()
    {
        var weightList = Guid.NewGuid();
        var bases = new Dictionary<Guid, QuantitySource> { [weightList] = QuantitySource.WeightKg };

        /* The customer charges weight without a list; the order has a weight list instead: the bases agree, the lists do not. */
        var result = Mismatch(Facts(Open(TermsWith([weightList])), Defaults([], QuantitySource.WeightKg), bases)).ShouldNotBeNull();

        result.Data["fields"].ShouldBe("priceListIds");
    }

    [Fact]
    public void A_list_whose_basis_is_unknown_adds_no_basis()
    {
        var unknownList = Guid.NewGuid();

        Mismatch(Facts(Open(TermsWith([unknownList])), Defaults([unknownList]), new Dictionary<Guid, QuantitySource>())).ShouldBeNull();
        Mismatch(Facts(Open(TermsWith([unknownList], QuantitySource.Colli)), Defaults([unknownList]), new Dictionary<Guid, QuantitySource>()))
            .ShouldNotBeNull().Data["fields"].ShouldBe("chargedBases");
    }

    [Fact]
    public void Defaults_without_charged_bases_information_compare_no_bases()
    {
        var defaults = new CustomerFinancialDefaults("EUR", null, 30, [], MultipleMatches.All, []);

        Mismatch(Facts(Open(TermsWith(null, QuantitySource.WeightKg)), defaults, new Dictionary<Guid, QuantitySource>())).ShouldBeNull();
    }
}
