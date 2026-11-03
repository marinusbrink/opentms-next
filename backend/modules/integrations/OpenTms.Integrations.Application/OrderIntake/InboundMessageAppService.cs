using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.Integrations.Features;
using OpenTms.Integrations.OrderIntake.Profiles;
using OpenTms.Integrations.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// Integrations app → Received and the trace page (design 72 "UI design"): the list of received messages (design-50
/// paging, filters status, channel, source and day received; the wildcard search covers sender, subject and external id),
/// one message with its documents, the step timeline and the intakes with the provenance of every field, the originals
/// as downloads, and reprocessing a document (the job runs it; <see cref="InboundDocumentsAppService"/> serves the
/// design's route). Feature <c>Integrations.OrderIntake</c> plus the app features of the Received node; tenant-filtered.
/// This is what a Customer Success Manager sees through impersonation.
/// </summary>
[RequiresFeature(IntegrationsFeatures.OrderIntake, AppFeatures.Integrations, AppFeatures.IntegrationsApp.Intake.Default, AppFeatures.IntegrationsApp.Intake.Received, RequiresAll = true)]
[Authorize(IntegrationsPermissions.InboundMessages.Default)]
public class InboundMessageAppService : IntegrationsAppServiceBase, IInboundMessageAppService
{
    private readonly IInboundMessageRepository _messages;
    private readonly IRepository<InboundDocument, Guid> _documents;
    private readonly IRepository<ImportProfile, Guid> _profiles;
    private readonly IRepository<MailboxConnection, Guid> _mailboxes;
    private readonly IRepository<ApiClient, Guid> _apiClients;
    private readonly IBlobContainer<InboundDocumentContainer> _blobs;
    private readonly OrderIntakeJobQueue _jobs;

    public InboundMessageAppService(
        IInboundMessageRepository messages,
        IRepository<InboundDocument, Guid> documents,
        IRepository<ImportProfile, Guid> profiles,
        IRepository<MailboxConnection, Guid> mailboxes,
        IRepository<ApiClient, Guid> apiClients,
        IBlobContainer<InboundDocumentContainer> blobs,
        OrderIntakeJobQueue jobs)
    {
        _messages = messages;
        _documents = documents;
        _profiles = profiles;
        _mailboxes = mailboxes;
        _apiClients = apiClients;
        _blobs = blobs;
        _jobs = jobs;
    }

    public virtual async Task<GridResponse<InboundMessageRowDto>> GetListAsync(InboundMessageListRequestDto input)
    {
        var all = await _messages.GetQueryableAsync();
        var totalCount = await AsyncExecuter.LongCountAsync(all);

        var filtered = all;
        if (input.Status.Count > 0)
        {
            filtered = filtered.Where(m => input.Status.Contains(m.Status));
        }

        if (input.Channel.Count > 0)
        {
            filtered = filtered.Where(m => input.Channel.Contains(m.Channel));
        }

        if (input.SourceId != null)
        {
            filtered = filtered.Where(m => m.SourceId == input.SourceId);
        }

        if (input.ReceivedFrom != null)
        {
            var from = input.ReceivedFrom.Value.ToDateTime(TimeOnly.MinValue);
            filtered = filtered.Where(m => m.ReceivedAt >= from);
        }

        if (input.ReceivedTo != null)
        {
            var to = input.ReceivedTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
            filtered = filtered.Where(m => m.ReceivedAt < to);
        }

        var term = input.WildcardSearch?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            /* ILIKE on the raw columns with the trigram indexes (PostgreSQL); see IInboundMessageRepository. */
            filtered = await _messages.WhereSearchMatchesAsync(filtered, term);
        }

        var filteredCount = filtered == all ? totalCount : await AsyncExecuter.LongCountAsync(filtered);
        var descending = input.SortBy == null ? !string.Equals(input.SortDir, "asc", StringComparison.OrdinalIgnoreCase) : input.Descending;
        /* Every order ends on the id, so pages never overlap or skip a row. */
        var ordered = (input.SortBy?.ToLowerInvariant()) switch
        {
            null or "" or "receivedat" => descending ? filtered.OrderByDescending(m => m.ReceivedAt).ThenByDescending(m => m.Id) : filtered.OrderBy(m => m.ReceivedAt).ThenBy(m => m.Id),
            "channel" => descending ? filtered.OrderByDescending(m => m.Channel).ThenByDescending(m => m.ReceivedAt).ThenBy(m => m.Id) : filtered.OrderBy(m => m.Channel).ThenByDescending(m => m.ReceivedAt).ThenBy(m => m.Id),
            "from" => descending ? filtered.OrderByDescending(m => m.From).ThenByDescending(m => m.ReceivedAt).ThenBy(m => m.Id) : filtered.OrderBy(m => m.From).ThenByDescending(m => m.ReceivedAt).ThenBy(m => m.Id),
            "subject" => descending ? filtered.OrderByDescending(m => m.Subject).ThenByDescending(m => m.ReceivedAt).ThenBy(m => m.Id) : filtered.OrderBy(m => m.Subject).ThenByDescending(m => m.ReceivedAt).ThenBy(m => m.Id),
            "status" => descending ? filtered.OrderByDescending(m => m.Status).ThenByDescending(m => m.ReceivedAt).ThenBy(m => m.Id) : filtered.OrderBy(m => m.Status).ThenByDescending(m => m.ReceivedAt).ThenBy(m => m.Id),
            _ => throw new BusinessException(ReferenceDataErrorCodes.UnknownSortColumn).WithData("sortBy", input.SortBy!)
        };

        /* Projected in the query: the page reads the list's columns only. */
        var rows = await AsyncExecuter.ToListAsync(ordered
            .Skip(input.StartRow)
            .Take(input.EndRow - input.StartRow)
            .Select(m => new InboundMessageRowDto
            {
                Id = m.Id,
                ReceivedAt = m.ReceivedAt,
                Channel = m.Channel,
                SourceId = m.SourceId,
                From = m.From,
                Subject = m.Subject,
                Status = m.Status
            }));
        var ids = rows.Select(m => m.Id).ToList();
        var documentCounts = ids.Count == 0
            ? new Dictionary<Guid, int>()
            : (await AsyncExecuter.ToListAsync((await _documents.GetQueryableAsync())
                    .Where(d => ids.Contains(d.InboundMessageId))
                    .GroupBy(d => d.InboundMessageId)
                    .Select(g => new { g.Key, Count = g.Count() })))
                .ToDictionary(x => x.Key, x => x.Count);
        var names = await GetSourceNamesAsync(rows.Select(r => (r.Channel, r.SourceId)).ToList());
        foreach (var row in rows)
        {
            row.SourceName = names.GetValueOrDefault(row.SourceId);
            row.DocumentCount = documentCounts.GetValueOrDefault(row.Id);
        }

        return new GridResponse<InboundMessageRowDto>
        {
            Rows = rows,
            TotalCount = totalCount,
            FilteredCount = filteredCount
        };
    }

    public virtual async Task<InboundMessageDto> GetAsync(Guid id)
    {
        var message = await _messages.GetAsync(id);
        var documents = await _documents.GetListAsync(d => d.InboundMessageId == id, includeDetails: true);
        var profileIds = documents.Where(d => d.ImportProfileId != null).Select(d => d.ImportProfileId!.Value).Distinct().ToList();
        var profileNames = profileIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _profiles.GetListAsync(p => profileIds.Contains(p.Id))).ToDictionary(p => p.Id, p => p.Name);
        var names = await GetSourceNamesAsync([(message.Channel, message.SourceId)]);

        return new InboundMessageDto
        {
            Id = message.Id,
            Channel = message.Channel,
            SourceId = message.SourceId,
            SourceName = names.GetValueOrDefault(message.SourceId),
            ExternalId = message.ExternalId,
            From = message.From,
            Subject = message.Subject,
            ReceivedAt = message.ReceivedAt,
            Status = message.Status,
            SizeBytes = message.SizeBytes,
            HasRawContent = message.RawBlobName != null,
            Documents = documents.OrderBy(d => d.CreationTime).ThenBy(d => d.Id).Select(d => ToDto(d, profileNames)).ToList()
        };
    }

    public virtual async Task<InboundMessageDto> GetByDocumentAsync(Guid documentId)
    {
        var document = await _documents.GetAsync(documentId, includeDetails: false);
        return await GetAsync(document.InboundMessageId);
    }

    public virtual async Task<IRemoteStreamContent> GetRawContentAsync(Guid id)
    {
        var message = await _messages.GetAsync(id);
        var bytes = message.RawBlobName == null ? null : await _blobs.GetAllBytesOrNullAsync(message.RawBlobName);
        if (bytes == null)
        {
            throw new EntityNotFoundException(typeof(InboundMessage), id);
        }

        return message.Channel == InboundChannel.Api
            ? new RemoteStreamContent(new MemoryStream(bytes), "request.json", "application/json")
            : new RemoteStreamContent(new MemoryStream(bytes), OrderIntakeConsts.RawMessageBlobFileName, "message/rfc822");
    }

    public virtual async Task<IRemoteStreamContent> GetDocumentContentAsync(Guid documentId)
    {
        var document = await _documents.GetAsync(documentId, includeDetails: false);
        var bytes = await _blobs.GetAllBytesOrNullAsync(document.BlobName);
        if (bytes == null)
        {
            throw new EntityNotFoundException(typeof(InboundDocument), documentId);
        }

        var fileName = document.FileName ?? (document.Kind == InboundDocumentKind.MailBody
            ? (document.Format == DocumentFormat.Html ? "body.html" : "body.txt")
            : document.Kind == InboundDocumentKind.ApiJson ? "order.json" : "document");
        return new RemoteStreamContent(new MemoryStream(bytes), fileName, document.ContentType);
    }

    /// <summary>In-process only: HTTP reaches it as <c>POST /api/integrations/inbound-documents/{id}/reprocess</c> (<see cref="InboundDocumentsAppService"/>).</summary>
    [RemoteService(IsEnabled = false)]
    [Authorize(IntegrationsPermissions.InboundMessages.Reprocess)]
    public virtual async Task<InboundMessageDto> ReprocessDocumentAsync(Guid documentId)
    {
        var document = await _documents.GetAsync(documentId, includeDetails: true);
        if (document.Status != InboundDocumentStatus.Pending)
        {
            document.ResetForReprocess();
            document.AddStep(GuidGenerator.Create(), Clock.Now, IntakeStep.Reprocessed, StepOutcome.Info, IntakeStepKeys.Reprocessed, userId: CurrentUser.Id);
            await _documents.UpdateAsync(document, autoSave: true);
        }

        var message = await _messages.GetAsync(document.InboundMessageId);
        message.MarkProcessing();
        await _messages.UpdateAsync(message, autoSave: true);
        await _jobs.EnqueueProcessingAsync(message.Id);
        Logger.LogInformation("Inbound document {InboundDocumentId} of message {InboundMessageId} queued for reprocessing.", documentId, message.Id);
        return await GetAsync(message.Id);
    }

    private async Task<Dictionary<Guid, string>> GetSourceNamesAsync(IReadOnlyCollection<(InboundChannel Channel, Guid SourceId)> sources)
    {
        var mailboxIds = sources.Where(m => m.Channel != InboundChannel.Api).Select(m => m.SourceId).Distinct().ToList();
        var clientIds = sources.Where(m => m.Channel == InboundChannel.Api).Select(m => m.SourceId).Distinct().ToList();
        var names = new Dictionary<Guid, string>();
        if (mailboxIds.Count > 0)
        {
            foreach (var mailbox in await _mailboxes.GetListAsync(c => mailboxIds.Contains(c.Id)))
            {
                names[mailbox.Id] = mailbox.DisplayName;
            }
        }

        if (clientIds.Count > 0)
        {
            foreach (var client in await _apiClients.GetListAsync(c => clientIds.Contains(c.Id)))
            {
                names[client.Id] = client.Name;
            }
        }

        return names;
    }

    private static InboundDocumentDto ToDto(InboundDocument document, IReadOnlyDictionary<Guid, string> profileNames)
    {
        var steps = document.Steps.OrderBy(s => s.At).ThenBy(s => s.Step).ToList();
        return new InboundDocumentDto
        {
            Id = document.Id,
            Kind = document.Kind,
            FileName = document.FileName,
            ContentType = document.ContentType,
            SizeBytes = document.SizeBytes,
            Sha256 = document.Sha256,
            Format = document.Format,
            SenderKey = document.SenderKey,
            Fingerprint = document.Fingerprint,
            ImportProfileId = document.ImportProfileId,
            ImportProfileVersion = document.ImportProfileVersion,
            ImportProfileName = document.ImportProfileId != null ? profileNames.GetValueOrDefault(document.ImportProfileId.Value) : null,
            Status = document.Status,
            MappingMethod = document.MappingMethod,
            IntakeCount = document.IntakeCount,
            Steps = steps.Select(s => new InboundDocumentStepDto
            {
                Id = s.Id,
                At = s.At,
                Step = s.Step,
                Outcome = s.Outcome,
                MessageKey = s.MessageKey,
                Parameters = new Dictionary<string, string>(s.Parameters),
                UserId = s.UserId,
                ModelId = s.ModelId,
                InputTokens = s.InputTokens,
                OutputTokens = s.OutputTokens,
                CostMicroUsd = s.CostMicroUsd,
                IntakeIndex = s.IntakeIndex,
                IntakeId = s.IntakeId,
                TransportOrderId = s.TransportOrderId,
                OrderNumber = s.OrderNumber
            }).ToList(),
            Intakes = (document.Mapping?.Intakes ?? []).OrderBy(i => i.Index).Select(i =>
            {
                var intakeSteps = steps.Where(s => s.IntakeIndex == i.Index).ToList();
                return new InboundDocumentIntakeDto
                {
                    Index = i.Index,
                    Payload = IntakeMappings.FromPayload(i.Payload),
                    Provenance = new Dictionary<string, SourceLocation>(i.Provenance),
                    Confidence = i.Confidence,
                    IntakeId = intakeSteps.LastOrDefault(s => s.IntakeId != null)?.IntakeId,
                    TransportOrderId = intakeSteps.LastOrDefault(s => s.TransportOrderId != null)?.TransportOrderId,
                    OrderNumber = intakeSteps.LastOrDefault(s => s.OrderNumber != null)?.OrderNumber
                };
            }).ToList()
        };
    }
}

/// <summary>
/// The design's explicit route for reprocessing (design 72 "Reprocess"): by the conventional controller of this service,
/// <c>POST /api/integrations/inbound-documents/{id}/reprocess</c>. Runs the document through the pipeline again (the
/// job); intakes Orders already holds are not created twice.
/// </summary>
[RequiresFeature(IntegrationsFeatures.OrderIntake, AppFeatures.Integrations, AppFeatures.IntegrationsApp.Intake.Default, AppFeatures.IntegrationsApp.Intake.Received, RequiresAll = true)]
[Authorize(IntegrationsPermissions.InboundMessages.Reprocess)]
public class InboundDocumentsAppService : IntegrationsAppServiceBase
{
    private readonly IInboundMessageAppService _messages;

    public InboundDocumentsAppService(IInboundMessageAppService messages)
    {
        _messages = messages;
    }

    public virtual Task<InboundMessageDto> ReprocessAsync(Guid id) => _messages.ReprocessDocumentAsync(id);
}
