using System;
using System.Collections.Generic;

namespace OpenTms.Financial.Tariffs.Published;

/// <summary>
/// Everything the price resolver needs about one order (design 57 "The calculation"): the terms, the lines with their
/// quantities, the stops with the ids the lanes are matched on, and the legs' distances. Produced by Orders
/// (<c>IOrderPricingReader</c>) for the calculator, or by the screen for a preview of an unsaved change. Ids and numbers
/// only; names are not needed to price.
/// </summary>
public class PricingSnapshotDto
{
    public Guid? OrderId { get; set; }

    public Guid CustomerRelationId { get; set; }

    public DateOnly OrderDate { get; set; }

    public string CurrencyCode { get; set; } = "EUR";

    public Guid? VatPercentageId { get; set; }

    /// <summary>The revenue price lists applied to this order, in order of position (QP5).</summary>
    public List<Guid> PriceListIds { get; set; } = new();

    public MultipleMatches MultipleMatches { get; set; } = MultipleMatches.All;

    /// <summary>
    /// The bases the order is charged by (design 61 "Order page review" point 3). Each one without a list of that basis in
    /// <see cref="PriceListIds"/> gives every line an open line (kind PriceList, no price list, the line's quantity of the
    /// basis, unit price 0) until the user prices it; none while the order has a fixed price.
    /// </summary>
    public List<QuantitySource> ChargedBases { get; set; } = new();

    /// <summary>The discounts and surcharges selected on the order (defaults plus manual picks, minus removals).</summary>
    public List<Guid> DiscountOrSurchargeIds { get; set; } = new();

    /// <summary>The order-level fixed price, when the order has one (a rate line that overrides the sum beneath it, AD-08).</summary>
    public decimal? FixedPriceAmount { get; set; }

    public List<PricingLineDto> Lines { get; set; } = new();

    /// <summary>The order's status as an integer of <c>TransportOrderStatus</c> (Orders' enum), so Planning can refuse work on cancelled orders without referencing Orders here.</summary>
    public int Status { get; set; }
}

public class PricingLineDto
{
    public Guid? LineId { get; set; }

    public int Position { get; set; }

    public Guid TransportUnitId { get; set; }

    public Guid? ProductId { get; set; }

    public Guid? VehicleTypeId { get; set; }

    public decimal Quantity { get; set; }

    public decimal? Positions { get; set; }

    public decimal? Colli { get; set; }

    public double? WeightKg { get; set; }

    public double? VolumeM3 { get; set; }

    public double? LoadingMeters { get; set; }

    public double? LoadFactor { get; set; }

    public double? Epq { get; set; }

    /// <summary>The sum of the line's legs' distances, when Planning has routed them (metres).</summary>
    public int? DistanceM { get; set; }

    public int? TollDistanceM { get; set; }

    public int? DurationS { get; set; }

    public Guid? TemperatureId { get; set; }

    public List<Guid> RequiredTransportCharacteristicIds { get; set; } = new();

    /// <summary>The stops of the line in position order; the first and the last define the lane.</summary>
    public List<PricingStopDto> Stops { get; set; } = new();
}

public class PricingStopDto
{
    public Guid? StopId { get; set; }

    public int Position { get; set; }

    public Guid RelationId { get; set; }

    public Guid? RelationAddressId { get; set; }

    public string? CountryCode { get; set; }

    public string? PostalCode { get; set; }

    /// <summary>The geographical regions the stop's address lies in (Master Data), for region lanes and regional surcharges.</summary>
    public List<Guid> GeographicalRegionIds { get; set; } = new();

    public DateOnly Date { get; set; }

    /* The facts Planning's leg generator needs beyond pricing (design 59): the activity, the window, the planned stay. */

    public Guid WaypointTypeId { get; set; }

    public Guid? ContactPersonId { get; set; }

    public Guid? TimeSlotId { get; set; }

    public DateTimeOffset? WindowFrom { get; set; }

    public DateTimeOffset? WindowTill { get; set; }

    public string? Reference { get; set; }

    public string? Remarks { get; set; }

    public int? PlannedDurationMin { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? TimeZoneId { get; set; }

    public string? AddressName { get; set; }

    public string? City { get; set; }
}
