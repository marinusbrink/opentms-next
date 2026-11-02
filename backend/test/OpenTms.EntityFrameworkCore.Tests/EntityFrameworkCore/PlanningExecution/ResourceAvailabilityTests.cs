using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Availability;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// Design 68 §2–§3 on SQLite: availability CRUD with its validation, the list's expansion (weekly repetition, working
/// pattern inverse), the warnings the trip calculator writes from it (20 double booked, 21 unavailable, 22 outside working
/// time — never on read), the recalculation fan-out of an availability change and of a trip change, the record channel's
/// availability notices, and the negative tenant-isolation tests. Risk class: High (planning, tenant data).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class ResourceAvailabilityTests : PlanboardTestBase
{
    private static ResourceUnavailabilityCreateDto Off(ResourceKind kind, Guid resource, DateTimeOffset from, DateTimeOffset till, UnavailabilityReason reason = UnavailabilityReason.Leave,
        int? weekdays = null, DateOnly? until = null, string? remarks = null)
        => new() { ResourceKind = kind, ResourceId = resource, From = from, Till = till, Reason = reason, RecurrenceWeekdays = weekdays, RecurrenceUntil = until, Remarks = remarks };

    private static ResourceAvailabilityListRequestDto ListOf(ResourceKind kind, DateTimeOffset from, DateTimeOffset to, params Guid[] ids)
        => new() { ResourceKind = kind, ResourceIds = [.. ids], From = from, To = to };

    /* CRUD */

    [Fact]
    public async Task An_unavailability_is_created_read_updated_and_deleted_with_its_stamp_and_notices()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();

        var created = await Availability.CreateAsync(Off(ResourceKind.Driver, f.Driver1, Local(8), Local(17), UnavailabilityReason.Training, remarks: " Forklift course "));
        created.ResourceKind.ShouldBe(ResourceKind.Driver);
        created.ResourceId.ShouldBe(f.Driver1);
        created.From.ShouldBe(Local(8));
        created.Till.ShouldBe(Local(17));
        created.Reason.ShouldBe(UnavailabilityReason.Training);
        created.Remarks.ShouldBe("Forklift course");
        created.ConcurrencyStamp.ShouldNotBeNullOrEmpty();
        var notice = NoticesOf(created.Id).ShouldHaveSingleItem();
        notice.TargetType.ShouldBe(PlanningExecutionApplicationModule.AvailabilityTargetType);
        notice.Hints!.From.ShouldBe(Day);
        notice.Hints.Till.ShouldBe(Day);
        notice.Hints.ResourceIds.ShouldBe([f.Driver1]);
        notice.Hints.Removed.ShouldBeFalse();

        (await Availability.GetAsync(created.Id)).Remarks.ShouldBe("Forklift course");

        var updated = await Availability.UpdateAsync(created.Id, new ResourceUnavailabilityUpdateDto
        {
            From = Local(9), Till = Local(12, 0, 28), Reason = UnavailabilityReason.Sick, ConcurrencyStamp = created.ConcurrencyStamp
        });
        updated.Till.ShouldBe(Local(12, 0, 28));
        updated.Reason.ShouldBe(UnavailabilityReason.Sick);
        updated.Remarks.ShouldBeNull();
        updated.ConcurrencyStamp.ShouldNotBe(created.ConcurrencyStamp);
        NoticesOf(created.Id).Last().Hints!.Till.ShouldBe(new DateOnly(2026, 10, 28));

        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Availability.UpdateAsync(created.Id, new ResourceUnavailabilityUpdateDto
        {
            From = Local(9), Till = Local(10), Reason = UnavailabilityReason.Sick, ConcurrencyStamp = created.ConcurrencyStamp
        }));

        await Availability.DeleteAsync(created.Id);
        NoticesOf(created.Id).Last().Hints!.Removed.ShouldBeTrue();
        await Should.ThrowAsync<EntityNotFoundException>(() => Availability.GetAsync(created.Id));
    }

    [Fact]
    public async Task Create_refuses_an_unknown_resource_a_wrong_kind_and_an_invalid_period_or_repetition()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();

        (await Should.ThrowAsync<BusinessException>(() => Availability.CreateAsync(Off(ResourceKind.Driver, Guid.NewGuid(), Local(8), Local(9))))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => Availability.CreateAsync(Off(ResourceKind.Driver, f.Tractor, Local(8), Local(9))))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound, "a vehicle is not a driver");
        (await Should.ThrowAsync<BusinessException>(() => Availability.CreateAsync(Off(ResourceKind.Vehicle, f.Driver1, Local(8), Local(9))))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => Availability.CreateAsync(Off(ResourceKind.Vehicle, f.Tractor, Local(9), Local(9))))).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidUnavailabilityPeriod);
        await Should.ThrowAsync<Volo.Abp.Validation.AbpValidationException>(() => Availability.CreateAsync(Off(ResourceKind.Vehicle, f.Tractor, Local(9), Local(10), weekdays: 0, until: new DateOnly(2026, 11, 30))));
        (await Should.ThrowAsync<BusinessException>(() => Availability.CreateAsync(Off(ResourceKind.Vehicle, f.Tractor, Local(9), Local(10), weekdays: AvailabilityWeekdays.Monday, until: new DateOnly(2026, 10, 25)))))
            .Code.ShouldBe(PlanningExecutionErrorCodes.InvalidRecurrence, "the until date before the first date");
        (await Should.ThrowAsync<BusinessException>(() => Availability.CreateAsync(Off(ResourceKind.Vehicle, f.Tractor, Local(9), Local(10), weekdays: AvailabilityWeekdays.Monday))))
            .Code.ShouldBe(PlanningExecutionErrorCodes.InvalidRecurrence);

        (await Availability.GetListAsync(ListOf(ResourceKind.Vehicle, Local(0), Local(0, 0, 30), f.Tractor))).Unavailabilities.ShouldBeEmpty("nothing was stored");
    }

    [Fact]
    public async Task The_list_expands_a_weekly_repetition_and_a_working_pattern_and_validates_its_window_and_ids()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        /* Every Monday and Wednesday 13:00–15:00 local from Monday 26 October through 11 November. */
        var weekly = await Availability.CreateAsync(Off(ResourceKind.Driver, f.Driver1, Local(13), Local(15), UnavailabilityReason.Other,
            AvailabilityWeekdays.Monday | AvailabilityWeekdays.Wednesday, new DateOnly(2026, 11, 11)));
        var patterns = await Availability.UpdatePatternsAsync(new ResourceWorkingPatternsUpdateDto
        {
            ResourceKind = ResourceKind.Driver, ResourceId = f.Driver2,
            Days = [new ResourceWorkingDayDto { Weekday = 1, Start = new TimeOnly(7, 0), End = new TimeOnly(15, 0) }, new ResourceWorkingDayDto { Weekday = 0, Start = new TimeOnly(8, 0), End = new TimeOnly(16, 0) }]
        });
        patterns.Select(p => p.Weekday).ShouldBe([0, 1]);

        var list = await Availability.GetListAsync(ListOf(ResourceKind.Driver, Local(0), Local(0, 0, 29), f.Driver1, f.Driver2, f.Driver3));

        list.Blocks.Where(b => b.ResourceId == f.Driver1).Select(b => b.From).ShouldBe([Local(13), Local(13, 0, 28)]);
        list.Blocks.Where(b => b.ResourceId == f.Driver1).ShouldAllBe(b => b.UnavailabilityId == weekly.Id && b.Reason == (int)UnavailabilityReason.Other);
        list.Unavailabilities.ShouldHaveSingleItem().Id.ShouldBe(weekly.Id);
        list.Blocks.Where(b => b.ResourceId == f.Driver2).Select(b => (b.From, b.Till)).ShouldBe([
            (Local(0), Local(8)), (Local(16), Local(7, 0, 27)), (Local(15, 0, 27), Local(0, 0, 28))
        ], "outside Monday 08–16 and Tuesday 07–15, merged across midnight; Wednesday has no row");
        list.Patterns.Count.ShouldBe(2);
        list.Blocks.ShouldNotContain(b => b.ResourceId == f.Driver3);

        var replaced = await Availability.UpdatePatternsAsync(new ResourceWorkingPatternsUpdateDto
        {
            ResourceKind = ResourceKind.Driver, ResourceId = f.Driver2, Days = [new ResourceWorkingDayDto { Weekday = 1, Start = new TimeOnly(6, 0), End = new TimeOnly(14, 0) }]
        });
        replaced.ShouldHaveSingleItem().Start.ShouldBe(new TimeOnly(6, 0), "a weekday is updated in place, the others removed");
        NoticesOf(f.Driver2).Last().Hints!.ResourceIds.ShouldBe([f.Driver2]);
        NoticesOf(f.Driver2).Last().Hints!.From.ShouldBeNull("a pattern concerns every date");

        (await Should.ThrowAsync<BusinessException>(() => Availability.GetListAsync(ListOf(ResourceKind.Driver, Local(0), Local(0).AddDays(63), f.Driver1)))).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidAvailabilityWindow);
        (await Should.ThrowAsync<BusinessException>(() => Availability.GetListAsync(ListOf(ResourceKind.Driver, Local(1), Local(0), f.Driver1)))).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidAvailabilityWindow);
        var tooMany = Enumerable.Range(0, PlanningExecutionConsts.MaxAvailabilityResourceIds + 1).Select(_ => Guid.NewGuid()).ToArray();
        (await Should.ThrowAsync<BusinessException>(() => Availability.GetListAsync(ListOf(ResourceKind.Driver, Local(0), Local(1), tooMany)))).Code.ShouldBe(PlanningExecutionErrorCodes.TooManyIds);
    }

    [Theory]
    [InlineData(7, 8, 17, true)]
    [InlineData(-1, 8, 17, true)]
    [InlineData(0, 17, 8, false)]
    [InlineData(0, 8, 8, false)]
    public async Task A_working_pattern_refuses_an_invalid_day(int weekday, int start, int end, bool byInputValidation)
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var call = () => Availability.UpdatePatternsAsync(new ResourceWorkingPatternsUpdateDto
        {
            ResourceKind = ResourceKind.Driver, ResourceId = f.Driver1, Days = [new ResourceWorkingDayDto { Weekday = weekday, Start = new TimeOnly(start, 0), End = new TimeOnly(end, 0) }]
        });
        if (byInputValidation)
        {
            /* The DTO's range (0–6) refuses it before the service runs: 400 as well. */
            await Should.ThrowAsync<Volo.Abp.Validation.AbpValidationException>(call);
        }
        else
        {
            (await Should.ThrowAsync<BusinessException>(call)).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidWorkingPattern);
        }

        (await Availability.GetListAsync(ListOf(ResourceKind.Driver, Local(0), Local(0, 0, 27), f.Driver1))).Patterns.ShouldBeEmpty("nothing was stored");
    }

    [Fact]
    public async Task A_working_pattern_refuses_a_weekday_twice_and_an_unknown_resource()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var day = new ResourceWorkingDayDto { Weekday = 2, Start = new TimeOnly(8, 0), End = new TimeOnly(16, 0) };
        (await Should.ThrowAsync<BusinessException>(() => Availability.UpdatePatternsAsync(new ResourceWorkingPatternsUpdateDto { ResourceKind = ResourceKind.Driver, ResourceId = f.Driver1, Days = [day, day] })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.InvalidWorkingPattern);
        (await Should.ThrowAsync<BusinessException>(() => Availability.UpdatePatternsAsync(new ResourceWorkingPatternsUpdateDto { ResourceKind = ResourceKind.Driver, ResourceId = Guid.NewGuid(), Days = [day] })))
            .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
    }

    /* Warnings written by the calculator */

    [Fact]
    public async Task The_calculator_writes_double_booking_unavailability_and_outside_working_time_and_reads_never_compute_them()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var trip = await TripWithAsync(f.Type, Local(6), Local(18), [f.Tractor], [f.Driver1, f.Driver2]);
        var other = await TripWithAsync(f.Type, Local(16), Local(20), [f.Rigid], [f.Driver1]);
        await Availability.CreateAsync(Off(ResourceKind.Vehicle, f.Tractor, Local(12), Local(13), UnavailabilityReason.Inspection));
        await Availability.UpdatePatternsAsync(new ResourceWorkingPatternsUpdateDto
        {
            ResourceKind = ResourceKind.Driver, ResourceId = f.Driver2, Days = [new ResourceWorkingDayDto { Weekday = 0, Start = new TimeOnly(8, 0), End = new TimeOnly(16, 0) }]
        });

        (await Trips.GetAsync(trip.Id)).Warnings.ShouldNotContain(w => w.Code >= TripWarningCode.ResourceDoubleBooked, "nothing is computed on read or save");

        await CalculateAsync(trip.Id);

        var warnings = (await Trips.GetAsync(trip.Id)).Warnings;
        warnings.ShouldContain(w => w.Code == TripWarningCode.ResourceDoubleBooked && w.Data == f.Driver1.ToString());
        warnings.ShouldContain(w => w.Code == TripWarningCode.ResourceUnavailable && w.Data == f.Tractor.ToString());
        warnings.ShouldContain(w => w.Code == TripWarningCode.OutsideWorkingTime && w.Data == f.Driver2.ToString());
        warnings.ShouldNotContain(w => w.Code == TripWarningCode.ResourceDoubleBooked && w.Data == f.Driver2.ToString());
        warnings.ShouldNotContain(w => w.Code == TripWarningCode.OutsideWorkingTime && w.Data == f.Driver1.ToString(), "a driver without a pattern is never outside it");
        var bar = (await Planboard.GetSchedulerBarsAsync(new OpenTms.PlanningExecution.Planboard.PlanboardBarsRequestDto { Ids = [trip.Id] })).Bars.Single();
        bar.WarningCodes.ShouldBe([TripWarningCode.ResourceDoubleBooked, TripWarningCode.ResourceUnavailable, TripWarningCode.OutsideWorkingTime], ignoreOrder: true);

        /* The other side of the double booking gets its warning from its own calculation. */
        await CalculateAsync(other.Id);
        (await Trips.GetAsync(other.Id)).Warnings.ShouldContain(w => w.Code == TripWarningCode.ResourceDoubleBooked && w.Data == f.Driver1.ToString());
    }

    [Fact]
    public async Task A_cancelled_trip_blocks_nothing_and_back_to_back_trips_are_no_double_booking()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var trip = await TripWithAsync(f.Type, Local(6), Local(12), [f.Tractor]);
        var cancelled = await TripWithAsync(f.Type, Local(8), Local(10), [f.Tractor]);
        await Trips.CancelAsync(cancelled.Id);
        await TripWithAsync(f.Type, Local(12), Local(14), [f.Tractor]);
        await Availability.CreateAsync(Off(ResourceKind.Vehicle, f.Tractor, Local(12), Local(13)));

        await CalculateAsync(trip.Id);

        (await Trips.GetAsync(trip.Id)).Warnings.ShouldNotContain(w => w.Code == TripWarningCode.ResourceDoubleBooked || w.Code == TripWarningCode.ResourceUnavailable,
            "a cancelled trip blocks nothing, and a period starting at the arrival does not overlap");
    }

    [Fact]
    public async Task A_weekly_unavailability_warns_only_the_trips_on_its_weekdays()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        /* Every Tuesday 10:00–11:00 local through 30 November. Monday's trip is clear; Tuesday's is not. */
        await Availability.CreateAsync(Off(ResourceKind.Driver, f.Driver3, Local(10, 0, 27), Local(11, 0, 27), UnavailabilityReason.Other, AvailabilityWeekdays.Tuesday, new DateOnly(2026, 11, 30)));
        var monday = await TripWithAsync(f.Type, Local(9), Local(12), drivers: [f.Driver3]);
        var tuesdayNextWeek = await TripWithAsync(f.Type, Local(9, 0, 27).AddDays(7), Local(12, 0, 27).AddDays(7), drivers: [f.Driver3]);

        await CalculateAsync(monday.Id);
        await CalculateAsync(tuesdayNextWeek.Id);

        (await Trips.GetAsync(monday.Id)).Warnings.ShouldNotContain(w => w.Code == TripWarningCode.ResourceUnavailable);
        (await Trips.GetAsync(tuesdayNextWeek.Id)).Warnings.ShouldContain(w => w.Code == TripWarningCode.ResourceUnavailable && w.Data == f.Driver3.ToString());
    }

    /* Fan-out */

    [Fact]
    public async Task An_availability_change_asks_the_resources_live_trips_in_the_period_before_and_after_to_recalculate()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var inPeriod = await TripWithAsync(f.Type, Future(8), Future(12), [f.Tractor]);
        var later = await TripWithAsync(f.Type, Future(8, 2), Future(12, 2), [f.Tractor]);
        var otherVehicle = await TripWithAsync(f.Type, Future(8), Future(12), [f.Rigid]);
        var cancelled = await TripWithAsync(f.Type, Future(8), Future(12), [f.Tractor]);
        await Trips.CancelAsync(cancelled.Id);

        var created = await Availability.CreateAsync(Off(ResourceKind.Vehicle, f.Tractor, Future(10), Future(11), UnavailabilityReason.Maintenance));
        Requests.ReasonsFor(PlanningExecutionApplicationModule.TripTargetType, inPeriod.Id).ShouldContain("AvailabilityCreated");
        Requests.ReasonsFor(PlanningExecutionApplicationModule.TripTargetType, later.Id).ShouldNotContain("AvailabilityCreated");
        Requests.ReasonsFor(PlanningExecutionApplicationModule.TripTargetType, otherVehicle.Id).ShouldNotContain("AvailabilityCreated");
        Requests.ReasonsFor(PlanningExecutionApplicationModule.TripTargetType, cancelled.Id).ShouldNotContain("AvailabilityCreated", "a cancelled trip is not live");

        /* Moved to day +2: both the trip it left and the trip it now covers recalculate. */
        await Availability.UpdateAsync(created.Id, new ResourceUnavailabilityUpdateDto
        {
            From = Future(10, 2), Till = Future(11, 2), Reason = UnavailabilityReason.Maintenance, ConcurrencyStamp = created.ConcurrencyStamp
        });
        Requests.ReasonsFor(PlanningExecutionApplicationModule.TripTargetType, inPeriod.Id).ShouldContain("AvailabilityChanged");
        Requests.ReasonsFor(PlanningExecutionApplicationModule.TripTargetType, later.Id).ShouldContain("AvailabilityChanged");

        await Availability.DeleteAsync(created.Id);
        Requests.ReasonsFor(PlanningExecutionApplicationModule.TripTargetType, later.Id).ShouldContain("AvailabilityDeleted");

        await Availability.UpdatePatternsAsync(new ResourceWorkingPatternsUpdateDto
        {
            ResourceKind = ResourceKind.Vehicle, ResourceId = f.Tractor, Days = [new ResourceWorkingDayDto { Weekday = 0, Start = new TimeOnly(6, 0), End = new TimeOnly(18, 0) }]
        });
        Requests.ReasonsFor(PlanningExecutionApplicationModule.TripTargetType, inPeriod.Id).ShouldContain("WorkingPatternChanged");
        Requests.ReasonsFor(PlanningExecutionApplicationModule.TripTargetType, later.Id).ShouldContain("WorkingPatternChanged");
    }

    [Fact]
    public async Task Cancelling_a_trip_asks_the_trips_it_double_booked_to_recalculate()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var trip = await TripWithAsync(f.Type, Local(6), Local(12), drivers: [f.Driver1]);
        var other = await TripWithAsync(f.Type, Local(10), Local(14), drivers: [f.Driver1]);

        await Trips.CancelAsync(trip.Id);

        Requests.ReasonsFor(PlanningExecutionApplicationModule.TripTargetType, other.Id).ShouldContain("OverlappingTripCancelled");
    }

    /* Isolation */

    [Fact]
    public async Task Negative_tenant_isolation_tenant_B_cannot_read_change_or_attach_to_tenant_A_availability()
    {
        FleetSet f;
        ResourceUnavailabilityDto created;
        using (InTenantA())
        {
            f = await FleetAsync();
            created = await Availability.CreateAsync(Off(ResourceKind.Driver, f.Driver1, Local(8), Local(17), UnavailabilityReason.Sick));
            await Availability.UpdatePatternsAsync(new ResourceWorkingPatternsUpdateDto
            {
                ResourceKind = ResourceKind.Driver, ResourceId = f.Driver1, Days = [new ResourceWorkingDayDto { Weekday = 0, Start = new TimeOnly(8, 0), End = new TimeOnly(16, 0) }]
            });
        }

        using (InTenantB())
        {
            var list = await Availability.GetListAsync(ListOf(ResourceKind.Driver, Local(0), Local(0, 0, 28), f.Driver1));
            list.Blocks.ShouldBeEmpty();
            list.Unavailabilities.ShouldBeEmpty();
            list.Patterns.ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => Availability.GetAsync(created.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => Availability.UpdateAsync(created.Id, new ResourceUnavailabilityUpdateDto
            {
                From = Local(8), Till = Local(9), Reason = UnavailabilityReason.Leave, ConcurrencyStamp = created.ConcurrencyStamp
            }));
            await Should.ThrowAsync<EntityNotFoundException>(() => Availability.DeleteAsync(created.Id));
            (await Should.ThrowAsync<BusinessException>(() => Availability.CreateAsync(Off(ResourceKind.Driver, f.Driver1, Local(8), Local(9))))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
            (await Should.ThrowAsync<BusinessException>(() => Availability.UpdatePatternsAsync(new ResourceWorkingPatternsUpdateDto
            {
                ResourceKind = ResourceKind.Driver, ResourceId = f.Driver1, Days = []
            }))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        }

        using (InTenantA())
        {
            var mine = await Availability.GetListAsync(ListOf(ResourceKind.Driver, Local(0), Local(0, 0, 28), f.Driver1));
            mine.Unavailabilities.ShouldHaveSingleItem().Reason.ShouldBe(UnavailabilityReason.Sick, "untouched");
            mine.Patterns.ShouldHaveSingleItem();
        }
    }
}
