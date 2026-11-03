using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Integrations.Features;
using OpenTms.Integrations.OrderIntake.Profiles;
using OpenTms.Integrations.Settings;
using OpenTms.Orders.Events;
using OpenTms.Orders.Published;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Features;
using Volo.Abp.Guids;
using Volo.Abp.Settings;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// The Integrations side of Orders' intake facts (design 72 "Domain impact", §4 "Activation"): each adds its step to the
/// source document's trace once (at-least-once delivery, constitution rule 3: the step for that intake is the guard),
/// settles the document's and the message's status, and moves the counters of the import profile that read the
/// document (only for the version that read it). An intake a user resolved without corrections, and a Draft confirmed
/// unchanged, add a confirmation (the profile becomes Active at the tenant's threshold without corrections); a
/// correction (corrected review values, or a Draft edited before it was confirmed) adds a correction, sends an Active
/// profile back to Proposed and asks <see cref="ImportProfileRepairJob"/> for new rules (feature AiMapping). Handlers run
/// in the raising tenant inside the dispatcher's unit of work.
/// </summary>
public class OrderIntakeEventHandlers :
    IDistributedEventHandler<OrderIntakeCompletedEto>,
    IDistributedEventHandler<OrderIntakeRejectedEto>,
    IDistributedEventHandler<IntakeOrderConfirmedEto>,
    ITransientDependency
{
    private readonly IRepository<InboundDocument, Guid> _documents;
    private readonly IRepository<InboundMessage, Guid> _messages;
    private readonly IRepository<ImportProfile, Guid> _profiles;
    private readonly ITransportOrderLookup _orders;
    private readonly ISettingProvider _settings;
    private readonly IFeatureChecker _features;
    private readonly OrderIntakeJobQueue _jobs;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;

    public ILogger<OrderIntakeEventHandlers> Logger { get; set; } = NullLogger<OrderIntakeEventHandlers>.Instance;

    public OrderIntakeEventHandlers(
        IRepository<InboundDocument, Guid> documents,
        IRepository<InboundMessage, Guid> messages,
        IRepository<ImportProfile, Guid> profiles,
        ITransportOrderLookup orders,
        ISettingProvider settings,
        IFeatureChecker features,
        OrderIntakeJobQueue jobs,
        IGuidGenerator guidGenerator,
        IClock clock)
    {
        _documents = documents;
        _messages = messages;
        _profiles = profiles;
        _orders = orders;
        _settings = settings;
        _features = features;
        _jobs = jobs;
        _guidGenerator = guidGenerator;
        _clock = clock;
    }

    [UnitOfWork]
    public virtual async Task HandleEventAsync(OrderIntakeCompletedEto eventData)
    {
        var document = await _documents.FindAsync(eventData.SourceDocumentId, includeDetails: true);
        if (document == null || document.HasStep(IntakeStep.OrderCreated, eventData.IntakeId))
        {
            /* Unknown here, or created at submit (the pipeline wrote OrderCreated then): nothing more to record. */
            return;
        }

        var reference = new StepIntakeReference(eventData.IntakeIndex, eventData.IntakeId, eventData.TransportOrderId);
        if (document.HasStep(IntakeStep.NeedsReview, eventData.IntakeId) && !document.HasStep(IntakeStep.Resolved, eventData.IntakeId))
        {
            AddStep(document, IntakeStep.Resolved, StepOutcome.Success, IntakeStepKeys.Resolved, reference,
                ("intakeIndex", Number(eventData.IntakeIndex + 1)), ("corrected", eventData.Corrected ? "true" : "false"));
            await CountAsync(document, eventData.IntakeId, eventData.IntakeIndex, eventData.Corrected);
        }

        var number = await OrderNumberAsync(eventData.TransportOrderId);
        AddStep(document, IntakeStep.OrderCreated, StepOutcome.Success, IntakeStepKeys.OrderCreated,
            reference with { OrderNumber = Cut(number, OrderIntakeConsts.MaxOrderNumberLength) },
            ("orderNumber", number ?? string.Empty), ("confirmed", eventData.Confirmed ? "true" : "false"));
        await SettleAsync(document);
    }

    [UnitOfWork]
    public virtual async Task HandleEventAsync(OrderIntakeRejectedEto eventData)
    {
        var document = await _documents.FindAsync(eventData.SourceDocumentId, includeDetails: true);
        if (document == null || document.HasStep(IntakeStep.Rejected, eventData.IntakeId))
        {
            return;
        }

        AddStep(document, IntakeStep.Rejected, StepOutcome.Info, IntakeStepKeys.Rejected, new StepIntakeReference(eventData.IntakeIndex, eventData.IntakeId),
            ("intakeIndex", Number(eventData.IntakeIndex + 1)));
        await SettleAsync(document);
    }

    [UnitOfWork]
    public virtual async Task HandleEventAsync(IntakeOrderConfirmedEto eventData)
    {
        var document = await _documents.FindAsync(eventData.SourceDocumentId, includeDetails: true);
        if (document == null || document.HasStep(IntakeStep.OrderConfirmed, eventData.IntakeId))
        {
            return;
        }

        var intakeIndex = document.Steps.Where(s => s.IntakeId == eventData.IntakeId && s.IntakeIndex != null).Select(s => s.IntakeIndex!.Value).FirstOrDefault();
        var number = await OrderNumberAsync(eventData.TransportOrderId);
        AddStep(document, IntakeStep.OrderConfirmed, StepOutcome.Success, IntakeStepKeys.OrderConfirmed,
            new StepIntakeReference(intakeIndex, eventData.IntakeId, eventData.TransportOrderId, Cut(number, OrderIntakeConsts.MaxOrderNumberLength)),
            ("orderNumber", number ?? string.Empty), ("edited", eventData.EditedAfterIntake ? "true" : "false"));
        await CountAsync(document, eventData.IntakeId, intakeIndex, eventData.EditedAfterIntake);
        await _documents.UpdateAsync(document);
    }

    /// <summary>Moves the counters of the profile version that read the document; a correction also asks for a repair.</summary>
    private async Task CountAsync(InboundDocument document, Guid intakeId, int intakeIndex, bool corrected)
    {
        if (document.ImportProfileId == null || document.MappingMethod is not (MappingMethod.Profile or MappingMethod.AiVerifiedProfile))
        {
            return;
        }

        var profile = await _profiles.FindAsync(document.ImportProfileId.Value);
        if (profile == null || profile.Version != document.ImportProfileVersion || profile.Status == ImportProfileStatus.Retired)
        {
            return;
        }

        if (corrected)
        {
            profile.RecordCorrection();
            if (await _features.IsEnabledAsync(IntegrationsFeatures.OrderIntakeAiMapping))
            {
                await _jobs.EnqueueRepairAsync(profile.Id, profile.Version, document.Id, intakeId, intakeIndex);
            }
        }
        else
        {
            var threshold = Math.Clamp(
                await _settings.GetAsync(IntegrationsSettings.ProfileActivationThreshold, OrderIntakeConsts.DefaultProfileActivationThreshold),
                OrderIntakeConsts.MinProfileActivationThreshold, OrderIntakeConsts.MaxProfileActivationThreshold);
            profile.RecordConfirmation(threshold);
        }

        await _profiles.UpdateAsync(profile);
        Logger.LogInformation("Import profile {ImportProfileId} v{Version}: {Outcome} recorded for intake {IntakeId}.", profile.Id, profile.Version, corrected ? "correction" : "confirmation", intakeId);
    }

    /// <summary>The document is Completed once no intake waits for review any more; then the message rolls up.</summary>
    private async Task SettleAsync(InboundDocument document)
    {
        if (document.Status is InboundDocumentStatus.NeedsReview or InboundDocumentStatus.Submitted or InboundDocumentStatus.Completed)
        {
            var waiting = document.Steps
                .Where(s => s.IntakeIndex != null && s.Step is IntakeStep.NeedsReview or IntakeStep.OrderCreated or IntakeStep.Rejected)
                .GroupBy(s => s.IntakeIndex)
                .Any(g => g.OrderBy(s => s.At).ThenBy(s => s.Step).Last().Step == IntakeStep.NeedsReview);
            document.SetStatus(waiting ? InboundDocumentStatus.NeedsReview : InboundDocumentStatus.Completed);
        }

        await _documents.UpdateAsync(document);
        var message = await _messages.FindAsync(document.InboundMessageId);
        if (message != null)
        {
            var statuses = (await _documents.GetListAsync(d => d.InboundMessageId == message.Id)).Select(d => d.Id == document.Id ? document.Status : d.Status).ToList();
            message.RollUp(statuses);
            await _messages.UpdateAsync(message);
        }
    }

    private async Task<string?> OrderNumberAsync(Guid orderId)
        => (await _orders.GetAsync([orderId])).FirstOrDefault()?.Number;

    private void AddStep(InboundDocument document, IntakeStep step, StepOutcome outcome, string key, StepIntakeReference intake, params (string Key, string Value)[] parameters)
        => document.AddStep(_guidGenerator.Create(), _clock.Now, step, outcome, key, parameters.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal), intake: intake);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string? Cut(string? text, int max) => text == null ? null : text.Length <= max ? text : text[..max];
}
