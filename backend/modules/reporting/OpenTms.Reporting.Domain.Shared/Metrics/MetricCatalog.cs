using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.TransportTrips;

namespace OpenTms.Reporting.Metrics;

/// <summary>What a metric counts: one row per order or one row per trip. One query never mixes the two.</summary>
public enum MetricGrain
{
    Order = 1,
    Trip = 2
}

public static class MetricUnits
{
    public const string Count = "count";
    public const string Money = "money";
    public const string Kilograms = "kg";
    public const string Kilometres = "km";
    public const string Ratio = "ratio";
    public const string Percent = "percent";
}

public static class MetricKeys
{
    public const string OrderCount = "order_count";
    public const string OrderRevenue = "order_revenue";
    public const string OrderCost = "order_cost";
    public const string OrderMargin = "order_margin";
    public const string OrderCo2Kg = "order_co2_kg";

    public const string TripCount = "trip_count";
    public const string TripCost = "trip_cost";
    public const string TripRevenue = "trip_revenue";
    public const string TripMargin = "trip_margin";
    public const string TripCo2Kg = "trip_co2_kg";
    public const string AverageLoadFactor = "average_load_factor";
    public const string PlannedDistanceKm = "planned_distance_km";
    public const string ActualDistanceKm = "actual_distance_km";
    public const string OnTimeStopPercentage = "on_time_stop_percentage";
}

public static class MetricBreakdowns
{
    public const string Customer = "customer";
    public const string OrderType = "order_type";
    public const string OrganizationUnit = "organization_unit";
    public const string Status = "status";
    public const string Day = "day";
    public const string Week = "week";
    public const string Month = "month";
    public const string Carrier = "carrier";
    public const string Vehicle = "vehicle";
    public const string TripType = "trip_type";
    public const string Execution = "execution";

    public static bool IsPeriod(string breakdown) => breakdown is Day or Week or Month;
}

public static class MetricFilters
{
    public const string Status = "status";
    public const string CustomerRelationId = "customerRelationId";
    public const string TransportOrderTypeId = "transportOrderTypeId";
    public const string OrganizationUnitId = "organizationUnitId";
    public const string CarrierRelationId = "carrierRelationId";
    public const string VehicleId = "vehicleId";
    public const string TransportTripTypeId = "transportTripTypeId";
    public const string Execution = "execution";
}

/// <param name="Key">The name the tools use.</param>
/// <param name="Grain">Order or trip.</param>
/// <param name="Unit">One of <see cref="MetricUnits"/>.</param>
/// <param name="IsMoney">Behind <c>Financial.RateLines</c> in every form.</param>
/// <param name="Decimals">The rounding of an answer's value.</param>
/// <param name="Description">What the metric is, in a line.</param>
/// <param name="Definition">The exact rule, stated with every answer.</param>
/// <param name="SplitsByCurrency">The answer has one row per the order's currency, on top of the breakdowns.</param>
public sealed record MetricDefinition(
    string Key,
    MetricGrain Grain,
    string Unit,
    bool IsMoney,
    int Decimals,
    string Description,
    string Definition,
    bool SplitsByCurrency = false)
{
    /// <summary>Money the calculators have already combined, so it may mix currencies (design 75 "Currency").</summary>
    public bool CanMixCurrencies => IsMoney && !SplitsByCurrency;
}

/// <summary>
/// The fixed list of metrics, breakdowns and filters (design 75 "The catalog", AD-23). Its names are not secret; only values
/// are protected. The permissions and features of a metric follow from its grain and <see cref="MetricDefinition.IsMoney"/>
/// in <c>MetricAccessRules</c>, next to the permission constants this project cannot see.
/// </summary>
public static class MetricCatalog
{
    /// <summary>A stop is on time at most this long after its current planned arrival; this version, for all tenants.</summary>
    public const int OnTimeToleranceMinutes = 15;

    public const int MaxMetrics = 5;
    public const int MaxBreakdowns = 2;
    public const int MaxPeriodYears = 5;
    public const int DefaultTop = 25;
    public const int MaxTop = 100;

    private const string ByOrderDate = " Orders count by order date.";
    private const string ByTripDate = " Trips count by trip date.";
    private const string NotConverted = " Amounts are not converted: the stored value may combine several currencies, and the answer then warns mixed_currencies.";

    /* The vehicle breakdown counts a trip once (design 75 "Assumptions"), which every trip metric's rule has to say. */
    private const string VehicleRule = " Per vehicle, a trip counts once under the vehicle in first position, so a trailer's numbers appear only when it is first.";

    public static readonly IReadOnlyList<TransportOrderStatus> DefaultOrderStatuses =
    [
        TransportOrderStatus.Open, TransportOrderStatus.ReadyForInvoicing, TransportOrderStatus.Invoiced, TransportOrderStatus.Archived
    ];

    public static readonly IReadOnlyList<TransportTripStatus> DefaultTripStatuses =
        Enum.GetValues<TransportTripStatus>().Where(s => s != TransportTripStatus.Cancelled).ToList();

    public static readonly IReadOnlyList<MetricDefinition> All =
    [
        new(MetricKeys.OrderCount, MetricGrain.Order, MetricUnits.Count, false, 0,
            "Number of transport orders.",
            "Number of orders." + ByOrderDate),
        new(MetricKeys.OrderRevenue, MetricGrain.Order, MetricUnits.Money, true, 2,
            "Calculated revenue of transport orders, per currency.",
            "Sum of the revenue calculated and stored on orders. Not invoiced revenue. One row per the order's own currency; never add rows of different currencies." + ByOrderDate,
            SplitsByCurrency: true),
        new(MetricKeys.OrderCost, MetricGrain.Order, MetricUnits.Money, true, 2,
            "Cost of revenue of transport orders.",
            "Sum of the cost of revenue stored on orders, which comes from their trips." + NotConverted + ByOrderDate),
        new(MetricKeys.OrderMargin, MetricGrain.Order, MetricUnits.Money, true, 2,
            "Margin of transport orders.",
            "Sum of the margin stored on orders: calculated revenue minus cost of revenue, for orders that have both." + NotConverted + ByOrderDate),
        new(MetricKeys.OrderCo2Kg, MetricGrain.Order, MetricUnits.Kilograms, false, 3,
            "CO2 of transport orders, in kg.",
            "Sum of the estimated CO2 stored on orders, in kg." + ByOrderDate),

        new(MetricKeys.TripCount, MetricGrain.Trip, MetricUnits.Count, false, 0,
            "Number of trips.",
            "Number of trips." + ByTripDate + VehicleRule),
        new(MetricKeys.TripCost, MetricGrain.Trip, MetricUnits.Money, true, 2,
            "Cost of trips.",
            "Sum of the cost stored on trips." + NotConverted + ByTripDate + VehicleRule),
        new(MetricKeys.TripRevenue, MetricGrain.Trip, MetricUnits.Money, true, 2,
            "Revenue allocated to trips.",
            "Sum of the order revenue allocated to trips and stored on them. Not invoiced revenue. A trip without an allocation stores 0, which counts as 0 here; see trip_margin." + NotConverted + ByTripDate + VehicleRule),
        new(MetricKeys.TripMargin, MetricGrain.Trip, MetricUnits.Money, true, 2,
            "Margin of trips.",
            "trip_revenue minus trip_cost, only over trips with a cost and with revenue above 0, because a missing allocation is stored as 0. Coverage counts the others as without a value." + NotConverted + ByTripDate + VehicleRule),
        new(MetricKeys.TripCo2Kg, MetricGrain.Trip, MetricUnits.Kilograms, false, 3,
            "CO2 of trips, in kg.",
            "Sum of the estimated CO2 stored on trips, in kg." + ByTripDate + VehicleRule),
        new(MetricKeys.AverageLoadFactor, MetricGrain.Trip, MetricUnits.Ratio, false, 4,
            "Average load factor of trips.",
            "Plain average over trips with a load factor, not weighted by distance; 1 is a full vehicle." + ByTripDate + VehicleRule),
        new(MetricKeys.PlannedDistanceKm, MetricGrain.Trip, MetricUnits.Kilometres, false, 3,
            "Planned distance of trips, in km.",
            "Sum of the planned distance stored on trips, in km." + ByTripDate + VehicleRule),
        new(MetricKeys.ActualDistanceKm, MetricGrain.Trip, MetricUnits.Kilometres, false, 3,
            "Actual distance of trips, in km.",
            "Sum of the actual distance stored on trips, in km." + ByTripDate + VehicleRule),
        new(MetricKeys.OnTimeStopPercentage, MetricGrain.Trip, MetricUnits.Percent, false, 2,
            "Share of stops reached on time.",
            "Stops at most 15 minutes after the current planned arrival, as a percentage of stops with both a planned and an actual arrival. A recalculation can move the plan, so this is not the first promise made. Coverage counts the trips without such a stop." + ByTripDate + VehicleRule),
    ];

    private static readonly Dictionary<string, MetricDefinition> ByKey = All.ToDictionary(m => m.Key, StringComparer.Ordinal);

    public static readonly IReadOnlyList<string> OrderBreakdowns =
    [
        MetricBreakdowns.Customer, MetricBreakdowns.OrderType, MetricBreakdowns.OrganizationUnit, MetricBreakdowns.Status,
        MetricBreakdowns.Day, MetricBreakdowns.Week, MetricBreakdowns.Month
    ];

    public static readonly IReadOnlyList<string> TripBreakdowns =
    [
        MetricBreakdowns.Carrier, MetricBreakdowns.Vehicle, MetricBreakdowns.TripType, MetricBreakdowns.Execution,
        MetricBreakdowns.OrganizationUnit, MetricBreakdowns.Status, MetricBreakdowns.Day, MetricBreakdowns.Week, MetricBreakdowns.Month
    ];

    public static readonly IReadOnlyList<string> OrderFilters =
    [
        MetricFilters.Status, MetricFilters.CustomerRelationId, MetricFilters.TransportOrderTypeId, MetricFilters.OrganizationUnitId
    ];

    public static readonly IReadOnlyList<string> TripFilters =
    [
        MetricFilters.Status, MetricFilters.CarrierRelationId, MetricFilters.VehicleId, MetricFilters.TransportTripTypeId,
        MetricFilters.Execution, MetricFilters.OrganizationUnitId
    ];

    public static MetricDefinition? Find(string? key) => key != null && ByKey.TryGetValue(key, out var metric) ? metric : null;

    public static IReadOnlyList<string> BreakdownsOf(MetricGrain grain) => grain == MetricGrain.Order ? OrderBreakdowns : TripBreakdowns;

    public static IReadOnlyList<string> FiltersOf(MetricGrain grain) => grain == MetricGrain.Order ? OrderFilters : TripFilters;
}

/// <summary>The grouping dates a fact stores, so a period breakdown is a plain column.</summary>
public static class MetricPeriods
{
    /// <summary>The Monday of the date's week.</summary>
    public static DateOnly WeekOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public static DateOnly MonthOf(DateOnly date) => new(date.Year, date.Month, 1);
}
