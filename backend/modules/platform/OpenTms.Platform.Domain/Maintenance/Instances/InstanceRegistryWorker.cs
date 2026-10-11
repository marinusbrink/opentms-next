using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Threading;

namespace OpenTms.Platform.Maintenance.Instances;

/// <summary>
/// Keeps this instance's row in the registry current: registers it at start (born), heartbeats
/// every <see cref="InstanceRegistryOptions.HeartbeatPeriod"/> with the health result (alive) and
/// marks it stopped when the application shuts down gracefully (dying). Cloud Run sends SIGTERM
/// before it removes an autoscaled instance, which runs the stop; a hard kill leaves the row to go
/// silent, which the view shows as unresponsive. Registered by the host when
/// <c>InstanceRegistry:Enabled</c> is true.
/// </summary>
public class InstanceRegistryWorker : AsyncPeriodicBackgroundWorkerBase, ISingletonDependency
{
    public InstanceRegistryWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory, IOptions<InstanceRegistryOptions> options)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)Math.Max(1000, options.Value.HeartbeatPeriod.TotalMilliseconds);
        /* The first heartbeat runs at once, so a fresh instance appears without waiting a period. */
        Timer.RunOnStart = true;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        try
        {
            await workerContext.ServiceProvider.GetRequiredService<IInstanceRegistry>().HeartbeatAsync(workerContext.CancellationToken);
        }
        catch (OperationCanceledException) when (workerContext.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            /* An unreachable host database must not take the instance down; the next period retries. */
            Logger.LogWarning(ex, "Instance heartbeat failed; retrying on the next period.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await base.StopAsync(cancellationToken);
        try
        {
            /* The token handed to a stopping worker is the application's shutdown token, already
             * cancelled by now; the goodbye is one small UPDATE and gets its own short budget. */
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var scope = ServiceScopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IInstanceRegistry>().MarkStoppedAsync(budget.Token);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Instance could not be marked stopped; it will show as unresponsive until retention removes it.");
        }
    }
}
