using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OpenTms.Platform.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.NumberSequences;

/// <summary>
/// The counter statements of design 50 as single atomic upserts on the Platform's own mapped table, in the current
/// unit of work's connection and transaction. Parameterised; the table name comes from the EF model. PostgreSQL in
/// production; the SQLite unit-test provider accepts the same statement (it stores a Guid as upper-case text).
/// </summary>
public class EfCoreNumberSequenceCounterStore : INumberSequenceCounterStore, ITransientDependency
{
    private const string SqliteProvider = "Microsoft.EntityFrameworkCore.Sqlite";

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
        var table = TableName(db);
        var sql =
            $"INSERT INTO {table} AS c (\"Id\", \"TenantId\", \"SequenceKey\", \"Period\", \"LastValue\") " +
            "VALUES (@id, @tenantId, @key, @period, 1) " +
            "ON CONFLICT (\"TenantId\", \"SequenceKey\", \"Period\") DO UPDATE SET \"LastValue\" = c.\"LastValue\" + 1 " +
            "RETURNING \"LastValue\"";

        await using var command = await CreateCommandAsync(db, sql, sequenceKey, period);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result);
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
        var table = TableName(db);
        var sql =
            $"INSERT INTO {table} AS c (\"Id\", \"TenantId\", \"SequenceKey\", \"Period\", \"LastValue\") " +
            "VALUES (@id, @tenantId, @key, @period, @value) " +
            "ON CONFLICT (\"TenantId\", \"SequenceKey\", \"Period\") DO UPDATE SET \"LastValue\" = @value WHERE c.\"LastValue\" < @value";

        await using var command = await CreateCommandAsync(db, sql, sequenceKey, period);
        AddParameter(command, "@value", lastValue);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    private async Task<DbCommand> CreateCommandAsync(PlatformDbContext db, string sql, string sequenceKey, string period)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync();
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();

        var sqlite = IsSqlite(db);
        AddParameter(command, "@id", GuidValue(_guidGenerator.Create(), sqlite));
        AddParameter(command, "@tenantId", _currentTenant.Id is { } tenantId ? GuidValue(tenantId, sqlite) : DBNull.Value);
        AddParameter(command, "@key", sequenceKey);
        AddParameter(command, "@period", period);
        return command;
    }

    private static object GuidValue(Guid value, bool sqlite) => sqlite ? value.ToString().ToUpperInvariant() : value;

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static bool IsSqlite(DbContext db) => db.Database.ProviderName == SqliteProvider;

    private static string TableName(PlatformDbContext db)
    {
        var entity = db.Model.FindEntityType(typeof(NumberSequenceCounter))!;
        var name = "\"" + entity.GetTableName() + "\"";
        var schema = entity.GetSchema();
        /* SQLite has no schemas; the test provider maps the table without one. */
        return schema == null || IsSqlite(db) ? name : "\"" + schema + "\"." + name;
    }
}
