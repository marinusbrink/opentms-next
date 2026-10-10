using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.Platform.Events.Outbox;

/// <summary>
/// Publishes the stored events of one database and deletes them afterwards (at least once:
/// a crash between publish and delete re-delivers the event, so handlers are idempotent —
/// constitution rule 3). ABP's own outbox sender is switched off because it only ever reads the
/// host database; this dispatcher is told which tenant to visit instead.
/// </summary>
public class OutboxDispatcher : ITransientDependency
{
    public ILogger<OutboxDispatcher> Logger { get; set; } = NullLogger<OutboxDispatcher>.Instance;

    private readonly IDistributedEventBus _distributedEventBus;
    private readonly AbpDistributedEventBusOptions _eventBusOptions;
    private readonly OutboxOptions _outboxOptions;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly IAbpDistributedLock _distributedLock;
    private readonly IConnectionStringResolver _connectionStringResolver;
    private readonly IServiceProvider _serviceProvider;

    public OutboxDispatcher(
        IDistributedEventBus distributedEventBus,
        IOptions<AbpDistributedEventBusOptions> eventBusOptions,
        IOptions<OutboxOptions> outboxOptions,
        IUnitOfWorkManager unitOfWorkManager,
        ICurrentTenant currentTenant,
        IAbpDistributedLock distributedLock,
        IConnectionStringResolver connectionStringResolver,
        IServiceProvider serviceProvider)
    {
        _distributedEventBus = distributedEventBus;
        _eventBusOptions = eventBusOptions.Value;
        _outboxOptions = outboxOptions.Value;
        _unitOfWorkManager = unitOfWorkManager;
        _currentTenant = currentTenant;
        _distributedLock = distributedLock;
        _connectionStringResolver = connectionStringResolver;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Visits the database of <paramref name="tenantId"/> (null = the host database) and publishes
    /// what is waiting there, oldest first. Returns the number of events published and deleted.
    /// Stops at the first failing event so the order within a database is preserved; the rest
    /// waits for the next signal or sweep (open point AO-10: there is no dead-letter store yet).
    /// </summary>
    public virtual async Task<int> DispatchAsync(Guid? tenantId, CancellationToken cancellationToken = default)
    {
        var outboxConfig = _eventBusOptions.Outboxes.Values.FirstOrDefault();
        if (outboxConfig == null)
        {
            return 0;
        }

        using (_currentTenant.Change(tenantId))
        {
            /* One dispatcher per database, not per tenant: two shared-tier tenants resolve to the
             * same (host) database and must not drain the same table concurrently. */
            var lockName = await GetLockNameAsync(outboxConfig);
            await using var handle = await _distributedLock.TryAcquireAsync(lockName, cancellationToken: cancellationToken);
            if (handle == null)
            {
                Logger.LogDebug("Outbox of tenant {TenantId} is being dispatched elsewhere; skipping.", tenantId);
                return 0;
            }

            var outbox = (IEventOutbox)_serviceProvider.GetRequiredService(outboxConfig.ImplementationType);
            var published = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                var waitingEvents = await GetWaitingEventsAsync(outbox, cancellationToken);
                if (waitingEvents.Count == 0)
                {
                    break;
                }

                foreach (var outgoingEvent in waitingEvents)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var eventTenantId = outgoingEvent.GetTenantIdOrDefault(tenantId);
                    try
                    {
                        /* Handlers run in the context of the tenant the event was raised for — also
                         * for a shared-tier tenant whose row lives in the host database. */
                        using (_currentTenant.Change(eventTenantId))
                        {
                            await _distributedEventBus
                                .AsSupportsEventBoxes()
                                .PublishFromOutboxAsync(outgoingEvent, outboxConfig);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex,
                            "Outbox event {EventName} ({EventId}) of tenant {TenantId} failed; it and the events behind it in this database are retried on the next run.",
                            outgoingEvent.EventName, outgoingEvent.Id, eventTenantId);
                        return published;
                    }

                    await DeleteAsync(outbox, outgoingEvent.Id);
                    published++;
                }

                if (waitingEvents.Count < _outboxOptions.BatchSize)
                {
                    break;
                }
            }

            if (published > 0)
            {
                Logger.LogInformation("Dispatched {Count} outbox events for tenant {TenantId}.", published, tenantId);
            }

            return published;
        }
    }

    private async Task<List<OutgoingEventInfo>> GetWaitingEventsAsync(IEventOutbox outbox, CancellationToken cancellationToken)
    {
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(), requiresNew: true))
        {
            var waitingEvents = await outbox.GetWaitingEventsAsync(_outboxOptions.BatchSize, cancellationToken: cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            return waitingEvents;
        }
    }

    private async Task DeleteAsync(IEventOutbox outbox, Guid eventId)
    {
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(), requiresNew: true))
        {
            await outbox.DeleteAsync(eventId);
            await unitOfWork.CompleteAsync();
        }
    }

    private async Task<string> GetLockNameAsync(OutboxConfig outboxConfig)
    {
        var connectionString = await _connectionStringResolver.ResolveAsync(outboxConfig.DatabaseName);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(connectionString));
        return "OpenTms.Outbox:" + Convert.ToHexString(hash, 0, 8);
    }
}
