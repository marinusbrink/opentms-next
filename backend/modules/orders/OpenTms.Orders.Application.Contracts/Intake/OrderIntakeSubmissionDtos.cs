using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using OpenTms.Orders.TransportOrders;

namespace OpenTms.Orders.Intake;

/// <summary>
/// One mapped intake handed to Orders by Integrations' pipeline (design 72 "Mapping, submit, trace"), once per intake in
/// the pipeline's transaction. (<see cref="SourceDocumentId"/>, <see cref="IntakeIndex"/>) is the natural key: the same
/// pair again returns the intake Orders already holds (constitution rule 3, reprocess).
/// </summary>
public class OrderIntakeSubmissionDto
{
    /// <summary>Integrations' inbound document.</summary>
    public Guid SourceDocumentId { get; set; }

    /// <summary>The position of the intake within its document (a spreadsheet may hold hundreds).</summary>
    [Range(0, int.MaxValue)]
    public int IntakeIndex { get; set; }

    /// <summary>The Integrations channel's name ("Gmail", "Microsoft365", "Api").</summary>
    [Required, MaxLength(IntakeConsts.MaxChannelLength)]
    public string Channel { get; set; } = string.Empty;

    /// <summary><see cref="OrderSource.Import"/> for mail and documents, <see cref="OrderSource.Api"/> for the API.</summary>
    public OrderSource SourceKind { get; set; } = OrderSource.Import;

    [MaxLength(IntakeConsts.MaxSenderAddressLength)]
    public string? SenderAddress { get; set; }

    [MaxLength(IntakeConsts.MaxSubjectLength)]
    public string? Subject { get; set; }

    [MaxLength(IntakeConsts.MaxFileNameLength)]
    public string? FileName { get; set; }

    [Required]
    public OrderIntakeDto Payload { get; set; } = new();

    /// <summary>0–1: how sure the mapping is (a profile run 1, an AI extraction without a verified profile at most 0.6).</summary>
    [Range(0, 1)]
    public decimal MappingConfidence { get; set; }

    /// <summary>The import profile that read the document is Active; only then may the order be confirmed automatically.</summary>
    public bool ProfileTrusted { get; set; }

    /// <summary>The channel's auto-confirm setting (mailbox connection or API client).</summary>
    public bool AutoConfirm { get; set; }

    /// <summary>The mailbox's default customer, used when nothing in the document resolves one.</summary>
    public Guid? DefaultCustomerRelationId { get; set; }

    /// <summary>The API client's customer: orders from that client are this customer's, whatever the payload says.</summary>
    public Guid? FixedCustomerRelationId { get; set; }

    public Guid? DefaultTransportOrderTypeId { get; set; }
}

/// <summary>What Orders made of one submitted intake; becomes the Submitted and OrderCreated/NeedsReview steps.</summary>
public class OrderIntakeSubmissionResultDto
{
    public Guid IntakeId { get; set; }

    public OrderIntakeStatus Status { get; set; }

    public Guid? TransportOrderId { get; set; }

    public string? OrderNumber { get; set; }

    /// <summary>The order was created Confirmed (Open) rather than Draft.</summary>
    public bool Confirmed { get; set; }

    /// <summary>The field paths still unresolved (empty once an order exists).</summary>
    public List<string> Unresolved { get; set; } = new();
}

/// <summary>
/// The names of the tenant's order types, waypoint types and transport units the AI may choose from (design 72 "The AI
/// step"). Names only, never ids or relation data (privacy note): the name in <see cref="Culture"/> plus the name in
/// every other language the record has.
/// </summary>
public class IntakeLearningContextDto
{
    /// <summary>The UI culture the primary names are in.</summary>
    public string Culture { get; set; } = string.Empty;

    public List<IntakeLearningChoiceDto> OrderTypes { get; set; } = new();

    public List<IntakeLearningWaypointTypeDto> WaypointTypes { get; set; } = new();

    public List<IntakeLearningChoiceDto> TransportUnits { get; set; } = new();
}

public class IntakeLearningChoiceDto
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The record's names in its other languages, distinct from <see cref="Name"/>.</summary>
    public List<string> OtherNames { get; set; } = new();
}

public class IntakeLearningWaypointTypeDto : IntakeLearningChoiceDto
{
    public WaypointActivity Activity { get; set; }
}
