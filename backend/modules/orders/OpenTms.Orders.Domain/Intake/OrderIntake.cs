using System;
using System.Linq;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Orders.Intake;

/// <summary>
/// One intake handed to Orders by Integrations' pipeline or intake API (design 72 "Orders: intake, resolution, review"):
/// the values as received (<see cref="Payload"/>, Orders' canonical <c>OrderIntakeDto</c> as JSON — the domain does not
/// reference the application contracts), their resolution against master data, and what became of it: an order, the
/// review queue, or a rejection. (<see cref="SourceDocumentId"/>, <see cref="IntakeIndex"/>) is the natural key
/// (constitution rule 3). The customer reference, the customer and the open-field count are kept as columns too, so the
/// review list searches and shows them without reading the jsonb.
/// </summary>
public class OrderIntake : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    /// <summary>Integrations' inbound document the intake was read from.</summary>
    public Guid SourceDocumentId { get; private set; }

    public int IntakeIndex { get; private set; }

    public string Channel { get; private set; } = default!;

    public OrderSource Source { get; private set; }

    public string? SenderAddress { get; private set; }

    public string? Subject { get; private set; }

    public string? FileName { get; private set; }

    /// <summary>The values as received (JSON of <c>OrderIntakeDto</c>).</summary>
    public string Payload { get; private set; } = default!;

    /// <summary>The values as a clerk corrected them (JSON of <c>OrderIntakeDto</c>); null while nothing was corrected.</summary>
    public string? ReviewedPayload { get; private set; }

    public IntakeResolution Resolution { get; private set; } = new();

    public OrderIntakeStatus Status { get; private set; } = OrderIntakeStatus.NeedsReview;

    public Guid? TransportOrderId { get; private set; }

    public decimal MappingConfidence { get; private set; }

    public bool ProfileTrusted { get; private set; }

    public bool AutoConfirm { get; private set; }

    public Guid? DefaultCustomerRelationId { get; private set; }

    public Guid? FixedCustomerRelationId { get; private set; }

    public Guid? DefaultTransportOrderTypeId { get; private set; }

    public string? RejectReason { get; private set; }

    /// <summary>A clerk changed received values before the order was created.</summary>
    public bool Corrected { get; private set; }

    /// <summary>The customer reference of the effective payload, for the list's search.</summary>
    public string? CustomerReference { get; private set; }

    /// <summary>The resolved customer, when the resolution found one.</summary>
    public Guid? CustomerRelationId { get; private set; }

    /// <summary>The resolved customer's name, else the name as received.</summary>
    public string? CustomerName { get; private set; }

    /// <summary>The fields still Ambiguous or Unresolved after the last resolution.</summary>
    public int OpenFieldCount { get; private set; }

    protected OrderIntake()
    {
    }

    public OrderIntake(Guid id, Guid? tenantId, Guid sourceDocumentId, int intakeIndex, string channel, OrderSource source, string payload, OrderIntakeChannelSettings settings)
        : base(id)
    {
        if (sourceDocumentId == Guid.Empty)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "sourceDocumentId");
        }

        if (intakeIndex < 0)
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "intakeIndex").WithData("value", intakeIndex);
        }

        if (source is not (OrderSource.Import or OrderSource.Api))
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "source").WithData("value", source.ToString());
        }

        Check.NotNull(settings, nameof(settings));
        if (settings.MappingConfidence is < 0 or > 1)
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "mappingConfidence").WithData("value", settings.MappingConfidence);
        }

        TenantId = tenantId;
        SourceDocumentId = sourceDocumentId;
        IntakeIndex = intakeIndex;
        Channel = Check.NotNullOrWhiteSpace(channel, nameof(channel), IntakeConsts.MaxChannelLength).Trim();
        Source = source;
        Payload = Check.NotNullOrWhiteSpace(payload, nameof(payload));
        SenderAddress = Text(settings.SenderAddress, IntakeConsts.MaxSenderAddressLength);
        Subject = Text(settings.Subject, IntakeConsts.MaxSubjectLength);
        FileName = Text(settings.FileName, IntakeConsts.MaxFileNameLength);
        MappingConfidence = settings.MappingConfidence;
        ProfileTrusted = settings.ProfileTrusted;
        AutoConfirm = settings.AutoConfirm;
        DefaultCustomerRelationId = settings.DefaultCustomerRelationId;
        FixedCustomerRelationId = settings.FixedCustomerRelationId;
        DefaultTransportOrderTypeId = settings.DefaultTransportOrderTypeId;
    }

    /// <summary>The settings the resolver applies, as the channel gave them.</summary>
    public OrderIntakeChannelSettings Settings => new(SenderAddress, Subject, FileName, MappingConfidence, ProfileTrusted, AutoConfirm,
        DefaultCustomerRelationId, FixedCustomerRelationId, DefaultTransportOrderTypeId);

    public void EnsureInReview()
    {
        if (Status != OrderIntakeStatus.NeedsReview)
        {
            throw new BusinessException(OrdersErrorCodes.IntakeNotInReview).WithData("status", Status.ToString());
        }
    }

    /// <summary>The outcome of a resolver run with the facts the list shows.</summary>
    public void SetResolution(IntakeResolution resolution, string? customerReference, Guid? customerRelationId, string? customerName)
    {
        Resolution = Check.NotNull(resolution, nameof(resolution));
        CustomerReference = Text(customerReference, OrdersConsts.MaxCustomerReferenceLength);
        CustomerRelationId = customerRelationId;
        CustomerName = Text(customerName, IntakeConsts.MaxNameLength);
        OpenFieldCount = resolution.OpenFieldCount;
    }

    /// <summary>A clerk corrected received values: kept beside the original, which never changes.</summary>
    public void Review(string reviewedPayload)
    {
        EnsureInReview();
        ReviewedPayload = Check.NotNullOrWhiteSpace(reviewedPayload, nameof(reviewedPayload));
        Corrected = true;
    }

    public void MarkOrderCreated(Guid transportOrderId)
    {
        EnsureInReview();
        if (transportOrderId == Guid.Empty)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "transportOrderId");
        }

        TransportOrderId = transportOrderId;
        Status = OrderIntakeStatus.OrderCreated;
    }

    /// <summary>
    /// Orders refused to create the order from a complete resolution (a business rule on the received values): the intake
    /// stays in review with the refusal on the pseudo-field <see cref="IntakeFieldPaths.Order"/>, Unresolved and carrying
    /// the error code, so the review shows it and the caller's unresolved list names it (design 72 build note 20).
    /// </summary>
    public void RecordOrderRefused(string errorCode)
    {
        EnsureInReview();
        var code = Check.NotNullOrWhiteSpace(errorCode, nameof(errorCode)).Trim();
        var fields = Resolution.Fields.Where(f => f.FieldPath != IntakeFieldPaths.Order).ToList();
        fields.Add(new IntakeFieldResolution
        {
            FieldPath = IntakeFieldPaths.Order,
            Status = IntakeResolutionStatus.Unresolved,
            ErrorCode = code.Length > IntakeConsts.MaxErrorCodeLength ? code[..IntakeConsts.MaxErrorCodeLength] : code
        });
        /* A new object, so the jsonb column is written whatever value comparer it carries. */
        Resolution = new IntakeResolution { Fields = fields };
        OpenFieldCount = Resolution.OpenFieldCount;
    }

    public void Reject(string reason)
    {
        EnsureInReview();
        RejectReason = Check.NotNullOrWhiteSpace(reason, nameof(reason), IntakeConsts.MaxRejectReasonLength).Trim();
        Status = OrderIntakeStatus.Rejected;
    }

    private static string? Text(string? value, int maxLength)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length > maxLength ? text[..maxLength] : text;
    }
}

/// <summary>What the channel told Orders about one intake besides its values (design 72 <c>OrderIntakeSubmissionDto</c>).</summary>
public sealed record OrderIntakeChannelSettings(
    string? SenderAddress,
    string? Subject,
    string? FileName,
    decimal MappingConfidence,
    bool ProfileTrusted,
    bool AutoConfirm,
    Guid? DefaultCustomerRelationId,
    Guid? FixedCustomerRelationId,
    Guid? DefaultTransportOrderTypeId);
