using System;
using System.Threading.Tasks;
using OpenTms.Platform.Administration.Maintenance;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Maintenance;
using OpenTms.Platform.Maintenance.BackgroundJobs;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Maintenance;

/// <summary>
/// Integration tests for the Background jobs tab's application service over the fake monitor.
/// Risk class: Critical — host-only (Hangfire's storage has no tenant filter) and requeue/delete
/// change what runs; High — state parsing and paging decide what the grid shows.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class BackgroundJobMaintenanceAppServiceTests : OpenTmsEntityFrameworkCoreTestBase
{
    private readonly IBackgroundJobMaintenanceAppService _service;
    private readonly FakeBackgroundJobMonitor _monitor;
    private readonly ICurrentTenant _currentTenant;

    public BackgroundJobMaintenanceAppServiceTests()
    {
        _service = GetRequiredService<IBackgroundJobMaintenanceAppService>();
        _monitor = GetRequiredService<FakeBackgroundJobMonitor>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _monitor.IsAvailable = true;
        _monitor.Jobs.Clear();
        _monitor.KnownJobIds.Clear();
    }

    private static GridRequest Request(int start = 0, int end = 50) => new() { StartRow = start, EndRow = end };

    [Fact]
    public async Task Overview_maps_the_counts_servers_and_queues()
    {
        var overview = await _service.GetOverviewAsync();

        overview.IsAvailable.ShouldBeTrue();
        overview.Failed.ShouldBe(5);
        overview.Recurring.ShouldBe(7);
        overview.Servers.ShouldHaveSingleItem().Name.ShouldBe("server-1");
        overview.Servers[0].WorkerCount.ShouldBe(20);
        overview.Queues.ShouldHaveSingleItem().Name.ShouldBe("default");
    }

    [Fact]
    public async Task Without_job_storage_the_overview_says_so_and_the_list_is_empty()
    {
        _monitor.IsAvailable = false;

        (await _service.GetOverviewAsync()).IsAvailable.ShouldBeFalse();
        (await _service.GetListAsync(Request(), BackgroundJobStates.Failed)).Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_list_passes_state_and_paging_to_the_monitor_and_maps_the_rows()
    {
        var tenantId = Guid.NewGuid();
        var failedAt = new DateTime(2026, 10, 11, 8, 0, 0, DateTimeKind.Utc);
        _monitor.Jobs.Add(new BackgroundJobInfo("1", BackgroundJobState.Failed, "TenantProvisioningJob", tenantId, failedAt, null, "boom", "InvalidOperationException", "it broke", null, "default", null, null));
        _monitor.Jobs.Add(new BackgroundJobInfo("2", BackgroundJobState.Failed, "Other", null, failedAt, null, null, null, null, null, null, null, null));
        _monitor.Jobs.Add(new BackgroundJobInfo("3", BackgroundJobState.Succeeded, "Other", null, failedAt, null, null, null, null, null, null, "srv", 12));

        var page = await _service.GetListAsync(Request(1, 2), "failed");

        _monitor.LastQuery.ShouldBe((BackgroundJobState.Failed, 1, 1));
        page.TotalCount.ShouldBe(2);
        page.FilteredCount.ShouldBe(2);
        var row = page.Rows.ShouldHaveSingleItem();
        row.Id.ShouldBe("2");
        row.State.ShouldBe("Failed");

        var first = (await _service.GetListAsync(Request(), BackgroundJobStates.Failed)).Rows[0];
        first.JobName.ShouldBe("TenantProvisioningJob");
        first.TenantId.ShouldBe(tenantId);
        first.StateChangedAt.ShouldBe(failedAt);
        first.Reason.ShouldBe("boom");
        first.ExceptionType.ShouldBe("InvalidOperationException");
        first.Queue.ShouldBe("default");
    }

    [Fact]
    public async Task An_unknown_state_is_a_validation_error()
    {
        await Should.ThrowAsync<AbpValidationException>(() => _service.GetListAsync(Request(), "Sleeping"));
    }

    [Fact]
    public async Task Requeue_and_delete_reach_the_monitor_and_an_unknown_job_is_not_found()
    {
        _monitor.KnownJobIds.Add("known");

        await _service.RequeueAsync("known");
        await _service.DeleteAsync("known");
        _monitor.Requeued.ShouldContain("known");
        _monitor.Deleted.ShouldContain("known");

        await Should.ThrowAsync<EntityNotFoundException>(() => _service.RequeueAsync("unknown"));
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync("unknown"));
    }

    [Fact]
    public async Task A_tenant_can_neither_read_nor_manage_background_jobs()
    {
        _monitor.KnownJobIds.Add("known");

        using (_currentTenant.Change(Guid.NewGuid()))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetOverviewAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetListAsync(Request(), BackgroundJobStates.Failed));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.RequeueAsync("known"));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.DeleteAsync("known"));
        }

        _monitor.Requeued.ShouldNotContain("known");
    }
}
