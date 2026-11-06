using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenTms.Orders.EntityFrameworkCore;
using OpenTms.Orders.TransportOrders.Published;
using OpenTms.Reporting.EntityFrameworkCore;
using OpenTms.Reporting.Metrics;
using OpenTms.Reporting.Published;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace OpenTms.PostgreSql.Reporting;

/// <summary>
/// VOLUME TEST (designs 74 and 75 "Performance"; budget: interactive reads p95 under 300 ms): in tenant A, 1,000,000 order facts and 250,000 trip facts, spread over five
/// years, inserted with one statement each after the indexes exist, then ANALYZE. Timed warm through the real <see cref="IMetricQuery"/>, 25 calls each after a warm-up, at the worst
/// case the design names (a year's orders grouped by customer and month, with revenue, margin and count) and its siblings. And 1,000,000 orders in Orders' own table with 100,000 for one
/// customer for <c>get_customer_revenue</c>. Follows <c>PlanboardVolumeTests</c>: it runs in every normal run and its rows live in one rolled-back transaction. A miss goes to the PO,
/// with a day-level rollup table as the prepared fallback (design 75). Risk class: High (performance at volume).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class MetricVolumeTests : ReportingPostgreSqlTestBase
{
    private const int OrderFactCount = 1_000_000;
    private const int TripFactCount = 250_000;
    private const int Calls = 25;
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(300);

    private static Guid Tenant => PostgreSqlTestDatabases.TenantAId;

    private readonly ITestOutputHelper _output;

    public MetricVolumeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>A timed call must have done the work: a refusal or a timeout answers fast too, and would pass the budget.</summary>
    private async Task AnsweredAsync(MetricQueryRequestDto request)
    {
        var answer = await Metrics.QueryAsync(request);
        answer.Status.ShouldBe(MetricAnswerStatus.Ok, answer.Message);
        answer.Rows.ShouldNotBeNull().ShouldNotBeEmpty();
    }

    private async Task P95Async(string name, Func<Task> call)
    {
        await call();
        await call();
        var times = new List<double>(Calls);
        for (var i = 0; i < Calls; i++)
        {
            var watch = Stopwatch.StartNew();
            await call();
            watch.Stop();
            times.Add(watch.Elapsed.TotalMilliseconds);
        }

        times.Sort();
        var p95 = times[(int)Math.Ceiling(Calls * 0.95) - 1];
        _output.WriteLine($"{name}: p95 {p95:F0} ms, median {times[Calls / 2]:F0} ms, max {times[^1]:F0} ms");
        TimeSpan.FromMilliseconds(p95).ShouldBeLessThan(Budget, name);
    }

    [Fact]
    public async Task Query_metrics_stays_within_300_ms_p95_at_1_000_000_orders_and_250_000_trips_at_the_worst_case()
    {
        using var features = MetricFeaturesOn();
        await InRolledBackTransactionAsync(Tenant, async () =>
        {
            var insert = Stopwatch.StartNew();
            var context = await ReportingContextAsync();
            context.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
            /* 8,000 customers spread apart from the dates, so a year by customer and month has tens of thousands of groups; 20 order types, 30 units, five years. */
            await context.Database.ExecuteSqlRawAsync($$"""
                INSERT INTO reporting."OrderFacts" ("Id", "TenantId", "OrderDate", "OrderWeek", "OrderMonth", "Status", "CustomerRelationId", "OrganizationUnitId", "TransportOrderTypeId", "CurrencyCode", "RevenueAmount", "CostOfRevenueAmount", "MarginAmount", "Co2Kg", "FactUpdatedAt")
                SELECT gen_random_uuid(), '{{Tenant}}', d, date_trunc('week', d)::date, date_trunc('month', d)::date,
                       (ARRAY[1,2,3,4,5,6])[1 + i % 6],
                       ('00000000-0000-0000-0000-' || lpad((1 + abs(hashint4(i)) % 8000)::text, 12, '0'))::uuid,
                       CASE WHEN i % 10 = 0 THEN NULL ELSE ('00000000-0000-0000-0001-' || lpad((1 + i % 30)::text, 12, '0'))::uuid END,
                       ('00000000-0000-0000-0002-' || lpad((1 + i % 20)::text, 12, '0'))::uuid,
                       CASE WHEN i % 50 = 0 THEN 'USD' ELSE 'EUR' END,
                       CASE WHEN i % 25 = 0 THEN NULL ELSE (i % 5000)::numeric + 0.25 END,
                       CASE WHEN i % 25 = 0 THEN NULL ELSE (i % 3000)::numeric END,
                       CASE WHEN i % 25 = 0 THEN NULL ELSE (i % 2000)::numeric + 0.25 END,
                       (i % 300)::numeric, now()
                FROM generate_series(1, {{OrderFactCount}}) AS i, LATERAL (SELECT ('2021-01-01'::date + (i % 1826))::date AS d) AS day;
                """);
            await context.Database.ExecuteSqlRawAsync($$"""
                INSERT INTO reporting."TripFacts" ("Id", "TenantId", "TripDate", "TripWeek", "TripMonth", "Status", "Execution", "CarrierRelationId", "PrimaryVehicleId", "OrganizationUnitId", "TransportTripTypeId", "CostAmount", "RevenueAmount", "Co2Kg", "LoadFactor", "PlannedDistanceM", "ActualDistanceM", "StopsMeasured", "StopsOnTime", "FactUpdatedAt")
                SELECT gen_random_uuid(), '{{Tenant}}', d, date_trunc('week', d)::date, date_trunc('month', d)::date,
                       (ARRAY[1,2,3,4])[1 + i % 4], 1 + i % 2,
                       ('00000000-0000-0000-0003-' || lpad((1 + abs(hashint4(i)) % 300)::text, 12, '0'))::uuid,
                       ('00000000-0000-0000-0004-' || lpad((1 + i % 500)::text, 12, '0'))::uuid,
                       ('00000000-0000-0000-0001-' || lpad((1 + i % 30)::text, 12, '0'))::uuid,
                       ('00000000-0000-0000-0005-' || lpad((1 + i % 10)::text, 12, '0'))::uuid,
                       (i % 2000)::numeric, (i % 4000)::numeric, (i % 400)::numeric, (i % 100)::numeric / 100, i % 500000, i % 450000, 2 + i % 6, 1 + i % 2, now()
                FROM generate_series(1, {{TripFactCount}}) AS i, LATERAL (SELECT ('2021-01-01'::date + (i % 1826))::date AS d) AS day;
                """);
            await context.Database.ExecuteSqlRawAsync("ANALYZE reporting.\"OrderFacts\"; ANALYZE reporting.\"TripFacts\";");
            _output.WriteLine($"Inserted {OrderFactCount} order facts and {TripFactCount} trip facts in {insert.Elapsed.TotalSeconds:F1} s.");
            await State.SetBackfillCompletedAtAsync(DateTime.Now);
            await FlushAsync();

            var from = new DateOnly(2025, 1, 1);
            var to = new DateOnly(2025, 12, 31);

            /* The worst case: a year's orders by customer and month, truncated, so the count of groups is a statement of its own. */
            var worst = Ask(from, to, [MetricKeys.OrderRevenue, MetricKeys.OrderMargin, MetricKeys.OrderCount], [MetricBreakdowns.Customer, MetricBreakdowns.Month]);
            var answer = await Metrics.QueryAsync(worst);
            answer.Status.ShouldBe(MetricAnswerStatus.Ok, answer.Message);
            answer.Truncated.ShouldBe(true);
            answer.Rows!.Count.ShouldBe(100);
            _output.WriteLine($"The worst case has {answer.TotalGroups} groups, of {answer.Coverage!.RecordsIncluded} records.");
            answer.TotalGroups!.Value.ShouldBeGreaterThan(50_000, "the worst case the design names, not an easy spread");

            await P95Async("a year's orders by customer and month, three metrics", () => AnsweredAsync(worst));
            await P95Async("a year's orders by customer and month, count only", () => AnsweredAsync(Ask(from, to, [MetricKeys.OrderCount], [MetricBreakdowns.Customer, MetricBreakdowns.Month])));
            await P95Async("five years of orders by month", () => AnsweredAsync(Ask(new DateOnly(2021, 1, 1), new DateOnly(2025, 12, 31), [MetricKeys.OrderCount], [MetricBreakdowns.Month])));
            await P95Async("a year of one customer's orders by month", () => AnsweredAsync(Ask(from, to, [MetricKeys.OrderRevenue], [MetricBreakdowns.Month], new MetricFiltersDto { CustomerRelationId = Guid.Parse("00000000-0000-0000-0000-000000000007") })));
            await P95Async("a year's trips by carrier and month, four metrics", () => AnsweredAsync(Ask(from, to, [MetricKeys.TripCost, MetricKeys.TripRevenue, MetricKeys.PlannedDistanceKm, MetricKeys.OnTimeStopPercentage], [MetricBreakdowns.Carrier, MetricBreakdowns.Month])));
        });
    }

    [Fact]
    public async Task Get_customer_revenue_stays_within_300_ms_p95_at_1_000_000_orders_with_100_000_for_one_customer()
    {
        const int total = 1_000_000;
        var customer = Guid.NewGuid();
        await InRolledBackTransactionAsync(Tenant, async () =>
        {
            var insert = Stopwatch.StartNew();
            var context = await GetRequiredService<IDbContextProvider<OrdersDbContext>>().GetDbContextAsync();
            context.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
            var type = await TemplateAsync(context);
            await context.Database.ExecuteSqlRawAsync($$"""
                INSERT INTO orders."TransportOrders" ("Id", "TenantId", "Status", "TransportOrderTypeId", "CustomerRelationId", "CustomerName", "OrderDate", "Attributes", "Source", "CopyIndex", "CurrencyCode", "MultipleMatches", "SendFreightDocumentsWithInvoice", "ExecutionProgress", "LineCount", "StopCount", "RevenueAmount", "ExtraProperties", "ConcurrencyStamp", "CreationTime")
                SELECT gen_random_uuid(), '{{Tenant}}', (ARRAY[1,2,3,4,5,6])[1 + i % 6], '{{type}}',
                       CASE WHEN i % 10 = 0 THEN '{{customer}}'::uuid ELSE ('00000000-0000-0000-0000-' || lpad((1 + i % 2000)::text, 12, '0'))::uuid END,
                       'Customer', ('2021-01-01'::date + (i % 1826))::date, jsonb_build_object(), 1, 0,
                       CASE WHEN i % 50 = 0 THEN 'USD' ELSE 'EUR' END, 1, false, 1, 1, 2,
                       CASE WHEN i % 25 = 0 THEN NULL ELSE (i % 5000)::numeric + 0.25 END,
                       jsonb_build_object()::text, md5(i::text), now()
                FROM generate_series(1, {{total}}) AS i;
                """);
            await context.Database.ExecuteSqlRawAsync("ANALYZE orders.\"TransportOrders\";");
            _output.WriteLine($"Inserted {total} orders in {insert.Elapsed.TotalSeconds:F1} s.");

            var reader = GetRequiredService<ICustomerRevenueReader>();
            var from = new DateOnly(2021, 1, 1);
            var to = new DateOnly(2025, 12, 31);
            var revenue = await reader.GetAsync(customer, from, to);
            revenue.Totals.Sum(t => t.OrderCount).ShouldBeGreaterThan(40_000);
            await P95Async("get_customer_revenue, 100,000 orders of one customer over five years", async () => (await reader.GetAsync(customer, from, to)).Totals.ShouldNotBeEmpty());
            await P95Async("get_customer_revenue, one year", async () => (await reader.GetAsync(customer, new DateOnly(2025, 1, 1), to)).Totals.ShouldNotBeEmpty());
        });
    }

    private static async Task<Guid> TemplateAsync(OrdersDbContext context)
    {
        var type = await context.Set<OpenTms.Orders.TransportOrders.TransportOrderType>().AsNoTracking().Select(t => t.Id).FirstOrDefaultAsync();
        return type == Guid.Empty ? Guid.NewGuid() : type;
    }
}
