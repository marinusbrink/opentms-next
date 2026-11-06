using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace OpenTms.Reporting.Metrics;

/// <summary>
/// Turns a checked plan into LINQ for one fact table (design 75 "Group queries"): a dynamic key of at most two breakdowns plus
/// the currency, with only the slots in use bound, and one aggregate per chosen metric. No text from a caller reaches it: keys
/// are looked up in the catalog first, and values are typed parameters.
/// </summary>
public static class MetricSqlBuilder
{
    /// <summary>The groups, sorted; the caller adds <c>Take</c>.</summary>
    public static IQueryable<MetricGroupRow> Groups<TFact>(IQueryable<TFact> query, IFactShape<TFact> shape, IReadOnlyList<string> groupBy, IReadOnlyList<string> metrics, bool splitByCurrency, string? sort)
    {
        var grouped = GroupByKey(query, shape, groupBy, splitByCurrency);
        var group = Expression.Parameter(typeof(IGrouping<MetricGroupKey, TFact>), "g");
        var bindings = new List<MemberBinding>();
        var key = Expression.Property(group, nameof(IGrouping<MetricGroupKey, TFact>.Key));

        foreach (var slot in SlotsOf(shape, groupBy))
        {
            bindings.Add(Expression.Bind(typeof(MetricGroupRow).GetProperty(slot)!, Expression.Property(key, slot)));
        }

        if (splitByCurrency)
        {
            bindings.Add(Expression.Bind(typeof(MetricGroupRow).GetProperty(nameof(MetricGroupRow.Currency))!, Expression.Property(key, nameof(MetricGroupKey.Currency))));
        }

        for (var i = 0; i < metrics.Count; i++)
        {
            var aggregate = shape.Metrics[metrics[i]].Aggregate;
            var body = new ParameterSwap(aggregate.Parameters[0], group).Visit(aggregate.Body);
            bindings.Add(Expression.Bind(typeof(MetricGroupRow).GetProperty("V" + i)!, body));
        }

        var selector = Expression.Lambda<Func<IGrouping<MetricGroupKey, TFact>, MetricGroupRow>>(
            Expression.MemberInit(Expression.New(typeof(MetricGroupRow)), bindings), group);
        return Sort(grouped.Select(selector), shape, groupBy, metrics, splitByCurrency, sort);
    }

    /// <summary>The number of groups without their aggregates; only asked for when the answer is truncated.</summary>
    public static IQueryable<int> GroupCount<TFact>(IQueryable<TFact> query, IFactShape<TFact> shape, IReadOnlyList<string> groupBy, bool splitByCurrency)
        => GroupByKey(query, shape, groupBy, splitByCurrency).Select(_ => 1);

    /// <summary>One row per status: its records, those without a value for a chosen metric, and the newest fact.</summary>
    public static IQueryable<MetricCoverageRow> Coverage<TFact>(IQueryable<TFact> query, IFactShape<TFact> shape, IReadOnlyList<string> metrics)
    {
        var grouped = query.GroupBy(shape.StatusCode);
        var row = shape.CoverageWithoutMissing;
        var missing = metrics.Select(m => shape.Metrics[m].Missing).OfType<Expression<Func<TFact, bool>>>().ToList();
        if (missing.Count == 0)
        {
            return grouped.Select(row);
        }

        /* Any chosen metric without a value: Count(f => a OR b) inside the group. */
        var fact = Expression.Parameter(typeof(TFact), "f");
        var any = missing.Select(m => new ParameterSwap(m.Parameters[0], fact).Visit(m.Body)).Aggregate(Expression.OrElse);
        var group = row.Parameters[0];
        var count = Expression.Call(
            typeof(Enumerable), nameof(Enumerable.LongCount), [typeof(TFact)], group, Expression.Lambda<Func<TFact, bool>>(any, fact));
        var init = (MemberInitExpression)row.Body;
        var withMissing = Expression.MemberInit(init.NewExpression, init.Bindings.Append(Expression.Bind(typeof(MetricCoverageRow).GetProperty(nameof(MetricCoverageRow.Missing))!, count)));
        return grouped.Select(Expression.Lambda<Func<IGrouping<int, TFact>, MetricCoverageRow>>(withMissing, group));
    }

    private static IQueryable<IGrouping<MetricGroupKey, TFact>> GroupByKey<TFact>(IQueryable<TFact> query, IFactShape<TFact> shape, IReadOnlyList<string> groupBy, bool splitByCurrency)
    {
        var fact = Expression.Parameter(typeof(TFact), "f");
        var bindings = new List<MemberBinding>();
        var slots = SlotsOf(shape, groupBy);
        for (var i = 0; i < groupBy.Count; i++)
        {
            var column = shape.Breakdowns[groupBy[i]].Column;
            bindings.Add(Expression.Bind(typeof(MetricGroupKey).GetProperty(slots[i])!, new ParameterSwap(column.Parameters[0], fact).Visit(column.Body)));
        }

        if (splitByCurrency)
        {
            var currency = shape.Currency!;
            bindings.Add(Expression.Bind(typeof(MetricGroupKey).GetProperty(nameof(MetricGroupKey.Currency))!, new ParameterSwap(currency.Parameters[0], fact).Visit(currency.Body)));
        }

        if (bindings.Count == 0)
        {
            bindings.Add(Expression.Bind(typeof(MetricGroupKey).GetProperty(nameof(MetricGroupKey.All))!, Expression.Constant(1)));
        }

        return query.GroupBy(Expression.Lambda<Func<TFact, MetricGroupKey>>(Expression.MemberInit(Expression.New(typeof(MetricGroupKey)), bindings), fact));
    }

    /// <summary>The key slot of each breakdown, in order: IdA, DateB and so on.</summary>
    public static IReadOnlyList<string> SlotsOf<TFact>(IFactShape<TFact> shape, IReadOnlyList<string> groupBy)
        => groupBy.Select((breakdown, i) => shape.Breakdowns[breakdown].Kind + (i == 0 ? "A" : "B")).ToList();

    /// <summary>
    /// A chosen metric descending with nulls last (PostgreSQL would put them first), or a period ascending; the default is the
    /// first metric. The other groups' slots keep the order stable, so <c>top</c> is deterministic.
    /// </summary>
    private static IQueryable<MetricGroupRow> Sort<TFact>(IQueryable<MetricGroupRow> rows, IFactShape<TFact> shape, IReadOnlyList<string> groupBy, IReadOnlyList<string> metrics, bool splitByCurrency, string? sort)
    {
        var slots = SlotsOf(shape, groupBy);
        var periodIndex = sort == null ? -1 : groupBy.ToList().IndexOf(sort);
        IOrderedQueryable<MetricGroupRow> ordered;
        if (periodIndex >= 0)
        {
            ordered = Queryable.OrderBy(rows, Slot<DateOnly?>(slots[periodIndex]));
        }
        else
        {
            var index = sort == null ? 0 : metrics.ToList().IndexOf(sort);
            var value = Slot<decimal?>("V" + index);
            var isNull = Expression.Lambda<Func<MetricGroupRow, bool>>(Expression.Equal(value.Body, Expression.Constant(null, typeof(decimal?))), value.Parameters);
            ordered = rows.OrderBy(isNull).ThenByDescending(value);
        }

        foreach (var slot in slots)
        {
            ordered = slot.StartsWith(nameof(MetricSlotKind.Id), StringComparison.Ordinal) ? ordered.ThenBy(Slot<Guid?>(slot))
                : slot.StartsWith(nameof(MetricSlotKind.Date), StringComparison.Ordinal) ? ordered.ThenBy(Slot<DateOnly?>(slot))
                : ordered.ThenBy(Slot<int?>(slot));
        }

        return splitByCurrency ? ordered.ThenBy(r => r.Currency) : ordered;
    }

    private static Expression<Func<MetricGroupRow, T>> Slot<T>(string property)
    {
        var row = Expression.Parameter(typeof(MetricGroupRow), "r");
        return Expression.Lambda<Func<MetricGroupRow, T>>(Expression.Property(row, property), row);
    }

    private sealed class ParameterSwap(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
