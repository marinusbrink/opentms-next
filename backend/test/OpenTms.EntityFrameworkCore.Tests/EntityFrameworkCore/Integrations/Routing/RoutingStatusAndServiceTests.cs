using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;
using OpenTms.Integrations.Permissions;
using OpenTms.Integrations.Routing;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Integrations.Routing;

/// <summary>
/// The status endpoint, the health check and the routing service through the real container on SQLite (design 60,
/// test risk analysis: "Health check and status endpoint", Medium; the cache's orchestration here, its tenant isolation
/// and unique key on PostgreSQL). The probe is served from memory for sixty seconds; the status shows the function's host
/// only and counts the current tenant's rows; a route is asked once, served from the cache afterwards and asked again
/// once the clock passes its expiry; toll and CO₂ come from the function when asked; the matrix fills the pairs the cache
/// misses with one table call.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class RoutingStatusAndServiceTests : OpenTmsTestBase<RoutingTestModule>
{
    private static readonly Guid TenantA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");

    private readonly IRoutingAppService _status;
    private readonly IRoutingService _routing;
    private readonly RoutingHttpHandler _handler;
    private readonly ManualTimeProvider _time;
    private readonly ICurrentTenant _currentTenant;
    private readonly IRepository<RouteCacheEntry, Guid> _cache;
    private readonly RoutingProbeCache _probes;

    public RoutingStatusAndServiceTests()
    {
        _status = GetRequiredService<IRoutingAppService>();
        _routing = GetRequiredService<IRoutingService>();
        _handler = GetRequiredService<RoutingHttpHandler>();
        _time = GetRequiredService<ManualTimeProvider>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _cache = GetRequiredService<IRepository<RouteCacheEntry, Guid>>();
        _probes = GetRequiredService<RoutingProbeCache>();
        _handler.Reset();
        _probes.Clear();
        _time.Now = RoutingTestModule.Start;
    }

    private static readonly GeoPoint A = RoutingHarness.Amsterdam;
    private static readonly GeoPoint B = RoutingHarness.Rotterdam;
    private static readonly GeoPoint C = RoutingHarness.Utrecht;

    [Fact]
    public async Task The_status_names_the_provider_the_probe_and_the_functions_host_only()
    {
        using (_currentTenant.Change(TenantA))
        {
            var status = await _status.GetStatusAsync();

            status.Provider.ShouldBe("Osrm");
            status.Configured.ShouldBeTrue();
            status.Reachable.ShouldBeTrue();
            status.RouteFunctionHost.ShouldBe("route-function.invalid", "the host, never the path or a key");
            status.LastProbeAt.ShouldBe(RoutingTestModule.Start);
            status.CacheRows.ShouldBe(0);
        }

        var probe = _handler.Requests.ShouldHaveSingleItem();
        probe.Method.ShouldBe(HttpMethod.Get);
        probe.Uri.Host.ShouldBe("osrm.invalid");
        probe.PathAndQuery.ShouldStartWith("/nearest/v1/driving/");
    }

    [Fact]
    public async Task The_probe_is_served_from_memory_for_sixty_seconds()
    {
        await _status.GetStatusAsync();
        await _status.GetStatusAsync();
        _handler.Requests.Count.ShouldBe(1, "the second status call within a minute does not probe again");

        _time.Now = RoutingTestModule.Start.AddSeconds(61);
        var status = await _status.GetStatusAsync();
        _handler.Requests.Count.ShouldBe(2);
        status.LastProbeAt.ShouldBe(_time.Now);
    }

    [Fact]
    public async Task Cache_rows_are_counted_for_the_current_tenant_only()
    {
        await WriteRowAsync(TenantA, "qq|a|1");
        await WriteRowAsync(TenantA, "qq|a|2");
        await WriteRowAsync(TenantB, "qq|b|1");

        using (_currentTenant.Change(TenantA))
        {
            (await _status.GetStatusAsync()).CacheRows.ShouldBe(2);
        }

        using (_currentTenant.Change(TenantB))
        {
            (await _status.GetStatusAsync()).CacheRows.ShouldBe(1);
        }

        using (_currentTenant.Change(null))
        {
            (await _status.GetStatusAsync()).CacheRows.ShouldBe(0, "the host sees no tenant rows");
        }
    }

    [Fact]
    public async Task The_health_check_is_healthy_when_the_engine_answers_and_unhealthy_when_it_does_not()
    {
        var check = new RoutingHealthCheck(_routing);
        var context = new HealthCheckContext { Registration = new HealthCheckRegistration(RoutingHealthCheck.Name, check, HealthStatus.Unhealthy, [RoutingHealthCheck.Tag]) };

        var healthy = await check.CheckHealthAsync(context);
        healthy.Status.ShouldBe(HealthStatus.Healthy);
        healthy.Description!.ShouldContain("Osrm");

        _probes.Clear();
        _handler.AlwaysThrow(new HttpRequestException(HttpRequestError.ConnectionError));
        var unhealthy = await check.CheckHealthAsync(context);
        unhealthy.Status.ShouldBe(HealthStatus.Unhealthy);
        unhealthy.Description!.ShouldNotContain("invalid", Case.Insensitive, "no host name in the health text");
    }

    [Fact]
    public async Task A_route_is_asked_once_served_from_the_cache_and_asked_again_after_it_expires()
    {
        using (_currentTenant.Change(TenantA))
        {
            var first = await _routing.GetRouteAsync(A, B, VehicleSpec.Default, RoutingOptions.DistanceOnly);
            first.ShouldNotBeNull();
            first.DistanceM.ShouldBe(72485);
            first.DurationS.ShouldBe(3712);
            first.FromCache.ShouldBeFalse();
            first.Provider.ShouldBe("Osrm");
            first.At.ShouldBe(RoutingTestModule.Start);
            _handler.Requests.ShouldHaveSingleItem().PathAndQuery.ShouldStartWith("/route/v1/driving/");

            var second = await _routing.GetRouteAsync(A, B, VehicleSpec.Default, RoutingOptions.DistanceOnly);
            second.ShouldNotBeNull();
            second.FromCache.ShouldBeTrue();
            second.DistanceM.ShouldBe(72485);
            _handler.Requests.Count.ShouldBe(1, "a hit never calls the engine");

            var row = (await _cache.GetListAsync()).ShouldHaveSingleItem();
            row.TenantId.ShouldBe(TenantA);
            row.Provider.ShouldBe("Osrm");
            row.Key.ShouldBe(RouteCacheKey.Build(A, B, VehicleSpec.Default, "Osrm", RoutingOptions.DistanceOnly));
            row.ExpiresAt.ShouldBe(RoutingTestModule.Start.UtcDateTime.AddDays(30), "Integrations:Routing:CacheDays default");
            row.Polyline.ShouldBeNull("geometry was not asked");

            _time.Now = RoutingTestModule.Start.AddDays(30).AddMinutes(1);
            var third = await _routing.GetRouteAsync(A, B, VehicleSpec.Default, RoutingOptions.DistanceOnly);
            third.ShouldNotBeNull().FromCache.ShouldBeFalse();
            _handler.Requests.Count.ShouldBe(2, "an expired row is asked again");
            var refreshed = (await _cache.GetListAsync()).ShouldHaveSingleItem();
            refreshed.Id.ShouldBe(row.Id, "the expired row is refreshed in place: the key stays unique");
            refreshed.ExpiresAt.ShouldBe(_time.Now.UtcDateTime.AddDays(30));
        }
    }

    [Fact]
    public async Task Toll_co2_and_geometry_come_from_the_function_when_asked_and_live_in_their_own_row()
    {
        using (_currentTenant.Change(TenantA))
        {
            var options = new RoutingOptions(IncludeGeometry: true, IncludeToll: true, IncludeCo2: true);
            var route = await _routing.GetRouteAsync(A, B, null, options);

            route.ShouldNotBeNull();
            route.DistanceM.ShouldBe(72485, "distance from OSRM, the configured provider");
            route.TollDistanceM.ShouldBe(51231);
            route.TollAmount.ShouldBe(12.35m);
            route.TollCurrencyCode.ShouldBe("EUR");
            route.Co2Kg.ShouldBe(58.123m);
            route.EncodedPolyline.ShouldBe("_p~iF~ps|U_ulLnnqC_mqNvxq`@");
            _handler.Requests.Count.ShouldBe(2);
            _handler.Requests[0].PathAndQuery.ShouldContain("overview=full");
            _handler.Requests[1].Method.ShouldBe(HttpMethod.Post);
            _handler.Requests[1].Json.GetProperty("vehicle").GetProperty("weight").GetInt32().ShouldBe(40_000, "the default truck when no vehicle is known");

            var distanceOnly = await _routing.GetRouteAsync(A, B, null, RoutingOptions.DistanceOnly);
            distanceOnly.ShouldNotBeNull().FromCache.ShouldBeFalse("a different question is a different row");
            distanceOnly.TollAmount.ShouldBeNull();
            (await _cache.GetListAsync()).Count.ShouldBe(2);

            var again = await _routing.GetRouteAsync(A, B, null, options);
            again.ShouldNotBeNull().FromCache.ShouldBeTrue();
            again.Co2Kg.ShouldBe(58.123m);
            again.EncodedPolyline.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task The_matrix_fills_the_pairs_the_cache_misses_with_one_table_call_and_caches_each_pair()
    {
        using (_currentTenant.Change(TenantA))
        {
            var matrix = await _routing.GetMatrixAsync([A, B, C], VehicleSpec.Default);

            matrix.ShouldNotBeNull();
            matrix.Legs.Count.ShouldBe(2);
            matrix.Legs[0].DistanceM.ShouldBe(72485);
            matrix.Legs[1].DistanceM.ShouldBe(58900);
            matrix.Legs[1].DurationS.ShouldBe(3011);
            matrix.TotalDistanceM.ShouldBe(131385);
            matrix.Legs.ShouldAllBe(l => !l.FromCache);
            _handler.Requests.ShouldHaveSingleItem().PathAndQuery.ShouldStartWith("/table/v1/driving/");
            (await _cache.GetListAsync()).Count.ShouldBe(2);

            var cached = await _routing.GetMatrixAsync([A, B, C], VehicleSpec.Default);
            cached.ShouldNotBeNull().Legs.ShouldAllBe(l => l.FromCache);
            _handler.Requests.Count.ShouldBe(1);

            /* One more stop: only the new pair is missing, so it is a single route call, not a table. */
            var extended = await _routing.GetMatrixAsync([A, B, C, A], VehicleSpec.Default);
            extended.ShouldNotBeNull().Legs.Count.ShouldBe(3);
            extended.Legs[2].FromCache.ShouldBeFalse();
            _handler.Requests.Count.ShouldBe(2);
            _handler.Requests[1].PathAndQuery.ShouldStartWith("/route/v1/driving/");

            (await _routing.GetMatrixAsync([A], null)).ShouldNotBeNull().Legs.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task A_matrix_with_an_unreachable_pair_is_null_and_nothing_is_cached()
    {
        _handler.Always(RoutingRecorded.OsrmTableUnreachable);

        using (_currentTenant.Change(TenantB))
        {
            (await _routing.GetMatrixAsync([A, B, C], null)).ShouldBeNull();
            (await _cache.GetListAsync()).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task The_Routing_permission_is_defined_on_both_sides_in_the_Integrations_group()
    {
        var manager = GetRequiredService<IPermissionDefinitionManager>();

        var permission = await manager.GetOrNullAsync(IntegrationsPermissions.Routing);
        permission.ShouldNotBeNull();
        permission.Name.ShouldBe("Integrations.Routing");
        permission.MultiTenancySide.ShouldBe(MultiTenancySides.Both);
        permission.Parent.ShouldBeNull();
        permission.DisplayName.ShouldNotBeNull();
        (await manager.GetGroupsAsync()).Single(g => g.Name == IntegrationsPermissions.GroupName).Permissions.Select(p => p.Name)
            .ShouldContain("Integrations.Routing");

        typeof(RoutingAppService).GetMethod(nameof(RoutingAppService.GetStatusAsync))!.GetCustomAttribute<AuthorizeAttribute>()!.Policy
            .ShouldBe(IntegrationsPermissions.Routing);
    }

    private async Task WriteRowAsync(Guid? tenantId, string key)
    {
        using (_currentTenant.Change(tenantId))
        {
            var now = RoutingTestModule.Start.UtcDateTime;
            await _cache.InsertAsync(new RouteCacheEntry(Guid.NewGuid(), tenantId, key, "Osrm", 1000, 60, null, null, null, null, null, now, now.AddDays(1)), autoSave: true);
        }
    }
}

/// <summary>
/// The unconfigured environment of design 60's rollout plan: no base URL means every routing call answers null, the
/// warning is written once per tenant per hour rather than per call, the status says "not configured" without a call,
/// and the health check is Degraded, not Unhealthy.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class RoutingUnconfiguredTests : OpenTmsTestBase<RoutingUnconfiguredTestModule>
{
    private static readonly Guid TenantA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");

    private readonly IRoutingService _routing;
    private readonly IRoutingAppService _status;
    private readonly RoutingHttpHandler _handler;
    private readonly CapturingLog _log;
    private readonly ManualTimeProvider _time;
    private readonly ICurrentTenant _currentTenant;

    public RoutingUnconfiguredTests()
    {
        _routing = GetRequiredService<IRoutingService>();
        _status = GetRequiredService<IRoutingAppService>();
        _handler = GetRequiredService<RoutingHttpHandler>();
        _log = GetRequiredService<CapturingLog>();
        _time = GetRequiredService<ManualTimeProvider>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        GetRequiredService<RoutingProbeCache>().Clear();
        _handler.Reset();
        _log.Entries.Clear();
    }

    [Fact]
    public async Task Every_routing_call_is_null_without_a_request_and_the_warning_is_written_once_per_tenant_per_hour()
    {
        using (_currentTenant.Change(TenantA))
        {
            (await _routing.GetRouteAsync(RoutingHarness.Amsterdam, RoutingHarness.Rotterdam, null, RoutingOptions.DistanceOnly)).ShouldBeNull();
            (await _routing.GetMatrixAsync([RoutingHarness.Amsterdam, RoutingHarness.Rotterdam], null)).ShouldBeNull();
            (await _routing.GetRouteAsync(RoutingHarness.Amsterdam, RoutingHarness.Utrecht, null, RoutingOptions.DistanceOnly)).ShouldBeNull();
        }

        _handler.Requests.ShouldBeEmpty();
        _log.Entries.Count(e => e.Message.Contains("Routing is not configured")).ShouldBe(1, "once, not per call");
        _log.Entries.ShouldAllBe(e => !e.Everything.Contains("52.37"), "no coordinates in the log");

        using (_currentTenant.Change(null))
        {
            (await _routing.GetRouteAsync(RoutingHarness.Amsterdam, RoutingHarness.Rotterdam, null, RoutingOptions.DistanceOnly)).ShouldBeNull();
        }

        _log.Entries.Count(e => e.Message.Contains("Routing is not configured")).ShouldBe(2, "the host is another tenant context");

        _time.Now = _time.Now.AddHours(1).AddSeconds(1);
        using (_currentTenant.Change(TenantA))
        {
            (await _routing.GetRouteAsync(RoutingHarness.Amsterdam, RoutingHarness.Rotterdam, null, RoutingOptions.DistanceOnly)).ShouldBeNull();
        }

        _log.Entries.Count(e => e.Message.Contains("Routing is not configured")).ShouldBe(3, "an hour later it is said again");
    }

    [Fact]
    public async Task The_status_and_the_health_check_say_not_configured_without_a_call()
    {
        var status = await _status.GetStatusAsync();
        status.Provider.ShouldBe("Osrm");
        status.Configured.ShouldBeFalse();
        status.Reachable.ShouldBeFalse();
        status.RouteFunctionHost.ShouldBeNull();
        status.LastProbeAt.ShouldBeNull();

        var check = new RoutingHealthCheck(_routing);
        var result = await check.CheckHealthAsync(new HealthCheckContext { Registration = new HealthCheckRegistration(RoutingHealthCheck.Name, check, HealthStatus.Unhealthy, [RoutingHealthCheck.Tag]) });
        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Description!.ShouldContain("not configured");
        _handler.Requests.ShouldBeEmpty();
    }
}
