using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Platform.Maintenance.BackgroundJobs;

/// <summary>The states a job can be listed by; <see cref="Recurring"/> lists the schedules rather than runs.</summary>
public enum BackgroundJobState
{
    Enqueued,
    Scheduled,
    Processing,
    Succeeded,
    Failed,
    Deleted,
    Recurring
}

public sealed record BackgroundJobServer(string Name, int WorkerCount, IReadOnlyList<string> Queues, DateTime? StartedAt, DateTime? HeartbeatAt);

public sealed record BackgroundJobQueue(string Name, long Length, long? Fetched);

public sealed record BackgroundJobStatistics(
    long Enqueued,
    long Scheduled,
    long Processing,
    long Succeeded,
    long Failed,
    long Deleted,
    long Recurring,
    IReadOnlyList<BackgroundJobServer> Servers,
    IReadOnlyList<BackgroundJobQueue> Queues);

/// <summary>One job run (or one recurring schedule, when <see cref="State"/> is Recurring).</summary>
/// <param name="Id">The job id (or the recurring job id).</param>
/// <param name="JobName">The ABP job type for jobs enqueued through ABP, otherwise Type.Method.</param>
/// <param name="TenantId">The tenant the job runs for, when its arguments carry one.</param>
/// <param name="StateChangedAt">When the job entered its state (enqueued, scheduled, started, succeeded, failed, deleted).</param>
/// <param name="NextRunAt">Recurring and scheduled jobs: when the next run is due.</param>
/// <param name="Reason">Failed: the failure reason; recurring: the last run's state.</param>
public sealed record BackgroundJobInfo(
    string Id,
    BackgroundJobState State,
    string JobName,
    Guid? TenantId,
    DateTime? StateChangedAt,
    DateTime? NextRunAt,
    string? Reason,
    string? ExceptionType,
    string? ExceptionMessage,
    string? Cron,
    string? Queue,
    string? ServerId,
    long? DurationMilliseconds);

public sealed record BackgroundJobPage(IReadOnlyList<BackgroundJobInfo> Jobs, long TotalCount);

/// <summary>
/// The port behind the Background jobs tab. The host implements it over Hangfire's monitoring API
/// (AD-13: Hangfire is the job engine); the module stays free of the Hangfire packages. Hangfire's
/// storage is the host database and carries no tenant filter, so the port is host-only by design.
/// </summary>
public interface IBackgroundJobMonitor
{
    /// <summary>False when no job storage is configured in this process.</summary>
    bool IsAvailable { get; }

    Task<BackgroundJobStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    Task<BackgroundJobPage> GetJobsAsync(BackgroundJobState state, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>Puts a failed, deleted or scheduled job back in the queue. Returns false when the job does not exist.</summary>
    Task<bool> RequeueAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>Marks a job as deleted (it will not run). Returns false when the job does not exist.</summary>
    Task<bool> DeleteAsync(string jobId, CancellationToken cancellationToken = default);
}
