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
