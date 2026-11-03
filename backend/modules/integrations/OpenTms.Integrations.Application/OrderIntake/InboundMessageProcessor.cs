using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Integrations.Features;
using OpenTms.Integrations.OrderIntake.Ai;
using OpenTms.Integrations.OrderIntake.Extraction;
using OpenTms.Integrations.OrderIntake.Profiles;
using OpenTms.Orders.Intake;
using OpenTms.Orders.Published;
using OpenTms.Orders.TransportOrders;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;
using Volo.Abp.ExceptionHandling;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Volo.Abp.Validation;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// The import pipeline of design 72 §1–6, per message in its tenant (feature <c>Integrations.OrderIntake</c>). Phase 1
/// (one transaction): every Pending document is extracted, fingerprinted and matched against the import profiles; a
/// matched document is mapped and submitted at once; the API's JSON is submitted as the canonical model. Phase 2 (no
/// open transaction — a learning call may think for minutes): one learning call for the message's unmatched documents and
/// one extraction call per scanned PDF or mail body whose profile failed, only with <c>Integrations.OrderIntake.AiMapping</c>
/// (otherwise NeedsAttention, "AI mapping is off"). Phase 3 (one transaction, the job's transaction): the AI results
/// become steps, a verified profile is stored Proposed, the intakes are submitted to Orders'
/// <c>IOrderIntakeService.SubmitAsync</c> with the channel's defaults, and the message's status is rolled up. Every stage
/// leaves a step. Idempotent: only Pending documents run (a reprocess puts a document back to Pending), and Orders
/// returns the intake it already holds for (document, index). A transient AI failure throws
/// <see cref="IntakeAiRetryException"/> after recording the attempt, so Hangfire retries; at the third attempt, or for any
/// other failure, the documents need attention.
/// </summary>
public class InboundMessageProcessor : IInboundMessageProcessor, ITransientDependency
{
    public const int MaxAiAttempts = 3;

    private const string TransientParameter = "transient";

    private readonly IRepository<InboundMessage, Guid> _messages;
    private readonly IRepository<InboundDocument, Guid> _documents;
    private readonly IRepository<ImportProfile, Guid> _profiles;
    private readonly IRepository<MailboxConnection, Guid> _mailboxes;
    private readonly IRepository<ApiClient, Guid> _apiClients;
    private readonly IBlobContainer<InboundDocumentContainer> _blobs;
    private readonly DocumentExtractionService _extraction;
    private readonly ImportProfileMatcher _matcher;
    private readonly IntakeLearner _learner;
    private readonly IOrderIntakeService _orderIntake;
    private readonly IFeatureChecker _features;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;

    public ILogger<InboundMessageProcessor> Logger { get; set; } = NullLogger<InboundMessageProcessor>.Instance;

    public InboundMessageProcessor(
        IRepository<InboundMessage, Guid> messages,
        IRepository<InboundDocument, Guid> documents,
        IRepository<ImportProfile, Guid> profiles,
        IRepository<MailboxConnection, Guid> mailboxes,
        IRepository<ApiClient, Guid> apiClients,
        IBlobContainer<InboundDocumentContainer> blobs,
        DocumentExtractionService extraction,
        ImportProfileMatcher matcher,
        IntakeLearner learner,
        IOrderIntakeService orderIntake,
        IFeatureChecker features,
        IUnitOfWorkManager unitOfWorkManager,
        ICurrentTenant currentTenant,
        IGuidGenerator guidGenerator,
        IClock clock)
    {
        _messages = messages;
        _documents = documents;
        _profiles = profiles;
        _mailboxes = mailboxes;
        _apiClients = apiClients;
        _blobs = blobs;
        _extraction = extraction;
        _matcher = matcher;
        _learner = learner;
        _orderIntake = orderIntake;
        _features = features;
        _unitOfWorkManager = unitOfWorkManager;
        _currentTenant = currentTenant;
        _guidGenerator = guidGenerator;
        _clock = clock;
    }

    /// <summary>The channel's defaults handed to Orders with every intake.</summary>
    private sealed record ChannelDefaults(string? SenderKey, Guid? DefaultCustomerRelationId, Guid? FixedCustomerRelationId, Guid? DefaultTransportOrderTypeId, bool AutoConfirm, OrderSource SourceKind);

    /// <summary>A document waiting for the AI between phase 1 and phase 3.</summary>
    private sealed class AiWork
    {
        public Guid DocumentId { get; init; }

        public string? FileName { get; init; }

        public ExtractedDocument Extracted { get; init; } = new();

        public byte[] Content { get; init; } = [];

        /// <summary>Extraction call (scanned PDF, mail body whose profile failed) rather than the learning call.</summary>
        public bool ExtractOnly { get; init; }

        public int PreviousTransientFailures { get; init; }
    }

    /* ======================================================================================================== */

    public async Task ProcessAsync(Guid inboundMessageId, CancellationToken cancellationToken = default)
    {
        if (!await _features.IsEnabledAsync(IntegrationsFeatures.OrderIntake))
        {
            Logger.LogInformation("Inbound message {InboundMessageId} (tenant {TenantId}) not processed: order intake is off.", inboundMessageId, _currentTenant.Id);
            return;
        }

        var aiEnabled = await _features.IsEnabledAsync(IntegrationsFeatures.OrderIntakeAiMapping);
        List<AiWork> work;
        string? subject;
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true))
        {
            (work, subject) = await RunProfilesAsync(inboundMessageId, aiEnabled, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
        }

        if (work.Count == 0)
        {
            return;
        }

        var requestedAt = _clock.Now;
        var learning = new List<(LearningOutcome Outcome, List<AiWork> Documents)>();
        try
        {
            IntakeLearningContextDto context;
            using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: false), requiresNew: true))
            {
                context = await _orderIntake.GetLearningContextAsync(cancellationToken);
                await unitOfWork.CompleteAsync(cancellationToken);
            }

            var learn = work.Where(w => !w.ExtractOnly).ToList();
            if (learn.Count > 0)
            {
                var documents = learn.Select((w, i) => ToLearningDocument(w, i + 1)).ToList();
                learning.Add((await _learner.LearnAsync(documents, subject, context, cancellationToken), learn));
            }

            foreach (var item in work.Where(w => w.ExtractOnly))
            {
                learning.Add((await _learner.ExtractAsync(ToLearningDocument(item, 1), subject, context, cancellationToken), [item]));
            }
        }
        catch (LanguageModelException ex) when (ex.IsTransient)
        {
            var attempt = work.Max(w => w.PreviousTransientFailures) + 1;
            if (attempt < MaxAiAttempts)
            {
                await RecordRetryAsync(work, requestedAt, attempt);
                Logger.LogWarning("The language model is unavailable for inbound message {InboundMessageId} (attempt {Attempt} of {MaxAttempts}); the job is retried.", inboundMessageId, attempt, MaxAiAttempts);
                throw new IntakeAiRetryException(attempt, ex);
            }

            Logger.LogWarning("The language model stayed unavailable for inbound message {InboundMessageId} after {MaxAttempts} attempts; the documents need attention.", inboundMessageId, MaxAiAttempts);
            learning = [(new LearningOutcome { FailureCode = "unavailable" }, work)];
        }
        catch (LanguageModelException ex)
        {
            Logger.LogWarning("The language model call for inbound message {InboundMessageId} failed: {Error}.", inboundMessageId, ex.Message);
            learning = [(new LearningOutcome { FailureCode = "error" }, work)];
        }

        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true))
        {
            await ApplyAiAsync(inboundMessageId, learning, requestedAt, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
        }
    }

    public async Task<IntakeResultDto> ProcessCanonicalAsync(Guid inboundMessageId, OrderIntakeDto order, CancellationToken cancellationToken = default)
    {
        var message = await _messages.GetAsync(inboundMessageId, cancellationToken: cancellationToken);
        var documents = await _documents.GetListAsync(d => d.InboundMessageId == inboundMessageId, includeDetails: true, cancellationToken);
        var document = documents.FirstOrDefault(d => d.Kind == InboundDocumentKind.ApiJson);
        if (document != null && document.Status == InboundDocumentStatus.Pending)
        {
            var defaults = await GetDefaultsAsync(message);
            await SubmitCanonicalAsync(message, document, order, defaults, cancellationToken);
            message.RollUp(documents.Select(d => d.Status).ToList());
            await _documents.UpdateAsync(document, cancellationToken: cancellationToken);
            await _messages.UpdateAsync(message, cancellationToken: cancellationToken);
        }

        return IntakeResultMapper.Build(message, documents);
    }

    /* ---------- Phase 1: extract, fingerprint, profiles ---------- */

    private async Task<(List<AiWork> Work, string? Subject)> RunProfilesAsync(Guid inboundMessageId, bool aiEnabled, CancellationToken cancellationToken)
    {
        var work = new List<AiWork>();
        var message = await _messages.FindAsync(inboundMessageId, cancellationToken: cancellationToken);
        if (message == null)
        {
            Logger.LogInformation("Inbound message {InboundMessageId} no longer exists; nothing to process.", inboundMessageId);
            return (work, null);
        }

        var documents = await _documents.GetListAsync(d => d.InboundMessageId == inboundMessageId, includeDetails: true, cancellationToken);
        var pending = documents.Where(d => d.Status == InboundDocumentStatus.Pending).OrderBy(d => d.CreationTime).ThenBy(d => d.Id).ToList();
        if (pending.Count == 0)
        {
            message.RollUp(documents.Select(d => d.Status).ToList());
            await _messages.UpdateAsync(message, cancellationToken: cancellationToken);
            return (work, message.Subject);
        }

        message.MarkProcessing();
        var defaults = await GetDefaultsAsync(message);
        foreach (var document in pending)
        {
            var item = await RunDocumentAsync(message, document, defaults, aiEnabled, cancellationToken);
            if (item != null)
            {
                work.Add(item);
            }

            await _documents.UpdateAsync(document, cancellationToken: cancellationToken);
        }

        message.RollUp(documents.Select(d => d.Status).ToList());
        await _messages.UpdateAsync(message, cancellationToken: cancellationToken);
        Logger.LogInformation(
            "Inbound message {InboundMessageId} (tenant {TenantId}): {PendingCount} documents read with profiles, {AiCount} wait for the AI.",
            inboundMessageId, _currentTenant.Id, pending.Count - work.Count, work.Count);
        return (work, message.Subject);
    }

    private async Task<AiWork?> RunDocumentAsync(InboundMessage message, InboundDocument document, ChannelDefaults defaults, bool aiEnabled, CancellationToken cancellationToken)
    {
        var content = await _blobs.GetAllBytesOrNullAsync(document.BlobName, cancellationToken);
        if (content == null)
        {
            Fail(document, IntakeStep.Failed, IntakeStepKeys.ExtractionFailed, "missing");
            return null;
        }

        if (document.Kind == InboundDocumentKind.ApiJson)
        {
            var order = IntakeMappings.ParseApiBody(content);
            if (order == null)
            {
                Fail(document, IntakeStep.Failed, IntakeStepKeys.ExtractionFailed, "json");
                return null;
            }

            await SubmitCanonicalAsync(message, document, order, defaults, cancellationToken);
            return null;
        }

        ExtractedDocument extracted;
        try
        {
            extracted = _extraction.Extract(content, document.Format, document.Kind);
        }
        catch (DocumentExtractionException ex)
        {
            Logger.LogInformation("Document {InboundDocumentId} cannot be read ({Reason}).", document.Id, ex.ReasonCode);
            Fail(document, IntakeStep.Failed, IntakeStepKeys.ExtractionFailed, ex.ReasonCode);
            return null;
        }

        document.SetFormat(extracted.Format);
        if (extracted.Format == DocumentFormat.ScannedPdf)
        {
            AddStep(document, IntakeStep.Extracted, StepOutcome.Warning, IntakeStepKeys.ExtractedScanned, ("pageCount", Number(extracted.PageCount)));
        }
        else
        {
            AddStep(document, IntakeStep.Extracted, StepOutcome.Success, IntakeStepKeys.Extracted, ("format", extracted.Format.ToString()), ("lineCount", Number(extracted.Lines.Count)));
        }

        document.SetFingerprint(defaults.SenderKey, extracted.Fingerprint);
        AddStep(document, IntakeStep.Fingerprinted, StepOutcome.Info, IntakeStepKeys.Fingerprinted,
            ("description", Cut(extracted.FingerprintDescription, OrderIntakeConsts.MaxStepParameterValueLength)!), ("fingerprint", extracted.Fingerprint[..Math.Min(12, extracted.Fingerprint.Length)]));

        var profileFailed = false;
        if (extracted.Format != DocumentFormat.ScannedPdf)
        {
            var candidates = await _matcher.GetCandidatesAsync(extracted.Format, defaults.SenderKey, cancellationToken);
            var match = _matcher.Match(candidates, extracted);
            foreach (var (profile, run) in match.Failed)
            {
                profileFailed = true;
                AddStep(document, IntakeStep.ProfileFailed, StepOutcome.Warning, IntakeStepKeys.ProfileFailed,
                    ("profileName", profile.Name), ("version", Number(profile.Version)), ("errorCount", Number(run.Errors.Count)),
                    ("firstError", Cut($"{run.Errors[0].FieldPath} {run.Errors[0].Code}".Trim(), 200)!));
            }

            if (match.Profile != null)
            {
                var profile = match.Profile;
                profile.RecordUse(_clock.Now);
                await _profiles.UpdateAsync(profile, cancellationToken: cancellationToken);
                document.SetProfile(profile.Id, profile.Version);
                AddStep(document, IntakeStep.ProfileMatched, StepOutcome.Success, IntakeStepKeys.ProfileMatched,
                    ("profileName", profile.Name), ("version", Number(profile.Version)), ("intakeCount", Number(match.Run!.Intakes.Count)));
                await MapAndSubmitAsync(message, document, MappingMethod.Profile, match.Run.Intakes, 1m, profile.Status == ImportProfileStatus.Active, defaults, cancellationToken);
                return null;
            }
        }

        if (!aiEnabled)
        {
            AddStep(document, IntakeStep.ProfileNotFound, StepOutcome.Warning, IntakeStepKeys.ProfileNotFoundAiOff);
            document.SetStatus(InboundDocumentStatus.NeedsAttention);
            return null;
        }

        if (!profileFailed && extracted.Format != DocumentFormat.ScannedPdf)
        {
            AddStep(document, IntakeStep.ProfileNotFound, StepOutcome.Info, IntakeStepKeys.ProfileNotFound);
        }

        return new AiWork
        {
            DocumentId = document.Id,
            FileName = document.FileName,
            Extracted = extracted,
            Content = content,
            ExtractOnly = extracted.Format == DocumentFormat.ScannedPdf || (profileFailed && document.Kind == InboundDocumentKind.MailBody),
            PreviousTransientFailures = TransientFailuresSinceReset(document)
        };
    }

    /* ---------- Phase 3: apply the AI's results ---------- */

    private async Task ApplyAiAsync(Guid inboundMessageId, List<(LearningOutcome Outcome, List<AiWork> Documents)> learning, DateTime requestedAt, CancellationToken cancellationToken)
    {
        var message = await _messages.GetAsync(inboundMessageId, cancellationToken: cancellationToken);
        var documents = await _documents.GetListAsync(d => d.InboundMessageId == inboundMessageId, includeDetails: true, cancellationToken);
        var defaults = await GetDefaultsAsync(message);
        foreach (var (outcome, items) in learning)
        {
            var modelId = outcome.ModelId ?? outcome.Calls.FirstOrDefault()?.Usage.ModelId ?? "-";
            var callDocuments = items
                .Select(i => documents.FirstOrDefault(d => d.Id == i.DocumentId))
                .Where(d => d is { Status: InboundDocumentStatus.Pending })
                .Select(d => d!)
                .ToList();
            foreach (var document in callDocuments)
            {
                document.AddStep(_guidGenerator.Create(), requestedAt, IntakeStep.AiRequested, StepOutcome.Info, IntakeStepKeys.AiRequested,
                    Parameters(("model", modelId), ("documentCount", Number(items.Count))));
            }

            /* The figures of each call go on the AiCompleted step of the call's first document (AP-05: once per call). */
            foreach (var call in outcome.Calls)
            {
                var first = callDocuments.FirstOrDefault(d => d.Id == call.FirstDocumentId) ?? callDocuments.FirstOrDefault();
                first?.AddStep(_guidGenerator.Create(), _clock.Now, IntakeStep.AiCompleted, StepOutcome.Success, IntakeStepKeys.AiCompleted,
                    Parameters(("model", call.Usage.ModelId), ("documentCount", Number(items.Count))), aiUsage: call.Usage);
            }

            foreach (var document in callDocuments)
            {
                var item = items.First(i => i.DocumentId == document.Id);
                if (outcome.FailureCode != null)
                {
                    Fail(document, IntakeStep.AiFailed, IntakeStepKeys.AiFailed, outcome.FailureCode, InboundDocumentStatus.NeedsAttention);
                    continue;
                }

                var result = outcome.Documents.FirstOrDefault(d => d.DocumentId == document.Id);
                await ApplyDocumentAsync(message, document, item, result, modelId, defaults, cancellationToken);
            }
        }

        foreach (var document in documents)
        {
            await _documents.UpdateAsync(document, cancellationToken: cancellationToken);
        }

        message.RollUp(documents.Select(d => d.Status).ToList());
        await _messages.UpdateAsync(message, cancellationToken: cancellationToken);
    }

    private async Task ApplyDocumentAsync(InboundMessage message, InboundDocument document, AiWork item, LearnedDocumentOutcome? result, string modelId, ChannelDefaults defaults, CancellationToken cancellationToken)
    {
        if (result is not { Answered: true })
        {
            Fail(document, IntakeStep.AiFailed, IntakeStepKeys.AiFailed, "noAnswer", InboundDocumentStatus.NeedsAttention);
            return;
        }

        if (!result.IsOrder)
        {
            AddStep(document, IntakeStep.NotAnOrder, StepOutcome.Info, IntakeStepKeys.NotAnOrder);
            document.SetStatus(InboundDocumentStatus.NotAnOrder);
            return;
        }

        if (result.ProfileProposed)
        {
            if (result.Rules != null)
            {
                var profile = new ImportProfile(
                    _guidGenerator.Create(), _currentTenant.Id, ProfileName(document, item), document.SenderKey, item.Extracted.Format,
                    OrderIntakeConsts.TransportOrderTarget, result.Rules, result.Signature, ProfileOrigin.Ai, Cut(modelId, OrderIntakeConsts.MaxModelIdLength), document.Id);
                profile.RecordUse(_clock.Now);
                await _profiles.InsertAsync(profile, cancellationToken: cancellationToken);
                document.SetProfile(profile.Id, profile.Version);
                AddStep(document, IntakeStep.ProfileProposed, StepOutcome.Info, IntakeStepKeys.ProfileProposed, ("profileName", profile.Name));
                AddStep(document, IntakeStep.ProfileVerified, StepOutcome.Success, IntakeStepKeys.ProfileVerified);
            }
            else
            {
                AddStep(document, IntakeStep.ProfileVerificationFailed, StepOutcome.Warning, IntakeStepKeys.ProfileVerificationFailed,
                    ("differenceCount", Number(result.Differences.Count)), ("repaired", result.RepairAttempted ? "true" : "false"));
            }
        }

        if (result.TruncatedWithoutProfile)
        {
            AddStep(document, IntakeStep.Failed, StepOutcome.Warning, IntakeStepKeys.SpreadsheetTruncated, ("rowCount", Number(IntakeLearner.MaxSpreadsheetRows)));
            document.SetStatus(InboundDocumentStatus.NeedsAttention);
            return;
        }

        var trusted = false;
        await MapAndSubmitAsync(message, document, result.Method, result.Intakes, result.Confidence, trusted, defaults, cancellationToken);
    }

    /* ---------- Mapping and submitting ---------- */

    private async Task SubmitCanonicalAsync(InboundMessage message, InboundDocument document, OrderIntakeDto order, ChannelDefaults defaults, CancellationToken cancellationToken)
    {
        document.SetFormat(DocumentFormat.Json);
        document.SetFingerprint(defaults.SenderKey, null);
        /* The API's JSON is already the canonical model: nothing is interpreted, so it counts as trusted (design 72 "Intake API"). */
        await MapAndSubmitAsync(message, document, MappingMethod.Canonical, [IntakeMappings.Canonical(order)], 1m, profileTrusted: true, defaults, cancellationToken);
    }

    private async Task MapAndSubmitAsync(
        InboundMessage message, InboundDocument document, MappingMethod method, IReadOnlyList<ProfileRunIntake> intakes, decimal confidence,
        bool profileTrusted, ChannelDefaults defaults, CancellationToken cancellationToken)
    {
        if (intakes.Count == 0)
        {
            AddStep(document, IntakeStep.Mapped, StepOutcome.Warning, IntakeStepKeys.NoIntakes);
            document.SetStatus(InboundDocumentStatus.NeedsAttention);
            return;
        }

        var mapping = IntakeMappings.ToMapping(intakes, Math.Clamp(confidence, 0m, 1m));
        document.SetMapping(method, mapping);
        AddStep(document, IntakeStep.Mapped, StepOutcome.Success, IntakeStepKeys.Mapped, ("intakeCount", Number(intakes.Count)), ("method", method.ToString()));

        var anyFailed = false;
        var anyReview = false;
        foreach (var intake in mapping.Intakes)
        {
            var submission = new OrderIntakeSubmissionDto
            {
                SourceDocumentId = document.Id,
                IntakeIndex = intake.Index,
                Channel = message.Channel.ToString(),
                SourceKind = defaults.SourceKind,
                SenderAddress = Cut(message.From, IntakeConsts.MaxSenderAddressLength),
                Subject = Cut(message.Subject, IntakeConsts.MaxSubjectLength),
                FileName = document.FileName,
                Payload = IntakeMappings.FromPayload(intake.Payload),
                MappingConfidence = intake.Confidence,
                ProfileTrusted = profileTrusted,
                AutoConfirm = defaults.AutoConfirm,
                DefaultCustomerRelationId = defaults.DefaultCustomerRelationId,
                FixedCustomerRelationId = defaults.FixedCustomerRelationId,
                DefaultTransportOrderTypeId = defaults.DefaultTransportOrderTypeId
            };

            OrderIntakeSubmissionResultDto result;
            try
            {
                result = await _orderIntake.SubmitAsync(submission, cancellationToken);
            }
            catch (Exception ex) when (ex is BusinessException or AbpValidationException)
            {
                /* Refused before anything was written (validation, a business rule): the intake needs attention, the rest goes on. */
                var code = ex is IHasErrorCode { Code: { Length: > 0 } errorCode } ? errorCode : ex.GetType().Name;
                Logger.LogInformation("Orders refused intake {IntakeIndex} of document {InboundDocumentId}: {ErrorCode}.", intake.Index, document.Id, code);
                AddStep(document, IntakeStep.Failed, StepOutcome.Error, IntakeStepKeys.SubmitFailed, new StepIntakeReference(intake.Index),
                    ("intakeIndex", Number(intake.Index + 1)), ("reason", Cut(code, 200)!));
                anyFailed = true;
                continue;
            }

            AddStep(document, IntakeStep.Submitted, StepOutcome.Info, IntakeStepKeys.Submitted, new StepIntakeReference(intake.Index, result.IntakeId),
                ("intakeIndex", Number(intake.Index + 1)));
            switch (result.Status)
            {
                case OrderIntakeStatus.OrderCreated:
                    AddStep(document, IntakeStep.OrderCreated, StepOutcome.Success, IntakeStepKeys.OrderCreated,
                        new StepIntakeReference(intake.Index, result.IntakeId, result.TransportOrderId, Cut(result.OrderNumber, OrderIntakeConsts.MaxOrderNumberLength)),
                        ("orderNumber", result.OrderNumber ?? string.Empty), ("confirmed", result.Confirmed ? "true" : "false"));
                    break;
                case OrderIntakeStatus.Rejected:
                    AddStep(document, IntakeStep.Rejected, StepOutcome.Info, IntakeStepKeys.Rejected, new StepIntakeReference(intake.Index, result.IntakeId),
                        ("intakeIndex", Number(intake.Index + 1)));
                    break;
                default:
                    anyReview = true;
                    AddStep(document, IntakeStep.NeedsReview, StepOutcome.Warning, IntakeStepKeys.NeedsReview, new StepIntakeReference(intake.Index, result.IntakeId),
                        ("intakeIndex", Number(intake.Index + 1)), ("unresolvedCount", Number(result.Unresolved.Count)),
                        (IntakeResultMapper.UnresolvedParameter, JoinBounded(result.Unresolved, OrderIntakeConsts.MaxStepParameterValueLength)));
                    break;
            }
        }

        document.SetStatus(anyFailed ? InboundDocumentStatus.NeedsAttention : anyReview ? InboundDocumentStatus.NeedsReview : InboundDocumentStatus.Completed);
    }

    /* ---------- Helpers ---------- */

    private async Task<ChannelDefaults> GetDefaultsAsync(InboundMessage message)
    {
        if (message.Channel == InboundChannel.Api)
        {
            var client = await _apiClients.FindAsync(message.SourceId, includeDetails: false);
            return new ChannelDefaults(
                client == null ? null : DocumentFingerprinter.ApiSenderKey(client.ClientId),
                null, client?.CustomerRelationId, client?.DefaultTransportOrderTypeId, client?.AutoConfirm ?? false, OrderSource.Api);
        }

        var mailbox = await _mailboxes.FindAsync(message.SourceId, includeDetails: false);
        return new ChannelDefaults(
            DocumentFingerprinter.SenderKeyFromAddress(message.From), mailbox?.DefaultCustomerRelationId, null, mailbox?.DefaultTransportOrderTypeId,
            mailbox?.AutoConfirm ?? false, OrderSource.Import);
    }

    private async Task RecordRetryAsync(IEnumerable<AiWork> work, DateTime at, int attempt)
    {
        using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
        var ids = work.Select(w => w.DocumentId).ToList();
        var documents = await _documents.GetListAsync(d => ids.Contains(d.Id), includeDetails: true);
        foreach (var document in documents)
        {
            document.AddStep(_guidGenerator.Create(), at, IntakeStep.AiFailed, StepOutcome.Warning, IntakeStepKeys.AiFailedRetry,
                Parameters(("attempt", Number(attempt)), ("maxAttempts", Number(MaxAiAttempts)), (TransientParameter, "true")));
            await _documents.UpdateAsync(document);
        }

        await unitOfWork.CompleteAsync();
    }

    /// <summary>The transient AI failures since the document was last reprocessed (the retry budget of design 72 §5).</summary>
    private static int TransientFailuresSinceReset(InboundDocument document)
    {
        var lastReset = document.Steps.Where(s => s.Step == IntakeStep.Reprocessed).Select(s => (DateTime?)s.At).Max();
        return document.Steps.Count(s => s.Step == IntakeStep.AiFailed && s.Parameters.ContainsKey(TransientParameter) && (lastReset == null || s.At > lastReset));
    }

    private static LearningDocument ToLearningDocument(AiWork work, int index)
        => new()
        {
            Index = index,
            DocumentId = work.DocumentId,
            FileName = work.FileName,
            Extracted = work.Extracted,
            PdfContent = work.Extracted.Format is DocumentFormat.Pdf or DocumentFormat.ScannedPdf ? work.Content : null
        };

    private static string ProfileName(InboundDocument document, AiWork item)
    {
        var name = $"{document.SenderKey ?? "*"} · {item.Extracted.Format}";
        if (!string.IsNullOrWhiteSpace(document.FileName))
        {
            name += " · " + System.IO.Path.GetFileNameWithoutExtension(document.FileName);
        }

        return Cut(name, OrderIntakeConsts.MaxProfileNameLength)!;
    }

    private void Fail(InboundDocument document, IntakeStep step, string key, string reason, InboundDocumentStatus status = InboundDocumentStatus.Failed)
    {
        AddStep(document, step, StepOutcome.Error, key, ("reason", reason));
        document.SetStatus(status);
    }

    private void AddStep(InboundDocument document, IntakeStep step, StepOutcome outcome, string key, params (string Key, string Value)[] parameters)
        => document.AddStep(_guidGenerator.Create(), _clock.Now, step, outcome, key, Parameters(parameters));

    private void AddStep(InboundDocument document, IntakeStep step, StepOutcome outcome, string key, StepIntakeReference intake, params (string Key, string Value)[] parameters)
        => document.AddStep(_guidGenerator.Create(), _clock.Now, step, outcome, key, Parameters(parameters), intake: intake);

    private static Dictionary<string, string> Parameters(params (string Key, string Value)[] parameters)
        => parameters.ToDictionary(p => p.Key, p => Cut(p.Value, OrderIntakeConsts.MaxStepParameterValueLength) ?? string.Empty, StringComparer.Ordinal);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string JoinBounded(IEnumerable<string> values, int max)
    {
        var joined = string.Empty;
        foreach (var value in values)
        {
            var next = joined.Length == 0 ? value : joined + "," + value;
            if (next.Length > max)
            {
                break;
            }

            joined = next;
        }

        return joined;
    }

    private static string? Cut(string? text, int max) => text == null ? null : text.Length <= max ? text : text[..max];
}
