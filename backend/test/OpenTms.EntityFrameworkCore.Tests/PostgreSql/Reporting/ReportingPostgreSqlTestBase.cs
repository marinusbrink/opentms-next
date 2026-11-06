using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenTms.Features;
using OpenTms.Orders.EntityFrameworkCore;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.EntityFrameworkCore;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.Events.Outbox;
using OpenTms.Platform.Features;
using OpenTms.PostgreSql.TransportRun;
using OpenTms.Reporting.EntityFrameworkCore;
using OpenTms.Reporting.Facts;
using OpenTms.Reporting.Features;
using OpenTms.Reporting.Published;
using OpenTms.TestDoubles;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.PostgreSql.Reporting;

/// <summary>
/// What the tests of design 75 share on real PostgreSQL: source orders and trips with chosen stored values, each turned into its fact by the
/// real calculator; the feature on through the test feature provider (the host tests prove stored feature values and real grants); and the
/// outbox dispatcher and derived-value job driven synchronously as production's worker and Hangfire run them, without a current user.
/// </summary>
public abstract class ReportingPostgreSqlTestBase : OpenTmsTestBase<TransportRunPostgreSqlTestModule>
{
    protected static readonly AbpUnitOfWorkOptions Transactional = new() { IsTransactional = true };

    protected ICurrentTenant CurrentTenant => GetRequiredService<ICurrentTenant>();

    protected IUnitOfWorkManager UnitOfWorkManager => GetRequiredService<IUnitOfWorkManager>();

    protected IMetricQuery Metrics => GetRequiredService<IMetricQuery>();

    protected IRepository<OrderFact, Guid> OrderFacts => GetRequiredService<IRepository<OrderFact, Guid>>();

    protected IRepository<TripFact, Guid> TripFacts => GetRequiredService<IRepository<TripFact, Guid>>();

    protected ReportingFactState State => GetRequiredService<ReportingFactState>();

    /// <summary>The features the catalog needs, for the current async flow.</summary>
    protected static IDisposable MetricFeaturesOn()
    {
        var overrides = new[]
        {
            ReportingFeatures.Metrics, AppFeatures.Transport, AppFeatures.TransportApp.Orders, AppFeatures.TransportApp.Trips
        }.Select(feature => TestFeatureValueProvider.Override(feature, "true")).ToList();
        return new Disposer(() =>
        {
            foreach (var item in Enumerable.Reverse(overrides))
            {
                item.Dispose();
            }
        });
    }

    protected async Task InRolledBackTransactionAsync(Guid? tenantId, Func<Task> action)
    {
        using (CurrentTenant.Change(tenantId))
        using (var unitOfWork = UnitOfWorkManager.Begin(Transactional, requiresNew: true))
        {
            try
            {
                await action();
            }
            finally
            {
                await unitOfWork.RollbackAsync();
            }
        }
    }

    protected async Task<T> CommittedAsync<T>(Guid? tenantId, Func<Task<T>> action, bool transactional = true)
    {
        using (CurrentTenant.Change(tenantId))
        using (var unitOfWork = UnitOfWorkManager.Begin(transactional ? Transactional : new AbpUnitOfWorkOptions(), requiresNew: true))
        {
            var result = await action();
            await unitOfWork.CompleteAsync();
            return result;
        }
    }

    protected Task CommittedAsync(Guid? tenantId, Func<Task> action, bool transactional = true)
        => CommittedAsync(tenantId, async () => { await action(); return 0; }, transactional);

    protected async Task<MetricAnswerDto> QueryAsync(MetricQueryRequestDto request, CancellationToken cancellationToken = default)
        => await Metrics.QueryAsync(request, cancellationToken);

    protected static MetricQueryRequestDto Ask(DateOnly from, DateOnly to, string[] metrics, string[]? groupBy = null, MetricFiltersDto? filters = null, string? sort = null, int? top = 100)
        => new() { Metrics = metrics.ToList(), DateFrom = from, DateTo = to, GroupBy = groupBy?.ToList(), Filters = filters, Sort = sort, Top = top };

    /* ---------- Source rows with chosen stored values ---------- */

    /// <summary>A real order type of the current tenant: the table has a foreign key to it. Seeded, or made here.</summary>
    protected async Task<Guid> OrderTypeOfTenantAsync(int index = 0)
    {
        var types = (await GetRequiredService<IRepository<TransportOrderType, Guid>>().GetQueryableAsync()).OrderBy(t => t.Id).Select(t => t.Id).ToList();
        while (types.Count <= index)
        {
            var run = GetRequiredService<OpenTms.TransportRun.TransportRunFixture>();
            var load = await run.WaypointTypeAsync("LOAD", "Load", "Laden", WaypointActivity.Load, 30);
            var unload = await run.WaypointTypeAsync("UNLOAD", "Unload", "Lossen", WaypointActivity.Unload, 20);
            types.Add(await run.OrderTypeAsync(TransportOrderRequiredFields.None, OpenTms.TransportRun.TransportRunFixture.NewSuffix(), load, unload));
        }

        return types[index];
    }

    /// <summary>A real trip type of the current tenant: the table has a foreign key to it. Seeded, or made here.</summary>
    protected async Task<Guid> TripTypeOfTenantAsync(int index = 0)
    {
        var types = (await GetRequiredService<IRepository<TransportTripType, Guid>>().GetQueryableAsync()).OrderBy(t => t.Id).Select(t => t.Id).ToList();
        while (types.Count <= index)
        {
            types.Add(await GetRequiredService<OpenTms.TransportRun.TransportRunFixture>().TripTypeAsync("T" + OpenTms.TransportRun.TransportRunFixture.NewSuffix()));
        }

        return types[index];
    }

    /// <summary>An order as its services and calculators would have left it, straight in Orders' table (no outbox event).</summary>
    protected async Task<Guid> SeedOrderAsync(
        Guid customerId,
        Guid typeId,
        DateOnly date,
        TransportOrderStatus status = TransportOrderStatus.Open,
        string currency = "EUR",
        decimal? revenue = null,
        decimal? cost = null,
        decimal? margin = null,
        decimal? co2 = null,
        Guid? organizationUnitId = null)
    {
        var order = new TransportOrder(
            Guid.NewGuid(),
            CurrentTenant.Id,
            new OrderHeaderInput(typeId, customerId, null, "Customer", date, null, organizationUnitId, null, null),
            new FinancialTermsInput(currency, null, null, [], OpenTms.Financial.Tariffs.MultipleMatches.All, [], null, false),
            OrderSource.Screen,
            Guid.NewGuid);
        order.WriteSummary(new OrderSummary(revenue, cost, margin, co2, ExecutionProgress.Unplanned, 0, 0, null, null, null, null, null, null, null), DateTime.Now);
        await GetRequiredService<IRepository<TransportOrder, Guid>>().InsertAsync(order, autoSave: true);
        var context = await GetRequiredService<IDbContextProvider<OrdersDbContext>>().GetDbContextAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE orders.\"TransportOrders\" SET \"Status\" = {(int)status} WHERE \"Id\" = {order.Id}");
        return order.Id;
    }

    /// <summary>A trip as its services and calculators would have left it, straight in Planning's table (no outbox event).</summary>
    protected async Task<Guid> SeedTripAsync(
        Guid typeId,
        DateOnly date,
        TransportTripStatus status = TransportTripStatus.Planned,
        TripExecution execution = TripExecution.OwnFleet,
        Guid? carrierId = null,
        Guid? vehicleId = null,
        decimal? cost = null,
        decimal? revenue = null,
        decimal? co2 = null,
        decimal? loadFactor = null,
        int? plannedDistanceM = null,
        int? actualDistanceM = null,
        Guid? organizationUnitId = null,
        params (DateTimeOffset? Planned, DateTimeOffset? Actual)[] stops)
    {
        var departure = new DateTimeOffset(date.ToDateTime(new TimeOnly(6, 0)), TimeSpan.Zero);
        var trip = new TransportTrip(Guid.NewGuid(), CurrentTenant.Id, "RPT-" + Guid.NewGuid().ToString("N")[..12], typeId, date, execution,
            StopCompositionMode.MergeByLocationAndActivity, FirstLastStopRule.None, departure);
        if (carrierId != null)
        {
            trip.SetCarrier(carrierId, null);
        }

        if (vehicleId != null)
        {
            trip.SetVehicles(null, [vehicleId.Value]);
        }

        trip.SetMoney(cost, revenue);
        trip.SetCo2(co2);
        trip.SetLoad(loadFactor, null);
        trip.SetPlannedRoute(plannedDistanceM, null, null);
        trip.SetActualDistance(actualDistanceM);
        for (var i = 0; i < stops.Length; i++)
        {
            var stop = new TripStop(Guid.NewGuid(), CurrentTenant.Id, trip.Id, i + 1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Stop " + i, "Utrecht", null, null, "Europe/Amsterdam", false);
            stop.SetPlanned(stops[i].Planned, null, null);
            stop.RecordActuals(stops[i].Actual, null, null);
            trip.Stops.Add(stop);
        }

        await GetRequiredService<IRepository<TransportTrip, Guid>>().InsertAsync(trip, autoSave: true);
        var context = await GetRequiredService<IDbContextProvider<PlanningExecutionDbContext>>().GetDbContextAsync();
        var parameters = new object?[] { (int)status, organizationUnitId, trip.Id };
        await context.Database.ExecuteSqlRawAsync("UPDATE planning.\"TransportTrips\" SET \"Status\" = {0}, \"OrganizationUnitId\" = {1} WHERE \"Id\" = {2}", parameters!);
        return trip.Id;
    }

    /// <summary>The real calculators, in the caller's transaction (the job only adds its own transaction and version guard).</summary>
    protected async Task CalculateOrderFactAsync(Guid orderId, long version = 1)
        => await GetRequiredService<OrderFactCalculator>().CalculateAsync(new DerivedValueCalculationContext(ReportingFactConsts.OrderFactTargetType, orderId, version, CancellationToken.None));

    protected async Task CalculateTripFactAsync(Guid tripId, long version = 1)
        => await GetRequiredService<TripFactCalculator>().CalculateAsync(new DerivedValueCalculationContext(ReportingFactConsts.TripFactTargetType, tripId, version, CancellationToken.None));

    /// <summary>A flush: the calculators leave their rows to the unit of work, and the query reads SQL.</summary>
    protected Task FlushAsync() => UnitOfWorkManager.Current!.SaveChangesAsync();

    protected async Task<ReportingDbContext> ReportingContextAsync() => await GetRequiredService<IDbContextProvider<ReportingDbContext>>().GetDbContextAsync();

    /* ---------- The background paths, driven ---------- */

    private static readonly HashSet<string> FactTargets = [ReportingFactConsts.OrderFactTargetType, ReportingFactConsts.TripFactTargetType];

    /// <summary>
    /// What the outbox worker and Hangfire do in production, to the end and without a user: dispatch the tenant's outbox, run the jobs the handlers
    /// enqueued, repeat until nothing moves. <paramref name="targets"/> null runs every target type's job (an order's summary raises the event that
    /// reaches its fact); otherwise only those, and the rest stay unrun. Returns the jobs run.
    /// </summary>
    protected async Task<int> DriveAsync(Guid tenantId, IReadOnlySet<string>? targets = null)
    {
        using var anonymous = AsBackgroundProcess();
        var dispatcher = GetRequiredService<IOutboxDispatcher>();
        var jobs = GetRequiredService<RecordingBackgroundJobManager>();
        var job = GetRequiredService<DerivedValueCalculationJob>();
        var run = 0;
        for (var round = 0; round < 40; round++)
        {
            var dispatched = await dispatcher.DispatchAsync(tenantId);
            var pending = jobs.Drain<DerivedValueCalculationArgs>().Where(j => j.TenantId == tenantId && (targets == null || targets.Contains(j.TargetType))).ToList();
            foreach (var args in pending)
            {
                await job.ExecuteAsync(args);
                run++;
            }

            if (dispatched == 0 && pending.Count == 0)
            {
                return run;
            }
        }

        throw new InvalidOperationException("The outbox did not come to rest within 40 rounds.");
    }

    /// <summary>The jobs of the fact tables alone, nothing else of the chain: for a test that wants a fact's calculator without the order's.</summary>
    protected Task<int> DriveFactsAsync(Guid tenantId) => DriveAsync(tenantId, FactTargets);

    /// <summary>Takes the recorded leftovers of earlier tests out, so a count means this test.</summary>
    protected void ForgetRecordedJobs() => GetRequiredService<RecordingBackgroundJobManager>().Clear();

    protected async Task<OrderFact?> OrderFactAsync(Guid tenantId, Guid orderId)
        => await CommittedAsync(tenantId, () => OrderFacts.FindAsync(orderId), transactional: false);

    protected async Task<TripFact?> TripFactAsync(Guid tenantId, Guid tripId)
        => await CommittedAsync(tenantId, () => TripFacts.FindAsync(tripId), transactional: false);

    private sealed class Disposer(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
