using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Orders.EntityFrameworkCore;
using OpenTms.Orders.TransportOrders;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;
using Xunit;
using Xunit.Abstractions;

namespace OpenTms.PostgreSql.Orders;

/// <summary>
/// VOLUME TEST (design 58 "Grid filters, views and search"; design 56 budgets: list p95 &lt; 300 ms): 100,000 orders in
/// tenant A with their stored grid columns, then the list's default page, a view, a status filter, a search over the
/// stored columns and a sort on a derived column are timed. The assertion carries a CI margin (1,500 ms, the tripwire of
/// the Master Data volume tests); the budget itself is the nightly k6 run's. Each query runs once untimed to warm EF
/// Core's query cache. The data lives in one rolled-back transaction. Risk class: High.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class TransportOrderVolumeTests : TransportOrdersPostgreSqlTestBase
{
    private const int OrderCount = 100_000;
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan Tripwire = TimeSpan.FromMilliseconds(1_500);

    private readonly ITestOutputHelper _output;
    private readonly IRepository<TransportOrder, Guid> _orders;
    private readonly IGuidGenerator _guids;
    private readonly IDbContextProvider<OrdersDbContext> _contextProvider;

    public TransportOrderVolumeTests(ITestOutputHelper output)
    {
        _output = output;
        _orders = GetRequiredService<IRepository<TransportOrder, Guid>>();
        _guids = GetRequiredService<IGuidGenerator>();
        _contextProvider = GetRequiredService<IDbContextProvider<OrdersDbContext>>();
    }

    [Fact]
    public async Task The_list_stays_within_the_interactive_read_budget_at_a_hundred_thousand_orders()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var f = await FixtureAsync();
            await InsertVolumeAsync(f);

            var measurements = new List<(string Name, TimeSpan Elapsed)>
            {
                await MeasureAsync("default page (newest first)", () => Orders.GetListAsync(new TransportOrderListRequestDto { StartRow = 0, EndRow = 100 })),
                await MeasureAsync("view Open", () => Orders.GetListAsync(new TransportOrderListRequestDto { View = TransportOrderView.Open, EndRow = 100 })),
                await MeasureAsync("status filter + date range", () => Orders.GetListAsync(new TransportOrderListRequestDto { Status = [TransportOrderStatus.ReadyForInvoicing], DateFrom = Today.AddDays(-30), DateTo = Today, EndRow = 100 })),
                await MeasureAsync("search on a stored stop name", () => Orders.GetListAsync(new TransportOrderListRequestDto { WildcardSearch = "venlo 00042", EndRow = 100 })),
                await MeasureAsync("search on the customer name", () => Orders.GetListAsync(new TransportOrderListRequestDto { WildcardSearch = "klant 7", EndRow = 100 })),
                await MeasureAsync("sorted by margin desc, page 50", () => Orders.GetListAsync(new TransportOrderListRequestDto { SortBy = "marginAmount", SortDir = "desc", StartRow = 4900, EndRow = 5000 })),
                await MeasureAsync("customer filter sorted by number", () => Orders.GetListAsync(new TransportOrderListRequestDto { CustomerRelationId = f.CustomerId, SortBy = "number", EndRow = 100 }))
            };

            foreach (var (name, elapsed) in measurements)
            {
                _output.WriteLine($"{name}: {elapsed.TotalMilliseconds:F0} ms (budget {Budget.TotalMilliseconds} ms, tripwire {Tripwire.TotalMilliseconds} ms)");
            }

            measurements.Where(m => m.Elapsed > Tripwire).Select(m => $"{m.Name}: {m.Elapsed.TotalMilliseconds:F0} ms").ShouldBeEmpty($"queries over the {Tripwire.TotalMilliseconds} ms tripwire");

            /* The results are the right ones, not just fast. */
            (await Orders.GetListAsync(new TransportOrderListRequestDto { EndRow = 100 })).TotalCount.ShouldBe(OrderCount);
            (await Orders.GetListAsync(new TransportOrderListRequestDto { WildcardSearch = "venlo 00042", EndRow = 100 })).FilteredCount.ShouldBe(1);
            var open = await Orders.GetListAsync(new TransportOrderListRequestDto { View = TransportOrderView.Open, EndRow = 100 });
            open.Rows.ShouldAllBe(r => r.Status == TransportOrderStatus.Open);
        });
    }

    private async Task InsertVolumeAsync(OpenTms.TransportRun.TransportRunWorld f)
    {
        var insert = Stopwatch.StartNew();
        /* Read once: Today resolves the transient TransportRunFixture (dozens of proxied services, ~15 ms) on every call. */
        var today = Today;
        var context = await _contextProvider.GetDbContextAsync();
        var statuses = new[] { TransportOrderStatus.Open, TransportOrderStatus.Open, TransportOrderStatus.Draft, TransportOrderStatus.ReadyForInvoicing, TransportOrderStatus.Invoiced, TransportOrderStatus.Archived };
        var header = new OrderHeaderInput(f.OrderTypeId, f.CustomerId, null, "Klant Foods", today, null, null, null, null);
        var terms = TransportOrderFinancialTerms.Default();
        var batch = new List<TransportOrder>(10_000);
        for (var i = 0; i < OrderCount; i++)
        {
            var order = new TransportOrder(_guids.Create(), TenantA, header with { OrderDate = today.AddDays(-(i % 400)), CustomerReference = "REF-" + i, CustomerName = "Klant " + (i % 1000) }, terms, OrderSource.Api, _guids.Create);
            var status = statuses[i % statuses.Length];
            if (status != TransportOrderStatus.Draft)
            {
                order.SetLines([new LineInput(null, new FreightInput(f.TransportUnitId, null, null, null, null, 1, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, PaymentInstruction.NotSet, null, null, null, null, null, null), [], [],
                    [new StopInput(null, f.LoadTypeId, f.DepotId, f.DepotAddressId, null, today, null, null, null, null, null, null), new StopInput(null, f.UnloadTypeId, f.ShopId, f.ShopAddressId, null, today, null, null, null, null, null, null)], null)], _guids.Create);
                order.Confirm($"VOL-{i:D6}", TransportOrderRequiredFields.None, _ => null);
                if (status is TransportOrderStatus.ReadyForInvoicing or TransportOrderStatus.Invoiced or TransportOrderStatus.Archived)
                {
                    order.Approve(status == TransportOrderStatus.Archived ? TransportOrderStatus.Archived : TransportOrderStatus.ReadyForInvoicing);
                }

                if (status == TransportOrderStatus.Invoiced)
                {
                    order.MarkInvoiced();
                }
            }

            order.WriteSummary(new OrderSummary(1000m + i % 500, 600m + i % 300, 400m + (i % 500) - (i % 300), 12.5m, ExecutionProgress.Planned, 1, 2,
                "DC Venlo " + i.ToString("D5"), "Venlo", today, "Shop " + (i % 97), "Utrecht", today.AddDays(1), "R-" + (i % 600)), DateTime.UtcNow);
            batch.Add(order);
            if (batch.Count == 10_000)
            {
                await _orders.InsertManyAsync(batch, autoSave: true);
                context.ChangeTracker.Clear();
                batch.Clear();
            }
        }

        _output.WriteLine($"inserted {OrderCount} orders in {insert.Elapsed.TotalSeconds:F1} s");
    }

    private static async Task<(string Name, TimeSpan Elapsed)> MeasureAsync<T>(string name, Func<Task<T>> query)
    {
        await query();
        var watch = Stopwatch.StartNew();
        await query();
        return (name, watch.Elapsed);
    }
}
