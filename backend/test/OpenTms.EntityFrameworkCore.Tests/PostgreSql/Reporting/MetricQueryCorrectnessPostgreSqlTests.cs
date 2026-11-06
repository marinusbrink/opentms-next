using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OpenTms.Orders.EntityFrameworkCore;
using OpenTms.Orders.TransportOrders;
using OpenTms.Orders.TransportOrders.Published;
using OpenTms.PlanningExecution.EntityFrameworkCore;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Reporting.Metrics;
using OpenTms.Reporting.Published;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace OpenTms.PostgreSql.Reporting;

/// <summary>
/// Design 75 "Correctness" (risk class High): for a seeded dataset of real order and trip rows, whose facts come from the real calculators, every metric,
/// breakdown and filter equals a sum computed independently by SQL on the <b>source</b> tables, not on the facts. Also: order_revenue per customer equals
/// get_customer_revenue, order_revenue splits by currency, mixed_currencies appears exactly when a period holds several, a value that does not exist counts in
/// coverage and is never zero, trips with revenue 0 stay out of trip_margin, the on-time boundary is exactly 15 minutes. Rows live in one rolled-back transaction.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class MetricQueryCorrectnessPostgreSqlTests : ReportingPostgreSqlTestBase
{
    private static Guid Tenant => PostgreSqlTestDatabases.TenantAId;

    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 3, 31);

    private static readonly Guid CustomerA = Guid.NewGuid();
    private static readonly Guid CustomerB = Guid.NewGuid();
    private static readonly Guid CustomerC = Guid.NewGuid();
    private Guid TypeA;
    private Guid TypeB;
    private static readonly Guid UnitA = Guid.NewGuid();
    private static readonly Guid UnitB = Guid.NewGuid();
    private static readonly Guid CarrierA = Guid.NewGuid();
    private static readonly Guid CarrierB = Guid.NewGuid();
    private static readonly Guid VehicleA = Guid.NewGuid();
    private static readonly Guid VehicleB = Guid.NewGuid();
    private Guid TripTypeA;
    private Guid TripTypeB;

    /// <summary>A rolled-back transaction with the tenant's real order and trip types at hand: their tables have foreign keys.</summary>
    private Task InWorldAsync(Func<Task> action) => InRolledBackTransactionAsync(Tenant, async () =>
    {
        TypeA = await OrderTypeOfTenantAsync();
        TypeB = await OrderTypeOfTenantAsync(1);
        TripTypeA = await TripTypeOfTenantAsync();
        TripTypeB = await TripTypeOfTenantAsync(1);
        await action();
    });

    /* ---------- The dataset ---------- */

    private async Task SeedOrdersAsync()
    {
        var random = new Random(75);
        var customers = new[] { CustomerA, CustomerB, CustomerC };
        var types = new[] { TypeA, TypeB };
        Guid?[] units = [UnitA, UnitB, null];
        var statuses = Enum.GetValues<TransportOrderStatus>();
        for (var i = 0; i < 60; i++)
        {
            /* Every fifth order carries no amounts yet; every seventh is in the second currency. */
            var hasAmounts = i % 5 != 0;
            var revenue = hasAmounts ? Math.Round((decimal)(random.NextDouble() * 5_000), 2) : (decimal?)null;
            var cost = hasAmounts && i % 3 != 0 ? Math.Round((decimal)(random.NextDouble() * 3_000), 2) : (decimal?)null;
            var id = await SeedOrderAsync(
                customers[i % customers.Length],
                types[i % types.Length],
                From.AddDays(i * 3 % 95),
                statuses[i % statuses.Length],
                i % 7 == 0 ? "USD" : "EUR",
                revenue,
                cost,
                revenue != null && cost != null ? revenue - cost : null,
                i % 4 == 0 ? null : Math.Round((decimal)(random.NextDouble() * 900), 3),
                units[i % units.Length]);
            await CalculateOrderFactAsync(id);
        }

        /* Outside the period: must count nowhere. */
        var outside = await SeedOrderAsync(CustomerA, TypeA, To.AddDays(1), TransportOrderStatus.Open, "EUR", 99_999m);
        await CalculateOrderFactAsync(outside);
        await FlushAsync();
    }

    private async Task SeedTripsAsync()
    {
        var random = new Random(750);
        Guid?[] carriers = [CarrierA, CarrierB, null];
        Guid?[] vehicles = [VehicleA, VehicleB, null];
        var types = new[] { TripTypeA, TripTypeB };
        Guid?[] units = [UnitA, null];
        var statuses = Enum.GetValues<TransportTripStatus>();
        for (var i = 0; i < 45; i++)
        {
            var date = From.AddDays(i * 2 % 88);
            var planned = new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
            var stops = new (DateTimeOffset?, DateTimeOffset?)[]
            {
                (planned, planned.AddMinutes(i % 4 == 0 ? 15 : -5)),
                (planned.AddHours(2), i % 3 == 0 ? planned.AddHours(2).AddMinutes(16) : null),
                (null, planned.AddHours(3)),
            };
            var id = await SeedTripAsync(
                types[i % types.Length],
                date,
                statuses[i % statuses.Length],
                i % 2 == 0 ? TripExecution.OwnFleet : TripExecution.Subcontracted,
                carriers[i % carriers.Length],
                vehicles[i % vehicles.Length],
                i % 6 == 0 ? null : Math.Round((decimal)(random.NextDouble() * 2_000), 2),
                i % 5 == 0 ? 0m : i % 11 == 0 ? null : Math.Round((decimal)(random.NextDouble() * 4_000), 2),
                i % 8 == 0 ? null : Math.Round((decimal)(random.NextDouble() * 400), 3),
                i % 9 == 0 ? null : Math.Round((decimal)random.NextDouble(), 4),
                i % 10 == 0 ? null : random.Next(1_000, 500_000),
                i % 7 == 0 ? null : random.Next(1_000, 500_000),
                units[i % units.Length],
                i % 13 == 0 ? [] : stops);
            await CalculateTripFactAsync(id);
        }

        var outside = await SeedTripAsync(TripTypeA, To.AddDays(1), cost: 99_999m);
        await CalculateTripFactAsync(outside);
        await FlushAsync();
    }

    private async Task ReadyAsync()
    {
        await State.SetBackfillCompletedAtAsync(DateTime.Now);
        await FlushAsync();
    }

    /* ---------- The independent sums: SQL on the source tables, never on the facts ---------- */

    private static readonly Dictionary<string, string> OrderAggregates = new()
    {
        [MetricKeys.OrderCount] = "count(*)",
        [MetricKeys.OrderRevenue] = "sum(o.\"RevenueAmount\")",
        [MetricKeys.OrderCost] = "sum(o.\"CostOfRevenueAmount\")",
        [MetricKeys.OrderMargin] = "sum(o.\"MarginAmount\")",
        [MetricKeys.OrderCo2Kg] = "sum(o.\"Co2Kg\")",
    };

    private static readonly Dictionary<string, string> OrderGroups = new()
    {
        [MetricBreakdowns.Customer] = "o.\"CustomerRelationId\"::text",
        [MetricBreakdowns.OrderType] = "o.\"TransportOrderTypeId\"::text",
        [MetricBreakdowns.OrganizationUnit] = "o.\"OrganizationUnitId\"::text",
        [MetricBreakdowns.Status] = "o.\"Status\"::text",
        [MetricBreakdowns.Day] = "to_char(o.\"OrderDate\", 'YYYY-MM-DD')",
        [MetricBreakdowns.Week] = "to_char(date_trunc('week', o.\"OrderDate\"), 'YYYY-MM-DD')",
        [MetricBreakdowns.Month] = "to_char(o.\"OrderDate\", 'YYYY-MM')",
    };

    private static readonly Dictionary<string, string> TripAggregates = new()
    {
        [MetricKeys.TripCount] = "count(*)",
        [MetricKeys.TripCost] = "sum(t.\"CostAmount\")",
        [MetricKeys.TripRevenue] = "sum(t.\"RevenueAmount\")",
        [MetricKeys.TripMargin] = "sum(t.\"RevenueAmount\" - t.\"CostAmount\") FILTER (WHERE t.\"CostAmount\" IS NOT NULL AND t.\"RevenueAmount\" > 0)",
        [MetricKeys.TripCo2Kg] = "sum(t.\"Co2Kg\")",
        [MetricKeys.AverageLoadFactor] = "avg(t.\"LoadFactor\")",
        [MetricKeys.PlannedDistanceKm] = "sum(t.\"PlannedDistanceM\") / 1000.0",
        [MetricKeys.ActualDistanceKm] = "sum(t.\"ActualDistanceM\") / 1000.0",
        [MetricKeys.OnTimeStopPercentage] = "CASE WHEN sum(s.measured) = 0 THEN NULL ELSE sum(s.on_time) * 100.0 / sum(s.measured) END",
    };

    /* The first vehicle by position, and each trip's measured and on-time stops, recomputed from the stops themselves. */
    private const string TripSource = """
        planning."TransportTrips" t
        CROSS JOIN LATERAL (
            SELECT count(*) FILTER (WHERE st."PlannedArrival" IS NOT NULL AND st."ActualArrival" IS NOT NULL) AS measured,
                   count(*) FILTER (WHERE st."ActualArrival" <= st."PlannedArrival" + interval '15 minutes') AS on_time
            FROM planning."TripStops" st WHERE st."TripId" = t."Id") s
        LEFT JOIN LATERAL (
            SELECT v."VehicleId" FROM planning."TripVehicles" v WHERE v."TripId" = t."Id" ORDER BY v."Position" LIMIT 1) pv ON true
        """;

    private static readonly Dictionary<string, string> TripGroups = new()
    {
        [MetricBreakdowns.Carrier] = "t.\"CarrierRelationId\"::text",
        [MetricBreakdowns.Vehicle] = "pv.\"VehicleId\"::text",
        [MetricBreakdowns.TripType] = "t.\"TransportTripTypeId\"::text",
        [MetricBreakdowns.Execution] = "t.\"Execution\"::text",
        [MetricBreakdowns.OrganizationUnit] = "t.\"OrganizationUnitId\"::text",
        [MetricBreakdowns.Status] = "t.\"Status\"::text",
        [MetricBreakdowns.Day] = "to_char(t.\"TripDate\", 'YYYY-MM-DD')",
        [MetricBreakdowns.Week] = "to_char(date_trunc('week', t.\"TripDate\"), 'YYYY-MM-DD')",
        [MetricBreakdowns.Month] = "to_char(t.\"TripDate\", 'YYYY-MM')",
    };

    private static string OrderStatusList(IEnumerable<TransportOrderStatus>? statuses = null)
        => string.Join(",", (statuses ?? MetricCatalog.DefaultOrderStatuses).Select(s => (int)s));

    private static string TripStatusList(IEnumerable<TransportTripStatus>? statuses = null)
        => string.Join(",", (statuses ?? MetricCatalog.DefaultTripStatuses).Select(s => (int)s));

    private async Task<Dictionary<string, decimal?>> OrderSumsAsync(string metric, string[] groupBy, string extraWhere = "", bool byCurrency = false)
    {
        var groups = groupBy.Select(g => OrderGroups[g]).ToList();
        if (byCurrency)
        {
            groups.Add("o.\"CurrencyCode\"");
        }

        var key = groups.Count == 0 ? "''" : "concat_ws('|', " + string.Join(", ", groups.Select(g => $"coalesce({g}, '(none)')")) + ")";
        var sql = $"""
            SELECT {key}, ({OrderAggregates[metric]})::numeric
            FROM orders."TransportOrders" o
            WHERE o."TenantId" = '{Tenant}' AND NOT o."IsDeleted" AND o."OrderDate" BETWEEN '{From:yyyy-MM-dd}' AND '{To:yyyy-MM-dd}' AND o."Status" IN ({OrderStatusList()}) {extraWhere}
            GROUP BY 1
            """;
        return await ReadAsync(await GetRequiredService<IDbContextProvider<OrdersDbContext>>().GetDbContextAsync(), sql);
    }

    private async Task<Dictionary<string, decimal?>> TripSumsAsync(string metric, string[] groupBy, string extraWhere = "")
    {
        var groups = groupBy.Select(g => TripGroups[g]).ToList();
        var key = groups.Count == 0 ? "''" : "concat_ws('|', " + string.Join(", ", groups.Select(g => $"coalesce({g}, '(none)')")) + ")";
        var sql = $"""
            SELECT {key}, ({TripAggregates[metric]})::numeric
            FROM {TripSource}
            WHERE t."TenantId" = '{Tenant}' AND NOT t."IsDeleted" AND t."TripDate" BETWEEN '{From:yyyy-MM-dd}' AND '{To:yyyy-MM-dd}' AND t."Status" IN ({TripStatusList()}) {extraWhere}
            GROUP BY 1
            """;
        return await ReadAsync(await GetRequiredService<IDbContextProvider<PlanningExecutionDbContext>>().GetDbContextAsync(), sql);
    }

    private static async Task<Dictionary<string, decimal?>> ReadAsync(DbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        var result = new Dictionary<string, decimal?>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetDecimal(1);
        }

        return result;
    }

    /// <summary>The answer's rows as the SQL keys above: an id as text, a status and an execution by their number, a period as its text.</summary>
    private static Dictionary<string, decimal?> KeyedRows(MetricAnswerDto answer, string metric, MetricGrain grain)
    {
        answer.Status.ShouldBe(MetricAnswerStatus.Ok, answer.Message);
        answer.Truncated.ShouldBe(false);
        var rows = new Dictionary<string, decimal?>();
        foreach (var row in answer.Rows!)
        {
            var parts = row.Groups.Select(g => g.Breakdown switch
            {
                MetricBreakdowns.Status => (grain == MetricGrain.Order
                    ? (int)Enum.Parse<TransportOrderStatus>(g.Name)
                    : (int)Enum.Parse<TransportTripStatus>(g.Name)).ToString(CultureInfo.InvariantCulture),
                MetricBreakdowns.Execution => ((int)Enum.Parse<TripExecution>(g.Name)).ToString(CultureInfo.InvariantCulture),
                _ => g.Id?.ToString() ?? g.Name,
            }).ToList();
            if (row.CurrencyCode != null)
            {
                parts.Add(row.CurrencyCode);
            }

            rows.Add(string.Join("|", parts), row.Values[metric]);
        }

        return rows;
    }

    private static void ShouldMatch(Dictionary<string, decimal?> answer, Dictionary<string, decimal?> expected, MetricDefinition metric, string what)
    {
        answer.Keys.ShouldBe(expected.Keys, ignoreOrder: true, what);
        foreach (var (key, value) in expected)
        {
            var rounded = value == null ? (decimal?)null : Math.Round(value.Value, metric.Decimals, MidpointRounding.AwayFromZero);
            answer[key].ShouldBe(rounded, what + " group " + key);
        }
    }

    /* ---------- Every metric, breakdown and filter ---------- */

    [Fact]
    public async Task Every_order_metric_alone_and_per_breakdown_equals_an_independent_sum_on_the_orders_table()
    {
        using var features = MetricFeaturesOn();
        await InWorldAsync(async () =>
        {
            await SeedOrdersAsync();
            await ReadyAsync();
            foreach (var metric in MetricCatalog.All.Where(m => m.Grain == MetricGrain.Order))
            {
                foreach (var groupBy in new[] { Array.Empty<string>() }.Concat(MetricCatalog.OrderBreakdowns.Select(b => new[] { b })))
                {
                    var answer = await QueryAsync(Ask(From, To, [metric.Key], groupBy));
                    var expected = await OrderSumsAsync(metric.Key, groupBy, byCurrency: metric.SplitsByCurrency);
                    ShouldMatch(KeyedRows(answer, metric.Key, MetricGrain.Order), expected, metric, metric.Key + " by " + string.Join("+", groupBy));
                }
            }
        });
    }

    [Fact]
    public async Task Every_trip_metric_alone_and_per_breakdown_equals_an_independent_sum_on_the_trips_table()
    {
        using var features = MetricFeaturesOn();
        await InWorldAsync(async () =>
        {
            await SeedTripsAsync();
            await ReadyAsync();
            foreach (var metric in MetricCatalog.All.Where(m => m.Grain == MetricGrain.Trip))
            {
                foreach (var groupBy in new[] { Array.Empty<string>() }.Concat(MetricCatalog.TripBreakdowns.Select(b => new[] { b })))
                {
                    var answer = await QueryAsync(Ask(From, To, [metric.Key], groupBy));
                    var expected = await TripSumsAsync(metric.Key, groupBy);
                    ShouldMatch(KeyedRows(answer, metric.Key, MetricGrain.Trip), expected, metric, metric.Key + " by " + string.Join("+", groupBy));
                }
            }
        });
    }

    [Fact]
    public async Task Two_breakdowns_at_once_equal_the_independent_sum()
    {
        using var features = MetricFeaturesOn();
        await InWorldAsync(async () =>
        {
            await SeedOrdersAsync();
            await SeedTripsAsync();
            await ReadyAsync();

            var orderCount = MetricCatalog.Find(MetricKeys.OrderCount)!;
            foreach (var first in MetricCatalog.OrderBreakdowns)
            {
                foreach (var second in MetricCatalog.OrderBreakdowns.Where(b => b != first))
                {
                    var answer = await QueryAsync(Ask(From, To, [orderCount.Key], [first, second]));
                    ShouldMatch(KeyedRows(answer, orderCount.Key, MetricGrain.Order), await OrderSumsAsync(orderCount.Key, [first, second]), orderCount, first + "+" + second);
                }
            }

            var tripCost = MetricCatalog.Find(MetricKeys.TripCost)!;
            foreach (var first in MetricCatalog.TripBreakdowns)
            {
                foreach (var second in MetricCatalog.TripBreakdowns.Where(b => b != first))
                {
                    var answer = await QueryAsync(Ask(From, To, [tripCost.Key], [first, second]));
                    ShouldMatch(KeyedRows(answer, tripCost.Key, MetricGrain.Trip), await TripSumsAsync(tripCost.Key, [first, second]), tripCost, first + "+" + second);
                }
            }
        });
    }

    [Fact]
    public async Task Every_order_filter_equals_the_independent_sum()
    {
        using var features = MetricFeaturesOn();
        await InWorldAsync(async () =>
        {
            await SeedOrdersAsync();
            await ReadyAsync();
            var metric = MetricCatalog.Find(MetricKeys.OrderCost)!;
            var cases = new (MetricFiltersDto Filter, string Where)[]
            {
                (new MetricFiltersDto { CustomerRelationId = CustomerB }, $"AND o.\"CustomerRelationId\" = '{CustomerB}'"),
                (new MetricFiltersDto { TransportOrderTypeId = TypeA }, $"AND o.\"TransportOrderTypeId\" = '{TypeA}'"),
                (new MetricFiltersDto { OrganizationUnitId = UnitA }, $"AND o.\"OrganizationUnitId\" = '{UnitA}'"),
                (new MetricFiltersDto { CustomerRelationId = CustomerA, TransportOrderTypeId = TypeB }, $"AND o.\"CustomerRelationId\" = '{CustomerA}' AND o.\"TransportOrderTypeId\" = '{TypeB}'"),
            };
            foreach (var (filter, where) in cases)
            {
                var answer = await QueryAsync(Ask(From, To, [metric.Key], [MetricBreakdowns.Month], filter));
                ShouldMatch(KeyedRows(answer, metric.Key, MetricGrain.Order), await OrderSumsAsync(metric.Key, [MetricBreakdowns.Month], where), metric, where);
            }

            /* A status filter replaces the default statuses: Draft is then counted. */
            var drafts = await QueryAsync(Ask(From, To, [MetricKeys.OrderCount], null, new MetricFiltersDto { Status = [nameof(TransportOrderStatus.Draft)] }));
            var context = await GetRequiredService<IDbContextProvider<OrdersDbContext>>().GetDbContextAsync();
            var expected = await ReadAsync(context, $"""
                SELECT '', count(*)::numeric FROM orders."TransportOrders" o
                WHERE o."TenantId" = '{Tenant}' AND NOT o."IsDeleted" AND o."OrderDate" BETWEEN '{From:yyyy-MM-dd}' AND '{To:yyyy-MM-dd}' AND o."Status" = {(int)TransportOrderStatus.Draft}
                """);
            drafts.Rows!.ShouldHaveSingleItem().Values[MetricKeys.OrderCount].ShouldBe(expected[""]);
        });
    }

    [Fact]
    public async Task Every_trip_filter_equals_the_independent_sum()
    {
        using var features = MetricFeaturesOn();
        await InWorldAsync(async () =>
        {
            await SeedTripsAsync();
            await ReadyAsync();
            var metric = MetricCatalog.Find(MetricKeys.TripCost)!;
            var cases = new (MetricFiltersDto Filter, string Where)[]
            {
                (new MetricFiltersDto { CarrierRelationId = CarrierA }, $"AND t.\"CarrierRelationId\" = '{CarrierA}'"),
                (new MetricFiltersDto { VehicleId = VehicleB }, $"AND pv.\"VehicleId\" = '{VehicleB}'"),
                (new MetricFiltersDto { TransportTripTypeId = TripTypeA }, $"AND t.\"TransportTripTypeId\" = '{TripTypeA}'"),
                (new MetricFiltersDto { Execution = nameof(TripExecution.Subcontracted) }, $"AND t.\"Execution\" = {(int)TripExecution.Subcontracted}"),
                (new MetricFiltersDto { OrganizationUnitId = UnitA }, $"AND t.\"OrganizationUnitId\" = '{UnitA}'"),
            };
            foreach (var (filter, where) in cases)
            {
                var answer = await QueryAsync(Ask(From, To, [metric.Key], [MetricBreakdowns.Month], filter));
                ShouldMatch(KeyedRows(answer, metric.Key, MetricGrain.Trip), await TripSumsAsync(metric.Key, [MetricBreakdowns.Month], where), metric, where);
            }

            var cancelled = await QueryAsync(Ask(From, To, [MetricKeys.TripCount], null, new MetricFiltersDto { Status = [nameof(TransportTripStatus.Cancelled)] }));
            var context = await GetRequiredService<IDbContextProvider<PlanningExecutionDbContext>>().GetDbContextAsync();
            var expected = await ReadAsync(context, $"""
                SELECT '', count(*)::numeric FROM planning."TransportTrips" t
                WHERE t."TenantId" = '{Tenant}' AND NOT t."IsDeleted" AND t."TripDate" BETWEEN '{From:yyyy-MM-dd}' AND '{To:yyyy-MM-dd}' AND t."Status" = {(int)TransportTripStatus.Cancelled}
                """);
            cancelled.Rows!.ShouldHaveSingleItem().Values[MetricKeys.TripCount].ShouldBe(expected[""]);
        });
    }

    [Fact]
    public async Task Coverage_counts_what_the_period_holds_what_has_no_value_and_what_the_status_rule_left_out()
    {
        using var features = MetricFeaturesOn();
        await InWorldAsync(async () =>
        {
            await SeedOrdersAsync();
            await ReadyAsync();
            var context = await GetRequiredService<IDbContextProvider<OrdersDbContext>>().GetDbContextAsync();
            var inPeriod = $"o.\"TenantId\" = '{Tenant}' AND NOT o.\"IsDeleted\" AND o.\"OrderDate\" BETWEEN '{From:yyyy-MM-dd}' AND '{To:yyyy-MM-dd}'";
            var counted = (await ReadAsync(context, $"SELECT '', count(*)::numeric FROM orders.\"TransportOrders\" o WHERE {inPeriod} AND o.\"Status\" IN ({OrderStatusList()})"))[""];
            var withoutEither = (await ReadAsync(context, $"SELECT '', count(*)::numeric FROM orders.\"TransportOrders\" o WHERE {inPeriod} AND o.\"Status\" IN ({OrderStatusList()}) AND (o.\"RevenueAmount\" IS NULL OR o.\"Co2Kg\" IS NULL)"))[""];
            var excluded = (await ReadAsync(context, $"SELECT '', count(*)::numeric FROM orders.\"TransportOrders\" o WHERE {inPeriod} AND o.\"Status\" NOT IN ({OrderStatusList()})"))[""];

            var answer = await QueryAsync(Ask(From, To, [MetricKeys.OrderRevenue, MetricKeys.OrderCo2Kg]));

            answer.Coverage!.RecordsIncluded.ShouldBe((long)counted!.Value);
            answer.Coverage.RecordsWithoutValue.ShouldBe((long)withoutEither!.Value);
            answer.Coverage.ExcludedByStatus.ShouldBe((long)excluded!.Value);
            withoutEither.Value.ShouldBeGreaterThan(0, "the dataset holds orders without a value");
            excluded.Value.ShouldBeGreaterThan(0, "the dataset holds orders the status rule leaves out");
            answer.Definitions!.Select(d => d.Metric).ShouldBe([MetricKeys.OrderRevenue, MetricKeys.OrderCo2Kg]);
            answer.Definitions.ShouldAllBe(d => d.Text.Length > 0);
            answer.Period!.DateFrom.ShouldBe(From);
            answer.Period.DateTo.ShouldBe(To);
            answer.LastFactUpdateAt.ShouldNotBeNull();
        });
    }

    /* ---------- order_revenue and get_customer_revenue ---------- */

    [Fact]
    public async Task Order_revenue_per_customer_equals_get_customer_revenue_in_every_currency()
    {
        using var features = MetricFeaturesOn();
        await InWorldAsync(async () =>
        {
            await SeedOrdersAsync();
            await ReadyAsync();
            var reader = GetRequiredService<ICustomerRevenueReader>();
            var answer = await QueryAsync(Ask(From, To, [MetricKeys.OrderRevenue], [MetricBreakdowns.Customer]));
            answer.Rows!.Select(r => r.CurrencyCode!).Distinct().OrderBy(c => c).ToList().ShouldBe(new List<string> { "EUR", "USD" }, "the dataset holds both currencies");

            foreach (var customer in new[] { CustomerA, CustomerB, CustomerC })
            {
                var revenue = await reader.GetAsync(customer, From, To);
                var rows = answer.Rows!.Where(r => r.Groups.Single().Id == customer && r.Values[MetricKeys.OrderRevenue] != null).ToList();
                rows.Select(r => r.CurrencyCode).ShouldBe(revenue.Totals.Select(t => t.CurrencyCode), ignoreOrder: true, customer.ToString());
                foreach (var total in revenue.Totals)
                {
                    rows.Single(r => r.CurrencyCode == total.CurrencyCode).Values[MetricKeys.OrderRevenue]
                        .ShouldBe(Math.Round(total.RevenueAmount, 2, MidpointRounding.AwayFromZero), customer + " " + total.CurrencyCode);
                }
            }
        });
    }

    [Fact]
    public async Task Order_revenue_splits_by_currency_on_top_of_the_breakdowns_and_never_adds_them_up()
    {
        using var features = MetricFeaturesOn();
        await InWorldAsync(async () =>
        {
            var from = new DateOnly(2026, 5, 1);
            await CalculateOrderFactAsync(await SeedOrderAsync(CustomerA, TypeA, from, currency: "EUR", revenue: 100m));
            await CalculateOrderFactAsync(await SeedOrderAsync(CustomerA, TypeA, from, currency: "EUR", revenue: 50m));
            await CalculateOrderFactAsync(await SeedOrderAsync(CustomerA, TypeA, from, currency: "USD", revenue: 7m));
            await FlushAsync();
            await ReadyAsync();

            var plain = await QueryAsync(Ask(from, from, [MetricKeys.OrderRevenue]));
            plain.Rows!.ToDictionary(r => r.CurrencyCode!, r => r.Values[MetricKeys.OrderRevenue]).ShouldBe(new Dictionary<string, decimal?> { ["EUR"] = 150m, ["USD"] = 7m });

            var perCustomer = await QueryAsync(Ask(from, from, [MetricKeys.OrderRevenue], [MetricBreakdowns.Customer]));
            perCustomer.Rows!.Count.ShouldBe(2, "one row per customer and currency");
            perCustomer.TotalGroups.ShouldBe(2);
        });
    }

    /* ---------- mixed_currencies ---------- */

    [Fact]
    public async Task Mixed_currencies_appears_exactly_when_the_period_holds_several_and_only_on_money_a_calculator_combined()
    {
        using var features = MetricFeaturesOn();
        await InWorldAsync(async () =>
        {
            var euroOnly = new DateOnly(2026, 6, 1);
            var mixed = new DateOnly(2026, 7, 1);
            await CalculateOrderFactAsync(await SeedOrderAsync(CustomerA, TypeA, euroOnly, currency: "EUR", revenue: 1m, cost: 1m, margin: 0m));
            await CalculateOrderFactAsync(await SeedOrderAsync(CustomerA, TypeA, mixed, currency: "EUR", revenue: 2m, cost: 1m, margin: 1m));
            await CalculateOrderFactAsync(await SeedOrderAsync(CustomerB, TypeA, mixed, currency: "USD", revenue: 3m, cost: 1m, margin: 2m));
            await CalculateTripFactAsync(await SeedTripAsync(TripTypeA, euroOnly, cost: 1m, revenue: 2m));
            await CalculateTripFactAsync(await SeedTripAsync(TripTypeA, mixed, cost: 1m, revenue: 2m));
            await FlushAsync();
            await ReadyAsync();

            foreach (var metric in new[] { MetricKeys.OrderCost, MetricKeys.OrderMargin, MetricKeys.TripCost, MetricKeys.TripRevenue, MetricKeys.TripMargin })
            {
                (await QueryAsync(Ask(euroOnly, euroOnly, [metric]))).Warnings.ShouldBeNull(metric + " in one currency");
                (await QueryAsync(Ask(mixed, mixed, [metric]))).Warnings.ShouldBe([MetricWarnings.MixedCurrencies], metric + " in two currencies");
            }

            foreach (var metric in new[] { MetricKeys.OrderRevenue, MetricKeys.OrderCount, MetricKeys.OrderCo2Kg, MetricKeys.TripCount, MetricKeys.PlannedDistanceKm })
            {
                (await QueryAsync(Ask(mixed, mixed, [metric]))).Warnings.ShouldBeNull(metric + " is not a combined amount");
            }

            /* The definition of such a metric says the amount is not converted. */
            (await QueryAsync(Ask(mixed, mixed, [MetricKeys.OrderCost]))).Definitions!.Single().Text.ShouldContain("not converted");
            /* In a mix, the warning stands. */
            (await QueryAsync(Ask(mixed, mixed, [MetricKeys.OrderRevenue, MetricKeys.OrderCost]))).Warnings.ShouldBe([MetricWarnings.MixedCurrencies]);
            /* Orders out of the counted statuses make no mix. */
            await CalculateOrderFactAsync(await SeedOrderAsync(CustomerA, TypeA, euroOnly, TransportOrderStatus.Draft, "USD", 5m, 5m, 0m));
            await FlushAsync();
            (await QueryAsync(Ask(euroOnly, euroOnly, [MetricKeys.OrderCost]))).Warnings.ShouldBeNull("a Draft in another currency is not counted");
        });
    }

    /* ---------- No value is never zero ---------- */

    [Fact]
    public async Task A_group_without_a_value_answers_null_and_counts_in_coverage_never_as_zero()
    {
        using var features = MetricFeaturesOn();
        await InWorldAsync(async () =>
        {
            var day = new DateOnly(2026, 8, 1);
            await CalculateOrderFactAsync(await SeedOrderAsync(CustomerA, TypeA, day, revenue: null, cost: null, co2: null));
            await CalculateOrderFactAsync(await SeedOrderAsync(CustomerB, TypeA, day, revenue: 10m, cost: 5m, margin: 5m, co2: 1.5m));
            await CalculateTripFactAsync(await SeedTripAsync(TripTypeA, day, carrierId: CarrierA));
            await CalculateTripFactAsync(await SeedTripAsync(TripTypeA, day, carrierId: CarrierB, cost: 5m, co2: 2m, loadFactor: 0.5m, plannedDistanceM: 1000, actualDistanceM: 2000));
            await FlushAsync();
            await ReadyAsync();

            foreach (var metric in new[] { MetricKeys.OrderCost, MetricKeys.OrderMargin, MetricKeys.OrderCo2Kg })
            {
                var answer = await QueryAsync(Ask(day, day, [metric], [MetricBreakdowns.Customer]));
                answer.Rows!.Single(r => r.Groups.Single().Id == CustomerA).Values[metric].ShouldBeNull(metric);
                answer.Rows!.Last().Groups.Single().Id.ShouldBe(CustomerA, "a metric sorts largest first with no-value groups last");
                answer.Coverage!.RecordsWithoutValue.ShouldBeGreaterThanOrEqualTo(1, metric);
            }

            foreach (var metric in new[] { MetricKeys.TripCost, MetricKeys.TripMargin, MetricKeys.TripCo2Kg, MetricKeys.AverageLoadFactor, MetricKeys.PlannedDistanceKm, MetricKeys.ActualDistanceKm, MetricKeys.OnTimeStopPercentage })
            {
                var answer = await QueryAsync(Ask(day, day, [metric], [MetricBreakdowns.Carrier]));
                answer.Rows!.Single(r => r.Groups.Single().Id == CarrierA).Values[metric].ShouldBeNull(metric);
                answer.Coverage!.RecordsWithoutValue.ShouldBeGreaterThanOrEqualTo(1, metric);
            }

            var empty = await QueryAsync(Ask(day.AddYears(-2), day.AddYears(-2), [MetricKeys.OrderCount]));
            empty.Status.ShouldBe(MetricAnswerStatus.Ok);
            empty.Rows.ShouldBeEmpty("a question without any record answers ok with no rows, not a row of zeros");
            empty.TotalGroups.ShouldBe(0);
            empty.Coverage!.RecordsIncluded.ShouldBe(0);
            empty.LastFactUpdateAt.ShouldBeNull();
        });
    }

    [Fact]
    public async Task Trips_with_revenue_zero_stay_out_of_trip_margin_and_coverage_counts_them()
    {
        using var features = MetricFeaturesOn();
        await InWorldAsync(async () =>
        {
            var day = new DateOnly(2026, 9, 1);
            await CalculateTripFactAsync(await SeedTripAsync(TripTypeA, day, cost: 100m, revenue: 0m));
            await CalculateTripFactAsync(await SeedTripAsync(TripTypeA, day, cost: 100m, revenue: 150m));
            await CalculateTripFactAsync(await SeedTripAsync(TripTypeA, day, cost: null, revenue: 500m));
            await FlushAsync();
            await ReadyAsync();

            var answer = await QueryAsync(Ask(day, day, [MetricKeys.TripMargin]));
            answer.Rows!.ShouldHaveSingleItem().Values[MetricKeys.TripMargin].ShouldBe(50m, "only the trip with a cost and with revenue above 0");
            answer.Coverage!.RecordsIncluded.ShouldBe(3);
            answer.Coverage.RecordsWithoutValue.ShouldBe(2);

            var revenue = await QueryAsync(Ask(day, day, [MetricKeys.TripRevenue]));
            revenue.Rows!.ShouldHaveSingleItem().Values[MetricKeys.TripRevenue].ShouldBe(650m, "trip_revenue itself counts the 0 as 0");
        });
    }

    /* ---------- On time ---------- */

    [Fact]
    public async Task A_stop_is_on_time_up_to_and_including_exactly_15_minutes_after_its_planned_arrival()
    {
        using var features = MetricFeaturesOn();
        await InWorldAsync(async () =>
        {
            var day = new DateOnly(2026, 10, 1);
            var planned = new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
            var trip = await SeedTripAsync(TripTypeA, day, stops:
            [
                (planned, planned.AddMinutes(-30)),
                (planned, planned.AddMinutes(15)),
                (planned, planned.AddMinutes(15).AddSeconds(1)),
                (planned, null),
                (null, planned)
            ]);
            await CalculateTripFactAsync(trip);
            await FlushAsync();
            await ReadyAsync();

            var fact = (await TripFacts.FindAsync(trip)).ShouldNotBeNull();
            fact.StopsMeasured.ShouldBe(3, "only the stops with both a planned and an actual arrival");
            fact.StopsOnTime.ShouldBe(2, "early and exactly 15 minutes are on time; a second later is not");

            var answer = await QueryAsync(Ask(day, day, [MetricKeys.OnTimeStopPercentage]));
            answer.Rows!.ShouldHaveSingleItem().Values[MetricKeys.OnTimeStopPercentage].ShouldBe(66.67m);
        });
    }

    /* ---------- Rounding, names, sort, top ---------- */

    [Fact]
    public async Task Names_periods_sort_and_top_are_as_the_design_says()
    {
        using var features = MetricFeaturesOn();
        await InWorldAsync(async () =>
        {
            var day = new DateOnly(2026, 2, 4); /* a Wednesday: its week is Monday the 2nd */
            for (var i = 0; i < 120; i++)
            {
                await CalculateOrderFactAsync(await SeedOrderAsync(Guid.NewGuid(), TypeA, day, revenue: i + 1, cost: i % 2 == 0 ? 1m : null));
            }

            await CalculateOrderFactAsync(await SeedOrderAsync(CustomerA, TypeA, day, organizationUnitId: null, revenue: 1m));
            await FlushAsync();
            await ReadyAsync();

            var defaulted = await QueryAsync(new MetricQueryRequestDto { Metrics = [MetricKeys.OrderCount], DateFrom = day, DateTo = day, GroupBy = [MetricBreakdowns.Customer] });
            defaulted.Rows!.Count.ShouldBe(25, "top defaults to 25");
            defaulted.Truncated.ShouldBe(true);
            defaulted.TotalGroups.ShouldBe(121);

            var capped = await QueryAsync(Ask(day, day, [MetricKeys.OrderRevenue], [MetricBreakdowns.Customer], top: 5_000));
            capped.Rows!.Count.ShouldBe(100, "top is at most 100 groups");
            capped.Truncated.ShouldBe(true);
            capped.Rows!.First().Values[MetricKeys.OrderRevenue].ShouldBe(120m, "the first metric, largest first");

            (await QueryAsync(Ask(day, day, [MetricKeys.OrderCount], [MetricBreakdowns.Customer], top: 0))).Rows!.Count.ShouldBe(1, "top below 1 becomes 1");
            (await QueryAsync(Ask(day, day, [MetricKeys.OrderCount], [MetricBreakdowns.Customer], top: -7))).Rows!.Count.ShouldBe(1);

            var unknown = await QueryAsync(Ask(day, day, [MetricKeys.OrderCount], [MetricBreakdowns.Customer], top: 1));
            unknown.Rows!.ShouldHaveSingleItem().Groups.Single().Name.ShouldBe("(unknown)", "an id without a name is (unknown) with its id");
            unknown.Rows!.Single().Groups.Single().Id.ShouldNotBeNull();

            var none = await QueryAsync(Ask(day, day, [MetricKeys.OrderCount], [MetricBreakdowns.OrganizationUnit]));
            var noneGroup = none.Rows!.ShouldHaveSingleItem().Groups.Single();
            noneGroup.Name.ShouldBe("(none)", "a record without such an id is (none) with no id");
            noneGroup.Id.ShouldBeNull();

            (await QueryAsync(Ask(day, day, [MetricKeys.OrderCount], [MetricBreakdowns.Day]))).Rows!.Single().Groups.Single().Name.ShouldBe("2026-02-04");
            (await QueryAsync(Ask(day, day, [MetricKeys.OrderCount], [MetricBreakdowns.Week]))).Rows!.Single().Groups.Single().Name.ShouldBe("2026-02-02");
            (await QueryAsync(Ask(day, day, [MetricKeys.OrderCount], [MetricBreakdowns.Month]))).Rows!.Single().Groups.Single().Name.ShouldBe("2026-02");
            (await QueryAsync(Ask(day, day, [MetricKeys.OrderCount], [MetricBreakdowns.Status]))).Rows!.Single().Groups.Single().Name.ShouldBe(nameof(TransportOrderStatus.Open));

            /* A period sort is oldest first. */
            await CalculateOrderFactAsync(await SeedOrderAsync(CustomerA, TypeA, day.AddMonths(1), revenue: 1m));
            await CalculateOrderFactAsync(await SeedOrderAsync(CustomerA, TypeA, day.AddMonths(-1), revenue: 1m));
            await FlushAsync();
            var byMonth = await QueryAsync(Ask(day.AddMonths(-1), day.AddMonths(1), [MetricKeys.OrderCount], [MetricBreakdowns.Month], sort: MetricBreakdowns.Month));
            byMonth.Rows!.Select(r => r.Groups.Single().Name).ShouldBe(["2026-01", "2026-02", "2026-03"]);
            var byCount = await QueryAsync(Ask(day.AddMonths(-1), day.AddMonths(1), [MetricKeys.OrderCount], [MetricBreakdowns.Month], sort: MetricKeys.OrderCount));
            byCount.Rows!.First().Groups.Single().Name.ShouldBe("2026-02");
        });
    }

    [Fact]
    public async Task Five_chosen_metrics_answer_in_one_row_and_each_is_rounded_as_the_design_says()
    {
        using var features = MetricFeaturesOn();
        await InWorldAsync(async () =>
        {
            var day = new DateOnly(2026, 4, 1);
            await CalculateOrderFactAsync(await SeedOrderAsync(CustomerA, TypeA, day, revenue: 1.125m, cost: 2.555m, margin: 0.005m, co2: 1.23456m));
            await CalculateTripFactAsync(await SeedTripAsync(TripTypeA, day, loadFactor: 0.33339m, plannedDistanceM: 1_234, actualDistanceM: 5_678));
            await FlushAsync();
            await ReadyAsync();

            var orders = await QueryAsync(Ask(day, day, [MetricKeys.OrderCount, MetricKeys.OrderRevenue, MetricKeys.OrderCost, MetricKeys.OrderMargin, MetricKeys.OrderCo2Kg]));
            var row = orders.Rows!.ShouldHaveSingleItem();
            row.Values[MetricKeys.OrderCount].ShouldBe(1m);
            row.Values[MetricKeys.OrderCo2Kg].ShouldBe(1.235m, "CO2 to three decimals");

            var trips = await QueryAsync(Ask(day, day, [MetricKeys.AverageLoadFactor, MetricKeys.PlannedDistanceKm, MetricKeys.ActualDistanceKm]));
            var tripRow = trips.Rows!.ShouldHaveSingleItem();
            tripRow.Values[MetricKeys.AverageLoadFactor].ShouldBe(0.3334m, "a load factor to four decimals");
            tripRow.Values[MetricKeys.PlannedDistanceKm].ShouldBe(1.234m);
            tripRow.Values[MetricKeys.ActualDistanceKm].ShouldBe(5.678m);
        });
    }
}
