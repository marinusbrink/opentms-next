using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTms.Platform.Events.Outbox;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Maintenance.Databases;
using OpenTms.Platform.Maintenance.Outbox;
using OpenTms.Platform.Permissions;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.Administration.Maintenance;

/* The Outbox tab. On the host side it lists the outbox of every database (the host database and
 * each tenant database); on the tenant side only the tenant's own. Tenant isolation holds by
 * construction: a tenant's row is read inside its own tenant scope, and a tenant may dispatch
 * nothing but its own outbox. Counting the waiting events visits each listed database, so a page
 * of the host list costs one short query per row on that page (bounded by the page size), never
 * one per tenant in the system. */
[RemoteService(IsEnabled = false)]
[Authorize(PlatformPermissions.Administration.Maintenance.Outbox)]
public class OutboxMaintenanceAppService : MaintenanceAppServiceBase, IOutboxMaintenanceAppService
{
    public const string HostKey = "host";

    private readonly IOutboxInspector _inspector;
    private readonly IOutboxDispatcher _dispatcher;
    private readonly IOutboxDispatchRegistry _registry;
    private readonly IDatabaseStatisticsProvider _statisticsProvider;
    private readonly ITenantStore _tenantStore;
    private readonly AbpDbConnectionOptions _connectionOptions;
    private readonly OutboxOptions _outboxOptions;

    public OutboxMaintenanceAppService(
        IOutboxInspector inspector,
        IOutboxDispatcher dispatcher,
        IOutboxDispatchRegistry registry,
        IDatabaseStatisticsProvider statisticsProvider,
        ITenantStore tenantStore,
        IOptions<AbpDbConnectionOptions> connectionOptions,
        IOptions<OutboxOptions> outboxOptions)
    {
        _inspector = inspector;
        _dispatcher = dispatcher;
        _registry = registry;
        _statisticsProvider = statisticsProvider;
        _tenantStore = tenantStore;
        _connectionOptions = connectionOptions.Value;
        _outboxOptions = outboxOptions.Value;
    }

    public Task<OutboxOverviewDto> GetOverviewAsync() =>
        Task.FromResult(new OutboxOverviewDto
        {
            DispatchEnabled = _outboxOptions.DispatchEnabled,
            SweepPeriod = _outboxOptions.SweepPeriod,
            BatchSize = _outboxOptions.BatchSize,
            InstanceName = Environment.MachineName
        });

    public async Task<GridResponse<OutboxRowDto>> GetListAsync(GridRequest request)
    {
        var shells = await BuildShellsAsync();

        IReadOnlyList<OutboxRowDto> filtered = shells;
        if (!string.IsNullOrWhiteSpace(request.WildcardSearch))
        {
            var search = request.WildcardSearch.Trim();
            filtered = shells.Where(r => Matches(r.DatabaseName, search) || Matches(r.TenantName, search)).ToList();
        }

        var sort = request.SortModels.FirstOrDefault();
        if (sort != null)
        {
            filtered = sort.ColId switch
            {
                "databaseName" => Sort(filtered, r => r.DatabaseName, sort.Sort),
                "tenantName" => Sort(filtered, r => r.TenantName ?? string.Empty, sort.Sort),
                _ => filtered
            };
        }

        var page = Page(shells, filtered, request);

        /* Only the rows on this page are measured. */
        var measured = new List<OutboxRowDto>(page.Rows.Count);
        foreach (var row in page.Rows)
        {
            measured.Add(await MeasureAsync(row));
        }

        return page with { Rows = measured };
    }

    [Authorize(PlatformPermissions.Administration.Maintenance.OutboxDispatch)]
    public async Task<OutboxDispatchResultDto> DispatchAsync(Guid? tenantId)
    {
        if (CurrentTenant.IsAvailable)
        {
            if (tenantId != CurrentTenant.Id)
            {
                throw new AbpAuthorizationException("A tenant may only dispatch its own outbox.");
            }

            var own = await _tenantStore.FindAsync(CurrentTenant.Id!.Value);
            if (own == null || !HasOwnDatabase(own))
            {
                /* The shared-tier outbox is the host's; dispatching it would publish other tenants' events too. */
                throw new AbpAuthorizationException("The outbox of a shared-tier tenant is dispatched by the host.");
            }
        }
        else if (tenantId != null)
        {
            var tenant = await _tenantStore.FindAsync(tenantId.Value);
            if (tenant == null || !HasOwnDatabase(tenant))
            {
                throw new BusinessException("OpenTms.Platform:OutboxTenantNotFound")
                    .WithData("tenantId", tenantId);
            }
        }

        var published = await _dispatcher.DispatchAsync(tenantId);
        Logger.LogInformation("Outbox of tenant {TenantId} dispatched on request of user {UserId}: {Count} events.", tenantId, CurrentUser.Id, published);
        return new OutboxDispatchResultDto { TenantId = tenantId, PublishedCount = published };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool HasOwnDatabase(TenantConfiguration tenant) =>
        !string.IsNullOrWhiteSpace(tenant.ConnectionStrings?.Default);

    private string HostDatabaseName() =>
        _statisticsProvider.ParseTarget(_connectionOptions.ConnectionStrings.Default
            ?? throw new AbpException("The host database has no Default connection string.")).DatabaseName;

    /// <summary>The rows without figures: the host database first, then the tenant databases by tenant name.</summary>
    private async Task<IReadOnlyList<OutboxRowDto>> BuildShellsAsync()
    {
        if (CurrentTenant.IsAvailable)
        {
            var own = await _tenantStore.FindAsync(CurrentTenant.Id!.Value);
            if (own == null)
            {
                return Array.Empty<OutboxRowDto>();
            }

            var hasOwnDatabase = HasOwnDatabase(own);
            return
            [
                new OutboxRowDto
                {
                    Key = own.Id.ToString("D"),
                    TenantId = own.Id,
                    TenantName = own.Name,
                    DatabaseName = hasOwnDatabase ? _statisticsProvider.ParseTarget(own.ConnectionStrings!.Default!).DatabaseName : string.Empty,
                    IsHost = false,
                    IsSharedTier = !hasOwnDatabase
                }
            ];
        }

        var tenants = await _tenantStore.GetListAsync(includeDetails: true);
        var rows = new List<OutboxRowDto>
        {
            new()
            {
                Key = HostKey,
                DatabaseName = HostDatabaseName(),
                IsHost = true,
                SharedTenantCount = tenants.Count(t => !HasOwnDatabase(t))
            }
        };
        rows.AddRange(tenants
            .Where(HasOwnDatabase)
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => new OutboxRowDto
            {
                Key = t.Id.ToString("D"),
                TenantId = t.Id,
                TenantName = t.Name,
                DatabaseName = _statisticsProvider.ParseTarget(t.ConnectionStrings!.Default!).DatabaseName,
                IsHost = false
            }));
        return rows;
    }

    private async Task<OutboxRowDto> MeasureAsync(OutboxRowDto row)
    {
        if (row.IsSharedTier)
        {
            return row with { IsReachable = false };
        }

        var record = _registry.Find(row.TenantId);
        var withRecord = row with
        {
            LastRunAt = record?.LastRunAt,
            LastPublishedCount = record?.LastPublishedCount,
            LastError = record?.LastError,
            LastErrorAt = record?.LastErrorAt
        };

        try
        {
            var snapshot = await _inspector.InspectAsync(row.TenantId);
            return withRecord with
            {
                IsReachable = true,
                PendingCount = snapshot.PendingCount,
                OldestPendingAt = snapshot.OldestPendingAt
            };
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "The outbox of tenant {TenantId} could not be read.", row.TenantId);
            return withRecord with { IsReachable = false };
        }
    }
}
