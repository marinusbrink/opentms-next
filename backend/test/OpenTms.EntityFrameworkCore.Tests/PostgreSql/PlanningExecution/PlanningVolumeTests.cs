using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenTms.PlanningExecution.EntityFrameworkCore;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace OpenTms.PostgreSql.PlanningExecution;

/// <summary>
/// VOLUME TEST (design 56 "Expected volumes" × 5; design 59 "Cost &amp; SLO impact": list p95 &lt; 300 ms): in tenant A,
/// 50,000 legs of a day (20,000 planned on 3,000 trips with their stops, 30,000 unplanned) and the grids on top of them:
/// the unplanned-legs list (first page, filtered by date and customer, searched), the trip list (first page, the open
/// view, searched through the trigram index, sorted by number) and a trip's legs. Each read is timed warm after ANALYZE
/// and held to the 300 ms budget. The rows live in one rolled-back transaction. Risk class: High (grids).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PlanningVolumeTests : PlanningPostgreSqlTestBase
{
    private const int TripCount = 3_000;
    private const int PlannedLegCount = 20_000;
    private const int UnplannedLegCount = 30_000;
    private static readonly TimeSpan ReadBudget = TimeSpan.FromMilliseconds(300);

    private readonly ITestOutputHelper _output;

    public PlanningVolumeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task The_unplanned_legs_list_and_the_trip_list_stay_within_the_budget_at_five_times_the_daily_volume()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var typeId = await SeededTripTypeAsync();
            var context = await GetRequiredService<IDbContextProvider<PlanningExecutionDbContext>>().GetDbContextAsync();
            var random = new Random(59);
            var customers = Enumerable.Range(0, 200).Select(_ => Guid.NewGuid()).ToList();
            var cities = new[] { "Amsterdam", "Rotterdam", "Utrecht", "Eindhoven", "Groningen", "Breda", "Zwolle", "Arnhem" };

            var insert = Stopwatch.StartNew();
            var trips = new List<TransportTrip>();
            for (var i = 0; i < TripCount; i++)
            {
                var departure = new DateTimeOffset(2026, 10, 26, 5 + i % 10, (i * 7) % 60, 0, TimeSpan.FromHours(2));
                var trip = new TransportTrip(Guid.NewGuid(), TenantA, $"TR-2026-{i + 1:00000}", typeId, Day, TripExecution.OwnFleet,
                    StopCompositionMode.MergeByLocationAndActivity, FirstLastStopRule.None, departure);
                trip.SetHeader($"Run {i}", Day, departure, departure.AddHours(10), null, null, null, null, null, null, null, TripExecution.OwnFleet,
                    StopCompositionMode.MergeByLocationAndActivity, FirstLastStopRule.None, null);
                trips.Add(trip);
            }

            var legs = new List<Leg>(PlannedLegCount + UnplannedLegCount);
            var stopsPerTrip = new Dictionary<Guid, List<TripStop>>();
            for (var i = 0; i < PlannedLegCount + UnplannedLegCount; i++)
            {
                var customer = customers[i % customers.Count];
                var origin = End(random, cities, i);
                var destination = End(random, cities, i + 1);
                var leg = new Leg(Guid.NewGuid(), TenantA, Guid.NewGuid(), Guid.NewGuid(), customer, 1, LegSource.Generated, origin, destination,
                    new LegQuantities(1 + i % 20, 100 + i % 900, null, 0.4 * (1 + i % 20), 1 + i % 20, null, Pallet, null, null, []));
                if (i < PlannedLegCount)
                {
                    var trip = trips[i % TripCount];
                    leg.AssignTo(trip.Id, i / TripCount + 1);
                    leg.SetRoute(10_000 + i % 50_000, 600 + i % 3_000, null, "Fake", DateTimeOffset.UtcNow);
                    var stops = stopsPerTrip.TryGetValue(trip.Id, out var list) ? list : stopsPerTrip[trip.Id] = new List<TripStop>();
                    var from = new TripStop(Guid.NewGuid(), TenantA, trip.Id, stops.Count + 1, origin.WaypointTypeId, origin.RelationId, origin.RelationAddressId, origin.Name, origin.City, origin.Latitude, origin.Longitude, origin.TimeZoneId, false);
                    from.AddLeg(leg.Id, StopLegRole.Origin);
                    var to = new TripStop(Guid.NewGuid(), TenantA, trip.Id, stops.Count + 2, destination.WaypointTypeId, destination.RelationId, destination.RelationAddressId, destination.Name, destination.City, destination.Latitude, destination.Longitude, destination.TimeZoneId, false);
                    to.AddLeg(leg.Id, StopLegRole.Destination);
                    stops.Add(from);
                    stops.Add(to);
                }

                legs.Add(leg);
            }

            foreach (var trip in trips)
            {
                var stops = stopsPerTrip.GetValueOrDefault(trip.Id) ?? [];
                foreach (var stop in stops)
                {
                    trip.Stops.Add(stop);
                }

                trip.SetSearchKey(string.Join(" ", new[] { trip.Number, trip.Name }.Concat(stops.Select(s => s.City)).Where(p => p != null)));
            }

            foreach (var chunk in trips.Chunk(500))
            {
                await context.TransportTrips.AddRangeAsync(chunk);
                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();
            }

            foreach (var chunk in legs.Chunk(2_000))
            {
                await context.Legs.AddRangeAsync(chunk);
                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();
            }

            await context.Database.ExecuteSqlRawAsync("ANALYZE planning.\"Legs\"; ANALYZE planning.\"TransportTrips\"; ANALYZE planning.\"TripStops\"; ANALYZE planning.\"TripStopLegs\";");
            _output.WriteLine($"Inserted {TripCount} trips, {legs.Count} legs and {stopsPerTrip.Values.Sum(s => s.Count)} stops in {insert.Elapsed.TotalSeconds:F1} s.");

            var legService = GetRequiredService<ILegAppService>();
            var tripService = GetRequiredService<ITransportTripAppService>();
            var sampleTrip = trips[TripCount / 2];

            await TimedAsync("unplanned legs, first page", () => legService.GetListAsync(new LegListRequestDto { StartRow = 0, EndRow = 100 }), r => r.Rows.Count.ShouldBe(100));
            await TimedAsync("unplanned legs, by date", () => legService.GetListAsync(new LegListRequestDto { StartRow = 0, EndRow = 100, DateFrom = Day, DateTo = Day }), r => r.FilteredCount.ShouldBeGreaterThan(0));
            await TimedAsync("unplanned legs, by customer", () => legService.GetListAsync(new LegListRequestDto { StartRow = 0, EndRow = 100, CustomerRelationId = customers[3] }), r => r.FilteredCount.ShouldBeGreaterThan(0));
            await TimedAsync("unplanned legs, searched", () => legService.GetListAsync(new LegListRequestDto { StartRow = 0, EndRow = 100, WildcardSearch = "breda" }), r => r.FilteredCount.ShouldBeGreaterThan(0));
            await TimedAsync("legs of a trip", () => legService.GetListAsync(new LegListRequestDto { StartRow = 0, EndRow = 100, TripId = sampleTrip.Id }), r => r.Rows.Count.ShouldBeGreaterThan(0));
            await TimedAsync("trips, first page", () => tripService.GetListAsync(new TransportTripListRequestDto { StartRow = 0, EndRow = 100 }), r => r.TotalCount.ShouldBeGreaterThanOrEqualTo(TripCount));
            await TimedAsync("trips, open view hiding empty", () => tripService.GetListAsync(new TransportTripListRequestDto { StartRow = 0, EndRow = 100, View = "open", HideEmpty = true }), r => r.Rows.Count.ShouldBe(100));
            await TimedAsync("trips, searched", () => tripService.GetListAsync(new TransportTripListRequestDto { StartRow = 0, EndRow = 100, WildcardSearch = "zwolle" }), r => r.FilteredCount.ShouldBeGreaterThan(0));
            await TimedAsync("trips, sorted by number desc", () => tripService.GetListAsync(new TransportTripListRequestDto { StartRow = 0, EndRow = 100, SortBy = "number", SortDir = "desc" }), r => r.Rows[0].Number.ShouldStartWith("TR-2026-03"));
            await TimedAsync("trip detail", () => tripService.GetAsync(sampleTrip.Id), r => r.Stops.Count.ShouldBeGreaterThan(0));
        });
    }

    private static LegEndSnapshot End(Random random, string[] cities, int i)
    {
        var city = cities[i % cities.Length];
        return new LegEndSnapshot(Guid.NewGuid(), i % 2 == 0 ? Load : Unload, Guid.NewGuid(), Guid.NewGuid(), $"Relation {i % 5000}", city, "NL",
            51.5 + random.NextDouble(), 4.5 + random.NextDouble(), "Europe/Amsterdam", Day.AddDays(i % 3), null, null, null, 30);
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
