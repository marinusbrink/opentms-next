using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Threading;

namespace OpenTms.Integrations.OrderIntake.Channels;

/// <summary>The mailbox sync's switches ("Integrations:OrderIntake"; design 72 "Sync").</summary>
public class MailboxSyncOptions
{
    public const string SectionName = "Integrations:OrderIntake";

    /// <summary>The per-instance switch; the worker is registered only where it is on.</summary>
    public bool SyncEnabled { get; set; } = true;

    /// <summary>One sweep over every tenant per period (default one minute).</summary>
    public TimeSpan SyncPeriod { get; set; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// The periodic mailbox sync (design 72 "Sync"): visits every tenant, the way <c>TemplateMaterialisationWorker</c> does, and
/// runs <see cref="MailboxSynchronizer"/> in it; a tenant without the feature costs one feature check. One failing tenant
/// never stops the others. Registered by <c>IntegrationsApplicationModule</c> when <c>Integrations:OrderIntake:SyncEnabled</c>.
/// Known limit (design 72 "Cost &amp; SLO"): beyond a few hundred connections the sweep needs partitioning or push.
/// </summary>
public class MailboxSyncWorker : AsyncPeriodicBackgroundWorkerBase
{
    public MailboxSyncWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory, IOptions<MailboxSyncOptions> options)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)Math.Clamp(options.Value.SyncPeriod.TotalMilliseconds, 1_000, int.MaxValue);
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var tenantStore = workerContext.ServiceProvider.GetRequiredService<ITenantStore>();
        var currentTenant = workerContext.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var tenants = await tenantStore.GetListAsync();
        foreach (var tenant in tenants)
        {
            workerContext.CancellationToken.ThrowIfCancellationRequested();
            try
            {
                using (currentTenant.Change(tenant.Id))
                {
                    var synchronizer = workerContext.ServiceProvider.GetRequiredService<MailboxSynchronizer>();
                    await synchronizer.SyncCurrentTenantAsync(workerContext.CancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "Mailbox sync failed for tenant {TenantId}; continuing with the remaining tenants.", tenant.Id);
            }
        }
    }
}
