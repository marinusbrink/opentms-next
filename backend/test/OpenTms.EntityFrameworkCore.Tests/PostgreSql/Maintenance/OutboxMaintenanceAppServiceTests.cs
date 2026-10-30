using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Platform.Administration.Maintenance;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Maintenance.Outbox;
using OpenTms.PostgreSql.Outbox;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.Maintenance;

/// <summary>
/// The Outbox tab against real PostgreSQL databases: the host sees every outbox, a tenant only its
/// own, and "dispatch now" publishes what is waiting.
/// Risk class: Critical — tenant isolation (a tenant never reads or dispatches another database's
/// outbox) and the dispatch is a write that publishes events.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class OutboxMaintenanceAppServiceTests : OpenTmsPostgreSqlTestBase
{
    private static readonly AbpUnitOfWorkOptions Transactional = new() { IsTransactional = true };

    private readonly IOutboxMaintenanceAppService _service;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDistributedEventBus _eventBus;
    private readonly ProbeRecorder _recorder;
    private readonly IOutboxDispatchRegistry _registry;

    public OutboxMaintenanceAppServiceTests()
    {
        _service = GetRequiredService<IOutboxMaintenanceAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _eventBus = GetRequiredService<IDistributedEventBus>();
        _recorder = GetRequiredService<ProbeRecorder>();
        _registry = GetRequiredService<IOutboxDispatchRegistry>();
    }

    private static GridRequest Request(int start = 0, int end = 50, string? search = null) => new()
    {
        StartRow = start,
        EndRow = end,
        WildcardSearch = search
    };

    [Fact]
    public async Task The_overview_reports_the_dispatcher_configuration()
    {
        var overview = await _service.GetOverviewAsync();

        overview.BatchSize.ShouldBeGreaterThan(0);
        overview.SweepPeriod.ShouldBeGreaterThan(TimeSpan.Zero);
        overview.InstanceName.ShouldBe(Environment.MachineName);
    }

    [Fact]
    public async Task The_host_lists_the_host_outbox_first_and_every_tenant_database_outbox()
    {
        var page = await _service.GetListAsync(Request());

        /* The test databases: the host (with the two shared-tier tenants: outbox-tenant-shared, transport-run-shared),
         * tenant A's own database and the committing tests' own database (transport-run-a; PostgreSqlTestDatabases). */
        page.TotalCount.ShouldBe(3);
        var host = page.Rows[0];
        host.IsHost.ShouldBeTrue();
        host.Key.ShouldBe("host");
        host.DatabaseName.ShouldBe(PostgreSqlTestDatabases.HostDatabase);
        host.SharedTenantCount.ShouldBe(2);
        host.IsReachable.ShouldBeTrue();
        host.PendingCount.ShouldNotBeNull();

        page.Rows.Skip(1).Select(r => r.TenantId).ShouldBe([PostgreSqlTestDatabases.TenantAId, PostgreSqlTestDatabases.TransportTenantAId], ignoreOrder: true);
        var tenant = page.Rows.Single(r => r.TenantId == PostgreSqlTestDatabases.TenantAId);
        tenant.TenantId.ShouldBe(PostgreSqlTestDatabases.TenantAId);
        tenant.DatabaseName.ShouldBe(PostgreSqlTestDatabases.TenantADatabase);
        tenant.IsSharedTier.ShouldBeFalse();
        tenant.IsReachable.ShouldBeTrue();
        tenant.PendingCount.ShouldNotBeNull();

        (await _service.GetListAsync(Request(search: "tenant-a"))).Rows.ShouldHaveSingleItem().TenantName.ShouldBe("outbox-tenant-a");
    }

    [Fact]
    public async Task A_waiting_event_is_counted_and_dispatch_now_publishes_it()
    {
        var probeId = Guid.NewGuid();
        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            await WithUnitOfWorkAsync(Transactional, () => _eventBus.PublishAsync(new OutboxProbeEto { ProbeId = probeId }));
        }

        var before = (await _service.GetListAsync(Request())).Rows.Single(r => r.TenantId == PostgreSqlTestDatabases.TenantAId);
        before.PendingCount.ShouldNotBeNull();
        before.PendingCount.Value.ShouldBeGreaterThanOrEqualTo(1);
        before.OldestPendingAt.ShouldNotBeNull();

        var result = await _service.DispatchAsync(PostgreSqlTestDatabases.TenantAId);
        result.TenantId.ShouldBe(PostgreSqlTestDatabases.TenantAId);
        result.PublishedCount.ShouldBeGreaterThanOrEqualTo(1);
        _recorder.For(probeId).ShouldHaveSingleItem().TenantId.ShouldBe(PostgreSqlTestDatabases.TenantAId);

        var after = (await _service.GetListAsync(Request())).Rows.Single(r => r.TenantId == PostgreSqlTestDatabases.TenantAId);
        after.PendingCount.ShouldBe(0);
        after.OldestPendingAt.ShouldBeNull();
        after.LastRunAt.ShouldNotBeNull();
        after.LastPublishedCount.ShouldBe(result.PublishedCount);
        _registry.Find(PostgreSqlTestDatabases.TenantAId).ShouldNotBeNull();
    }

    [Fact]
    public async Task Dispatching_the_host_outbox_and_an_unknown_tenant()
    {
        (await _service.DispatchAsync(null)).TenantId.ShouldBeNull();

        await Should.ThrowAsync<BusinessException>(() => _service.DispatchAsync(Guid.NewGuid()));
        await Should.ThrowAsync<BusinessException>(() => _service.DispatchAsync(PostgreSqlTestDatabases.SharedTenantId));
    }

    [Fact]
    public async Task A_tenant_with_its_own_database_sees_and_dispatches_only_its_own_outbox()
    {
        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            var page = await _service.GetListAsync(Request());

            page.TotalCount.ShouldBe(1);
            var own = page.Rows.ShouldHaveSingleItem();
            own.TenantId.ShouldBe(PostgreSqlTestDatabases.TenantAId);
            own.IsHost.ShouldBeFalse();
            own.IsSharedTier.ShouldBeFalse();
            own.IsReachable.ShouldBeTrue();
            own.PendingCount.ShouldNotBeNull();

            (await _service.DispatchAsync(PostgreSqlTestDatabases.TenantAId)).TenantId.ShouldBe(PostgreSqlTestDatabases.TenantAId);

            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.DispatchAsync(null));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.DispatchAsync(PostgreSqlTestDatabases.SharedTenantId));
        }
    }

    [Fact]
    public async Task A_shared_tier_tenant_sees_that_its_outbox_is_the_hosts_and_cannot_dispatch_it()
    {
        using (_currentTenant.Change(PostgreSqlTestDatabases.SharedTenantId))
        {
            var page = await _service.GetListAsync(Request());

            var own = page.Rows.ShouldHaveSingleItem();
            own.TenantId.ShouldBe(PostgreSqlTestDatabases.SharedTenantId);
            own.IsSharedTier.ShouldBeTrue();
            own.IsReachable.ShouldBeFalse();
            own.PendingCount.ShouldBeNull();
            own.DatabaseName.ShouldBeEmpty();

            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.DispatchAsync(PostgreSqlTestDatabases.SharedTenantId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.DispatchAsync(null));
        }
    }
}
