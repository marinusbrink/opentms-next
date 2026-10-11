using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OpenTms.Platform.Maintenance.Databases;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Platform.EntityFrameworkCore.Maintenance;

/// <summary>
/// <see cref="IDatabaseStatisticsProvider"/> for PostgreSQL. The list query reads <c>pg_database</c>
/// and <c>pg_stat_database</c> on each distinct server (the server-wide catalog knows every
/// database on it); the detail query connects to the one database asked for. Raw SQL is used
/// deliberately: these are catalog views, not entities, and no module maps them
/// (coding conventions: documented exception).
/// </summary>
public class PostgreSqlDatabaseStatisticsProvider : IDatabaseStatisticsProvider, ITransientDependency
{
    public ILogger<PostgreSqlDatabaseStatisticsProvider> Logger { get; set; } = NullLogger<PostgreSqlDatabaseStatisticsProvider>.Instance;

    private const string StatisticsSql = """
        SELECT d.datname,
               pg_database_size(d.datname),
               s.numbackends,
               s.xact_commit,
               s.xact_rollback,
               s.blks_read,
               s.blks_hit,
               s.tup_returned,
               s.tup_fetched,
               s.tup_inserted,
               s.tup_updated,
               s.tup_deleted,
               s.deadlocks,
               s.temp_bytes,
               s.stats_reset
        FROM pg_database d
        LEFT JOIN pg_stat_database s ON s.datid = d.oid
        WHERE d.datname = ANY(@names)
        """;

    private const string SchemasSql = """
        SELECT n.nspname,
               COALESCE(SUM(pg_total_relation_size(c.oid)), 0)::bigint,
               COUNT(c.oid) FILTER (WHERE c.relkind IN ('r', 'p'))::int
        FROM pg_namespace n
        LEFT JOIN pg_class c ON c.relnamespace = n.oid AND c.relkind IN ('r', 'p', 'm')
        WHERE n.nspname NOT IN ('pg_catalog', 'information_schema', 'pg_toast')
          AND n.nspname NOT LIKE 'pg_temp_%'
          AND n.nspname NOT LIKE 'pg_toast_temp_%'
        GROUP BY n.nspname
        ORDER BY 2 DESC, n.nspname
        """;

    private const string TablesSql = """
        SELECT s.schemaname,
               s.relname,
               pg_total_relation_size(s.relid),
               s.n_live_tup,
               s.n_dead_tup,
               s.seq_scan,
               COALESCE(s.idx_scan, 0),
               GREATEST(s.last_vacuum, s.last_autovacuum),
               GREATEST(s.last_analyze, s.last_autoanalyze)
        FROM pg_stat_user_tables s
        ORDER BY pg_total_relation_size(s.relid) DESC, s.schemaname, s.relname
        LIMIT @limit
        """;

    /* The blob store is ABP's AbpBlobs table in the default schema of the application context;
     * to_regclass keeps the query harmless in a database that does not have it. */
    private const string BlobsSql = """
        SELECT COUNT(*)::bigint,
               COALESCE(SUM(octet_length("Content")), 0)::bigint,
               pg_total_relation_size('public."AbpBlobs"')
        FROM public."AbpBlobs"
        """;

    private const string BlobsExistSql = """SELECT to_regclass('public."AbpBlobs"') IS NOT NULL""";

    public DatabaseTarget ParseTarget(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var host = string.IsNullOrEmpty(builder.Host) ? "localhost" : builder.Host;
        var port = builder.Port == 0 ? 5432 : builder.Port;
        return new DatabaseTarget(connectionString, builder.Database ?? string.Empty, $"{host}:{port}");
    }

    public async Task<IReadOnlyDictionary<string, DatabaseStatistics>> GetStatisticsAsync(
        IReadOnlyCollection<DatabaseTarget> targets,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, DatabaseStatistics>(StringComparer.Ordinal);

        /* One connection per distinct server, never one per database (04-data-and-tenancy). */
        foreach (var serverGroup in targets.GroupBy(ServerKeyOf))
        {
            var names = serverGroup.Select(t => t.DatabaseName).Distinct(StringComparer.Ordinal).ToArray();
            try
            {
                await using var connection = new NpgsqlConnection(serverGroup.First().ConnectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = new NpgsqlCommand(StatisticsSql, connection);
                command.Parameters.AddWithValue("names", names);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var statistics = new DatabaseStatistics(
                        DatabaseName: reader.GetString(0),
                        SizeBytes: reader.GetInt64(1),
                        ActiveConnections: reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                        TransactionsCommitted: LongOrZero(reader, 3),
                        TransactionsRolledBack: LongOrZero(reader, 4),
                        BlocksRead: LongOrZero(reader, 5),
                        BlocksHit: LongOrZero(reader, 6),
                        RowsReturned: LongOrZero(reader, 7),
                        RowsFetched: LongOrZero(reader, 8),
                        RowsInserted: LongOrZero(reader, 9),
                        RowsUpdated: LongOrZero(reader, 10),
                        RowsDeleted: LongOrZero(reader, 11),
                        Deadlocks: LongOrZero(reader, 12),
                        TempBytes: LongOrZero(reader, 13),
                        StatisticsResetAt: reader.IsDBNull(14) ? null : reader.GetDateTime(14));
                    result[statistics.DatabaseName] = statistics;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                /* An unreachable server leaves its databases out of the list; the view shows them
                 * without figures. The server address is not logged (it may carry credentials). */
                Logger.LogWarning(ex, "Database statistics could not be read for {Count} database(s) on one server.", names.Length);
            }
        }

        return result;
    }

    public async Task<DatabaseDetails> GetDetailsAsync(DatabaseTarget target, int largestTableCount, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(target.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var schemas = new List<SchemaSize>();
        await using (var command = new NpgsqlCommand(SchemasSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                schemas.Add(new SchemaSize(reader.GetString(0), reader.GetInt64(1), reader.GetInt32(2)));
            }
        }

        var tables = new List<TableStatistics>();
        await using (var command = new NpgsqlCommand(TablesSql, connection))
        {
            command.Parameters.AddWithValue("limit", largestTableCount);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(new TableStatistics(
                    SchemaName: reader.GetString(0),
                    TableName: reader.GetString(1),
                    TotalSizeBytes: reader.GetInt64(2),
                    LiveRows: LongOrZero(reader, 3),
                    DeadRows: LongOrZero(reader, 4),
                    SequentialScans: LongOrZero(reader, 5),
                    IndexScans: LongOrZero(reader, 6),
                    LastVacuumAt: reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    LastAnalyzeAt: reader.IsDBNull(8) ? null : reader.GetDateTime(8)));
            }
        }

        BlobStorageStatistics? blobs = null;
        await using (var existsCommand = new NpgsqlCommand(BlobsExistSql, connection))
        {
            if (await existsCommand.ExecuteScalarAsync(cancellationToken) is true)
            {
                await using var command = new NpgsqlCommand(BlobsSql, connection);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    blobs = new BlobStorageStatistics(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
                }
            }
        }

        return new DatabaseDetails(schemas, tables, blobs);
    }

    /// <summary>Everything in the connection string except the database: the server a target lives on.</summary>
    internal static string ServerKeyOf(DatabaseTarget target)
    {
        var builder = new NpgsqlConnectionStringBuilder(target.ConnectionString) { Database = null };
        return builder.ConnectionString;
    }

    private static long LongOrZero(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? 0 : reader.GetInt64(ordinal);
}
