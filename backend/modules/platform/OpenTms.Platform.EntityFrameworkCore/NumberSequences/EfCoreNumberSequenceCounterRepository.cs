using System.Linq;
using System.Collections.Generic;
using System;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using OpenTms.Platform.NumberSequences;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace OpenTms.Platform.EntityFrameworkCore.NumberSequences;

/// <summary>
/// The counter of the number generator: one INSERT … ON CONFLICT … DO UPDATE … RETURNING statement per number, in the
/// current unit of work's transaction (docs/designs/49-platform-master-data.md, "Generation"). Raw SQL by design: no
/// read-then-write race, the row lock serialises concurrent callers until commit, a rollback gives the number back.
/// PostgreSQL and SQLite (≥ 3.35, the unit-test provider) both run the statement. Table and column names come from the
/// EF model and are quoted by the provider (SQLite has no schemas), never hardcoded.
/// </summary>
public class EfCoreNumberSequenceCounterRepository
    : EfCoreRepository<PlatformDbContext, NumberSequenceCounter>, INumberSequenceCounterRepository
{
    public EfCoreNumberSequenceCounterRepository(IDbContextProvider<PlatformDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public virtual async Task<long> IncrementAsync(Guid? tenantId, Guid numberSequenceId, int periodYear, int periodMonth, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        var database = dbContext.Database;

        var sql = BuildUpsertSql(dbContext);
        var typeMappings = dbContext.GetService<IRelationalTypeMappingSource>();

        await database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.Transaction = database.CurrentTransaction?.GetDbTransaction();

            /* Parameters through the provider's type mappings, so a Guid is bound the way EF stores it (uuid on
             * PostgreSQL, TEXT on SQLite). */
            command.Parameters.Add(Parameter(typeMappings, command, "@tenantId", typeof(Guid), tenantId));
            command.Parameters.Add(Parameter(typeMappings, command, "@numberSequenceId", typeof(Guid), numberSequenceId));
            command.Parameters.Add(Parameter(typeMappings, command, "@periodYear", typeof(int), periodYear));
            command.Parameters.Add(Parameter(typeMappings, command, "@periodMonth", typeof(int), periodMonth));

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }
        finally
        {
            await database.CloseConnectionAsync();
        }
    }

    public virtual async Task LockSequencesAsync(Guid? tenantId, IReadOnlyCollection<Guid> numberSequenceIds, bool exclusive, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        var database = dbContext.Database;
        if (numberSequenceIds.Count == 0 || database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            return;
        }

        var entityType = dbContext.Model.FindEntityType(typeof(NumberSequence))
                         ?? throw new InvalidOperationException("NumberSequence is not mapped by the Platform context.");
        var table = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
        var sqlHelper = dbContext.GetService<ISqlGenerationHelper>();
        string Column(string property) => sqlHelper.DelimitIdentifier(entityType.FindProperty(property)!.GetColumnName(table)!);

        /* The tenant condition keeps a foreign id from locking another tenant's row in a shared database; ids in a fixed
         * order so two bulk edits never deadlock. */
        var sql = $"SELECT 1 FROM {sqlHelper.DelimitIdentifier(table.Name, table.Schema)} " +
                  $"WHERE {Column(nameof(NumberSequence.Id))} = ANY(@ids) " +
                  $"AND {Column(nameof(NumberSequence.TenantId))} IS NOT DISTINCT FROM @tenantId " +
                  $"ORDER BY {Column(nameof(NumberSequence.Id))} " +
                  (exclusive ? "FOR UPDATE" : "FOR SHARE");

        var typeMappings = dbContext.GetService<IRelationalTypeMappingSource>();
        await database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.Transaction = database.CurrentTransaction?.GetDbTransaction();
            command.Parameters.Add(Parameter(typeMappings, command, "@ids", typeof(Guid[]), numberSequenceIds.OrderBy(id => id).ToArray()));
            command.Parameters.Add(Parameter(typeMappings, command, "@tenantId", typeof(Guid), tenantId));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
            }
        }
        finally
        {
            await database.CloseConnectionAsync();
        }
    }

    private static string BuildUpsertSql(DbContext dbContext)
    {
        var entityType = dbContext.Model.FindEntityType(typeof(NumberSequenceCounter))
                         ?? throw new InvalidOperationException("NumberSequenceCounter is not mapped by the Platform context.");
        var tableName = entityType.GetTableName()!;
        var schema = entityType.GetSchema();
        var table = StoreObjectIdentifier.Table(tableName, schema);
        var sqlHelper = dbContext.GetService<ISqlGenerationHelper>();

        string Column(string property) => sqlHelper.DelimitIdentifier(entityType.FindProperty(property)!.GetColumnName(table)!);

        var tenantId = Column(nameof(NumberSequenceCounter.TenantId));
        var numberSequenceId = Column(nameof(NumberSequenceCounter.NumberSequenceId));
        var periodYear = Column(nameof(NumberSequenceCounter.PeriodYear));
        var periodMonth = Column(nameof(NumberSequenceCounter.PeriodMonth));
        var lastValue = Column(nameof(NumberSequenceCounter.LastValue));

        return $"INSERT INTO {sqlHelper.DelimitIdentifier(tableName, schema)} AS c ({tenantId}, {numberSequenceId}, {periodYear}, {periodMonth}, {lastValue}) " +
               "VALUES (@tenantId, @numberSequenceId, @periodYear, @periodMonth, 1) " +
               $"ON CONFLICT ({numberSequenceId}, {periodYear}, {periodMonth}) DO UPDATE SET {lastValue} = c.{lastValue} + 1 " +
               $"RETURNING {lastValue}";
    }

    private static DbParameter Parameter(IRelationalTypeMappingSource typeMappings, DbCommand command, string name, Type type, object? value)
    {
        var mapping = typeMappings.FindMapping(type)
                      ?? throw new InvalidOperationException($"No relational type mapping for {type.Name}.");
        return mapping.CreateParameter(command, name, value, nullable: true);
    }
}
