using System;
using System.Collections.Generic;

namespace OpenTms.Platform.Administration.Maintenance;

// ── Caches ──────────────────────────────────────────────────────────────────────────

public record CacheStatusDto
{
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>True while the cache lives inside one process: clearing it clears this instance only.</summary>
    public bool IsPerInstance { get; init; }

    public int? EntryCount { get; init; }

    public string? KeyPrefix { get; init; }

    /// <summary>The instance that answered (machine name); relevant while the cache is per instance.</summary>
    public string InstanceName { get; init; } = string.Empty;
}

public record CacheClearResultDto
{
    public int? ClearedEntryCount { get; init; }

    public string InstanceName { get; init; } = string.Empty;
}

/// <summary>One named cache (ABP's cache name, the "c:" part of the key) with what it holds.</summary>
public record CacheNameDto
{
    public string Name { get; init; } = string.Empty;

    public int EntryCount { get; init; }

    public long SizeBytes { get; init; }
}

/// <summary>One cache entry of this instance.</summary>
public record CacheEntryRowDto
{
    /// <summary>The full key as stored; the key of the remove endpoint.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>ABP's cache name, parsed from the key; the raw key when it does not follow ABP's format.</summary>
    public string CacheName { get; init; } = string.Empty;

    /// <summary>The item's own key within its cache, parsed from the full key.</summary>
    public string ItemKey { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? AbsoluteExpiresAt { get; init; }

    public TimeSpan? SlidingExpiration { get; init; }
}

// ── Databases ────────────────────────────────────────────────────────────────────────

public record BlobStorageDto
{
    public long BlobCount { get; init; }

    public long ContentBytes { get; init; }

    public long TableSizeBytes { get; init; }
}

public record DatabaseRowDto
{
    /// <summary>"host" for the host database, otherwise the tenant id. The key of the detail endpoint.</summary>
    public string Key { get; init; } = string.Empty;

    public string DatabaseName { get; init; } = string.Empty;

    /// <summary>Server host and port, never the full connection string.</summary>
    public string ServerName { get; init; } = string.Empty;

    public bool IsHost { get; init; }

    public Guid? TenantId { get; init; }

    public string? TenantName { get; init; }

    /// <summary>Host database only: how many shared-tier tenants (without a database of their own) live in it.</summary>
    public int SharedTenantCount { get; init; }

    /// <summary>False when the server could not be reached or the database does not exist; the figures are then null.</summary>
    public bool IsReachable { get; init; }

    public long? SizeBytes { get; init; }

    public int? ActiveConnections { get; init; }

    /// <summary>0..1: the share of block reads served from the buffer cache.</summary>
    public double? CacheHitRatio { get; init; }

    public long? TransactionsCommitted { get; init; }

    public long? TransactionsRolledBack { get; init; }

    public long? RowsReturned { get; init; }

    public long? RowsFetched { get; init; }

    public long? RowsInserted { get; init; }

    public long? RowsUpdated { get; init; }

    public long? RowsDeleted { get; init; }

    public long? Deadlocks { get; init; }

    public long? TempBytes { get; init; }

    public DateTime? StatisticsResetAt { get; init; }
}

public record DatabaseSchemaDto
{
    public string SchemaName { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public int TableCount { get; init; }
}

public record DatabaseTableDto
{
    public string SchemaName { get; init; } = string.Empty;

    public string TableName { get; init; } = string.Empty;

    public long TotalSizeBytes { get; init; }

    public long LiveRows { get; init; }

    public long DeadRows { get; init; }

    public long SequentialScans { get; init; }

    public long IndexScans { get; init; }

    public DateTime? LastVacuumAt { get; init; }

    public DateTime? LastAnalyzeAt { get; init; }
}

public record DatabaseDetailDto
{
    public string Key { get; init; } = string.Empty;

    public string DatabaseName { get; init; } = string.Empty;

    public string ServerName { get; init; } = string.Empty;

    public bool IsHost { get; init; }

    public string? TenantName { get; init; }

    /// <summary>Host database only: the names of the shared-tier tenants living in it.</summary>
    public List<string> SharedTenantNames { get; init; } = new();

    public long? SizeBytes { get; init; }

    public List<DatabaseSchemaDto> Schemas { get; init; } = new();

    public List<DatabaseTableDto> LargestTables { get; init; } = new();

    /// <summary>Null when the database has no blob store table.</summary>
    public BlobStorageDto? BlobStorage { get; init; }
}

public record DatabasesSummaryDto
{
    public int DatabaseCount { get; init; }

    public int TenantDatabaseCount { get; init; }

    public int SharedTenantCount { get; init; }

    /// <summary>Sum over the reachable databases.</summary>
    public long TotalSizeBytes { get; init; }

    public int UnreachableCount { get; init; }

    /// <summary>The blob store of the host database (the one database the host is always connected to).</summary>
    public BlobStorageDto? HostBlobStorage { get; init; }
}

// ── Outbox ───────────────────────────────────────────────────────────────────────────

public record OutboxOverviewDto
{
    /// <summary>Whether this instance runs the dispatcher (Outbox:DispatchEnabled).</summary>
    public bool DispatchEnabled { get; init; }

    public TimeSpan SweepPeriod { get; init; }

    public int BatchSize { get; init; }

    public string InstanceName { get; init; } = string.Empty;
}

public record OutboxRowDto
{
    /// <summary>"host" for the host database, otherwise the tenant id.</summary>
    public string Key { get; init; } = string.Empty;

    public Guid? TenantId { get; init; }

    public string? TenantName { get; init; }

    public string DatabaseName { get; init; } = string.Empty;

    public bool IsHost { get; init; }

    /// <summary>Host row: how many shared-tier tenants write their events into this outbox.</summary>
    public int SharedTenantCount { get; init; }

    /// <summary>
    /// Tenant side only: the tenant has no database of its own, so its outbox is the host's and the
    /// figures are available to the host only.
    /// </summary>
    public bool IsSharedTier { get; init; }

    public bool IsReachable { get; init; }

    public long? PendingCount { get; init; }

    public DateTime? OldestPendingAt { get; init; }

    public DateTime? LastRunAt { get; init; }

    public int? LastPublishedCount { get; init; }

    public string? LastError { get; init; }

    public DateTime? LastErrorAt { get; init; }
}

public record OutboxDispatchResultDto
{
    public Guid? TenantId { get; init; }

    public int PublishedCount { get; init; }
}

// ── Health ───────────────────────────────────────────────────────────────────────────

public record HealthCheckEntryDto
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Healthy, Degraded or Unhealthy.</summary>
    public string Status { get; init; } = string.Empty;

    public string? Description { get; init; }

    public double DurationMilliseconds { get; init; }

    public List<string> Tags { get; init; } = new();

    public string? Error { get; init; }
}

public record ProcessInfoDto
{
    public string ApplicationName { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string Environment { get; init; } = string.Empty;

    public string MachineName { get; init; } = string.Empty;

    public DateTime StartedAt { get; init; }

    public double UptimeSeconds { get; init; }

    public string RuntimeVersion { get; init; } = string.Empty;

    public long WorkingSetBytes { get; init; }

    public long ManagedMemoryBytes { get; init; }

    public int ProcessorCount { get; init; }

    public bool OutboxDispatchEnabled { get; init; }
}

public record HealthReportDto
{
    /// <summary>Healthy, Degraded or Unhealthy.</summary>
    public string Status { get; init; } = string.Empty;

    public double TotalDurationMilliseconds { get; init; }

    public List<HealthCheckEntryDto> Entries { get; init; } = new();

    public ProcessInfoDto Process { get; init; } = new();
}

// ── Background jobs ──────────────────────────────────────────────────────────────────

public record BackgroundJobServerDto
{
    public string Name { get; init; } = string.Empty;

    public int WorkerCount { get; init; }

    public List<string> Queues { get; init; } = new();

    public DateTime? StartedAt { get; init; }

    public DateTime? HeartbeatAt { get; init; }
}

public record BackgroundJobQueueDto
{
    public string Name { get; init; } = string.Empty;

    public long Length { get; init; }

    public long? Fetched { get; init; }
}

public record BackgroundJobsOverviewDto
{
    /// <summary>False when no job storage is configured in this process; the counts are then zero.</summary>
    public bool IsAvailable { get; init; }

    public long Enqueued { get; init; }

    public long Scheduled { get; init; }

    public long Processing { get; init; }

    public long Succeeded { get; init; }

    public long Failed { get; init; }

    public long Deleted { get; init; }

    public long Recurring { get; init; }

    public List<BackgroundJobServerDto> Servers { get; init; } = new();

    public List<BackgroundJobQueueDto> Queues { get; init; } = new();
}

public record BackgroundJobRowDto
{
    public string Id { get; init; } = string.Empty;

    /// <summary>One of the <see cref="BackgroundJobStates"/> values.</summary>
    public string State { get; init; } = string.Empty;

    public string JobName { get; init; } = string.Empty;

    public Guid? TenantId { get; init; }

    public DateTime? StateChangedAt { get; init; }

    public DateTime? NextRunAt { get; init; }

    public string? Reason { get; init; }

    public string? ExceptionType { get; init; }

    public string? ExceptionMessage { get; init; }

    public string? Cron { get; init; }

    public string? Queue { get; init; }

    public string? ServerId { get; init; }

    public long? DurationMilliseconds { get; init; }
}

/// <summary>The job states the list endpoint accepts, as strings on the API.</summary>
public static class BackgroundJobStates
{
    public const string Enqueued = "Enqueued";
    public const string Scheduled = "Scheduled";
    public const string Processing = "Processing";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Deleted = "Deleted";
    public const string Recurring = "Recurring";

    public static readonly IReadOnlyList<string> All = new[] { Enqueued, Scheduled, Processing, Succeeded, Failed, Deleted, Recurring };
}
