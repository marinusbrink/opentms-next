using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Platform.Maintenance.Databases;

/// <summary>
/// One database as the maintenance view addresses it: a connection string, the database name it
/// carries and the server (host and port) it points at. Only the last two ever leave the backend.
/// </summary>
public sealed record DatabaseTarget(string ConnectionString, string DatabaseName, string ServerName);

/// <summary>
/// Server-level facts about one database, read from the catalog of its server without connecting
/// to the database itself (one query per server covers every database on it).
/// </summary>
public sealed record DatabaseStatistics(
    string DatabaseName,
    long SizeBytes,
    int ActiveConnections,
    long TransactionsCommitted,
    long TransactionsRolledBack,
    long BlocksRead,
    long BlocksHit,
    long RowsReturned,
    long RowsFetched,
    long RowsInserted,
    long RowsUpdated,
    long RowsDeleted,
    long Deadlocks,
    long TempBytes,
    DateTime? StatisticsResetAt)
{
    /// <summary>Share of block reads served from the buffer cache; null before any read happened.</summary>
    public double? CacheHitRatio =>
        BlocksRead + BlocksHit == 0 ? null : (double)BlocksHit / (BlocksRead + BlocksHit);
}

public sealed record SchemaSize(string SchemaName, long SizeBytes, int TableCount);

public sealed record TableStatistics(
    string SchemaName,
    string TableName,
    long TotalSizeBytes,
    long LiveRows,
    long DeadRows,
    long SequentialScans,
    long IndexScans,
    DateTime? LastVacuumAt,
    DateTime? LastAnalyzeAt);

/// <summary>Size of the database blob store (ABP's AbpBlobs table) inside one database.</summary>
public sealed record BlobStorageStatistics(long BlobCount, long ContentBytes, long TableSizeBytes);

/// <summary>What one connection to the database itself can tell: schemas, the largest tables, the blob store.</summary>
public sealed record DatabaseDetails(
    IReadOnlyList<SchemaSize> Schemas,
    IReadOnlyList<TableStatistics> LargestTables,
    BlobStorageStatistics? BlobStorage);

/// <summary>
/// The port behind the Databases tab. Reading is split in two so that listing a thousand tenant
/// databases costs one connection per server, not one per database (04-data-and-tenancy:
/// connections are the constraint); the per-database details are read on demand for one
/// database at a time.
/// </summary>
public interface IDatabaseStatisticsProvider
{
    /// <summary>Reads the database and server names out of a connection string.</summary>
    DatabaseTarget ParseTarget(string connectionString);

    /// <summary>
    /// Statistics for the given databases, grouped by the server they live on: one connection and
    /// one catalog query per distinct server. A database that is unreachable or missing is absent
    /// from the result.
    /// </summary>
    Task<IReadOnlyDictionary<string, DatabaseStatistics>> GetStatisticsAsync(
        IReadOnlyCollection<DatabaseTarget> targets,
        CancellationToken cancellationToken = default);

    /// <summary>Connects to one database and reads its schemas, largest tables and blob store.</summary>
    Task<DatabaseDetails> GetDetailsAsync(DatabaseTarget target, int largestTableCount, CancellationToken cancellationToken = default);
}
