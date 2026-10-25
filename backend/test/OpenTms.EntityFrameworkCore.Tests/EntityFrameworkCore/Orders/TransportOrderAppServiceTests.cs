using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Orders;
using OpenTms.Orders.Events;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Orders;

/// <summary>
/// TransportOrderAppService on SQLite (design 58; orders.md scenarios 1–17, 29–47): create as Draft or Open, the Confirm
/// rules and the number, the saves per part with their events and derived-value requests, the lifecycle commands with
/// their refusals (locks through Planning's legs), approval with the stale hash, the list's views, filters, search and
/// sort, the price preview, and tenant isolation. Risk class: Critical.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class TransportOrderAppServiceTests : TransportOrderTestBase<OpenTmsEntityFrameworkCoreTestModule>
{
    // ── create and confirm ───────────────────────────────────────────────────

    [Fact]
    public async Task Create_saves_a_Draft_without_number_with_the_types_pattern_and_asks_for_a_price_scenario_1()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();

        var order = await Orders.CreateAsync(CreateDto(f));

        order.Status.ShouldBe(TransportOrderStatus.Draft);
        order.Number.ShouldBeNull();
        order.CustomerName.ShouldBe("Klant Foods B.V.");
        order.TransportOrderTypeName.ShouldBe("Standard");
        order.DefaultStops.Select(s => s.WaypointTypeName).ShouldBe(["Load", "Unload"], "the type's default waypoints are the order's pattern");
        var line = order.Lines.ShouldHaveSingleItem();
        line.Position.ShouldBe(1);
        line.TransportUnitName.ShouldBe("Europallet");
        line.ProductName.ShouldBe("Zuivel");
        line.Stops.Select(s => (s.Position, s.RelationName, s.City, s.WaypointTypeName)).ShouldBe([(1, "DC Venlo", "Venlo", "Load"), (2, "Supermarkt Utrecht", "Utrecht", "Unload")]);
        line.Stops[0].TimeSlotName.ShouldBe("Morning");
        order.FinancialTerms.VatPercentageName.ShouldBe("Hoog");
        order.FinancialTerms.PriceListNames.ShouldBe(["Tarieven 2026"]);
        order.References.ShouldHaveSingleItem().Value.ShouldBe("123");
        order.Summary.ExecutionProgress.ShouldBe(ExecutionProgress.Unplanned);

        Events.Of<TransportOrderLinesChangedEto>(order.Id).ShouldHaveSingleItem().LineIds.ShouldBe([line.Id!.Value]);
        Events.Of<TransportOrderConfirmedEto>(order.Id).ShouldBeEmpty();
        DerivedValues.ReasonsFor(order.Id).ShouldBe(["Created"]);
    }

    [Fact]
    public async Task Create_with_confirm_draws_the_number_and_publishes_the_confirm_events_scenario_7_and_30()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();

        var first = await Orders.CreateAsync(CreateDto(f, confirm: true));
        var second = await Orders.CreateAsync(CreateDto(f, confirm: true));

        first.Status.ShouldBe(TransportOrderStatus.Open);
        first.Number.ShouldBe($"TO-{Today.Year}-00001");
        second.Number.ShouldBe($"TO-{Today.Year}-00002");
        Events.Of<TransportOrderConfirmedEto>(first.Id).ShouldHaveSingleItem();
        Events.Of<TransportOrderStopsChangedEto>(first.Id).ShouldHaveSingleItem().LineIds.ShouldBe(first.Lines.Select(l => l.Id!.Value));
        DerivedValues.ReasonsFor(first.Id).ShouldBe(["Confirmed"]);
    }

    [Fact]
    public async Task Confirm_refuses_an_incomplete_Draft_before_it_draws_a_number_scenarios_4_5_6()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync(TransportOrderRequiredFields.CustomerReference);

        var dto = CreateDto(f, customerReference: null);
        dto.Lines[0].Stops!.RemoveAt(1);
        var draft = await Orders.CreateAsync(dto);
        draft.Status.ShouldBe(TransportOrderStatus.Draft, "a Draft saves incomplete (Assumption 6)");

        (await Should.ThrowAsync<BusinessException>(() => Orders.ConfirmAsync(draft.Id))).Code.ShouldBe(OrdersErrorCodes.LineNeedsTwoStops);
        await Orders.UpdateLinesAsync(draft.Id, new TransportOrderLinesUpdateDto { Lines = [Line(f)], ConcurrencyStamp = (await Orders.GetAsync(draft.Id)).ConcurrencyStamp });
        var ex = await Should.ThrowAsync<BusinessException>(() => Orders.ConfirmAsync(draft.Id));
        ex.Code.ShouldBe(OrdersErrorCodes.RequiredFieldMissing);
        ex.Data["field"].ShouldBe("customerReference");

        var reloaded = await Orders.GetAsync(draft.Id);
        await Orders.UpdateAsync(draft.Id, HeaderUpdate(reloaded, u => { u.CustomerReference = "PO-77"; }));
        var confirmed = await Orders.ConfirmAsync(draft.Id);
        confirmed.Number.ShouldBe($"TO-{Today.Year}-00001", "the refused confirms drew no number");
        (await Should.ThrowAsync<BusinessException>(() => Orders.ConfirmAsync(draft.Id))).Code.ShouldBe(OrdersErrorCodes.InvalidTransition);

        var empty = await Orders.CreateAsync(CreateDto(f, lines: 0));
        (await Should.ThrowAsync<BusinessException>(() => Orders.ConfirmAsync(empty.Id))).Code.ShouldBe(OrdersErrorCodes.LineRequired);
    }

    [Fact]
    public async Task Unknown_or_mismatched_ids_are_refused_through_the_published_lookups()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();

        var unknownUnit = CreateDto(f);
        unknownUnit.Lines[0].TransportUnitId = Guid.NewGuid();
        (await Should.ThrowAsync<BusinessException>(() => Orders.CreateAsync(unknownUnit))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);

        var unknownCustomer = CreateDto(f);
        unknownCustomer.CustomerRelationId = Guid.NewGuid();
        (await Should.ThrowAsync<BusinessException>(() => Orders.CreateAsync(unknownCustomer))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);

        var wrongAddress = CreateDto(f);
        wrongAddress.Lines[0].Stops![0].RelationAddressId = f.ShopAddressId;
        (await Should.ThrowAsync<BusinessException>(() => Orders.CreateAsync(wrongAddress))).Code.ShouldBe(OrdersErrorCodes.StopAddressMismatch);

        var unknownVat = CreateDto(f);
        unknownVat.FinancialTerms!.VatPercentageId = Guid.NewGuid();
        (await Should.ThrowAsync<BusinessException>(() => Orders.CreateAsync(unknownVat))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);

        var unknownType = CreateDto(f);
        unknownType.TransportOrderTypeId = Guid.NewGuid();
        (await Should.ThrowAsync<BusinessException>(() => Orders.CreateAsync(unknownType))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
    }

    // ── saves per part ───────────────────────────────────────────────────────

    [Fact]
    public async Task Header_update_checks_the_stamp_the_numbering_period_and_publishes_when_pricing_inputs_change_scenario_8()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var order = await Orders.CreateAsync(CreateDto(f, confirm: true, orderDate: new DateOnly(Today.Year, 6, 15)));

        var updated = await Orders.UpdateAsync(order.Id, HeaderUpdate(order, u => { u.CustomerReference = "PO-2"; u.InternalRemarks = "Bel vooraf"; }));
        updated.CustomerReference.ShouldBe("PO-2");
        DerivedValues.ReasonsFor(order.Id).ShouldBe(["Confirmed", "HeaderChanged"]);
        Events.Of<TransportOrderFinancialTermsChangedEto>(order.Id).ShouldBeEmpty();

        var dated = await Orders.UpdateAsync(order.Id, HeaderUpdate(updated, u => { u.OrderDate = new DateOnly(Today.Year, 12, 31); }));
        Events.Of<TransportOrderFinancialTermsChangedEto>(order.Id).ShouldHaveSingleItem("a changed order date reprices");

        (await Should.ThrowAsync<BusinessException>(() => Orders.UpdateAsync(order.Id, HeaderUpdate(dated, u => { u.OrderDate = new DateOnly(Today.Year + 1, 1, 1); }))))
            .Code.ShouldBe(OrdersErrorCodes.OrderDatePeriodLocked);

        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Orders.UpdateAsync(order.Id, HeaderUpdate(order, u => { u.ConcurrencyStamp = Guid.NewGuid().ToString("N"); })));
    }

    [Fact]
    public async Task Lines_are_replaced_added_changed_and_removed_with_the_matching_events_scenarios_10_13_18()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var order = await Orders.CreateAsync(CreateDto(f, confirm: true));
        var lineId = order.Lines.Single().Id!.Value;

        /* Add a line without stops: it starts with the pattern, which the clerk first fills with relations. */
        await Orders.UpdateDefaultStopsAsync(order.Id, new TransportOrderDefaultStopsUpdateDto
        {
            DefaultStops =
            [
                new() { WaypointTypeId = f.LoadTypeId, RelationId = f.DepotId, RelationAddressId = f.DepotAddressId, DayOffset = 0, PlannedDurationMin = 30 },
                new() { WaypointTypeId = f.UnloadTypeId, RelationId = f.ShopId, RelationAddressId = f.ShopAddressId, DayOffset = 2 }
            ],
            ConcurrencyStamp = order.ConcurrencyStamp
        });
        var added = await Orders.AddLineAsync(order.Id, Line(f, quantity: 5, withStops: false));
        added.Lines.Count.ShouldBe(2);
        var second = added.Lines.Single(l => l.Position == 2);
        second.Stops.Select(s => s.Date).ShouldBe([Today, Today.AddDays(2)]);
        second.Stops[0].RelationName.ShouldBe("DC Venlo");
        Events.Of<TransportOrderStopsChangedEto>(order.Id).Last().LineIds.ShouldBe([second.Id!.Value]);

        /* Change a quantity only: lines changed, stops not. */
        var stopsBefore = Events.Of<TransportOrderStopsChangedEto>(order.Id).Count;
        var lineDto = LinesOf(added).Single(l => l.Id == lineId);
        lineDto.Quantity = 9;
        var changed = await Orders.UpdateLineAsync(order.Id, lineId, lineDto);
        changed.Lines.Single(l => l.Id == lineId).Quantity.ShouldBe(9m);
        Events.Of<TransportOrderStopsChangedEto>(order.Id).Count.ShouldBe(stopsBefore);
        Events.Of<TransportOrderLinesChangedEto>(order.Id).Last().LineIds.ShouldBe([lineId]);

        /* Replace the whole list: the second line goes, the first keeps its id. */
        var replaced = await Orders.UpdateLinesAsync(order.Id, new TransportOrderLinesUpdateDto { Lines = [LinesOf(changed).Single(l => l.Id == lineId)], ConcurrencyStamp = changed.ConcurrencyStamp });
        replaced.Lines.ShouldHaveSingleItem().Id.ShouldBe(lineId);
        Events.Of<TransportOrderLinesChangedEto>(order.Id).Last().LineIds.ShouldContain(second.Id!.Value, "a removed line is announced too");

        var deleted = await Orders.DeleteLineAsync(order.Id, lineId);
        deleted.Lines.ShouldBeEmpty();
        await Should.ThrowAsync<EntityNotFoundException>(() => Orders.DeleteLineAsync(order.Id, lineId));
        DerivedValues.ReasonsFor(order.Id).Count(r => r == "LinesChanged").ShouldBe(4);
    }

    [Fact]
    public async Task Stops_of_a_line_on_a_trip_in_progress_are_locked_but_its_quantity_is_not_scenarios_21_26()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var order = await Orders.CreateAsync(CreateDto(f, confirm: true));
        var lineId = order.Lines.Single().Id!.Value;
        Legs.Add(order.Id, lineId, Guid.NewGuid(), "R-1", TransportTripStatus.InProgress);

        var lineDto = LinesOf(order).Single();
        lineDto.Stops![1].Date = Today.AddDays(5);
        var ex = await Should.ThrowAsync<BusinessException>(() => Orders.UpdateLineAsync(order.Id, lineId, lineDto));
        ex.Code.ShouldBe(OrdersErrorCodes.StopLockedByTrip);
        ex.Data["tripNumber"].ShouldBe("R-1");
        (await Orders.GetAsync(order.Id)).Lines.Single().Stops[1].Date.ShouldBe(Today.AddDays(1), "nothing changed");

        var quantityOnly = LinesOf(order).Single();
        quantityOnly.Quantity = 3;
        (await Orders.UpdateLineAsync(order.Id, lineId, quantityOnly)).Lines.Single().Quantity.ShouldBe(3m);

        (await Should.ThrowAsync<BusinessException>(() => Orders.DeleteLineAsync(order.Id, lineId))).Code.ShouldBe(OrdersErrorCodes.StopLockedByTrip);
    }

    [Fact]
    public async Task Terms_references_trip_request_and_attributes_save_with_their_checks_and_events()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var order = await Orders.CreateAsync(CreateDto(f));
        var ds = Financial.AddDiscountOrSurcharge("Brandstoftoeslag");

        var terms = await Orders.UpdateFinancialTermsAsync(order.Id, new TransportOrderFinancialTermsUpdateDto
        {
            CurrencyCode = "eur", VatPercentageId = f.VatId, PriceListIds = [f.PriceListId], FixedPriceAmount = 450.00m,
            DiscountsOrSurcharges = [new() { DiscountOrSurchargeId = ds, Source = DiscountOrSurchargeSource.Default }], SendFreightDocumentsWithInvoice = true, ConcurrencyStamp = order.ConcurrencyStamp
        });
        terms.FinancialTerms.CurrencyCode.ShouldBe("EUR");
        terms.FinancialTerms.FixedPriceAmount.ShouldBe(450.00m);
        terms.FinancialTerms.DiscountsOrSurcharges.ShouldHaveSingleItem().Name.ShouldBe("Brandstoftoeslag");
        Events.Of<TransportOrderFinancialTermsChangedEto>(order.Id).ShouldHaveSingleItem();
        (await Should.ThrowAsync<BusinessException>(() => Orders.UpdateFinancialTermsAsync(order.Id, new TransportOrderFinancialTermsUpdateDto { PriceListIds = [Guid.NewGuid()], ConcurrencyStamp = terms.ConcurrencyStamp })))
            .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);

        var references = await Orders.UpdateReferencesAsync(order.Id, new TransportOrderReferencesUpdateDto
        {
            References = [new() { Type = "PO", Value = "1" }, new() { Type = "Zegel", Value = "ZG-9", Description = "Container seal" }], ConcurrencyStamp = terms.ConcurrencyStamp
        });
        references.References.Select(r => r.Value).ShouldBe(["1", "ZG-9"]);

        var trip = await Orders.UpdateTripRequestAsync(order.Id, new TransportOrderTripRequestUpdateDto
        {
            TripRequest = new TransportOrderTripRequestInputDto
            {
                TransportTripTypeId = Guid.NewGuid(), DepartureRelationId = f.DepotId, DepartureAddressId = f.DepotAddressId, ArrivalRelationId = f.ShopId, ArrivalAddressId = f.ShopAddressId,
                PlannedDeparture = new DateTimeOffset(Today.ToDateTime(new TimeOnly(6, 0)), TimeSpan.FromHours(2)), CarrierRelationId = f.CarrierId, AgreedPurchaseAmount = 300m
            },
            ConcurrencyStamp = references.ConcurrencyStamp
        });
        trip.TripRequest.ShouldNotBeNull();
        trip.TripRequest.DepartureRelationName.ShouldBe("DC Venlo");
        trip.TripRequest.CarrierRelationName.ShouldBe("Charter Transport");
        trip.TripRequest.TripId.ShouldBeNull();
        (await Should.ThrowAsync<BusinessException>(() => Orders.UpdateTripRequestAsync(order.Id, new TransportOrderTripRequestUpdateDto
        {
            TripRequest = new TransportOrderTripRequestInputDto { TransportTripTypeId = Guid.NewGuid(), DepartureRelationId = f.DepotId, DepartureAddressId = f.DepotAddressId, ArrivalRelationId = f.ShopId, ArrivalAddressId = f.ShopAddressId, PlannedDeparture = DateTimeOffset.UtcNow, VehicleIds = [Guid.NewGuid()] },
            ConcurrencyStamp = trip.ConcurrencyStamp
        }))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        var withoutTrip = await Orders.UpdateTripRequestAsync(order.Id, new TransportOrderTripRequestUpdateDto { TripRequest = null, ConcurrencyStamp = trip.ConcurrencyStamp });
        withoutTrip.TripRequest.ShouldBeNull();

        var undefined = await Should.ThrowAsync<BusinessException>(() => Orders.UpdateAttributesAsync(order.Id, new TransportOrderAttributesUpdateDto { Attributes = new() { ["Project"] = "X" }, ConcurrencyStamp = withoutTrip.ConcurrencyStamp }));
        undefined.Code.ShouldBe(OrdersErrorCodes.InvalidAttributes);
        undefined.Data["key"].ShouldBe("Project");
        await GetRequiredService<IOrderAttributeDefinitionAppService>().CreateAsync(new OrderAttributeDefinitionCreateDto { Target = OrderAttributeTarget.Order, Key = "Project", Names = Names("Project") });
        var attributes = await Orders.UpdateAttributesAsync(order.Id, new TransportOrderAttributesUpdateDto { Attributes = new() { ["Project"] = " Najaar " }, ConcurrencyStamp = withoutTrip.ConcurrencyStamp });
        attributes.Attributes.ShouldBe(new Dictionary<string, string> { ["Project"] = "Najaar" });
    }

    // ── the lifecycle ────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_is_refused_with_a_leg_on_a_trip_in_progress_or_completed_and_a_cancelled_order_is_frozen_scenarios_44_45()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var busy = await Orders.CreateAsync(CreateDto(f, confirm: true));
        Legs.Add(busy.Id, busy.Lines.Single().Id!.Value, Guid.NewGuid(), "R-9", TransportTripStatus.Completed);
        var ex = await Should.ThrowAsync<BusinessException>(() => Orders.CancelAsync(busy.Id));
        ex.Code.ShouldBe(OrdersErrorCodes.CancelRefusedTripInProgress);
        ex.Data["tripNumber"].ShouldBe("R-9");

        var planned = await Orders.CreateAsync(CreateDto(f, confirm: true));
        Legs.Add(planned.Id, planned.Lines.Single().Id!.Value, Guid.NewGuid(), "R-10", TransportTripStatus.Planned);
        var cancelled = await Orders.CancelAsync(planned.Id);
        cancelled.Status.ShouldBe(TransportOrderStatus.Cancelled);
        Events.Of<TransportOrderCancelledEto>(planned.Id).ShouldHaveSingleItem();
        (await Should.ThrowAsync<BusinessException>(() => Orders.UpdateAsync(planned.Id, HeaderUpdate(cancelled)))).Code.ShouldBe(OrdersErrorCodes.OrderNotEditable);
        (await Should.ThrowAsync<BusinessException>(() => Orders.CancelAsync(planned.Id))).Code.ShouldBe(OrdersErrorCodes.InvalidTransition);
    }

    [Fact]
    public async Task Approval_validates_approves_with_a_fresh_hash_and_refuses_a_stale_or_failing_one_scenarios_33_to_37_42()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var order = await Orders.CreateAsync(CreateDto(f, confirm: true));
        RateLines.SetRevenue(order.Id, 1250m);

        var validation = await Orders.ValidateApprovalAsync(new ApprovalValidateRequestDto { Ids = [order.Id] });
        var evaluation = validation.Orders.ShouldHaveSingleItem();
        evaluation.CanApprove.ShouldBeTrue();
        evaluation.ProposedStatus.ShouldBe(TransportOrderStatus.ReadyForInvoicing);
        evaluation.Results.ShouldBeEmpty();
        validation.Hash.Length.ShouldBe(64);

        (await Should.ThrowAsync<BusinessException>(() => Orders.ApproveAsync(order.Id, new TransportOrderApproveDto { Hash = new string('0', 64) }))).Code.ShouldBe(OrdersErrorCodes.ApprovalResultsStale);

        /* The revenue drops below the threshold meanwhile: the old hash is stale, the new evaluation proposes the archive. */
        RateLines.SetRevenue(order.Id, 0.20m);
        (await Should.ThrowAsync<BusinessException>(() => Orders.ApproveAsync(order.Id, new TransportOrderApproveDto { Hash = validation.Hash }))).Code.ShouldBe(OrdersErrorCodes.ApprovalResultsStale);
        var again = await Orders.ValidateApprovalAsync(new ApprovalValidateRequestDto { Ids = [order.Id] });
        again.Orders.Single().ProposedStatus.ShouldBe(TransportOrderStatus.Archived);
        again.Orders.Single().Results.ShouldHaveSingleItem().Message.ShouldContain("0.20");
        var approved = await Orders.ApproveAsync(order.Id, new TransportOrderApproveDto { Hash = again.Hash });
        approved.Status.ShouldBe(TransportOrderStatus.Archived);
        Events.Of<TransportOrderStatusChangedEto>(order.Id).ShouldHaveSingleItem().Status.ShouldBe(TransportOrderStatus.Archived);

        /* Revise back to Open (reason kept), approve to ReadyForInvoicing, then the invoicing path to Archived. */
        var revised = await Orders.ReviseAsync(order.Id, new TransportOrderReviseDto { Reason = "Klant betwist het gewicht" });
        revised.Status.ShouldBe(TransportOrderStatus.Open);
        revised.Remarks.ShouldHaveSingleItem().Text.ShouldBe("Klant betwist het gewicht");
        RateLines.SetRevenue(order.Id, 1250m);
        var ready = await Orders.ApproveManyAsync(new ApprovalApproveRequestDto { Ids = [order.Id], Hash = (await Orders.ValidateApprovalAsync(new ApprovalValidateRequestDto { Ids = [order.Id] })).Hash });
        ready.ApprovedIds.ShouldBe([order.Id]);
        (await Orders.GetAsync(order.Id)).Status.ShouldBe(TransportOrderStatus.ReadyForInvoicing);
        (await Orders.ArchiveAsync(order.Id)).Status.ShouldBe(TransportOrderStatus.Archived);

        /* A Draft fails the system rule OrderMustBeOpen: validation says so, approve refuses. */
        var draft = await Orders.CreateAsync(CreateDto(f));
        var draftValidation = await Orders.ValidateApprovalAsync(new ApprovalValidateRequestDto { Ids = [draft.Id] });
        draftValidation.Orders.Single().CanApprove.ShouldBeFalse();
        draftValidation.Orders.Single().Results.Single(r => r.Severity == ApprovalSeverity.Error).Rule.ShouldBe(ApprovalRule.OrderMustBeOpen);
        (await Should.ThrowAsync<BusinessException>(() => Orders.ApproveAsync(draft.Id, new TransportOrderApproveDto { Hash = draftValidation.Hash }))).Code.ShouldBe(OrdersErrorCodes.ApprovalHasErrors);
        var many = await Orders.ApproveManyAsync(new ApprovalApproveRequestDto { Ids = [draft.Id], Hash = draftValidation.Hash });
        many.ApprovedIds.ShouldBeEmpty();
        many.Skipped.ShouldHaveSingleItem().OrderId.ShouldBe(draft.Id);
    }

    [Fact]
    public async Task Delete_is_refused_with_an_assigned_leg_and_otherwise_removes_the_order_and_tells_Planning_scenarios_45_47()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var assigned = await Orders.CreateAsync(CreateDto(f, confirm: true));
        Legs.Add(assigned.Id, assigned.Lines.Single().Id!.Value, Guid.NewGuid(), "R-3", TransportTripStatus.Planned);
        (await Should.ThrowAsync<BusinessException>(() => Orders.DeleteAsync(assigned.Id))).Code.ShouldBe(OrdersErrorCodes.DeleteRefusedLegsAssigned);

        var free = await Orders.CreateAsync(CreateDto(f, confirm: true));
        await Orders.DeleteAsync(free.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => Orders.GetAsync(free.Id));
        Events.Of<TransportOrderCancelledEto>(free.Id).ShouldHaveSingleItem();

        var ready = await Orders.CreateAsync(CreateDto(f, confirm: true));
        RateLines.SetRevenue(ready.Id, 100m);
        await Orders.ApproveAsync(ready.Id, new TransportOrderApproveDto { Hash = (await Orders.ValidateApprovalAsync(new ApprovalValidateRequestDto { Ids = [ready.Id] })).Hash });
        (await Should.ThrowAsync<BusinessException>(() => Orders.DeleteAsync(ready.Id))).Code.ShouldBe(OrdersErrorCodes.InvalidTransition, "scenario 46");

        var draft = await Orders.CreateAsync(CreateDto(f));
        var result = await Orders.BulkDeleteAsync(new TransportOrderBulkDeleteRequestDto { Selection = new() { Mode = "Explicit", ExplicitIds = [draft.Id, assigned.Id, ready.Id] } });
        result.DeletedCount.ShouldBe(1);
        result.SkippedRows.Count.ShouldBe(2);
    }

    // ── reads ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_list_filters_on_status_views_and_customer_searches_sorts_and_pages_scenarios_65_66()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var draft = await Orders.CreateAsync(CreateDto(f, customerReference: "DRAFT-1"));
        var today = await Orders.CreateAsync(CreateDto(f, confirm: true, customerReference: "TODAY-1"));
        var tomorrow = await Orders.CreateAsync(CreateDto(f, confirm: true, customerReference: "TOMORROW-1", orderDate: Today.AddDays(1)));
        var cancelled = await Orders.CreateAsync(CreateDto(f, confirm: true, customerReference: "GONE-1"));
        await Orders.CancelAsync(cancelled.Id);

        var all = await Orders.GetListAsync(new TransportOrderListRequestDto { StartRow = 0, EndRow = 100 });
        all.TotalCount.ShouldBe(4);
        all.Rows.First().Id.ShouldBe(tomorrow.Id, "newest order date first by default");
        all.Rows.Single(r => r.Id == today.Id).CustomerName.ShouldBe("Klant Foods B.V.");
        all.Rows.Single(r => r.Id == today.Id).TransportOrderTypeName.ShouldBe("Standard");

        (await Orders.GetListAsync(new TransportOrderListRequestDto { View = TransportOrderView.Drafts })).Rows.Select(r => r.Id).ShouldBe([draft.Id]);
        (await Orders.GetListAsync(new TransportOrderListRequestDto { View = TransportOrderView.Open })).Rows.Select(r => r.Id).ShouldBe([today.Id, tomorrow.Id], ignoreOrder: true);
        (await Orders.GetListAsync(new TransportOrderListRequestDto { View = TransportOrderView.Today })).Rows.Select(r => r.Id).ShouldBe([draft.Id, today.Id], ignoreOrder: true);
        (await Orders.GetListAsync(new TransportOrderListRequestDto { View = TransportOrderView.Tomorrow })).Rows.Select(r => r.Id).ShouldBe([tomorrow.Id]);
        (await Orders.GetListAsync(new TransportOrderListRequestDto { Status = [TransportOrderStatus.Cancelled] })).Rows.Select(r => r.Id).ShouldBe([cancelled.Id]);
        (await Orders.GetListAsync(new TransportOrderListRequestDto { DateFrom = Today.AddDays(1) })).Rows.Select(r => r.Id).ShouldBe([tomorrow.Id]);
        (await Orders.GetListAsync(new TransportOrderListRequestDto { CustomerRelationId = Guid.NewGuid() })).TotalCount.ShouldBe(0);

        var searched = await Orders.GetListAsync(new TransportOrderListRequestDto { WildcardSearch = "tomorrow" });
        searched.FilteredCount.ShouldBe(1);
        searched.TotalCount.ShouldBe(4);
        (await Orders.GetListAsync(new TransportOrderListRequestDto { WildcardSearch = today.Number })).Rows.ShouldHaveSingleItem().Id.ShouldBe(today.Id);
        (await Orders.GetListAsync(new TransportOrderListRequestDto { WildcardSearch = "klant foods" })).FilteredCount.ShouldBe(4);

        var byNumber = await Orders.GetListAsync(new TransportOrderListRequestDto { SortBy = "number", SortDir = "desc", View = TransportOrderView.Open });
        byNumber.Rows.Select(r => r.Number).ShouldBe([tomorrow.Number, today.Number]);
        var page = await Orders.GetListAsync(new TransportOrderListRequestDto { StartRow = 1, EndRow = 3, SortBy = "customerReference" });
        page.Rows.Count.ShouldBe(2);
        page.Rows.Select(r => r.CustomerReference).ShouldBe(["GONE-1", "TODAY-1"]);
        (await Should.ThrowAsync<BusinessException>(() => Orders.GetListAsync(new TransportOrderListRequestDto { SortBy = "nope" }))).Code.ShouldBe(ReferenceDataErrorCodes.UnknownSortColumn);
    }

    [Fact]
    public async Task Summary_and_price_preview_read_the_stored_values_and_Financials_preview()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var order = await Orders.CreateAsync(CreateDto(f, confirm: true));

        var summary = await Orders.GetSummaryAsync(order.Id);
        summary.Id.ShouldBe(order.Id);
        summary.RevenueAmount.ShouldBeNull("never calculated on SQLite");
        summary.ExecutionProgress.ShouldBe(ExecutionProgress.Unplanned);

        var stored = await Orders.PreviewPricesAsync(order.Id, new PricePreviewRequestDto());
        stored.TotalAmount.ShouldBe(20m, "2 units × 10 (the fake preview)");
        var unsaved = await Orders.PreviewPricesAsync(order.Id, new PricePreviewRequestDto { Lines = [Line(f, quantity: 7)], FinancialTerms = new TransportOrderFinancialTermsInputDto { CurrencyCode = "EUR", FixedPriceAmount = 500m } });
        unsaved.TotalAmount.ShouldBe(500m, "the posted fixed price overrides");
        var snapshot = GetRequiredService<OpenTms.Fakes.FakePricePreviewService>().Snapshots.Last();
        snapshot.Lines.Single().Quantity.ShouldBe(7m);
        snapshot.Lines.Single().Stops.Count.ShouldBe(2);
        snapshot.Lines.Single().Stops[0].CountryCode.ShouldBe("NL");
        snapshot.Lines.Single().Stops[0].City.ShouldBe("Venlo");
        snapshot.Lines.Single().Stops[0].WindowFrom.ShouldNotBeNull("the time slot resolved in the address's zone");
        snapshot.Lines.Single().Stops[0].TimeZoneId.ShouldBe("Europe/Amsterdam");
    }

    [Fact]
    public async Task Another_tenant_sees_nothing()
    {
        Guid orderId;
        using (InTenantA())
        {
            var f = await FixtureAsync();
            orderId = (await Orders.CreateAsync(CreateDto(f, confirm: true))).Id;
        }

        using (InTenantB())
        {
            (await Orders.GetListAsync(new TransportOrderListRequestDto())).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<EntityNotFoundException>(() => Orders.GetAsync(orderId));
            await Should.ThrowAsync<EntityNotFoundException>(() => Orders.ConfirmAsync(orderId));
            await Should.ThrowAsync<EntityNotFoundException>(() => Orders.GetSummaryAsync(orderId));
            (await Orders.ValidateApprovalAsync(new ApprovalValidateRequestDto { Ids = [orderId] })).Orders.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task The_published_readers_return_the_snapshot_and_the_reference_of_the_current_tenant_only()
    {
        var pricing = GetRequiredService<OpenTms.Orders.Published.IOrderPricingReader>();
        var lookup = GetRequiredService<OpenTms.Orders.Published.ITransportOrderLookup>();
        Guid orderId;
        using (InTenantA())
        {
            var f = await FixtureAsync();
            var dto = CreateDto(f, confirm: true);
            dto.TripRequest = new TransportOrderTripRequestInputDto
            {
                TransportTripTypeId = Guid.NewGuid(), DepartureRelationId = f.DepotId, DepartureAddressId = f.DepotAddressId, ArrivalRelationId = f.ShopId, ArrivalAddressId = f.ShopAddressId,
                PlannedDeparture = DateTimeOffset.UtcNow, VehicleIds = [], DriverIds = []
            };
            orderId = (await Orders.CreateAsync(dto)).Id;

            var snapshot = (await pricing.GetSnapshotAsync(orderId)).ShouldNotBeNull();
            snapshot.CustomerRelationId.ShouldBe(f.CustomerId);
            snapshot.PriceListIds.ShouldBe([f.PriceListId]);
            snapshot.Status.ShouldBe((int)TransportOrderStatus.Open);
            snapshot.Lines.Single().Stops.Select(s => s.RelationId).ShouldBe([f.DepotId, f.ShopId]);
            (await pricing.GetSnapshotsAsync([orderId, Guid.NewGuid()])).ShouldHaveSingleItem();

            var reference = (await lookup.GetAsync([orderId])).ShouldHaveSingleItem();
            reference.CustomerName.ShouldBe("Klant Foods B.V.");
            reference.Status.ShouldBe(TransportOrderStatus.Open);
            (await lookup.GetTripRequestAsync(orderId)).ShouldNotBeNull().DepartureRelationId.ShouldBe(f.DepotId);
        }

        using (InTenantB())
        {
            (await pricing.GetSnapshotAsync(orderId)).ShouldBeNull();
            (await lookup.GetAsync([orderId])).ShouldBeEmpty();
            (await lookup.GetTripRequestAsync(orderId)).ShouldBeNull();
        }
    }
}
