using System;
using System.Collections.Generic;
using OpenTms.Orders.TransportOrders;

namespace OpenTms.Orders.Events;

/*
 * The facts Orders publishes (design 56 "Ownership", design 58 "Events"). Written to the outbox in the transaction of the
 * command that caused them (AD-16); ids only (constitution rule 5); names per AP-06 (OpenTms.Orders.<FactInPastTense>).
 * Handlers are idempotent (rule 3): Planning regenerates legs from the order's current stops, Financial requests the
 * order's rate lines for a new version.
 */

/// <summary>A Draft order was confirmed: it has its number; Planning generates its legs and, with a trip request, its trip.</summary>
public class TransportOrderConfirmedEto
{
    public Guid OrderId { get; set; }
}

/// <summary>Lines were added, changed or removed (quantities, products, units); pricing and legs follow.</summary>
public class TransportOrderLinesChangedEto
{
    public Guid OrderId { get; set; }

    public List<Guid> LineIds { get; set; } = new();
}

/// <summary>Stops of the named lines changed (places, dates, windows); Planning regenerates those lines' legs.</summary>
public class TransportOrderStopsChangedEto
{
    public Guid OrderId { get; set; }

    public List<Guid> LineIds { get; set; } = new();
}

/// <summary>Financial terms changed (price lists, discounts and surcharges, fixed price, VAT, currency); pricing follows.</summary>
public class TransportOrderFinancialTermsChangedEto
{
    public Guid OrderId { get; set; }
}

/// <summary>The order was cancelled or deleted: Planning unassigns and removes its legs, Financial keeps its lines.</summary>
public class TransportOrderCancelledEto
{
    public Guid OrderId { get; set; }
}

/// <summary>The order's status changed (Open, ReadyForInvoicing, Archived, …).</summary>
public class TransportOrderStatusChangedEto
{
    public Guid OrderId { get; set; }

    public TransportOrderStatus Status { get; set; }
}
