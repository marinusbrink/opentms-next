using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Platform.Events.Outbox;
using Shouldly;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.Outbox;

/// <summary>
/// The transactional outbox against real PostgreSQL databases (docs/designs/outbox-per-tenant.md).
/// Risk class: critical — tenant isolation (the event lands in the tenant's own database and its
/// handlers run in that tenant's context) and the fact-plus-event guarantee.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class OutboxPerTenantIntegrationTests : OpenTmsPostgreSqlTestBase
{
    private static readonly AbpUnitOfWorkOptions Transactional = new() { IsTransactional = true };

    private readonly IDistributedEventBus _eventBus;
    private readonly ICurrentTenant _currentTenant;
    private readonly ProbeRecorder _recorder;
    private readonly IOutboxSignal _signal;

    public OutboxPerTenantIntegrationTests()
    {
        _eventBus = GetRequiredService<IDistributedEventBus>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _recorder = GetRequiredService<ProbeRecorder>();
        _signal = GetRequiredService<IOutboxSignal>();
    }

    // ── Fact and event commit together, in the tenant's own database ──────────────

    [Fact]
    public async Task Publishing_inside_a_transaction_stores_the_event_in_the_tenants_own_database_only()
    {
        var probeId = Guid.NewGuid();

        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            await WithUnitOfWorkAsync(Transactional, () => _eventBus.PublishAsync(new OutboxProbeEto { ProbeId = probeId }));
        }

        (await CountAsync(PostgreSqlTestDatabases.TenantADatabase, probeId)).ShouldBe(1);
        (await CountAsync(PostgreSqlTestDatabases.HostDatabase, probeId)).ShouldBe(0);

        // Nothing was delivered in-process: the dispatcher owns delivery.
        _recorder.For(probeId).ShouldBeEmpty();

        await DispatchAsync(PostgreSqlTestDatabases.TenantAId); // leave the database clean
    }

    [Fact]
    public async Task A_unit_of_work_that_fails_after_publishing_leaves_no_event_behind()
    {
        var probeId = Guid.NewGuid();

        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            await Should.ThrowAsync<InvalidOperationException>(() => WithUnitOfWorkAsync(Transactional, async () =>
            {
                await _eventBus.PublishAsync(new OutboxProbeEto { ProbeId = probeId });
                throw new InvalidOperationException("the fact could not be written");
            }));
        }

        (await CountAsync(PostgreSqlTestDatabases.TenantADatabase, probeId)).ShouldBe(0);
        (await CountAsync(PostgreSqlTestDatabases.HostDatabase, probeId)).ShouldBe(0);
        _recorder.For(probeId).ShouldBeEmpty();
    }

    [Fact]
    public async Task Publishing_without_a_unit_of_work_throws()
    {
        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            await Should.ThrowAsync<OutboxRequiresTransactionException>(
                () => _eventBus.PublishAsync(new OutboxProbeEto { ProbeId = Guid.NewGuid() }));
        }
    }

    [Fact]
    public async Task Publishing_in_a_non_transactional_unit_of_work_throws()
    {
        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            await Should.ThrowAsync<OutboxRequiresTransactionException>(
                () => WithUnitOfWorkAsync(new AbpUnitOfWorkOptions { IsTransactional = false },
                    () => _eventBus.PublishAsync(new OutboxProbeEto { ProbeId = Guid.NewGuid() })));
        }
    }

    [Fact]
    public async Task Events_outside_the_OpenTms_namespace_bypass_the_outbox_and_are_delivered_directly()
    {
        var probeId = Guid.NewGuid();

        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            await WithUnitOfWorkAsync(Transactional,
                () => _eventBus.PublishAsync(new ThirdParty.Probe.ExternalProbeEto { ProbeId = probeId }));
        }

        _recorder.For(probeId).ShouldHaveSingleItem().TenantId.ShouldBe(PostgreSqlTestDatabases.TenantAId);
        (await CountAsync(PostgreSqlTestDatabases.TenantADatabase, probeId)).ShouldBe(0);
        (await CountAsync(PostgreSqlTestDatabases.HostDatabase, probeId)).ShouldBe(0);
    }

    // ── Dispatching ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dispatching_delivers_the_event_in_the_tenants_context_and_removes_it()
    {
        var probeId = Guid.NewGuid();
        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            await WithUnitOfWorkAsync(Transactional, () => _eventBus.PublishAsync(new OutboxProbeEto { ProbeId = probeId }));
        }

        (await DispatchAsync(PostgreSqlTestDatabases.TenantAId)).ShouldBeGreaterThanOrEqualTo(1);

        var delivery = _recorder.For(probeId).ShouldHaveSingleItem();
        delivery.TenantId.ShouldBe(PostgreSqlTestDatabases.TenantAId);
        (await CountAsync(PostgreSqlTestDatabases.TenantADatabase, probeId)).ShouldBe(0);

        // A second run finds nothing: no double delivery.
        (await DispatchAsync(PostgreSqlTestDatabases.TenantAId)).ShouldBe(0);
        _recorder.For(probeId).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Dispatching_one_tenant_never_touches_the_events_of_the_host_or_another_tenant()
    {
        var hostProbe = Guid.NewGuid();
        var tenantProbe = Guid.NewGuid();

        using (_currentTenant.Change(null))
        {
            await WithUnitOfWorkAsync(Transactional, () => _eventBus.PublishAsync(new OutboxProbeEto { ProbeId = hostProbe }));
        }
        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            await WithUnitOfWorkAsync(Transactional, () => _eventBus.PublishAsync(new OutboxProbeEto { ProbeId = tenantProbe }));
        }

        await DispatchAsync(PostgreSqlTestDatabases.TenantAId);

        _recorder.For(tenantProbe).ShouldHaveSingleItem().TenantId.ShouldBe(PostgreSqlTestDatabases.TenantAId);
        _recorder.For(hostProbe).ShouldBeEmpty();
        (await CountAsync(PostgreSqlTestDatabases.HostDatabase, hostProbe)).ShouldBe(1);

        await DispatchAsync(null);
        _recorder.For(hostProbe).ShouldHaveSingleItem().TenantId.ShouldBeNull();
        (await CountAsync(PostgreSqlTestDatabases.HostDatabase, hostProbe)).ShouldBe(0);
    }

    [Fact]
    public async Task A_shared_tier_tenants_event_lives_in_the_host_database_and_is_dispatched_in_that_tenants_context()
    {
        var probeId = Guid.NewGuid();
        using (_currentTenant.Change(PostgreSqlTestDatabases.SharedTenantId))
        {
            await WithUnitOfWorkAsync(Transactional, () => _eventBus.PublishAsync(new OutboxProbeEto { ProbeId = probeId }));
        }

        (await CountAsync(PostgreSqlTestDatabases.HostDatabase, probeId)).ShouldBe(1);
        (await CountAsync(PostgreSqlTestDatabases.TenantADatabase, probeId)).ShouldBe(0);

        await DispatchAsync(null);

        _recorder.For(probeId).ShouldHaveSingleItem().TenantId.ShouldBe(PostgreSqlTestDatabases.SharedTenantId);
        (await CountAsync(PostgreSqlTestDatabases.HostDatabase, probeId)).ShouldBe(0);
    }

    [Fact]
    public async Task The_row_follows_the_publishers_tenant_even_when_the_unit_of_work_completes_in_another_context()
    {
        /* A host-side job that does `using (CurrentTenant.Change(tenant)) { write fact; publish; }`
         * and completes its unit of work afterwards, outside the Change: the event must sit next to
         * the fact, in the tenant's database, stamped with the tenant — not in the host database. */
        var probeId = Guid.NewGuid();

        using (_currentTenant.Change(null))
        {
            await WithUnitOfWorkAsync(Transactional, async () =>
            {
                using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
                {
                    await _eventBus.PublishAsync(new OutboxProbeEto { ProbeId = probeId });
                }
            });
        }

        (await CountAsync(PostgreSqlTestDatabases.TenantADatabase, probeId)).ShouldBe(1);
        (await CountAsync(PostgreSqlTestDatabases.HostDatabase, probeId)).ShouldBe(0);

        await DispatchAsync(PostgreSqlTestDatabases.TenantAId);
        _recorder.For(probeId).ShouldHaveSingleItem().TenantId.ShouldBe(PostgreSqlTestDatabases.TenantAId);
    }

    [Fact]
    public async Task A_shared_tier_signal_visit_runs_each_host_database_row_under_its_own_tenant_and_leaves_other_databases_alone()
    {
        /* The production path after a shared-tier commit: the signal names the shared tenant, the
         * dispatcher visits the host database and finds host-level rows and the shared tenant's rows. */
        var hostProbe = Guid.NewGuid();
        var sharedProbe = Guid.NewGuid();
        var tenantAProbe = Guid.NewGuid();

        using (_currentTenant.Change(null))
        {
            await WithUnitOfWorkAsync(Transactional, () => _eventBus.PublishAsync(new OutboxProbeEto { ProbeId = hostProbe }));
        }
        using (_currentTenant.Change(PostgreSqlTestDatabases.SharedTenantId))
        {
            await WithUnitOfWorkAsync(Transactional, () => _eventBus.PublishAsync(new OutboxProbeEto { ProbeId = sharedProbe }));
        }
        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            await WithUnitOfWorkAsync(Transactional, () => _eventBus.PublishAsync(new OutboxProbeEto { ProbeId = tenantAProbe }));
        }

        await DispatchAsync(PostgreSqlTestDatabases.SharedTenantId);

        _recorder.For(hostProbe).ShouldHaveSingleItem().TenantId.ShouldBeNull();
        _recorder.For(sharedProbe).ShouldHaveSingleItem().TenantId.ShouldBe(PostgreSqlTestDatabases.SharedTenantId);
        _recorder.For(tenantAProbe).ShouldBeEmpty();
        (await CountAsync(PostgreSqlTestDatabases.TenantADatabase, tenantAProbe)).ShouldBe(1);

        await DispatchAsync(PostgreSqlTestDatabases.TenantAId);
        _recorder.For(tenantAProbe).ShouldHaveSingleItem().TenantId.ShouldBe(PostgreSqlTestDatabases.TenantAId);
    }

    [Fact]
    public async Task A_failing_handler_keeps_the_event_for_the_next_run()
    {
        var probeId = Guid.NewGuid();
        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            await WithUnitOfWorkAsync(Transactional, () => _eventBus.PublishAsync(new FailingProbeEto { ProbeId = probeId }));
        }

        _recorder.FailNextFailingProbe = true;
        await DispatchAsync(PostgreSqlTestDatabases.TenantAId);

        _recorder.For(probeId).ShouldBeEmpty();
        (await CountAsync(PostgreSqlTestDatabases.TenantADatabase, probeId)).ShouldBe(1);

        await DispatchAsync(PostgreSqlTestDatabases.TenantAId);

        _recorder.For(probeId).ShouldHaveSingleItem().TenantId.ShouldBe(PostgreSqlTestDatabases.TenantAId);
        (await CountAsync(PostgreSqlTestDatabases.TenantADatabase, probeId)).ShouldBe(0);
    }

    [Fact]
    public async Task The_commit_signals_the_dispatcher_with_the_tenant_that_committed()
    {
        while (_signal.TryRead(out _)) { }

        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            await WithUnitOfWorkAsync(Transactional, () => _eventBus.PublishAsync(new OutboxProbeEto { ProbeId = Guid.NewGuid() }));
        }

        _signal.TryRead(out var signalledTenant).ShouldBeTrue();
        signalledTenant.ShouldBe(PostgreSqlTestDatabases.TenantAId);

        await DispatchAsync(PostgreSqlTestDatabases.TenantAId);
    }

    [Fact]
    public async Task A_rolled_back_unit_of_work_does_not_signal()
    {
        while (_signal.TryRead(out _)) { }

        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            await Should.ThrowAsync<InvalidOperationException>(() => WithUnitOfWorkAsync(Transactional, async () =>
            {
                await _eventBus.PublishAsync(new OutboxProbeEto { ProbeId = Guid.NewGuid() });
                throw new InvalidOperationException("rolled back");
            }));
        }

        _signal.TryRead(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Dispatching_reads_in_batches_until_the_database_is_empty()
    {
        var batchSize = GetRequiredService<Microsoft.Extensions.Options.IOptions<OutboxOptions>>().Value.BatchSize;
        var total = batchSize * 2 + 3;
        var probeIds = Enumerable.Range(0, total).Select(_ => Guid.NewGuid()).ToList();

        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            await WithUnitOfWorkAsync(Transactional, async () =>
            {
                foreach (var probeId in probeIds)
                {
                    await _eventBus.PublishAsync(new OutboxProbeEto { ProbeId = probeId });
                }
            });
        }

        (await DispatchAsync(PostgreSqlTestDatabases.TenantAId)).ShouldBeGreaterThanOrEqualTo(total);

        probeIds.ShouldAllBe(id => _recorder.For(id).Count == 1);
        (await CountAsync(PostgreSqlTestDatabases.TenantADatabase, probeIds[0])).ShouldBe(0);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private Task<int> DispatchAsync(Guid? tenantId)
    {
        return GetRequiredService<IOutboxDispatcher>().DispatchAsync(tenantId);
    }

    private static Task<long> CountAsync(string database, Guid probeId)
    {
        return PostgreSqlTestDatabases.CountOutboxRowsAsync(database, probeId.ToString("D"));
    }
}
