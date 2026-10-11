using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.Platform.Maintenance.BackgroundJobs;
using OpenTms.Platform.Maintenance.Health;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Platform.Maintenance;

/* The two ports the host implements (Hangfire, HealthCheckService) have no implementation in the
 * test modules; these fakes stand in and let the tests steer what the services see. Singletons,
 * so a test can configure them before calling the application service. */

public class FakeBackgroundJobMonitor : IBackgroundJobMonitor, ISingletonDependency
{
    public bool IsAvailable { get; set; } = true;

    public BackgroundJobStatistics Statistics { get; set; } = new(
        Enqueued: 1, Scheduled: 2, Processing: 3, Succeeded: 4, Failed: 5, Deleted: 6, Recurring: 7,
        Servers: [new BackgroundJobServer("server-1", 20, ["default"], DateTime.UtcNow, DateTime.UtcNow)],
        Queues: [new BackgroundJobQueue("default", 1, 0)]);

    public List<BackgroundJobInfo> Jobs { get; } = new();

    public HashSet<string> KnownJobIds { get; } = new();

    public List<string> Requeued { get; } = new();

    public List<string> Deleted { get; } = new();

    public (BackgroundJobState State, int Skip, int Take)? LastQuery { get; private set; }

    public Task<BackgroundJobStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Statistics);

    public Task<BackgroundJobPage> GetJobsAsync(BackgroundJobState state, int skip, int take, CancellationToken cancellationToken = default)
    {
        LastQuery = (state, skip, take);
        var jobs = Jobs.Where(j => j.State == state).ToList();
        return Task.FromResult(new BackgroundJobPage(jobs.Skip(skip).Take(take).ToList(), jobs.Count));
    }

    public Task<bool> RequeueAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (!KnownJobIds.Contains(jobId)) return Task.FromResult(false);
        Requeued.Add(jobId);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (!KnownJobIds.Contains(jobId)) return Task.FromResult(false);
        Deleted.Add(jobId);
        return Task.FromResult(true);
    }
}

public class FakeHealthReportProvider : IHealthReportProvider, ISingletonDependency
{
    public HealthReport Report { get; set; } = new(
        "Healthy",
        TimeSpan.FromMilliseconds(12),
        [new HealthCheckEntry("OpenTms DbContext Check", "Healthy", "Could connect to database and get record.", TimeSpan.FromMilliseconds(10), ["database", "ready"], null)]);

    public Task<HealthReport> GetReportAsync(CancellationToken cancellationToken = default) => Task.FromResult(Report);
}
