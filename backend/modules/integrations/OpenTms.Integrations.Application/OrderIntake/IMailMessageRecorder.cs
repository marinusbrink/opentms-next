using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>Records a raw RFC 822 mail of a mailbox connection as an inbound message (design 72 §2): body and attachments become documents.</summary>
public interface IMailMessageRecorder
{
    Task<InboundMessageRecordResult> RecordMailAsync(InboundChannel channel, Guid mailboxConnectionId, string externalId, byte[] rawMessage, DateTime receivedAt, CancellationToken cancellationToken = default);
}
