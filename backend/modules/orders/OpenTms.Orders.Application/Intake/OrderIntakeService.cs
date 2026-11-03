using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.MasterData.Published;
using OpenTms.Orders.Published;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Orders.Intake;

/// <summary>
/// See <see cref="IOrderIntakeService"/> (design 72, module boundary rule 3: Integrations' stated calls 1–3). Submit is
/// idempotent on (SourceDocumentId, IntakeIndex): the intake Orders already holds is answered unchanged, so a reprocessed
/// document or a retried API call creates nothing twice (constitution rule 3). Behaviour is gated on the tenant feature
/// <see cref="IntakeConsts.OrderIntakeFeature"/> (constitution rule 4).
/// </summary>
// Published in-process service: Integrations' pipeline submits from a Hangfire job without a user, so it is anonymous; the HTTP-facing caller (the intake API) authorizes.
[RemoteService(IsEnabled = false)]
[AllowAnonymous]
public class OrderIntakeService : OrdersAppServiceBase, IOrderIntakeService
{
    private readonly IRepository<OrderIntake, Guid> _intakes;
    private readonly IRepository<TransportOrder, Guid> _orders;
    private readonly IRepository<TransportOrderType, Guid> _orderTypes;
    private readonly IRepository<WaypointType, Guid> _waypointTypes;
    private readonly IMasterDataMatchQuery _match;
    private readonly OrderIntakeProcessor _processor;

    public OrderIntakeService(
        IRepository<OrderIntake, Guid> intakes,
        IRepository<TransportOrder, Guid> orders,
        IRepository<TransportOrderType, Guid> orderTypes,
        IRepository<WaypointType, Guid> waypointTypes,
        IMasterDataMatchQuery match,
        OrderIntakeProcessor processor)
    {
        _intakes = intakes;
        _orders = orders;
        _orderTypes = orderTypes;
        _waypointTypes = waypointTypes;
        _match = match;
        _processor = processor;
    }

    public virtual async Task<OrderIntakeSubmissionResultDto> SubmitAsync(OrderIntakeSubmissionDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input, nameof(input));
        Check.NotNull(input.Payload, nameof(input.Payload));
        await FeatureChecker.CheckEnabledAsync(IntakeConsts.OrderIntakeFeature);
        if (input.SourceKind is not (OrderSource.Import or OrderSource.Api))
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "sourceKind").WithData("value", input.SourceKind.ToString());
        }

        var existing = await _intakes.FindAsync(i => i.SourceDocumentId == input.SourceDocumentId && i.IntakeIndex == input.IntakeIndex, cancellationToken: cancellationToken);
        if (existing != null)
        {
            Logger.LogInformation("Order intake {IntakeId} for document {DocumentId} index {IntakeIndex} exists; answered unchanged.", existing.Id, input.SourceDocumentId, input.IntakeIndex);
            return await ResultAsync(existing, cancellationToken);
        }

        var settings = new OrderIntakeChannelSettings(input.SenderAddress, input.Subject, input.FileName, input.MappingConfidence, input.ProfileTrusted, input.AutoConfirm,
            input.DefaultCustomerRelationId, input.FixedCustomerRelationId, input.DefaultTransportOrderTypeId);
        var intake = new OrderIntake(GuidGenerator.Create(), CurrentTenant.Id, input.SourceDocumentId, input.IntakeIndex, input.Channel, input.SourceKind,
            OrderIntakeProcessor.Serialize(input.Payload), settings);

        var result = await _processor.ResolveAsync(intake, input.Payload);
        await _processor.ApplyAsync(intake, input.Payload, result);
        await _intakes.InsertAsync(intake, autoSave: true, cancellationToken: cancellationToken);

        TransportOrder? order = null;
        if (result.IsComplete)
        {
            /* Confirmed at once only when the profile is trusted, the channel auto-confirms and every field was resolved by a
             * trusted rule; the type's Confirm rules still apply, and a refusal leaves a Draft (design 72). */
            var autoConfirm = intake.ProfileTrusted && intake.AutoConfirm && result.ResolvedByTrustedRules;
            order = await _processor.CreateOrderAsync(intake, input.Payload, result, autoConfirm, confirmOnlyWhenComplete: true);
        }
        else
        {
            Logger.LogInformation("Order intake {IntakeId} for document {DocumentId} index {IntakeIndex} needs review ({OpenFields} open fields).",
                intake.Id, input.SourceDocumentId, input.IntakeIndex, intake.OpenFieldCount);
        }

        return new OrderIntakeSubmissionResultDto
        {
            IntakeId = intake.Id,
            Status = intake.Status,
            TransportOrderId = order?.Id,
            OrderNumber = order?.Number,
            Confirmed = order != null && order.Status != TransportOrderStatus.Draft,
            Unresolved = order == null ? intake.Resolution.BlockingFieldPaths() : []
        };
    }

    public virtual async Task<OrderIntakeDto?> GetReviewedPayloadAsync(Guid intakeId, CancellationToken cancellationToken = default)
    {
        var intake = await _intakes.FindAsync(intakeId, includeDetails: false, cancellationToken);
        return intake?.ReviewedPayload == null ? null : OrderIntakeProcessor.Deserialize(intake.ReviewedPayload);
    }

    public virtual async Task<IntakeLearningContextDto> GetLearningContextAsync(CancellationToken cancellationToken = default)
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        var orderTypes = (await _orderTypes.GetListAsync(t => !t.IsArchived, cancellationToken: cancellationToken)).OrderBy(t => t.Code).Take(IntakeConsts.MaxTypes);
        var waypointTypes = (await _waypointTypes.GetListAsync(t => !t.IsArchived, cancellationToken: cancellationToken)).OrderBy(t => t.Code).Take(IntakeConsts.MaxTypes);
        var units = await _match.GetReferenceNamesAsync(MasterDataReferenceKind.TransportUnit);

        return new IntakeLearningContextDto
        {
            Culture = culture,
            OrderTypes = orderTypes.Select(t => Choice<IntakeLearningChoiceDto>(t.Name.Resolve(culture), t.Name.Texts.Values)).ToList(),
            WaypointTypes = waypointTypes.Select(t =>
            {
                var choice = Choice<IntakeLearningWaypointTypeDto>(t.Name.Resolve(culture), t.Name.Texts.Values);
                choice.Activity = t.Activity;
                return choice;
            }).ToList(),
            TransportUnits = units.Select(u => new IntakeLearningChoiceDto { Name = u }).ToList()
        };
    }

    private static T Choice<T>(string name, IEnumerable<string> allNames)
        where T : IntakeLearningChoiceDto, new()
        => new() { Name = name, OtherNames = allNames.Where(n => !string.Equals(n, name, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToList() };

    private async Task<OrderIntakeSubmissionResultDto> ResultAsync(OrderIntake intake, CancellationToken cancellationToken)
    {
        string? number = null;
        var confirmed = false;
        if (intake.TransportOrderId is { } orderId)
        {
            var order = (await AsyncExecuter.ToListAsync((await _orders.GetQueryableAsync()).Where(o => o.Id == orderId).Select(o => new { o.Number, o.Status }), cancellationToken)).FirstOrDefault();
            number = order?.Number;
            confirmed = order != null && order.Status != TransportOrderStatus.Draft;
        }

        return new OrderIntakeSubmissionResultDto
        {
            IntakeId = intake.Id,
            Status = intake.Status,
            TransportOrderId = intake.TransportOrderId,
            OrderNumber = number,
            Confirmed = confirmed,
            Unresolved = intake.Status == OrderIntakeStatus.NeedsReview ? intake.Resolution.BlockingFieldPaths() : []
        };
    }
}
