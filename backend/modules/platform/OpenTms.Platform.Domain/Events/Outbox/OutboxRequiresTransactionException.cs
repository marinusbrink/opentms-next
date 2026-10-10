using System;
using Volo.Abp;

namespace OpenTms.Platform.Events.Outbox;

/// <summary>
/// Thrown when an outbox event is published outside a transactional unit of work. Without the
/// transaction the fact would be committed without its event, which is exactly what the outbox
/// exists to prevent (constitution rule 3; AD-16).
/// </summary>
public class OutboxRequiresTransactionException : AbpException
{
    public Type EventType { get; }

    public OutboxRequiresTransactionException(Type eventType)
        : base($"The event {eventType.FullName} uses the transactional outbox and may only be published " +
               "inside a transactional unit of work (an application-service method that changes data). " +
               "Publishing outside a transaction would commit the fact without its event.")
    {
        EventType = eventType;
    }
}
