using System;
using System.Linq.Expressions;

namespace OpenTms.Reporting.Metrics;

/// <summary>
/// The key of one group of a query. A breakdown takes the slot of its kind in position A or B; slots the query does not use are
/// not bound, so they are not in the SQL. <see cref="All"/> is the constant key of a query without breakdowns.
/// </summary>
public sealed class MetricGroupKey
{
    public int All { get; set; }

    public Guid? IdA { get; set; }

    public DateOnly? DateA { get; set; }

    public int? CodeA { get; set; }

    public Guid? IdB { get; set; }

    public DateOnly? DateB { get; set; }

    public int? CodeB { get; set; }

    public string? Currency { get; set; }
}

/// <summary>One row of a query as it leaves SQL: the key's slots and a value per chosen metric, by position.</summary>
public sealed class MetricGroupRow
{
    public Guid? IdA { get; set; }

    public DateOnly? DateA { get; set; }

    public int? CodeA { get; set; }

    public Guid? IdB { get; set; }

    public DateOnly? DateB { get; set; }

    public int? CodeB { get; set; }

    public string? Currency { get; set; }

    public decimal? V0 { get; set; }

    public decimal? V1 { get; set; }

    public decimal? V2 { get; set; }

    public decimal? V3 { get; set; }

    public decimal? V4 { get; set; }
}

/// <summary>One status of the period and filters: how many records, how many without a value, and the newest fact.</summary>
public sealed class MetricCoverageRow
{
    public int Status { get; set; }

    public long All { get; set; }

    public long Missing { get; set; }

    public DateTime LastUpdate { get; set; }
}

/// <summary>The column of a breakdown, and how it fills a key slot.</summary>
public enum MetricSlotKind
{
    Id = 1,
    Date = 2,
    Code = 3
}

public sealed record MetricBreakdownColumn(MetricSlotKind Kind, LambdaExpression Column);

/// <summary>A metric's SQL form: its aggregate over a group, and the rule for a record without a value (null when it always has one).</summary>
public sealed record MetricColumn<TFact>(
    Expression<Func<System.Linq.IGrouping<MetricGroupKey, TFact>, decimal?>> Aggregate,
    Expression<Func<TFact, bool>>? Missing);
