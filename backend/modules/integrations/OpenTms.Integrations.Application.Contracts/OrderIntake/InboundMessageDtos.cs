using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTms.Orders.Intake;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// Integrations app → Received and its trace page (design 72 "UI design"), route root
/// <c>/api/integrations/inbound-messages</c>: the list, one message with its documents, steps and mappings, the
/// originals, and reprocessing a document (the design's <c>POST /api/integrations/inbound-documents/{id}/reprocess</c>).
/// This is what a Customer Success Manager sees through impersonation.
/// </summary>
public interface IInboundMessageAppService : IApplicationService
{
    Task<GridResponse<InboundMessageRowDto>> GetListAsync(InboundMessageListRequestDto input);

    Task<InboundMessageDto> GetAsync(Guid id);

    /// <summary>The raw RFC 822 mail (<c>message.eml</c>), or the API call's body.</summary>
    Task<IRemoteStreamContent> GetRawContentAsync(Guid id);

    /// <summary>The original file of one document.</summary>
    Task<IRemoteStreamContent> GetDocumentContentAsync(Guid documentId);

    /// <summary>Runs the document through the pipeline again; intakes Orders already holds are not created twice.</summary>
    Task<InboundMessageDto> ReprocessDocumentAsync(Guid documentId);
}

/// <summary>
/// The design-50 paging plus the filters. <see cref="ReferenceDataListRequestDto.WildcardSearch"/> searches the sender,
/// the subject and the external id.
/// </summary>
public class InboundMessageListRequestDto : ReferenceDataListRequestDto
{
    public List<InboundMessageStatus> Status { get; set; } = new();

    public List<InboundChannel> Channel { get; set; } = new();

    /// <summary>One mailbox connection or API client.</summary>
    public Guid? SourceId { get; set; }

    /// <summary>On the day received.</summary>
    public DateOnly? ReceivedFrom { get; set; }

    public DateOnly? ReceivedTo { get; set; }
}

public class InboundMessageRowDto
{
    public Guid Id { get; set; }

    public DateTime ReceivedAt { get; set; }

    public InboundChannel Channel { get; set; }

    public Guid SourceId { get; set; }

    /// <summary>The mailbox connection's or API client's name.</summary>
    public string? SourceName { get; set; }

    public string? From { get; set; }

    public string? Subject { get; set; }

    public int DocumentCount { get; set; }

    public InboundMessageStatus Status { get; set; }
}

public class InboundMessageDto
{
    public Guid Id { get; set; }

    public InboundChannel Channel { get; set; }

    public Guid SourceId { get; set; }

    public string? SourceName { get; set; }

    public string ExternalId { get; set; } = string.Empty;

    public string? From { get; set; }

    public string? Subject { get; set; }

    public DateTime ReceivedAt { get; set; }

    public InboundMessageStatus Status { get; set; }

    public long SizeBytes { get; set; }

    public bool HasRawContent { get; set; }

    public List<InboundDocumentDto> Documents { get; set; } = new();
}

public class InboundDocumentDto
{
    public Guid Id { get; set; }

    public InboundDocumentKind Kind { get; set; }

    public string? FileName { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public DocumentFormat Format { get; set; }

    public string? SenderKey { get; set; }

    public string? Fingerprint { get; set; }

    public Guid? ImportProfileId { get; set; }

    public int? ImportProfileVersion { get; set; }

    public string? ImportProfileName { get; set; }

    public InboundDocumentStatus Status { get; set; }

    public MappingMethod MappingMethod { get; set; }

    public int IntakeCount { get; set; }

    /// <summary>The timeline, oldest first.</summary>
    public List<InboundDocumentStepDto> Steps { get; set; } = new();

    /// <summary>The intakes read from the document with the provenance of every field.</summary>
    public List<InboundDocumentIntakeDto> Intakes { get; set; } = new();
}

public class InboundDocumentStepDto
{
    public Guid Id { get; set; }

    public DateTime At { get; set; }

    public IntakeStep Step { get; set; }

    public StepOutcome Outcome { get; set; }

    /// <summary>The Integrations localization key; the client formats it with <see cref="Parameters"/>.</summary>
    public string MessageKey { get; set; } = string.Empty;

    public Dictionary<string, string> Parameters { get; set; } = new();

    public Guid? UserId { get; set; }

    public string? UserName { get; set; }

    public string? ModelId { get; set; }

    public int? InputTokens { get; set; }

    public int? OutputTokens { get; set; }

    public long? CostMicroUsd { get; set; }

    public int? IntakeIndex { get; set; }

    public Guid? IntakeId { get; set; }

    public Guid? TransportOrderId { get; set; }

    public string? OrderNumber { get; set; }
}

/// <summary>One intake of a document as the field table shows it: the values, where each was read, and what Orders made of it.</summary>
public class InboundDocumentIntakeDto
{
    public int Index { get; set; }

    public OrderIntakeDto Payload { get; set; } = new();

    /// <summary>Field path → where its value was read.</summary>
    public Dictionary<string, SourceLocation> Provenance { get; set; } = new();

    public decimal Confidence { get; set; }

    /// <summary>Orders' intake, once submitted (from the step log).</summary>
    public Guid? IntakeId { get; set; }

    public Guid? TransportOrderId { get; set; }

    public string? OrderNumber { get; set; }
}
