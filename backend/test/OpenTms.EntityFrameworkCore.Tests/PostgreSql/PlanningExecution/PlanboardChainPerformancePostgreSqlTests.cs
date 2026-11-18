using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial.Tariffs;
using OpenTms.Financial.Tariffs.Calculators;
using OpenTms.Orders.Events;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.Events.Outbox;
using OpenTms.PostgreSql.TransportRun;
using OpenTms.TestDoubles;
using OpenTms.TransportRun;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.PlanningExecution;

/// <summary>
/// Design 75 risk analysis — "Performance: TripCalculator chain" (High risk, row 10): the full derived-value chain
/// from leg assignment to DerivedValueStateStore recording UpToDate for the last Financial calculator must complete
/// within 1 second (design §Performance, Assumption 2: "derived-value chain for Planning + Financial (without
/// routing) completes within 1 s p95").
///
/// k6 measurement of the full POST-response → SignalR hub-push chain was not feasible because k6 lacks WebSocket
/// support (see PR #77 finding). This backend integration test covers the same chain synchronously on a real
/// PostgreSQL database, measuring from AssignLegsAsync completion (the outbox entry is committed) to every
/// derived-value state row reaching UpToDate — the same signal, minus the final hub-push hop.
///
/// The chain driven here:
///   TripCalculator (Planning) → outbox → TripRateLineCalculator + OrderRateLineCalculator (Financial)
///   → outbox → TripCalculator (Planning, reads committed rate lines for allocation) → settles
///
/// Routing is disabled so the measurement reflects the non-routing budget of the design.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PlanboardChainPerformancePostgreSqlTests : OpenTmsTestBase<TransportRunPostgreSqlTestModule>
{
    private static readonly TimeSpan ChainBudget = TimeSpan.FromSeconds(1);
    private static readonly AbpUnitOfWorkOptions Transactional = new() { IsTransactional = true };

    private static readonly HashSet<string> ChainTargetTypes =
    [
        PlanningExecutionApplicationModule.TripTargetType,
        PlanningExecutionApplicationModule.LegTargetType,
        FinancialDerivedValueTypes.TripRateLines,
        FinancialDerivedValueTypes.OrderRateLines
    ];

    private static Guid TenantA => PostgreSqlTestDatabases.TransportTenantAId;

    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _uow;
    private readonly IOutboxDispatcher _dispatcher;
    private readonly RecordingBackgroundJobManager _jobs;
    private readonly DerivedValueCalculationJob _job;
    private readonly IDerivedValueStateStore _states;
    private readonly TestRoutingWorld _routing;
    private readonly OrderLegsHandler _handler;
    private readonly TripOperations _operations;
    private readonly IRepository<Leg, Guid> _legs;
    private readonly IRepository<TransportTripType, Guid> _tripTypes;

    private TransportRunFixture Run => GetRequiredService<TransportRunFixture>();

    public PlanboardChainPerformancePostgreSqlTests()
    {
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _uow = GetRequiredService<IUnitOfWorkManager>();
        _dispatcher = GetRequiredService<IOutboxDispatcher>();
        _jobs = GetRequiredService<RecordingBackgroundJobManager>();
        _job = GetRequiredService<DerivedValueCalculationJob>();
        _states = GetRequiredService<IDerivedValueStateStore>();
        _routing = GetRequiredService<TestRoutingWorld>();
        _handler = GetRequiredService<OrderLegsHandler>();
        _operations = GetRequiredService<TripOperations>();
        _legs = GetRequiredService<IRepository<Leg, Guid>>();
        _tripTypes = GetRequiredService<IRepository<TransportTripType, Guid>>();
    }

    [Fact]
    public async Task Assign_legs_full_derived_value_chain_completes_within_1_second_budget()
    {
        // Routing is disabled: the test measures the planning + financial chain only, matching
        // design 75 §Performance "Planning + Financial, no routing".
        _routing.RoutingAvailable = false;

        var world = await InTenantAsync(() => Run.BuildAsync(new TransportRunWorldOptions { UniqueNames = true }));
        var order = await InTenantAsync(() =>
        {
            // No TripRequest: the leg stays unassigned so the test can measure the assign → chain path.
            var dto = Run.CreateDto(world, confirm: true);
            return GetRequiredService<OpenTms.Orders.TransportOrders.ITransportOrderAppService>().CreateAsync(dto);
        });

        // Drive setup calculations (order-confirmation handlers and their derived-value requests).
        // These must settle before we start measuring the assign-triggered chain.
        _jobs.Clear();
        await DriveAsync();
        _jobs.Clear();

        // Retrieve the generated leg and create an empty trip.
        var legIds = await InTenantAsync(async () =>
        {
            using (AsBackgroundProcess())
            {
                await _handler.HandleEventAsync(new TransportOrderConfirmedEto { OrderId = order.Id });
            }
            await _uow.Current!.SaveChangesAsync();
            return (await _legs.GetListAsync(l => l.OrderId == order.Id)).Select(l => l.Id).ToList();
        });

        legIds.ShouldNotBeEmpty("the order must generate at least one leg");

        var tripId = await InTenantAsync(async () =>
        {
            var typeId = (await _tripTypes.GetListAsync()).OrderBy(t => t.IsDefault ? 0 : 1).First().Id;
            var trip = await _operations.CreateAsync(new TransportTripCreateDto
            {
                TransportTripTypeId = typeId,
                PlannedDeparture = new DateTimeOffset(2026, 10, 26, 6, 0, 0, TimeSpan.FromHours(1)),
                PlannedArrival = new DateTimeOffset(2026, 10, 26, 18, 0, 0, TimeSpan.FromHours(1))
            }, null, null, null);
            return trip.Id;
        });

        // Drain any setup-phase outbox entries and job requests produced by trip creation.
        _jobs.Clear();
        await DriveAsync();
        _jobs.Clear();

        // Assign the legs inside a committed transaction: this writes the DerivedValuesRequestedEto
        // entry to the outbox, which is what AssignLegsAsync triggers in production.
        await InTenantAsync(async () =>
        {
            var trip = await GetRequiredService<IRepository<TransportTrip, Guid>>().GetAsync(tripId);
            var legsToAssign = await _legs.GetListAsync(l => legIds.Contains(l.Id));
            await _operations.AssignAsync(trip, legsToAssign, allowOutsideWindow: true, afterStopId: null, moving: false);
        });

        // ── measure: from assign completion (outbox written, committed) to full-chain UpToDate ──
        var sw = Stopwatch.StartNew();
        await DriveAsync();
        sw.Stop();

        // Verify the TripCalculator state is UpToDate (the minimum chain signal).
        DerivedValueState? tripState;
        using (_currentTenant.Change(TenantA))
        using (var uow = _uow.Begin(new AbpUnitOfWorkOptions(), requiresNew: true))
        {
            tripState = await _states.FindAsync(PlanningExecutionApplicationModule.TripTargetType, tripId);
            await uow.CompleteAsync();
        }

        tripState.ShouldNotBeNull("TripCalculator must have been requested after leg assignment");
        tripState.Status.ShouldBe(DerivedValueStatus.UpToDate,
            "TripCalculator must reach UpToDate within the chain");

        // ── assert the chain fits within the 1 s budget ──
        sw.Elapsed.ShouldBeLessThan(ChainBudget,
            $"The Planning + Financial derived-value chain (without routing) took " +
            $"{sw.Elapsed.TotalMilliseconds:F0} ms, exceeding the 1 s budget from design 75 §Performance " +
            $"(Assumption 2). Chain: TripCalculator → outbox → TripRateLineCalculator / OrderRateLineCalculator " +
            $"→ outbox → TripCalculator (allocation pass). " +
            $"If this consistently exceeds 1 s, consider chaining the Financial calculators as a single Hangfire " +
            $"continuation to save one outbox round-trip (~100 ms, design 75 addendum).");
    }

    // ── infrastructure ────────────────────────────────────────────────────────

    private async Task<T> InTenantAsync<T>(Func<Task<T>> action)
    {
        using (_currentTenant.Change(TenantA))
        using (var uow = _uow.Begin(Transactional, requiresNew: true))
        {
            var result = await action();
            await uow.CompleteAsync();
            return result;
        }
    }

    private Task InTenantAsync(Func<Task> action)
        => InTenantAsync(async () => { await action(); return 0; });

    /// <summary>
    /// Drives the full derived-value chain synchronously — the same pattern as production, without a current user:
    /// dispatch the tenant's outbox, run every captured Hangfire job, repeat until quiescent.
    /// </summary>
    private async Task DriveAsync()
    {
        using var anonymous = AsBackgroundProcess();
        for (var round = 0; round < 20; round++)
        {
            var dispatched = await _dispatcher.DispatchAsync(TenantA);
            var jobs = _jobs.Drain<DerivedValueCalculationArgs>()
                .Where(j => ChainTargetTypes.Contains(j.TargetType))
                .ToList();
            foreach (var args in jobs)
            {
                await _job.ExecuteAsync(args);
            }

            if (dispatched == 0 && jobs.Count == 0)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            "The derived-value chain did not quiesce within 20 rounds. " +
            "A calculator or handler is keeping the loop alive.");
    }
}
