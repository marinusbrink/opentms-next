using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Orders;
using OpenTms.Orders.Events;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.ReferenceData;
using OpenTms.TransportRun;
using Volo.Abp.SettingManagement;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Validation;
using Xunit;
using RateLineKind = OpenTms.Financial.Tariffs.RateLineKind;

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
        using (UiCulture("nl"))
        {
            (await Orders.GetAsync(order.Id)).FinancialTerms.VatPercentageName.ShouldBe("Hoog");
        }
        order.FinancialTerms.PriceListNames.ShouldBe(["Tarieven 2026"]);
        order.References.ShouldHaveSingleItem().Value.ShouldBe("123");
        order.Summary.ExecutionProgress.ShouldBe(ExecutionProgress.Unplanned);

        Events.Of<TransportOrderLinesChangedEto>(order.Id).ShouldHaveSingleItem().LineIds.ShouldBe([line.Id!.Value]);
        Events.Of<TransportOrderConfirmedEto>(order.Id).ShouldBeEmpty();
        SummaryReasons(order.Id).ShouldBe(["Created"]);
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
        SummaryReasons(first.Id).ShouldBe(["Confirmed", "LegsGenerated"], "the confirm itself, then Planning's legs (delivered in process on SQLite)");
        (await Run.LegsOfAsync(first.Id)).ShouldHaveSingleItem("Planning generated the leg of the one line");
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

    [Fact]
    public async Task Without_posted_terms_the_customers_financial_defaults_are_copied_on_create_and_on_a_customer_change_and_posted_terms_are_kept()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var fuel = await Run.DiscountOrSurchargeAsync("Brandstoftoeslag");
        await GetRequiredService<OpenTms.Financial.Invoicing.IRelationFinancialSettingsAppService>().UpsertByRelationAsync(f.CustomerId, new OpenTms.Financial.Invoicing.RelationFinancialSettingsUpsertDto
        {
            RevenuePriceListIds = [f.PriceListId], VatPercentageId = f.VatId, PaymentTermDays = 45, CurrencyCode = "EUR",
            DiscountsOrSurcharges = [new OpenTms.Financial.Invoicing.RelationDiscountOrSurchargeInputDto { DiscountOrSurchargeId = fuel, ValidFrom = new DateOnly(2020, 1, 1) }]
        });

        /* Create without terms: the customer's defaults (design 58 through Financial's published lookup). */
        var dto = Run.CreateDto(f, withTerms: false);
        var order = await Orders.CreateAsync(dto);
        order.FinancialTerms.PriceListIds.ShouldBe([f.PriceListId]);
        order.FinancialTerms.VatPercentageId.ShouldBe(f.VatId);
        order.FinancialTerms.PaymentTermDays.ShouldBe(45);
        order.FinancialTerms.CurrencyCode.ShouldBe("EUR");
        var surcharge = order.FinancialTerms.DiscountsOrSurcharges.ShouldHaveSingleItem();
        (surcharge.DiscountOrSurchargeId, surcharge.Source).ShouldBe((fuel, DiscountOrSurchargeSource.Default));

        /* Posted terms are kept as posted. */
        var posted = await Orders.CreateAsync(CreateDto(f));
        posted.FinancialTerms.PaymentTermDays.ShouldBe(30);
        posted.FinancialTerms.DiscountsOrSurcharges.ShouldBeEmpty();

        /* A customer without settings: the module's default terms. */
        var bare = Run.CreateDto(f, withTerms: false);
        bare.CustomerRelationId = f.ShopId;
        var plain = await Orders.CreateAsync(bare);
        plain.FinancialTerms.PriceListIds.ShouldBeEmpty();
        plain.FinancialTerms.PaymentTermDays.ShouldBeNull();

        /* The customer changes on the header: the new customer's defaults replace the terms, and the order reprices. */
        var moved = await Orders.UpdateAsync(plain.Id, HeaderUpdate(plain, u => u.CustomerRelationId = f.CustomerId));
        moved.FinancialTerms.PriceListIds.ShouldBe([f.PriceListId]);
        moved.FinancialTerms.PaymentTermDays.ShouldBe(45);
        Events.Of<TransportOrderFinancialTermsChangedEto>(plain.Id).ShouldHaveSingleItem();

        /* ... unless the request carries terms, which are kept. */
        var kept = await Orders.UpdateAsync(moved.Id, HeaderUpdate(moved, u =>
        {
            u.CustomerRelationId = f.ShopId;
            u.FinancialTerms = new TransportOrderFinancialTermsInputDto { CurrencyCode = "EUR", PaymentTermDays = 7, PriceListIds = [f.PriceListId] };
        }));
        kept.FinancialTerms.PaymentTermDays.ShouldBe(7);
        kept.FinancialTerms.PriceListIds.ShouldBe([f.PriceListId]);
    }

    [Fact]
    public async Task Approval_warns_when_the_terms_differ_from_the_customers_defaults_and_passes_when_they_agree()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        /* The rule is off by default (OrdersSettings.DefaultActiveApprovalRules): this tenant switches it on. */
        await GetRequiredService<ISettingManager>().SetForTenantAsync(TenantA, OrdersSettings.ActiveApprovalRules,
            OrdersSettings.DefaultActiveApprovalRules + "," + nameof(ApprovalRule.FinancialDefaultsMismatch));
        /* The fixture's customer settings: the revenue list, the VAT, 30 days, EUR — what CreateDto posts. */
        var agreeing = await Orders.CreateAsync(CreateDto(f, confirm: true));
        await Run.StoreRevenueAsync(agreeing, 100m);
        (await Orders.ValidateApprovalAsync(new ApprovalValidateRequestDto { Ids = [agreeing.Id] })).Orders.Single().Results.ShouldNotContain(r => r.Rule == ApprovalRule.FinancialDefaultsMismatch);

        var differing = await Orders.CreateAsync(CreateDto(f, confirm: true));
        await Run.StoreRevenueAsync(differing, 100m);
        await Orders.UpdateFinancialTermsAsync(differing.Id, new TransportOrderFinancialTermsUpdateDto { CurrencyCode = "EUR", VatPercentageId = f.VatId, PaymentTermDays = 60, PriceListIds = [f.PriceListId], ConcurrencyStamp = differing.ConcurrencyStamp });
        var result = (await Orders.ValidateApprovalAsync(new ApprovalValidateRequestDto { Ids = [differing.Id] })).Orders.Single().Results.Single(r => r.Rule == ApprovalRule.FinancialDefaultsMismatch);
        result.Severity.ShouldBe(ApprovalSeverity.Warning);
        result.Message.ShouldContain("paymentTermDays");
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
        SummaryReasons(order.Id).ShouldBe(["Confirmed", "LegsGenerated", "HeaderChanged"]);
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
        SummaryReasons(order.Id).Count(r => r == "LinesChanged").ShouldBe(4);
    }

    [Fact]
    public async Task Stops_of_a_line_on_a_trip_in_progress_are_locked_but_its_quantity_is_not_scenarios_21_26()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var order = await Orders.CreateAsync(CreateDto(f, confirm: true));
        var lineId = order.Lines.Single().Id!.Value;
        var trip = await Run.PlanAsync(f.TripTypeId, [order.Id], TransportTripStatus.InProgress);

        var lineDto = LinesOf(order).Single();
        lineDto.Stops![1].Date = Today.AddDays(5);
        var ex = await Should.ThrowAsync<BusinessException>(() => Orders.UpdateLineAsync(order.Id, lineId, lineDto));
        ex.Code.ShouldBe(OrdersErrorCodes.StopLockedByTrip);
        ex.Data["tripNumber"].ShouldBe(trip.Number);
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
        var ds = await Run.DiscountOrSurchargeAsync("Brandstoftoeslag");

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

    // ── the trip request's freight items (design 61 "Order page, revised" point 7) ──

    private static TransportOrderTripRequestInputDto TripRequestOf(OpenTms.TransportRun.TransportRunWorld f, List<Guid>? lineIds = null) => new()
    {
        TransportTripTypeId = f.TripTypeId, DepartureRelationId = f.DepotId, DepartureAddressId = f.DepotAddressId, ArrivalRelationId = f.ShopId, ArrivalAddressId = f.ShopAddressId,
        PlannedDeparture = DateTimeOffset.UtcNow, LineIds = lineIds
    };

    [Fact]
    public async Task The_trip_request_names_its_freight_items_by_the_posted_line_ids_on_create_and_refuses_lines_the_order_does_not_have()
    {
        Guid otherTenantsLine;
        using (InTenantB())
        {
            var fb = await FixtureAsync();
            otherTenantsLine = (await Orders.CreateAsync(CreateDto(fb))).Lines.Single().Id!.Value;
        }

        using var _ = InTenantA();
        var f = await FixtureAsync();
        var firstKey = Guid.NewGuid();
        var secondKey = Guid.NewGuid();
        var dto = CreateDto(f);
        dto.Lines = [Line(f, quantity: 1, id: firstKey), Line(f, quantity: 2, id: secondKey)];
        dto.TripRequest = TripRequestOf(f, [secondKey]);

        var order = await Orders.CreateAsync(dto);

        var second = order.Lines.Single(l => l.Position == 2).Id!.Value;
        second.ShouldNotBe(secondKey, "a new line gets its own id; the posted id is only its key in the create");
        order.TripRequest.ShouldNotBeNull().LineIds.ShouldBe([second]);
        (await Orders.GetAsync(order.Id)).TripRequest!.LineIds.ShouldBe([second], "stored and read back");
        var lookup = GetRequiredService<OpenTms.Orders.Published.ITransportOrderLookup>();
        (await lookup.GetTripRequestAsync(order.Id)).ShouldNotBeNull().LineIds.ShouldBe([second], "Planning reads the selection");

        var all = await Orders.UpdateTripRequestAsync(order.Id, new TransportOrderTripRequestUpdateDto { TripRequest = TripRequestOf(f, []), ConcurrencyStamp = order.ConcurrencyStamp });
        all.TripRequest.ShouldNotBeNull().LineIds.ShouldBeNull("an empty selection means every line");
        (await lookup.GetTripRequestAsync(order.Id)).ShouldNotBeNull().LineIds.ShouldBeNull();

        var unknown = await Should.ThrowAsync<BusinessException>(() => Orders.UpdateTripRequestAsync(order.Id,
            new TransportOrderTripRequestUpdateDto { TripRequest = TripRequestOf(f, [second, Guid.NewGuid()]), ConcurrencyStamp = all.ConcurrencyStamp }));
        unknown.Code.ShouldBe(OrdersErrorCodes.TripRequestLineUnknown);
        OrdersErrorCodes.BadRequestCodes.ShouldContain(OrdersErrorCodes.TripRequestLineUnknown, "travels as HTTP 400");
        (await Should.ThrowAsync<BusinessException>(() => Orders.UpdateTripRequestAsync(order.Id,
            new TransportOrderTripRequestUpdateDto { TripRequest = TripRequestOf(f, [otherTenantsLine]), ConcurrencyStamp = all.ConcurrencyStamp })))
            .Code.ShouldBe(OrdersErrorCodes.TripRequestLineUnknown, "another tenant's line is no line of this order");

        var unknownOnCreate = CreateDto(f);
        unknownOnCreate.TripRequest = TripRequestOf(f, [Guid.NewGuid()]);
        (await Should.ThrowAsync<BusinessException>(() => Orders.CreateAsync(unknownOnCreate))).Code.ShouldBe(OrdersErrorCodes.TripRequestLineUnknown);
        (await Orders.GetListAsync(new TransportOrderListRequestDto())).TotalCount.ShouldBe(1, "the refused create left nothing behind");
    }

    [Fact]
    public async Task A_removed_line_leaves_the_trip_requests_freight_items_and_the_last_one_leaves_an_empty_selection()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var order = await Orders.CreateAsync(CreateDto(f, lines: 3));
        var lines = order.Lines.OrderBy(l => l.Position).Select(l => l.Id!.Value).ToList();
        var selected = await Orders.UpdateTripRequestAsync(order.Id, new TransportOrderTripRequestUpdateDto { TripRequest = TripRequestOf(f, [lines[0], lines[1]]), ConcurrencyStamp = order.ConcurrencyStamp });
        selected.TripRequest!.LineIds.ShouldBe([lines[0], lines[1]]);

        /* The full replace drops the first line... */
        var replaced = await Orders.UpdateLinesAsync(order.Id, new TransportOrderLinesUpdateDto
        {
            Lines = LinesOf(selected).Where(l => l.Id != lines[0]).ToList(), ConcurrencyStamp = selected.ConcurrencyStamp
        });
        replaced.TripRequest!.LineIds.ShouldBe([lines[1]]);

        /* ...the single delete the second: the selection is empty, not "every line" (Planning creates no trip for it). */
        var emptied = await Orders.DeleteLineAsync(order.Id, lines[1]);
        emptied.TripRequest.ShouldNotBeNull().LineIds.ShouldNotBeNull().ShouldBeEmpty();
        (await GetRequiredService<OpenTms.Orders.Published.ITransportOrderLookup>().GetTripRequestAsync(order.Id)).ShouldNotBeNull().LineIds.ShouldNotBeNull().ShouldBeEmpty();

        /* A request for every line stays one when a line goes. */
        var every = await Orders.UpdateTripRequestAsync(order.Id, new TransportOrderTripRequestUpdateDto { TripRequest = TripRequestOf(f), ConcurrencyStamp = emptied.ConcurrencyStamp });
        every.TripRequest!.LineIds.ShouldBeNull();
        (await Orders.DeleteLineAsync(order.Id, lines[2])).TripRequest!.LineIds.ShouldBeNull();
    }

    // ── the lifecycle ────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_is_refused_with_a_leg_on_a_trip_in_progress_or_completed_and_a_cancelled_order_is_frozen_scenarios_44_45()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var busy = await Orders.CreateAsync(CreateDto(f, confirm: true));
        var busyTrip = await Run.PlanAsync(f.TripTypeId, [busy.Id], TransportTripStatus.Completed);
        var ex = await Should.ThrowAsync<BusinessException>(() => Orders.CancelAsync(busy.Id));
        ex.Code.ShouldBe(OrdersErrorCodes.CancelRefusedTripInProgress);
        ex.Data["tripNumber"].ShouldBe(busyTrip.Number);

        var planned = await Orders.CreateAsync(CreateDto(f, confirm: true));
        var plannedTrip = await Run.PlanAsync(f.TripTypeId, [planned.Id]);
        var cancelled = await Orders.CancelAsync(planned.Id);
        cancelled.Status.ShouldBe(TransportOrderStatus.Cancelled);
        Events.Of<TransportOrderCancelledEto>(planned.Id).ShouldHaveSingleItem();
        (await Run.LegsOfAsync(planned.Id)).ShouldBeEmpty("Planning removed the legs of the cancelled order");
        (await GetRequiredService<ITransportTripAppService>().GetAsync(plannedTrip.Id)).Stops.ShouldBeEmpty();
        (await Should.ThrowAsync<BusinessException>(() => Orders.UpdateAsync(planned.Id, HeaderUpdate(cancelled)))).Code.ShouldBe(OrdersErrorCodes.OrderNotEditable);
        (await Should.ThrowAsync<BusinessException>(() => Orders.CancelAsync(planned.Id))).Code.ShouldBe(OrdersErrorCodes.InvalidTransition);
    }

    [Fact]
    public async Task Approval_validates_approves_with_a_fresh_hash_and_refuses_a_stale_or_failing_one_scenarios_33_to_37_42()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var order = await Orders.CreateAsync(CreateDto(f, confirm: true));
        await Run.StoreRevenueAsync(order, 1250m);

        var validation = await Orders.ValidateApprovalAsync(new ApprovalValidateRequestDto { Ids = [order.Id] });
        var evaluation = validation.Orders.ShouldHaveSingleItem();
        evaluation.CanApprove.ShouldBeTrue();
        evaluation.ProposedStatus.ShouldBe(TransportOrderStatus.ReadyForInvoicing);
        evaluation.Results.ShouldBeEmpty();
        validation.Hash.Length.ShouldBe(64);

        (await Should.ThrowAsync<BusinessException>(() => Orders.ApproveAsync(order.Id, new TransportOrderApproveDto { Hash = new string('0', 64) }))).Code.ShouldBe(OrdersErrorCodes.ApprovalResultsStale);

        /* The revenue drops below the threshold meanwhile: the old hash is stale, the new evaluation proposes the archive. */
        await Run.StoreRevenueAsync(order, 0.20m);
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
        await Run.StoreRevenueAsync(order, 1250m);
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
        await Run.PlanAsync(f.TripTypeId, [assigned.Id]);
        (await Should.ThrowAsync<BusinessException>(() => Orders.DeleteAsync(assigned.Id))).Code.ShouldBe(OrdersErrorCodes.DeleteRefusedLegsAssigned);

        var free = await Orders.CreateAsync(CreateDto(f, confirm: true));
        await Orders.DeleteAsync(free.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => Orders.GetAsync(free.Id));
        Events.Of<TransportOrderCancelledEto>(free.Id).ShouldHaveSingleItem();

        var ready = await Orders.CreateAsync(CreateDto(f, confirm: true));
        await Run.StoreRevenueAsync(ready, 100m);
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

        /* Financial's real preview over the stored order: the customer's list prices the lane depot → shop. */
        var stored = await Orders.PreviewPricesAsync(order.Id, new PricePreviewRequestDto());
        stored.TotalAmount.ShouldBe(2m * TransportRunFixture.RevenueUnitPrice, "2 units × the list price");
        stored.Lines.ShouldHaveSingleItem().PriceListId.ShouldBe(f.PriceListId);
        var unsaved = await Orders.PreviewPricesAsync(order.Id, new PricePreviewRequestDto { Lines = [Line(f, quantity: 7)], FinancialTerms = new TransportOrderFinancialTermsInputDto { CurrencyCode = "EUR", PriceListIds = [f.PriceListId], FixedPriceAmount = 500m } });
        unsaved.TotalAmount.ShouldBe(500m, "the posted fixed price overrides");
        unsaved.Lines.First(l => l.Kind == OpenTms.Financial.Tariffs.RateLineKind.PriceList).Quantity.ShouldBe(7m, "the unsaved line was priced, not the stored one");

        /* The snapshot Orders hands Financial carries the stops with their places, windows and zones. */
        var snapshot = (await GetRequiredService<OpenTms.Orders.Published.IOrderPricingReader>().GetSnapshotAsync(order.Id)).ShouldNotBeNull();
        snapshot.Lines.Single().Stops.Count.ShouldBe(2);
        snapshot.Lines.Single().Stops[0].CountryCode.ShouldBe("NL");
        snapshot.Lines.Single().Stops[0].City.ShouldBe("Venlo");
        snapshot.Lines.Single().Stops[0].WindowFrom.ShouldNotBeNull("the time slot resolved in the address's zone");
        snapshot.Lines.Single().Stops[0].TimeZoneId.ShouldBe("Europe/Amsterdam");
    }

    // ── price preview of a new order (Marinus 2026-10-30: review prices before an order is saved) ─────────

    private const decimal Price = TransportRunFixture.RevenueUnitPrice;

    private static List<OpenTms.Financial.Tariffs.Published.PricePreviewLineDto> Priced(OpenTms.Financial.Tariffs.Published.PricePreviewDto preview)
        => preview.Lines.Where(l => l.Kind == RateLineKind.PriceList).OrderBy(l => l.LinePosition).ToList();

    [Fact]
    public async Task New_order_preview_prices_the_posted_lines_with_the_customers_list_by_position_and_stores_nothing()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var rateLines = GetRequiredService<IRepository<OpenTms.Financial.Tariffs.RateLine, Guid>>();
        var ordersBefore = (await Orders.GetListAsync(new TransportOrderListRequestDto())).TotalCount;
        var rateLinesBefore = await rateLines.GetCountAsync();
        var eventsBefore = Events.All.Count;
        var requestsBefore = Requests.All.Count;

        var preview = await Orders.PreviewNewOrderPricesAsync(new NewOrderPricePreviewRequestDto
        {
            CustomerRelationId = f.CustomerId,
            OrderDate = Today,
            FinancialTerms = new TransportOrderFinancialTermsInputDto { CurrencyCode = "EUR", PriceListIds = [f.PriceListId] },
            Lines = [Line(f, quantity: 2), Line(f, quantity: 3)]
        });

        /* Each unsaved item is named by its 1-based position; it has no id. */
        var priced = Priced(preview);
        priced.Count.ShouldBe(2);
        priced.Select(l => l.LinePosition).ShouldBe(new int?[] { 1, 2 });
        priced.Select(l => l.Quantity).ShouldBe([2m, 3m]);
        priced.Select(l => l.Amount).ShouldBe([2m * Price, 3m * Price]);
        priced.ShouldAllBe(l => l.PriceListId == f.PriceListId);
        priced.ShouldAllBe(l => l.OrderLineId == null, "an unsaved item has no id (OrderLineId keeps its old behaviour)");
        preview.LinesAmount.ShouldBe(5m * Price);
        preview.TotalAmount.ShouldBe(5m * Price);
        preview.CurrencyCode.ShouldBe("EUR");

        /* Nothing is stored or published: no order, no rate line, no event, no derived-value request. */
        (await Orders.GetListAsync(new TransportOrderListRequestDto())).TotalCount.ShouldBe(ordersBefore);
        (await rateLines.GetCountAsync()).ShouldBe(rateLinesBefore);
        Events.All.Count.ShouldBe(eventsBefore);
        Requests.All.Count.ShouldBe(requestsBefore);

        /* A missing order date is today: the same answer. */
        var undated = await Orders.PreviewNewOrderPricesAsync(new NewOrderPricePreviewRequestDto
        {
            CustomerRelationId = f.CustomerId,
            FinancialTerms = new TransportOrderFinancialTermsInputDto { CurrencyCode = "EUR", PriceListIds = [f.PriceListId] },
            Lines = [Line(f, quantity: 2), Line(f, quantity: 3)]
        });
        undated.TotalAmount.ShouldBe(5m * Price);

        /* No lines yet: an empty preview, not an error. */
        var empty = await Orders.PreviewNewOrderPricesAsync(new NewOrderPricePreviewRequestDto { CustomerRelationId = f.CustomerId });
        empty.Lines.ShouldBeEmpty();
        empty.TotalAmount.ShouldBe(0m);
    }

    [Fact]
    public async Task New_order_preview_without_terms_uses_the_customers_defaults_and_posted_terms_override_them()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();

        /* No terms posted: the customer's financial defaults (its revenue list), as Create copies them. */
        var defaults = await Orders.PreviewNewOrderPricesAsync(new NewOrderPricePreviewRequestDto { CustomerRelationId = f.CustomerId, Lines = [Line(f, quantity: 2)] });
        var line = defaults.Lines.ShouldHaveSingleItem();
        line.PriceListId.ShouldBe(f.PriceListId);
        line.LinePosition.ShouldBe(1);
        defaults.TotalAmount.ShouldBe(2m * Price);

        /* A customer without financial settings: the module's default terms, so no list and no price. */
        var bare = await Orders.PreviewNewOrderPricesAsync(new NewOrderPricePreviewRequestDto { CustomerRelationId = f.ShopId, Lines = [Line(f, quantity: 2)] });
        bare.Lines.ShouldNotContain(l => l.Kind == RateLineKind.PriceList);
        bare.TotalAmount.ShouldBe(0m);

        /* Posted terms without a list override the customer's list. */
        var noList = await Orders.PreviewNewOrderPricesAsync(new NewOrderPricePreviewRequestDto
        {
            CustomerRelationId = f.CustomerId, FinancialTerms = new TransportOrderFinancialTermsInputDto { CurrencyCode = "EUR" }, Lines = [Line(f, quantity: 2)]
        });
        noList.Lines.ShouldNotContain(l => l.Kind == RateLineKind.PriceList);
        noList.TotalAmount.ShouldBe(0m);

        /* A posted fixed price overrides: an order-level line, named by no position and no item. */
        var fixedPrice = await Orders.PreviewNewOrderPricesAsync(new NewOrderPricePreviewRequestDto
        {
            CustomerRelationId = f.CustomerId,
            FinancialTerms = new TransportOrderFinancialTermsInputDto { CurrencyCode = "EUR", PriceListIds = [f.PriceListId], FixedPriceAmount = 500m },
            Lines = [Line(f, quantity: 2), Line(f, quantity: 3)]
        });
        fixedPrice.TotalAmount.ShouldBe(500m, "the posted fixed price overrides the list");
        var orderLevel = fixedPrice.Lines.Single(l => l.Kind == RateLineKind.FixedPrice);
        orderLevel.Amount.ShouldBe(500m);
        orderLevel.LinePosition.ShouldBeNull("an order-level line belongs to no freight item");
        orderLevel.OrderLineId.ShouldBeNull();
        Priced(fixedPrice).Select(l => l.LinePosition).ShouldBe(new int?[] { 1, 2 }, "the items' list lines still name their positions");
    }

    [Fact]
    public async Task The_stored_order_preview_names_each_item_by_its_position_and_a_saved_item_also_by_its_id()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var order = await Orders.CreateAsync(CreateDto(f, lines: 2));
        var ids = order.Lines.OrderBy(l => l.Position).Select(l => l.Id).ToList();

        /* The stored state: both saved items, by position and by id. */
        var stored = Priced(await Orders.PreviewPricesAsync(order.Id, new PricePreviewRequestDto()));
        stored.Select(l => l.LinePosition).ShouldBe(new int?[] { 1, 2 });
        stored.Select(l => l.OrderLineId).ShouldBe(ids);

        /* An item added on the screen and not saved yet: its position, no id; the saved ones keep theirs. */
        var posted = LinesOf(order);
        posted.Add(Line(f, quantity: 5));
        var added = Priced(await Orders.PreviewPricesAsync(order.Id, new PricePreviewRequestDto { Lines = posted }));
        added.Select(l => l.LinePosition).ShouldBe(new int?[] { 1, 2, 3 });
        added.Select(l => l.OrderLineId).ShouldBe([ids[0], ids[1], null]);
        added[2].Quantity.ShouldBe(5m);

        /* The first item removed on the screen: the second moves up to position 1 and keeps its id. */
        var removed = Priced(await Orders.PreviewPricesAsync(order.Id, new PricePreviewRequestDto { Lines = [LinesOf(order)[1]] })).ShouldHaveSingleItem();
        removed.LinePosition.ShouldBe(1);
        removed.OrderLineId.ShouldBe(ids[1]);
    }

    [Fact]
    public async Task New_order_preview_needs_a_customer_and_a_signed_in_user()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();

        await Should.ThrowAsync<AbpValidationException>(() => Orders.PreviewNewOrderPricesAsync(new NewOrderPricePreviewRequestDto { Lines = [Line(f)] }));

        /* The permission itself is read by reflection (OpenTms.Application.Tests, NewOrderPricePreviewContractTests): the
         * SQLite tests grant every policy to a signed-in user, so here only the anonymous caller can be refused. */
        using (AsBackgroundProcess())
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => Orders.PreviewNewOrderPricesAsync(new NewOrderPricePreviewRequestDto { CustomerRelationId = f.CustomerId, Lines = [Line(f)] }));
        }
    }

    [Fact]
    public async Task Another_tenant_cannot_price_a_new_order_with_this_tenants_customer_or_price_list()
    {
        TransportRunWorld a;
        using (InTenantA())
        {
            a = await FixtureAsync();
            (await Orders.PreviewNewOrderPricesAsync(new NewOrderPricePreviewRequestDto { CustomerRelationId = a.CustomerId, Lines = [Line(a, quantity: 2)] }))
                .TotalAmount.ShouldBe(2m * Price, "control: A's customer prices with A's list in A");
        }

        using (InTenantB())
        {
            var b = await FixtureAsync();
            /* Control: the preview works in B with B's own world, so an empty answer below is the isolation, not a broken preview. */
            (await Orders.PreviewNewOrderPricesAsync(new NewOrderPricePreviewRequestDto { CustomerRelationId = b.CustomerId, Lines = [Line(b, quantity: 2)] }))
                .TotalAmount.ShouldBe(2m * Price);

            /* A's customer, A's list and A's stops posted: the lane (A's depot → A's shop) would match if A's list resolved. */
            var posted = await Orders.PreviewNewOrderPricesAsync(new NewOrderPricePreviewRequestDto
            {
                CustomerRelationId = a.CustomerId,
                FinancialTerms = new TransportOrderFinancialTermsInputDto { CurrencyCode = "EUR", PriceListIds = [a.PriceListId] },
                Lines = [Line(a, quantity: 2)]
            });
            posted.Lines.ShouldNotContain(l => l.PriceListId == a.PriceListId);
            posted.Lines.ShouldNotContain(l => l.Kind == RateLineKind.PriceList);
            posted.TotalAmount.ShouldBe(0m);

            /* A's customer without terms: A's customer settings (with A's list) do not resolve in B. */
            var defaults = await Orders.PreviewNewOrderPricesAsync(new NewOrderPricePreviewRequestDto { CustomerRelationId = a.CustomerId, Lines = [Line(a, quantity: 2)] });
            defaults.Lines.ShouldBeEmpty();
            defaults.TotalAmount.ShouldBe(0m);

            /* A's list next to B's own list on B's stops: only B's list prices. */
            var mixed = await Orders.PreviewNewOrderPricesAsync(new NewOrderPricePreviewRequestDto
            {
                CustomerRelationId = b.CustomerId,
                FinancialTerms = new TransportOrderFinancialTermsInputDto { CurrencyCode = "EUR", PriceListIds = [a.PriceListId, b.PriceListId] },
                Lines = [Line(b, quantity: 2)]
            });
            mixed.Lines.ShouldNotContain(l => l.PriceListId == a.PriceListId);
            Priced(mixed).ShouldHaveSingleItem().PriceListId.ShouldBe(b.PriceListId);
        }
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
                TransportTripTypeId = f.TripTypeId, DepartureRelationId = f.DepotId, DepartureAddressId = f.DepotAddressId, ArrivalRelationId = f.ShopId, ArrivalAddressId = f.ShopAddressId,
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
