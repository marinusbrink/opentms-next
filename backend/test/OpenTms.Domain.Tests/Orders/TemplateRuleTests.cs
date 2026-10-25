using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Volo.Abp;
using Xunit;
using static OpenTms.Orders.TransportOrders.TransportOrderBuilders;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// Templates and recurrence (design 58; orders.md scenarios 48–53, 61): the weekly and monthly schedule with interval,
/// validity and horizon, the materialiser's date shifting and time-of-day resolution in the tenant's zone (DST
/// included), and the round trip order → template → draft of "copy order" (Assumption 5). Risk class: High.
/// </summary>
public class TemplateRuleTests
{
    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private static TemplateSchedule Weekly(RecurrenceDays days, int interval = 1, DateOnly? validFrom = null, DateOnly? validTill = null, int horizon = 5)
        => TemplateSchedule.Create(TemplateRecurrence.Weekly, interval, days, null, horizon, validFrom ?? new DateOnly(2026, 10, 5), validTill);

    // ── the schedule ─────────────────────────────────────────────────────────

    [Fact]
    public void A_weekly_template_with_Monday_checked_is_due_on_Mondays_in_the_horizon_scenario_51()
    {
        var schedule = Weekly(RecurrenceDays.Monday);
        schedule.Occurs(new DateOnly(2026, 10, 26)).ShouldBeTrue("a Monday");
        schedule.Occurs(new DateOnly(2026, 10, 27)).ShouldBeFalse("a Tuesday");
        /* The horizon of 5 days from Thursday 22 October holds Monday the 26th only. */
        schedule.DueDates(new DateOnly(2026, 10, 22)).ShouldBe([new DateOnly(2026, 10, 26)]);
        /* From Saturday the 24th with a horizon of 10 days: the 26th and 2 November. */
        TemplateSchedule.Create(TemplateRecurrence.Weekly, 1, RecurrenceDays.Monday, null, 10, new DateOnly(2026, 10, 5), null)
            .DueDates(new DateOnly(2026, 10, 24)).ShouldBe([new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 2)]);
    }

    [Fact]
    public void A_two_week_interval_counts_weeks_from_the_week_of_ValidFrom()
    {
        var schedule = Weekly(RecurrenceDays.Monday | RecurrenceDays.Friday, interval: 2, validFrom: new DateOnly(2026, 10, 5));
        schedule.Occurs(new DateOnly(2026, 10, 5)).ShouldBeTrue("week 0, Monday");
        schedule.Occurs(new DateOnly(2026, 10, 9)).ShouldBeTrue("week 0, Friday");
        schedule.Occurs(new DateOnly(2026, 10, 12)).ShouldBeFalse("week 1");
        schedule.Occurs(new DateOnly(2026, 10, 19)).ShouldBeTrue("week 2");
        schedule.Occurs(new DateOnly(2026, 10, 4)).ShouldBeFalse("before ValidFrom");
    }

    [Fact]
    public void A_monthly_template_is_due_on_its_day_of_month_clamped_to_shorter_months_scenario_52_made_to_work()
    {
        var schedule = TemplateSchedule.Create(TemplateRecurrence.Monthly, 1, RecurrenceDays.None, 31, 10, new DateOnly(2026, 1, 1), null);
        schedule.Occurs(new DateOnly(2026, 1, 31)).ShouldBeTrue();
        schedule.Occurs(new DateOnly(2026, 2, 28)).ShouldBeTrue("February has no 31st: the last day");
        schedule.Occurs(new DateOnly(2026, 2, 27)).ShouldBeFalse();
        schedule.Occurs(new DateOnly(2026, 4, 30)).ShouldBeTrue();

        var quarterly = TemplateSchedule.Create(TemplateRecurrence.Monthly, 3, RecurrenceDays.None, 1, 5, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        quarterly.Occurs(new DateOnly(2026, 4, 1)).ShouldBeTrue();
        quarterly.Occurs(new DateOnly(2026, 5, 1)).ShouldBeFalse();
        quarterly.Occurs(new DateOnly(2027, 1, 1)).ShouldBeFalse("after ValidTill");
    }

    [Fact]
    public void Validity_bounds_the_schedule_and_None_never_occurs()
    {
        var schedule = Weekly(RecurrenceDays.Monday, validFrom: new DateOnly(2026, 10, 26), validTill: new DateOnly(2026, 11, 2));
        schedule.Occurs(new DateOnly(2026, 10, 19)).ShouldBeFalse();
        schedule.Occurs(new DateOnly(2026, 10, 26)).ShouldBeTrue();
        schedule.Occurs(new DateOnly(2026, 11, 2)).ShouldBeTrue();
        schedule.Occurs(new DateOnly(2026, 11, 9)).ShouldBeFalse();
        TemplateSchedule.None().Occurs(new DateOnly(2026, 10, 26)).ShouldBeFalse();
        TemplateSchedule.None().DueDates(new DateOnly(2026, 10, 26)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(TemplateRecurrence.Weekly, 0, RecurrenceDays.Monday, null, 5, "interval")]
    [InlineData(TemplateRecurrence.Weekly, 53, RecurrenceDays.Monday, null, 5, "interval")]
    [InlineData(TemplateRecurrence.Weekly, 1, RecurrenceDays.None, null, 5, "daysOfWeek")]
    [InlineData(TemplateRecurrence.Monthly, 1, RecurrenceDays.None, null, 5, "dayOfMonth")]
    [InlineData(TemplateRecurrence.Monthly, 1, RecurrenceDays.None, 32, 5, "dayOfMonth")]
    [InlineData(TemplateRecurrence.Weekly, 1, RecurrenceDays.Monday, null, 0, "horizonDays")]
    [InlineData(TemplateRecurrence.Weekly, 1, RecurrenceDays.Monday, null, 61, "horizonDays")]
    public void An_invalid_schedule_is_refused_naming_the_field(TemplateRecurrence recurrence, int interval, RecurrenceDays days, int? dayOfMonth, int horizon, string field)
    {
        var ex = Should.Throw<BusinessException>(() => TemplateSchedule.Create(recurrence, interval, days, dayOfMonth, horizon, new DateOnly(2026, 1, 1), null));
        ex.Code.ShouldBe(OrdersErrorCodes.InvalidSchedule);
        ex.Data["field"].ShouldBe(field);
    }

    [Fact]
    public void ValidTill_before_ValidFrom_is_refused()
    {
        Should.Throw<BusinessException>(() => TemplateSchedule.Create(TemplateRecurrence.None, 1, RecurrenceDays.None, null, 5, new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1)))
            .Code.ShouldBe(OrdersErrorCodes.InvalidSchedule);
    }

    // ── the materialiser ─────────────────────────────────────────────────────

    private static TransportOrderTemplate Template()
    {
        var template = new TransportOrderTemplate(Guid.NewGuid(), TenantA, "Weekly Venlo", Header(), Terms(), Weekly(RecurrenceDays.Monday), Guid.NewGuid);
        template.SetLines([
            new TemplateLineInput(Freight(quantity: 3), [new MeasurementInput(MeasurementKind.WeightKg, 1, 120)], [Guid.NewGuid()], [
                new TemplateStopInput(LoadType, DepotId, DepotAddressId, null, 0, null, new TimeOnly(6, 0), new TimeOnly(8, 0), "L", null, 30),
                new TemplateStopInput(UnloadType, ShopId, ShopAddressId, null, 2, null, null, null, "U", "Achterom", 20)
            ], new Dictionary<string, string> { ["Seal"] = "ZG-1" })
        ], Guid.NewGuid);
        template.SetReferences([new ReferenceInput("PO", "123", null)], Guid.NewGuid);
        template.SetTripRequest(new TemplateTripRequestInput(Guid.NewGuid(), DepotId, DepotAddressId, ShopId, ShopAddressId, 0, new TimeOnly(5, 30), 1, new TimeOnly(14, 0), null, null, null, [], [], null, null));
        return template;
    }

    [Fact]
    public void Materialising_shifts_the_stops_by_their_offsets_from_the_order_date_scenario_49()
    {
        var draft = TemplateMaterialiser.ToDraft(Template(), new DateOnly(2026, 11, 2), Amsterdam);

        draft.Header.OrderDate.ShouldBe(new DateOnly(2026, 11, 2));
        draft.Header.CustomerRelationId.ShouldBe(CustomerId);
        var line = draft.Lines.ShouldHaveSingleItem();
        line.Freight.Quantity.ShouldBe(3m);
        line.Measurements.ShouldHaveSingleItem().Value.ShouldBe(120);
        line.Stops.Select(s => s.Date).ShouldBe([new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 4)]);
        line.Stops[0].WindowFrom.ShouldBe(new TimeOnly(6, 0));
        line.Stops[1].Remarks.ShouldBe("Achterom");
        line.Attributes!["Seal"].ShouldBe("ZG-1");
        draft.References.ShouldHaveSingleItem().Value.ShouldBe("123");
        draft.TripRequest.ShouldNotBeNull();
        draft.TripRequest.PlannedDeparture.ShouldBe(new DateTimeOffset(2026, 11, 2, 5, 30, 0, TimeSpan.FromHours(1)), "2 November is winter time in Amsterdam");
        draft.TripRequest.PlannedArrival.ShouldBe(new DateTimeOffset(2026, 11, 3, 14, 0, 0, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void A_trip_request_time_in_the_DST_gap_moves_to_the_first_valid_instant_scenario_61()
    {
        var template = Template();
        template.SetTripRequest(new TemplateTripRequestInput(Guid.NewGuid(), DepotId, DepotAddressId, ShopId, ShopAddressId, 0, new TimeOnly(2, 30), null, null, null, null, null, [], [], null, null));

        var draft = TemplateMaterialiser.ToDraft(template, new DateOnly(2026, 3, 29), Amsterdam);

        draft.TripRequest!.PlannedDeparture.ShouldBe(new DateTimeOffset(2026, 3, 29, 3, 0, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void A_template_from_an_order_keeps_the_stops_as_offsets_and_the_trip_times_as_times_of_day_scenario_50()
    {
        var order = Confirmed("TO-1", Line(Freight(quantity: 7), Load(new DateOnly(2026, 10, 20)), Unload(new DateOnly(2026, 10, 23))));
        order.SetReferences([new ReferenceInput("PO", "P-1", null)], Guid.NewGuid);
        order.SetTripRequest(new TripRequestInput(Guid.NewGuid(), DepotId, DepotAddressId, ShopId, ShopAddressId,
            new DateTimeOffset(2026, 10, 21, 4, 0, 0, TimeSpan.Zero), null, null, null, null, [], [], 300m, "EUR"));

        var template = TransportOrderTemplate.FromOrder(Guid.NewGuid(), order, "Copy", Amsterdam, Guid.NewGuid);

        template.SourceOrderId.ShouldBe(order.Id);
        template.Schedule.Recurrence.ShouldBe(TemplateRecurrence.None);
        var line = template.Lines.ShouldHaveSingleItem();
        line.Freight.Quantity.ShouldBe(7m);
        line.Stops.OrderBy(s => s.Position).Select(s => s.DayOffset).ShouldBe([0, 3]);
        template.TripRequest.ShouldNotBeNull();
        template.TripRequest.DepartureDayOffset.ShouldBe(1);
        template.TripRequest.DepartureTime.ShouldBe(new TimeOnly(6, 0), "04:00 UTC is 06:00 in Amsterdam summer time");
        template.TripRequest.AgreedPurchaseAmount.ShouldBe(300m);

        /* And back: a copy dated a week later keeps the span of days. */
        var draft = TemplateMaterialiser.ToDraft(template, new DateOnly(2026, 10, 27), Amsterdam);
        draft.Lines.Single().Stops.Select(s => s.Date).ShouldBe([new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 30)]);
        draft.TripRequest!.PlannedDeparture.ShouldBe(new DateTimeOffset(2026, 10, 28, 6, 0, 0, TimeSpan.FromHours(1)), "after the clocks went back");
        draft.References.ShouldHaveSingleItem().Value.ShouldBe("P-1");
    }

    [Fact]
    public void The_template_name_is_required_unique_by_normalized_name_and_the_last_date_only_moves_forward()
    {
        var template = Template();
        template.NormalizedName.ShouldBe("WEEKLY VENLO");
        Should.Throw<BusinessException>(() => template.SetName(" ")).Code.ShouldBe(Platform.ReferenceData.ReferenceDataErrorCodes.NameRequired);

        template.RecordMaterialised(new DateOnly(2026, 11, 2));
        template.RecordMaterialised(new DateOnly(2026, 10, 26));
        template.LastMaterialisedDate.ShouldBe(new DateOnly(2026, 11, 2));
    }
}
