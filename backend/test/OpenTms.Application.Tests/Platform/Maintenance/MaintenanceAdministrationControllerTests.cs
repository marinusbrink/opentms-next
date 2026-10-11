using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using OpenTms.Controllers.Platform;
using OpenTms.Platform.Administration.Maintenance;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Permissions;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.Maintenance;

/// <summary>
/// Controller-layer unit tests for MaintenanceAdministrationController (no ABP DI, no HTTP pipeline).
/// Risk class: Critical — the policy on every action decides who may clear caches, read every
/// tenant's database figures or requeue jobs; the query-string mapping decides what a page shows.
/// </summary>
public class MaintenanceAdministrationControllerTests
{
    private readonly FakeCacheService _caches = new();
    private readonly FakeDatabaseService _databases = new();
    private readonly FakeOutboxService _outbox = new();
    private readonly FakeHealthService _health = new();
    private readonly FakeBackgroundJobService _jobs = new();
    private readonly MaintenanceAdministrationController _controller;

    public MaintenanceAdministrationControllerTests()
    {
        _controller = new MaintenanceAdministrationController(_caches, _databases, _outbox, _health, _jobs);
    }

    [Theory]
    [InlineData(nameof(MaintenanceAdministrationController.GetCacheStatusAsync), PlatformPermissions.Administration.Maintenance.Caches)]
    [InlineData(nameof(MaintenanceAdministrationController.ClearCachesAsync), PlatformPermissions.Administration.Maintenance.CachesClear)]
    [InlineData(nameof(MaintenanceAdministrationController.GetDatabasesAsync), PlatformPermissions.Administration.Maintenance.Databases)]
    [InlineData(nameof(MaintenanceAdministrationController.GetDatabasesSummaryAsync), PlatformPermissions.Administration.Maintenance.Databases)]
    [InlineData(nameof(MaintenanceAdministrationController.GetDatabaseAsync), PlatformPermissions.Administration.Maintenance.Databases)]
    [InlineData(nameof(MaintenanceAdministrationController.GetOutboxOverviewAsync), PlatformPermissions.Administration.Maintenance.Outbox)]
    [InlineData(nameof(MaintenanceAdministrationController.GetOutboxesAsync), PlatformPermissions.Administration.Maintenance.Outbox)]
    [InlineData(nameof(MaintenanceAdministrationController.DispatchOutboxAsync), PlatformPermissions.Administration.Maintenance.OutboxDispatch)]
    [InlineData(nameof(MaintenanceAdministrationController.GetHealthAsync), PlatformPermissions.Administration.Maintenance.Health)]
    [InlineData(nameof(MaintenanceAdministrationController.GetBackgroundJobsOverviewAsync), PlatformPermissions.Administration.Maintenance.BackgroundJobs)]
    [InlineData(nameof(MaintenanceAdministrationController.GetBackgroundJobsAsync), PlatformPermissions.Administration.Maintenance.BackgroundJobs)]
    [InlineData(nameof(MaintenanceAdministrationController.RequeueBackgroundJobAsync), PlatformPermissions.Administration.Maintenance.BackgroundJobsManage)]
    [InlineData(nameof(MaintenanceAdministrationController.DeleteBackgroundJobAsync), PlatformPermissions.Administration.Maintenance.BackgroundJobsManage)]
    public void Every_action_requires_its_maintenance_permission(string action, string expectedPolicy)
    {
        var authorize = typeof(MaintenanceAdministrationController)
            .GetMethod(action, BindingFlags.Public | BindingFlags.Instance)!
            .GetCustomAttributes<AuthorizeAttribute>()
            .ToList();

        authorize.ShouldHaveSingleItem().Policy.ShouldBe(expectedPolicy);
    }

    [Fact]
    public void No_public_action_is_left_without_a_policy()
    {
        var unguarded = typeof(MaintenanceAdministrationController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .Where(m => !m.GetCustomAttributes<AuthorizeAttribute>().Any())
            .Select(m => m.Name)
            .ToList();

        unguarded.ShouldBeEmpty();
    }

    [Fact]
    public async Task Databases_list_maps_the_query_string_to_the_grid_request()
    {
        var result = await _controller.GetDatabasesAsync(startRow: 10, endRow: 60, wildcardSearch: "demo", sortBy: "sizeBytes", sortDir: "desc");

        result.Result.ShouldBeOfType<OkObjectResult>();
        var request = _databases.LastRequest!;
        request.StartRow.ShouldBe(10);
        request.EndRow.ShouldBe(60);
        request.WildcardSearch.ShouldBe("demo");
        request.SortModels.ShouldHaveSingleItem().ShouldBe(new SortModel("sizeBytes", "desc"));
    }

    [Fact]
    public async Task Outbox_list_maps_the_query_string_and_dispatch_passes_the_tenant()
    {
        var tenantId = Guid.NewGuid();

        var list = await _controller.GetOutboxesAsync(startRow: 0, endRow: 25, wildcardSearch: null, sortBy: "tenantName", sortDir: null);
        list.Result.ShouldBeOfType<OkObjectResult>();
        _outbox.LastRequest!.SortModels.ShouldHaveSingleItem().ShouldBe(new SortModel("tenantName", "asc"));

        var dispatch = await _controller.DispatchOutboxAsync(tenantId);
        dispatch.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<OutboxDispatchResultDto>().TenantId.ShouldBe(tenantId);
        _outbox.LastDispatchedTenant.ShouldBe(tenantId);

        await _controller.DispatchOutboxAsync();
        _outbox.LastDispatchedTenant.ShouldBeNull();
    }

    [Fact]
    public async Task Background_jobs_list_passes_state_and_paging_and_defaults_to_failed()
    {
        await _controller.GetBackgroundJobsAsync(state: BackgroundJobStates.Succeeded, startRow: 50, endRow: 100);
        _jobs.LastState.ShouldBe(BackgroundJobStates.Succeeded);
        _jobs.LastRequest!.StartRow.ShouldBe(50);
        _jobs.LastRequest.EndRow.ShouldBe(100);

        await _controller.GetBackgroundJobsAsync();
        _jobs.LastState.ShouldBe(BackgroundJobStates.Failed);
    }

    [Fact]
    public async Task Job_actions_return_no_content_and_detail_and_status_actions_return_the_service_result()
    {
        (await _controller.RequeueBackgroundJobAsync("job-1")).ShouldBeOfType<NoContentResult>();
        _jobs.Requeued.ShouldBe("job-1");

        (await _controller.DeleteBackgroundJobAsync("job-2")).ShouldBeOfType<NoContentResult>();
        _jobs.Deleted.ShouldBe("job-2");

        (await _controller.GetDatabaseAsync("host")).Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<DatabaseDetailDto>().Key.ShouldBe("host");
        (await _controller.GetCacheStatusAsync()).Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<CacheStatusDto>().ProviderName.ShouldBe("InMemory");
        (await _controller.ClearCachesAsync()).Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<CacheClearResultDto>().ClearedEntryCount.ShouldBe(3);
        (await _controller.GetHealthAsync()).Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<HealthReportDto>().Status.ShouldBe("Healthy");
        (await _controller.GetBackgroundJobsOverviewAsync()).Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<BackgroundJobsOverviewDto>().IsAvailable.ShouldBeTrue();
        (await _controller.GetDatabasesSummaryAsync()).Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<DatabasesSummaryDto>().DatabaseCount.ShouldBe(2);
        (await _controller.GetOutboxOverviewAsync()).Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<OutboxOverviewDto>().BatchSize.ShouldBe(100);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeCacheService : ICacheMaintenanceAppService
    {
        public Task<CacheStatusDto> GetStatusAsync() => Task.FromResult(new CacheStatusDto { ProviderName = "InMemory" });
        public Task<CacheClearResultDto> ClearAsync() => Task.FromResult(new CacheClearResultDto { ClearedEntryCount = 3 });
    }

    private sealed class FakeDatabaseService : IDatabaseMaintenanceAppService
    {
        public GridRequest? LastRequest { get; private set; }

        public Task<GridResponse<DatabaseRowDto>> GetListAsync(GridRequest request)
        {
            LastRequest = request;
            return Task.FromResult(new GridResponse<DatabaseRowDto>());
        }

        public Task<DatabasesSummaryDto> GetSummaryAsync() => Task.FromResult(new DatabasesSummaryDto { DatabaseCount = 2 });
        public Task<DatabaseDetailDto> GetAsync(string key) => Task.FromResult(new DatabaseDetailDto { Key = key });
    }

    private sealed class FakeOutboxService : IOutboxMaintenanceAppService
    {
        public GridRequest? LastRequest { get; private set; }
        public Guid? LastDispatchedTenant { get; private set; }

        public Task<OutboxOverviewDto> GetOverviewAsync() => Task.FromResult(new OutboxOverviewDto { BatchSize = 100 });

        public Task<GridResponse<OutboxRowDto>> GetListAsync(GridRequest request)
        {
            LastRequest = request;
            return Task.FromResult(new GridResponse<OutboxRowDto>());
        }

        public Task<OutboxDispatchResultDto> DispatchAsync(Guid? tenantId)
        {
            LastDispatchedTenant = tenantId;
            return Task.FromResult(new OutboxDispatchResultDto { TenantId = tenantId, PublishedCount = 1 });
        }
    }

    private sealed class FakeHealthService : IHealthMaintenanceAppService
    {
        public Task<HealthReportDto> GetReportAsync() => Task.FromResult(new HealthReportDto { Status = "Healthy" });
    }

    private sealed class FakeBackgroundJobService : IBackgroundJobMaintenanceAppService
    {
        public GridRequest? LastRequest { get; private set; }
        public string? LastState { get; private set; }
        public string? Requeued { get; private set; }
        public string? Deleted { get; private set; }

        public Task<BackgroundJobsOverviewDto> GetOverviewAsync() => Task.FromResult(new BackgroundJobsOverviewDto { IsAvailable = true });

        public Task<GridResponse<BackgroundJobRowDto>> GetListAsync(GridRequest request, string state)
        {
            LastRequest = request;
            LastState = state;
            return Task.FromResult(new GridResponse<BackgroundJobRowDto>());
        }

        public Task RequeueAsync(string jobId)
        {
            Requeued = jobId;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string jobId)
        {
            Deleted = jobId;
            return Task.CompletedTask;
        }
    }
}
