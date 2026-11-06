using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using OpenTms.Financial.Tariffs;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Relations;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.DerivedValues;

namespace OpenTms.Integrations.AiConnector;

// ── paging ─────────────────────────────────────────────────────────────────────

/// <summary>A list tool's paging: mapped onto the grid's StartRow/EndRow; never more than <see cref="MaxTake"/> rows.</summary>
public abstract class AiToolListInput
{
    public const int DefaultTake = 25;
    public const int MaxTake = 50;
    public const int MaxSkip = 1_000_000;
    public const int MaxTextLength = 200;

    [Range(0, MaxSkip)]
    [Description("Rows to skip. Default 0.")]
    public int? Skip { get; set; }

    [Description("Rows to return. Default 25, at most 50; a larger value is capped.")]
    public int? Take { get; set; }

    [JsonIgnore]
    public int StartRow => Skip ?? 0;

    [JsonIgnore]
    public int EndRow => StartRow + Math.Clamp(Take ?? DefaultTake, 1, MaxTake);
}

/// <summary>The row count of a result, for the log line (design 74 "Logging"); never serialized.</summary>
public interface IAiToolRowCount
{
    [JsonIgnore]
    int RowCount { get; }
}

/// <summary><c>{ total, rows }</c>: <see cref="Total"/> is the grid's filtered count.</summary>
public class AiToolList<T> : IAiToolRowCount
{
    public long Total { get; set; }

    public List<T> Rows { get; set; } = new();

    [JsonIgnore]
    public int RowCount => Rows.Count;
}

// ── inputs ─────────────────────────────────────────────────────────────────────

public class SearchOrdersInput : AiToolListInput
{
    public TransportOrderStatus? Status { get; set; }

    [Description("On the order date, inclusive.")]
    public DateOnly? DateFrom { get; set; }

    [Description("On the order date, inclusive.")]
    public DateOnly? DateTo { get; set; }

    public Guid? CustomerRelationId { get; set; }

    [MaxLength(MaxTextLength)]
    [Description("Searches number, customer, customer reference, first and last stop and trip numbers.")]
    public string? Text { get; set; }
}

public class GetOrderInput
{
    public required Guid Id { get; set; }
}

public class SearchTripsInput : AiToolListInput
{
    public TransportTripStatus? Status { get; set; }

    [Description("On the trip date, inclusive.")]
    public DateOnly? DateFrom { get; set; }

    [Description("On the trip date, inclusive.")]
    public DateOnly? DateTo { get; set; }

    public Guid? CarrierRelationId { get; set; }

    [MaxLength(MaxTextLength)]
    [Description("Searches number, carrier, vehicles, licence plates, customers, order numbers and stop cities.")]
    public string? Text { get; set; }
}

public class GetTripInput
{
    public required Guid Id { get; set; }
}

public class GetLegsInput : AiToolListInput, IValidatableObject
{
    [Description("The order whose legs to list. Give orderId or tripId, not both.")]
    public Guid? OrderId { get; set; }

    [Description("The trip whose legs to list. Give orderId or tripId, not both.")]
    public Guid? TripId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if ((OrderId == null) == (TripId == null))
        {
            yield return new ValidationResult("Give exactly one of orderId and tripId.", [nameof(OrderId), nameof(TripId)]);
        }
    }
}

public class SearchRelationsInput : AiToolListInput
{
    [Description("Relations carrying any of these roles, comma separated: Customer, Carrier, Creditor, Depot, OwnCompany.")]
    public RelationRoles? Roles { get; set; }

    [MaxLength(MaxTextLength)]
    public string? Text { get; set; }
}

public class SearchVehiclesInput : AiToolListInput
{
    public Guid? VehicleTypeId { get; set; }

    [MaxLength(MaxTextLength)]
    [Description("Searches name and licence plate.")]
    public string? Text { get; set; }
}

public class GetOrderRatesInput
{
    public required Guid OrderId { get; set; }

    [Description("Revenue (default) or Cost. Cost lists the cost lines of the order's legs on the trips they are on now.")]
    public RateSide? Side { get; set; }
}

public class GetCustomerRevenueInput : IValidatableObject
{
    public required Guid CustomerRelationId { get; set; }

    [Description("On the order date, inclusive.")]
    public required DateOnly DateFrom { get; set; }

    [Description("On the order date, inclusive.")]
    public required DateOnly DateTo { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DateTo < DateFrom)
        {
            yield return new ValidationResult("dateTo must not be before dateFrom.", [nameof(DateFrom), nameof(DateTo)]);
        }
    }
}

// ── orders ─────────────────────────────────────────────────────────────────────

public class AiOrderRow
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public TransportOrderStatus Status { get; set; }
    public DateOnly OrderDate { get; set; }
    public Guid CustomerRelationId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerReference { get; set; }
    public string? TransportOrderTypeName { get; set; }
    public OrderSource Source { get; set; }
    public string? FirstStopName { get; set; }
    public string? FirstStopCity { get; set; }
    public DateOnly? FirstStopDate { get; set; }
    public string? LastStopName { get; set; }
    public string? LastStopCity { get; set; }
    public DateOnly? LastStopDate { get; set; }
    public int LineCount { get; set; }
    public int StopCount { get; set; }
    public string? TripNumbers { get; set; }
    public ExecutionProgress ExecutionProgress { get; set; }
    public decimal? Co2Kg { get; set; }
    public bool HasTripRequest { get; set; }
}

public class AiOrderReference
{
    public string? Type { get; set; }
    public string? Value { get; set; }
}

public class AiOrderDefaultStop
{
    public int Position { get; set; }
    public string? RelationName { get; set; }
    public string? WaypointTypeName { get; set; }
    public TimeOnly? WindowFrom { get; set; }
    public TimeOnly? WindowTill { get; set; }
}

public class AiOrderLine
{
    public int Position { get; set; }
    public string? ProductName { get; set; }
    public string? TransportUnitName { get; set; }
    public decimal Quantity { get; set; }
    public decimal? Colli { get; set; }
    public double? WeightKg { get; set; }
    public double? VolumeM3 { get; set; }
    public double? LoadingMeters { get; set; }
    public string? TemperatureName { get; set; }
    public string? VehicleTypeName { get; set; }
    public List<string> RequiredTransportCharacteristicNames { get; set; } = new();
    public string? ShipperRelationName { get; set; }
}

public class AiOrderDetail
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public TransportOrderStatus Status { get; set; }
    public DateOnly OrderDate { get; set; }
    public Guid CustomerRelationId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerReference { get; set; }
    public string? TransportOrderTypeName { get; set; }
    public OrderSource Source { get; set; }
    public string? FirstStopName { get; set; }
    public string? FirstStopCity { get; set; }
    public DateOnly? FirstStopDate { get; set; }
    public string? LastStopName { get; set; }
    public string? LastStopCity { get; set; }
    public DateOnly? LastStopDate { get; set; }
    public int LineCount { get; set; }
    public int StopCount { get; set; }
    public string? TripNumbers { get; set; }
    public ExecutionProgress ExecutionProgress { get; set; }
    public decimal? Co2Kg { get; set; }
    public List<AiOrderReference> References { get; set; } = new();
    public List<AiOrderDefaultStop> DefaultStops { get; set; } = new();
    public List<AiOrderLine> Lines { get; set; } = new();
}

// ── trips ──────────────────────────────────────────────────────────────────────

public class AiTripWarning
{
    public TripWarningCode Code { get; set; }
}

public class AiTripRow
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public TransportTripStatus Status { get; set; }
    public TripExecution Execution { get; set; }
    public DateOnly TripDate { get; set; }
    public string? TransportTripTypeName { get; set; }
    public Guid? CarrierRelationId { get; set; }
    public string? CarrierName { get; set; }
    public List<string> Customers { get; set; } = new();
    public List<string> OrderNumbers { get; set; } = new();
    public List<string> Vehicles { get; set; } = new();
    public string? FirstStopCity { get; set; }
    public string? LastStopCity { get; set; }
    public DateTimeOffset PlannedDeparture { get; set; }
    public DateTimeOffset? PlannedArrival { get; set; }
    public DateTimeOffset? ActualDeparture { get; set; }
    public DateTimeOffset? ActualArrival { get; set; }
    public int LegCount { get; set; }
    public int StopCount { get; set; }
    public decimal? LoadFactor { get; set; }
    public double? LoadingMeters { get; set; }
    public int? PlannedDistanceM { get; set; }
    public int? ActualDistanceM { get; set; }
    public int? PlannedDurationS { get; set; }
    public decimal? Co2Kg { get; set; }
    public List<AiTripWarning> Warnings { get; set; } = new();
}

public class AiTripVehicle
{
    public string? Name { get; set; }
    public string? LicencePlate { get; set; }
}

public class AiTripStop
{
    public int Position { get; set; }
    public string? Name { get; set; }
    public string? RelationName { get; set; }
    public string? City { get; set; }
    public DateTimeOffset? PlannedArrival { get; set; }
    public DateTimeOffset? PlannedDeparture { get; set; }
    public DateTimeOffset? EstimatedArrival { get; set; }
    public DateTimeOffset? EstimatedDeparture { get; set; }
    public DateTimeOffset? ActualArrival { get; set; }
    public DateTimeOffset? ActualDeparture { get; set; }
}

public class AiTripDetail
{
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public TransportTripStatus Status { get; set; }
    public TripExecution Execution { get; set; }
    public DateOnly TripDate { get; set; }
    public string? TransportTripTypeName { get; set; }
    public Guid? CarrierRelationId { get; set; }
    public string? CarrierName { get; set; }
    public string? VehicleCombinationName { get; set; }
    public List<AiTripVehicle> Vehicles { get; set; } = new();
    public DateTimeOffset PlannedDeparture { get; set; }
    public DateTimeOffset? PlannedArrival { get; set; }
    public DateTimeOffset? ActualDeparture { get; set; }
    public DateTimeOffset? ActualArrival { get; set; }
    public decimal? LoadFactor { get; set; }
    public double? LoadingMeters { get; set; }
    public int? PlannedDistanceM { get; set; }
    public int? ActualDistanceM { get; set; }
    public int? PlannedDurationS { get; set; }
    public decimal? Co2Kg { get; set; }
    public List<AiTripWarning> Warnings { get; set; } = new();
    public List<AiTripStop> Stops { get; set; } = new();
    public int LegCount { get; set; }
    public int StopCount { get; set; }
    public int OrderCount { get; set; }
    public decimal TotalQuantity { get; set; }
    public double TotalWeightKg { get; set; }
}

// ── legs ───────────────────────────────────────────────────────────────────────

public class AiLegEnd
{
    public string? Name { get; set; }
    public string? City { get; set; }
    public string? CountryCode { get; set; }
    public DateOnly PlannedDate { get; set; }
    public DateTimeOffset? WindowFrom { get; set; }
    public DateTimeOffset? WindowTill { get; set; }
}

public class AiLegRow
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public Guid? TripId { get; set; }
    public string? TripNumber { get; set; }
    public TransportTripStatus? TripStatus { get; set; }
    public int Position { get; set; }
    public string? CustomerName { get; set; }
    public AiLegEnd Origin { get; set; } = new();
    public AiLegEnd Destination { get; set; } = new();
    public decimal Quantity { get; set; }
    public double? WeightKg { get; set; }
    public double? VolumeM3 { get; set; }
    public double? LoadingMeters { get; set; }
    public int? DistanceM { get; set; }
    public int? DurationS { get; set; }
    public decimal? EstimatedCo2Kg { get; set; }
    public bool IsViaLeg { get; set; }
}

// ── master data ────────────────────────────────────────────────────────────────

public class AiRelationRow
{
    public Guid Id { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? City { get; set; }
    public string? CountryCode { get; set; }
    public RelationRoles Roles { get; set; }
    public List<string> CategoryNames { get; set; } = new();
    public bool IsBlocked { get; set; }
    public bool IsArchived { get; set; }
}

public class AiVehicleRow
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public VehicleTypeKind Kind { get; set; }
    public string? LicencePlate { get; set; }
    public string? RegistrationCountryCode { get; set; }
    public string? VehicleTypeName { get; set; }
    public FuelType? FuelType { get; set; }
    public EuroClass? EuroClass { get; set; }
    public string? OwnerName { get; set; }
    public string? HomeDepotName { get; set; }
    public string? CombinationName { get; set; }
    public bool IsArchived { get; set; }
}

// ── money ──────────────────────────────────────────────────────────────────────

public class AiRateTotals
{
    public decimal LinesAmount { get; set; }
    public decimal DiscountsAndSurchargesAmount { get; set; }
    public decimal AdditionalServicesAmount { get; set; }
    public decimal Total { get; set; }
    public bool HasFixedPrice { get; set; }
}

public class AiRateLine
{
    public RateSide Side { get; set; }
    public RateLineKind Kind { get; set; }
    public string? AdditionalServiceName { get; set; }
    public string? DiscountOrSurchargeName { get; set; }
    public string? PriceListCode { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public string? CurrencyCode { get; set; }
    public bool IsManual { get; set; }
    public string? TripNumber { get; set; }
}

public class AiOrderRates
{
    public string? CurrencyCode { get; set; }

    /// <summary>Whether the stored lines are up to date; null when no calculation was requested yet.</summary>
    public DerivedValueStatus? State { get; set; }

    public AiRateTotals Totals { get; set; } = new();

    public List<AiRateLine> Lines { get; set; } = new();
}

public class AiCustomerRevenueTotal
{
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal RevenueAmount { get; set; }
    public int OrderCount { get; set; }
}

public class AiCustomerRevenue
{
    public Guid CustomerRelationId { get; set; }
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }

    /// <summary>The fixed text every figure comes with.</summary>
    public string Definition { get; set; } = string.Empty;

    public List<AiCustomerRevenueTotal> Totals { get; set; } = new();

    /// <summary>Counted orders whose revenue is not calculated yet; they are in no total.</summary>
    public int OrdersWithoutAmount { get; set; }

    public int ExcludedDraftCount { get; set; }

    public int ExcludedCancelledCount { get; set; }
}

// ── metrics (design 75) ────────────────────────────────────────────────────────

public class ListMetricsInput
{
}

public class QueryMetricsInput
{
    [Description("Required: 1 to 5 metric keys from list_metrics, all order metrics or all trip metrics.")]
    public List<string>? Metrics { get; set; }

    [Description("Required. First day, inclusive: the order date for order metrics, the trip date for trip metrics.")]
    public DateOnly? DateFrom { get; set; }

    [Description("Required. Last day, inclusive, at most five years after dateFrom.")]
    public DateOnly? DateTo { get; set; }

    [Description("0 to 2 breakdowns list_metrics names for these metrics.")]
    public List<string>? GroupBy { get; set; }

    [Description("Filters list_metrics names for these metrics.")]
    public QueryMetricsFilters? Filters { get; set; }

    [Description("A chosen metric, largest first, or a period among groupBy (day, week, month), oldest first. Default: the first metric, largest first.")]
    public string? Sort { get; set; }

    [Description("Groups to return. Default 25, at most 100; a larger value is capped.")]
    public int? Top { get; set; }
}

public class QueryMetricsFilters
{
    [Description("Statuses to count, replacing the default: orders Open, ReadyForInvoicing, Invoiced and Archived; trips every status except Cancelled.")]
    public List<string>? Status { get; set; }

    [Description("Order metrics. Find the id with search_relations.")]
    public Guid? CustomerRelationId { get; set; }

    [Description("Order metrics.")]
    public Guid? TransportOrderTypeId { get; set; }

    public Guid? OrganizationUnitId { get; set; }

    [Description("Trip metrics. Find the id with search_relations.")]
    public Guid? CarrierRelationId { get; set; }

    [Description("Trip metrics. Find the id with search_vehicles.")]
    public Guid? VehicleId { get; set; }

    [Description("Trip metrics.")]
    public Guid? TransportTripTypeId { get; set; }

    [Description("Trip metrics: OwnFleet or Subcontracted.")]
    public string? Execution { get; set; }
}

public class AiMetricList : IAiToolRowCount
{
    public List<AiMetricListItem> Metrics { get; set; } = new();

    public bool FactsReady { get; set; }

    [JsonIgnore]
    public int RowCount => Metrics.Count;
}

public class AiMetricListItem
{
    public string Key { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Definition { get; set; } = string.Empty;

    public List<string> Breakdowns { get; set; } = new();

    public List<string> Filters { get; set; } = new();
}

public class AiMetricAnswer : IAiToolRowCount
{
    public string Status { get; set; } = string.Empty;

    public string? Message { get; set; }

    public string? Hint { get; set; }

    public AiMetricPeriod? Period { get; set; }

    public List<AiMetricDefinitionText>? Definitions { get; set; }

    public List<AiMetricRow>? Rows { get; set; }

    public long? TotalGroups { get; set; }

    public bool? Truncated { get; set; }

    public AiMetricCoverage? Coverage { get; set; }

    public DateTime? LastFactUpdateAt { get; set; }

    public List<string>? Warnings { get; set; }

    public long? FactsMissing { get; set; }

    [JsonIgnore]
    public int RowCount => Rows?.Count ?? 0;
}

public class AiMetricPeriod
{
    public DateOnly DateFrom { get; set; }

    public DateOnly DateTo { get; set; }
}

public class AiMetricDefinitionText
{
    public string Metric { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public class AiMetricRow
{
    public List<AiMetricGroup> Groups { get; set; } = new();

    public string? CurrencyCode { get; set; }

    /// <summary>Per chosen metric; a null stays in the answer, because no value is not zero.</summary>
    public Dictionary<string, decimal?> Values { get; set; } = new();
}

public class AiMetricGroup
{
    public string Breakdown { get; set; } = string.Empty;

    public Guid? Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public class AiMetricCoverage
{
    public long RecordsIncluded { get; set; }

    public long RecordsWithoutValue { get; set; }

    public long ExcludedByStatus { get; set; }
}
