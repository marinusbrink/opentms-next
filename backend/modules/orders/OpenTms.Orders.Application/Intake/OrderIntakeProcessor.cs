using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Orders.Events;
using OpenTms.Orders.Localization;
using OpenTms.Orders.TransportOrders;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Orders.Intake;

/// <summary>
/// The steps the intake service (submit) and the review (resolve) share (design 72 "Orders: intake, resolution, review"):
/// resolve against master data, keep the resolution on the intake, learn the clerk's picks as aliases, and create the
/// order through <see cref="TransportOrderCommands.CreateAsync"/> — the one code path every order is built by — with the
/// OrderIntakeCompleted event. Runs inside the caller's transactional unit of work.
/// </summary>
public class OrderIntakeProcessor : ITransientDependency
{
    private static readonly IReadOnlyDictionary<string, Guid> NoPicks = new Dictionary<string, Guid>();

    private readonly OrderIntakeResolver _resolver;
    private readonly IntakeMasterDataCreator _creator;
    private readonly TransportOrderCommands _commands;
    private readonly OrdersReferences _references;
    private readonly OrdersClock _clock;
    private readonly IRepository<OrderIntake, Guid> _intakes;
    private readonly IRepository<IntakeAlias, Guid> _aliases;
    private readonly IDistributedEventBus _events;
    private readonly IGuidGenerator _guids;
    private readonly ICurrentTenant _currentTenant;
    private readonly IStringLocalizer<OrdersResource> _l;

    public ILogger<OrderIntakeProcessor> Logger { get; set; } = NullLogger<OrderIntakeProcessor>.Instance;

    public OrderIntakeProcessor(
        OrderIntakeResolver resolver,
        IntakeMasterDataCreator creator,
        TransportOrderCommands commands,
        OrdersReferences references,
        OrdersClock clock,
        IRepository<OrderIntake, Guid> intakes,
        IRepository<IntakeAlias, Guid> aliases,
        IDistributedEventBus events,
        IGuidGenerator guids,
        ICurrentTenant currentTenant,
        IStringLocalizer<OrdersResource> l)
    {
        _resolver = resolver;
        _creator = creator;
        _commands = commands;
        _references = references;
        _clock = clock;
        _intakes = intakes;
        _aliases = aliases;
        _events = events;
        _guids = guids;
        _currentTenant = currentTenant;
        _l = l;
    }

    // ── JSON of the payload ────────────────────────────────────────────────────

    public static string Serialize(OrderIntakeDto payload) => JsonSerializer.Serialize(payload, OrderIntakeJson.Options);

    public static OrderIntakeDto Deserialize(string json) => JsonSerializer.Deserialize<OrderIntakeDto>(json, OrderIntakeJson.Options) ?? new OrderIntakeDto();

    /// <summary>The same values, whatever the jsonb column did to the text's layout: both sides re-serialized.</summary>
    public static bool SameValues(OrderIntakeDto a, OrderIntakeDto b) => Serialize(a) == Serialize(b);

    // ── the steps ──────────────────────────────────────────────────────────────

    public Task<IntakeResolveResult> ResolveAsync(OrderIntake intake, OrderIntakeDto payload, IReadOnlyDictionary<string, Guid>? picks = null)
        => _resolver.ResolveAsync(new IntakeResolveRequest(payload, intake.Settings, picks ?? NoPicks));

    /// <summary>
    /// Creates in Master Data what the resolution planned (build note 26) — only call it when the resolution is complete,
    /// so nothing is created for an intake that goes to review anyway. Afterwards the result may be incomplete again (a
    /// refused write).
    /// </summary>
    public Task CreateMasterDataAsync(OrderIntake intake, IntakeResolveResult result)
        => result.IsComplete ? _creator.CreateAsync(intake, result) : Task.CompletedTask;

    /// <summary>The distinct Master Data rows the resolution found by a similar name and the ones the intake created (for the trace).</summary>
    public static (int Fuzzy, int Created) MasterDataCounts(IntakeResolution resolution)
        => (Distinct(resolution, IntakeResolutionMethod.Fuzzy), Distinct(resolution, IntakeResolutionMethod.Created));

    private static int Distinct(IntakeResolution resolution, IntakeResolutionMethod method)
        => resolution.Fields.Where(f => f.Status == IntakeResolutionStatus.Resolved && f.Method == method && f.TargetId != null).Select(f => f.TargetId).Distinct().Count();

    /// <summary>Keeps the resolution on the intake and counts the aliases it used.</summary>
    public async Task ApplyAsync(OrderIntake intake, OrderIntakeDto payload, IntakeResolveResult result)
    {
        intake.SetResolution(result.Resolution, payload.CustomerReference, result.CustomerRelationId, result.CustomerName);
        var used = result.UsedAliases.Distinct().ToList();
        foreach (var alias in used)
        {
            alias.Used();
        }

        if (used.Count > 0)
        {
            await _aliases.UpdateManyAsync(used);
        }
    }

    /// <summary>
    /// Stores the clerk's picks on open fields, and the fuzzy and created resolutions (build note 25), as aliases; a text
    /// already known takes the latest target. Saved at once, so the next intake of the same message resolves by alias.
    /// </summary>
    public async Task LearnAliasesAsync(IntakeResolveResult result)
    {
        foreach (var learn in result.AliasesToLearn.DistinctBy(a => (a.Kind, a.ScopeRelationId, a.Text)))
        {
            var existing = await _aliases.FindAsync(a => a.Kind == learn.Kind && a.ScopeRelationId == learn.ScopeRelationId && a.NormalizedText == learn.Text);
            if (existing != null)
            {
                existing.Retarget(learn.TargetId, learn.TargetRelationId);
                await _aliases.UpdateAsync(existing, autoSave: true);
            }
            else
            {
                await _aliases.InsertAsync(new IntakeAlias(_guids.Create(), _currentTenant.Id, learn.Kind, learn.ScopeRelationId, learn.Text, learn.TargetId, learn.TargetRelationId), autoSave: true);
            }
        }
    }

    /// <summary>
    /// The order from a complete resolution: Draft, or Confirmed with <paramref name="confirm"/>. With
    /// <paramref name="confirmOnlyWhenComplete"/> a confirm the type's rules refuse leaves a Draft (the automatic confirm);
    /// without it the refusal is the caller's error (a clerk's "Create and confirm").
    /// </summary>
    public async Task<TransportOrder> CreateOrderAsync(OrderIntake intake, OrderIntakeDto payload, IntakeResolveResult result, bool confirm, bool confirmOnlyWhenComplete)
    {
        var order = await BuildOrderAsync(intake, payload, result, confirm, confirmOnlyWhenComplete);
        return await CompleteAsync(intake, order);
    }

    /// <summary>
    /// <see cref="CreateOrderAsync"/> for a submission (design 72 build note 20): a business rule refusal while building
    /// the order is not the caller's error but a fact on the intake — it stays NeedsReview with the refusal on the
    /// pseudo-field <see cref="IntakeFieldPaths.Order"/>, and null is returned. Every rule of
    /// <see cref="TransportOrderCommands.CreateAsync"/> runs before its insert, so nothing of the order is written.
    /// </summary>
    public async Task<TransportOrder?> TryCreateOrderAsync(OrderIntake intake, OrderIntakeDto payload, IntakeResolveResult result, bool confirm, bool confirmOnlyWhenComplete)
    {
        TransportOrder order;
        try
        {
            order = await BuildOrderAsync(intake, payload, result, confirm, confirmOnlyWhenComplete);
        }
        catch (BusinessException e)
        {
            var code = string.IsNullOrWhiteSpace(e.Code) ? nameof(BusinessException) : e.Code;
            intake.RecordOrderRefused(code);
            await _intakes.UpdateAsync(intake, autoSave: true);
            Logger.LogInformation("Order intake {IntakeId} needs review: Orders refused the order with {Code}.", intake.Id, code);
            return null;
        }

        return await CompleteAsync(intake, order);
    }

    private async Task<TransportOrder> BuildOrderAsync(OrderIntake intake, OrderIntakeDto payload, IntakeResolveResult result, bool confirm, bool confirmOnlyWhenComplete)
    {
        if (!result.IsComplete || result.CustomerRelationId == null || result.TransportOrderTypeId == null)
        {
            throw new BusinessException(OrdersErrorCodes.IntakeUnresolved).WithData("fields", string.Join(", ", result.Resolution.BlockingFieldPaths()));
        }

        var orderDate = result.OrderDate ?? _clock.Today();
        var terms = await _references.DefaultTermsAsync(result.CustomerRelationId.Value, orderDate);
        var draft = BuildDraft(payload, result, orderDate, terms, _l["Orders:IntakeStopContact"]);
        return await _commands.CreateAsync(draft, intake.Source, intake.MappingConfidence, null, 0, confirm, intake.Id, confirmOnlyWhenComplete);
    }

    private async Task<TransportOrder> CompleteAsync(OrderIntake intake, TransportOrder order)
    {
        intake.MarkOrderCreated(order.Id);
        await _intakes.UpdateAsync(intake, autoSave: true);
        var confirmed = order.Status != TransportOrderStatus.Draft;
        await _events.PublishAsync(new OrderIntakeCompletedEto
        {
            IntakeId = intake.Id,
            SourceDocumentId = intake.SourceDocumentId,
            IntakeIndex = intake.IntakeIndex,
            TransportOrderId = order.Id,
            Confirmed = confirmed,
            Corrected = intake.Corrected
        });
        Logger.LogInformation("Order intake {IntakeId} became transport order {OrderId} ({Status}).", intake.Id, order.Id, order.Status);
        return order;
    }

    /// <summary>The order's draft from the payload and the resolved ids (design 58's one input shape).</summary>
    /// <param name="contactLabel">The localized label of the stop's contact line ("Contact", "Contactpersoon").</param>
    public static TransportOrderDraft BuildDraft(OrderIntakeDto payload, IntakeResolveResult result, DateOnly orderDate, FinancialTermsInput terms, string contactLabel)
    {
        var header = new OrderHeaderInput(
            result.TransportOrderTypeId!.Value,
            result.CustomerRelationId!.Value,
            null,
            string.Empty,
            orderDate,
            Text(payload.CustomerReference),
            null,
            Text(payload.Remarks),
            null);

        var lines = payload.Lines.Select((line, i) =>
        {
            var resolved = result.Lines[i];
            var freight = new FreightInput(
                resolved.TransportUnitId!.Value, resolved.ProductId, null, resolved.TemperatureId, null,
                line.Quantity, line.Positions, line.Colli, line.WeightKg, line.VolumeM3, line.LoadingMeters, null, null,
                line.LengthCm, line.WidthCm, line.HeightCm, null, LineRemarks(line), null, null, null, null,
                PaymentInstruction.NotSet, Text(line.ContainerNumber), null, null, null, null, Text(line.Barcode));
            var stops = line.Stops.Select((stop, j) =>
            {
                var place = resolved.Stops[j];
                return new StopInput(null, place.WaypointTypeId!.Value, place.RelationId!.Value, place.RelationAddressId!.Value, null, place.Date!.Value,
                    null, place.WindowFrom, place.WindowTill, Text(stop.Reference), StopRemarks(stop, contactLabel), null);
            }).ToList();
            return new LineInput(null, freight, [], [], stops, null);
        }).ToList();

        var references = payload.References
            .Where(r => !string.IsNullOrWhiteSpace(r.Type) && !string.IsNullOrWhiteSpace(r.Value))
            .Select(r => new ReferenceInput(r.Type.Trim(), r.Value.Trim(), null))
            .ToList();

        return new TransportOrderDraft(header, terms, [], lines, references, null, null);
    }

    /// <summary>
    /// The stop's remarks with the contact the document named appended as "{label}: {name}, {phone}" (only the parts
    /// present; the stop has no contact field of its own). Within the remarks' bound the appended part is cut, never the
    /// received remarks.
    /// </summary>
    public static string? StopRemarks(OrderIntakeStopDto stop, string contactLabel)
    {
        var remarks = Text(stop.Remarks);
        var parts = new[] { Text(stop.ContactName), Text(stop.ContactPhone) }.Where(p => p != null).ToList();
        if (parts.Count == 0)
        {
            return remarks;
        }

        var contact = contactLabel + ": " + string.Join(", ", parts);
        if (remarks == null)
        {
            return contact.Length > OrdersConsts.MaxStopRemarksLength ? contact[..OrdersConsts.MaxStopRemarksLength] : contact;
        }

        var room = OrdersConsts.MaxStopRemarksLength - remarks.Length - Environment.NewLine.Length;
        if (room <= 0)
        {
            return remarks;
        }

        return remarks + Environment.NewLine + (contact.Length > room ? contact[..room] : contact);
    }

    /// <summary>The line's description and remarks in the one remarks field the freight has.</summary>
    private static string? LineRemarks(OrderIntakeLineDto line)
    {
        var text = string.Join(Environment.NewLine, new[] { Text(line.Description), Text(line.Remarks) }.Where(t => t != null));
        if (text.Length == 0)
        {
            return null;
        }

        return text.Length > OrdersConsts.MaxRemarksLength ? text[..OrdersConsts.MaxRemarksLength] : text;
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
