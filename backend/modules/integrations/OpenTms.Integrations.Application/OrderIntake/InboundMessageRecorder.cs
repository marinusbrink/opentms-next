using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Integrations.Events;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>The blob container of raw messages (<c>{messageId}/message.eml</c>) and documents (<c>{messageId}/{documentId}</c>); tenant-scoped like every container (AD-09).</summary>
[BlobContainerName(OrderIntakeConsts.BlobContainerName)]
public class InboundDocumentContainer
{
}

/// <summary>
/// Stores what a channel received (design 72 §2), idempotent on (tenant, channel, source, external id): the message row,
/// its raw content and every document's file in <see cref="InboundDocumentContainer"/> with its SHA-256, the steps
/// Received and Stored. Limits: a document over 25 MB is Failed and not stored; a message over 40 MB stores no file and
/// every document is Failed (the step names the limit). Skipped documents are listed with their name. Publishes
/// <see cref="InboundMessageReceivedEto"/> through the outbox and, for a new message with something to process that is
/// not the API's JSON (processed in the request), enqueues <see cref="InboundMessageProcessingJob"/> once the unit of
/// work has committed. Runs in the caller's unit of work and tenant.
/// </summary>
public class InboundMessageRecorder : IInboundMessageRecorder, ITransientDependency
{
    private readonly IRepository<InboundMessage, Guid> _messages;
    private readonly IRepository<InboundDocument, Guid> _documents;
    private readonly IBlobContainer<InboundDocumentContainer> _blobs;
    private readonly IDistributedEventBus _eventBus;
    private readonly OrderIntakeJobQueue _jobs;
    private readonly ICurrentTenant _currentTenant;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;

    public ILogger<InboundMessageRecorder> Logger { get; set; } = NullLogger<InboundMessageRecorder>.Instance;

    public InboundMessageRecorder(
        IRepository<InboundMessage, Guid> messages,
        IRepository<InboundDocument, Guid> documents,
        IBlobContainer<InboundDocumentContainer> blobs,
        IDistributedEventBus eventBus,
        OrderIntakeJobQueue jobs,
        ICurrentTenant currentTenant,
        IGuidGenerator guidGenerator,
        IClock clock)
    {
        _messages = messages;
        _documents = documents;
        _blobs = blobs;
        _eventBus = eventBus;
        _jobs = jobs;
        _currentTenant = currentTenant;
        _guidGenerator = guidGenerator;
        _clock = clock;
    }

    public async Task<InboundMessageRecordResult> RecordAsync(InboundMessageRecording recording, CancellationToken cancellationToken = default)
    {
        var externalId = recording.ExternalId?.Trim() ?? string.Empty;
        var existing = await _messages.FirstOrDefaultAsync(
            m => m.Channel == recording.Channel && m.SourceId == recording.SourceId && m.ExternalId == externalId, cancellationToken);
        if (existing != null)
        {
            Logger.LogInformation("Inbound message {InboundMessageId} ({Channel}, source {SourceId}) was recorded before; nothing stored.", existing.Id, recording.Channel, recording.SourceId);
            return new InboundMessageRecordResult(existing.Id, false);
        }

        var messageId = _guidGenerator.Create();
        var size = recording.RawContent?.LongLength ?? recording.Documents.Sum(d => d.Content.LongLength);
        var message = new InboundMessage(messageId, _currentTenant.Id, recording.Channel, recording.SourceId, externalId, recording.From, recording.Subject, recording.ReceivedAt, size);
        var now = _clock.Now;
        var messageTooLarge = size > OrderIntakeConsts.MaxMessageBytes;
        if (!messageTooLarge && recording.RawContent is { Length: > 0 })
        {
            var rawName = $"{messageId}/{OrderIntakeConsts.RawMessageBlobFileName}";
            await _blobs.SaveAsync(rawName, recording.RawContent, overrideExisting: true, cancellationToken);
            message.SetRawBlob(rawName);
        }

        /* Saved at once (in this unit of work's transaction): the API's JSON path reads the message back in the same request. */
        await _messages.InsertAsync(message, autoSave: true, cancellationToken: cancellationToken);

        var documents = new List<InboundDocument>();
        foreach (var recorded in recording.Documents)
        {
            documents.Add(await RecordDocumentAsync(message, recorded, messageTooLarge, now, cancellationToken));
        }

        await _documents.InsertManyAsync(documents, autoSave: true, cancellationToken: cancellationToken);

        var processable = documents.Any(d => d.Status == InboundDocumentStatus.Pending);
        if (!processable)
        {
            message.RollUp(documents.Select(d => d.Status).ToList());
        }

        await _messages.UpdateAsync(message, autoSave: true, cancellationToken: cancellationToken);
        await _eventBus.PublishAsync(new InboundMessageReceivedEto { InboundMessageId = messageId });

        if (processable && documents.Any(d => d.Kind != InboundDocumentKind.ApiJson))
        {
            await _jobs.EnqueueProcessingAsync(messageId);
        }

        Logger.LogInformation(
            "Inbound message {InboundMessageId} recorded ({Channel}, source {SourceId}, tenant {TenantId}): {DocumentCount} documents, {SizeBytes} bytes.",
            messageId, recording.Channel, recording.SourceId, _currentTenant.Id, documents.Count, size);
        return new InboundMessageRecordResult(messageId, true);
    }

    private async Task<InboundDocument> RecordDocumentAsync(InboundMessage message, InboundDocumentRecording recorded, bool messageTooLarge, DateTime now, CancellationToken cancellationToken)
    {
        var documentId = _guidGenerator.Create();
        var blobName = $"{message.Id}/{documentId}";
        var content = recorded.Content ?? [];
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        var contentType = string.IsNullOrWhiteSpace(recorded.ContentType) ? DocumentFormatDetector.DefaultContentType(recorded.Format) : recorded.ContentType.Trim();
        if (contentType.Length > OrderIntakeConsts.MaxContentTypeLength)
        {
            contentType = contentType[..OrderIntakeConsts.MaxContentTypeLength];
        }

        var fileName = CutFileName(recorded.FileName);
        var tooLarge = content.LongLength > OrderIntakeConsts.MaxDocumentBytes;
        var status = messageTooLarge || tooLarge
            ? InboundDocumentStatus.Failed
            : recorded.Skipped ? InboundDocumentStatus.Skipped : InboundDocumentStatus.Pending;
        var document = new InboundDocument(documentId, message.TenantId, message.Id, recorded.Kind, fileName, contentType, content.LongLength, sha256, blobName, recorded.Format, status);
        document.AddStep(_guidGenerator.Create(), now, IntakeStep.Received, StepOutcome.Info, IntakeStepKeys.Received,
            new Dictionary<string, string> { ["channel"] = message.Channel.ToString() });

        if (messageTooLarge)
        {
            document.AddStep(_guidGenerator.Create(), now, IntakeStep.Failed, StepOutcome.Error, IntakeStepKeys.MessageTooLarge,
                new Dictionary<string, string> { ["maxBytes"] = OrderIntakeConsts.MaxMessageBytes.ToString(CultureInfo.InvariantCulture) });
            return document;
        }

        if (tooLarge)
        {
            document.AddStep(_guidGenerator.Create(), now, IntakeStep.Failed, StepOutcome.Error, IntakeStepKeys.DocumentTooLarge,
                new Dictionary<string, string> { ["maxBytes"] = OrderIntakeConsts.MaxDocumentBytes.ToString(CultureInfo.InvariantCulture) });
            return document;
        }

        await _blobs.SaveAsync(blobName, content, overrideExisting: true, cancellationToken);
        document.AddStep(_guidGenerator.Create(), now, IntakeStep.Stored, StepOutcome.Success, IntakeStepKeys.Stored,
            new Dictionary<string, string> { ["sizeBytes"] = content.LongLength.ToString(CultureInfo.InvariantCulture) });
        if (recorded.Skipped)
        {
            document.AddStep(_guidGenerator.Create(), now, IntakeStep.Skipped, StepOutcome.Info, IntakeStepKeys.Skipped,
                new Dictionary<string, string> { ["fileName"] = fileName ?? string.Empty });
        }

        return document;
    }

    private static string? CutFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var trimmed = fileName.Trim();
        return trimmed.Length <= OrderIntakeConsts.MaxFileNameLength ? trimmed : trimmed[..OrderIntakeConsts.MaxFileNameLength];
    }
}
