using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenTms.Platform.Administration.Maintenance;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Permissions;

namespace OpenTms.Controllers.Platform;

/// <summary>
/// The Maintenance view of the Administration app: caches, databases, outbox, health and background
/// jobs (docs/designs/maintenance-view.md). Everything but the outbox is host-only; the permission
/// definitions enforce the side, the application services guard it again.
/// </summary>
[ApiController]
[Route("api/platform/administration/maintenance")]
public class MaintenanceAdministrationController : OpenTmsController
{
    private readonly ICacheMaintenanceAppService _caches;
    private readonly IDatabaseMaintenanceAppService _databases;
    private readonly IOutboxMaintenanceAppService _outbox;
    private readonly IHealthMaintenanceAppService _health;
    private readonly IBackgroundJobMaintenanceAppService _backgroundJobs;

    public MaintenanceAdministrationController(
        ICacheMaintenanceAppService caches,
        IDatabaseMaintenanceAppService databases,
        IOutboxMaintenanceAppService outbox,
        IHealthMaintenanceAppService health,
        IBackgroundJobMaintenanceAppService backgroundJobs)
    {
        _caches = caches;
        _databases = databases;
        _outbox = outbox;
        _health = health;
        _backgroundJobs = backgroundJobs;
    }

    // ── Caches ───────────────────────────────────────────────────────────────

    [HttpGet("caches")]
    [Authorize(PlatformPermissions.Administration.Maintenance.Caches)]
    [ProducesResponseType(typeof(CacheStatusDto), 200)]
    public async Task<ActionResult<CacheStatusDto>> GetCacheStatusAsync() => Ok(await _caches.GetStatusAsync());

    [HttpGet("caches/names")]
    [Authorize(PlatformPermissions.Administration.Maintenance.Caches)]
    [ProducesResponseType(typeof(List<CacheNameDto>), 200)]
    public async Task<ActionResult<List<CacheNameDto>>> GetCacheNamesAsync() => Ok(await _caches.GetNamesAsync());

    /// <summary>A page of this instance's cache entries, optionally of one named cache.</summary>
    [HttpGet("caches/entries")]
    [Authorize(PlatformPermissions.Administration.Maintenance.Caches)]
    [ProducesResponseType(typeof(GridResponse<CacheEntryRowDto>), 200)]
    public async Task<ActionResult<GridResponse<CacheEntryRowDto>>> GetCacheEntriesAsync(
        [FromQuery] int startRow = 0,
        [FromQuery] int endRow = 50,
        [FromQuery] string? wildcardSearch = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null,
        [FromQuery] string? cacheName = null)
    {
        var result = await _caches.GetEntriesAsync(BuildGridRequest(startRow, endRow, wildcardSearch, sortBy, sortDir), cacheName);
        return Ok(result);
    }

    [HttpPost("caches/clear")]
    [Authorize(PlatformPermissions.Administration.Maintenance.CachesClear)]
    [ProducesResponseType(typeof(CacheClearResultDto), 200)]
    public async Task<ActionResult<CacheClearResultDto>> ClearCachesAsync() => Ok(await _caches.ClearAsync());

    /// <summary>Removes one entry; the full key travels in the query string because it contains colons and commas.</summary>
    [HttpDelete("caches/entries")]
    [Authorize(PlatformPermissions.Administration.Maintenance.CachesClear)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> RemoveCacheEntryAsync([FromQuery] string key)
    {
        await _caches.RemoveEntryAsync(key);
        return NoContent();
    }

    // ── Databases ────────────────────────────────────────────────────────────

    [HttpGet("databases")]
    [Authorize(PlatformPermissions.Administration.Maintenance.Databases)]
    [ProducesResponseType(typeof(GridResponse<DatabaseRowDto>), 200)]
    public async Task<ActionResult<GridResponse<DatabaseRowDto>>> GetDatabasesAsync(
        [FromQuery] int startRow = 0,
        [FromQuery] int endRow = 50,
        [FromQuery] string? wildcardSearch = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null)
    {
        var result = await _databases.GetListAsync(BuildGridRequest(startRow, endRow, wildcardSearch, sortBy, sortDir));
        return Ok(result);
    }

    [HttpGet("databases/summary")]
    [Authorize(PlatformPermissions.Administration.Maintenance.Databases)]
    [ProducesResponseType(typeof(DatabasesSummaryDto), 200)]
    public async Task<ActionResult<DatabasesSummaryDto>> GetDatabasesSummaryAsync() => Ok(await _databases.GetSummaryAsync());

    [HttpGet("databases/{key}")]
    [Authorize(PlatformPermissions.Administration.Maintenance.Databases)]
    [ProducesResponseType(typeof(DatabaseDetailDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<DatabaseDetailDto>> GetDatabaseAsync(string key) => Ok(await _databases.GetAsync(key));

    // ── Outbox ───────────────────────────────────────────────────────────────

    [HttpGet("outbox/overview")]
    [Authorize(PlatformPermissions.Administration.Maintenance.Outbox)]
    [ProducesResponseType(typeof(OutboxOverviewDto), 200)]
    public async Task<ActionResult<OutboxOverviewDto>> GetOutboxOverviewAsync() => Ok(await _outbox.GetOverviewAsync());

    [HttpGet("outbox")]
    [Authorize(PlatformPermissions.Administration.Maintenance.Outbox)]
    [ProducesResponseType(typeof(GridResponse<OutboxRowDto>), 200)]
    public async Task<ActionResult<GridResponse<OutboxRowDto>>> GetOutboxesAsync(
        [FromQuery] int startRow = 0,
        [FromQuery] int endRow = 50,
        [FromQuery] string? wildcardSearch = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null)
    {
        var result = await _outbox.GetListAsync(BuildGridRequest(startRow, endRow, wildcardSearch, sortBy, sortDir));
        return Ok(result);
    }

    /// <summary>Dispatches one outbox now: the host database when <paramref name="tenantId"/> is empty.</summary>
    [HttpPost("outbox/dispatch")]
    [Authorize(PlatformPermissions.Administration.Maintenance.OutboxDispatch)]
    [ProducesResponseType(typeof(OutboxDispatchResultDto), 200)]
    public async Task<ActionResult<OutboxDispatchResultDto>> DispatchOutboxAsync([FromQuery] Guid? tenantId = null) =>
        Ok(await _outbox.DispatchAsync(tenantId));

    // ── Health ───────────────────────────────────────────────────────────────

    [HttpGet("health")]
    [Authorize(PlatformPermissions.Administration.Maintenance.Health)]
    [ProducesResponseType(typeof(HealthReportDto), 200)]
    public async Task<ActionResult<HealthReportDto>> GetHealthAsync() => Ok(await _health.GetReportAsync());

    // ── Background jobs ──────────────────────────────────────────────────────

    [HttpGet("background-jobs/overview")]
    [Authorize(PlatformPermissions.Administration.Maintenance.BackgroundJobs)]
    [ProducesResponseType(typeof(BackgroundJobsOverviewDto), 200)]
    public async Task<ActionResult<BackgroundJobsOverviewDto>> GetBackgroundJobsOverviewAsync() => Ok(await _backgroundJobs.GetOverviewAsync());

    /// <summary>A page of jobs in one state (<see cref="BackgroundJobStates"/>); Hangfire's own order, newest first.</summary>
    [HttpGet("background-jobs")]
    [Authorize(PlatformPermissions.Administration.Maintenance.BackgroundJobs)]
    [ProducesResponseType(typeof(GridResponse<BackgroundJobRowDto>), 200)]
    public async Task<ActionResult<GridResponse<BackgroundJobRowDto>>> GetBackgroundJobsAsync(
        [FromQuery] string state = BackgroundJobStates.Failed,
        [FromQuery] int startRow = 0,
        [FromQuery] int endRow = 50)
    {
        var result = await _backgroundJobs.GetListAsync(BuildGridRequest(startRow, endRow, null, null, null), state);
        return Ok(result);
    }

    [HttpPost("background-jobs/{jobId}/requeue")]
    [Authorize(PlatformPermissions.Administration.Maintenance.BackgroundJobsManage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> RequeueBackgroundJobAsync(string jobId)
    {
        await _backgroundJobs.RequeueAsync(jobId);
        return NoContent();
    }

    [HttpDelete("background-jobs/{jobId}")]
    [Authorize(PlatformPermissions.Administration.Maintenance.BackgroundJobsManage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteBackgroundJobAsync(string jobId)
    {
        await _backgroundJobs.DeleteAsync(jobId);
        return NoContent();
    }

    private static GridRequest BuildGridRequest(int startRow, int endRow, string? wildcardSearch, string? sortBy, string? sortDir)
    {
        var sortModels = sortBy != null
            ? new List<SortModel> { new SortModel(sortBy, sortDir ?? "asc") }
            : new List<SortModel>();

        return new GridRequest
        {
            StartRow = startRow,
            EndRow = endRow,
            WildcardSearch = wildcardSearch,
            SortModels = sortModels
        };
    }
}
