using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTms.Reporting.Facts;
using OpenTms.Reporting.Metrics;
using OpenTms.Reporting.Published;
using Shouldly;
using Xunit;

namespace OpenTms.PostgreSql.Reporting;

/// <summary>
/// Design 75 "Refusals and limits" (risk class Medium): every status of the refusal table, a refusal holding no value, and a query past its 2 seconds answering
/// <c>timeout</c>, never part of an answer: another connection holds a lock on the fact table, so the real statement really waits, and PostgreSQL cancels it.
///
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class MetricRefusalsPostgreSqlTests : ReportingPostgreSqlTestBase
{
    private static Guid Tenant => PostgreSqlTestDatabases.TenantAId;

    private static readonly DateOnly Day = new(2026, 3, 2);

    private static void ShouldBeRefusal(MetricAnswerDto answer, string status)
    {
        answer.Status.ShouldBe(status, answer.Message);
        answer.Message.ShouldNotBeNullOrEmpty();
        answer.Hint.ShouldNotBeNullOrEmpty();
        answer.Rows.ShouldBeNull("a refusal has no value");
        answer.Coverage.ShouldBeNull();
        answer.Definitions.ShouldBeNull();
        answer.TotalGroups.ShouldBeNull();
        answer.LastFactUpdateAt.ShouldBeNull();
        answer.Warnings.ShouldBeNull();
    }

    [Fact]
    public async Task An_unknown_metric_names_what_is_allowed()
    {
        using var features = MetricFeaturesOn();
        await InRolledBackTransactionAsync(Tenant, async () =>
        {
            var answer = await QueryAsync(Ask(Day, Day, ["angry_customers"]));
            ShouldBeRefusal(answer, MetricAnswerStatus.UnknownMetric);
            answer.Hint!.ShouldContain(MetricKeys.OrderRevenue);
            /* Also when one of several is unknown, and it needs no read: nothing else in the request matters yet. */
            ShouldBeRefusal(await QueryAsync(Ask(Day, Day, [MetricKeys.OrderCount, "nope"])), MetricAnswerStatus.UnknownMetric);
        });
    }

    public static TheoryData<string, MetricQueryRequestDto> UnsupportedCombinations => new()
    {
        { "no metrics", new MetricQueryRequestDto { Metrics = [], DateFrom = Day, DateTo = Day } },
        { "metrics left out", new MetricQueryRequestDto { DateFrom = Day, DateTo = Day } },
        { "six metrics", new MetricQueryRequestDto { Metrics = [MetricKeys.TripCount, MetricKeys.TripCost, MetricKeys.TripRevenue, MetricKeys.TripMargin, MetricKeys.TripCo2Kg, MetricKeys.AverageLoadFactor], DateFrom = Day, DateTo = Day } },
        { "both grains", new MetricQueryRequestDto { Metrics = [MetricKeys.OrderCount, MetricKeys.TripCount], DateFrom = Day, DateTo = Day } },
        { "a breakdown of the other grain", new MetricQueryRequestDto { Metrics = [MetricKeys.OrderCount], DateFrom = Day, DateTo = Day, GroupBy = [MetricBreakdowns.Carrier] } },
        { "the other way round", new MetricQueryRequestDto { Metrics = [MetricKeys.TripCount], DateFrom = Day, DateTo = Day, GroupBy = [MetricBreakdowns.Customer] } },
        { "a breakdown that does not exist", new MetricQueryRequestDto { Metrics = [MetricKeys.OrderCount], DateFrom = Day, DateTo = Day, GroupBy = ["region"] } },
        { "three breakdowns", new MetricQueryRequestDto { Metrics = [MetricKeys.OrderCount], DateFrom = Day, DateTo = Day, GroupBy = [MetricBreakdowns.Customer, MetricBreakdowns.Month, MetricBreakdowns.Status] } },
        { "a filter of the other grain on orders", new MetricQueryRequestDto { Metrics = [MetricKeys.OrderCount], DateFrom = Day, DateTo = Day, Filters = new MetricFiltersDto { CarrierRelationId = Guid.NewGuid() } } },
        { "a filter of the other grain on trips", new MetricQueryRequestDto { Metrics = [MetricKeys.TripCount], DateFrom = Day, DateTo = Day, Filters = new MetricFiltersDto { CustomerRelationId = Guid.NewGuid() } } },
        { "a status that does not exist", new MetricQueryRequestDto { Metrics = [MetricKeys.OrderCount], DateFrom = Day, DateTo = Day, Filters = new MetricFiltersDto { Status = ["Lost"] } } },
        { "a status given as a number", new MetricQueryRequestDto { Metrics = [MetricKeys.OrderCount], DateFrom = Day, DateTo = Day, Filters = new MetricFiltersDto { Status = ["2"] } } },
        { "an execution that does not exist", new MetricQueryRequestDto { Metrics = [MetricKeys.TripCount], DateFrom = Day, DateTo = Day, Filters = new MetricFiltersDto { Execution = "Teleport" } } },
        { "a sort that is not chosen", new MetricQueryRequestDto { Metrics = [MetricKeys.OrderCount], DateFrom = Day, DateTo = Day, Sort = MetricKeys.OrderRevenue } },
        { "a sort on a period not among the breakdowns", new MetricQueryRequestDto { Metrics = [MetricKeys.OrderCount], DateFrom = Day, DateTo = Day, Sort = MetricBreakdowns.Month } },
    };

    [Theory]
    [MemberData(nameof(UnsupportedCombinations))]
    public async Task An_unsupported_combination_is_refused_with_a_hint(string what, MetricQueryRequestDto request)
    {
        using var features = MetricFeaturesOn();
        await InRolledBackTransactionAsync(Tenant, async () =>
        {
            var answer = await QueryAsync(request);
            answer.Status.ShouldBe(MetricAnswerStatus.UnsupportedCombination, what);
            ShouldBeRefusal(answer, MetricAnswerStatus.UnsupportedCombination);
        });
    }

    public static TheoryData<string, DateOnly?, DateOnly?> InvalidPeriods => new()
    {
        { "both missing", null, null },
        { "the start missing", null, Day },
        { "the end missing", Day, null },
        { "reversed", Day, Day.AddDays(-1) },
        { "a day over five years", Day, Day.AddYears(5).AddDays(1) },
    };

    [Theory]
    [MemberData(nameof(InvalidPeriods))]
    public async Task An_invalid_period_is_refused(string what, DateOnly? from, DateOnly? to)
    {
        using var features = MetricFeaturesOn();
        await InRolledBackTransactionAsync(Tenant, async () =>
        {
            var answer = await QueryAsync(new MetricQueryRequestDto { Metrics = [MetricKeys.OrderCount], DateFrom = from, DateTo = to });
            answer.Status.ShouldBe(MetricAnswerStatus.InvalidPeriod, what);
            ShouldBeRefusal(answer, MetricAnswerStatus.InvalidPeriod);
        });
    }

    [Fact]
    public async Task Exactly_five_years_is_allowed()
    {
        using var features = MetricFeaturesOn();
        await InRolledBackTransactionAsync(Tenant, async () =>
        {
            await State.SetBackfillCompletedAtAsync(DateTime.Now);
            await FlushAsync();
            (await QueryAsync(Ask(Day, Day.AddYears(5), [MetricKeys.OrderCount]))).Status.ShouldBe(MetricAnswerStatus.Ok);
        });
    }

    [Fact]
    public async Task Until_the_backfill_finished_a_query_answers_not_ready_with_the_progress_of_its_grain()
    {
        using var features = MetricFeaturesOn();
        await InRolledBackTransactionAsync(Tenant, async () =>
        {
            var orders = await QueryAsync(Ask(Day, Day, [MetricKeys.OrderCount]));
            ShouldBeRefusal(orders, MetricAnswerStatus.NotReady);
            orders.Hint!.ShouldContain("orders prepared");

            var trips = await QueryAsync(Ask(Day, Day, [MetricKeys.TripCount]));
            ShouldBeRefusal(trips, MetricAnswerStatus.NotReady);
            trips.Hint!.ShouldContain("trips prepared");

            (await Metrics.ListAsync()).FactsReady.ShouldBeFalse();

            /* The catalog and the period come before it: they need no read. */
            ShouldBeRefusal(await QueryAsync(Ask(Day, Day, ["nope"])), MetricAnswerStatus.UnknownMetric);
            ShouldBeRefusal(await QueryAsync(Ask(Day, Day.AddDays(-1), [MetricKeys.OrderCount])), MetricAnswerStatus.InvalidPeriod);
        });
    }

    [Fact]
    public async Task Without_the_feature_every_metric_is_not_allowed_and_the_progress_stays_unsaid()
    {
        /* Permissions are always allowed in this test process: the host tests prove real grants. */
        await InRolledBackTransactionAsync(Tenant, async () =>
        {
            var answer = await QueryAsync(Ask(Day, Day, [MetricKeys.OrderCount]));
            ShouldBeRefusal(answer, MetricAnswerStatus.NotAllowed);
            answer.Hint!.ShouldNotContain("prepared");
            (await Metrics.ListAsync()).Metrics.ShouldBeEmpty();
        });
    }

    [Fact]
    public async Task A_query_past_its_2_seconds_is_cancelled_in_PostgreSQL_and_answers_timeout_with_no_partial_result()
    {
        using var features = MetricFeaturesOn();
        GetRequiredService<IOptions<ReportingMetricsOptions>>().Value.QueryTimeout.ShouldBe(TimeSpan.FromSeconds(2), "the default of the budget 'never block a request over 2 s'");
        var tenant = PostgreSqlTestDatabases.TransportTenantAId;
        await CommittedAsync(tenant, () => State.SetBackfillCompletedAtAsync(DateTime.Now));
        try
        {
            await using var blocker = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(PostgreSqlTestDatabases.TransportTenantADatabase));
            await blocker.OpenAsync();
            await using var transaction = await blocker.BeginTransactionAsync();
            await using (var lockFacts = new NpgsqlCommand("LOCK TABLE reporting.\"OrderFacts\" IN ACCESS EXCLUSIVE MODE", blocker, transaction))
            {
                await lockFacts.ExecuteNonQueryAsync();
            }

            var watch = Stopwatch.StartNew();
            var answer = await CommittedAsync(tenant, () => QueryAsync(Ask(Day, Day, [MetricKeys.OrderCount], [MetricBreakdowns.Customer])), transactional: false);
            watch.Stop();

            ShouldBeRefusal(answer, MetricAnswerStatus.Timeout);
            answer.Hint!.ShouldContain("shorter period");
            watch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1.9));
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(4), "cancelled at 2 seconds, not at the end of the lock");

            await transaction.RollbackAsync();
        }
        finally
        {
            await CommittedAsync(tenant, () => State.ClearBackfillAsync());
        }
    }
}
