using System;

namespace OpenTms.Orders.Events;

/*
 * The facts of the order intake (design 72 "Domain impact"): AP-06 names OpenTms.Orders.OrderIntakeCompleted,
 * OpenTms.Orders.OrderIntakeRejected and OpenTms.Orders.IntakeOrderConfirmed. Written to the outbox in the transaction
 * that caused them (AD-16); ids only (constitution rule 5). Integrations handles them idempotently (rule 3) on its step
 * log and its import-profile counters.
 */

/// <summary>An intake became an order (Draft or Confirmed). <see cref="Corrected"/>: a clerk changed received values first.</summary>
public class OrderIntakeCompletedEto
{
    public Guid IntakeId { get; set; }

    /// <summary>Integrations' inbound document the intake was read from.</summary>
    public Guid SourceDocumentId { get; set; }

    public int IntakeIndex { get; set; }

    public Guid TransportOrderId { get; set; }

    public bool Confirmed { get; set; }

    public bool Corrected { get; set; }
}

/// <summary>A clerk rejected an intake; no order was created.</summary>
public class OrderIntakeRejectedEto
{
    public Guid IntakeId { get; set; }

    public Guid SourceDocumentId { get; set; }

    public int IntakeIndex { get; set; }
}

/// <summary>A user confirmed a Draft order that came from an intake. <see cref="EditedAfterIntake"/>: the Draft was changed first.</summary>
public class IntakeOrderConfirmedEto
{
    public Guid IntakeId { get; set; }

    public Guid SourceDocumentId { get; set; }

    public Guid TransportOrderId { get; set; }

    public bool EditedAfterIntake { get; set; }
}
