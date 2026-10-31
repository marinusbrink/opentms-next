using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Orders.TransportOrders;

namespace OpenTms.Orders.TestData;

/// <summary>
/// What Orders generated in a "Reset to dataset" run (docs/designs/65-tenant-test-datasets.md), published for the
/// contributors after it (Planning &amp; Execution builds legs and trips from it without reading Orders' tables). Per
/// order the facts Planning's leg generation takes from Orders' pricing snapshot today (<c>PricingSnapshotDto</c>, mapped
/// by <c>OrderLegGeneration.ToInput</c>): the lines with their quantities and their stops in position order, each stop
/// with its place, coordinates, zone and its window already resolved to instants in the address's zone.
/// Every generated order is listed, Draft and Cancelled included: like <c>OrderLegGeneration.ToInput</c>, a consumer
/// treats those two as orders without legs.
/// </summary>
public sealed class OrdersTestDataset
{
    public List<TestTransportOrder> Orders { get; } = new();

    public List<Guid> TemplateIds { get; } = new();

    /// <summary>
    /// The tenant's waypoint types by id (seeded reference data): the activity, the successor a via depot departs with
    /// and the default stay, the facts Planning otherwise reads through <c>ITransportOrderLookup.GetWaypointTypesAsync</c>.
    /// </summary>
    public Dictionary<Guid, TestWaypointType> WaypointTypes { get; } = new();
}

public sealed record TestWaypointType(Guid Id, string Code, WaypointActivity Activity, Guid? SuccessiveWaypointTypeId, int? DefaultDurationMinutes);

/// <summary>A generated transport order; <see cref="Number"/> is null for a Draft (numbers are drawn on Confirm).</summary>
public sealed record TestTransportOrder(
    Guid Id,
    string? Number,
    TransportOrderStatus Status,
    DateOnly OrderDate,
    Guid CustomerRelationId,
    string CustomerName,
    string? CustomerReference,
    Guid? SourceTemplateId,
    IReadOnlyList<TestTransportOrderLine> Lines)
{
    /// <summary>Whether Planning would generate legs for it (confirmed and not cancelled).</summary>
    public bool HasLegs => Status is not (TransportOrderStatus.Draft or TransportOrderStatus.Cancelled);

    /// <summary>Every stop of every line, lines in position order, stops in position order (stops belong to a line).</summary>
    public IEnumerable<TestTransportOrderStop> Stops => Lines.SelectMany(l => l.Stops);
}

/// <summary>A line (freight item) with the quantities a leg carries (Planning's <c>LegQuantities</c>) and its own stops.</summary>
public sealed record TestTransportOrderLine(
    Guid Id,
    int Position,
    Guid TransportUnitId,
    Guid? ProductId,
    Guid? TemperatureId,
    decimal Quantity,
    double? WeightKg,
    double? VolumeM3,
    double? LoadingMeters,
    double? Epq,
    double? LoadFactor,
    IReadOnlyList<Guid> RequiredTransportCharacteristicIds,
    IReadOnlyList<TestTransportOrderStop> Stops);

/// <summary>
/// A stop of a line, shaped like Planning's <c>LegEndSnapshot</c>: the window is an instant pair resolved in
/// <see cref="TimeZoneId"/> (a time slot or an explicit window), null for a whole-day stop.
/// </summary>
public sealed record TestTransportOrderStop(
    Guid Id,
    int Position,
    Guid WaypointTypeId,
    WaypointActivity Activity,
    Guid RelationId,
    Guid RelationAddressId,
    string Name,
    string City,
    string CountryCode,
    double Latitude,
    double Longitude,
    string TimeZoneId,
    DateOnly Date,
    DateTimeOffset? WindowFrom,
    DateTimeOffset? WindowTill,
    string? Reference,
    int? PlannedDurationMin);
