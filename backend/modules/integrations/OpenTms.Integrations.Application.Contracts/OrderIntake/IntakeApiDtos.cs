using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Orders.Intake;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// The intake API (design 72 "Intake API"), route root <c>/api/integrations/intake</c>, served by
/// <c>IntakeApiController</c>: authorize <c>Integrations.IntakeApi.Submit</c>, feature <c>Integrations.OrderIntake</c>, and
/// the principal must carry the <c>opentms_api_client_id</c> of an active client of the current tenant (the tenant
/// comes from the token, never from the payload). The caller's ExternalId is its idempotency key.
/// </summary>
public interface IIntakeApiAppService : IApplicationService
{
    /// <summary>
    /// <c>POST /orders</c>: records the JSON order as an inbound message and processes it in the request (no AI, no
    /// profile). The same ExternalId again returns the stored result and creates nothing.
    /// </summary>
    Task<IntakeResultDto> SubmitOrderAsync(IntakeOrderRequestDto input);

    /// <summary><c>POST /documents</c> (multipart, ≤ 25 MB): recorded like a mail with one attachment and processed by the pipeline job (202).</summary>
    Task<IntakeResultDto> SubmitDocumentAsync(string externalId, IRemoteStreamContent file);

    /// <summary><c>GET /messages/{externalId}</c>: the result of an earlier call; null when this client sent no such message.</summary>
    Task<IntakeResultDto?> GetByExternalIdAsync(string externalId);
}

public class IntakeOrderRequestDto
{
    /// <summary>The caller's idempotency key.</summary>
    [Required, MaxLength(OrderIntakeConsts.MaxApiExternalIdLength)]
    public string ExternalId { get; set; } = string.Empty;

    [Required]
    public OrderIntakeDto Order { get; set; } = new();
}

/// <summary>What became of a message; for a document still in the pipeline <see cref="Intakes"/> is empty.</summary>
public class IntakeResultDto
{
    public Guid InboundMessageId { get; set; }

    public InboundMessageStatus Status { get; set; }

    public List<IntakeResultItemDto> Intakes { get; set; } = new();
}

public class IntakeResultItemDto
{
    /// <summary>Null when Orders did not accept the intake.</summary>
    public Guid? IntakeId { get; set; }

    /// <summary>Orders' status of the intake; null when it was not submitted.</summary>
    public OrderIntakeStatus? Status { get; set; }

    public Guid? TransportOrderId { get; set; }

    public string? OrderNumber { get; set; }

    /// <summary>The field paths still unresolved, e.g. <c>lines[0].stops[1].address</c>.</summary>
    public List<string> Unresolved { get; set; } = new();
}
