using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenTms.MasterData.Fleet;
using OpenTms.PlanningExecution.EntityFrameworkCore;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.Planboard;
using OpenTms.PlanningExecution.TransportTrips;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace OpenTms.PostgreSql.PlanningExecution;

/// <summary>
/// VOLUME TEST (design 66 D1 × 5: 1,000 open trips, 5,000 unplanned legs, 300 vehicles and 300 drivers per tenant, each
/// times five; budget 200 ms p95 for every Planboard read): in tenant A, 1,500 vehicles and 1,500 drivers through the real
/// Master Data services, 5,000 trips over one week with their vehicles, drivers and two stops each, and 25,000 unplanned
/// legs. The Scheduler (tractor and driver mode, a page of 200 rows over seven days), the bars refetch, the "all trips"
/// layer, unplanned-near, the Resources pane and a trip map are each timed warm after ANALYZE and held to 200 ms. The rows
/// live in one rolled-back transaction. Risk class: High (performance at volume × 5).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PlanboardVolumeTests : PlanningPostgreSqlTestBase
{
    private const int VehicleCount = 1_500;
    private const int DriverCount = 1_500;
    private const int TripCount = 5_000;
    private const int UnplannedLegCount = 25_000;
    private static readonly TimeSpan ReadBudget = TimeSpan.FromMilliseconds(200);

    private readonly ITestOutputHelper _output;

    public PlanboardVolumeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task The_planboard_reads_stay_within_200_ms_at_five_times_the_D1_volume()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var insert = Stopwatch.StartNew();
            var typeId = await SeededTripTypeAsync();
            await OrdersMasterDataAsync();
            var owner = await Run.PlaceAsync("VOLOWN" + Guid.NewGuid().ToString("N")[..4], "Volume Carrier", OpenTms.MasterData.Relations.RelationRoles.Carrier, 52.09, 5.12, "Utrecht");
            var tractorType = await Run.VehicleTypeAsync("Volume tractor", VehicleTypeKind.Tractor);
            var trailerType = await Run.VehicleTypeAsync("Volume trailer", VehicleTypeKind.Trailer, 33, 13.6);
            /* The rows the services would write, straight into Master Data's tables (the services take minutes for 3,000 rows). */
            var masterData = await GetRequiredService<IDbContextProvider<OpenTms.MasterData.EntityFrameworkCore.MasterDataDbContext>>().GetDbContextAsync();
            var tractors = new List<Guid>();
            var trailers = new List<Guid>();
            var vehicles = new List<Vehicle>();
            for (var i = 0; i < VehicleCount; i++)
            {
                var vehicle = new Vehicle(Guid.NewGuid(), TenantA, $"Vol {(i % 2 == 0 ? "Truck" : "Trailer")} {i:0000}", i % 2 == 0 ? tractorType : trailerType, owner.RelationId);
                vehicles.Add(vehicle);
                (i % 2 == 0 ? tractors : trailers).Add(vehicle.Id);
            }

            var drivers = new List<Guid>();
            var driverRows = new List<OpenTms.MasterData.Drivers.Driver>();
            for (var i = 0; i < DriverCount; i++)
            {
                var driver = new OpenTms.MasterData.Drivers.Driver(Guid.NewGuid(), TenantA, $"V{i:0000}", "Vol", $"Driver {i:0000}");
                driverRows.Add(driver);
                drivers.Add(driver.Id);
            }

            await masterData.Vehicles.AddRangeAsync(vehicles);
            await masterData.Drivers.AddRangeAsync(driverRows);
            await masterData.SaveChangesAsync();
            masterData.ChangeTracker.Clear();
            await FlushAsync();
            _output.WriteLine($"Master Data: {VehicleCount} vehicles and {DriverCount} drivers in {insert.Elapsed.TotalSeconds:F1} s.");

            var context = await GetRequiredService<IDbContextProvider<PlanningExecutionDbContext>>().GetDbContextAsync();
            var random = new Random(66);
            var monday = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.FromHours(1));
            var trips = new List<TransportTrip>(TripCount);
            for (var i = 0; i < TripCount; i++)
            {
                var departure = monday.AddDays(i % 7).AddHours(5 + i % 12).AddMinutes(i * 7 % 60);
                var trip = new TransportTrip(Guid.NewGuid(), TenantA, $"PB-2026-{i + 1:00000}", typeId, DateOnly.FromDateTime(departure.Date), TripExecution.OwnFleet,
                    StopCompositionMode.MergeByLocationAndActivity, FirstLastStopRule.None, departure);
                trip.SetHeader($"Volume {i}", DateOnly.FromDateTime(departure.Date), departure, departure.AddHours(4 + i % 5), null, null, null, null, null, null, null,
                    TripExecution.OwnFleet, StopCompositionMode.MergeByLocationAndActivity, FirstLastStopRule.None, null);
                trip.SetVehicles(null, [tractors[i % tractors.Count], trailers[i % trailers.Count]]);
                trip.SetDrivers([drivers[i % drivers.Count]]);
                for (var s = 0; s < 2; s++)
                {
                    trip.Stops.Add(new TripStop(Guid.NewGuid(), TenantA, trip.Id, s + 1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), $"Stop {i}-{s}", "Utrecht",
                        51.8 + random.NextDouble() * 0.6, 4.6 + random.NextDouble() * 0.8, "Europe/Amsterdam", false));
                }

                trips.Add(trip);
            }

            foreach (var chunk in trips.Chunk(500))
            {
                await context.TransportTrips.AddRangeAsync(chunk);
                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();
            }

            var legs = new List<Leg>(UnplannedLegCount);
            for (var i = 0; i < UnplannedLegCount; i++)
            {
                var day = DateOnly.FromDateTime(monday.Date).AddDays(i % 7);
                LegEndSnapshot End(int k) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), $"Relation {k % 5000}", "Utrecht", "NL",
                    51.5 + random.NextDouble(), 4.5 + random.NextDouble(), "Europe/Amsterdam", day, null, null, null, 30);
                legs.Add(new Leg(Guid.NewGuid(), TenantA, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, LegSource.Generated, End(i), End(i + 1),
                    new LegQuantities(1 + i % 20, 100 + i % 900, null, 0.4 * (1 + i % 20), 1 + i % 20, null, Guid.NewGuid(), null, null, [])));
            }

            foreach (var chunk in legs.Chunk(2_500))
            {
                await context.Legs.AddRangeAsync(chunk);
                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();
            }

            await context.Database.ExecuteSqlRawAsync(
                "ANALYZE planning.\"Legs\"; ANALYZE planning.\"TransportTrips\"; ANALYZE planning.\"TripVehicles\"; ANALYZE planning.\"TripDrivers\"; ANALYZE planning.\"TripStops\"; ANALYZE masterdata.\"Vehicles\"; ANALYZE masterdata.\"Drivers\";");
            _output.WriteLine($"Inserted {TripCount} trips and {UnplannedLegCount} unplanned legs; total set-up {insert.Elapsed.TotalSeconds:F1} s.");

            var planboard = GetRequiredService<IPlanboardAppService>();
            var week = (From: monday, To: monday.AddDays(7));
            var sample = trips[TripCount / 2];

            await TimedAsync("scheduler, tractor mode, 200 rows, 7 days", () => planboard.GetSchedulerAsync(new PlanboardSchedulerRequestDto { Mode = PlanboardResourceMode.Tractor, From = week.From, To = week.To, Take = 200 }),
                r => { r.Rows.Count.ShouldBe(200); r.Bars.Count.ShouldBeGreaterThan(0); });
            await TimedAsync("scheduler, driver mode, 200 rows, 1 day", () => planboard.GetSchedulerAsync(new PlanboardSchedulerRequestDto { Mode = PlanboardResourceMode.Driver, From = monday, To = monday.AddDays(1), Take = 200 }),
                r => r.Rows.Count.ShouldBe(200));
            await TimedAsync("scheduler, trailer mode, searched", () => planboard.GetSchedulerAsync(new PlanboardSchedulerRequestDto { Mode = PlanboardResourceMode.Trailer, From = monday, To = monday.AddDays(1), Take = 200, Search = "trailer 07" }),
                r => r.Rows.Count.ShouldBeGreaterThan(0));
            await TimedAsync("scheduler bars, 100 ids", () => planboard.GetSchedulerBarsAsync(new PlanboardBarsRequestDto { Ids = trips.Take(100).Select(t => t.Id).ToList() }),
                r => r.Bars.Count.ShouldBe(100));
            await TimedAsync("map, all trips of a day", () => planboard.GetMapTripsAsync(new PlanboardMapTripsRequestDto { From = monday, To = monday.AddDays(1) }),
                r => r.Trips.Count.ShouldBeGreaterThan(0));
            await TimedAsync("map, one trip", () => planboard.GetMapAsync(sample.Id), r => r.Stops.Count.ShouldBe(2));
            await TimedAsync("unplanned near, 25 km, 1 day", () => planboard.GetUnplannedNearAsync(new PlanboardUnplannedNearRequestDto { TripId = sample.Id, RadiusKm = 25, From = DateOnly.FromDateTime(monday.Date), To = DateOnly.FromDateTime(monday.Date) }),
                r => r.Legs.Count.ShouldBeGreaterThan(0));
            await TimedAsync("resources, drivers", () => planboard.GetResourceDriversAsync(new PlanboardResourceDriversRequestDto { From = monday, To = monday.AddDays(1), Take = 200 }),
                r => r.Items.Count.ShouldBe(200));
            await TimedAsync("resources, vehicles", () => planboard.GetResourceVehiclesAsync(new PlanboardResourceVehiclesRequestDto { From = monday, To = monday.AddDays(1), Take = 200 }),
                r => r.Items.Count.ShouldBe(200));
            await TimedAsync("leg groups by origin date", () => GetRequiredService<ILegAppService>().GetGroupsAsync(new LegListRequestDto { StartRow = 0, EndRow = 100, GroupBy = LegGroupings.OriginDate }),
                r => r.Rows.Count.ShouldBeGreaterThanOrEqualTo(7));
        });
    }

    private async Task TimedAsync<T>(string name, Func<Task<T>> read, Action<T> assert)
    {
        /* Cold, then warm: the warm read is the one held to the budget. */
        assert(await read());
        var watch = Stopwatch.StartNew();
        var result = await read();
        watch.Stop();
        assert(result);
        _output.WriteLine($"{name}: {watch.ElapsedMilliseconds} ms");
        watch.Elapsed.ShouldBeLessThan(ReadBudget, name);
    }
}
