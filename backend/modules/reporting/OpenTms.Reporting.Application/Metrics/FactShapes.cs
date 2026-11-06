using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Reporting.Facts;

namespace OpenTms.Reporting.Metrics;

/// <summary>What the query needs to know about one fact table: its columns per breakdown and metric, its filters and its status.</summary>
public interface IFactShape<TFact>
{
    IReadOnlyDictionary<string, MetricBreakdownColumn> Breakdowns { get; }

    IReadOnlyDictionary<string, MetricColumn<TFact>> Metrics { get; }

    Expression<Func<IGrouping<int, TFact>, MetricCoverageRow>> CoverageWithoutMissing { get; }

    Expression<Func<TFact, int>> StatusCode { get; }

    Expression<Func<TFact, string>>? Currency { get; }

    IQueryable<TFact> InPeriod(IQueryable<TFact> query, DateOnly from, DateOnly to);

    IQueryable<TFact> WithStatus(IQueryable<TFact> query, IReadOnlyCollection<int> statuses);

    IQueryable<TFact> Filtered(IQueryable<TFact> query, ParsedMetricFilters filters);
}

public sealed record ParsedMetricFilters(
    Guid? CustomerRelationId,
    Guid? TransportOrderTypeId,
    Guid? OrganizationUnitId,
    Guid? CarrierRelationId,
    Guid? VehicleId,
    Guid? TransportTripTypeId,
    TripExecution? Execution);

/// <summary>The order grain: the catalog's order metrics as SQL over <see cref="OrderFact"/>.</summary>
public sealed class OrderFactShape : IFactShape<OrderFact>
{
    public static readonly OrderFactShape Instance = new();

    public IReadOnlyDictionary<string, MetricBreakdownColumn> Breakdowns { get; } = new Dictionary<string, MetricBreakdownColumn>
    {
        [MetricBreakdowns.Customer] = new(MetricSlotKind.Id, (Expression<Func<OrderFact, Guid?>>)(f => f.CustomerRelationId)),
        [MetricBreakdowns.OrderType] = new(MetricSlotKind.Id, (Expression<Func<OrderFact, Guid?>>)(f => f.TransportOrderTypeId)),
        [MetricBreakdowns.OrganizationUnit] = new(MetricSlotKind.Id, (Expression<Func<OrderFact, Guid?>>)(f => f.OrganizationUnitId)),
        [MetricBreakdowns.Status] = new(MetricSlotKind.Code, (Expression<Func<OrderFact, int?>>)(f => (int?)f.Status)),
        [MetricBreakdowns.Day] = new(MetricSlotKind.Date, (Expression<Func<OrderFact, DateOnly?>>)(f => f.OrderDate)),
        [MetricBreakdowns.Week] = new(MetricSlotKind.Date, (Expression<Func<OrderFact, DateOnly?>>)(f => f.OrderWeek)),
        [MetricBreakdowns.Month] = new(MetricSlotKind.Date, (Expression<Func<OrderFact, DateOnly?>>)(f => f.OrderMonth)),
    };

    public IReadOnlyDictionary<string, MetricColumn<OrderFact>> Metrics { get; } = new Dictionary<string, MetricColumn<OrderFact>>
    {
        [MetricKeys.OrderCount] = new(g => g.Count(), null),
        /* EF turns a Sum over nothing into 0, and no value is not zero: each sum asks first whether the group has a value. */
        [MetricKeys.OrderRevenue] = new(g => g.Count(f => f.RevenueAmount != null) == 0 ? null : g.Sum(f => f.RevenueAmount), f => f.RevenueAmount == null),
        [MetricKeys.OrderCost] = new(g => g.Count(f => f.CostOfRevenueAmount != null) == 0 ? null : g.Sum(f => f.CostOfRevenueAmount), f => f.CostOfRevenueAmount == null),
        [MetricKeys.OrderMargin] = new(g => g.Count(f => f.MarginAmount != null) == 0 ? null : g.Sum(f => f.MarginAmount), f => f.MarginAmount == null),
        [MetricKeys.OrderCo2Kg] = new(g => g.Count(f => f.Co2Kg != null) == 0 ? null : g.Sum(f => f.Co2Kg), f => f.Co2Kg == null),
    };

    public Expression<Func<IGrouping<int, OrderFact>, MetricCoverageRow>> CoverageWithoutMissing { get; }
        = g => new MetricCoverageRow { Status = g.Key, All = g.LongCount(), LastUpdate = g.Max(f => f.FactUpdatedAt) };

    public Expression<Func<OrderFact, int>> StatusCode { get; } = f => (int)f.Status;

    public Expression<Func<OrderFact, string>>? Currency { get; } = f => f.CurrencyCode;

    public IQueryable<OrderFact> InPeriod(IQueryable<OrderFact> query, DateOnly from, DateOnly to)
        => query.Where(f => f.OrderDate >= from && f.OrderDate <= to);

    public IQueryable<OrderFact> WithStatus(IQueryable<OrderFact> query, IReadOnlyCollection<int> statuses)
    {
        var values = statuses.Select(s => (TransportOrderStatus)s).ToList();
        return query.Where(f => values.Contains(f.Status));
    }

    public IQueryable<OrderFact> Filtered(IQueryable<OrderFact> query, ParsedMetricFilters filters)
    {
        if (filters.CustomerRelationId is { } customer)
        {
            query = query.Where(f => f.CustomerRelationId == customer);
        }

        if (filters.TransportOrderTypeId is { } type)
        {
            query = query.Where(f => f.TransportOrderTypeId == type);
        }

        if (filters.OrganizationUnitId is { } unit)
        {
            query = query.Where(f => f.OrganizationUnitId == unit);
        }

        return query;
    }
}

/// <summary>The trip grain: the catalog's trip metrics as SQL over <see cref="TripFact"/>.</summary>
public sealed class TripFactShape : IFactShape<TripFact>
{
    public static readonly TripFactShape Instance = new();

    public IReadOnlyDictionary<string, MetricBreakdownColumn> Breakdowns { get; } = new Dictionary<string, MetricBreakdownColumn>
    {
        [MetricBreakdowns.Carrier] = new(MetricSlotKind.Id, (Expression<Func<TripFact, Guid?>>)(f => f.CarrierRelationId)),
        [MetricBreakdowns.Vehicle] = new(MetricSlotKind.Id, (Expression<Func<TripFact, Guid?>>)(f => f.PrimaryVehicleId)),
        [MetricBreakdowns.TripType] = new(MetricSlotKind.Id, (Expression<Func<TripFact, Guid?>>)(f => f.TransportTripTypeId)),
        [MetricBreakdowns.OrganizationUnit] = new(MetricSlotKind.Id, (Expression<Func<TripFact, Guid?>>)(f => f.OrganizationUnitId)),
        [MetricBreakdowns.Execution] = new(MetricSlotKind.Code, (Expression<Func<TripFact, int?>>)(f => (int?)f.Execution)),
        [MetricBreakdowns.Status] = new(MetricSlotKind.Code, (Expression<Func<TripFact, int?>>)(f => (int?)f.Status)),
        [MetricBreakdowns.Day] = new(MetricSlotKind.Date, (Expression<Func<TripFact, DateOnly?>>)(f => f.TripDate)),
        [MetricBreakdowns.Week] = new(MetricSlotKind.Date, (Expression<Func<TripFact, DateOnly?>>)(f => f.TripWeek)),
        [MetricBreakdowns.Month] = new(MetricSlotKind.Date, (Expression<Func<TripFact, DateOnly?>>)(f => f.TripMonth)),
    };

    public IReadOnlyDictionary<string, MetricColumn<TripFact>> Metrics { get; } = new Dictionary<string, MetricColumn<TripFact>>
    {
        [MetricKeys.TripCount] = new(g => g.Count(), null),
        /* EF turns a Sum over nothing into 0, and no value is not zero: each sum asks first whether the group has a value. */
        [MetricKeys.TripCost] = new(g => g.Count(f => f.CostAmount != null) == 0 ? null : g.Sum(f => f.CostAmount), f => f.CostAmount == null),
        [MetricKeys.TripRevenue] = new(g => g.Count(f => f.RevenueAmount != null) == 0 ? null : g.Sum(f => f.RevenueAmount), f => f.RevenueAmount == null),
        /* Only trips with a cost and with revenue above 0: a missing allocation is stored as 0. */
        [MetricKeys.TripMargin] = new(
            g => g.Count(f => f.CostAmount != null && f.RevenueAmount > 0) == 0
                ? null
                : g.Where(f => f.CostAmount != null && f.RevenueAmount > 0).Sum(f => f.RevenueAmount - f.CostAmount),
            f => f.CostAmount == null || f.RevenueAmount == null || f.RevenueAmount <= 0),
        [MetricKeys.TripCo2Kg] = new(g => g.Count(f => f.Co2Kg != null) == 0 ? null : g.Sum(f => f.Co2Kg), f => f.Co2Kg == null),
        [MetricKeys.AverageLoadFactor] = new(g => g.Average(f => f.LoadFactor), f => f.LoadFactor == null),
        [MetricKeys.PlannedDistanceKm] = new(g => g.Count(f => f.PlannedDistanceM != null) == 0 ? null : (decimal?)g.Sum(f => (long?)f.PlannedDistanceM) / 1000m, f => f.PlannedDistanceM == null),
        [MetricKeys.ActualDistanceKm] = new(g => g.Count(f => f.ActualDistanceM != null) == 0 ? null : (decimal?)g.Sum(f => (long?)f.ActualDistanceM) / 1000m, f => f.ActualDistanceM == null),
        [MetricKeys.OnTimeStopPercentage] = new(
            g => g.Sum(f => f.StopsMeasured) == 0 ? null : (decimal?)g.Sum(f => f.StopsOnTime) * 100m / g.Sum(f => f.StopsMeasured),
            f => f.StopsMeasured == 0),
    };

    public Expression<Func<IGrouping<int, TripFact>, MetricCoverageRow>> CoverageWithoutMissing { get; }
        = g => new MetricCoverageRow { Status = g.Key, All = g.LongCount(), LastUpdate = g.Max(f => f.FactUpdatedAt) };

    public Expression<Func<TripFact, int>> StatusCode { get; } = f => (int)f.Status;

    public Expression<Func<TripFact, string>>? Currency => null;

    public IQueryable<TripFact> InPeriod(IQueryable<TripFact> query, DateOnly from, DateOnly to)
        => query.Where(f => f.TripDate >= from && f.TripDate <= to);

    public IQueryable<TripFact> WithStatus(IQueryable<TripFact> query, IReadOnlyCollection<int> statuses)
    {
        var values = statuses.Select(s => (TransportTripStatus)s).ToList();
        return query.Where(f => values.Contains(f.Status));
    }

    public IQueryable<TripFact> Filtered(IQueryable<TripFact> query, ParsedMetricFilters filters)
    {
        if (filters.CarrierRelationId is { } carrier)
        {
            query = query.Where(f => f.CarrierRelationId == carrier);
        }

        if (filters.VehicleId is { } vehicle)
        {
            query = query.Where(f => f.PrimaryVehicleId == vehicle);
        }

        if (filters.TransportTripTypeId is { } type)
        {
            query = query.Where(f => f.TransportTripTypeId == type);
        }

        if (filters.Execution is { } execution)
        {
            query = query.Where(f => f.Execution == execution);
        }

        if (filters.OrganizationUnitId is { } unit)
        {
            query = query.Where(f => f.OrganizationUnitId == unit);
        }

        return query;
    }
}
