using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// One document of an inbound message (design 72 "Inbound store"): the mail body, an attachment, or the API's JSON or
/// file, with its raw content in the blob container (<c>{messageId}/{id}</c>). It carries what the pipeline found — the
/// format, the sender key and fingerprint, the profile used, the mapping with its provenance — and the step log that
/// is the trace from the original file to the transport order. Steps record ids, keys and figures, never file content.
/// </summary>
public class InboundDocument : CreationAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid InboundMessageId { get; private set; }

    public InboundDocumentKind Kind { get; private set; }

    public string? FileName { get; private set; }

    public string ContentType { get; private set; } = default!;

    public long SizeBytes { get; private set; }

    /// <summary>Hex SHA-256 of the raw content.</summary>
    public string Sha256 { get; private set; } = default!;

    public string BlobName { get; private set; } = default!;

    public DocumentFormat Format { get; private set; }

    /// <summary>The lowercased From address, or <c>api:{clientId}</c>.</summary>
    public string? SenderKey { get; private set; }

    public string? Fingerprint { get; private set; }

    public Guid? ImportProfileId { get; private set; }

    public int? ImportProfileVersion { get; private set; }

    public InboundDocumentStatus Status { get; private set; }

    public MappingMethod MappingMethod { get; private set; }

    /// <summary>The intakes read from the document with their provenance (jsonb); null until mapped.</summary>
    public DocumentMapping? Mapping { get; private set; }

    public int IntakeCount { get; private set; }

    public virtual ICollection<InboundDocumentStep> Steps { get; private set; } = new List<InboundDocumentStep>();

    protected InboundDocument()
    {
    }

    public InboundDocument(
        Guid id, Guid? tenantId, Guid inboundMessageId, InboundDocumentKind kind, string? fileName, string contentType,
        long sizeBytes, string sha256, string blobName, DocumentFormat format, InboundDocumentStatus status = InboundDocumentStatus.Pending)
        : base(id)
    {
        TenantId = tenantId;
        InboundMessageId = inboundMessageId;
        Kind = kind;
        FileName = Check.Length(fileName, nameof(fileName), OrderIntakeConsts.MaxFileNameLength);
        ContentType = Check.NotNullOrWhiteSpace(contentType, nameof(contentType), OrderIntakeConsts.MaxContentTypeLength);
        SizeBytes = Check.Range(sizeBytes, nameof(sizeBytes), 0, long.MaxValue);
        Sha256 = Check.NotNullOrWhiteSpace(sha256, nameof(sha256), OrderIntakeConsts.Sha256Length);
        BlobName = Check.NotNullOrWhiteSpace(blobName, nameof(blobName), OrderIntakeConsts.MaxBlobNameLength);
        Format = format;
        Status = status;
        MappingMethod = MappingMethod.None;
    }

    /// <summary>Appends a step to the trace and returns it.</summary>
    public InboundDocumentStep AddStep(
        Guid stepId,
        DateTime at,
        IntakeStep step,
        StepOutcome outcome,
        string messageKey,
        IReadOnlyDictionary<string, string>? parameters = null,
        Guid? userId = null,
        StepAiUsage? aiUsage = null,
        StepIntakeReference? intake = null)
    {
        var added = new InboundDocumentStep(stepId, TenantId, Id, at, step, outcome, messageKey, parameters, userId, aiUsage, intake);
        Steps.Add(added);
        return added;
    }

    /// <summary>Whether the trace already holds this step (for this intake): event handlers run at least once.</summary>
    public bool HasStep(IntakeStep step, Guid? intakeId = null)
        => Steps.Any(s => s.Step == step && (intakeId == null || s.IntakeId == intakeId));

    /// <summary>The extractor settled the format (a PDF without text layer becomes ScannedPdf).</summary>
    public void SetFormat(DocumentFormat format) => Format = format;

    public void SetFingerprint(string? senderKey, string? fingerprint)
    {
        SenderKey = Check.Length(senderKey, nameof(senderKey), OrderIntakeConsts.MaxSenderKeyLength);
        Fingerprint = Check.Length(fingerprint, nameof(fingerprint), OrderIntakeConsts.FingerprintLength);
    }

    public void SetProfile(Guid? importProfileId, int? importProfileVersion)
    {
        if ((importProfileId == null) != (importProfileVersion == null))
        {
            throw new ArgumentException("A profile and its version go together.", nameof(importProfileVersion));
        }

        ImportProfileId = importProfileId;
        ImportProfileVersion = importProfileVersion;
    }

    /// <summary>The intakes were read: the document is Mapped.</summary>
    public void SetMapping(MappingMethod method, DocumentMapping mapping)
    {
        Check.NotNull(mapping, nameof(mapping));
        if (method == MappingMethod.None)
        {
            throw new ArgumentException("A mapping has a method.", nameof(method));
        }

        if (mapping.Intakes.Select(i => i.Index).Distinct().Count() != mapping.Intakes.Count)
        {
            throw new ArgumentException("The intakes of a document have distinct indexes.", nameof(mapping));
        }

        MappingMethod = method;
        Mapping = mapping;
        IntakeCount = mapping.Intakes.Count;
        Status = InboundDocumentStatus.Mapped;
    }

    public void SetStatus(InboundDocumentStatus status) => Status = status;

    /// <summary>
    /// Back to the start for a reprocess (e.g. after the AI feature was switched on or a profile retired). The steps stay:
    /// the trace shows both runs. Intakes Orders already holds are not created twice (Orders' natural key).
    /// </summary>
    public void ResetForReprocess()
    {
        Status = InboundDocumentStatus.Pending;
        MappingMethod = MappingMethod.None;
        Mapping = null;
        IntakeCount = 0;
        ImportProfileId = null;
        ImportProfileVersion = null;
    }
}

/// <summary>The figures of one language-model call on its step (AP-05; the same as its AI usage record).</summary>
public record StepAiUsage(string ModelId, int InputTokens, int OutputTokens, long CostMicroUsd);

/// <summary>The intake (and order) a step is about.</summary>
public record StepIntakeReference(int IntakeIndex, Guid? IntakeId = null, Guid? TransportOrderId = null, string? OrderNumber = null);

/// <summary>
/// One step of a document's trace: when, what, the outcome, a localization key with its parameters (ids, counts, short
/// codes; never file content or personal data), the user of a manual step, the figures of an AI call, and the intake and
/// order it concerns.
/// </summary>
public class InboundDocumentStep : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid InboundDocumentId { get; private set; }

    public DateTime At { get; private set; }

    public IntakeStep Step { get; private set; }

    public StepOutcome Outcome { get; private set; }

    /// <summary>The Integrations localization key of the step's text.</summary>
    public string MessageKey { get; private set; } = default!;

    /// <summary>The text's parameters (jsonb).</summary>
    public Dictionary<string, string> Parameters { get; private set; } = new();

    public Guid? UserId { get; private set; }

    public string? ModelId { get; private set; }

    public int? InputTokens { get; private set; }

    public int? OutputTokens { get; private set; }

    public long? CostMicroUsd { get; private set; }

    public int? IntakeIndex { get; private set; }

    public Guid? IntakeId { get; private set; }

    public Guid? TransportOrderId { get; private set; }

    public string? OrderNumber { get; private set; }

    protected InboundDocumentStep()
    {
    }

    internal InboundDocumentStep(
        Guid id, Guid? tenantId, Guid inboundDocumentId, DateTime at, IntakeStep step, StepOutcome outcome, string messageKey,
        IReadOnlyDictionary<string, string>? parameters, Guid? userId, StepAiUsage? aiUsage, StepIntakeReference? intake)
        : base(id)
    {
        TenantId = tenantId;
        InboundDocumentId = inboundDocumentId;
        At = at;
        Step = step;
        Outcome = outcome;
        MessageKey = Check.NotNullOrWhiteSpace(messageKey, nameof(messageKey), OrderIntakeConsts.MaxMessageKeyLength);
        Parameters = BoundedParameters(parameters);
        UserId = userId;
        if (aiUsage != null)
        {
            ModelId = Check.NotNullOrWhiteSpace(aiUsage.ModelId, nameof(aiUsage.ModelId), OrderIntakeConsts.MaxModelIdLength);
            InputTokens = aiUsage.InputTokens;
            OutputTokens = aiUsage.OutputTokens;
            CostMicroUsd = aiUsage.CostMicroUsd;
        }

        if (intake != null)
        {
            IntakeIndex = intake.IntakeIndex;
            IntakeId = intake.IntakeId;
            TransportOrderId = intake.TransportOrderId;
            OrderNumber = Check.Length(intake.OrderNumber, nameof(intake.OrderNumber), OrderIntakeConsts.MaxOrderNumberLength);
        }
    }

    private static Dictionary<string, string> BoundedParameters(IReadOnlyDictionary<string, string>? parameters)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (parameters == null)
        {
            return result;
        }

        if (parameters.Count > OrderIntakeConsts.MaxStepParameters)
        {
            throw new ArgumentException($"A step has at most {OrderIntakeConsts.MaxStepParameters} parameters.", nameof(parameters));
        }

        foreach (var (key, value) in parameters)
        {
            result[Check.NotNullOrWhiteSpace(key, nameof(parameters), OrderIntakeConsts.MaxStepParameterKeyLength)] =
                Check.Length(value, nameof(parameters), OrderIntakeConsts.MaxStepParameterValueLength) ?? string.Empty;
        }

        return result;
    }
}
