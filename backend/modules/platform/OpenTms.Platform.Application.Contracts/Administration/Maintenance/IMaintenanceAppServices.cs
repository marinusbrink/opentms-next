using System;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.Platform.Administration.Maintenance;

/// <summary>The Caches tab (host only): what the application cache is and a way to clear it.</summary>
public interface ICacheMaintenanceAppService : IApplicationService
{
    Task<CacheStatusDto> GetStatusAsync();

    Task<CacheClearResultDto> ClearAsync();
}

/// <summary>The Databases tab (host only): every database the host knows, with size and server statistics.</summary>
public interface IDatabaseMaintenanceAppService : IApplicationService
{
    Task<GridResponse<DatabaseRowDto>> GetListAsync(GridRequest request);

    Task<DatabasesSummaryDto> GetSummaryAsync();

    /// <summary><paramref name="key"/> is "host" or a tenant id (see <see cref="DatabaseRowDto.Key"/>).</summary>
    Task<DatabaseDetailDto> GetAsync(string key);
}

/// <summary>
/// The Outbox tab: the host sees every database's outbox, a tenant only its own (and a shared-tier
/// tenant only that its outbox is the host's).
/// </summary>
public interface IOutboxMaintenanceAppService : IApplicationService
{
    Task<OutboxOverviewDto> GetOverviewAsync();

    Task<GridResponse<OutboxRowDto>> GetListAsync(GridRequest request);

    /// <summary>Runs the dispatcher for one database now. A tenant may only pass its own id.</summary>
    Task<OutboxDispatchResultDto> DispatchAsync(Guid? tenantId);
}

/// <summary>The Health tab (host only): the registered health checks plus facts about this process.</summary>
public interface IHealthMaintenanceAppService : IApplicationService
{
    Task<HealthReportDto> GetReportAsync();
}

/// <summary>The Background jobs tab (host only): Hangfire's counts, servers, queues and jobs per state.</summary>
public interface IBackgroundJobMaintenanceAppService : IApplicationService
{
    Task<BackgroundJobsOverviewDto> GetOverviewAsync();

    /// <summary><paramref name="state"/> is one of <see cref="BackgroundJobStates"/>.</summary>
    Task<GridResponse<BackgroundJobRowDto>> GetListAsync(GridRequest request, string state);

    Task RequeueAsync(string jobId);

    Task DeleteAsync(string jobId);
}
