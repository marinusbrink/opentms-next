using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Xunit;
using static OpenTms.Orders.TransportOrders.TransportOrderBuilders;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// The entity rules of the transport order (design 58 "Lifecycle", design 56 "Status machines"; orders.md scenarios
/// 4–9, 29–31, 42, 44–47): every transition and every refusal of the status machine, the Confirm checks (required
/// fields, ≥ 1 line, ≥ 2 stops), the numbering-period lock, the attribute bounds, the stop window rule, the line and
/// stop replacement semantics and the trip request bounds. Risk class: Critical.
/// </summary>
public class TransportOrderRuleTests
{
    // ── the status machine ───────────────────────────────────────────────────

    [Fact]
    public void A_new_order_is_a_Draft_without_number_and_Confirm_makes_it_Open_with_the_number()
    {
        var order = Order();
        order.Status.ShouldBe(TransportOrderStatus.Draft);
        order.Number.ShouldBeNull();

        order.Confirm("TO-2026-00007", TransportOrderRequiredFields.None, ActivityOf);

        order.Status.ShouldBe(TransportOrderStatus.Open);
        order.Number.ShouldBe("TO-2026-00007");
    }

    [Theory]
    [InlineData(TransportOrderStatus.Open)]
    [InlineData(TransportOrderStatus.ReadyForInvoicing)]
    [InlineData(TransportOrderStatus.Invoiced)]
    [InlineData(TransportOrderStatus.Archived)]
    [InlineData(TransportOrderStatus.Cancelled)]
    public void Confirm_is_refused_for_every_status_but_Draft(TransportOrderStatus status)
    {
        var order = InStatus(status);
        var ex = Should.Throw<BusinessException>(() => order.Confirm("X", TransportOrderRequiredFields.None, ActivityOf));
        ex.Code.ShouldBe(OrdersErrorCodes.InvalidTransition);
        ex.Data["from"].ShouldBe(status.ToString());
        ex.Data["to"].ShouldBe("Open");
    }

    [Theory]
    [InlineData(TransportOrderStatus.Draft)]
    [InlineData(TransportOrderStatus.Open)]
    public void Cancel_is_allowed_from_Draft_and_Open(TransportOrderStatus status)
    {
        var order = InStatus(status);
        order.Cancel();
        order.Status.ShouldBe(TransportOrderStatus.Cancelled);
    }

    [Theory]
    [InlineData(TransportOrderStatus.ReadyForInvoicing)]
    [InlineData(TransportOrderStatus.Invoiced)]
    [InlineData(TransportOrderStatus.Archived)]
    [InlineData(TransportOrderStatus.Cancelled)]
    public void Cancel_is_refused_once_the_order_left_Open(TransportOrderStatus status)
    {
        Should.Throw<BusinessException>(() => InStatus(status).Cancel()).Code.ShouldBe(OrdersErrorCodes.InvalidTransition);
    }

    [Theory]
    [InlineData(TransportOrderStatus.ReadyForInvoicing)]
    [InlineData(TransportOrderStatus.Archived)]
    public void Approve_moves_an_Open_order_to_the_proposed_status(TransportOrderStatus proposed)
    {
        var order = Confirmed();
        order.Approve(proposed);
        order.Status.ShouldBe(proposed);
    }

    [Theory]
    [InlineData(TransportOrderStatus.Open)]
    [InlineData(TransportOrderStatus.Invoiced)]
    [InlineData(TransportOrderStatus.Cancelled)]
    [InlineData(TransportOrderStatus.Draft)]
    public void Approve_refuses_a_target_that_is_not_ReadyForInvoicing_or_Archived(TransportOrderStatus target)
    {
        Should.Throw<BusinessException>(() => Confirmed().Approve(target)).Code.ShouldBe(OrdersErrorCodes.InvalidTransition);
    }

    [Theory]
    [InlineData(TransportOrderStatus.Draft)]
    [InlineData(TransportOrderStatus.ReadyForInvoicing)]
    [InlineData(TransportOrderStatus.Cancelled)]
    public void Approve_is_refused_when_the_order_is_not_Open(TransportOrderStatus status)
    {
        Should.Throw<BusinessException>(() => InStatus(status).Approve(TransportOrderStatus.ReadyForInvoicing)).Code.ShouldBe(OrdersErrorCodes.InvalidTransition);
    }

    [Theory]
    [InlineData(TransportOrderStatus.ReadyForInvoicing)]
    [InlineData(TransportOrderStatus.Archived)]
    public void Revise_returns_the_order_to_Open_and_keeps_the_reason_as_a_remark(TransportOrderStatus status)
    {
        var order = InStatus(status);
        var userId = Guid.NewGuid();
        var at = new DateTime(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);

        order.Revise("  Customer disputes the weight  ", userId, at, Guid.NewGuid());

        order.Status.ShouldBe(TransportOrderStatus.Open);
        var remark = order.Remarks.ShouldHaveSingleItem();
        remark.Kind.ShouldBe(OrderRemarkKind.Revision);
        remark.Text.ShouldBe("Customer disputes the weight");
        remark.UserId.ShouldBe(userId);
        remark.At.ShouldBe(at);
    }

    [Fact]
    public void Revise_from_Invoiced_is_refused_Q15_and_so_is_revising_an_Open_or_Draft_order()
    {
        Should.Throw<BusinessException>(() => InStatus(TransportOrderStatus.Invoiced).Revise("reason", null, DateTime.UtcNow, Guid.NewGuid())).Code.ShouldBe(OrdersErrorCodes.InvalidTransition);
        Should.Throw<BusinessException>(() => InStatus(TransportOrderStatus.Open).Revise("reason", null, DateTime.UtcNow, Guid.NewGuid())).Code.ShouldBe(OrdersErrorCodes.InvalidTransition);
        Should.Throw<BusinessException>(() => InStatus(TransportOrderStatus.Draft).Revise("reason", null, DateTime.UtcNow, Guid.NewGuid())).Code.ShouldBe(OrdersErrorCodes.InvalidTransition);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Revise_without_a_reason_is_refused_and_changes_nothing(string? reason)
    {
        var order = InStatus(TransportOrderStatus.ReadyForInvoicing);
        Should.Throw<BusinessException>(() => order.Revise(reason, null, DateTime.UtcNow, Guid.NewGuid())).Code.ShouldBe(OrdersErrorCodes.RevisionReasonRequired);
        order.Status.ShouldBe(TransportOrderStatus.ReadyForInvoicing);
        order.Remarks.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(TransportOrderStatus.ReadyForInvoicing)]
    [InlineData(TransportOrderStatus.Invoiced)]
    public void Archive_is_allowed_from_ReadyForInvoicing_and_Invoiced(TransportOrderStatus status)
    {
        var order = InStatus(status);
        order.Archive();
        order.Status.ShouldBe(TransportOrderStatus.Archived);
    }

    [Theory]
    [InlineData(TransportOrderStatus.Draft)]
    [InlineData(TransportOrderStatus.Open)]
    [InlineData(TransportOrderStatus.Cancelled)]
    [InlineData(TransportOrderStatus.Archived)]
    public void Archive_is_refused_otherwise(TransportOrderStatus status)
    {
        Should.Throw<BusinessException>(() => InStatus(status).Archive()).Code.ShouldBe(OrdersErrorCodes.InvalidTransition);
    }

    [Fact]
    public void Invoiced_is_written_from_ReadyForInvoicing_only()
    {
        var order = InStatus(TransportOrderStatus.ReadyForInvoicing);
        order.MarkInvoiced();
        order.Status.ShouldBe(TransportOrderStatus.Invoiced);
        Should.Throw<BusinessException>(() => InStatus(TransportOrderStatus.Open).MarkInvoiced()).Code.ShouldBe(OrdersErrorCodes.InvalidTransition);
    }

    [Theory]
    [InlineData(TransportOrderStatus.ReadyForInvoicing)]
    [InlineData(TransportOrderStatus.Invoiced)]
    [InlineData(TransportOrderStatus.Archived)]
    [InlineData(TransportOrderStatus.Cancelled)]
    public void Only_a_Draft_or_Open_order_is_editable_or_deletable(TransportOrderStatus status)
    {
        var order = InStatus(status);
        order.IsEditable.ShouldBeFalse();
        Should.Throw<BusinessException>(order.EnsureEditable).Code.ShouldBe(OrdersErrorCodes.OrderNotEditable);
        Should.Throw<BusinessException>(order.EnsureDeletable).Code.ShouldBe(OrdersErrorCodes.InvalidTransition);
        InStatus(TransportOrderStatus.Draft).IsEditable.ShouldBeTrue();
        InStatus(TransportOrderStatus.Open).IsEditable.ShouldBeTrue();
    }

    // ── the Confirm checks ───────────────────────────────────────────────────

    [Fact]
    public void Confirm_needs_at_least_one_line_scenario_6()
    {
        var order = new TransportOrder(Guid.NewGuid(), TenantA, Header(), Terms(), OrderSource.Screen, Guid.NewGuid);
        var ex = Should.Throw<BusinessException>(() => order.Confirm("X", TransportOrderRequiredFields.None, ActivityOf));
        ex.Code.ShouldBe(OrdersErrorCodes.LineRequired);
        order.Status.ShouldBe(TransportOrderStatus.Draft);
        order.Number.ShouldBeNull();
    }

    [Fact]
    public void Confirm_needs_two_stops_per_line_scenario_5_but_a_Draft_saves_with_one()
    {
        var order = Order(OrderSource.Screen, null, Line(Load(), Unload()), Line(Load()));
        order.Lines.Count.ShouldBe(2, "a Draft keeps an incomplete line (Assumption 6)");

        var ex = Should.Throw<BusinessException>(() => order.Confirm("X", TransportOrderRequiredFields.None, ActivityOf));
        ex.Code.ShouldBe(OrdersErrorCodes.LineNeedsTwoStops);
        ex.Data["position"].ShouldBe(2);
    }

    [Theory]
    [InlineData(TransportOrderRequiredFields.CustomerReference, "customerReference")]
    [InlineData(TransportOrderRequiredFields.WeightPerLine, "weightKg")]
    [InlineData(TransportOrderRequiredFields.ProductPerLine, "productId")]
    [InlineData(TransportOrderRequiredFields.VolumePerLine, "volumeM3")]
    [InlineData(TransportOrderRequiredFields.LoadingMetersPerLine, "loadingMeters")]
    [InlineData(TransportOrderRequiredFields.Temperature, "temperatureId")]
    [InlineData(TransportOrderRequiredFields.LoadingTimeWindow, "loadingTimeWindow")]
    [InlineData(TransportOrderRequiredFields.UnloadingTimeWindow, "unloadingTimeWindow")]
    public void Confirm_names_the_first_required_field_of_the_type_that_is_empty_scenario_4(TransportOrderRequiredFields required, string field)
    {
        var order = new TransportOrder(Guid.NewGuid(), TenantA, Header(customerReference: null), Terms(), OrderSource.Screen, Guid.NewGuid);
        order.SetLines([Line(Freight(weightKg: null), Load(reference: null), Unload(reference: null))], Guid.NewGuid);

        var ex = Should.Throw<BusinessException>(() => order.Confirm("X", required, ActivityOf));
        ex.Code.ShouldBe(OrdersErrorCodes.RequiredFieldMissing);
        ex.Data["field"].ShouldBe(field);
    }

    [Fact]
    public void Required_references_and_windows_look_at_the_stops_activity()
    {
        var order = Order(OrderSource.Screen, null, Line(Load(reference: "L", from: new TimeOnly(8, 0), till: new TimeOnly(12, 0)), Unload(reference: null)));
        var required = TransportOrderRequiredFields.LoadingReference | TransportOrderRequiredFields.LoadingTimeWindow | TransportOrderRequiredFields.UnloadingReference;

        var ex = Should.Throw<BusinessException>(() => order.Confirm("X", required, ActivityOf));
        ex.Data["field"].ShouldBe("unloadingReference");

        var complete = Order(OrderSource.Screen, null, Line(Load(reference: "L", from: new TimeOnly(8, 0), till: new TimeOnly(12, 0)), Unload(reference: "U")));
        complete.Confirm("X", required, ActivityOf);
        complete.Status.ShouldBe(TransportOrderStatus.Open);
    }

    // ── header, numbering period, attributes ─────────────────────────────────

    [Fact]
    public void A_numbered_order_cannot_move_its_date_out_of_the_numbering_period_scenario_8()
    {
        var yearly = NumberSequenceFormat.Create("TO", includeYear: true, includeMonth: false, separator: "-", digits: 5);
        var order = Confirmed();

        order.SetHeader(Header(new DateOnly(2026, 12, 31)), yearly);
        order.OrderDate.ShouldBe(new DateOnly(2026, 12, 31));

        var ex = Should.Throw<BusinessException>(() => order.SetHeader(Header(new DateOnly(2027, 1, 1)), yearly));
        ex.Code.ShouldBe(OrdersErrorCodes.OrderDatePeriodLocked);
        order.OrderDate.ShouldBe(new DateOnly(2026, 12, 31));

        var draft = Order();
        draft.SetHeader(Header(new DateOnly(2027, 1, 1)), yearly);
        draft.OrderDate.ShouldBe(new DateOnly(2027, 1, 1), "a Draft has no number and no lock");

        var monthly = NumberSequenceFormat.Create("TO", includeYear: true, includeMonth: true, separator: "-", digits: 5);
        Should.Throw<BusinessException>(() => order.SetHeader(Header(new DateOnly(2026, 11, 1)), monthly)).Code.ShouldBe(OrdersErrorCodes.OrderDatePeriodLocked);
    }

    [Fact]
    public void The_header_requires_a_type_and_a_customer_and_trims_its_texts()
    {
        Should.Throw<BusinessException>(() => Order().SetHeader(Header() with { TransportOrderTypeId = Guid.Empty }, null)).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        Should.Throw<BusinessException>(() => Order().SetHeader(Header() with { CustomerRelationId = Guid.Empty }, null)).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);

        var order = Order();
        order.SetHeader(Header() with { CustomerReference = "  PO-9  ", InternalRemarks = " " }, null);
        order.CustomerReference.ShouldBe("PO-9");
        order.InternalRemarks.ShouldBeNull();
        Should.Throw<BusinessException>(() => order.SetHeader(Header() with { CustomerReference = new string('x', 129) }, null)).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
    }

    [Fact]
    public void Attributes_are_bounded_trimmed_and_empty_values_drop_the_key_Q12()
    {
        var order = Order();
        order.SetAttributes(new Dictionary<string, string> { [" Project "] = " Najaar ", ["Empty"] = "  " });
        order.Attributes.ShouldBe(new Dictionary<string, string> { ["Project"] = "Najaar" });

        Should.Throw<BusinessException>(() => order.SetAttributes(Attributes(21))).Code.ShouldBe(OrdersErrorCodes.InvalidAttributes);
        order.SetAttributes(Attributes(20));
        order.Attributes.Count.ShouldBe(20);
        Should.Throw<BusinessException>(() => order.SetAttributes(new Dictionary<string, string> { [new string('k', 33)] = "v" })).Code.ShouldBe(OrdersErrorCodes.InvalidAttributes);
        Should.Throw<BusinessException>(() => order.SetAttributes(new Dictionary<string, string> { ["k"] = new string('v', 501) })).Code.ShouldBe(OrdersErrorCodes.InvalidAttributes);
        order.SetAttributes(new Dictionary<string, string> { ["k"] = new string('v', 500) });
    }

    [Fact]
    public void Draft_confidence_is_a_fraction_and_the_copy_index_needs_a_template()
    {
        var order = Order(OrderSource.Agent);
        order.SetDraftConfidence(0.87m);
        order.DraftConfidence.ShouldBe(0.87m);
        Should.Throw<BusinessException>(() => order.SetDraftConfidence(1.5m)).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);

        order.SetSourceTemplate(null, 3);
        order.CopyIndex.ShouldBe(0, "no template, no copy index");
        var template = Guid.NewGuid();
        order.SetSourceTemplate(template, 3);
        (order.SourceTemplateId, order.CopyIndex).ShouldBe((template, 3));
        Should.Throw<BusinessException>(() => order.SetSourceTemplate(template, -1)).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
    }

    // ── lines and stops ──────────────────────────────────────────────────────

    [Fact]
    public void Lines_without_stops_start_with_the_pattern_dated_from_the_order_date_Assumption_1()
    {
        var order = Order();
        order.SetDefaultStops([
            new DefaultStopInput(LoadType, DepotId, DepotAddressId, null, 0, null, null, null, 30),
            new DefaultStopInput(UnloadType, ShopId, ShopAddressId, null, 2, null, null, null, null),
            new DefaultStopInput(UnloadType, null, null, null, 3, null, null, null, null)
        ]);

        var pattern = order.StopsFromPattern();
        pattern.Count.ShouldBe(2, "a pattern stop without relation is a placeholder the clerk fills");
        pattern[0].Date.ShouldBe(new DateOnly(2026, 10, 20));
        pattern[1].Date.ShouldBe(new DateOnly(2026, 10, 22));
        pattern[1].RelationAddressId.ShouldBe(ShopAddressId);
    }

    [Fact]
    public void Replacing_lines_matches_on_id_renumbers_and_reports_what_changed()
    {
        var order = Order(OrderSource.Screen, null, Line(Load(), Unload()), Line(Freight(quantity: 5), Load(), Unload()));
        var first = order.Lines.Single(l => l.Position == 1);
        var second = order.Lines.Single(l => l.Position == 2);
        var firstStops = first.Stops.OrderBy(s => s.Position).Select(s => s.ToInput()).ToList();

        /* Second line first (reordered), first line with a changed stop date, a third line new. */
        var changes = order.SetLines([
            new LineInput(second.Id, Freight(quantity: 6), [], [], second.Stops.OrderBy(s => s.Position).Select(s => s.ToInput()).ToList(), null),
            new LineInput(first.Id, Freight(), [], [], [firstStops[0], firstStops[1] with { Date = new DateOnly(2026, 10, 23) }], null),
            Line(Load(), Unload())
        ], Guid.NewGuid);

        order.Lines.Count.ShouldBe(3);
        order.Lines.Single(l => l.Id == second.Id).Position.ShouldBe(1);
        order.Lines.Single(l => l.Id == second.Id).Freight.Quantity.ShouldBe(6m);
        order.Lines.Single(l => l.Id == first.Id).Position.ShouldBe(2);
        changes.LineIds.Count.ShouldBe(3);
        changes.StopChangedLineIds.ShouldContain(first.Id, "its stop date moved");
        changes.StopChangedLineIds.ShouldNotContain(second.Id, "only its quantity changed");
        changes.StopChangedLineIds.Count.ShouldBe(2, "the new line counts as changed stops");
        changes.RemovedLineIds.ShouldBeEmpty();

        var removal = order.SetLines([order.Lines.Single(l => l.Id == second.Id).ToInput()], Guid.NewGuid);
        removal.RemovedLineIds.Count.ShouldBe(2);
        order.Lines.ShouldHaveSingleItem().Position.ShouldBe(1);
    }

    [Fact]
    public void Removing_a_line_renumbers_the_rest_and_the_line_count_is_bounded()
    {
        var order = Order(OrderSource.Screen, null, Line(Load(), Unload()), Line(Load(), Unload()), Line(Load(), Unload()));
        var middle = order.Lines.Single(l => l.Position == 2);
        order.RemoveLine(middle.Id);
        order.Lines.OrderBy(l => l.Position).Select(l => l.Position).ShouldBe([1, 2]);

        var tooMany = Enumerable.Range(0, OrdersConsts.MaxLines + 1).Select(_ => Line(Load(), Unload())).ToList();
        Should.Throw<BusinessException>(() => order.SetLines(tooMany, Guid.NewGuid)).Code.ShouldBe(ReferenceDataErrorCodes.TooManyChildren);
    }

    [Fact]
    public void A_stop_takes_a_slot_or_a_window_never_both_and_a_window_needs_both_ends_Assumption_2()
    {
        var slot = Guid.NewGuid();
        Should.Throw<BusinessException>(() => Order(OrderSource.Screen, null, Line(Load(timeSlotId: slot, from: new TimeOnly(8, 0), till: new TimeOnly(10, 0)), Unload())))
            .Code.ShouldBe(OrdersErrorCodes.StopWindowAmbiguous);
        Should.Throw<BusinessException>(() => Order(OrderSource.Screen, null, Line(Load(from: new TimeOnly(8, 0)), Unload())))
            .Code.ShouldBe(OrdersErrorCodes.StopWindowAmbiguous);

        var order = Order(OrderSource.Screen, null, Line(Load(timeSlotId: slot), Unload()));
        order.Lines.Single().Stops.OrderBy(s => s.Position).First().Details.TimeSlotId.ShouldBe(slot);
    }

    [Fact]
    public void Measurements_are_three_per_kind_and_unique_characteristics_are_bounded()
    {
        var order = Order();
        var line = order.Lines.Single();
        line.SetMeasurements([new MeasurementInput(MeasurementKind.WeightKg, 1, 100), new MeasurementInput(MeasurementKind.WeightKg, 2, 101), new MeasurementInput(MeasurementKind.VolumeM3, 1, 2.5)]);
        line.Measurements.Count.ShouldBe(3);
        line.SetMeasurements([new MeasurementInput(MeasurementKind.WeightKg, 1, 99)]);
        line.Measurements.ShouldHaveSingleItem().Value.ShouldBe(99);
        Should.Throw<BusinessException>(() => line.SetMeasurements([new MeasurementInput(MeasurementKind.WeightKg, 4, 1)])).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        Should.Throw<BusinessException>(() => line.SetMeasurements([new MeasurementInput(MeasurementKind.WeightKg, 1, 1), new MeasurementInput(MeasurementKind.WeightKg, 1, 2)])).Code.ShouldBe(ReferenceDataErrorCodes.DuplicateKey);

        var characteristic = Guid.NewGuid();
        line.SetRequiredTransportCharacteristics([characteristic, characteristic, Guid.Empty]);
        line.RequiredTransportCharacteristics.ShouldHaveSingleItem().TransportCharacteristicId.ShouldBe(characteristic);
        Should.Throw<BusinessException>(() => line.SetRequiredTransportCharacteristics(Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()).ToList())).Code.ShouldBe(ReferenceDataErrorCodes.TooManyChildren);
    }

    [Fact]
    public void The_freight_refuses_negative_quantities_and_an_empty_transport_unit()
    {
        Should.Throw<BusinessException>(() => FreightDetails.From(Freight() with { Quantity = -1 })).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        Should.Throw<BusinessException>(() => FreightDetails.From(Freight() with { WeightKg = -5 })).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        Should.Throw<BusinessException>(() => FreightDetails.From(Freight() with { TransportUnitId = Guid.Empty })).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        Should.Throw<BusinessException>(() => FreightDetails.From(Freight() with { PaymentInstruction = (PaymentInstruction)9 })).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
    }

    // ── references, terms, trip request ──────────────────────────────────────

    [Fact]
    public void References_are_positioned_bounded_and_require_type_and_value()
    {
        var order = Order();
        order.SetReferences([new ReferenceInput("PO", "123", null), new ReferenceInput("Load", "L-9", "Loading ref")], Guid.NewGuid);
        order.References.OrderBy(r => r.Position).Select(r => (r.Position, r.Type, r.Value)).ShouldBe([(1, "PO", "123"), (2, "Load", "L-9")]);
        Should.Throw<BusinessException>(() => order.SetReferences([new ReferenceInput("", "x", null)], Guid.NewGuid)).Code.ShouldBe(ReferenceDataErrorCodes.NameRequired);
        Should.Throw<BusinessException>(() => order.SetReferences(Enumerable.Range(0, 51).Select(i => new ReferenceInput("T", i.ToString(), null)).ToList(), Guid.NewGuid)).Code.ShouldBe(ReferenceDataErrorCodes.TooManyChildren);
    }

    [Fact]
    public void Financial_terms_keep_price_lists_in_position_order_and_refuse_duplicates_and_bad_currencies()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var ds = Guid.NewGuid();
        var order = Order();

        order.SetFinancialTerms(new FinancialTermsInput("eur", null, 14, [b, a], Financial.Tariffs.MultipleMatches.HighestWins, [new DiscountOrSurchargeInput(ds, DiscountOrSurchargeSource.Default)], 250.50m, true), Guid.NewGuid);

        order.FinancialTerms.CurrencyCode.ShouldBe("EUR");
        order.PriceLists.OrderBy(p => p.Position).Select(p => p.PriceListId).ShouldBe([b, a]);
        order.DiscountsOrSurcharges.ShouldHaveSingleItem().Source.ShouldBe(DiscountOrSurchargeSource.Default);
        order.FinancialTerms.FixedPriceAmount.ShouldBe(250.50m);

        order.SetFinancialTerms(new FinancialTermsInput("EUR", null, null, [a], Financial.Tariffs.MultipleMatches.All, [], null, false), Guid.NewGuid);
        order.PriceLists.ShouldHaveSingleItem().PriceListId.ShouldBe(a);
        order.DiscountsOrSurcharges.ShouldBeEmpty();

        Should.Throw<BusinessException>(() => order.SetFinancialTerms(Terms(priceLists: [a, a]), Guid.NewGuid)).Code.ShouldBe(ReferenceDataErrorCodes.DuplicateKey);
        Should.Throw<BusinessException>(() => order.SetFinancialTerms(Terms() with { CurrencyCode = "EURO" }, Guid.NewGuid)).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        Should.Throw<BusinessException>(() => order.SetFinancialTerms(Terms(fixedPrice: -1), Guid.NewGuid)).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
    }

    [Fact]
    public void The_trip_request_names_at_most_three_vehicles_and_drivers_and_keeps_the_trip_Planning_reported_Q9()
    {
        var order = Order();
        var request = new TripRequestInput(Guid.NewGuid(), DepotId, DepotAddressId, ShopId, ShopAddressId,
            new DateTimeOffset(2026, 10, 20, 6, 0, 0, TimeSpan.FromHours(2)), new DateTimeOffset(2026, 10, 20, 16, 0, 0, TimeSpan.FromHours(2)),
            null, null, null, [Guid.NewGuid(), Guid.NewGuid()], [Guid.NewGuid()], 450m, "eur");

        order.SetTripRequest(request);
        order.TripRequest.ShouldNotBeNull();
        order.TripRequest.VehicleIds.Count.ShouldBe(2);
        order.TripRequest.AgreedPurchaseCurrencyCode.ShouldBe("EUR");

        var tripId = Guid.NewGuid();
        order.RecordTrip(tripId).ShouldBeTrue();
        order.RecordTrip(tripId).ShouldBeFalse("idempotent: the same trip again changes nothing");
        order.SetTripRequest(request with { AgreedPurchaseAmount = 500m });
        order.TripRequest!.TripId.ShouldBe(tripId, "a changed request keeps the trip Planning created");

        Should.Throw<BusinessException>(() => order.SetTripRequest(request with { VehicleIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()] })).Code.ShouldBe(OrdersErrorCodes.TooManyTripRequestMembers);
        Should.Throw<BusinessException>(() => order.SetTripRequest(request with { PlannedArrival = request.PlannedDeparture.AddHours(-1) })).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);

        order.SetTripRequest(null);
        order.TripRequest.ShouldBeNull();
        order.RecordTrip(tripId).ShouldBeFalse("a removed request records nothing");
    }

    [Fact]
    public void The_trip_request_takes_freight_items_of_the_order_only_and_loses_removed_lines_design_61_point_7()
    {
        var order = Order(OrderSource.Screen, null, Line(Load(), Unload()), Line(Load(), Unload()), Line(Load(), Unload()));
        var lines = order.Lines.OrderBy(l => l.Position).Select(l => l.Id).ToList();
        var request = new TripRequestInput(Guid.NewGuid(), DepotId, DepotAddressId, ShopId, ShopAddressId,
            new DateTimeOffset(2026, 10, 20, 6, 0, 0, TimeSpan.FromHours(2)), null, null, null, null, [], [], null, null);

        order.SetTripRequest(request);
        order.TripRequest!.LineIds.ShouldBeNull("no selection: every line goes on the trip");
        order.SetTripRequest(request with { LineIds = [lines[0], lines[1], lines[0], Guid.Empty] });
        order.TripRequest!.LineIds.ShouldBe([lines[0], lines[1]], "distinct, without empty ids");
        order.TripRequest.ToInput().LineIds.ShouldBe([lines[0], lines[1]]);
        order.TripRequest.Covers([lines[2]]).ShouldBeFalse();
        order.TripRequest.Covers([lines[1], lines[2]]).ShouldBeTrue();

        var unknown = Should.Throw<BusinessException>(() => order.SetTripRequest(request with { LineIds = [lines[0], Guid.NewGuid()] }));
        unknown.Code.ShouldBe(OrdersErrorCodes.TripRequestLineUnknown);
        unknown.Data["field"].ShouldBe("lineIds");
        order.TripRequest!.LineIds.ShouldBe([lines[0], lines[1]], "a refused request leaves the stored one as it was");

        /* A removed line leaves the selection, through the full replace and through the single delete. */
        order.SetLines(order.Lines.Where(l => l.Id != lines[0]).OrderBy(l => l.Position).Select(l => l.ToInput()).ToList(), Guid.NewGuid);
        order.TripRequest!.LineIds.ShouldBe([lines[1]]);
        order.RemoveLine(lines[1]);
        order.TripRequest!.LineIds.ShouldNotBeNull().ShouldBeEmpty("the last selected line went: no line, not every line");

        /* Every line stays every line when lines go. */
        order.SetTripRequest(request);
        order.RemoveLine(lines[2]);
        order.TripRequest!.LineIds.ShouldBeNull();
    }

    [Fact]
    public void A_drafts_trip_request_names_its_lines_by_their_posted_ids_and_follows_them_to_the_new_ids()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var mapped = Guid.NewGuid();
        var other = Guid.NewGuid();

        TripRequestLines.Remap([second, other], [first, second], [Guid.NewGuid(), mapped]).ShouldBe([mapped, other], "an id that names no posted line is kept, so validation refuses it");
        TripRequestLines.Remap(null, [first], [mapped]).ShouldBeNull();
        TripRequestLines.Normalize([]).ShouldNotBeNull().ShouldBeEmpty("in the domain an empty list is a selection of no line; the API maps empty to null");
    }

    [Fact]
    public void The_summary_is_written_by_the_calculator_only_through_WriteSummary()
    {
        var order = Confirmed();
        var at = new DateTime(2026, 10, 25, 10, 0, 0, DateTimeKind.Utc);
        order.WriteSummary(new OrderSummary(1250m, 900m, 350m, 12.5m, ExecutionProgress.Planned, 1, 2, "DC Venlo", "Venlo", new DateOnly(2026, 10, 20), "Supermarkt", "Utrecht", new DateOnly(2026, 10, 21), "R2026-0421"), at);

        (order.RevenueAmount, order.CostOfRevenueAmount, order.MarginAmount, order.Co2Kg).ShouldBe((1250m, 900m, 350m, 12.5m));
        order.ExecutionProgress.ShouldBe(ExecutionProgress.Planned);
        (order.FirstStopName, order.LastStopCity, order.TripNumbers, order.CalculatedAt).ShouldBe(("DC Venlo", "Utrecht", "R2026-0421", at));
    }

    [Fact]
    public void The_order_type_header_input_round_trips()
    {
        var order = Confirmed();
        var header = order.HeaderFor();
        header.CustomerName.ShouldBe("Klant Foods B.V.");
        header.OrderDate.ShouldBe(new DateOnly(2026, 10, 20));
        order.FinancialTermsInput().CurrencyCode.ShouldBe("EUR");
        order.StopEnds().StopCount.ShouldBe(2);
    }
}
