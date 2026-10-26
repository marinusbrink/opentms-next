using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.DerivedValues;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.Events.Outbox;
using OpenTms.Platform.NumberSequences;
using OpenTms.TestDoubles;
using Shouldly;
using Volo.Abp;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.DerivedValues;

/// <summary>
/// Design 49 against real PostgreSQL: a request commits or rolls back with its save, the job writes the result only for
/// the version still requested (a save during the calculation discards it), failures are recorded and retried, and
/// everything stays in the requesting tenant's own database. Risk class: Critical.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class DerivedValuesPostgreSqlTests : OpenTmsPostgreSqlTestBase
{
    private static readonly AbpUnitOfWorkOptions Transactional = new() { IsTransactional = true };

    private readonly IDerivedValueScheduler _scheduler;
    private readonly IDerivedValueStateStore _store;
    private readonly DerivedValueCalculationJob _job;
    private readonly DerivedValuesRequestedHandler _handler;
    private readonly INumberSequenceGenerator _sequences;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly ProbeCalculatorControl _control;
    private readonly CapturingRecordUpdateTransport _pushes;
    private readonly IOutboxDispatcher _dispatcher;

    public DerivedValuesPostgreSqlTests()
    {
        _scheduler = GetRequiredService<IDerivedValueScheduler>();
        _store = GetRequiredService<IDerivedValueStateStore>();
        _job = GetRequiredService<DerivedValueCalculationJob>();
        _handler = GetRequiredService<DerivedValuesRequestedHandler>();
        _sequences = GetRequiredService<INumberSequenceGenerator>();
        _unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _control = GetRequiredService<ProbeCalculatorControl>();
        _pushes = GetRequiredService<CapturingRecordUpdateTransport>();
        _dispatcher = GetRequiredService<IOutboxDispatcher>();
    }

    private static Guid TenantA => PostgreSqlTestDatabases.TenantAId;

    private async Task<long> RequestAsync(Guid targetId, bool commit = true)
    {
        using var uow = _unitOfWorkManager.Begin(Transactional, requiresNew: true);
        var version = await _scheduler.RequestAsync(DerivedValueProbe.TargetType, targetId, "Test");
        if (commit)
        {
            await uow.CompleteAsync();
        }

        return version;
    }

    private async Task<DerivedValueState?> StateAsync(Guid targetId)
    {
        using var uow = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(), requiresNew: true);
        var state = await _store.FindAsync(DerivedValueProbe.TargetType, targetId);
        await uow.CompleteAsync();
        return state;
    }

    /// <summary>The probe's trace: how many calculations of the record committed.</summary>
    private async Task<long> CommittedCalculationsAsync(Guid targetId)
    {
        using var uow = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(), requiresNew: true);
        var next = await _sequences.PeekNextValueAsync(DerivedValueProbe.SequenceKey(targetId), DerivedValueProbe.Format);
        await uow.CompleteAsync();
        return next - 1;
    }

    /* The job, the dispatcher and the request handler run as in production: without a current user. */
    private async Task RunJobAsync(Guid targetId, long version, Guid? tenantId = null)
    {
        using (AsBackgroundProcess())
        {
            await _job.ExecuteAsync(new DerivedValueCalculationArgs { TenantId = tenantId ?? TenantA, TargetType = DerivedValueProbe.TargetType, TargetId = targetId, Version = version });
        }
    }

    private async Task<int> DispatchAsync(Guid tenantId)
    {
        using (AsBackgroundProcess())
        {
            return await _dispatcher.DispatchAsync(tenantId);
        }
    }

    [Fact]
    public async Task A_request_commits_its_version_and_its_event_with_the_save_in_the_tenants_own_database()
    {
        var targetId = Guid.NewGuid();
        using (_currentTenant.Change(TenantA))
        {
            (await RequestAsync(targetId)).ShouldBe(1);
            (await RequestAsync(targetId)).ShouldBe(2);

            var state = (await StateAsync(targetId)).ShouldNotBeNull();
            state.RequestedVersion.ShouldBe(2);
            state.CalculatedVersion.ShouldBe(0);
            state.Status.ShouldBe(DerivedValueStatus.Pending);
            state.TenantId.ShouldBe(TenantA);
        }

        (await PostgreSqlTestDatabases.CountOutboxRowsAsync(PostgreSqlTestDatabases.TenantADatabase, targetId.ToString())).ShouldBe(2);
        (await PostgreSqlTestDatabases.CountOutboxRowsAsync(PostgreSqlTestDatabases.HostDatabase, targetId.ToString())).ShouldBe(0);

        using (_currentTenant.Change(PostgreSqlTestDatabases.SharedTenantId))
        {
            (await StateAsync(targetId)).ShouldBeNull("another tenant never sees the state");
        }

        await DispatchAsync(TenantA);
    }

    [Fact]
    public async Task A_rolled_back_save_requests_nothing()
    {
        var targetId = Guid.NewGuid();
        using (_currentTenant.Change(TenantA))
        {
            await RequestAsync(targetId, commit: false);
            (await StateAsync(targetId)).ShouldBeNull();
        }

        (await PostgreSqlTestDatabases.CountOutboxRowsAsync(PostgreSqlTestDatabases.TenantADatabase, targetId.ToString())).ShouldBe(0);
    }

    [Fact]
    public async Task Requesting_outside_a_transaction_or_for_an_unknown_type_is_refused()
    {
        using (_currentTenant.Change(TenantA))
        {
            using (_unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: false), requiresNew: true))
            {
                (await Should.ThrowAsync<BusinessException>(() => _scheduler.RequestAsync(DerivedValueProbe.TargetType, Guid.NewGuid())))
                    .Code.ShouldBe(DerivedValueErrorCodes.RequiresTransaction);
            }

            using (_unitOfWorkManager.Begin(Transactional, requiresNew: true))
            {
                (await Should.ThrowAsync<BusinessException>(() => _scheduler.RequestAsync("Test.Unknown", Guid.NewGuid())))
                    .Code.ShouldBe(DerivedValueErrorCodes.UnknownTargetType);
            }
        }
    }

    [Fact]
    public async Task The_job_stores_the_result_publishes_recalculated_and_pushes_up_to_date()
    {
        var targetId = Guid.NewGuid();
        using (_currentTenant.Change(TenantA))
        {
            var version = await RequestAsync(targetId);
            await RunJobAsync(targetId, version);

            var state = (await StateAsync(targetId)).ShouldNotBeNull();
            state.CalculatedVersion.ShouldBe(version);
            state.Status.ShouldBe(DerivedValueStatus.UpToDate);
            state.CalculatedAt.ShouldNotBeNull();
            (await CommittedCalculationsAsync(targetId)).ShouldBe(1);

            /* A duplicate job (at-least-once) is one effect. */
            await RunJobAsync(targetId, version);
            (await CommittedCalculationsAsync(targetId)).ShouldBe(1);
        }

        var push = _pushes.For(targetId).ShouldHaveSingleItem();
        push.TenantId.ShouldBe(TenantA);
        push.Version.ShouldBe(1);
        push.Status.ShouldBe(DerivedValueStatus.UpToDate);
        push.Group.ShouldBe($"{TenantA:N}:{DerivedValueProbe.TargetType}:{targetId:N}");

        /* Request and recalculated events, both in tenant A's database. */
        (await PostgreSqlTestDatabases.CountOutboxRowsAsync(PostgreSqlTestDatabases.TenantADatabase, targetId.ToString())).ShouldBe(2);
        await DispatchAsync(TenantA);
    }

    [Fact]
    public async Task A_save_during_the_calculation_discards_the_result_and_the_next_job_writes()
    {
        var targetId = Guid.NewGuid();
        using (_currentTenant.Change(TenantA))
        {
            var first = await RequestAsync(targetId);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _control.Gates[targetId] = gate;
            _control.Started[targetId] = started;

            var running = Task.Run(async () =>
            {
                using (_currentTenant.Change(TenantA))
                {
                    await RunJobAsync(targetId, first);
                }
            });

            await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var second = await RequestAsync(targetId);
            gate.SetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(30));

            var discarded = (await StateAsync(targetId)).ShouldNotBeNull();
            discarded.RequestedVersion.ShouldBe(second);
            discarded.CalculatedVersion.ShouldBe(0, "the superseded result is not kept");
            discarded.Status.ShouldBe(DerivedValueStatus.Pending);
            (await CommittedCalculationsAsync(targetId)).ShouldBe(0, "the calculator's writes were rolled back");
            _pushes.For(targetId).ShouldBeEmpty();

            await RunJobAsync(targetId, second);
            var done = (await StateAsync(targetId)).ShouldNotBeNull();
            done.CalculatedVersion.ShouldBe(second);
            done.Status.ShouldBe(DerivedValueStatus.UpToDate);
            (await CommittedCalculationsAsync(targetId)).ShouldBe(1);
        }

        await DispatchAsync(TenantA);
    }

    [Fact]
    public async Task A_failing_calculation_keeps_the_old_values_records_the_code_and_rethrows_for_a_retry()
    {
        var targetId = Guid.NewGuid();
        using (_currentTenant.Change(TenantA))
        {
            var version = await RequestAsync(targetId);
            _control.FailWith[targetId] = "Test:Boom";

            (await Should.ThrowAsync<BusinessException>(() => RunJobAsync(targetId, version))).Code.ShouldBe("Test:Boom");
            var failed = (await StateAsync(targetId)).ShouldNotBeNull();
            failed.Status.ShouldBe(DerivedValueStatus.Failed);
            failed.FailedAttempts.ShouldBe(1);
            failed.LastErrorCode.ShouldBe("Test:Boom");
            failed.CalculatedVersion.ShouldBe(0);
            (await CommittedCalculationsAsync(targetId)).ShouldBe(0);
            _pushes.For(targetId).Single().Status.ShouldBe(DerivedValueStatus.Failed);

            /* Hangfire's retry after the cause is gone. */
            _control.FailWith.TryRemove(targetId, out _);
            await RunJobAsync(targetId, version);
            var recovered = (await StateAsync(targetId)).ShouldNotBeNull();
            recovered.Status.ShouldBe(DerivedValueStatus.UpToDate);
            recovered.FailedAttempts.ShouldBe(0);
            recovered.LastErrorCode.ShouldBeNull();
        }

        await DispatchAsync(TenantA);
    }

    [Fact]
    public async Task The_handler_enqueues_nothing_for_a_stale_or_calculated_version()
    {
        var targetId = Guid.NewGuid();
        using (_currentTenant.Change(TenantA))
        {
            var first = await RequestAsync(targetId);
            await RequestAsync(targetId);

            /* A stale event (v1 while v2 is requested) is a no-op: the v2 event carries the work. No job store is
             * touched, so the handler completes without a background-job manager call. */
            using (var uow = _unitOfWorkManager.Begin(Transactional, requiresNew: true))
            {
                await _handler.HandleEventAsync(new DerivedValuesRequestedEto
                {
                    TargetType = DerivedValueProbe.TargetType,
                    Targets = [new DerivedValueTargetVersion { TargetId = targetId, Version = first }]
                });
                await uow.CompleteAsync();
            }

            _control.Calls.Where(c => c.TargetId == targetId).ShouldBeEmpty();
        }

        await DispatchAsync(TenantA);
    }

    [Fact]
    public async Task The_dispatcher_runs_the_request_handler_in_a_unit_of_work_which_enqueues_the_job_and_frees_the_outbox()
    {
        var targetId = Guid.NewGuid();
        long version;
        using (_currentTenant.Change(TenantA))
        {
            version = await RequestAsync(targetId);
        }

        (await PostgreSqlTestDatabases.CountOutboxRowsAsync(PostgreSqlTestDatabases.TenantADatabase, targetId.ToString())).ShouldBe(1);

        /* The production path: the dispatcher reads the tenant's outbox and hands the event to DerivedValuesRequestedHandler,
         * which reads the state (a DbContext: it needs the dispatcher's unit of work) and enqueues the job. */
        (await DispatchAsync(TenantA)).ShouldBeGreaterThanOrEqualTo(1);

        (await PostgreSqlTestDatabases.CountOutboxRowsAsync(PostgreSqlTestDatabases.TenantADatabase, targetId.ToString())).ShouldBe(0, "the handled row is deleted; nothing blocks the tenant's outbox");
        GetRequiredService<RecordingBackgroundJobManager>().Of<DerivedValueCalculationArgs>().ShouldContain(a => a.TargetId == targetId);
        var enqueued = GetRequiredService<RecordingBackgroundJobManager>().Of<DerivedValueCalculationArgs>().Single(a => a.TargetId == targetId);
        (enqueued.TenantId, enqueued.TargetType, enqueued.Version).ShouldBe((TenantA, DerivedValueProbe.TargetType, version));

        /* As Hangfire would: the job calculates the record; its "recalculated" fact dispatches in turn. */
        await RunJobAsync(targetId, version);
        using (_currentTenant.Change(TenantA))
        {
            (await StateAsync(targetId)).ShouldNotBeNull().Status.ShouldBe(DerivedValueStatus.UpToDate);
        }

        (await DispatchAsync(TenantA)).ShouldBeGreaterThanOrEqualTo(1, "the recalculated fact");
        (await PostgreSqlTestDatabases.CountOutboxRowsAsync(PostgreSqlTestDatabases.TenantADatabase, targetId.ToString())).ShouldBe(0);
    }

    [Fact]
    public async Task A_job_runs_in_the_tenant_of_its_arguments_only()
    {
        var targetId = Guid.NewGuid();
        long version;
        using (_currentTenant.Change(PostgreSqlTestDatabases.SharedTenantId))
        {
            version = await RequestAsync(targetId);
        }

        /* Tenant A has no state for this id: the job finds nothing to do there. */
        await RunJobAsync(targetId, version, TenantA);
        using (_currentTenant.Change(PostgreSqlTestDatabases.SharedTenantId))
        {
            (await StateAsync(targetId)).ShouldNotBeNull().CalculatedVersion.ShouldBe(0);
        }

        await RunJobAsync(targetId, version, PostgreSqlTestDatabases.SharedTenantId);
        using (_currentTenant.Change(PostgreSqlTestDatabases.SharedTenantId))
        {
            (await StateAsync(targetId)).ShouldNotBeNull().CalculatedVersion.ShouldBe(version);
        }

        await DispatchAsync(PostgreSqlTestDatabases.SharedTenantId);
    }

    [Fact]
    public async Task The_states_endpoint_returns_the_calculated_records_and_recalculate_requests_them_again()
    {
        var service = GetRequiredService<IDerivedValueAppService>();
        var calculated = Guid.NewGuid();
        var never = Guid.NewGuid();
        using (_currentTenant.Change(TenantA))
        {
            var version = await RequestAsync(calculated);
            await RunJobAsync(calculated, version);

            var states = await WithUnitOfWorkAsync(() => service.GetStatesAsync(new DerivedValueStatesRequestDto { TargetType = DerivedValueProbe.TargetType, TargetIds = [calculated, never] }));
            var state = states.ShouldHaveSingleItem();
            state.TargetId.ShouldBe(calculated);
            state.Status.ShouldBe(DerivedValueStatus.UpToDate);
            state.CalculatedVersion.ShouldBe(version);

            (await WithUnitOfWorkAsync(Transactional, () => service.RecalculateAsync(new RecalculateDerivedValuesDto { TargetType = DerivedValueProbe.TargetType, TargetIds = [calculated] }))).ShouldBe(1);
            (await StateAsync(calculated)).ShouldNotBeNull().Status.ShouldBe(DerivedValueStatus.Pending);

            (await Should.ThrowAsync<BusinessException>(() => WithUnitOfWorkAsync(() => service.GetStatesAsync(new DerivedValueStatesRequestDto { TargetType = "Test.Unknown", TargetIds = [calculated] }))))
                .Code.ShouldBe(DerivedValueErrorCodes.UnknownTargetType);
        }

        await DispatchAsync(TenantA);
    }

    [Fact]
    public async Task A_bulk_rerun_of_ten_thousand_records_stays_within_budget()
    {
        var ids = Enumerable.Range(0, DerivedValueConsts.MaxTargetsPerRequest).Select(_ => Guid.NewGuid()).ToList();
        using (_currentTenant.Change(TenantA))
        {
            var watch = Stopwatch.StartNew();
            using (var uow = _unitOfWorkManager.Begin(Transactional, requiresNew: true))
            {
                (await _scheduler.RequestManyAsync(DerivedValueProbe.TargetType, ids, "Bulk")).ShouldBe(ids.Count);
                await uow.CompleteAsync();
            }

            watch.Stop();
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2), $"10,000 requests took {watch.Elapsed}");

            /* One upsert, and one outbox row per thousand records. */
            (await PostgreSqlTestDatabases.CountOutboxRowsAsync(PostgreSqlTestDatabases.TenantADatabase, ids[0].ToString())).ShouldBe(1);
            using (var uow = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(), requiresNew: true))
            {
                (await _store.GetListAsync(DerivedValueProbe.TargetType, ids.Take(500).ToList())).ShouldAllBe(s => s.RequestedVersion == 1 && s.Status == DerivedValueStatus.Pending);
                await uow.CompleteAsync();
            }

            using (var uow = _unitOfWorkManager.Begin(Transactional, requiresNew: true))
            {
                (await Should.ThrowAsync<BusinessException>(() => _scheduler.RequestManyAsync(DerivedValueProbe.TargetType, Enumerable.Range(0, DerivedValueConsts.MaxTargetsPerRequest + 1).Select(_ => Guid.NewGuid()).ToList())))
                    .Code.ShouldBe(DerivedValueErrorCodes.TooManyTargets);
            }
        }

        /* Drain the 10,000 request events so later tests see a clean outbox. */
        while (await DispatchAsync(TenantA) > 0)
        {
        }
    }
}
