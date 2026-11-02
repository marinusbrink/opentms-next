using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTms.Platform.Maintenance;
using OpenTms.Platform.Maintenance.BackgroundJobs;
using Volo.Abp.DependencyInjection;

namespace OpenTms.HealthChecks;

/// <summary>
/// Hangfire, the job engine (AD-13): at least one server polls the queues, and no job ended in Failed. Without a server
/// nothing in the background runs (calculations, e-mail, provisioning): Unhealthy. A failed job has used up its retries
/// and waits for an administrator on the Background jobs tab: Degraded until it is retried or deleted.
/// </summary>
public class BackgroundJobsHealthCheck : IHealthCheck, ITransientDependency
{
    public const string Name = "background-jobs";

    public const string NoServer = "NoServer";

    public const string FailedJobs = "FailedJobs";

    /// <summary>A server whose heartbeat is older than this no longer counts (Hangfire's own server timeout is 5 minutes).</summary>
    public static readonly TimeSpan ServerSilence = TimeSpan.FromMinutes(5);

    private readonly IBackgroundJobMonitor _monitor;

    public BackgroundJobsHealthCheck(IBackgroundJobMonitor monitor)
    {
        _monitor = monitor;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_monitor.IsAvailable)
        {
            return HealthCheckResult.Unhealthy("background jobs: no job storage in this process.", data: HealthCheckFacts.Data(HealthCheckFacts.Reasons.NotConfigured));
        }

        BackgroundJobStatistics statistics;
        try
        {
            statistics = await _monitor.GetStatisticsAsync(cancellationToken);
        }
        catch (Exception e)
        {
            return HealthCheckResult.Unhealthy("background jobs: the job storage could not be read.", e, HealthCheckFacts.Data(HealthCheckFacts.Reasons.Unreachable));
        }

        var now = DateTime.UtcNow;
        var servers = statistics.Servers.Count(s => s.HeartbeatAt is { } at && now - DateTime.SpecifyKind(at, DateTimeKind.Utc) < ServerSilence);
        var extra = new Dictionary<string, object>
        {
            ["servers"] = servers,
            ["failed"] = statistics.Failed,
            ["enqueued"] = statistics.Enqueued
        };

        if (servers == 0)
        {
            return HealthCheckResult.Unhealthy(
                $"background jobs: no job server is running ({statistics.Enqueued} jobs waiting).",
                data: HealthCheckFacts.Data(NoServer, extra: extra));
        }

        if (statistics.Failed > 0)
        {
            return HealthCheckResult.Degraded(
                $"background jobs: {statistics.Failed} failed jobs wait for an administrator.",
                data: HealthCheckFacts.Data(FailedJobs, extra: extra));
        }

        return HealthCheckResult.Healthy(
            $"background jobs: {servers} servers running, no failed jobs.",
            HealthCheckFacts.Data(HealthCheckFacts.Reasons.Ok, extra: extra));
    }
}
