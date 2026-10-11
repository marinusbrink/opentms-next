using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Maintenance.Databases;
using OpenTms.Platform.Permissions;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.Administration.Maintenance;

/* The Databases tab. Host only: it lists every database the host knows (the host database and the
 * database of every tenant with a connection string of its own) with the server's statistics.
 * Tenants without a connection string live in the host database and are counted on its row. */
[RemoteService(IsEnabled = false)]
[Authorize(PlatformPermissions.Administration.Maintenance.Databases)]
public class DatabaseMaintenanceAppService : MaintenanceAppServiceBase, IDatabaseMaintenanceAppService
{
    public const string HostKey = "host";
    private const int LargestTableCount = 20;

    private readonly IDatabaseStatisticsProvider _statisticsProvider;
    private readonly ITenantStore _tenantStore;
    private readonly AbpDbConnectionOptions _connectionOptions;

    public DatabaseMaintenanceAppService(
        IDatabaseStatisticsProvider statisticsProvider,
        ITenantStore tenantStore,
        IOptions<AbpDbConnectionOptions> connectionOptions)
    {
        _statisticsProvider = statisticsProvider;
        _tenantStore = tenantStore;
        _connectionOptions = connectionOptions.Value;
    }

    public async Task<GridResponse<DatabaseRowDto>> GetListAsync(GridRequest request)
    {
        EnsureHostSide();
        var all = await BuildRowsAsync();

        IReadOnlyList<DatabaseRowDto> filtered = all;
        if (!string.IsNullOrWhiteSpace(request.WildcardSearch))
        {
            var search = request.WildcardSearch.Trim();
            filtered = all.Where(r => Matches(r.DatabaseName, search) || Matches(r.TenantName, search) || Matches(r.ServerName, search)).ToList();
        }

        var sort = request.SortModels.FirstOrDefault();
        if (sort != null)
        {
            filtered = sort.ColId switch
            {
                "databaseName" => Sort(filtered, r => r.DatabaseName, sort.Sort),
                "tenantName" => Sort(filtered, r => r.TenantName ?? string.Empty, sort.Sort),
                "serverName" => Sort(filtered, r => r.ServerName, sort.Sort),
                "sizeBytes" => Sort(filtered, r => r.SizeBytes ?? -1, sort.Sort),
                "activeConnections" => Sort(filtered, r => r.ActiveConnections ?? -1, sort.Sort),
                "cacheHitRatio" => Sort(filtered, r => r.CacheHitRatio ?? -1, sort.Sort),
                "transactionsCommitted" => Sort(filtered, r => r.TransactionsCommitted ?? -1, sort.Sort),
                "transactionsRolledBack" => Sort(filtered, r => r.TransactionsRolledBack ?? -1, sort.Sort),
                "deadlocks" => Sort(filtered, r => r.Deadlocks ?? -1, sort.Sort),
                "tempBytes" => Sort(filtered, r => r.TempBytes ?? -1, sort.Sort),
                _ => filtered
            };
        }

        return Page(all, filtered, request);
    }

    public async Task<DatabasesSummaryDto> GetSummaryAsync()
    {
        EnsureHostSide();
        var rows = await BuildRowsAsync();
        var host = rows.First(r => r.IsHost);

        BlobStorageDto? hostBlobs = null;
        try
        {
            var details = await _statisticsProvider.GetDetailsAsync(HostTarget(), largestTableCount: 0);
            hostBlobs = ToDto(details.BlobStorage);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "The host database's blob store could not be measured.");
        }

        return new DatabasesSummaryDto
        {
            DatabaseCount = rows.Count,
            TenantDatabaseCount = rows.Count - 1,
            SharedTenantCount = host.SharedTenantCount,
            TotalSizeBytes = rows.Sum(r => r.SizeBytes ?? 0),
            UnreachableCount = rows.Count(r => !r.IsReachable),
            HostBlobStorage = hostBlobs
        };
    }

    public async Task<DatabaseDetailDto> GetAsync(string key)
    {
        EnsureHostSide();
        var (target, isHost, tenantName) = await ResolveTargetAsync(key);
        var tenants = await _tenantStore.GetListAsync(includeDetails: true);

        var statistics = await _statisticsProvider.GetStatisticsAsync([target]);
        var details = await _statisticsProvider.GetDetailsAsync(target, LargestTableCount);

        return new DatabaseDetailDto
        {
            Key = key,
            DatabaseName = target.DatabaseName,
            ServerName = target.ServerName,
            IsHost = isHost,
            TenantName = tenantName,
            SharedTenantNames = isHost ? tenants.Where(t => !HasOwnDatabase(t)).Select(t => t.Name).OrderBy(n => n).ToList() : new List<string>(),
            SizeBytes = statistics.TryGetValue(target.DatabaseName, out var s) ? s.SizeBytes : null,
            Schemas = details.Schemas.Select(x => new DatabaseSchemaDto { SchemaName = x.SchemaName, SizeBytes = x.SizeBytes, TableCount = x.TableCount }).ToList(),
            LargestTables = details.LargestTables.Select(x => new DatabaseTableDto
            {
                SchemaName = x.SchemaName,
                TableName = x.TableName,
                TotalSizeBytes = x.TotalSizeBytes,
                LiveRows = x.LiveRows,
                DeadRows = x.DeadRows,
                SequentialScans = x.SequentialScans,
                IndexScans = x.IndexScans,
                LastVacuumAt = x.LastVacuumAt,
                LastAnalyzeAt = x.LastAnalyzeAt
            }).ToList(),
            BlobStorage = ToDto(details.BlobStorage)
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private DatabaseTarget HostTarget() =>
        _statisticsProvider.ParseTarget(_connectionOptions.ConnectionStrings.Default
            ?? throw new AbpException("The host database has no Default connection string."));

    private static bool HasOwnDatabase(TenantConfiguration tenant) =>
        !string.IsNullOrWhiteSpace(tenant.ConnectionStrings?.Default);

    private async Task<(DatabaseTarget Target, bool IsHost, string? TenantName)> ResolveTargetAsync(string key)
    {
        if (string.Equals(key, HostKey, StringComparison.OrdinalIgnoreCase))
        {
            return (HostTarget(), true, null);
        }

        if (!Guid.TryParse(key, out var tenantId))
        {
            throw new EntityNotFoundException(typeof(DatabaseDetailDto), key);
        }

        var tenant = await _tenantStore.FindAsync(tenantId);
        if (tenant == null || !HasOwnDatabase(tenant))
        {
            /* A shared-tier tenant has no database of its own: its data is the host database's. */
            throw new EntityNotFoundException(typeof(DatabaseDetailDto), key);
        }

        return (_statisticsProvider.ParseTarget(tenant.ConnectionStrings!.Default!), false, tenant.Name);
    }

    /// <summary>The host row first, then the tenant databases by tenant name; one catalog query per server.</summary>
    private async Task<IReadOnlyList<DatabaseRowDto>> BuildRowsAsync()
    {
        var tenants = await _tenantStore.GetListAsync(includeDetails: true);
        var hostTarget = HostTarget();

        var tenantTargets = tenants
            .Where(HasOwnDatabase)
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => (Tenant: t, Target: _statisticsProvider.ParseTarget(t.ConnectionStrings!.Default!)))
            .ToList();

        var targets = new List<DatabaseTarget> { hostTarget };
        targets.AddRange(tenantTargets.Select(x => x.Target));
        var statistics = await _statisticsProvider.GetStatisticsAsync(targets);

        var rows = new List<DatabaseRowDto>
        {
            ToRow(HostKey, hostTarget, isHost: true, tenantId: null, tenantName: null,
                sharedTenantCount: tenants.Count(t => !HasOwnDatabase(t)),
                statistics.GetValueOrDefault(hostTarget.DatabaseName))
        };
        rows.AddRange(tenantTargets.Select(x =>
            ToRow(x.Tenant.Id.ToString("D"), x.Target, isHost: false, x.Tenant.Id, x.Tenant.Name, sharedTenantCount: 0,
                statistics.GetValueOrDefault(x.Target.DatabaseName))));
        return rows;
    }

    private static DatabaseRowDto ToRow(string key, DatabaseTarget target, bool isHost, Guid? tenantId, string? tenantName, int sharedTenantCount, DatabaseStatistics? s) =>
        new()
        {
            Key = key,
            DatabaseName = target.DatabaseName,
            ServerName = target.ServerName,
            IsHost = isHost,
            TenantId = tenantId,
            TenantName = tenantName,
            SharedTenantCount = sharedTenantCount,
            IsReachable = s != null,
            SizeBytes = s?.SizeBytes,
            ActiveConnections = s?.ActiveConnections,
            CacheHitRatio = s?.CacheHitRatio,
            TransactionsCommitted = s?.TransactionsCommitted,
            TransactionsRolledBack = s?.TransactionsRolledBack,
            RowsReturned = s?.RowsReturned,
            RowsFetched = s?.RowsFetched,
            RowsInserted = s?.RowsInserted,
            RowsUpdated = s?.RowsUpdated,
            RowsDeleted = s?.RowsDeleted,
            Deadlocks = s?.Deadlocks,
            TempBytes = s?.TempBytes,
            StatisticsResetAt = s?.StatisticsResetAt
        };

    private static BlobStorageDto? ToDto(BlobStorageStatistics? blobs) =>
        blobs == null ? null : new BlobStorageDto { BlobCount = blobs.BlobCount, ContentBytes = blobs.ContentBytes, TableSizeBytes = blobs.TableSizeBytes };
}
