using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// One received message (design 72 "Inbound store"): a mail from a connected mailbox or one call of the intake API,
/// with its raw content in the blob container (<c>{id}/message.eml</c>). Unique per (tenant, channel, source,
/// external id), so the same mail or API ExternalId delivered twice is stored once (constitution rule 3). Its status is
/// the roll-up of its documents.
/// </summary>
public class InboundMessage : CreationAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public InboundChannel Channel { get; private set; }

    /// <summary>The mailbox connection or the API client.</summary>
    public Guid SourceId { get; private set; }

    /// <summary>Gmail's message id, the Message-ID header (Graph ids change on move), or the API caller's ExternalId.</summary>
    public string ExternalId { get; private set; } = default!;

    public string? From { get; private set; }

    public string? Subject { get; private set; }

    public DateTime ReceivedAt { get; private set; }

    public InboundMessageStatus Status { get; private set; }

    public string? RawBlobName { get; private set; }

    public long SizeBytes { get; private set; }

    protected InboundMessage()
    {
    }

    public InboundMessage(
        Guid id, Guid? tenantId, InboundChannel channel, Guid sourceId, string externalId, string? from, string? subject,
        DateTime receivedAt, long sizeBytes)
        : base(id)
    {
        TenantId = tenantId;
        Channel = channel;
        SourceId = sourceId;
        ExternalId = Check.NotNullOrWhiteSpace(externalId, nameof(externalId), OrderIntakeConsts.MaxExternalIdLength);
        From = Truncate(from, OrderIntakeConsts.MaxFromLength);
        Subject = Truncate(subject, OrderIntakeConsts.MaxSubjectLength);
        ReceivedAt = receivedAt;
        SizeBytes = Check.Range(sizeBytes, nameof(sizeBytes), 0, long.MaxValue);
        Status = InboundMessageStatus.Received;
    }

    public void SetRawBlob(string blobName)
        => RawBlobName = Check.NotNullOrWhiteSpace(blobName, nameof(blobName), OrderIntakeConsts.MaxBlobNameLength);

    public void MarkProcessing() => Status = InboundMessageStatus.Processing;

    /// <summary>The message itself failed (too large, unreadable) before any document could be processed.</summary>
    public void MarkFailed() => Status = InboundMessageStatus.Failed;

    /// <summary>Sets the status from the documents' statuses (<see cref="RollUpStatus"/>).</summary>
    public void RollUp(IReadOnlyCollection<InboundDocumentStatus> documentStatuses) => Status = RollUpStatus(documentStatuses);

    /// <summary>
    /// The roll-up: a document still in the pipeline → Processing; else any Failed → Failed; else any waiting for a person
    /// (NeedsAttention, NeedsReview) → NeedsAttention; else any Completed → Completed; else (only NotAnOrder and Skipped
    /// documents, or none) → NotAnOrder.
    /// </summary>
    public static InboundMessageStatus RollUpStatus(IReadOnlyCollection<InboundDocumentStatus> documentStatuses)
    {
        Check.NotNull(documentStatuses, nameof(documentStatuses));
        if (documentStatuses.Any(s => s is InboundDocumentStatus.Pending or InboundDocumentStatus.Mapped or InboundDocumentStatus.Submitted))
        {
            return InboundMessageStatus.Processing;
        }

        if (documentStatuses.Contains(InboundDocumentStatus.Failed))
        {
            return InboundMessageStatus.Failed;
        }

        if (documentStatuses.Any(s => s is InboundDocumentStatus.NeedsAttention or InboundDocumentStatus.NeedsReview))
        {
            return InboundMessageStatus.NeedsAttention;
        }

        return documentStatuses.Contains(InboundDocumentStatus.Completed) ? InboundMessageStatus.Completed : InboundMessageStatus.NotAnOrder;
    }

    /// <summary>Headers come from outside: an over-long one is cut, never refused (the message must still be stored).</summary>
    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
