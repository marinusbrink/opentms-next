using System;
using System.Collections.Generic;
using OpenTms.PlanningExecution.TransportTrips;

namespace OpenTms.PlanningExecution.Events;

/*
 * The facts Planning & Execution publishes (design 56 "Ownership", design 59 "Published contract and events"). Outbox,
 * ids only, idempotent handlers: Orders stores progress and trip numbers, Financial recalculates the trip's cost lines
 * and the orders' lines when distances change.
 */

/// <summary>The legs of an order were (re)generated from its stops; distances follow in the background.</summary>
public class LegsGeneratedEto
{
    public Guid OrderId { get; set; }

    public List<Guid> LegIds { get; set; } = new();
}

public class LegAssignedEto
{
    public Guid LegId { get; set; }

    public Guid TripId { get; set; }

    public Guid OrderId { get; set; }
}

public class LegUnassignedEto
{
    public Guid LegId { get; set; }

    public Guid TripId { get; set; }

    public Guid OrderId { get; set; }
}

/// <summary>A trip exists; when created from an order's trip request it carries that order and the agreed carrier price.</summary>
public class TransportTripCreatedEto
{
    public Guid TripId { get; set; }

    public Guid? CreatedFromOrderId { get; set; }

    public decimal? AgreedPurchaseAmount { get; set; }

    public string? AgreedPurchaseCurrencyCode { get; set; }
}

/// <summary>
/// The trip request of a confirmed order was not honoured (design 59 build note 13): a field of the request does not
/// resolve in Planning (trip type, relation, address, vehicle, combination or driver), or its freight items name no leg
/// of the order (field "lineIds", design 61 point 7); no trip was created and the order's legs stay unplanned. Orders shows it as a warning on the order. Carries the field name, never values.
/// </summary>
public class TransportTripRequestNotHonouredEto
{
    public Guid OrderId { get; set; }

    /// <summary>The request field that did not resolve, e.g. "transportTripTypeId", "vehicleIds" or "lineIds".</summary>
    public string? Field { get; set; }

    /// <summary>The error code Planning met (today always the reference-data "ReferenceNotFound").</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>The trip's header, carrier, vehicles, drivers, distance or stops changed.</summary>
public class TransportTripChangedEto
{
    public Guid TripId { get; set; }

    public TripChangeKind Change { get; set; }
}

/// <summary>The set of legs on the trip changed; the orders named are those whose legs moved on or off.</summary>
public class TransportTripLegsChangedEto
{
    public Guid TripId { get; set; }

    public List<Guid> OrderIds { get; set; } = new();
}

public class TransportTripStatusChangedEto
{
    public Guid TripId { get; set; }

    public TransportTripStatus Status { get; set; }

    public List<Guid> OrderIds { get; set; } = new();
}

/// <summary>The trip calculator stored new values (times, distances, CO₂, allocation); the orders' summaries follow.</summary>
public class TransportTripRecalculatedEto
{
    public Guid TripId { get; set; }

    public long Version { get; set; }

    public List<Guid> OrderIds { get; set; } = new();
}
