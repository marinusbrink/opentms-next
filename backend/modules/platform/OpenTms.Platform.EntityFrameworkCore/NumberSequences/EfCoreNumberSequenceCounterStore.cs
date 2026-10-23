using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenTms.Platform.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.NumberSequences;

/// <summary>
/// The counter statements of design 50 as single atomic upserts on the Platform's own mapped table, in the current
/// unit of work's connection and transaction (<see cref="PlatformSqlCommands"/>).
/// </summary>
public class EfCoreNumberSequenceCounterStore : INumberSequenceCounterStore, ITransientDependency
{
    private readonly IDbContextProvider<PlatformDbContext> _dbContextProvider;
    private readonly ICurrentTenant _currentTenant;
    private readonly IGuidGenerator _guidGenerator;

    public EfCoreNumberSequenceCounterStore(
        IDbContextProvider<PlatformDbContext> dbContextProvider,
        ICurrentTenant currentTenant,
        IGuidGenerator guidGenerator)
    {
        _dbContextProvider = dbContextProvider;
        _currentTenant = currentTenant;
        _guidGenerator = guidGenerator;
    }

    public virtual async Task<long> IncrementAsync(string sequenceKey, string period)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        var sql =
            $"INSERT INTO {PlatformSqlCommands.TableName<NumberSequenceCounter>(db)} AS c (\"Id\", \"TenantId\", \"SequenceKey\", \"Period\", \"LastValue\") " +
            "VALUES (@id, @tenantId, @key, @period, 1) " +
            "ON CONFLICT (\"TenantId\", \"SequenceKey\", \"Period\") DO UPDATE SET \"LastValue\" = c.\"LastValue\" + 1 " +
            "RETURNING \"LastValue\"";

        await using var command = await CreateCommandAsync(db, sql, sequenceKey, period);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public virtual async Task<long> GetLastValueAsync(string sequenceKey, string period)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        var tenantId = _currentTenant.Id;
        /* The tenant filter applies as well; the explicit tenant test keeps host counters out of a tenant read. */
        var values = await db.NumberSequenceCounters
            .Where(c => c.TenantId == tenantId && c.SequenceKey == sequenceKey && c.Period == period)
            .Select(c => c.LastValue)
            .ToListAsync();
        return values.Count == 0 ? 0 : values[0];
    }

    public virtual async Task<bool> RaiseLastValueAsync(string sequenceKey, string period, long lastValue)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        var sql =
            $"INSERT INTO {PlatformSqlCommands.TableName<NumberSequenceCounter>(db)} AS c (\"Id\", \"TenantId\", \"SequenceKey\", \"Period\", \"LastValue\") " +
            "VALUES (@id, @tenantId, @key, @period, @value) " +
            "ON CONFLICT (\"TenantId\", \"SequenceKey\", \"Period\") DO UPDATE SET \"LastValue\" = @value WHERE c.\"LastValue\" < @value";

        await using var command = await CreateCommandAsync(db, sql, sequenceKey, period);
        PlatformSqlCommands.Add(command, "@value", lastValue);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    private async Task<DbCommand> CreateCommandAsync(PlatformDbContext db, string sql, string sequenceKey, string period)
    {
        var command = await PlatformSqlCommands.CreateAsync(db, sql);
        PlatformSqlCommands.Add(command, "@id", PlatformSqlCommands.GuidValue(db, _guidGenerator.Create()));
        PlatformSqlCommands.Add(command, "@tenantId", PlatformSqlCommands.GuidValue(db, _currentTenant.Id));
        PlatformSqlCommands.Add(command, "@key", sequenceKey);
        PlatformSqlCommands.Add(command, "@period", period);
        return command;
    }
}
