using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.NumberSequences;

/// <summary>
/// Design 50 against real PostgreSQL databases. Risk class critical: numbers must never repeat, never skip, and never
/// leak between tenants. Every test uses its own sequence key, so tests do not share counters.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class NumberSequencePostgreSqlTests : OpenTmsPostgreSqlTestBase
{
    private static readonly NumberSequenceFormat Format = NumberSequenceFormat.Create("TO", false, false, "-", 5);

    private readonly INumberSequenceGenerator _generator;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentTenant _currentTenant;

    public NumberSequencePostgreSqlTests()
    {
        _generator = GetRequiredService<INumberSequenceGenerator>();
        _unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    private static string NewKey() => "Test.Sequence:" + Guid.NewGuid();

    private async Task<string> TakeAsync(string key, bool commit = true)
    {
        using var uow = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
        var number = await _generator.NextAsync(key, Format);
        if (commit)
        {
            await uow.CompleteAsync();
        }

        return number;
    }

    [Fact]
    public async Task Parallel_transactions_on_one_sequence_get_distinct_consecutive_numbers()
    {
        var key = NewKey();
        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            var numbers = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => Task.Run(async () =>
            {
                using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
                {
                    return await TakeAsync(key);
                }
            })));

            numbers.Distinct().Count().ShouldBe(30);
            numbers.OrderBy(n => n).ShouldBe(Enumerable.Range(1, 30).Select(i => $"TO-{i:D5}"));
        }
    }

    [Fact]
    public async Task A_rolled_back_transaction_returns_its_number()
    {
        var key = NewKey();
        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            (await TakeAsync(key)).ShouldBe("TO-00001");
            (await TakeAsync(key, commit: false)).ShouldBe("TO-00002");
            (await TakeAsync(key)).ShouldBe("TO-00002");
        }
    }

    [Fact]
    public async Task Counters_are_per_tenant_and_per_database()
    {
        var key = NewKey();
        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            (await TakeAsync(key)).ShouldBe("TO-00001");
            (await TakeAsync(key)).ShouldBe("TO-00002");
        }

        /* The shared-tier tenant lives in the host database; the host itself has its own counter there. */
        using (_currentTenant.Change(PostgreSqlTestDatabases.SharedTenantId))
        {
            (await TakeAsync(key)).ShouldBe("TO-00001");
        }

        using (_currentTenant.Change(null))
        {
            (await TakeAsync(key)).ShouldBe("TO-00001");
        }

        using (_currentTenant.Change(PostgreSqlTestDatabases.SharedTenantId))
        {
            (await TakeAsync(key)).ShouldBe("TO-00002");
        }
    }

    [Fact]
    public async Task Taking_a_number_outside_a_transaction_is_refused()
    {
        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            using var uow = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: false), requiresNew: true);
            var error = await Should.ThrowAsync<BusinessException>(() => _generator.NextAsync(NewKey(), Format));
            error.Code.ShouldBe(ReferenceDataErrorCodes.RequiresTransaction);
        }
    }

    [Fact]
    public async Task Next_value_moves_only_upwards_and_peeking_takes_nothing()
    {
        var key = NewKey();
        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            using (var uow = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true))
            {
                (await _generator.PeekNextAsync(key, Format)).ShouldBe("TO-00001");
                (await _generator.PeekNextAsync(key, Format)).ShouldBe("TO-00001");
                await _generator.SetNextValueAsync(key, Format, 500);
                await uow.CompleteAsync();
            }

            (await TakeAsync(key)).ShouldBe("TO-00500");

            using (var uow = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true))
            {
                var error = await Should.ThrowAsync<BusinessException>(() => _generator.SetNextValueAsync(key, Format, 500));
                error.Code.ShouldBe(ReferenceDataErrorCodes.NextNumberTooLow);
                (await _generator.PeekNextValueAsync(key, Format)).ShouldBe(501);
                await uow.CompleteAsync();
            }
        }
    }

    [Fact]
    public async Task A_yearly_sequence_counts_per_period()
    {
        var key = NewKey();
        var yearly = NumberSequenceFormat.Create("TR", true, false, "-", 5);
        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            using var uow = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
            var number = await _generator.NextAsync(key, yearly);
            var year = number.Split('-')[1];
            number.ShouldBe($"TR-{year}-00001");
            (await _generator.NextAsync(key, Format)).ShouldBe("TO-00001", "a format without year counts in its own period");
            await uow.CompleteAsync();
        }
    }
}
