using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTms.Platform;
using OpenTms.Platform.NumberSequences;
using OpenTms.PostgreSql.MasterData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.PostgreSql.Platform;

/// <summary>
/// Shared plumbing for the Platform master-data tests against real PostgreSQL (design 49): the rolled-back transaction of
/// <see cref="MasterDataPostgreSqlTestBase"/>, plus committed rows for the tests that need several transactions at once
/// (the number generator's concurrency and rollback), removed again with plain SQL by id, and raw reads of the counters.
/// </summary>
public abstract class PlatformMasterDataPostgreSqlTestBase : MasterDataPostgreSqlTestBase
{
    protected static readonly AbpUnitOfWorkOptions Transactional = new() { IsTransactional = true };

    /// <summary>The database a tenant's rows live in: tenant A its own, the shared tenant and the host the host database.</summary>
    protected static string DatabaseOf(Guid? tenant) => tenant == TenantA ? PostgreSqlTestDatabases.TenantADatabase : PostgreSqlTestDatabases.HostDatabase;

    protected static string NameFor(Guid? tenant) => tenant == null ? "host" : tenant == TenantA ? "tenant A (own database)" : "shared tenant (host database)";

    protected static PlatformLocalizedNameDto PlatformNames(string en, string? nl = null)
    {
        var names = new PlatformLocalizedNameDto { ["en"] = en };
        if (nl != null)
        {
            names["nl"] = nl;
        }

        return names;
    }

    protected static TRequest PlatformList<TRequest>(string? search = null, int start = 0, int end = 100, string? sortBy = null, string? sortDir = null)
        where TRequest : PlatformMasterDataListRequestDto, new()
        => new() { StartRow = start, EndRow = end, WildcardSearch = search, SortBy = sortBy, SortDir = sortDir };

    /// <summary>Commits a number sequence in <paramref name="tenant"/> in its own transaction; remove it with <see cref="DeleteSequenceAsync"/>.</summary>
    protected async Task<Guid> CommitSequenceAsync(Guid? tenant, NumberSequenceScheme scheme = NumberSequenceScheme.TwoDigitYear, string? prefix = "O-", string? yearSuffix = "-", int digits = 4)
    {
        using (CurrentTenant.Change(tenant))
        {
            return await WithUnitOfWorkAsync(Transactional, async () =>
            {
                var sequence = new NumberSequence(Guid.NewGuid(), tenant, "QQ generator " + Guid.NewGuid().ToString("N"),
                    new NumberSequenceFormat(scheme, prefix, null, yearSuffix, null, null, digits));
                await GetRequiredService<IRepository<NumberSequence, Guid>>().InsertAsync(sequence, autoSave: true);
                return sequence.Id;
            });
        }
    }

    /// <summary>Removes a committed sequence (and, by the cascade, its counters) from the tenant's database.</summary>
    protected static async Task DeleteSequenceAsync(Guid? tenant, Guid id)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(DatabaseOf(tenant)));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM platform.\"NumberSequences\" WHERE \"Id\" = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>The committed counter rows of a sequence: (year, month) → last value, read outside every unit of work.</summary>
    protected static async Task<Dictionary<(int Year, int Month), long>> CommittedCountersAsync(Guid? tenant, Guid sequenceId)
    {
        var rows = new Dictionary<(int, int), long>();
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(DatabaseOf(tenant)));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT \"PeriodYear\", \"PeriodMonth\", \"LastValue\", \"TenantId\" FROM platform.\"NumberSequenceCounters\" WHERE \"NumberSequenceId\" = @id", connection);
        command.Parameters.AddWithValue("id", sequenceId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            (reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3)).ShouldBe(tenant, "a counter row carries its sequence's tenant");
            rows[(reader.GetInt32(0), reader.GetInt32(1))] = reader.GetInt64(2);
        }

        return rows;
    }

    /// <summary>
    /// Draws one number in its own scope and transactional unit of work, as a request of another module would: optionally
    /// waits for <paramref name="start"/> first, holds the transaction (and so the counter row lock) for
    /// <paramref name="hold"/> after drawing, then commits or rolls back.
    /// </summary>
    protected async Task<string> DrawInOwnTransactionAsync(Guid? tenant, Guid sequenceId, DateOnly date, Task? start = null, TimeSpan? hold = null, bool rollback = false)
    {
        if (start != null)
        {
            await start;
        }

        using var scope = ServiceProvider.CreateScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var unitOfWorkManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var generator = scope.ServiceProvider.GetRequiredService<INumberGenerator>();

        using (currentTenant.Change(tenant))
        using (var unitOfWork = unitOfWorkManager.Begin(Transactional, requiresNew: true))
        {
            var number = await generator.GetNextAsync(sequenceId, date);
            if (hold != null)
            {
                await Task.Delay(hold.Value);
            }

            if (rollback)
            {
                await unitOfWork.RollbackAsync();
            }
            else
            {
                await unitOfWork.CompleteAsync();
            }

            return number;
        }
    }

    protected static async Task<BusinessException> ShouldFailAsync(Func<Task> action, string code)
    {
        var ex = await Should.ThrowAsync<BusinessException>(action);
        ex.Code.ShouldBe(code, ex.Message);
        return ex;
    }
}
