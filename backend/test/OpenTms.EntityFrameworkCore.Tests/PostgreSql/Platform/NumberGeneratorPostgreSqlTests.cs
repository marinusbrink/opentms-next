using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.Features;
using OpenTms.Platform;
using OpenTms.Platform.Features;
using OpenTms.Platform.NumberSequences;
using Shouldly;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.Platform;

/// <summary>
/// INumberGenerator against real PostgreSQL (docs/designs/49-platform-master-data.md, test risk analysis "Number generation
/// (atomicity, period rollover, rollback gives the number back, formatter)", Critical): 50 parallel callers on one sequence
/// get 50 distinct consecutive numbers, also when the period's counter row does not exist yet; a rolled-back caller's
/// number goes to the next caller (gapless per committed transaction), also while that caller is waiting on the row lock;
/// the period comes from the passed date for every scheme; outside a transactional unit of work the generator refuses;
/// another tenant's sequence id is EntityNotFound and draws nothing; counters are per tenant; no feature gates the
/// generator; a sequence with drawn numbers is frozen through its service with the real transaction. Committed rows are
/// removed by id at the end of each test.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class NumberGeneratorPostgreSqlTests : PlatformMasterDataPostgreSqlTestBase
{
    private const int Callers = 50;
    private static readonly DateOnly Date = new(2026, 10, 17);

    private INumberGenerator Generator => GetRequiredService<INumberGenerator>();
    private IRepository<NumberSequence, Guid> Sequences => GetRequiredService<IRepository<NumberSequence, Guid>>();
    private IRepository<NumberSequenceCounter> CounterRows => GetRequiredService<IRepository<NumberSequenceCounter>>();
    private INumberSequenceAppService SequenceService => GetRequiredService<INumberSequenceAppService>();

    public static IEnumerable<object?[]> Tenants() => [["tenant-a"], ["shared"]];

    private static Guid? TenantOf(string tenant) => tenant == "tenant-a" ? TenantA : SharedTenant;

    private async Task<string[]> DrawInParallelAsync(Guid? tenant, Guid sequenceId, DateOnly date)
    {
        /* Fifty callers hold fifty connections at once; the idle pooled connections the earlier tests of this process left
         * open (one pool per database) would push a default server (max_connections = 100) over its limit. */
        Npgsql.NpgsqlConnection.ClearAllPools();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callers = Enumerable.Range(0, Callers)
            .Select(_ => Task.Run(() => DrawInOwnTransactionAsync(tenant, sequenceId, date, start.Task, TimeSpan.FromMilliseconds(5))))
            .ToList();
        start.SetResult();
        return await Task.WhenAll(callers);
    }

    // ── Atomicity ─────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Tenants))]
    public async Task Fifty_parallel_callers_on_one_sequence_get_fifty_distinct_consecutive_numbers(string tenantName)
    {
        var tenant = TenantOf(tenantName);
        var sequence = await CommitSequenceAsync(tenant);
        try
        {
            /* The period's row exists before the burst: every caller takes the update path. */
            (await DrawInOwnTransactionAsync(tenant, sequence, Date)).ShouldBe("O-26-0001");

            var numbers = await DrawInParallelAsync(tenant, sequence, Date);

            numbers.Distinct().Count().ShouldBe(Callers, "no number is given twice: " + string.Join(", ", numbers.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key)));
            numbers.OrderBy(n => n, StringComparer.Ordinal).ShouldBe(Enumerable.Range(2, Callers).Select(i => $"O-26-{i:D4}"), "consecutive, without a gap");
            (await CommittedCountersAsync(tenant, sequence)).ShouldBe(new Dictionary<(int, int), long> { [(2026, 0)] = Callers + 1 });
        }
        finally
        {
            await DeleteSequenceAsync(tenant, sequence);
        }
    }

    [Theory]
    [MemberData(nameof(Tenants))]
    public async Task Fifty_parallel_first_calls_of_a_new_period_create_one_counter_row_and_fifty_distinct_numbers(string tenantName)
    {
        var tenant = TenantOf(tenantName);
        var sequence = await CommitSequenceAsync(tenant, NumberSequenceScheme.FourDigitYearMonth, "M", "/", 3);
        try
        {
            /* A row for another period exists; the burst opens 2031-01, whose row nobody has created yet. */
            (await DrawInOwnTransactionAsync(tenant, sequence, new DateOnly(2030, 12, 31))).ShouldBe("M2030/12001");
            var numbers = await DrawInParallelAsync(tenant, sequence, new DateOnly(2031, 1, 1));

            numbers.Distinct().Count().ShouldBe(Callers);
            numbers.OrderBy(n => n, StringComparer.Ordinal).ShouldBe(Enumerable.Range(1, Callers).Select(i => $"M2031/01{i:D3}"));
            (await CommittedCountersAsync(tenant, sequence)).ShouldBe(new Dictionary<(int, int), long> { [(2030, 12)] = 1, [(2031, 1)] = Callers });
        }
        finally
        {
            await DeleteSequenceAsync(tenant, sequence);
        }
    }

    // ── Rollback gives the number back ───────────────────────────────────────

    [Theory]
    [MemberData(nameof(Tenants))]
    public async Task A_rolled_back_callers_number_is_given_to_the_next_caller(string tenantName)
    {
        var tenant = TenantOf(tenantName);
        var sequence = await CommitSequenceAsync(tenant);
        try
        {
            /* First number of the period, rolled back: the row itself disappears and the next caller starts at 1 again. */
            (await DrawInOwnTransactionAsync(tenant, sequence, Date, rollback: true)).ShouldBe("O-26-0001");
            (await CommittedCountersAsync(tenant, sequence)).ShouldBeEmpty();
            (await DrawInOwnTransactionAsync(tenant, sequence, Date)).ShouldBe("O-26-0001");

            /* A later number, rolled back, comes back too. */
            (await DrawInOwnTransactionAsync(tenant, sequence, Date)).ShouldBe("O-26-0002");
            (await DrawInOwnTransactionAsync(tenant, sequence, Date, rollback: true)).ShouldBe("O-26-0003");
            (await DrawInOwnTransactionAsync(tenant, sequence, Date)).ShouldBe("O-26-0003");
            (await CommittedCountersAsync(tenant, sequence))[(2026, 0)].ShouldBe(3);
        }
        finally
        {
            await DeleteSequenceAsync(tenant, sequence);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_caller_waiting_on_the_row_lock_gets_the_number_of_the_caller_that_rolls_back(bool rowExists)
    {
        var sequence = await CommitSequenceAsync(TenantA);
        try
        {
            var expected = "O-26-0001";
            if (rowExists)
            {
                await DrawInOwnTransactionAsync(TenantA, sequence, Date);
                expected = "O-26-0002";
            }

            using var scope = ServiceProvider.CreateScope();
            var unitOfWorkManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var generator = scope.ServiceProvider.GetRequiredService<INumberGenerator>();
            Task<string> waiting;
            using (CurrentTenant.Change(TenantA))
            using (var first = unitOfWorkManager.Begin(Transactional, requiresNew: true))
            {
                (await generator.GetNextAsync(sequence, Date)).ShouldBe(expected);

                /* A second caller blocks on the row (or on the not yet committed insert of the period's row). */
                waiting = Task.Run(() => DrawInOwnTransactionAsync(TenantA, sequence, Date));
                await Task.Delay(750);
                waiting.IsCompleted.ShouldBeFalse("the second caller must wait for the first transaction to end");

                await first.RollbackAsync();
            }

            (await waiting).ShouldBe(expected, "the rolled-back number is not lost");
            (await DrawInOwnTransactionAsync(TenantA, sequence, Date)).ShouldBe(rowExists ? "O-26-0003" : "O-26-0002");
        }
        finally
        {
            await DeleteSequenceAsync(TenantA, sequence);
        }
    }

    // ── Period rollover from the passed date, per scheme ─────────────────────

    public static IEnumerable<object[]> Rollover() =>
    [
        /* scheme, then (date → expected number) in call order */
        [NumberSequenceScheme.FourDigitYear, new[] { "2026-12-31=R2026-0001", "2026-12-31=R2026-0002", "2027-01-01=R2027-0001", "2026-06-15=R2026-0003", "2027-12-31=R2027-0002" }],
        [NumberSequenceScheme.TwoDigitYear, new[] { "2026-12-31=R26-0001", "2027-01-01=R27-0001", "2026-01-01=R26-0002", "2126-01-01=R26-0001" }],
        [NumberSequenceScheme.FourDigitYearMonth, new[] { "2026-11-30=R2026-110001", "2026-12-01=R2026-120001", "2026-12-31=R2026-120002", "2027-01-01=R2027-010001", "2026-11-01=R2026-110002" }],
        [NumberSequenceScheme.TwoDigitYearMonth, new[] { "2026-12-31=R26-120001", "2027-01-01=R27-010001", "2027-01-31=R27-010002", "2026-12-01=R26-120002" }],
        [NumberSequenceScheme.CounterOnly, new[] { "2026-12-31=R0001", "2027-01-01=R0002", "1999-05-05=R0003", "2040-02-29=R0004" }],
    ];

    [Theory]
    [MemberData(nameof(Rollover))]
    public async Task The_counter_restarts_per_period_of_the_passed_date_never_of_the_clock(NumberSequenceScheme scheme, string[] calls)
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var sequence = new NumberSequence(Guid.NewGuid(), TenantA, "QQ rollover " + scheme, new NumberSequenceFormat(scheme, "R", null, "-", null, null, 4));
            await Sequences.InsertAsync(sequence, autoSave: true);

            foreach (var call in calls)
            {
                var (date, expected) = (DateOnly.Parse(call.Split('=')[0]), call.Split('=')[1]);
                (await Generator.GetNextAsync(sequence.Id, date)).ShouldBe(expected, $"{scheme} on {date:yyyy-MM-dd}");
            }

            /* The periods are those of the dates passed: none is today's unless a call passed today. */
            var periods = (await CounterRows.GetListAsync(c => c.NumberSequenceId == sequence.Id)).Select(c => (c.PeriodYear, c.PeriodMonth)).ToList();
            var expectedPeriods = calls.Select(c => NumberSequenceFormatter.PeriodOf(scheme, DateOnly.Parse(c.Split('=')[0]))).Distinct().ToList();
            periods.ShouldBe(expectedPeriods, ignoreOrder: true);
            if (scheme == NumberSequenceScheme.CounterOnly)
            {
                periods.ShouldBe([(0, 0)]);
            }
        });
    }

    [Fact]
    public async Task The_designs_example_O_26_0001_comes_out_of_a_real_sequence()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var created = await SequenceService.CreateAsync(new NumberSequenceCreateDto { Name = "QQ orders", Scheme = NumberSequenceScheme.TwoDigitYear, Prefix = "O-", YearSuffix = "-", MinimumDigits = 4 });
            (await Generator.GetNextAsync(created.Id, new DateOnly(2026, 3, 5))).ShouldBe("O-26-0001");
            (await Generator.GetNextAsync(created.Id, new DateOnly(2026, 3, 5))).ShouldBe("O-26-0002");
            var read = await SequenceService.GetAsync(created.Id);
            read.IsInUse.ShouldBeTrue();
        });
    }

    // ── Refusals ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Outside_a_transactional_unit_of_work_the_generator_refuses_and_draws_nothing()
    {
        var sequence = await CommitSequenceAsync(TenantA);
        try
        {
            using (CurrentTenant.Change(TenantA))
            {
                await Should.ThrowAsync<NumberGenerationRequiresTransactionException>(() => Generator.GetNextAsync(sequence, Date));
                await Should.ThrowAsync<NumberGenerationRequiresTransactionException>(() =>
                    WithUnitOfWorkAsync(new AbpUnitOfWorkOptions { IsTransactional = false }, () => Generator.GetNextAsync(sequence, Date)));
            }

            (await CommittedCountersAsync(TenantA, sequence)).ShouldBeEmpty();
        }
        finally
        {
            await DeleteSequenceAsync(TenantA, sequence);
        }
    }

    [Fact]
    public async Task Another_tenants_sequence_id_is_not_found_and_draws_nothing()
    {
        var inTenantA = await CommitSequenceAsync(TenantA);
        var inShared = await CommitSequenceAsync(SharedTenant);
        try
        {
            /* Every other tenant, the host database's two neighbours (host and shared tenant) included. */
            foreach (var (caller, sequence, owner) in new (Guid? Caller, Guid Sequence, Guid? Owner)[]
                     {
                         (SharedTenant, inTenantA, TenantA), (null, inTenantA, TenantA),
                         (null, inShared, SharedTenant), (TenantA, inShared, SharedTenant),
                     })
            {
                await Should.ThrowAsync<EntityNotFoundException>(() => DrawInOwnTransactionAsync(caller, sequence, Date), $"{NameFor(caller)} drawing from {NameFor(owner)}");
                (await CommittedCountersAsync(owner, sequence)).ShouldBeEmpty($"{NameFor(caller)} drew nothing from {NameFor(owner)}");
            }

            /* The owners still draw their first number. */
            (await DrawInOwnTransactionAsync(TenantA, inTenantA, Date)).ShouldBe("O-26-0001");
            (await DrawInOwnTransactionAsync(SharedTenant, inShared, Date)).ShouldBe("O-26-0001");
        }
        finally
        {
            await DeleteSequenceAsync(TenantA, inTenantA);
            await DeleteSequenceAsync(SharedTenant, inShared);
        }
    }

    [Fact]
    public async Task A_deleted_or_unknown_sequence_is_not_found()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var sequence = new NumberSequence(Guid.NewGuid(), TenantA, "QQ deleted", new NumberSequenceFormat(NumberSequenceScheme.CounterOnly, null, null, null, null, null, 4));
            await Sequences.InsertAsync(sequence, autoSave: true);
            await Sequences.DeleteAsync(sequence, autoSave: true);

            await Should.ThrowAsync<EntityNotFoundException>(() => Generator.GetNextAsync(sequence.Id, Date));
            await Should.ThrowAsync<EntityNotFoundException>(() => Generator.GetNextAsync(Guid.NewGuid(), Date));
            (await CounterRows.CountAsync(c => c.NumberSequenceId == sequence.Id)).ShouldBe(0);
        });
    }

    // ── Counters per tenant ──────────────────────────────────────────────────

    [Fact]
    public async Task Counters_are_per_tenant_also_for_the_host_and_the_shared_tenant_in_one_database()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            async Task<Guid> SequenceAsync(Guid? tenant)
            {
                using (CurrentTenant.Change(tenant))
                {
                    var sequence = new NumberSequence(Guid.NewGuid(), tenant, "QQ per tenant", new NumberSequenceFormat(NumberSequenceScheme.TwoDigitYear, "O-", null, "-", null, null, 4));
                    await Sequences.InsertAsync(sequence, autoSave: true);
                    return sequence.Id;
                }
            }

            async Task<string> DrawAsync(Guid? tenant, Guid sequence)
            {
                using (CurrentTenant.Change(tenant))
                {
                    return await Generator.GetNextAsync(sequence, Date);
                }
            }

            var a = await SequenceAsync(TenantA);
            var shared = await SequenceAsync(SharedTenant);
            var host = await SequenceAsync(null);

            (await DrawAsync(TenantA, a)).ShouldBe("O-26-0001");
            (await DrawAsync(TenantA, a)).ShouldBe("O-26-0002");
            (await DrawAsync(TenantA, a)).ShouldBe("O-26-0003");
            (await DrawAsync(SharedTenant, shared)).ShouldBe("O-26-0001");
            (await DrawAsync(null, host)).ShouldBe("O-26-0001");
            (await DrawAsync(null, host)).ShouldBe("O-26-0002");
            (await DrawAsync(SharedTenant, shared)).ShouldBe("O-26-0002");

            foreach (var (tenant, sequence, last) in new (Guid? Tenant, Guid Sequence, long Last)[] { (TenantA, a, 3), (SharedTenant, shared, 2), (null, host, 2) })
            {
                using (CurrentTenant.Change(tenant))
                {
                    var rows = await CounterRows.GetListAsync();
                    var row = rows.ShouldHaveSingleItem($"{NameFor(tenant)} sees its own counter only");
                    (row.NumberSequenceId, row.TenantId, row.LastValue).ShouldBe((sequence, tenant, last));
                }
            }
        });
    }

    [Fact]
    public async Task No_feature_gates_the_generator()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var sequence = new NumberSequence(Guid.NewGuid(), TenantA, "QQ ungated", new NumberSequenceFormat(NumberSequenceScheme.CounterOnly, "U", null, null, null, null, 2));
            await Sequences.InsertAsync(sequence, autoSave: true);

            using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Default, "false"))
            using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Platform.Default, "false"))
            using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Platform.NumberSequences, "false"))
            {
                (await Generator.GetNextAsync(sequence.Id, Date)).ShouldBe("U01");
            }
        });
    }

    // ── The guard through the service with the real transaction ──────────────

    [Fact]
    public async Task A_sequence_with_drawn_numbers_refuses_a_format_change_and_a_delete_and_a_refused_update_writes_nothing()
    {
        var sequence = await CommitSequenceAsync(TenantA);
        try
        {
            (await DrawInOwnTransactionAsync(TenantA, sequence, Date)).ShouldBe("O-26-0001");

            using (CurrentTenant.Change(TenantA))
            {
                var dto = await SequenceService.GetAsync(sequence);
                dto.IsInUse.ShouldBeTrue();

                /* The service's own transactional unit of work: the name set before the refusal is rolled back with it. */
                await ShouldFailAsync(() => SequenceService.UpdateAsync(sequence, new NumberSequenceUpdateDto
                {
                    Name = "QQ renamed", Scheme = dto.Scheme, Prefix = dto.Prefix, YearSuffix = dto.YearSuffix, MinimumDigits = 6, ConcurrencyStamp = dto.ConcurrencyStamp
                }), PlatformErrorCodes.NumberSequenceInUse);

                var after = await SequenceService.GetAsync(sequence);
                (after.Name, after.MinimumDigits, after.ConcurrencyStamp).ShouldBe((dto.Name, 4, dto.ConcurrencyStamp), "a refused update writes nothing");

                (await ShouldFailAsync(() => SequenceService.DeleteAsync(sequence), PlatformErrorCodes.InUse)).Data["reason"].ShouldNotBeNull();
                (await SequenceService.BulkDeleteAsync(new PlatformMasterDataBulkDeleteRequestDto { Selection = Explicit(sequence) })).SkippedRows.ShouldHaveSingleItem().Id.ShouldBe(sequence);

                /* Name and description stay editable. */
                var renamed = await SequenceService.UpdateAsync(sequence, new NumberSequenceUpdateDto
                {
                    Name = dto.Name + " renamed", Scheme = dto.Scheme, Prefix = dto.Prefix, YearSuffix = dto.YearSuffix, MinimumDigits = 4, Description = "kept",
                    ConcurrencyStamp = dto.ConcurrencyStamp
                });
                (renamed.Name, renamed.Description).ShouldBe((dto.Name + " renamed", "kept"));
            }

            (await DrawInOwnTransactionAsync(TenantA, sequence, Date)).ShouldBe("O-26-0002");
        }
        finally
        {
            await DeleteSequenceAsync(TenantA, sequence);
        }
    }
}
