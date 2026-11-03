using System;

namespace OpenTms.Integrations.Events;

/*
 * The facts Integrations publishes about the order intake (design 72 "Domain impact"). Written to the outbox in the
 * transaction that recorded the fact (AD-16); ids only (constitution rule 5); AP-06 name
 * OpenTms.Integrations.InboundMessageReceived. Nothing handles it in this run; later consumers must be idempotent (rule 3).
 */

/// <summary>A message (mail or API call) was stored with its raw content and documents.</summary>
public class InboundMessageReceivedEto
{
    public Guid InboundMessageId { get; set; }
}
