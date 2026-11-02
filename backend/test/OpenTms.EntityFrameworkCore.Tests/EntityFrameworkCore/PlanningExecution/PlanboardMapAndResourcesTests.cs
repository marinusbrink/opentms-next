using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData.Relations;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Availability;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.Planboard;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.TestDoubles;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// Design 68 §4 and the map and Resources-pane reads on SQLite: route segments stored by the calculator (and cleared when
/// routing is off), the trip map with lateness computed from stored estimates only, the "all trips" layer, unplanned legs
/// near a trip (radius, dates, unplanned only), the Resources pane's drivers and vehicles with their trips and blocks, and a
/// negative tenant-isolation test for every one of these reads. Risk class: High (planning, tenant data).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class PlanboardMapAndResourcesTests : PlanboardTestBase
{
    private async Task<(FleetSet F, TransportTripDto Trip)> RoutedTripAsync(DateTimeOffset departure)
    {
        var f = await FleetAsync();
        var order = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.Depot, f.CustomerA), Line(f.Depot, f.CustomerB, fromHour: 9, toHour: 14));
        var trip = await TripWithAsync(f.Type, departure, Local(20), [f.Tractor], [f.Driver1]);
        trip = await Trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = await LegIdsOfOrderAsync(order), AllowOutsideWindow = true });
        return (f, trip);
    }

    /* Route geometry and the trip map */

    [Fact]
    public async Task The_calculator_stores_one_segment_per_consecutive_stop_pair_and_the_map_returns_them()
    {
        using var _ = InTenantA();
        var (_, trip) = await RoutedTripAsync(At(6));

        (await Planboard.GetMapAsync(trip.Id)).Segments.ShouldBeEmpty("nothing is routed on save or read");

        await CalculateAsync(trip.Id);
        var map = await Planboard.GetMapAsync(trip.Id);

        map.Stops.Count.ShouldBe(3);
        map.Segments.Count.ShouldBe(2);
        for (var i = 0; i < map.Segments.Count; i++)
        {
            var from = map.Stops[i];
            var to = map.Stops[i + 1];
            var segment = map.Segments[i];
            segment.FromStopId.ShouldBe(from.Id);
            segment.ToStopId.ShouldBe(to.Id);
            var expected = World.Route(new OpenTms.Integrations.Routing.GeoPoint(from.Latitude!.Value, from.Longitude!.Value), new OpenTms.Integrations.Routing.GeoPoint(to.Latitude!.Value, to.Longitude!.Value), null);
            segment.DistanceM.ShouldBe(expected.DistanceM);
            segment.DurationS.ShouldBe(expected.DurationS);
            segment.EncodedPolyline.ShouldBeNull("the test router draws no geometry: the map draws a straight line");
        }

        map.RouteProvider.ShouldBe("Test");
        map.RoutedAt.ShouldNotBeNull();
        map.PlannedDistanceM.ShouldBe(map.Segments.Sum(s => s.DistanceM));
        map.ConcurrencyStamp.ShouldNotBeNullOrEmpty();
        World.MatrixCalls.ShouldContain(c => c.TenantId == TenantA);
    }

    [Fact]
    public async Task Without_routing_the_calculator_clears_the_stored_segments()
    {
        using var _ = InTenantA();
        var (_, trip) = await RoutedTripAsync(At(6));
        await CalculateAsync(trip.Id);
        (await Planboard.GetMapAsync(trip.Id)).Segments.ShouldNotBeEmpty();

        World.RoutingAvailable = false;
        try
        {
            await CalculateAsync(trip.Id, 2);
        }
        finally
        {
            World.RoutingAvailable = true;
        }

        var map = await Planboard.GetMapAsync(trip.Id);
        map.Segments.ShouldBeEmpty();
        map.RouteProvider.ShouldBeNull();
        map.RoutedAt.ShouldBeNull();
        (await Trips.GetAsync(trip.Id)).Warnings.ShouldContain(w => w.Code == TripWarningCode.RoutingNotConfigured);
    }

    [Fact]
    public async Task The_map_computes_lateness_from_the_stored_estimates_and_the_legs_windows_only()
    {
        using var _ = InTenantA();
        /* Departing at 12:00 local the depot's load windows (08:00–10:00 and 09:00–11:00 local) are missed. The legs' windows are
         * the addresses' local times (+01:00 on 26 October), so the test states local instants. */
        var (f, trip) = await RoutedTripAsync(Local(12));

        var before = await Planboard.GetMapAsync(trip.Id);
        before.Stops.ShouldAllBe(s => s.LatenessMinutes == null || s.EstimatedArrival != null, "no estimate, no lateness");

        await CalculateAsync(trip.Id);
        var map = await Planboard.GetMapAsync(trip.Id);

        var depot = map.Stops[0];
        depot.Name.ShouldBe("Depot Utrecht");
        depot.WindowTill.ShouldBe(Local(10), "the tightest window end of the stop's legs");
        depot.WindowFrom.ShouldBe(Local(9), "the latest window start of the stop's legs");
        depot.EstimatedArrival.ShouldNotBeNull();
        depot.LatenessMinutes.ShouldBe((int)Math.Ceiling((depot.EstimatedArrival!.Value - depot.WindowTill!.Value).TotalMinutes));
        depot.LatenessMinutes!.Value.ShouldBeGreaterThanOrEqualTo(120);
        depot.LegIds.Count.ShouldBe(2);
        foreach (var stop in map.Stops.Where(s => s.EstimatedArrival != null && s.WindowTill != null))
        {
            stop.LatenessMinutes.ShouldBe(Math.Max(0, (int)Math.Ceiling((stop.EstimatedArrival!.Value - stop.WindowTill!.Value).TotalMinutes)), stop.Name);
        }

        map.Stops.Single(s => s.Name == "Customer A").WindowFrom.ShouldBe(Local(12));
        map.DeparturePoint.ShouldBeNull("no departure address on this trip");
        f.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_map_of_an_unknown_trip_is_not_found()
    {
        using var _ = InTenantA();
        await Should.ThrowAsync<EntityNotFoundException>(() => Planboard.GetMapAsync(Guid.NewGuid()));
    }

    /* All trips layer */

    [Fact]
    public async Task The_all_trips_layer_lists_live_trips_in_the_window_filtered_by_organisation_unit()
    {
        using var _ = InTenantA();
        var (f, routed) = await RoutedTripAsync(At(6));
        var unit = await OrganizationUnitAsync("North");
        var inUnit = await TripWithAsync(f.Type, At(7), At(9), organizationUnitId: unit);
        var cancelled = await TripWithAsync(f.Type, At(7), At(9));
        await Trips.CancelAsync(cancelled.Id);
        var nextWeek = await TripWithAsync(f.Type, At(7, 0, 26).AddDays(7), At(9, 0, 26).AddDays(7));

        var layer = await Planboard.GetMapTripsAsync(new PlanboardMapTripsRequestDto { From = At(0), To = At(0, 0, 27) });
        layer.Trips.Select(t => t.Id).ShouldBe([routed.Id, inUnit.Id], "earliest first; neither cancelled nor outside the window");
        layer.Truncated.ShouldBeFalse();
        var row = layer.Trips[0];
        row.StopCount.ShouldBe(3);
        row.FirstLatitude.ShouldBe(52.09);
        row.LastLatitude.ShouldNotBeNull();
        row.Number.ShouldBe(routed.Number);
        layer.Trips[1].StopCount.ShouldBe(0);
        layer.Trips[1].FirstLatitude.ShouldBeNull();

        (await Planboard.GetMapTripsAsync(new PlanboardMapTripsRequestDto { From = At(0), To = At(0, 0, 27), OrganizationUnitIds = [unit] })).Trips.Select(t => t.Id).ShouldBe([inUnit.Id]);
        (await Should.ThrowAsync<BusinessException>(() => Planboard.GetMapTripsAsync(new PlanboardMapTripsRequestDto { From = At(0), To = At(0).AddDays(15) })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.InvalidSchedulerWindow);
        nextWeek.ShouldNotBeNull();
    }

    /* Unplanned legs near a trip */

    [Fact]
    public async Task Unplanned_near_finds_the_legs_within_the_radius_of_any_stop_nearest_first_and_only_unplanned_ones_in_the_dates()
    {
        using var _ = InTenantA();
        var (f, trip) = await RoutedTripAsync(At(6));
        var amersfoort = await PlaceAsync("AMF", "Amersfoort DC", 52.156, 5.387, RelationRoles.Customer, "Amersfoort");
        var maastricht = await PlaceAsync("MST", "Maastricht DC", 50.851, 5.691, RelationRoles.Customer, "Maastricht");
        var hilversum = await PlaceAsync("HVS", "Hilversum DC", 52.223, 5.176, RelationRoles.Customer, "Hilversum");
        var near = (await LegIdsOfOrderAsync(await ConfirmedOrderAsync(amersfoort.RelationId, Line(amersfoort, maastricht)))).Single();
        var nearer = (await LegIdsOfOrderAsync(await ConfirmedOrderAsync(hilversum.RelationId, Line(maastricht, hilversum)))).Single();
        var heerlen = await PlaceAsync("HRL", "Heerlen DC", 50.888, 5.979, RelationRoles.Customer, "Heerlen");
        var far = (await LegIdsOfOrderAsync(await ConfirmedOrderAsync(maastricht.RelationId, Line(maastricht, heerlen)))).Single();
        var otherDay = (await LegIdsOfOrderAsync(await ConfirmedOrderAsync(amersfoort.RelationId, Line(amersfoort, maastricht, day: 29)))).Single();

        var result = await Planboard.GetUnplannedNearAsync(new PlanboardUnplannedNearRequestDto { TripId = trip.Id, RadiusKm = 25, From = Day, To = Day });

        result.Legs.Select(l => l.Id).ShouldBe([nearer, near], "Hilversum (destination) lies nearer to Amsterdam/Utrecht than Amersfoort (origin)");
        result.Legs.ShouldAllBe(l => l.DistanceKm > 0 && l.DistanceKm <= 25);
        result.Legs.Select(l => l.Id).ShouldNotContain(far);
        result.Legs.Select(l => l.Id).ShouldNotContain(otherDay, "outside the dates");
        result.Names[amersfoort.RelationId].ShouldBe("Amersfoort DC");
        result.Truncated.ShouldBeFalse();
        (await Planboard.GetUnplannedNearAsync(new PlanboardUnplannedNearRequestDto { TripId = trip.Id, RadiusKm = 25, From = Day, To = Day.AddDays(3) })).Legs.Select(l => l.Id).ShouldContain(otherDay);
        (await Planboard.GetUnplannedNearAsync(new PlanboardUnplannedNearRequestDto { TripId = trip.Id, RadiusKm = 100, From = Day, To = Day })).Legs.Select(l => l.Id)
            .ShouldNotContain(far, "Maastricht–Heerlen lies about 140 km from the nearest stop");

        await Should.ThrowAsync<Volo.Abp.Validation.AbpValidationException>(() => Planboard.GetUnplannedNearAsync(new PlanboardUnplannedNearRequestDto { TripId = trip.Id, RadiusKm = 101, From = Day, To = Day }));
        (await Should.ThrowAsync<BusinessException>(() => Planboard.GetUnplannedNearAsync(new PlanboardUnplannedNearRequestDto { TripId = trip.Id, RadiusKm = 25, From = Day, To = Day.AddDays(-1) })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.InvalidNearbySearch);
        (await Should.ThrowAsync<BusinessException>(() => Planboard.GetUnplannedNearAsync(new PlanboardUnplannedNearRequestDto { TripId = trip.Id, RadiusKm = 25, From = Day, To = Day.AddDays(32) })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.InvalidNearbySearch);

        /* A leg on a trip is not unplanned. */
        var empty = await TripWithAsync(f.Type, At(6), At(18));
        await Trips.AssignLegsAsync(empty.Id, new AssignLegsDto { LegIds = [nearer], AllowOutsideWindow = true });
        (await Planboard.GetUnplannedNearAsync(new PlanboardUnplannedNearRequestDto { TripId = trip.Id, RadiusKm = 25, From = Day, To = Day })).Legs.Select(l => l.Id).ShouldBe([near]);

        await Should.ThrowAsync<EntityNotFoundException>(() => Planboard.GetUnplannedNearAsync(new PlanboardUnplannedNearRequestDto { TripId = Guid.NewGuid(), RadiusKm = 25, From = Day, To = Day }));
        (await Planboard.GetUnplannedNearAsync(new PlanboardUnplannedNearRequestDto { TripId = (await TripWithAsync(f.Type, At(6), At(7))).Id, RadiusKm = 25, From = Day, To = Day }))
            .Legs.ShouldBeEmpty("a trip without stops has nothing near");
    }

    /* Resources pane */

    [Fact]
    public async Task The_resources_pane_lists_drivers_and_vehicles_with_their_trips_and_blocks_in_the_window()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var trip = await TripWithAsync(f.Type, Local(6), Local(10), [f.Tractor, f.Trailer1], [f.Driver1]);
        var later = await TripWithAsync(f.Type, Local(12), Local(14), [f.Tractor], [f.Driver1]);
        var outside = await TripWithAsync(f.Type, Local(6, 0, 28), Local(10, 0, 28), [f.Tractor], [f.Driver1]);
        var leave = await Availability.CreateAsync(new ResourceUnavailabilityCreateDto { ResourceKind = ResourceKind.Driver, ResourceId = f.Driver2, From = Local(8), Till = Local(12), Reason = UnavailabilityReason.Leave });
        await Availability.CreateAsync(new ResourceUnavailabilityCreateDto { ResourceKind = ResourceKind.Vehicle, ResourceId = f.Trailer2, From = Local(8), Till = Local(12), Reason = UnavailabilityReason.Maintenance });

        var drivers = await Planboard.GetResourceDriversAsync(new PlanboardResourceDriversRequestDto { From = Local(0), To = Local(0, 0, 27) });
        drivers.TotalCount.ShouldBe(5);
        drivers.Items.Select(d => d.Code).ShouldBe(["D1", "D2", "D3", "D4", "D9"]);
        drivers.Items[0].TripIds.ShouldBe([trip.Id, later.Id], "earliest first; the trip on the 28th is outside the window");
        drivers.Items[0].TripIds.ShouldNotContain(outside.Id);
        drivers.Items[1].Blocks.ShouldHaveSingleItem().UnavailabilityId.ShouldBe(leave.Id);
        (await Planboard.GetResourceDriversAsync(new PlanboardResourceDriversRequestDto { From = Local(0), To = Local(0, 0, 27), Search = "bram" })).Items.ShouldHaveSingleItem().Id.ShouldBe(f.Driver2);
        (await Planboard.GetResourceDriversAsync(new PlanboardResourceDriversRequestDto { From = Local(0), To = Local(0, 0, 27), Skip = 4, Take = 1 })).Items.ShouldHaveSingleItem().Code.ShouldBe("D9");

        var vehicles = await Planboard.GetResourceVehiclesAsync(new PlanboardResourceVehiclesRequestDto { From = Local(0), To = Local(0, 0, 27) });
        vehicles.TotalCount.ShouldBe(6, "every kind, trailers included");
        vehicles.Items.Single(v => v.Id == f.Tractor).TripIds.ShouldBe([trip.Id, later.Id]);
        vehicles.Items.Single(v => v.Id == f.Trailer1).TripIds.ShouldBe([trip.Id]);
        vehicles.Items.Single(v => v.Id == f.Trailer1).Kind.ShouldBe(OpenTms.MasterData.Fleet.VehicleTypeKind.Trailer);
        vehicles.Items.Single(v => v.Id == f.Trailer2).Blocks.ShouldHaveSingleItem().Reason.ShouldBe((int)UnavailabilityReason.Maintenance);
        vehicles.Items.Single(v => v.Id == f.Rigid).TripIds.ShouldBeEmpty();

        (await Should.ThrowAsync<BusinessException>(() => Planboard.GetResourceDriversAsync(new PlanboardResourceDriversRequestDto { From = Local(0), To = Local(0).AddDays(15) })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.InvalidSchedulerWindow);
    }

    /* Isolation */

    [Fact]
    public async Task Negative_tenant_isolation_tenant_B_reads_no_map_layer_nearby_leg_or_resource_of_tenant_A()
    {
        TransportTripDto trip;
        using (InTenantA())
        {
            (_, trip) = await RoutedTripAsync(At(6));
            await CalculateAsync(trip.Id);
        }

        using (InTenantB())
        {
            await TripTypeAsync();
            await Should.ThrowAsync<EntityNotFoundException>(() => Planboard.GetMapAsync(trip.Id));
            (await Planboard.GetMapTripsAsync(new PlanboardMapTripsRequestDto { From = At(0), To = At(0, 0, 27) })).Trips.ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => Planboard.GetUnplannedNearAsync(new PlanboardUnplannedNearRequestDto { TripId = trip.Id, RadiusKm = 100, From = Day, To = Day }));
            var drivers = await Planboard.GetResourceDriversAsync(new PlanboardResourceDriversRequestDto { From = At(0), To = At(0, 0, 27) });
            drivers.Items.ShouldBeEmpty();
            drivers.TotalCount.ShouldBe(0);
            (await Planboard.GetResourceVehiclesAsync(new PlanboardResourceVehiclesRequestDto { From = At(0), To = At(0, 0, 27) })).Items.ShouldBeEmpty();

            /* Tenant B's own trip near tenant A's legs sees none of them. */
            var ownPlace = await PlaceAsync("BDEP", "B Depot", 52.09, 5.12, RelationRoles.Customer | RelationRoles.Depot, "Utrecht");
            var ownCustomer = await PlaceAsync("BCUS", "B Customer", 52.37, 4.90, RelationRoles.Customer, "Amsterdam");
            var ownOrder = await ConfirmedOrderAsync(ownPlace.RelationId, Line(ownPlace, ownCustomer));
            var ownTrip = await CreateTripAsync(await TripTypeAsync(), At(6), At(18));
            var ownLegs = await LegIdsOfOrderAsync(ownOrder);
            await Trips.AssignLegsAsync(ownTrip.Id, new AssignLegsDto { LegIds = ownLegs, AllowOutsideWindow = true });
            (await Planboard.GetUnplannedNearAsync(new PlanboardUnplannedNearRequestDto { TripId = ownTrip.Id, RadiusKm = 100, From = Day, To = Day })).Legs.ShouldBeEmpty();
        }
    }
}
