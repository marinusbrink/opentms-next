using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using OpenTms.Platform.Maintenance.BackgroundJobs;
using BackgroundJobServer = OpenTms.Platform.Maintenance.BackgroundJobs.BackgroundJobServer;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Maintenance;

/// <summary>
/// <see cref="IBackgroundJobMonitor"/> over Hangfire's monitoring API (AD-13). Read-only except for
/// requeue and delete, which are state changes Hangfire records like any other.
/// </summary>
public class HangfireBackgroundJobMonitor : IBackgroundJobMonitor, ITransientDependency
{
    private readonly JobStorage _storage;
    private readonly IBackgroundJobClient _client;

    public HangfireBackgroundJobMonitor(JobStorage storage, IBackgroundJobClient client)
    {
        _storage = storage;
        _client = client;
    }

    public bool IsAvailable => true;

    public Task<BackgroundJobStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var api = _storage.GetMonitoringApi();
        var statistics = api.GetStatistics();

        var servers = api.Servers()
            .Select(s => new BackgroundJobServer(s.Name, s.WorkersCount, s.Queues?.ToList() ?? new List<string>(), s.StartedAt, s.Heartbeat))
            .ToList();
        var queues = api.Queues()
            .Select(q => new BackgroundJobQueue(q.Name, q.Length, q.Fetched))
            .ToList();

        return Task.FromResult(new BackgroundJobStatistics(
            statistics.Enqueued,
            statistics.Scheduled,
            statistics.Processing,
            statistics.Succeeded,
            statistics.Failed,
            statistics.Deleted,
            statistics.Recurring,
            servers,
            queues));
    }

    public Task<BackgroundJobPage> GetJobsAsync(BackgroundJobState state, int skip, int take, CancellationToken cancellationToken = default)
    {
        var api = _storage.GetMonitoringApi();
        var statistics = api.GetStatistics();

        BackgroundJobPage page = state switch
        {
            BackgroundJobState.Enqueued => EnqueuedPage(api, skip, take),
            BackgroundJobState.Scheduled => new BackgroundJobPage(
                api.ScheduledJobs(skip, take).Select(j => Row(j.Key, state, j.Value.Job, j.Value.ScheduledAt, j.Value.EnqueueAt, null, null, null, null, null, null, null)).ToList(),
                statistics.Scheduled),
            BackgroundJobState.Processing => new BackgroundJobPage(
                api.ProcessingJobs(skip, take).Select(j => Row(j.Key, state, j.Value.Job, j.Value.StartedAt, null, null, null, null, null, null, j.Value.ServerId, null)).ToList(),
                statistics.Processing),
            BackgroundJobState.Succeeded => new BackgroundJobPage(
                api.SucceededJobs(skip, take).Select(j => Row(j.Key, state, j.Value.Job, j.Value.SucceededAt, null, null, null, null, null, null, null, j.Value.TotalDuration)).ToList(),
                statistics.Succeeded),
            BackgroundJobState.Failed => new BackgroundJobPage(
                api.FailedJobs(skip, take).Select(j => Row(j.Key, state, j.Value.Job, j.Value.FailedAt, null, j.Value.Reason, j.Value.ExceptionType, j.Value.ExceptionMessage, null, null, null, null)).ToList(),
                statistics.Failed),
            BackgroundJobState.Deleted => new BackgroundJobPage(
                api.DeletedJobs(skip, take).Select(j => Row(j.Key, state, j.Value.Job, j.Value.DeletedAt, null, null, null, null, null, null, null, null)).ToList(),
                statistics.Deleted),
            BackgroundJobState.Recurring => RecurringPage(skip, take),
            _ => new BackgroundJobPage(Array.Empty<BackgroundJobInfo>(), 0)
        };

        return Task.FromResult(page);
    }

    public Task<bool> RequeueAsync(string jobId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_client.ChangeState(jobId, new EnqueuedState(), expectedState: null));

    public Task<bool> DeleteAsync(string jobId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_client.ChangeState(jobId, new DeletedState(), expectedState: null));

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Enqueued jobs are listed per queue; one page runs over the queues in order.</summary>
    private static BackgroundJobPage EnqueuedPage(IMonitoringApi api, int skip, int take)
    {
        var rows = new List<BackgroundJobInfo>();
        long total = 0;
        var remainingSkip = skip;

        foreach (var queue in api.Queues())
        {
            total += queue.Length;
            if (rows.Count >= take)
            {
                continue;
            }

            if (remainingSkip >= queue.Length)
            {
                remainingSkip -= (int)queue.Length;
                continue;
            }

            var jobs = api.EnqueuedJobs(queue.Name, remainingSkip, take - rows.Count);
            remainingSkip = 0;
            rows.AddRange(jobs.Select(j => Row(j.Key, BackgroundJobState.Enqueued, j.Value.Job, j.Value.EnqueuedAt, null, null, null, null, null, queue.Name, null, null)));
        }

        return new BackgroundJobPage(rows, total);
    }

    private BackgroundJobPage RecurringPage(int skip, int take)
    {
        using var connection = _storage.GetConnection();
        var all = connection.GetRecurringJobs();
        var rows = all
            .OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .Skip(skip)
            .Take(take)
            .Select(r => new BackgroundJobInfo(
                r.Id,
                BackgroundJobState.Recurring,
                r.Job == null ? r.Id : JobNameOf(r.Job),
                r.Job == null ? null : TenantIdOf(r.Job),
                r.LastExecution,
                r.NextExecution,
                r.Error ?? r.LastJobState,
                null,
                r.LoadException?.Message,
                r.Cron,
                r.Queue,
                null,
                null))
            .ToList();
        return new BackgroundJobPage(rows, all.Count);
    }

    private static BackgroundJobInfo Row(
        string id,
        BackgroundJobState state,
        Job? job,
        DateTime? stateChangedAt,
        DateTime? nextRunAt,
        string? reason,
        string? exceptionType,
        string? exceptionMessage,
        string? cron,
        string? queue,
        string? serverId,
        long? durationMilliseconds) =>
        new(
            id,
            state,
            job == null ? id : JobNameOf(job),
            job == null ? null : TenantIdOf(job),
            stateChangedAt,
            nextRunAt,
            reason,
            exceptionType,
            exceptionMessage,
            cron,
            queue,
            serverId,
            durationMilliseconds);

    /// <summary>
    /// Jobs enqueued through ABP run inside Hangfire's adapter for the ABP job type; the name users
    /// know is that type's. Anything else shows as Type.Method.
    /// </summary>
    private static string JobNameOf(Job job)
    {
        var type = job.Type;
        if (type.IsGenericType && type.Name.StartsWith("HangfireJobExecutionAdapter", StringComparison.Ordinal))
        {
            return type.GetGenericArguments()[0].Name;
        }

        return $"{type.Name}.{job.Method.Name}";
    }

    /// <summary>The tenant a job runs for, when its (first) argument carries one.</summary>
    private static Guid? TenantIdOf(Job job)
    {
        var args = job.Args?.OfType<IMultiTenant>().FirstOrDefault();
        return args?.TenantId;
    }
}
