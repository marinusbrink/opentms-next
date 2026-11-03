using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTms.Integrations.Features;
using OpenTms.Integrations.OrderIntake.Ai;
using OpenTms.Integrations.OrderIntake.Extraction;
using OpenTms.Integrations.OrderIntake.Profiles;
using OpenTms.Orders.Intake;
using OpenTms.Orders.Published;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Tracing;
using Volo.Abp.Uow;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// The pipeline job of one inbound message (design 72 "The pipeline", AD-11/AD-13): runs <see cref="IInboundMessageProcessor"/>
/// explicitly in the tenant of its arguments (constitution rule 1) with the correlation id of the request that recorded
/// the message. Idempotent: only Pending documents run. A transient AI failure is rethrown, so Hangfire retries it with
/// back-off; the processor stops rethrowing at its third attempt.
/// </summary>
public class InboundMessageProcessingJob : AsyncBackgroundJob<InboundMessageProcessingArgs>, ITransientDependency
{
    private readonly IInboundMessageProcessor _processor;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICorrelationIdProvider _correlationIds;

    public InboundMessageProcessingJob(IInboundMessageProcessor processor, ICurrentTenant currentTenant, ICorrelationIdProvider correlationIds)
    {
        _processor = processor;
        _currentTenant = currentTenant;
        _correlationIds = correlationIds;
    }

    public override async Task ExecuteAsync(InboundMessageProcessingArgs args)
    {
        using var correlation = _correlationIds.Change(string.IsNullOrWhiteSpace(args.CorrelationId) ? null : args.CorrelationId);
        using var tenant = _currentTenant.Change(args.TenantId);
        try
        {
            await _processor.ProcessAsync(args.InboundMessageId);
        }
        catch (IntakeAiRetryException ex)
        {
            Logger.LogWarning("Inbound message {InboundMessageId} (tenant {TenantId}): the language model is unavailable, attempt {Attempt}; Hangfire retries.",
                args.InboundMessageId, args.TenantId, ex.Attempt);
            throw;
        }
    }
}

/// <summary>
/// Repairs an import profile after a clerk corrected a value it read (design 72 §4 "Repair"; AI, feature
/// <c>Integrations.OrderIntake.AiMapping</c>): the document, the current rules and the reviewed values
/// (<c>IOrderIntakeService.GetReviewedPayloadAsync</c>) go to the language model; the executor's output must equal the
/// reviewed values on the corrected fields, then the profile gets its next version (Origin Repair, Proposed). Idempotent:
/// once a repair step exists for the intake, or the profile moved to another version, nothing runs. Tokens are spent
/// only on corrections; the call runs outside any open transaction.
/// </summary>
public class ImportProfileRepairJob : AsyncBackgroundJob<ImportProfileRepairArgs>, ITransientDependency
{
    private readonly IRepository<ImportProfile, Guid> _profiles;
    private readonly IRepository<InboundDocument, Guid> _documents;
    private readonly IBlobContainer<InboundDocumentContainer> _blobs;
    private readonly DocumentExtractionService _extraction;
    private readonly IntakeLearner _learner;
    private readonly IOrderIntakeService _orderIntake;
    private readonly IFeatureChecker _features;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICorrelationIdProvider _correlationIds;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;

    public ImportProfileRepairJob(
        IRepository<ImportProfile, Guid> profiles,
        IRepository<InboundDocument, Guid> documents,
        IBlobContainer<InboundDocumentContainer> blobs,
        DocumentExtractionService extraction,
        IntakeLearner learner,
        IOrderIntakeService orderIntake,
        IFeatureChecker features,
        IUnitOfWorkManager unitOfWorkManager,
        ICurrentTenant currentTenant,
        ICorrelationIdProvider correlationIds,
        IGuidGenerator guidGenerator,
        IClock clock)
    {
        _profiles = profiles;
        _documents = documents;
        _blobs = blobs;
        _extraction = extraction;
        _learner = learner;
        _orderIntake = orderIntake;
        _features = features;
        _unitOfWorkManager = unitOfWorkManager;
        _currentTenant = currentTenant;
        _correlationIds = correlationIds;
        _guidGenerator = guidGenerator;
        _clock = clock;
    }

    public override async Task ExecuteAsync(ImportProfileRepairArgs args)
    {
        using var correlation = _correlationIds.Change(string.IsNullOrWhiteSpace(args.CorrelationId) ? null : args.CorrelationId);
        using var tenant = _currentTenant.Change(args.TenantId);
        if (!await _features.IsEnabledAsync(IntegrationsFeatures.OrderIntake) || !await _features.IsEnabledAsync(IntegrationsFeatures.OrderIntakeAiMapping))
        {
            Logger.LogInformation("Repair of import profile {ImportProfileId} skipped: AI mapping is off for tenant {TenantId}.", args.ImportProfileId, args.TenantId);
            return;
        }

        LearningDocument? document = null;
        ImportProfileRules? rules = null;
        List<string> signature = [];
        OrderIntakeDto? reviewed = null;
        List<string> corrected = [];
        IntakeLearningContextDto? context = null;
        var previousFailures = 0;
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: false), requiresNew: true))
        {
            var profile = await _profiles.FindAsync(args.ImportProfileId);
            var inbound = await _documents.FindAsync(args.InboundDocumentId, includeDetails: true);
            if (profile == null || profile.Status == ImportProfileStatus.Retired || profile.Version != args.ProfileVersion || inbound == null
                || inbound.Steps.Any(s => s.IntakeId == args.IntakeId && s.MessageKey is IntakeStepKeys.ProfileRepaired or IntakeStepKeys.ProfileRepairFailed))
            {
                Logger.LogInformation("Repair of import profile {ImportProfileId} for intake {IntakeId}: nothing to do.", args.ImportProfileId, args.IntakeId);
                await unitOfWork.CompleteAsync();
                return;
            }

            previousFailures = inbound.Steps.Count(s => s.IntakeId == args.IntakeId && s.Step == IntakeStep.AiFailed);
            reviewed = await _orderIntake.GetReviewedPayloadAsync(args.IntakeId);
            var original = inbound.Mapping?.Intakes.FirstOrDefault(i => i.Index == args.IntakeIndex);
            if (reviewed != null && original != null)
            {
                corrected = IntakeComparison.CompareOne(IntakeMappings.FromPayload(original.Payload), reviewed).Select(d => d.Path).ToList();
            }

            var content = await _blobs.GetAllBytesOrNullAsync(inbound.BlobName);
            if (reviewed == null || corrected.Count == 0 || content == null)
            {
                Logger.LogInformation("Repair of import profile {ImportProfileId} for intake {IntakeId}: no reviewed values to learn from.", args.ImportProfileId, args.IntakeId);
                await unitOfWork.CompleteAsync();
                return;
            }

            ExtractedDocument extracted;
            try
            {
                extracted = _extraction.Extract(content, inbound.Format, inbound.Kind);
            }
            catch (DocumentExtractionException)
            {
                await unitOfWork.CompleteAsync();
                return;
            }

            document = new LearningDocument
            {
                Index = 1,
                DocumentId = inbound.Id,
                FileName = inbound.FileName,
                Extracted = extracted,
                PdfContent = extracted.Format is DocumentFormat.Pdf ? content : null
            };
            rules = profile.Rules;
            signature = profile.Signature.ToList();
            context = await _orderIntake.GetLearningContextAsync();
            await unitOfWork.CompleteAsync();
        }

        RepairOutcome outcome;
        try
        {
            outcome = await _learner.RepairAsync(document!, rules!, signature, args.IntakeIndex, reviewed!, corrected, context!);
        }
        catch (LanguageModelException ex) when (ex.IsTransient && previousFailures + 1 < InboundMessageProcessor.MaxAiAttempts)
        {
            await AddStepAsync(args, IntakeStep.AiFailed, StepOutcome.Warning, IntakeStepKeys.AiFailedRetry, null,
                ("attempt", (previousFailures + 1).ToString(CultureInfo.InvariantCulture)), ("maxAttempts", InboundMessageProcessor.MaxAiAttempts.ToString(CultureInfo.InvariantCulture)));
            throw;
        }
        catch (LanguageModelException)
        {
            outcome = new RepairOutcome { FailureCode = "unavailable" };
        }

        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true))
        {
            var profile = await _profiles.FindAsync(args.ImportProfileId);
            var inbound = await _documents.FindAsync(args.InboundDocumentId, includeDetails: true);
            if (profile != null && inbound != null)
            {
                var reference = new StepIntakeReference(args.IntakeIndex, args.IntakeId);
                if (outcome.Call != null)
                {
                    inbound.AddStep(_guidGenerator.Create(), _clock.Now, IntakeStep.AiCompleted, StepOutcome.Success, IntakeStepKeys.AiCompleted,
                        new Dictionary<string, string> { ["model"] = outcome.Call.Usage.ModelId, ["documentCount"] = "1" }, aiUsage: outcome.Call.Usage, intake: reference);
                }

                if (outcome.Rules != null && profile.Version == args.ProfileVersion && profile.Status != ImportProfileStatus.Retired)
                {
                    profile.NextVersion(outcome.Rules, outcome.Signature, ProfileOrigin.Repair, outcome.ModelId);
                    await _profiles.UpdateAsync(profile);
                    inbound.AddStep(_guidGenerator.Create(), _clock.Now, IntakeStep.ProfileProposed, StepOutcome.Success, IntakeStepKeys.ProfileRepaired,
                        new Dictionary<string, string> { ["profileName"] = profile.Name, ["version"] = profile.Version.ToString(CultureInfo.InvariantCulture) }, intake: reference);
                    Logger.LogInformation("Import profile {ImportProfileId} repaired to version {Version}.", profile.Id, profile.Version);
                }
                else
                {
                    inbound.AddStep(_guidGenerator.Create(), _clock.Now, IntakeStep.ProfileVerificationFailed, StepOutcome.Warning, IntakeStepKeys.ProfileRepairFailed,
                        new Dictionary<string, string>
                        {
                            ["profileName"] = profile.Name,
                            ["differenceCount"] = outcome.Differences.Count.ToString(CultureInfo.InvariantCulture),
                            ["reason"] = outcome.FailureCode ?? "differences"
                        }, intake: reference);
                }

                await _documents.UpdateAsync(inbound);
            }

            await unitOfWork.CompleteAsync();
        }
    }

    private async Task AddStepAsync(ImportProfileRepairArgs args, IntakeStep step, StepOutcome outcome, string key, StepAiUsage? usage, params (string Key, string Value)[] parameters)
    {
        using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
        var inbound = await _documents.FindAsync(args.InboundDocumentId, includeDetails: true);
        if (inbound != null)
        {
            inbound.AddStep(_guidGenerator.Create(), _clock.Now, step, outcome, key, parameters.ToDictionary(p => p.Key, p => p.Value), aiUsage: usage,
                intake: new StepIntakeReference(args.IntakeIndex, args.IntakeId));
            await _documents.UpdateAsync(inbound);
        }

        await unitOfWork.CompleteAsync();
    }
}
