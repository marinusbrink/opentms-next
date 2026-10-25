using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenTms.EntityFrameworkCore.Platform.DocumentRendering;
using OpenTms.Features;
using OpenTms.Financial;
using OpenTms.Financial.Events;
using OpenTms.Financial.Tariffs;
using OpenTms.Financial.Tariffs.Calculators;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.Orders;
using OpenTms.Orders.ConsignmentNotes;
using OpenTms.Orders.Events;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Events;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.Published;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.DocumentRendering;
using OpenTms.Platform.Events.Outbox;
using OpenTms.Platform.Features;
using OpenTms.Platform.ReportDefinitions;
using OpenTms.TestDoubles;
using OpenTms.TransportRun;
using Shouldly;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.TransportRun;

/// <summary>
/// The end-to-end chain of the transport run against real PostgreSQL (design 56 "Test risk analysis", critical): one
/// confirmed order with a trip request travels through the real outbox dispatcher and the real derived-value jobs,
/// driven synchronously the way Hangfire and the worker would: Orders confirms → Planning generates the leg and the
/// requested trip → the route calculator (routing double) stores the distance → Financial prices the order from the
/// customer's list and the trip from the own-fleet cost per kilometre → Planning allocates on the leg → Orders stores
/// revenue, cost, margin and progress. Every stored value, the facts raised (and the tenant they ran in) and the
/// version guards are asserted; a second order in the shared tenant stays invisible to tenant A at every step; a changed
/// stop on the assigned leg unassigns it with the trip's warning and reprices; a CMR print stores a numbered document;
/// the preview equals the stored lines; a cancelled order loses its legs and keeps its rate lines. Rows commit in
/// tenant A's own database and the shared tenant's part of the host database; the outbox is left empty.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class TransportRunChainTests : OpenTmsTestBase<TransportRunPostgreSqlTestModule>
{
    private static readonly AbpUnitOfWorkOptions Transactional = new() { IsTransactional = true };

    private static Guid TenantA => PostgreSqlTestDatabases.TenantAId;

    private static Guid TenantB => PostgreSqlTestDatabases.SharedTenantId;

    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _uow;
    private readonly IOutboxDispatcher _dispatcher;
    private readonly RecordingBackgroundJobManager _jobs;
    private readonly DerivedValueCalculationJob _job;
    private readonly IDerivedValueStateStore _states;
    private readonly ChainEventRecorder _events;
    private readonly TestRoutingWorld _routing;

    private TransportRunFixture Run => GetRequiredService<TransportRunFixture>();

    private ITransportOrderAppService Orders => GetRequiredService<ITransportOrderAppService>();

    private ITransportTripAppService Trips => GetRequiredService<ITransportTripAppService>();

    public TransportRunChainTests()
    {
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _uow = GetRequiredService<IUnitOfWorkManager>();
        _dispatcher = GetRequiredService<IOutboxDispatcher>();
        _jobs = GetRequiredService<RecordingBackgroundJobManager>();
        _job = GetRequiredService<DerivedValueCalculationJob>();
        _states = GetRequiredService<IDerivedValueStateStore>();
        _events = GetRequiredService<ChainEventRecorder>();
        _routing = GetRequiredService<TestRoutingWorld>();
    }

    // ── driving the system ─────────────────────────────────────────────────────

    private async Task<T> InTenantAsync<T>(Guid tenantId, Func<Task<T>> action, bool transactional = true)
    {
        using (_currentTenant.Change(tenantId))
        using (var uow = _uow.Begin(transactional ? Transactional : new AbpUnitOfWorkOptions(), requiresNew: true))
        {
            var result = await action();
            await uow.CompleteAsync();
            return result;
        }
    }

    private Task InTenantAsync(Guid tenantId, Func<Task> action, bool transactional = true)
        => InTenantAsync(tenantId, async () => { await action(); return 0; }, transactional);

    /// <summary>
    /// What the outbox worker and Hangfire do in production, synchronously and to the end: dispatch the tenant's outbox
    /// (the handlers request calculations and raise facts), run every job the request handler enqueued, repeat until
    /// nothing moves. Returns the number of jobs run.
    /// </summary>
    private static readonly HashSet<string> ChainTargetTypes =
    [
        OrdersConsts.OrderSummaryTargetType, OrderRateLineCalculator.TargetType, TripRateLineCalculator.TargetType,
        PlanningExecutionApplicationModule.LegTargetType, PlanningExecutionApplicationModule.TripTargetType
    ];

    private async Task<int> DriveAsync(Guid tenantId)
    {
        var jobsRun = 0;
        for (var round = 0; round < 40; round++)
        {
            var dispatched = await _dispatcher.DispatchAsync(tenantId);
            /* Only the chain's own jobs: earlier tests of the process may have left probe jobs in the recorder. */
            var jobs = _jobs.Drain<DerivedValueCalculationArgs>().Where(j => ChainTargetTypes.Contains(j.TargetType)).ToList();
            foreach (var job in jobs)
            {
                await _job.ExecuteAsync(job);
                jobsRun++;
            }

            if (dispatched == 0 && jobs.Count == 0)
            {
                return jobsRun;
            }
        }

        throw new InvalidOperationException("The chain did not come to rest within 40 rounds: a calculator or handler keeps the loop alive.");
    }

    private Task<DerivedValueState?> StateAsync(Guid tenantId, string targetType, Guid id)
        => InTenantAsync(tenantId, () => _states.FindAsync(targetType, id), transactional: false);

    private async Task ShouldBeUpToDateAsync(Guid tenantId, string targetType, Guid id)
    {
        var state = (await StateAsync(tenantId, targetType, id)).ShouldNotBeNull($"{targetType} of {id} was never requested");
        state.Status.ShouldBe(DerivedValueStatus.UpToDate, $"{targetType} of {id}: {state.LastErrorCode}");
        state.CalculatedVersion.ShouldBe(state.RequestedVersion, $"{targetType} of {id}: the version guard — the last requested version is the calculated one");
    }

    private static Task<long> OutboxRowsAsync(string database) => PostgreSqlTestDatabases.CountOutboxRowsAsync(database, "");

    private sealed record Scenario(TransportRunWorld World, TransportOrderDto Order, Guid LineId, Guid LegId, TransportTripDto Trip, int ExpectedDistanceM);

    /// <summary>The world and a confirmed order with a trip request (own fleet, depot → shop) in the tenant, the chain driven to rest.</summary>
    private async Task<Scenario> GoldenPathAsync(Guid tenantId, string ownFleetCostPerKm)
    {
        var world = await InTenantAsync(tenantId, () => Run.BuildAsync(new TransportRunWorldOptions { UniqueNames = true }));
        await InTenantAsync(tenantId, () => GetRequiredService<ISettingManager>().SetForTenantAsync(tenantId, FinancialSettings.OwnFleetCostPerKm, ownFleetCostPerKm));

        var order = await InTenantAsync(tenantId, () =>
        {
            var dto = Run.CreateDto(world, confirm: true);
            dto.TripRequest = new TransportOrderTripRequestInputDto
            {
                TransportTripTypeId = world.TripTypeId, DepartureRelationId = world.DepotId, DepartureAddressId = world.DepotAddressId, ArrivalRelationId = world.ShopId, ArrivalAddressId = world.ShopAddressId,
                PlannedDeparture = TransportRunFixture.At(Run.Today, 6), PlannedArrival = TransportRunFixture.At(Run.Today, 18)
            };
            return Orders.CreateAsync(dto);
        });
        order.Status.ShouldBe(TransportOrderStatus.Open);

        await DriveAsync(tenantId);

        var leg = await InTenantAsync(tenantId, async () => (await Run.LegsOfAsync(order.Id)).ShouldHaveSingleItem(), transactional: false);
        var trip = await InTenantAsync(tenantId, async () => await Trips.GetAsync(leg.TripId.ShouldNotBeNull("the requested trip holds the leg")), transactional: false);
        var expectedDistance = (int)Math.Round(TestRoutingWorld.GreatCircleM(
            new(TransportRunFixture.Venlo.Lat, TransportRunFixture.Venlo.Lon), new(TransportRunFixture.Utrecht.Lat, TransportRunFixture.Utrecht.Lon)) * _routing.RoadFactor);
        return new Scenario(world, order, order.Lines.Single().Id!.Value, leg.Id, trip, expectedDistance);
    }

    // ── the chain ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_confirmed_order_travels_through_every_module_and_every_stored_value_fact_and_version_guard_is_in_place()
    {
        _events.Clear();
        _jobs.Clear();
        _routing.RoutingAvailable = true;
        _routing.RoutingThrows = false;
        var s = await GoldenPathAsync(TenantA, "1.00");
        var orderId = s.Order.Id;
        var expectedKm = RateLine.Round4(s.ExpectedDistanceM / 1000m);
        var expectedCost = RateLine.Round2(expectedKm * 1.00m);
        var expectedRevenue = 2m * TransportRunFixture.RevenueUnitPrice;

        /* Planning: the leg and the trip of the request, routed through the double. */
        var leg = await InTenantAsync(TenantA, () => GetRequiredService<IRepository<Leg, Guid>>().GetAsync(s.LegId), transactional: false);
        leg.Source.ShouldBe(LegSource.Generated);
        (leg.OriginRelationId, leg.DestinationRelationId).ShouldBe((s.World.DepotId, s.World.ShopId));
        leg.DistanceM.ShouldBe(s.ExpectedDistanceM, "the route calculator stored the double's distance");
        leg.RouteProvider.ShouldBe("Test");
        leg.AllocatedRevenueAmount.ShouldBe(expectedRevenue, "AD-20: the order's revenue on its one leg");
        leg.AllocatedCostAmount.ShouldBe(expectedCost, "AD-20: the trip's cost on its one leg");
        leg.EstimatedCo2Kg.ShouldNotBeNull();

        var trip = s.Trip;
        trip.CreatedFromOrderId.ShouldBe(orderId, "Tevens trip (Q9)");
        trip.Execution.ShouldBe(TripExecution.OwnFleet);
        trip.Status.ShouldBe(TransportTripStatus.Planned);
        trip.Stops.Count.ShouldBe(2);
        trip.Stops[0].PlannedArrival.ShouldBe(trip.PlannedDeparture);
        trip.Stops[1].PlannedArrival.ShouldNotBeNull();
        trip.PlannedDistanceM.ShouldBe(s.ExpectedDistanceM);
        trip.CostAmount.ShouldBe(expectedCost, "the own-fleet line of Financial");
        trip.RevenueAmount.ShouldBe(expectedRevenue);
        trip.Warnings.ShouldNotContain(w => w.Code == TripWarningCode.NoCostPriceSource);
        trip.Warnings.ShouldNotContain(w => w.Code == TripWarningCode.RoutingNotConfigured);
        trip.CalculatedAt.ShouldNotBeNull();
        (await InTenantAsync(TenantA, () => Orders.GetAsync(orderId), transactional: false)).TripRequest.ShouldNotBeNull().TripId.ShouldBe(trip.Id, "Orders recorded the created trip");

        /* Financial: the revenue line from the customer's list, the own-fleet cost line of the trip. */
        var rateLines = GetRequiredService<IRateLineAppService>();
        var revenue = await InTenantAsync(TenantA, () => rateLines.GetAsync(new RateLineGetRequestDto { OrderId = orderId }), transactional: false);
        var priceLine = revenue.Lines.ShouldHaveSingleItem();
        (priceLine.Kind, priceLine.Quantity, priceLine.UnitPrice, priceLine.Amount, priceLine.PriceListId, priceLine.OrderLineId).ShouldBe((RateLineKind.PriceList, 2m, TransportRunFixture.RevenueUnitPrice, expectedRevenue, s.World.PriceListId, s.LineId));
        revenue.Totals.Total.ShouldBe(expectedRevenue);
        var cost = await InTenantAsync(TenantA, () => rateLines.GetAsync(new RateLineGetRequestDto { TripId = trip.Id }), transactional: false);
        var ownFleet = cost.Lines.ShouldHaveSingleItem();
        (ownFleet.Kind, ownFleet.Quantity, ownFleet.UnitPrice, ownFleet.Amount).ShouldBe((RateLineKind.OwnFleetDistance, expectedKm, 1.0000m, expectedCost));

        /* Orders: the stored summary. */
        var summary = await InTenantAsync(TenantA, () => Orders.GetSummaryAsync(orderId), transactional: false);
        summary.RevenueAmount.ShouldBe(expectedRevenue);
        summary.CostOfRevenueAmount.ShouldBe(expectedCost);
        summary.MarginAmount.ShouldBe(expectedRevenue - expectedCost);
        summary.ExecutionProgress.ShouldBe(ExecutionProgress.Planned);
        summary.TripNumbers.ShouldBe(trip.Number);
        summary.LineCount.ShouldBe(1);
        summary.StopCount.ShouldBe(2);
        (summary.FirstStopCity, summary.LastStopCity).ShouldBe(("Venlo", "Utrecht"));
        summary.Co2Kg.ShouldBe(leg.EstimatedCo2Kg);
        var detail = await InTenantAsync(TenantA, () => Orders.GetAsync(orderId), transactional: false);
        detail.Lines.Single().DistanceM.ShouldBe(s.ExpectedDistanceM);

        /* The version guards: every derived value of the chain is up to date for the last version requested. */
        await ShouldBeUpToDateAsync(TenantA, OrdersConsts.OrderSummaryTargetType, orderId);
        await ShouldBeUpToDateAsync(TenantA, OrderRateLineCalculator.TargetType, orderId);
        await ShouldBeUpToDateAsync(TenantA, TripRateLineCalculator.TargetType, trip.Id);
        await ShouldBeUpToDateAsync(TenantA, PlanningExecutionApplicationModule.LegTargetType, s.LegId);
        await ShouldBeUpToDateAsync(TenantA, PlanningExecutionApplicationModule.TripTargetType, trip.Id);

        /* The facts, each handled in tenant A's context; the outbox is empty. */
        _events.Of<TransportOrderConfirmedEto>(TenantA).ShouldContain(e => e.OrderId == orderId);
        _events.Of<LegsGeneratedEto>(TenantA).ShouldContain(e => e.OrderId == orderId && e.LegIds.Contains(s.LegId));
        _events.Of<TransportTripCreatedEto>(TenantA).ShouldContain(e => e.TripId == trip.Id && e.CreatedFromOrderId == orderId);
        _events.Of<LegAssignedEto>(TenantA).ShouldContain(e => e.LegId == s.LegId && e.TripId == trip.Id);
        _events.Of<RateLinesChangedEto>(TenantA).ShouldContain(e => e.OrderId == orderId);
        _events.Of<RateLinesChangedEto>(TenantA).ShouldContain(e => e.TripId == trip.Id);
        _events.Of<TransportTripRecalculatedEto>(TenantA).ShouldContain(e => e.TripId == trip.Id && e.OrderIds.Contains(orderId));
        _events.Of<DerivedValuesRecalculatedEto>(TenantA).Select(e => e.TargetType).Distinct().ShouldBe(
            [OrdersConsts.OrderSummaryTargetType, OrderRateLineCalculator.TargetType, TripRateLineCalculator.TargetType, PlanningExecutionApplicationModule.LegTargetType, PlanningExecutionApplicationModule.TripTargetType], ignoreOrder: true);
        _events.All.ShouldAllBe(e => e.TenantId == TenantA, "every handler ran in the raising tenant's context");
        (await OutboxRowsAsync(PostgreSqlTestDatabases.TenantADatabase)).ShouldBe(0);
        (await OutboxRowsAsync(PostgreSqlTestDatabases.HostDatabase)).ShouldBe(0);

        /* The preview path (design 56, Assumption 3): Financial's pure preview over the stored order equals the stored lines. */
        var preview = await InTenantAsync(TenantA, () => Orders.PreviewPricesAsync(orderId, new PricePreviewRequestDto()), transactional: false);
        preview.TotalAmount.ShouldBe(revenue.Totals.Total);
        preview.Lines.Select(l => (l.Kind, l.Quantity, l.UnitPrice, l.Amount)).ShouldBe(revenue.Lines.Select(l => (l.Kind, l.Quantity, l.UnitPrice, l.Amount)));

        /* A second order in the shared tenant (host database) runs its own chain; tenant A sees none of it, and it sees none of A's. */
        _events.Clear();
        var b = await GoldenPathAsync(TenantB, "2.00");
        (await InTenantAsync(TenantB, () => Orders.GetSummaryAsync(b.Order.Id), transactional: false)).RevenueAmount.ShouldBe(expectedRevenue);
        _events.All.ShouldAllBe(e => e.TenantId == TenantB);
        await InTenantAsync(TenantA, async () =>
        {
            await Should.ThrowAsync<EntityNotFoundException>(() => Orders.GetAsync(b.Order.Id));
            (await GetRequiredService<ILegReader>().GetByOrderAsync(b.Order.Id)).ShouldBeEmpty();
            (await GetRequiredService<ITransportTripLookup>().GetAsync([b.Trip.Id])).ShouldBeEmpty();
            (await GetRequiredService<IRateLineReader>().GetOrderTotalsAsync(b.Order.Id)).CalculationVersion.ShouldBeNull();
            (await GetRequiredService<IRepository<Leg, Guid>>().GetListAsync(l => l.OrderId == b.Order.Id)).ShouldBeEmpty();
            (await _states.FindAsync(OrdersConsts.OrderSummaryTargetType, b.Order.Id)).ShouldBeNull();
        }, transactional: false);
        await InTenantAsync(TenantB, async () =>
        {
            await Should.ThrowAsync<EntityNotFoundException>(() => Orders.GetAsync(orderId));
            (await GetRequiredService<ILegReader>().GetByOrderAsync(orderId)).ShouldBeEmpty();
            (await GetRequiredService<IRateLineReader>().GetOrderTotalsAsync(orderId)).CalculationVersion.ShouldBeNull();
        }, transactional: false);
        (await OutboxRowsAsync(PostgreSqlTestDatabases.TenantADatabase)).ShouldBe(0);
        (await OutboxRowsAsync(PostgreSqlTestDatabases.HostDatabase)).ShouldBe(0);

        /* A CMR print (design 55's fake provider) stores one numbered document for the order's line, on its trip. */
        List<ConsignmentNoteDocumentDto> documents;
        using (TestFeatureValueProvider.Override(PlatformFeatures.DocumentRendering, "true"))
        {
            await InTenantAsync(TenantA, () => GetRequiredService<IReportDefinitionAppService>().CreateAsync(new ReportDefinitionCreateDto
            {
                Name = "CMR nl " + s.World.Suffix, Purpose = ReportPurpose.ConsignmentNote, Culture = "nl", IsDefault = true, Provider = ReportRenderingProvider.PdfGeneratorApi,
                ProviderTemplateId = "tpl-1", FileNamePattern = "cmr-{{ number }}.pdf"
            }));
            documents = await InTenantAsync(TenantA, () => GetRequiredService<IConsignmentNoteAppService>().PrintAsync(new PrintConsignmentNoteDto { OrderLineIds = [s.LineId] }));
        }

        var document = documents.ShouldHaveSingleItem();
        document.Number.ShouldMatch(@"^CMR-\d{4}-\d{6}$");
        document.OrderId.ShouldBe(orderId);
        document.LineIds.ShouldBe([s.LineId]);
        document.TripId.ShouldBe(trip.Id, "the trip of the line's leg");
        await InTenantAsync(TenantA, async () =>
        {
            (await GetRequiredService<IConsignmentNoteAppService>().GetDocumentListAsync(new ConsignmentNoteDocumentListRequestDto { OrderId = orderId })).TotalCount.ShouldBe(1);
            (await GetRequiredService<IConsignmentNoteAppService>().GetDocumentAsync(document.Id)).Number.ShouldBe(document.Number);
        }, transactional: false);

        /* A changed stop on the assigned leg: the leg leaves the trip with the warning, the order is unplanned again and repriced on the new lane. */
        _events.Clear();
        await InTenantAsync(TenantA, () => GetRequiredService<IPriceListAppService>().CreateRuleAsync(s.World.PriceListId, new PriceListRuleInputDto
        {
            FromRelationId = s.World.DepotId, ToRelationId = s.World.CustomerId, Brackets = [new PriceListBracketInputDto { UpperBound = null, UnitPrices = [12m] }]
        }));
        await InTenantAsync(TenantA, async () =>
        {
            var current = await Orders.GetAsync(orderId);
            var line = current.Lines.Single();
            var input = new TransportOrderLineInputDto
            {
                Id = line.Id, TransportUnitId = line.TransportUnitId, ProductId = line.ProductId, Quantity = line.Quantity, WeightKg = line.WeightKg,
                Stops = line.Stops.Select(st => new TransportOrderStopInputDto
                {
                    Id = st.Id, WaypointTypeId = st.WaypointTypeId, RelationId = st.RelationId, RelationAddressId = st.RelationAddressId, Date = st.Date, TimeSlotId = st.TimeSlotId,
                    Reference = st.Reference, PlannedDurationMin = st.PlannedDurationMin
                }).ToList()
            };
            input.Stops[1].RelationId = s.World.CustomerId;
            input.Stops[1].RelationAddressId = s.World.CustomerAddressId;
            await Orders.UpdateLineAsync(orderId, line.Id!.Value, input);
        });
        await DriveAsync(TenantA);

        var moved = await InTenantAsync(TenantA, async () => (await Run.LegsOfAsync(orderId)).ShouldHaveSingleItem(), transactional: false);
        moved.Id.ShouldBe(s.LegId, "the stop kept its id, so the leg kept its key");
        moved.TripId.ShouldBeNull("the changed leg left the trip");
        moved.DestinationRelationId.ShouldBe(s.World.CustomerId);
        moved.DestinationCity.ShouldBe("Barendrecht");
        var warned = await InTenantAsync(TenantA, () => Trips.GetAsync(trip.Id), transactional: false);
        warned.Stops.ShouldBeEmpty();
        warned.Warnings.ShouldContain(w => w.Code == TripWarningCode.LegChangedAfterPlanning && w.LegId == s.LegId);
        _events.Of<LegUnassignedEto>(TenantA).ShouldContain(e => e.LegId == s.LegId && e.TripId == trip.Id);
        var repriced = await InTenantAsync(TenantA, () => rateLines.GetAsync(new RateLineGetRequestDto { OrderId = orderId }), transactional: false);
        repriced.Lines.ShouldHaveSingleItem().UnitPrice.ShouldBe(12m, "the new lane depot → customer");
        var unplanned = await InTenantAsync(TenantA, () => Orders.GetSummaryAsync(orderId), transactional: false);
        unplanned.ExecutionProgress.ShouldBe(ExecutionProgress.Unplanned);
        unplanned.RevenueAmount.ShouldBe(24m);
        unplanned.TripNumbers.ShouldBeNull();
        await ShouldBeUpToDateAsync(TenantA, OrdersConsts.OrderSummaryTargetType, orderId);
        await ShouldBeUpToDateAsync(TenantA, OrderRateLineCalculator.TargetType, orderId);
        await ShouldBeUpToDateAsync(TenantA, PlanningExecutionApplicationModule.TripTargetType, trip.Id);

        /* A cancelled order loses its legs and keeps its rate lines (money history, design 57 §4.4). */
        await InTenantAsync(TenantA, () => Orders.CancelAsync(orderId));
        await DriveAsync(TenantA);
        (await InTenantAsync(TenantA, () => Run.LegsOfAsync(orderId), transactional: false)).ShouldBeEmpty();
        (await InTenantAsync(TenantA, () => rateLines.GetAsync(new RateLineGetRequestDto { OrderId = orderId }), transactional: false)).Lines.ShouldHaveSingleItem();
        (await InTenantAsync(TenantA, () => Orders.GetAsync(orderId), transactional: false)).Status.ShouldBe(TransportOrderStatus.Cancelled);
        _events.Of<TransportOrderCancelledEto>(TenantA).ShouldContain(e => e.OrderId == orderId);
        (await OutboxRowsAsync(PostgreSqlTestDatabases.TenantADatabase)).ShouldBe(0);

        /* A second print draws the next number: the sequence is one per tenant (design 56 "Numbers"). */
        var other = await InTenantAsync(TenantA, () => Orders.CreateAsync(Run.CreateDto(s.World, confirm: true)));
        List<ConsignmentNoteDocumentDto> again;
        using (TestFeatureValueProvider.Override(PlatformFeatures.DocumentRendering, "true"))
        {
            again = await InTenantAsync(TenantA, () => GetRequiredService<IConsignmentNoteAppService>().PrintAsync(new PrintConsignmentNoteDto { OrderLineIds = [other.Lines.Single().Id!.Value] }));
        }

        var first = long.Parse(Regex.Match(document.Number, @"\d{6}$").Value);
        long.Parse(Regex.Match(again.Single().Number, @"\d{6}$").Value).ShouldBe(first + 1);
        await DriveAsync(TenantA);
        (await OutboxRowsAsync(PostgreSqlTestDatabases.TenantADatabase)).ShouldBe(0);
    }
}

/// <summary>Records every fact of the run that was dispatched, with the tenant the handler ran in.</summary>
public class ChainEventRecorder : ISingletonDependency
{
    private readonly ConcurrentQueue<(Guid? TenantId, object Event)> _events = new();

    public IReadOnlyList<(Guid? TenantId, object Event)> All => _events.ToList();

    public IReadOnlyList<T> Of<T>(Guid tenantId) => _events.Where(e => e.TenantId == tenantId).Select(e => e.Event).OfType<T>().ToList();

    public void Record(Guid? tenantId, object e) => _events.Enqueue((tenantId, e));

    public void Clear() => _events.Clear();
}

public class ChainEventRecordingHandler :
    IDistributedEventHandler<TransportOrderConfirmedEto>, IDistributedEventHandler<TransportOrderCancelledEto>,
    IDistributedEventHandler<LegsGeneratedEto>, IDistributedEventHandler<LegAssignedEto>, IDistributedEventHandler<LegUnassignedEto>,
    IDistributedEventHandler<TransportTripCreatedEto>, IDistributedEventHandler<TransportTripRecalculatedEto>,
    IDistributedEventHandler<RateLinesChangedEto>, IDistributedEventHandler<DerivedValuesRecalculatedEto>,
    ITransientDependency
{
    private readonly ChainEventRecorder _recorder;
    private readonly ICurrentTenant _currentTenant;

    public ChainEventRecordingHandler(ChainEventRecorder recorder, ICurrentTenant currentTenant)
    {
        _recorder = recorder;
        _currentTenant = currentTenant;
    }

    public Task HandleEventAsync(TransportOrderConfirmedEto e) => Record(e);
    public Task HandleEventAsync(TransportOrderCancelledEto e) => Record(e);
    public Task HandleEventAsync(LegsGeneratedEto e) => Record(e);
    public Task HandleEventAsync(LegAssignedEto e) => Record(e);
    public Task HandleEventAsync(LegUnassignedEto e) => Record(e);
    public Task HandleEventAsync(TransportTripCreatedEto e) => Record(e);
    public Task HandleEventAsync(TransportTripRecalculatedEto e) => Record(e);
    public Task HandleEventAsync(RateLinesChangedEto e) => Record(e);
    public Task HandleEventAsync(DerivedValuesRecalculatedEto e) => Record(e);

    private Task Record(object e)
    {
        _recorder.Record(_currentTenant.Id, e);
        return Task.CompletedTask;
    }
}
