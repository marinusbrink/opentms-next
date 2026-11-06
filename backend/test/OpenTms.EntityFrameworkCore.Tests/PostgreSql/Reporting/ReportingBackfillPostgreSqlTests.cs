using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTms.Orders.Published;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.Published;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Reporting.Facts;
using OpenTms.Reporting.Features;
using OpenTms.Reporting.Metrics;
using OpenTms.Reporting.Published;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.SettingManagement;
using Xunit;

namespace OpenTms.PostgreSql.Reporting;

/// <summary>
/// Design 75 "Backfill" (risk class High) on PostgreSQL through the real scheduler, dispatcher and job: the backfill resumes after an interruption, stays
/// <c>not_ready</c> until every fact exists rather than merely was requested, its results equal the source, it never runs for a tenant with the feature off,
/// turning the feature off and on clears and restarts it, and counts that still differ an hour after the position ran out start the sweep again without clearing.
/// Each test works in a tenant whose facts it starts by clearing; rows commit.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class ReportingBackfillPostgreSqlTests : ReportingPostgreSqlTestBase
{
    private static Guid Tenant => PostgreSqlTestDatabases.TransportTenantAId;

    private const int BatchSize = 3;

    private ReportingFactBackfillRunner Runner => GetRequiredService<ReportingFactBackfillRunner>();

    private async Task<bool> PassAsync(CancellationToken cancellationToken = default)
    {
        using (AsBackgroundProcess())
        using (CurrentTenant.Change(Tenant))
        {
            return await Runner.RunForCurrentTenantAsync(cancellationToken);
        }
    }

    private async Task<ReportingBackfillPosition?> PositionAsync()
        => await CommittedAsync(Tenant, () => State.GetBackfillPositionAsync(), transactional: false);

    private async Task<DateTime?> CompletedAtAsync()
        => await CommittedAsync(Tenant, () => State.GetBackfillCompletedAtAsync(), transactional: false);

    private async Task<(long Orders, long Trips)> SourceCountsAsync()
        => await CommittedAsync(Tenant, async () => (await GetRequiredService<IOrderFactReader>().GetCountAsync(), await GetRequiredService<ITripFactReader>().GetCountAsync()), transactional: false);

    private async Task<(long Orders, long Trips)> FactCountsAsync()
        => await CommittedAsync(Tenant, async () => (await OrderFacts.LongCountAsync(), await TripFacts.LongCountAsync()), transactional: false);

    /// <summary>A clean start, with this batch size, and committed rows of each grain so every pass has something to request.</summary>
    private async Task StartOverAsync(int orders = 7, int trips = 4)
    {
        GetRequiredService<IOptions<ReportingMetricsOptions>>().Value.BackfillBatchSize = BatchSize;
        await CommittedAsync(Tenant, async () =>
        {
            await State.ClearFactsAsync();
            await State.ClearBackfillAsync();
        });
        await DriveFactsAsync(Tenant);
        ForgetRecordedJobs();

        await CommittedAsync(Tenant, async () =>
        {
            var orderType = await OrderTypeOfTenantAsync();
            var tripType = await TripTypeOfTenantAsync();
            for (var i = 0; i < orders; i++)
            {
                await SeedOrderAsync(Guid.NewGuid(), orderType, new DateOnly(2025, 3, 3).AddDays(i), revenue: 10m + i);
            }

            for (var i = 0; i < trips; i++)
            {
                await SeedTripAsync(tripType, new DateOnly(2025, 3, 3).AddDays(i), cost: 5m + i);
            }
        });
    }

    /// <summary>Passes and the jobs they caused, until the backfill says it is done.</summary>
    private async Task RunToTheEndAsync()
    {
        for (var i = 0; i < 2_000; i++)
        {
            var worked = await PassAsync();
            await DriveFactsAsync(Tenant);
            if (!worked)
            {
                return;
            }
        }

        throw new InvalidOperationException("The backfill did not finish.");
    }

    [Fact]
    public async Task A_first_pass_clears_old_facts_and_requests_one_batch_and_later_passes_go_on_from_its_position()
    {
        using var features = MetricFeaturesOn();
        await StartOverAsync();
        /* A stale fact from an earlier time the feature was on. */
        var stale = Guid.NewGuid();
        await CommittedAsync(Tenant, async () =>
        {
            var fact = new OrderFact(stale, Tenant);
            fact.Apply(new DateOnly(2020, 1, 1), TransportOrderStatus.Open, Guid.NewGuid(), null, Guid.NewGuid(), "EUR", 1m, null, null, null, DateTime.Now);
            await OrderFacts.InsertAsync(fact);
        });

        (await PassAsync()).ShouldBeTrue();
        (await OrderFactAsync(Tenant, stale)).ShouldBeNull("the first pass clears the tenant's facts");

        var first = (await PositionAsync()).ShouldNotBeNull();
        first.OrdersAfter.ShouldNotBeNull();
        first.TripsAfter.ShouldNotBeNull();

        await DriveFactsAsync(Tenant);
        (await FactCountsAsync()).ShouldBe((BatchSize, BatchSize), "one batch of each grain, and requesting is what the jobs act on");

        (await PassAsync()).ShouldBeTrue();
        var second = (await PositionAsync()).ShouldNotBeNull();
        second.OrdersAfter!.Value.CompareTo(first.OrdersAfter!.Value).ShouldBeGreaterThan(0, "a pass goes on after the last id it requested");
        await DriveFactsAsync(Tenant);
        (await FactCountsAsync()).ShouldBe((BatchSize * 2, BatchSize * 2), "and does not clear again");
    }

    [Fact]
    public async Task An_interrupted_pass_keeps_its_position_and_the_next_one_repeats_it_without_clearing()
    {
        using var features = MetricFeaturesOn();
        await StartOverAsync();
        (await PassAsync()).ShouldBeTrue();
        await DriveFactsAsync(Tenant);
        var position = (await PositionAsync()).ShouldNotBeNull();
        var factsBefore = await FactCountsAsync();
        factsBefore.Orders.ShouldBeGreaterThan(0);

        /* The host restarts in the middle of a pass: the token is cancelled, its transaction rolls back. */
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => PassAsync(cancelled.Token));

        var kept = (await PositionAsync()).ShouldNotBeNull();
        kept.OrdersAfter.ShouldBe(position.OrdersAfter, "the position and the requests commit together, or neither");
        kept.TripsAfter.ShouldBe(position.TripsAfter);
        (await FactCountsAsync()).ShouldBe(factsBefore, "the facts already made stay");

        /* A new process picks up from the stored position. */
        await RunToTheEndAsync();

        (await CompletedAtAsync()).ShouldNotBeNull();
        var (orders, trips) = await SourceCountsAsync();
        (await FactCountsAsync()).ShouldBe((orders, trips), "every source has its fact");
    }

    [Fact]
    public async Task Until_every_fact_exists_a_query_answers_not_ready_even_when_everything_was_requested()
    {
        using var features = MetricFeaturesOn();
        await StartOverAsync();
        var request = Ask(new DateOnly(2025, 3, 3), new DateOnly(2025, 3, 20), [MetricKeys.OrderCount]);

        /* Every id requested, while no job has run: the position ran out, the counts do not match. */
        for (var i = 0; i < 2_000; i++)
        {
            await PassAsync();
            var current = await PositionAsync();
            if (current is { OrdersDone: true, TripsDone: true })
            {
                break;
            }
        }

        var position = (await PositionAsync()).ShouldNotBeNull();
        position.OrdersDone.ShouldBeTrue();
        position.TripsDone.ShouldBeTrue();
        position.ExhaustedAt.ShouldNotBeNull();
        (await CompletedAtAsync()).ShouldBeNull("requesting is not the same as having run");
        var answer = await CommittedAsync(Tenant, () => QueryAsync(request), transactional: false);
        answer.Status.ShouldBe(MetricAnswerStatus.NotReady);
        answer.Hint.ShouldContain("orders prepared");
        (await CommittedAsync(Tenant, () => Metrics.ListAsync(), transactional: false)).FactsReady.ShouldBeFalse();

        /* The jobs run; the next pass sees the counts match. */
        await DriveFactsAsync(Tenant);
        (await PassAsync()).ShouldBeTrue();
        (await CompletedAtAsync()).ShouldNotBeNull();
        (await CommittedAsync(Tenant, () => QueryAsync(request), transactional: false)).Status.ShouldBe(MetricAnswerStatus.Ok);
        (await CommittedAsync(Tenant, () => Metrics.ListAsync(), transactional: false)).FactsReady.ShouldBeTrue();
        (await PassAsync()).ShouldBeFalse("a finished backfill does no more");
    }

    [Fact]
    public async Task The_results_after_a_backfill_equal_the_source()
    {
        using var features = MetricFeaturesOn();
        await StartOverAsync();
        await RunToTheEndAsync();

        var orders = await CommittedAsync(Tenant, async () =>
            (await GetRequiredService<IRepository<TransportOrder, Guid>>().GetQueryableAsync()).Select(o => new { o.Id, o.OrderDate, o.Status, o.CustomerRelationId, o.RevenueAmount, o.CostOfRevenueAmount, o.FinancialTerms.CurrencyCode }).ToList(), transactional: false);
        var facts = await CommittedAsync(Tenant, async () =>
            (await OrderFacts.GetQueryableAsync()).Select(f => new { f.Id, f.OrderDate, f.Status, f.CustomerRelationId, f.RevenueAmount, f.CostOfRevenueAmount, f.CurrencyCode }).ToList(), transactional: false);
        facts.OrderBy(f => f.Id).Select(f => (f.Id, f.OrderDate, f.Status, f.CustomerRelationId, f.RevenueAmount, f.CostOfRevenueAmount, f.CurrencyCode))
            .ShouldBe(orders.OrderBy(o => o.Id).Select(o => (o.Id, o.OrderDate, o.Status, o.CustomerRelationId, o.RevenueAmount, o.CostOfRevenueAmount, o.CurrencyCode)));

        var trips = await CommittedAsync(Tenant, async () =>
            (await GetRequiredService<IRepository<TransportTrip, Guid>>().GetQueryableAsync()).Select(t => new { t.Id, t.TripDate, t.Status, t.CostAmount, t.RevenueAmount }).ToList(), transactional: false);
        var tripFacts = await CommittedAsync(Tenant, async () =>
            (await TripFacts.GetQueryableAsync()).Select(f => new { f.Id, f.TripDate, f.Status, f.CostAmount, f.RevenueAmount }).ToList(), transactional: false);
        tripFacts.OrderBy(f => f.Id).Select(f => (f.Id, f.TripDate, f.Status, f.CostAmount, f.RevenueAmount))
            .ShouldBe(trips.OrderBy(t => t.Id).Select(t => (t.Id, t.TripDate, t.Status, t.CostAmount, t.RevenueAmount)));
    }

    [Fact]
    public async Task A_tenant_with_the_feature_off_gets_no_run()
    {
        using (MetricFeaturesOn())
        {
            await StartOverAsync(orders: 1, trips: 1);
        }

        (await PassAsync()).ShouldBeFalse();
        (await PositionAsync()).ShouldBeNull();
        (await FactCountsAsync()).ShouldBe((0L, 0L));
    }

    [Fact]
    public async Task Turning_the_feature_off_and_on_clears_and_restarts_it()
    {
        using var features = MetricFeaturesOn();
        await StartOverAsync(orders: 2, trips: 1);
        await RunToTheEndAsync();
        (await CompletedAtAsync()).ShouldNotBeNull();
        var old = (await CommittedAsync(Tenant, () => OrderFacts.GetListAsync(), transactional: false)).First();

        /* A fact the sources no longer back: it goes stale while the feature is off. */
        var stale = Guid.NewGuid();
        await CommittedAsync(Tenant, async () =>
        {
            var fact = new OrderFact(stale, Tenant);
            fact.Apply(new DateOnly(2020, 1, 1), TransportOrderStatus.Open, Guid.NewGuid(), null, Guid.NewGuid(), "EUR", 1m, null, null, null, DateTime.Now);
            await OrderFacts.InsertAsync(fact);
        });

        /* The daily repair of a tenant whose feature is off clears the completion and the position. */
        using (OpenTms.Features.TestFeatureValueProvider.Override(ReportingFeatures.Metrics, null))
        using (AsBackgroundProcess())
        using (CurrentTenant.Change(Tenant))
        {
            await GetRequiredService<ReportingFactRepairRunner>().RunForCurrentTenantAsync();
        }

        (await CompletedAtAsync()).ShouldBeNull();
        (await PositionAsync()).ShouldBeNull();
        (await OrderFactAsync(Tenant, stale)).ShouldNotBeNull("nothing clears facts while the feature is off");
        var answer = await CommittedAsync(Tenant, () => QueryAsync(Ask(new DateOnly(2025, 3, 3), new DateOnly(2025, 3, 3), [MetricKeys.OrderCount])), transactional: false);
        answer.Status.ShouldBe(MetricAnswerStatus.NotReady);

        /* On again: a new backfill, which clears first. */
        (await PassAsync()).ShouldBeTrue();
        (await OrderFactAsync(Tenant, stale)).ShouldBeNull("the first pass of the new backfill clears the stale facts");
        await RunToTheEndAsync();
        (await CompletedAtAsync()).ShouldNotBeNull();
        var (orders, trips) = await SourceCountsAsync();
        (await FactCountsAsync()).ShouldBe((orders, trips));
        (await OrderFactAsync(Tenant, old.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Counts_that_still_differ_after_an_hour_start_the_sweep_again_without_clearing_and_not_before()
    {
        using var features = MetricFeaturesOn();
        await StartOverAsync();

        /* Every id requested, and a job of each batch failed for good: nothing here runs them. */
        for (var i = 0; i < 2_000; i++)
        {
            await PassAsync();
            if (await PositionAsync() is { OrdersDone: true, TripsDone: true })
            {
                break;
            }
        }

        ForgetRecordedJobs();
        await DriveFactsAsync(Tenant);
        ForgetRecordedJobs();
        var kept = Guid.NewGuid();
        await CommittedAsync(Tenant, async () =>
        {
            var fact = new OrderFact(kept, Tenant);
            fact.Apply(new DateOnly(2020, 1, 1), TransportOrderStatus.Open, Guid.NewGuid(), null, Guid.NewGuid(), "EUR", 1m, null, null, null, DateTime.Now);
            await OrderFacts.InsertAsync(fact);
        });

        var position = (await PositionAsync()).ShouldNotBeNull();

        /* 59 minutes after the position ran out: it waits. */
        position.ExhaustedAt = DateTime.Now.AddMinutes(-59);
        await CommittedAsync(Tenant, () => State.SetBackfillPositionAsync(position));
        (await PassAsync()).ShouldBeTrue();
        var waiting = (await PositionAsync()).ShouldNotBeNull();
        waiting.OrdersDone.ShouldBeTrue("not yet swept again");
        waiting.ExhaustedAt.ShouldNotBeNull();
        (await CompletedAtAsync()).ShouldBeNull();

        /* 61 minutes: the sweep starts again, without clearing. */
        position.ExhaustedAt = DateTime.Now.AddMinutes(-61);
        await CommittedAsync(Tenant, () => State.SetBackfillPositionAsync(position));
        (await PassAsync()).ShouldBeTrue();
        var swept = (await PositionAsync()).ShouldNotBeNull();
        swept.OrdersDone.ShouldBeFalse("the position starts over");
        swept.OrdersAfter.ShouldBeNull();
        swept.ExhaustedAt.ShouldBeNull();
        (await OrderFactAsync(Tenant, kept)).ShouldNotBeNull("a sweep again does not clear");
        (await CompletedAtAsync()).ShouldBeNull();

        /* A failed calculation gets its second chance; a surplus fact still keeps the counts apart, so remove it as a repair would. */
        await CommittedAsync(Tenant, () => OrderFacts.DeleteAsync(kept));
        await RunToTheEndAsync();
        (await CompletedAtAsync()).ShouldNotBeNull("the second sweep finishes the job");
        var (orders, trips) = await SourceCountsAsync();
        (await FactCountsAsync()).ShouldBe((orders, trips));
    }

    [Fact]
    public async Task A_second_tenant_backfills_on_its_own_and_touches_nothing_of_the_first()
    {
        using var features = MetricFeaturesOn();
        await StartOverAsync(orders: 2, trips: 1);
        await RunToTheEndAsync();
        var firstFacts = await FactCountsAsync();
        var shared = PostgreSqlTestDatabases.TransportSharedTenantId;

        await CommittedAsync(shared, async () =>
        {
            await State.ClearFactsAsync();
            await State.ClearBackfillAsync();
            await SeedOrderAsync(Guid.NewGuid(), await OrderTypeOfTenantAsync(), new DateOnly(2025, 4, 1), revenue: 3m);
        });
        for (var i = 0; i < 2_000; i++)
        {
            bool worked;
            using (AsBackgroundProcess())
            using (CurrentTenant.Change(shared))
            {
                worked = await Runner.RunForCurrentTenantAsync();
            }

            await DriveFactsAsync(shared);
            if (!worked)
            {
                break;
            }
        }

        (await CommittedAsync(shared, () => State.GetBackfillCompletedAtAsync(), transactional: false)).ShouldNotBeNull();
        (await FactCountsAsync()).ShouldBe(firstFacts);
    }
}
