using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.Tenants;
using OpenTms.Platform.TestData;
using OpenTms.Reporting.Facts;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.TestData;

/// <summary>
/// Design 75 "Backfill": "a dataset reset leaves no facts". The real <see cref="TestDatasetResetJob"/> end to end on PostgreSQL, with the real contributors: a tenant that held facts and
/// a finished backfill ends with none of either, so <c>demo</c> holds no stale numbers and a new backfill prepares the new data, while the other shared-tier tenant in the same database keeps its
/// facts and settings. Risk class: Critical (the action deletes a tenant's data, and on the shared tier only the filter separates the tenants).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class ReportingDatasetResetPostgreSqlTests : OpenTmsTestBase<TestDatasetPostgreSqlTestModule>
{
    private static Guid TenantA => TestDatasetTestDatabases.TenantAId;

    private static Guid TenantB => TestDatasetTestDatabases.TenantBId;

    private ICurrentTenant CurrentTenant => GetRequiredService<ICurrentTenant>();

    private IUnitOfWorkManager UnitOfWorkManager => GetRequiredService<IUnitOfWorkManager>();

    private async Task<T> InTenantAsync<T>(Guid tenant, Func<Task<T>> action)
    {
        using (CurrentTenant.Change(tenant))
        using (var unitOfWork = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            var result = await action();
            await unitOfWork.CompleteAsync();
            return result;
        }
    }

    private Task SeedFactsAsync(Guid tenant) => InTenantAsync(tenant, async () =>
    {
        var orderFacts = GetRequiredService<IRepository<OrderFact, Guid>>();
        var tripFacts = GetRequiredService<IRepository<TripFact, Guid>>();
        var state = GetRequiredService<ReportingFactState>();
        var order = new OrderFact(Guid.NewGuid(), tenant);
        order.Apply(new DateOnly(2026, 1, 5), TransportOrderStatus.Open, Guid.NewGuid(), null, Guid.NewGuid(), "EUR", 1m, null, null, null, DateTime.Now);
        await orderFacts.InsertAsync(order);
        var trip = new TripFact(Guid.NewGuid(), tenant);
        trip.Apply(new DateOnly(2026, 1, 5), TransportTripStatus.Planned, TripExecution.OwnFleet, null, null, null, Guid.NewGuid(), 1m, 1m, null, null, null, null, 0, 0, DateTime.Now);
        await tripFacts.InsertAsync(trip);
        await state.SetBackfillCompletedAtAsync(DateTime.Now);
        await state.SetBackfillPositionAsync(new ReportingBackfillPosition { OrdersDone = true, TripsDone = true });
        return 0;
    });

    private Task<(long Orders, long Trips, DateTime? CompletedAt, bool HasPosition)> ReadAsync(Guid tenant) => InTenantAsync(tenant, async () =>
    {
        var state = GetRequiredService<ReportingFactState>();
        return (
            await GetRequiredService<IRepository<OrderFact, Guid>>().LongCountAsync(),
            await GetRequiredService<IRepository<TripFact, Guid>>().LongCountAsync(),
            await state.GetBackfillCompletedAtAsync(),
            await state.GetBackfillPositionAsync() != null);
    });

    [Fact]
    public async Task The_real_job_leaves_a_reset_tenant_without_facts_or_backfill_settings_and_the_other_tenant_as_it_was()
    {
        /* TenantB is the shared-tier tenant: its neighbour's facts sit in the same tables of the host database when it resets. */
        await SeedFactsAsync(TenantB);
        await SeedFactsAsync(TenantA);
        var writer = GetRequiredService<TenantStateWriter>();
        await writer.SetDatasetResetStateAsync(TenantB, TestDatasetResetState.Queued);

        await GetRequiredService<TestDatasetResetJob>().ExecuteAsync(new TestDatasetResetArgs { TenantId = TenantB, Dataset = TestDatasets.Average, RequestId = Guid.NewGuid() });

        var tenant = (await writer.FindAsync(TenantB))!;
        tenant.GetProperty<string?>(TenantProperties.DatasetResetError).ShouldBeNull();
        tenant.GetProperty<string?>(TenantProperties.DatasetResetState).ShouldBe(TestDatasetResetState.Succeeded);

        var reset = await ReadAsync(TenantB);
        reset.Orders.ShouldBe(0, "the generators raise no events: a new backfill prepares the new data");
        reset.Trips.ShouldBe(0);
        reset.CompletedAt.ShouldBeNull();
        reset.HasPosition.ShouldBeFalse();

        var other = await ReadAsync(TenantA);
        other.Orders.ShouldBeGreaterThan(0, "the other tenant keeps its facts");
        other.Trips.ShouldBeGreaterThan(0);
        other.CompletedAt.ShouldNotBeNull();
        other.HasPosition.ShouldBeTrue();

        await InTenantAsync(TenantA, async () =>
        {
            await GetRequiredService<ReportingFactState>().ClearFactsAsync();
            await GetRequiredService<ReportingFactState>().ClearBackfillAsync();
            return 0;
        });
    }
}
