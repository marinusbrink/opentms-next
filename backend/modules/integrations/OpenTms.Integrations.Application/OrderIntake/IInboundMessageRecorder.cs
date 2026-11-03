using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// Stores what a channel received (design 72 "Inbound store"): the message row, its raw content and each document's
/// file in the blob container, and the steps Received and Stored; publishes <c>InboundMessageReceivedEto</c>. Idempotent on
/// (channel, source, external id): the same mail or API ExternalId again returns the stored message with
/// <see cref="InboundMessageRecordResult.IsNew"/> false and stores nothing (constitution rule 3). Runs in the caller's
/// unit of work and tenant: the mailbox sync's per-connection unit of work, or the API request.
/// </summary>
public interface IInboundMessageRecorder
{
    Task<InboundMessageRecordResult> RecordAsync(InboundMessageRecording message, CancellationToken cancellationToken = default);
}

/// <summary>A received message as a channel adapter hands it over.</summary>
public class InboundMessageRecording
{
    public InboundChannel Channel { get; set; }

    /// <summary>The mailbox connection or the API client.</summary>
    public Guid SourceId { get; set; }

    /// <summary>Gmail's message id, the Message-ID header (else the Graph id), or the API caller's ExternalId.</summary>
    public string ExternalId { get; set; } = string.Empty;

    public string? From { get; set; }

    public string? Subject { get; set; }

    public DateTime ReceivedAt { get; set; }

    /// <summary>The raw RFC 822 message (stored as <c>message.eml</c>), or the API call's body; null when there is none.</summary>
    public byte[]? RawContent { get; set; }

    public List<InboundDocumentRecording> Documents { get; set; } = new();
}

/// <summary>One document of a received message: the mail body, a supported attachment, an unsupported one (Skipped), the API's JSON or file.</summary>
public class InboundDocumentRecording
{
    public InboundDocumentKind Kind { get; set; }

    public string? FileName { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public byte[] Content { get; set; } = [];

    /// <summary>The format as far as the name and content type tell; the extractor may refine it (ScannedPdf).</summary>
    public DocumentFormat Format { get; set; }

    /// <summary>Listed in the trace with its name, never processed (images, calendar invites, an attached .eml).</summary>
    public bool Skipped { get; set; }
}

/// <param name="InboundMessageId">The stored message, new or existing.</param>
/// <param name="IsNew">False when the message was stored before (same channel, source and external id).</param>
public record InboundMessageRecordResult(Guid InboundMessageId, bool IsNew);
