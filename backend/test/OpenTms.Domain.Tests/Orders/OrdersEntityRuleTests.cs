using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.Localization;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// The entity rules of the Orders master data (design 51): <see cref="TimeSlot.ToWindow"/> over ordinary days, overnight
/// slots and the Amsterdam DST nights of 2026 (29 March: 02:00 → 03:00; 25 October: 03:00 → 02:00), the zero-length
/// slot, the successor that is itself, the bounds and uniqueness of the children and the required-field flags.
/// Risk class: High.
/// </summary>
public class OrdersEntityRuleTests
{
    private const string Amsterdam = "Europe/Amsterdam";

    private static LocalizedName Name(string en) => LocalizedName.Create(new Dictionary<string, string?> { ["en"] = en });

    private static TimeSlot Slot(int startHour, int startMinute, int endHour, int endMinute)
        => new(Guid.NewGuid(), Guid.NewGuid(), "S", Name("Slot"), new TimeOnly(startHour, startMinute), new TimeOnly(endHour, endMinute));

    private static DateTimeOffset At(int year, int month, int day, int hour, int minute, int offsetHours)
        => new(year, month, day, hour, minute, 0, TimeSpan.FromHours(offsetHours));

    // ── TimeSlot.ToWindow ─────────────────────────────────────────────────────

    [Fact]
    public void A_day_slot_is_the_local_times_on_that_date()
    {
        var (from, till) = Slot(8, 0, 17, 0).ToWindow(new DateOnly(2026, 10, 22), Amsterdam);

        from.ShouldBe(At(2026, 10, 22, 8, 0, 2));
        from.Offset.ShouldBe(TimeSpan.FromHours(2));
        till.ShouldBe(At(2026, 10, 22, 17, 0, 2));
    }

    [Fact]
    public void An_overnight_slot_ends_the_next_day()
    {
        var slot = Slot(22, 0, 6, 0);
        slot.IsOvernight.ShouldBeTrue();

        var (from, till) = slot.ToWindow(new DateOnly(2026, 1, 15), Amsterdam);

        from.ShouldBe(At(2026, 1, 15, 22, 0, 1));
        till.ShouldBe(At(2026, 1, 16, 6, 0, 1));
        (till - from).ShouldBe(TimeSpan.FromHours(8));
    }

    [Fact]
    public void An_overnight_slot_over_the_autumn_change_lasts_an_hour_longer()
    {
        var (from, till) = Slot(22, 0, 6, 0).ToWindow(new DateOnly(2026, 10, 24), Amsterdam);

        from.ShouldBe(At(2026, 10, 24, 22, 0, 2));
        till.ShouldBe(At(2026, 10, 25, 6, 0, 1));
        (till - from).ShouldBe(TimeSpan.FromHours(9));
    }

    [Fact]
    public void A_time_in_the_spring_gap_moves_forward_to_the_first_valid_instant()
    {
        var (from, till) = Slot(2, 30, 6, 0).ToWindow(new DateOnly(2026, 3, 29), Amsterdam);

        /* 02:30 does not exist; the clocks jump from 02:00 CET to 03:00 CEST (01:00 UTC). */
        from.ShouldBe(At(2026, 3, 29, 3, 0, 2));
        from.UtcDateTime.ShouldBe(new DateTime(2026, 3, 29, 1, 0, 0, DateTimeKind.Utc));
        from.Offset.ShouldBe(TimeSpan.FromHours(2));
        till.ShouldBe(At(2026, 3, 29, 6, 0, 2));
    }

    [Fact]
    public void An_overnight_end_in_the_spring_gap_moves_forward_too()
    {
        var (from, till) = Slot(22, 0, 2, 15).ToWindow(new DateOnly(2026, 3, 28), Amsterdam);

        from.ShouldBe(At(2026, 3, 28, 22, 0, 1));
        till.ShouldBe(At(2026, 3, 29, 3, 0, 2));
    }

    [Fact]
    public void An_ambiguous_time_in_the_autumn_overlap_takes_the_earlier_offset()
    {
        var (from, till) = Slot(2, 30, 6, 0).ToWindow(new DateOnly(2026, 10, 25), Amsterdam);

        /* 02:30 happens twice; the earlier is 02:30 CEST (+02:00, 00:30 UTC). */
        from.ShouldBe(At(2026, 10, 25, 2, 30, 2));
        from.Offset.ShouldBe(TimeSpan.FromHours(2));
        till.ShouldBe(At(2026, 10, 25, 6, 0, 1));
    }

    [Fact]
    public void A_slot_of_zero_length_is_refused()
    {
        Should.Throw<BusinessException>(() => Slot(8, 0, 8, 0)).Code.ShouldBe(OrdersErrorCodes.EmptyTimeSlot);

        var slot = Slot(8, 0, 12, 0);
        Should.Throw<BusinessException>(() => slot.SetTimes(new TimeOnly(9, 0), new TimeOnly(9, 0))).Code.ShouldBe(OrdersErrorCodes.EmptyTimeSlot);
        slot.StartTime.ShouldBe(new TimeOnly(8, 0), "a refused change leaves the slot as it was");
    }

    [Fact]
    public void An_unknown_time_zone_is_refused()
    {
        Should.Throw<TimeZoneNotFoundException>(() => Slot(8, 0, 12, 0).ToWindow(new DateOnly(2026, 10, 22), "Europe/Atlantis"));
    }

    // ── WaypointType ──────────────────────────────────────────────────────────

    private static WaypointType Waypoint(string code = "LOAD") => new(Guid.NewGuid(), Guid.NewGuid(), code, Name(code), WaypointActivity.Load);

    [Fact]
    public void A_waypoint_type_is_not_its_own_successor_but_a_cycle_is_allowed()
    {
        var load = Waypoint("LOAD");
        var unload = Waypoint("UNLOAD");

        Should.Throw<BusinessException>(() => load.SetSuccessor(load.Id)).Code.ShouldBe(OrdersErrorCodes.SuccessorIsSelf);

        load.SetSuccessor(unload.Id);
        unload.SetSuccessor(load.Id);
        load.SuccessiveWaypointTypeId.ShouldBe(unload.Id);
        unload.SuccessiveWaypointTypeId.ShouldBe(load.Id);
    }

    [Fact]
    public void An_undefined_activity_or_a_duration_outside_a_day_is_refused()
    {
        var type = Waypoint();
        Should.Throw<BusinessException>(() => type.SetActivity((WaypointActivity)99)).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        Should.Throw<BusinessException>(() => type.SetDefaultDuration(-1)).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        Should.Throw<BusinessException>(() => type.SetDefaultDuration(1441)).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        type.SetDefaultDuration(1440);
        type.SetDefaultDuration(null);
        type.DefaultDurationMinutes.ShouldBeNull();
    }

    [Fact]
    public void Fleet_management_codes_are_one_per_vendor_at_most_twenty_and_updated_in_place()
    {
        var type = Waypoint();
        type.SetFleetManagementCodes([("Transics", "L1"), ("Trimble", "100")], Guid.NewGuid);
        var transics = type.FleetManagementCodes.Single(c => c.Vendor == "Transics");

        type.SetFleetManagementCodes([(" transics ", "L2")], Guid.NewGuid);
        type.FleetManagementCodes.ShouldHaveSingleItem().Id.ShouldBe(transics.Id);
        transics.Code.ShouldBe("L2");

        Should.Throw<BusinessException>(() => type.SetFleetManagementCodes([("Transics", "A"), ("TRANSICS", "B")], Guid.NewGuid))
            .Code.ShouldBe(ReferenceDataErrorCodes.DuplicateKey);
        Should.Throw<BusinessException>(() => type.SetFleetManagementCodes(Enumerable.Range(1, 21).Select(i => ($"V{i}", "C")), Guid.NewGuid))
            .Code.ShouldBe(ReferenceDataErrorCodes.TooManyChildren);
    }

    // ── TransportOrderType ────────────────────────────────────────────────────

    private static TransportOrderType OrderType()
        => new(Guid.NewGuid(), Guid.NewGuid(), "STD", Name("Standard"), NumberSequenceFormat.Create("TO", true, false, "-", 5));

    [Fact]
    public void Default_stops_keep_their_order_may_repeat_a_type_and_are_at_most_ten()
    {
        var type = OrderType();
        var load = Guid.NewGuid();
        var unload = Guid.NewGuid();

        type.SetDefaultWaypoints([load, load, unload]);
        type.DefaultWaypoints.OrderBy(w => w.Position).Select(w => (w.Position, w.WaypointTypeId)).ShouldBe([(1, load), (2, load), (3, unload)]);

        type.SetDefaultWaypoints([unload]);
        type.DefaultWaypoints.ShouldHaveSingleItem().WaypointTypeId.ShouldBe(unload);

        Should.Throw<BusinessException>(() => type.SetDefaultWaypoints(Enumerable.Range(0, 11).Select(_ => load).ToList()))
            .Code.ShouldBe(ReferenceDataErrorCodes.TooManyChildren);
    }

    [Fact]
    public void Required_fields_accept_every_defined_flag_and_refuse_another_bit()
    {
        var type = OrderType();
        type.SetRequiredFields(TransportOrderRequiredFields.CustomerReference | TransportOrderRequiredFields.Temperature);
        type.RequiredFields.HasFlag(TransportOrderRequiredFields.Temperature).ShouldBeTrue();

        type.SetRequiredFields(TransportOrderRequiredFieldsExtensions.All);
        Enum.GetValues<TransportOrderRequiredFields>().Aggregate(TransportOrderRequiredFields.None, (all, f) => all | f)
            .ShouldBe(TransportOrderRequiredFieldsExtensions.All, "All covers exactly the defined flags");

        Should.Throw<BusinessException>(() => type.SetRequiredFields((TransportOrderRequiredFields)4096)).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
    }

    [Fact]
    public void The_default_order_type_cannot_be_archived()
    {
        var type = OrderType();
        type.SetDefault(true);
        Should.Throw<BusinessException>(() => type.SetArchived(true)).Code.ShouldBe(ReferenceDataErrorCodes.DefaultRequired);
        type.SequenceKey.ShouldBe("Orders.TransportOrderType:" + type.Id);
    }
}
