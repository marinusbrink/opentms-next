using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.EventBus.Local;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Tracing;
using Volo.Abp.Uow;

namespace OpenTms.Platform.Events.Outbox;

/// <summary>
/// The distributed event bus of this application: ABP's in-process implementation, plus the one
/// rule the outbox needs that ABP does not enforce — an outbox event may only be published inside a
/// transactional unit of work. Delivery still happens in-process today; the event spine (AD-14)
/// replaces <see cref="PublishFromOutboxAsync"/> later without touching publishers.
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IDistributedEventBus), typeof(LocalDistributedEventBus), typeof(OpenTmsDistributedEventBus))]
public class OpenTmsDistributedEventBus : LocalDistributedEventBus, ISingletonDependency
{
    public ILogger<OpenTmsDistributedEventBus> Logger { get; set; } = NullLogger<OpenTmsDistributedEventBus>.Instance;

    public OpenTmsDistributedEventBus(
        IServiceScopeFactory serviceScopeFactory,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        IOptions<AbpDistributedEventBusOptions> abpDistributedEventBusOptions,
        IGuidGenerator guidGenerator,
        IClock clock,
        IEventHandlerInvoker eventHandlerInvoker,
        ILocalEventBus localEventBus,
        ICorrelationIdProvider correlationIdProvider)
        : base(
            serviceScopeFactory,
            currentTenant,
            unitOfWorkManager,
            abpDistributedEventBusOptions,
            guidGenerator,
            clock,
            eventHandlerInvoker,
            localEventBus,
            correlationIdProvider)
    {
    }

    public override Task PublishAsync(Type eventType, object eventData, bool onUnitOfWorkComplete = true, bool useOutbox = true)
    {
        if (useOutbox && IsOutboxEvent(eventType))
        {
            EnsureTransactionalUnitOfWork(eventType);
        }

        return base.PublishAsync(eventType, eventData, onUnitOfWorkComplete, useOutbox);
    }

    public override Task PublishFromOutboxAsync(OutgoingEventInfo outgoingEvent, OutboxConfig outboxConfig)
    {
        if (!EventTypes.ContainsKey(outgoingEvent.EventName) && !DynamicEventNames.ContainsKey(outgoingEvent.EventName))
        {
            /* ABP drops an outbox event whose type no handler in this process subscribed to. That is
             * expected for events nobody reacts to yet, but it should never be silent. */
            Logger.LogWarning(
                "Outbox event {EventName} ({EventId}) has no subscribed handler type in this process and is dropped.",
                outgoingEvent.EventName, outgoingEvent.Id);
        }

        return base.PublishFromOutboxAsync(outgoingEvent, outboxConfig);
    }

    /// <summary>True when at least one configured outbox selects this event type.</summary>
    protected virtual bool IsOutboxEvent(Type eventType)
    {
        return AbpDistributedEventBusOptions.Outboxes.Values.Any(config => config.Selector == null || config.Selector(eventType));
    }

    protected virtual void EnsureTransactionalUnitOfWork(Type eventType)
    {
        var unitOfWork = UnitOfWorkManager.Current;
        if (unitOfWork == null || !unitOfWork.Options.IsTransactional)
        {
            throw new OutboxRequiresTransactionException(eventType);
        }
    }
}
