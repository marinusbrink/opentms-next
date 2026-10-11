using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Maintenance.BackgroundJobs;
using OpenTms.Platform.Permissions;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Validation;

namespace OpenTms.Platform.Administration.Maintenance;

/* The Background jobs tab. Host only: Hangfire's storage is the host database and carries no
 * tenant filter (AD-13), so a tenant-scoped view would have to filter on job arguments, which is
 * not the ABP data filter (constitution rule 1). */
[RemoteService(IsEnabled = false)]
[Authorize(PlatformPermissions.Administration.Maintenance.BackgroundJobs)]
public class BackgroundJobMaintenanceAppService : MaintenanceAppServiceBase, IBackgroundJobMaintenanceAppService
{
    private readonly IBackgroundJobMonitor _monitor;

    public BackgroundJobMaintenanceAppService(IBackgroundJobMonitor monitor)
    {
        _monitor = monitor;
    }

    public async Task<BackgroundJobsOverviewDto> GetOverviewAsync()
    {
        EnsureHostSide();
        if (!_monitor.IsAvailable)
        {
            return new BackgroundJobsOverviewDto { IsAvailable = false };
        }

        var statistics = await _monitor.GetStatisticsAsync();
        return new BackgroundJobsOverviewDto
        {
            IsAvailable = true,
            Enqueued = statistics.Enqueued,
            Scheduled = statistics.Scheduled,
            Processing = statistics.Processing,
            Succeeded = statistics.Succeeded,
            Failed = statistics.Failed,
            Deleted = statistics.Deleted,
            Recurring = statistics.Recurring,
            Servers = statistics.Servers.Select(s => new BackgroundJobServerDto
            {
                Name = s.Name,
                WorkerCount = s.WorkerCount,
                Queues = s.Queues.ToList(),
                StartedAt = s.StartedAt,
                HeartbeatAt = s.HeartbeatAt
            }).ToList(),
            Queues = statistics.Queues.Select(q => new BackgroundJobQueueDto { Name = q.Name, Length = q.Length, Fetched = q.Fetched }).ToList()
        };
    }

    public async Task<GridResponse<BackgroundJobRowDto>> GetListAsync(GridRequest request, string state)
    {
        EnsureHostSide();
        if (!Enum.TryParse<BackgroundJobState>(state, ignoreCase: true, out var jobState))
        {
            throw new AbpValidationException($"'{state}' is not a background job state.");
        }

        if (!_monitor.IsAvailable)
        {
            return new GridResponse<BackgroundJobRowDto>();
        }

        var page = await _monitor.GetJobsAsync(jobState, request.StartRow, Math.Max(0, request.EndRow - request.StartRow));
        return new GridResponse<BackgroundJobRowDto>
        {
            Rows = page.Jobs.Select(j => new BackgroundJobRowDto
            {
                Id = j.Id,
                State = j.State.ToString(),
                JobName = j.JobName,
                TenantId = j.TenantId,
                StateChangedAt = j.StateChangedAt,
                NextRunAt = j.NextRunAt,
                Reason = j.Reason,
                ExceptionType = j.ExceptionType,
                ExceptionMessage = j.ExceptionMessage,
                Cron = j.Cron,
                Queue = j.Queue,
                ServerId = j.ServerId,
                DurationMilliseconds = j.DurationMilliseconds
            }).ToList(),
            TotalCount = page.TotalCount,
            FilteredCount = page.TotalCount
        };
    }

    [Authorize(PlatformPermissions.Administration.Maintenance.BackgroundJobsManage)]
    public async Task RequeueAsync(string jobId)
    {
        EnsureHostSide();
        if (!await _monitor.RequeueAsync(jobId))
        {
            throw new EntityNotFoundException(typeof(BackgroundJobRowDto), jobId);
        }

        Logger.LogInformation("Background job {JobId} requeued by user {UserId}.", jobId, CurrentUser.Id);
    }

    [Authorize(PlatformPermissions.Administration.Maintenance.BackgroundJobsManage)]
    public async Task DeleteAsync(string jobId)
    {
        EnsureHostSide();
        if (!await _monitor.DeleteAsync(jobId))
        {
            throw new EntityNotFoundException(typeof(BackgroundJobRowDto), jobId);
        }

        Logger.LogInformation("Background job {JobId} deleted by user {UserId}.", jobId, CurrentUser.Id);
    }
}
