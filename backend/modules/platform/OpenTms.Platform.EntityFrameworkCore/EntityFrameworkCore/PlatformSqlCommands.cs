using System;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace OpenTms.Platform.EntityFrameworkCore;

/// <summary>
/// The few atomic statements Platform runs on its own tables (number-sequence counters, derived-value states; designs
/// 49 and 50): parameterised commands on the context's connection and current transaction, the table name taken from
/// the EF model. PostgreSQL in production; the SQLite unit-test provider accepts the same statements (it has no schemas
/// and stores a Guid as upper-case text).
/// </summary>
internal static class PlatformSqlCommands
{
    private const string SqliteProvider = "Microsoft.EntityFrameworkCore.Sqlite";

    public static bool IsSqlite(DbContext db) => db.Database.ProviderName == SqliteProvider;

    public static string TableName<TEntity>(DbContext db)
    {
        var entity = db.Model.FindEntityType(typeof(TEntity))!;
        var name = "\"" + entity.GetTableName() + "\"";
        var schema = entity.GetSchema();
        return schema == null || IsSqlite(db) ? name : "\"" + schema + "\"." + name;
    }

    public static async Task<DbCommand> CreateAsync(DbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync();
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        return command;
    }

    public static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    public static object GuidValue(DbContext db, Guid value) => IsSqlite(db) ? value.ToString().ToUpperInvariant() : value;

    public static object GuidValue(DbContext db, Guid? value) => value is { } id ? GuidValue(db, id) : DBNull.Value;
}
