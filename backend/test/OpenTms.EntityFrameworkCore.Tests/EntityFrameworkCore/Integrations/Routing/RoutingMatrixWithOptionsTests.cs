using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;
using OpenTms.Integrations.Routing;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Integrations.Routing;

/// <summary>
/// Design 74 §9: <c>GetMatrixAsync</c> with geometry, toll or CO₂ reads the cache once, asks the missing pairs at most four
/// at a time and stores everything once. Through the real container on SQLite, with the real providers on the fake
/// transport. Every pair answers its own values (<see cref="FakeRoutingEngines"/>), so a leg that lands in the wrong place
/// is visible: toll is a pricing input. Overlap and order are forced with signals rather than timing, so a sequential
/// implementation fails on the assertion and not on a clock.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class RoutingMatrixWithOptionsTests : OpenTmsTestBase<RoutingTestModule>
{
    private static readonly Guid TenantA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

    private static readonly RoutingOptions Everything = new(IncludeGeometry: true, IncludeToll: true, IncludeCo2: true);

    private readonly IRoutingService _routing;
    private readonly RoutingHttpHandler _handler;
    private readonly ManualTimeProvider _time;
    private readonly ICurrentTenant _currentTenant;
    private readonly IRepository<RouteCacheEntry, Guid> _cache;

    public RoutingMatrixWithOptionsTests()
    {
        _routing = GetRequiredService<IRoutingService>();
        _handler = GetRequiredService<RoutingHttpHandler>();
        _time = GetRequiredService<ManualTimeProvider>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _cache = GetRequiredService<IRepository<RouteCacheEntry, Guid>>();
        _handler.Reset();
        _time.Now = RoutingTestModule.Start;
    }

    private FakeRoutingEngines EnginesFor(IReadOnlyList<GeoPoint> stops)
    {
        var engines = new FakeRoutingEngines(stops);
        _handler.Fallback = engines.AnswerAsync;
        return engines;
    }

    private static string KeyOf(IReadOnlyList<GeoPoint> stops, int pair)
        => RouteCacheKey.Build(stops[pair], stops[pair + 1], VehicleSpec.Default, "Osrm+" + RoutingConsts.RouteFunctionProviderName, Everything);

    [Fact]
    public async Task Legs_stay_in_pair_order_when_earlier_pairs_answer_later()
    {
        var stops = FakeRoutingEngines.Stops(7);
        var engines = EnginesFor(stops);

        /* The first four pairs start together; each one answers only after the next one has, so 3 → 2 → 1 → 0. */
        var answered = Enumerable.Range(0, 6).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var answerOrder = new ConcurrentQueue<int>();
        engines.BeforeAnswer = async (lane, function) =>
        {
            if (function)
            {
                return;
            }

            if (lane < 3)
            {
                await FakeRoutingEngines.Patiently(answered[lane + 1].Task);
            }

            answerOrder.Enqueue(lane);
            answered[lane].TrySetResult();
        };

        using (_currentTenant.Change(TenantA))
        {
            var matrix = await _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything);

            var order = answerOrder.ToList();
            order.IndexOf(3).ShouldBeLessThan(order.IndexOf(0), "the test only proves something when a later pair answers first");
            order.Where(l => l < 4).ToList().ShouldBe([3, 2, 1, 0], "a freed slot may let pair 4 or 5 finish in between");

            matrix.ShouldNotBeNull();
            matrix.Legs.Count.ShouldBe(6);
            for (var pair = 0; pair < 6; pair++)
            {
                FakeRoutingEngines.ShouldBeLane(matrix.Legs[pair], pair, fromCache: false);
            }

            matrix.TotalDistanceM.ShouldBe(Enumerable.Range(0, 6).Sum(l => FakeRoutingEngines.DistanceOf(l)));
            matrix.Provider.ShouldBe("Osrm");
        }
    }

    [Fact]
    public async Task Calls_really_overlap_and_never_exceed_four_at_once()
    {
        var stops = FakeRoutingEngines.Stops(11);
        var engines = EnginesFor(stops);

        /* Nobody answers before four are in flight; after that each call stays long enough for a fifth to show up. */
        var fourInFlight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engines.BeforeAnswer = async (_, _) =>
        {
            if (engines.InFlight >= 4)
            {
                fourInFlight.TrySetResult();
            }

            await FakeRoutingEngines.Patiently(fourInFlight.Task);
            await Task.Delay(20);
        };

        using (_currentTenant.Change(TenantA))
        {
            var matrix = await _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything);

            matrix.ShouldNotBeNull().Legs.Count.ShouldBe(10);
            engines.Peak.ShouldBe(4, "ten missing pairs: four at a time, not one and not ten");
            engines.Calls.Count(c => !c.Function).ShouldBe(10);
            engines.Calls.Count(c => c.Function).ShouldBe(10);
            (await _cache.GetListAsync()).Count.ShouldBe(10);
        }
    }

    [Fact]
    public async Task Five_times_the_largest_trip_of_the_report_stays_at_four_at_once_and_in_order()
    {
        /* The task names 10-15 stops as the trips that waited; five times that is 75 stops. On the fake transport this proves
         * the shape (bounded fan-out, order, one effect per pair), not the seconds against a real engine. */
        var stops = FakeRoutingEngines.Stops(75);
        var engines = EnginesFor(stops);
        engines.BeforeAnswer = (_, _) => Task.Delay(2);

        using (_currentTenant.Change(TenantA))
        {
            var matrix = await _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything);

            matrix.ShouldNotBeNull().Legs.Count.ShouldBe(74);
            for (var pair = 0; pair < 74; pair++)
            {
                FakeRoutingEngines.ShouldBeLane(matrix.Legs[pair], pair, fromCache: false);
            }

            engines.Peak.ShouldBeLessThanOrEqualTo(4);
            engines.Calls.Count.ShouldBe(148);
            (await _cache.GetListAsync()).Count.ShouldBe(74);

            var again = await _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything);
            again.ShouldNotBeNull().Legs.ShouldAllBe(l => l.FromCache);
            engines.Calls.Count.ShouldBe(148, "a repeat of the big trip asks nothing");
        }
    }

    [Fact]
    public async Task A_repeated_pair_is_asked_once_and_both_legs_get_its_values()
    {
        var a = RoutingHarness.Amsterdam;
        var b = RoutingHarness.Rotterdam;
        GeoPoint[] stops = [a, b, a, b];
        var engines = EnginesFor(stops);

        using (_currentTenant.Change(TenantA))
        {
            var matrix = await _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything);

            matrix.ShouldNotBeNull();
            matrix.Legs.Count.ShouldBe(3);
            FakeRoutingEngines.ShouldBeLane(matrix.Legs[0], 0, fromCache: false);
            FakeRoutingEngines.ShouldBeLane(matrix.Legs[1], 1, fromCache: false);
            FakeRoutingEngines.ShouldBeLane(matrix.Legs[2], 0, fromCache: false);

            engines.Calls.Count(c => c is { Lane: 0, Function: false }).ShouldBe(1, "A→B goes to the engine once");
            engines.Calls.Count(c => c is { Lane: 0, Function: true }).ShouldBe(1, "and to the function once");
            engines.Calls.Count(c => c is { Lane: 1, Function: false }).ShouldBe(1, "B→A is a different question");
            engines.Calls.Count.ShouldBe(4);
            _handler.Requests.Count.ShouldBe(4);

            (await _cache.GetListAsync()).Count.ShouldBe(2, "one row per distinct pair: a second insert would hit the unique key");

            var cached = await _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything);
            cached.ShouldNotBeNull();
            FakeRoutingEngines.ShouldBeLane(cached.Legs[0], 0, fromCache: true);
            FakeRoutingEngines.ShouldBeLane(cached.Legs[1], 1, fromCache: true);
            FakeRoutingEngines.ShouldBeLane(cached.Legs[2], 0, fromCache: true);
            engines.Calls.Count.ShouldBe(4);
        }
    }

    [Fact]
    public async Task Cached_pairs_are_not_asked_and_are_marked_and_a_second_call_makes_no_requests()
    {
        var stops = FakeRoutingEngines.Stops(5);
        var engines = EnginesFor(stops);

        using (_currentTenant.Change(TenantA))
        {
            /* The same row as the single route question: design 68 §4 says the matrix shares its key and cache. */
            (await _routing.GetRouteAsync(stops[1], stops[2], VehicleSpec.Default, Everything)).ShouldNotBeNull();
            (await _routing.GetRouteAsync(stops[3], stops[4], VehicleSpec.Default, Everything)).ShouldNotBeNull();
            engines.Calls.Clear();

            var matrix = await _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything);

            matrix.ShouldNotBeNull();
            FakeRoutingEngines.ShouldBeLane(matrix.Legs[0], 0, fromCache: false);
            FakeRoutingEngines.ShouldBeLane(matrix.Legs[1], 1, fromCache: true);
            FakeRoutingEngines.ShouldBeLane(matrix.Legs[2], 2, fromCache: false);
            FakeRoutingEngines.ShouldBeLane(matrix.Legs[3], 3, fromCache: true);
            engines.Calls.Select(c => c.Lane).Distinct().OrderBy(l => l).ToList().ShouldBe([0, 2], "only the pairs the cache misses");
            engines.Calls.Count.ShouldBe(4);
            (await _cache.GetListAsync()).Count.ShouldBe(4);

            var requestsSoFar = _handler.Requests.Count;
            var second = await _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything);

            second.ShouldNotBeNull();
            for (var pair = 0; pair < 4; pair++)
            {
                FakeRoutingEngines.ShouldBeLane(second.Legs[pair], pair, fromCache: true);
            }

            _handler.Requests.Count.ShouldBe(requestsSoFar, "a second identical call makes no requests");
            (await _cache.GetListAsync()).Count.ShouldBe(4);
        }
    }

    [Fact]
    public async Task Expired_rows_are_asked_again_and_refreshed_in_place()
    {
        var stops = FakeRoutingEngines.Stops(4);
        var engines = EnginesFor(stops);

        using (_currentTenant.Change(TenantA))
        {
            await _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything);
            var ids = (await _cache.GetListAsync()).Select(r => r.Id).OrderBy(i => i).ToList();
            engines.Calls.Clear();

            _time.Now = RoutingTestModule.Start.AddDays(30).AddMinutes(1);
            var matrix = await _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything);

            matrix.ShouldNotBeNull().Legs.ShouldAllBe(l => !l.FromCache);
            engines.Calls.Count.ShouldBe(6);
            var rows = await _cache.GetListAsync();
            rows.Select(r => r.Id).OrderBy(i => i).ToList().ShouldBe(ids, "the key stays unique");
            rows.ShouldAllBe(r => r.ExpiresAt == _time.Now.UtcDateTime.AddDays(30));
        }
    }

    [Fact]
    public async Task A_pair_without_a_path_makes_the_matrix_null_keeps_the_others_and_the_retry_asks_only_that_pair()
    {
        var stops = FakeRoutingEngines.Stops(5);
        var engines = EnginesFor(stops);
        engines.Failure = (lane, function) => lane == 2 && !function ? RoutingHttpHandler.Status(HttpStatusCode.BadRequest, RoutingRecorded.OsrmNoRoute) : null;

        using (_currentTenant.Change(TenantA))
        {
            (await _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything)).ShouldBeNull();

            engines.Calls.ShouldNotContain(c => c.Lane == 2 && c.Function, "no route means nothing to ask the function");
            var rows = await _cache.GetListAsync();
            rows.Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal).ShouldBe(new[] { 0, 1, 3 }.Select(p => KeyOf(stops, p)).OrderBy(k => k, StringComparer.Ordinal));

            /* Still no path: only the failed pair goes out again, and the answer is still null. */
            engines.Calls.Clear();
            (await _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything)).ShouldBeNull();
            engines.Calls.ShouldHaveSingleItem().ShouldBe((2, false));

            /* The road opens. */
            engines.Calls.Clear();
            engines.Failure = null;
            var matrix = await _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything);

            matrix.ShouldNotBeNull();
            engines.Calls.Select(c => c.Lane).Distinct().ShouldBe([2]);
            FakeRoutingEngines.ShouldBeLane(matrix.Legs[0], 0, fromCache: true);
            FakeRoutingEngines.ShouldBeLane(matrix.Legs[1], 1, fromCache: true);
            FakeRoutingEngines.ShouldBeLane(matrix.Legs[2], 2, fromCache: false);
            FakeRoutingEngines.ShouldBeLane(matrix.Legs[3], 3, fromCache: true);
        }
    }

    [Fact]
    public async Task A_function_down_for_one_pair_reaches_the_caller_caches_the_complete_pairs_and_the_retry_asks_only_that_pair()
    {
        var stops = FakeRoutingEngines.Stops(7);
        var engines = EnginesFor(stops);
        engines.Failure = (lane, function) => lane == 1 && function ? RoutingHttpHandler.Status(HttpStatusCode.BadGateway, "{}") : null;

        using (_currentTenant.Change(TenantA))
        {
            var failure = await Should.ThrowAsync<RoutingUnavailableException>(() => _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything));
            failure.StatusCode.ShouldBe(502);
            failure.Code.ShouldBe(OpenTms.Integrations.IntegrationsErrorCodes.RoutingUnavailable);

            var rows = await _cache.GetListAsync();
            rows.Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal)
                .ShouldBe(new[] { 0, 2, 3, 4, 5 }.Select(p => KeyOf(stops, p)).OrderBy(k => k, StringComparer.Ordinal), "every pair that came back complete, none without its toll");
            rows.ShouldAllBe(r => r.TollAmount != null && r.TollDistanceM != null && r.Co2Kg != null);

            engines.Calls.Clear();
            engines.Failure = null;
            var matrix = await _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything);

            matrix.ShouldNotBeNull();
            engines.Calls.Select(c => c.Lane).Distinct().ShouldBe([1], "the retry asks only what is missing");
            for (var pair = 0; pair < 6; pair++)
            {
                FakeRoutingEngines.ShouldBeLane(matrix.Legs[pair], pair, fromCache: pair != 1);
            }
        }
    }

    [Fact]
    public async Task A_function_down_for_every_pair_throws_the_routing_exception_and_caches_nothing()
    {
        var stops = FakeRoutingEngines.Stops(6);
        var engines = EnginesFor(stops);
        engines.Failure = (_, function) => function ? RoutingHttpHandler.Status(HttpStatusCode.BadGateway, "{}") : null;

        using (_currentTenant.Change(TenantA))
        {
            /* Five pairs fail at once: the caller still sees the routing exception Hangfire retries on, not an aggregate. */
            (await Should.ThrowAsync<RoutingUnavailableException>(() => _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything)))
                .StatusCode.ShouldBe(502);
            (await _cache.GetListAsync()).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task An_engine_down_for_one_pair_reaches_the_caller_and_the_complete_pairs_are_cached()
    {
        var stops = FakeRoutingEngines.Stops(4);
        var engines = EnginesFor(stops);
        engines.Failure = (lane, function) => lane == 0 && !function ? RoutingHttpHandler.Status(HttpStatusCode.ServiceUnavailable, "{}") : null;

        using (_currentTenant.Change(TenantA))
        {
            (await Should.ThrowAsync<RoutingUnavailableException>(() => _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything)))
                .StatusCode.ShouldBe(503);

            engines.Calls.ShouldNotContain(c => c.Lane == 0 && c.Function);
            (await _cache.GetListAsync()).Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal)
                .ShouldBe(new[] { 1, 2 }.Select(p => KeyOf(stops, p)).OrderBy(k => k, StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task A_function_without_a_path_leaves_that_legs_toll_empty_and_does_not_pin_it_in_the_cache()
    {
        var stops = FakeRoutingEngines.Stops(4);
        var engines = EnginesFor(stops);
        engines.Failure = (lane, function) => lane == 1 && function
            ? RoutingHttpHandler.Status((HttpStatusCode)ValhallaFunctionProvider.NoPathStatusCode, RoutingRecorded.FunctionNoPathBody)
            : null;

        using (_currentTenant.Change(TenantA))
        {
            var matrix = await _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything);

            matrix.ShouldNotBeNull();
            FakeRoutingEngines.ShouldBeLane(matrix.Legs[0], 0, fromCache: false);
            FakeRoutingEngines.ShouldBeLane(matrix.Legs[2], 2, fromCache: false);
            matrix.Legs[1].DistanceM.ShouldBe(FakeRoutingEngines.DistanceOf(1), "the engine found the road");
            matrix.Legs[1].TollAmount.ShouldBeNull();
            matrix.Legs[1].TollDistanceM.ShouldBeNull();
            matrix.Legs[1].Co2Kg.ShouldBeNull();

            (await _cache.GetListAsync()).Select(r => r.Key).ShouldNotContain(KeyOf(stops, 1), "no toll is not 30 days of no toll");
            (await _cache.GetListAsync()).Count.ShouldBe(2);

            engines.Calls.Clear();
            engines.Failure = null;
            var retried = await _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything);
            retried.ShouldNotBeNull();
            FakeRoutingEngines.ShouldBeLane(retried.Legs[1], 1, fromCache: false);
            engines.Calls.Select(c => c.Lane).Distinct().ShouldBe([1]);
        }
    }

    [Fact]
    public async Task A_token_cancelled_midway_starts_no_further_calls_and_reaches_the_caller()
    {
        var stops = FakeRoutingEngines.Stops(11);
        var engines = EnginesFor(stops);

        /* The four in flight answer only after Cancel() has returned, so the six waiting pairs are cancelled before a slot
         * frees: a call that still starts is a call the token did not stop. */
        var fourStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        engines.BeforeAnswer = async (_, _) =>
        {
            if (Interlocked.Increment(ref started) == 4)
            {
                fourStarted.TrySetResult();
            }

            await release.Task.WaitAsync(TimeSpan.FromSeconds(1));
        };

        using var cancellation = new CancellationTokenSource();
        using (_currentTenant.Change(TenantA))
        {
            var running = _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything, cancellation.Token);
            await fourStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await cancellation.CancelAsync();
            release.SetResult();

            await Should.ThrowAsync<OperationCanceledException>(() => running);

            engines.Calls.Count.ShouldBe(4, "the four in flight, nothing after the token was cancelled");
            engines.Calls.ShouldAllBe(c => !c.Function);
            (await _cache.GetListAsync()).ShouldBeEmpty("no distance without its toll");
        }
    }

    [Fact]
    public async Task An_already_cancelled_token_makes_no_call_at_all()
    {
        var stops = FakeRoutingEngines.Stops(6);
        var engines = EnginesFor(stops);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        using (_currentTenant.Change(TenantA))
        {
            await Should.ThrowAsync<OperationCanceledException>(() => _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything, cancellation.Token));
            (await _cache.GetListAsync()).ShouldBeEmpty();
        }

        engines.Calls.ShouldBeEmpty();
        _handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Tenant_B_does_not_get_tenant_As_cached_rows_from_the_matrix_and_does_not_touch_them()
    {
        var stops = FakeRoutingEngines.Stops(5);
        var engines = EnginesFor(stops);

        using (_currentTenant.Change(TenantA))
        {
            (await _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything)).ShouldNotBeNull();
        }

        engines.Calls.Clear();
        engines.Offset = 7;
        using (_currentTenant.Change(TenantB))
        {
            var matrix = await _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything);

            matrix.ShouldNotBeNull();
            for (var pair = 0; pair < 4; pair++)
            {
                FakeRoutingEngines.ShouldBeLane(matrix.Legs[pair], pair, fromCache: false, offset: 7);
            }

            engines.Calls.Count.ShouldBe(8, "same lanes, same vehicle, other tenant: everything is asked");
            var rows = await _cache.GetListAsync();
            rows.Count.ShouldBe(4);
            rows.ShouldAllBe(r => r.TenantId == TenantB);
        }

        engines.Calls.Clear();
        engines.Offset = 0;
        using (_currentTenant.Change(TenantA))
        {
            var matrix = await _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything);

            matrix.ShouldNotBeNull();
            for (var pair = 0; pair < 4; pair++)
            {
                FakeRoutingEngines.ShouldBeLane(matrix.Legs[pair], pair, fromCache: true);
            }

            engines.Calls.ShouldBeEmpty();
            var rows = await _cache.GetListAsync();
            rows.Count.ShouldBe(4, "B's write did not refresh or add to A's rows");
            rows.ShouldAllBe(r => r.TenantId == TenantA);
        }

        using (_currentTenant.Change(null))
        {
            var matrix = await _routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything);
            matrix.ShouldNotBeNull().Legs.ShouldAllBe(l => !l.FromCache, "the host is not a way into tenant rows either");
        }
    }

    [Fact]
    public async Task Options_that_ask_nothing_extra_take_the_plain_matrix_with_its_one_table_call()
    {
        using (_currentTenant.Change(TenantA))
        {
            var matrix = await _routing.GetMatrixAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam, RoutingHarness.Utrecht], VehicleSpec.Default, RoutingOptions.DistanceOnly);

            matrix.ShouldNotBeNull().Legs.Count.ShouldBe(2);
            _handler.Requests.ShouldHaveSingleItem().PathAndQuery.ShouldStartWith("/table/v1/driving/");
        }
    }

    [Fact]
    public async Task One_stop_or_none_is_an_empty_matrix_without_a_call()
    {
        using (_currentTenant.Change(TenantA))
        {
            (await _routing.GetMatrixAsync([RoutingHarness.Amsterdam], VehicleSpec.Default, Everything)).ShouldNotBeNull().Legs.ShouldBeEmpty();
            (await _routing.GetMatrixAsync([], VehicleSpec.Default, Everything)).ShouldNotBeNull().Legs.ShouldBeEmpty();
            _handler.Requests.ShouldBeEmpty();
            (await _cache.GetListAsync()).ShouldBeEmpty();
        }
    }
}

/// <summary>
/// The other half of design 74 §9: per matrix the cache is read once and written once, not once per pair. Counted at the
/// database command, so it holds whatever the service does on the way there.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class RoutingMatrixCacheCommandTests : OpenTmsTestBase<RoutingCommandCountingTestModule>
{
    private static readonly Guid TenantA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");

    private static readonly RoutingOptions Everything = new(IncludeGeometry: true, IncludeToll: true, IncludeCo2: true);

    [Fact]
    public async Task Missing_pairs_cost_one_cache_read_and_one_lookup_before_the_store_and_a_full_hit_one_read()
    {
        var routing = GetRequiredService<IRoutingService>();
        var handler = GetRequiredService<RoutingHttpHandler>();
        var commands = GetRequiredService<RouteCacheCommandCounter>();
        var currentTenant = GetRequiredService<ICurrentTenant>();
        var stops = FakeRoutingEngines.Stops(11);
        handler.Reset();
        handler.Fallback = new FakeRoutingEngines(stops).AnswerAsync;

        using (currentTenant.Change(TenantA))
        {
            commands.Reset();
            (await routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything)).ShouldNotBeNull();

            commands.Selects.ShouldBe(2, "the hits, then the store's lookup of existing rows; it was two per pair");

            commands.Reset();
            (await routing.GetMatrixAsync(stops, VehicleSpec.Default, Everything)).ShouldNotBeNull().Legs.ShouldAllBe(l => l.FromCache);

            commands.Selects.ShouldBe(1);
            commands.Writes.ShouldBe(0, "nothing to store on a full hit");
        }
    }
}

/// <summary>The routing test application with the commands against the route cache table counted.</summary>
[DependsOn(typeof(RoutingTestModule))]
public class RoutingCommandCountingTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var counter = new RouteCacheCommandCounter();
        context.Services.AddSingleton(counter);
        Configure<AbpDbContextOptions>(options => options.PreConfigure(c => c.DbContextOptions.AddInterceptors(counter)));
    }
}

public sealed class RouteCacheCommandCounter : DbCommandInterceptor
{
    private int _selects;
    private int _writes;

    public int Selects => Volatile.Read(ref _selects);

    public int Writes => Volatile.Read(ref _writes);

    public void Reset()
    {
        Volatile.Write(ref _selects, 0);
        Volatile.Write(ref _writes, 0);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Count(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Count(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Count(DbCommand command)
    {
        if (!command.CommandText.Contains("RouteCache", StringComparison.Ordinal))
        {
            return;
        }

        if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _selects);
        }
        else
        {
            Interlocked.Increment(ref _writes);
        }
    }
}

/// <summary>
/// Both engines of one trip, safe for concurrent calls: every distinct lane answers its own distance, duration, toll, CO₂
/// and polyline, and the number of calls in flight is measured. A set <see cref="Failure"/> answers instead of the lane's
/// values; <see cref="BeforeAnswer"/> holds a call back. The function reports a deliberately different route distance:
/// the distance on a leg must be the engine's.
/// </summary>
internal sealed class FakeRoutingEngines
{
    private readonly Dictionary<string, int> _lanes = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _inFlight;
    private int _peak;

    public FakeRoutingEngines(IReadOnlyList<GeoPoint> stops)
    {
        for (var i = 0; i + 1 < stops.Count; i++)
        {
            _lanes.TryAdd(OsrmRoutingProvider.Coordinates([stops[i], stops[i + 1]]), _lanes.Count);
        }
    }

    public ConcurrentQueue<(int Lane, bool Function)> Calls { get; } = new();

    public Func<int, bool, Task>? BeforeAnswer { get; set; }

    public Func<int, bool, HttpResponseMessage?>? Failure { get; set; }

    /// <summary>Added to every value, so two tenants asking the same lane get answers that can be told apart.</summary>
    public int Offset { get; set; }

    public int InFlight
    {
        get
        {
            lock (_gate)
            {
                return _inFlight;
            }
        }
    }

    public int Peak
    {
        get
        {
            lock (_gate)
            {
                return _peak;
            }
        }
    }

    public static List<GeoPoint> Stops(int count)
        => Enumerable.Range(0, count).Select(i => new GeoPoint(52.0 + (i * 0.01), 5.0)).ToList();

    public static int DistanceOf(int lane, int offset = 0) => 10000 + (lane * 100) + offset;

    public static int DurationOf(int lane, int offset = 0) => 500 + lane + offset;

    public static int TollDistanceOf(int lane, int offset = 0) => 2000 + lane + offset;

    public static decimal TollAmountOf(int lane, int offset = 0) => 1.25m + lane + offset;

    public static decimal Co2Of(int lane, int offset = 0) => 10.5m + lane + offset;

    public static string PolylineOf(int lane, int offset = 0) => "engine-" + (lane + offset);

    public static void ShouldBeLane(RouteResult leg, int lane, bool fromCache, int offset = 0)
    {
        leg.DistanceM.ShouldBe(DistanceOf(lane, offset));
        leg.DurationS.ShouldBe(DurationOf(lane, offset));
        leg.TollDistanceM.ShouldBe(TollDistanceOf(lane, offset));
        leg.TollAmount.ShouldBe(TollAmountOf(lane, offset));
        leg.TollCurrencyCode.ShouldBe("EUR");
        leg.Co2Kg.ShouldBe(Co2Of(lane, offset));
        leg.EncodedPolyline.ShouldBe(PolylineOf(lane, offset));
        leg.Provider.ShouldBe("Osrm");
        leg.FromCache.ShouldBe(fromCache);
    }

    /// <summary>Waits for a signal, but not past a provider's two-second timeout: code that never overlaps fails on the assertion.</summary>
    public static async Task Patiently(Task signal)
    {
        try
        {
            await signal.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch (TimeoutException)
        {
        }
    }

    public async Task<HttpResponseMessage> AnswerAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var function = request.Method == HttpMethod.Post;
        var lane = function
            ? LaneOfBody(await request.Content!.ReadAsStringAsync(cancellationToken))
            : LaneOfPath(request.RequestUri!.AbsolutePath);

        lock (_gate)
        {
            _inFlight++;
            _peak = Math.Max(_peak, _inFlight);
        }

        try
        {
            Calls.Enqueue((lane, function));
            if (BeforeAnswer != null)
            {
                await BeforeAnswer(lane, function);
            }

            return Failure?.Invoke(lane, function) ?? RoutingHttpHandler.Json(function ? FunctionBody(lane) : EngineBody(lane));
        }
        finally
        {
            lock (_gate)
            {
                _inFlight--;
            }
        }
    }

    private int LaneOfPath(string path)
        => _lanes[path[(path.LastIndexOf('/') + 1)..]];

    private int LaneOfBody(string body)
    {
        using var document = JsonDocument.Parse(body);
        return _lanes[OsrmRoutingProvider.Coordinates([Point(document.RootElement.GetProperty("origin")), Point(document.RootElement.GetProperty("destination"))])];
    }

    private static GeoPoint Point(JsonElement element)
        => new(element.GetProperty("lat").GetDouble(), element.GetProperty("lon").GetDouble());

    private string EngineBody(int lane)
    {
        var distance = DistanceOf(lane, Offset).ToString(CultureInfo.InvariantCulture);
        var duration = DurationOf(lane, Offset).ToString(CultureInfo.InvariantCulture);
        return $$"""{"code":"Ok","routes":[{"geometry":"{{PolylineOf(lane, Offset)}}","legs":[{"duration":{{duration}},"distance":{{distance}}}],"duration":{{duration}},"distance":{{distance}}}],"waypoints":[]}""";
    }

    private string FunctionBody(int lane)
    {
        var tollAmount = TollAmountOf(lane, Offset).ToString(CultureInfo.InvariantCulture);
        var tollDistance = TollDistanceOf(lane, Offset).ToString(CultureInfo.InvariantCulture);
        var co2 = Co2Of(lane, Offset).ToString(CultureInfo.InvariantCulture);
        return $$$"""{"route":{"distance":1,"duration":1,"polyline":"function"},"toll":{"totalCost":{{{tollAmount}}},"totalDistance":{{{tollDistance}}},"currency":"eur","segments":[]},"carbonFootprint":{"totalKgCO2e":{{{co2}}},"gCO2ePerTkm":1}}""";
    }
}
