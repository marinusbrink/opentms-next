using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData.Fleet;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Availability;
using OpenTms.PlanningExecution.Planboard;
using OpenTms.PlanningExecution.TransportTrips;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// Design 68 "API contract — reads", the Scheduler on SQLite: rows per mode (tractor = Tractor/Rigid/Van, trailer =
/// Trailer, driver), the overlap rule at the window's edges, a trip in two trailer rows, a combination's members, the KPIs,
/// the blocks, the batched names, the bars-by-ids refetch (≤ 100, live trips only), a bounded number of SQL statements
/// whatever the number of trips, and the negative tenant-isolation tests. Risk class: High (planning, tenant data).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class PlanboardSchedulerTests : PlanboardTestBase
{
    private static readonly DateTimeOffset DayStart = At(0);
    private static readonly DateTimeOffset DayEnd = At(0, 0, 27);

    [Fact]
    public async Task The_tractor_mode_lists_motor_vehicles_the_trailer_mode_trailers_and_the_driver_mode_drivers()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();

        var tractor = await Planboard.GetSchedulerAsync(Window(PlanboardResourceMode.Tractor, DayStart, DayEnd));
        tractor.Rows.Select(r => r.Id).ShouldBe([f.Rigid, f.CharterTractor, f.Tractor, f.Van], ignoreOrder: true);
        tractor.Rows.Select(r => r.Kind).Distinct().ShouldBe([VehicleTypeKind.Rigid, VehicleTypeKind.Tractor, VehicleTypeKind.Van], ignoreOrder: true);
        tractor.Rows.Single(r => r.Id == f.Tractor).DefaultDriverIds.ShouldBe([f.DefaultDriver]);
        tractor.Rows.Single(r => r.Id == f.Tractor).TypeName.ShouldBe("Tractor");
        tractor.TotalRows.ShouldBe(4);

        var trailer = await Planboard.GetSchedulerAsync(Window(PlanboardResourceMode.Trailer, DayStart, DayEnd));
        trailer.Rows.Select(r => r.Id).ShouldBe([f.Trailer1, f.Trailer2], ignoreOrder: true);

        var driver = await Planboard.GetSchedulerAsync(Window(PlanboardResourceMode.Driver, DayStart, DayEnd));
        driver.Rows.Select(r => r.Code).ShouldBe(["D1", "D2", "D3", "D4", "D9"]);
        driver.Rows[0].Name.ShouldBe("Anna Aalders");

        var searched = await Planboard.GetSchedulerAsync(Window(PlanboardResourceMode.Tractor, DayStart, DayEnd, search: "charter"));
        searched.Rows.ShouldHaveSingleItem().Id.ShouldBe(f.CharterTractor);

        var one = await Planboard.GetSchedulerAsync(Window(PlanboardResourceMode.Tractor, DayStart, DayEnd, take: 1));
        one.Rows.Count.ShouldBe(1);
        one.TotalRows.ShouldBe(4);
        var second = await Planboard.GetSchedulerAsync(new PlanboardSchedulerRequestDto { Mode = PlanboardResourceMode.Tractor, From = DayStart, To = DayEnd, Skip = 1, Take = 1 });
        second.Rows.ShouldHaveSingleItem().Id.ShouldNotBe(one.Rows[0].Id);

        /* The page size is 1..200 (input validation, 400). */
        await Should.ThrowAsync<Volo.Abp.Validation.AbpValidationException>(() => Planboard.GetSchedulerAsync(Window(PlanboardResourceMode.Tractor, DayStart, DayEnd, take: 0)));
        await Should.ThrowAsync<Volo.Abp.Validation.AbpValidationException>(() => Planboard.GetSchedulerAsync(Window(PlanboardResourceMode.Tractor, DayStart, DayEnd, take: PlanningExecutionConsts.MaxPlanboardPageSize + 1)));
        (await Planboard.GetSchedulerAsync(Window(PlanboardResourceMode.Tractor, DayStart, DayEnd, take: PlanningExecutionConsts.MaxPlanboardPageSize))).Rows.Count.ShouldBe(4);
    }

    [Fact]
    public async Task A_bar_overlaps_the_window_when_it_departs_before_its_end_and_arrives_at_or_after_its_start()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var inside = await TripWithAsync(f.Type, At(6), At(18), [f.Tractor]);
        var endsAtStart = await TripWithAsync(f.Type, At(18, 0, 25), At(0, 0, 26), [f.Tractor]);
        var endedBefore = await TripWithAsync(f.Type, At(10, 0, 25), At(23, 59, 25), [f.Tractor]);
        var departsAtEnd = await TripWithAsync(f.Type, At(0, 0, 27), At(6, 0, 27), [f.Tractor]);
        var noArrival = await TripWithAsync(f.Type, At(23, 0), null, [f.Tractor]);
        var cancelled = await TripWithAsync(f.Type, At(8), At(9), [f.Tractor]);
        await Trips.CancelAsync(cancelled.Id);
        var archived = await TripWithAsync(f.Type, At(8), At(9), [f.Tractor]);
        await Trips.CancelAsync(archived.Id);
        await Trips.ArchiveAsync(archived.Id);
        var otherResource = await TripWithAsync(f.Type, At(8), At(9), [f.CharterTractor]);

        var page = await Planboard.GetSchedulerAsync(Window(PlanboardResourceMode.Tractor, DayStart, DayEnd, search: "Truck 01"));

        page.Rows.ShouldHaveSingleItem().Id.ShouldBe(f.Tractor);
        page.Bars.Select(b => b.Id).ShouldBe([endsAtStart.Id, inside.Id, noArrival.Id], "earliest departure first; the arrival at the window's start counts, a departure at its end does not");
        page.Bars.Select(b => b.Id).ShouldNotContain(endedBefore.Id);
        page.Bars.Select(b => b.Id).ShouldNotContain(departsAtEnd.Id);
        page.Bars.Select(b => b.Id).ShouldNotContain(cancelled.Id, "a cancelled trip is not on the Scheduler");
        page.Bars.Select(b => b.Id).ShouldNotContain(otherResource.Id, "only trips of the page's rows");
        page.BarsTruncated.ShouldBeFalse();

        var bar = page.Bars.Single(b => b.Id == inside.Id);
        bar.Number.ShouldBe(inside.Number);
        bar.Status.ShouldBe(TransportTripStatus.Planned);
        bar.VehicleIds.ShouldBe([f.Tractor]);
        bar.ConcurrencyStamp.ShouldBe(inside.ConcurrencyStamp);
        page.Names[f.Tractor].ShouldContain("Truck 01");

        (await Should.ThrowAsync<BusinessException>(() => Planboard.GetSchedulerAsync(Window(PlanboardResourceMode.Tractor, DayStart, DayStart.AddDays(14).AddMinutes(1)))))
            .Code.ShouldBe(PlanningExecutionErrorCodes.InvalidSchedulerWindow);
        (await Should.ThrowAsync<BusinessException>(() => Planboard.GetSchedulerAsync(Window(PlanboardResourceMode.Tractor, DayEnd, DayStart))))
            .Code.ShouldBe(PlanningExecutionErrorCodes.InvalidSchedulerWindow);
    }

    [Fact]
    public async Task A_trip_with_two_trailers_is_one_bar_on_both_trailer_rows_and_a_combinations_members_carry_its_trip()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var doubleTrailer = await TripWithAsync(f.Type, At(6), At(12), [f.Tractor, f.Trailer1, f.Trailer2], [f.Driver1, f.Driver2]);
        var combination = await CombinationAsync("Combi 1", f.CharterTractor, f.Trailer1);
        var combined = await TripWithAsync(f.Type, At(13), At(17), combination: combination);

        var trailers = await Planboard.GetSchedulerAsync(Window(PlanboardResourceMode.Trailer, DayStart, DayEnd));
        trailers.Bars.Count(b => b.Id == doubleTrailer.Id).ShouldBe(1, "one bar; the client draws it on every row of its vehicles");
        trailers.Bars.Single(b => b.Id == doubleTrailer.Id).VehicleIds.ShouldBe([f.Tractor, f.Trailer1, f.Trailer2], "by position");
        trailers.Bars.Select(b => b.Id).ShouldContain(combined.Id, "the combination's trailer is a trip vehicle row (FromCombination)");
        trailers.Kpis.RowsWithoutTrip.ShouldBe(0);

        var tractors = await Planboard.GetSchedulerAsync(Window(PlanboardResourceMode.Tractor, DayStart, DayEnd));
        tractors.Bars.Single(b => b.Id == combined.Id).VehicleIds.ShouldBe([f.CharterTractor, f.Trailer1], "the combination's members in member order");

        var drivers = await Planboard.GetSchedulerAsync(Window(PlanboardResourceMode.Driver, DayStart, DayEnd));
        drivers.Bars.ShouldHaveSingleItem().Id.ShouldBe(doubleTrailer.Id);
        drivers.Bars[0].DriverIds.ShouldBe([f.Driver1, f.Driver2]);
        drivers.Names[f.Driver1].ShouldBe("Anna Aalders");
        drivers.Names[f.Driver2].ShouldBe("Bram Bakker");
        drivers.Names.ShouldContainKey(f.Tractor, "every vehicle on the bars is named too");
    }

    [Fact]
    public async Task The_kpis_count_rows_trips_running_trips_empty_rows_and_the_planned_share_of_the_window()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        /* Five driver rows over 24 hours: 12 h + 6 h of trips (one clipped at the window start) = 18 of 120 h. */
        await TripWithAsync(f.Type, At(6), At(18), drivers: [f.Driver1]);
        var running = await TripWithAsync(f.Type, At(21, 0, 25), At(6), drivers: [f.Driver2]);
        await Trips.StartAsync(running.Id, new TripTimestampDto { At = At(21, 5, 25) });

        var page = await Planboard.GetSchedulerAsync(Window(PlanboardResourceMode.Driver, DayStart, DayEnd));

        page.Kpis.Rows.ShouldBe(5);
        page.Kpis.Trips.ShouldBe(2);
        page.Kpis.InProgress.ShouldBe(1);
        page.Kpis.RowsWithoutTrip.ShouldBe(3);
        page.Kpis.UtilisationPercent.ShouldBe(15.0m, "18 planned hours of 5 rows x 24 hours");
    }

    [Fact]
    public async Task The_blocks_of_the_rows_in_the_window_carry_the_unavailability_and_outside_working_time()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var leave = await Availability.CreateAsync(new ResourceUnavailabilityCreateDto
        {
            ResourceKind = ResourceKind.Driver, ResourceId = f.Driver1, From = At(10), Till = At(14), Reason = UnavailabilityReason.Sick, Remarks = "dentist"
        });
        await Availability.UpdatePatternsAsync(new ResourceWorkingPatternsUpdateDto
        {
            ResourceKind = ResourceKind.Driver, ResourceId = f.Driver2, Days = [new ResourceWorkingDayDto { Weekday = 0, Start = new TimeOnly(8, 0), End = new TimeOnly(17, 0) }]
        });
        await Availability.CreateAsync(new ResourceUnavailabilityCreateDto
        {
            ResourceKind = ResourceKind.Vehicle, ResourceId = f.Tractor, From = At(10), Till = At(14), Reason = UnavailabilityReason.Maintenance
        });

        var page = await Planboard.GetSchedulerAsync(Window(PlanboardResourceMode.Driver, Local(0), Local(0, 0, 27)));

        var sick = page.Blocks.Single(b => b.ResourceId == f.Driver1);
        sick.UnavailabilityId.ShouldBe(leave.Id);
        sick.From.ShouldBe(At(10));
        sick.Till.ShouldBe(At(14));
        sick.Reason.ShouldBe((int)UnavailabilityReason.Sick, "the admin may read availability, so the reason is shown");
        page.Blocks.Where(b => b.ResourceId == f.Driver2).Select(b => (b.From, b.Till)).ShouldBe([(Local(0), Local(8)), (Local(17), Local(0, 0, 27))], "outside Monday 08:00-17:00 local");
        page.Blocks.ShouldNotContain(b => b.ResourceId == f.Tractor, "the driver mode shows driver blocks only");
    }

    [Fact]
    public async Task The_bars_refetch_returns_live_trips_by_id_and_refuses_more_than_one_hundred_ids()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var a = await TripWithAsync(f.Type, At(6), At(12), [f.Tractor], [f.Driver1], carrier: f.Carrier);
        var b = await TripWithAsync(f.Type, At(13), At(15), [f.Rigid]);
        var cancelled = await TripWithAsync(f.Type, At(13), At(15), [f.Van]);
        await Trips.CancelAsync(cancelled.Id);

        var bars = await Planboard.GetSchedulerBarsAsync(new PlanboardBarsRequestDto { Ids = [b.Id, a.Id, cancelled.Id, Guid.NewGuid(), a.Id] });
        bars.Bars.Select(x => x.Id).ShouldBe([a.Id, b.Id], "earliest first; cancelled and unknown ids are absent");
        bars.Names[f.Carrier].ShouldBe("Charter Carrier");
        bars.Names[f.Driver1].ShouldBe("Anna Aalders");

        var hundred = Enumerable.Range(0, PlanningExecutionConsts.MaxIdsPerQuery).Select(_ => Guid.NewGuid()).ToList();
        (await Planboard.GetSchedulerBarsAsync(new PlanboardBarsRequestDto { Ids = hundred })).Bars.ShouldBeEmpty();
        (await Should.ThrowAsync<BusinessException>(() => Planboard.GetSchedulerBarsAsync(new PlanboardBarsRequestDto { Ids = [.. hundred, Guid.NewGuid()] })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.TooManyIds);
    }

    [Fact]
    public async Task The_scheduler_read_runs_the_same_number_of_statements_for_two_trips_and_for_twelve()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        await TripWithAsync(f.Type, At(6), At(8), [f.Tractor, f.Trailer1], [f.Driver1], carrier: f.Carrier);
        await TripWithAsync(f.Type, At(9), At(11), [f.Rigid], [f.Driver2]);
        var request = Window(PlanboardResourceMode.Tractor, DayStart, DayEnd);
        await Planboard.GetSchedulerAsync(request); // warm-up: feature, permission and setting caches

        int few;
        System.Collections.Generic.List<string> fewCommands;
        using (var capture = EfCommandCapture.Begin())
        {
            (await Planboard.GetSchedulerAsync(request)).Bars.Count.ShouldBe(2);
            few = capture.Commands.Count;
            fewCommands = capture.Commands.ToList();
        }

        for (var i = 0; i < 10; i++)
        {
            var vehicle = new[] { f.Tractor, f.Rigid, f.Van, f.CharterTractor }[i % 4];
            await TripWithAsync(f.Type, At(12, i * 5), At(13, i * 5), [vehicle, f.Trailer2], [f.Driver3, f.Driver4], carrier: f.Carrier);
        }

        int many;
        using (var capture = EfCommandCapture.Begin())
        {
            (await Planboard.GetSchedulerAsync(request)).Bars.Count.ShouldBe(12);
            many = capture.Commands.Count;
            capture.On("TransportTrips").Count.ShouldBeGreaterThan(0, "the capture sees the trip statements");
        }

        many.ShouldBe(few, "no statement per trip (N+1)");
        /* The test tenants are not in the tenant store, so ABP looks the tenant up per DbContext (cached in production). */
        var statements = fewCommands.Where(c => !c.Contains("\"AbpTenants\"", StringComparison.Ordinal)).ToList();
        statements.Count.ShouldBeLessThanOrEqualTo(15, "rows (count, page, default drivers, units), trip ids, bars (trips, vehicles, drivers), "
            + "availability (unavailabilities, patterns), names (vehicles, drivers, carriers):\n" + string.Join("\n---\n", statements));
    }

    [Fact]
    public async Task Negative_tenant_isolation_tenant_B_sees_no_rows_bars_blocks_or_names_of_tenant_A()
    {
        FleetSet f;
        Guid tripId;
        using (InTenantA())
        {
            f = await FleetAsync();
            tripId = (await TripWithAsync(f.Type, At(6), At(18), [f.Tractor, f.Trailer1], [f.Driver1])).Id;
            await Availability.CreateAsync(new ResourceUnavailabilityCreateDto { ResourceKind = ResourceKind.Driver, ResourceId = f.Driver1, From = At(1), Till = At(2), Reason = UnavailabilityReason.Leave });
        }

        using (InTenantB())
        {
            await TripTypeAsync();
            foreach (var mode in new[] { PlanboardResourceMode.Tractor, PlanboardResourceMode.Trailer, PlanboardResourceMode.Driver })
            {
                var page = await Planboard.GetSchedulerAsync(Window(mode, DayStart, DayEnd));
                page.Rows.ShouldBeEmpty(mode.ToString());
                page.Bars.ShouldBeEmpty(mode.ToString());
                page.Blocks.ShouldBeEmpty(mode.ToString());
                page.Names.ShouldBeEmpty(mode.ToString());
                page.TotalRows.ShouldBe(0);
            }

            var bars = await Planboard.GetSchedulerBarsAsync(new PlanboardBarsRequestDto { Ids = [tripId] });
            bars.Bars.ShouldBeEmpty("another tenant's trip id answers nothing");
            bars.Names.ShouldBeEmpty();
        }
    }
}
