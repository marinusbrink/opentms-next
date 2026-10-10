using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.Events.Outbox;

/// <summary>
/// Runs the <see cref="OutboxDispatcher"/>: immediately for the tenant that just committed (via
/// <see cref="IOutboxSignal"/>), and over every database every <see cref="OutboxOptions.SweepPeriod"/>
/// as a safety net for signals lost to a crash or raised in another process. Registered by the
/// host only when <c>Outbox:DispatchEnabled</c> is true.
///
/// Known limits (open point AO-07): the sweep and the signals share this one loop, databases are
/// visited one after another, and the lock that keeps two instances apart is in-process.
/// </summary>
public class OutboxDispatchWorker : BackgroundWorkerBase, ISingletonDependency
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOutboxSignal _signal;
    private readonly OutboxOptions _options;

    private CancellationTokenSource? _loopCancellation;
    private Task? _loop;

    public OutboxDispatchWorker(
        IServiceScopeFactory serviceScopeFactory,
        IOutboxSignal signal,
        IOptions<OutboxOptions> options)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _signal = signal;
        _options = options.Value;
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await base.StartAsync(cancellationToken);
        _loopCancellation = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_loopCancellation.Token), CancellationToken.None);
    }

    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        /* Idempotent: a second stop (or a stop before start) is a no-op. */
        var loopCancellation = Interlocked.Exchange(ref _loopCancellation, null);
        if (loopCancellation == null)
        {
            return;
        }

        await loopCancellation.CancelAsync();

        var loop = Interlocked.Exchange(ref _loop, null);
        if (loop != null)
        {
            try
            {
                await loop.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
                Logger.LogDebug("Outbox dispatch loop did not finish within the stop timeout.");
            }
        }

        loopCancellation.Dispose();
        await base.StopAsync(cancellationToken);
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        /* A sweep is due immediately at start-up: it catches up on whatever a previous instance
         * left behind. */
        var nextSweepAt = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var untilSweep = nextSweepAt - DateTime.UtcNow;
                if (untilSweep <= TimeSpan.Zero)
                {
                    await SweepSafelyAsync(stoppingToken);
                    nextSweepAt = DateTime.UtcNow + _options.SweepPeriod;
                    continue;
                }

                var signalled = await WaitForSignalAsync(untilSweep, stoppingToken);
                if (!signalled)
                {
                    continue; // the sweep is due
                }

                while (_signal.TryRead(out var tenantId))
                {
                    await DispatchSafelyAsync(tenantId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Outbox dispatch loop failed; continuing.");
            }
        }
    }

    private async Task<bool> WaitForSignalAsync(TimeSpan timeout, CancellationToken stoppingToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            return await _signal.WaitToReadAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task SweepSafelyAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var tenantStore = scope.ServiceProvider.GetRequiredService<ITenantStore>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();

            await dispatcher.DispatchAsync(null, stoppingToken);

            /* Shared-tier tenants (no connection string of their own) live in the host database and
             * were covered by the visit above; only tenants with their own database get a visit. */
            var tenants = await tenantStore.GetListAsync(includeDetails: true);
            foreach (var tenant in tenants.Where(HasOwnDatabase))
            {
                stoppingToken.ThrowIfCancellationRequested();
                try
                {
                    await dispatcher.DispatchAsync(tenant.Id, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    /* Continue past a failing tenant: one unreachable database must not block the rest. */
                    Logger.LogError(ex, "Outbox sweep failed for tenant {TenantId}; continuing with the remaining tenants.", tenant.Id);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Outbox sweep failed.");
        }
    }

    private async Task DispatchSafelyAsync(Guid? tenantId, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().DispatchAsync(tenantId, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Outbox dispatch failed for tenant {TenantId}; the next sweep retries it.", tenantId);
        }
    }

    private static bool HasOwnDatabase(TenantConfiguration tenant)
    {
        var connectionStrings = tenant.ConnectionStrings;
        return connectionStrings != null &&
               connectionStrings.Values.Any(value => !string.IsNullOrWhiteSpace(value));
    }
}
