using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Tariffs;
using OpenTms.Financial.Tariffs.Calculators;
using OpenTms.Financial.Tariffs.Handlers;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Relations;
using OpenTms.Orders.Events;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.Events;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.Events.Outbox;
using OpenTms.PostgreSql.TransportRun;
using OpenTms.TransportRun;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.Financial;

/// <summary>
/// The two Financial calculators through the real derived-value job against PostgreSQL (design 57, design 49): the order's
/// non-manual revenue lines are replaced, manual and adjusted lines kept and re-valued, the event published once per
/// change, a superseded version discarded, a repeated job and an unchanged recalculation one effect; the trip's cost lines
/// per segment, the agreed price written once by the handler and left alone, the own-fleet line from the vehicles' cost
/// price else the tenant setting (scenario 62, QP10); a job never writes in another tenant. The orders, legs and trips are
/// real rows of Orders and Planning (created through their services and Planning's handler, in tenant A's database); the
/// facts they raise are drained from the outbox at the end of each test. Risk class: Critical.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class RateLineCalculatorPostgreSqlTests : OpenTmsTestBase<TransportRunPostgreSqlTestModule>
{
    private static readonly AbpUnitOfWorkOptions Transactional = new() { IsTransactional = true };
    private static readonly DateOnly Monday = new(2026, 10, 26);

    private readonly IDerivedValueScheduler _scheduler;
    private readonly IDerivedValueStateStore _states;
    private readonly DerivedValueCalculationJob _job;
    private readonly IUnitOfWorkManager _uow;
    private readonly ICurrentTenant _currentTenant;
    private readonly IRepository<RateLine, Guid> _lines;
    private readonly IRepository<PriceList, Guid> _lists;
    private readonly IRepository<PriceListRule, Guid> _rules;
    private readonly IGuidGenerator _guids;
    private readonly IOutboxDispatcher _dispatcher;

    /// <summary>The rows commit: the committing tests' own tenant with its own database, never tenant A.</summary>
    private static Guid TenantA => PostgreSqlTestDatabases.TransportTenantAId;

    private TransportRunFixture Run => GetRequiredService<TransportRunFixture>();

    private ITransportOrderAppService Orders => GetRequiredService<ITransportOrderAppService>();

    public RateLineCalculatorPostgreSqlTests()
    {
        _scheduler = GetRequiredService<IDerivedValueScheduler>();
        _states = GetRequiredService<IDerivedValueStateStore>();
        _job = GetRequiredService<DerivedValueCalculationJob>();
        _uow = GetRequiredService<IUnitOfWorkManager>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _lines = GetRequiredService<IRepository<RateLine, Guid>>();
        _lists = GetRequiredService<IRepository<PriceList, Guid>>();
        _rules = GetRequiredService<IRepository<PriceListRule, Guid>>();
        _guids = GetRequiredService<IGuidGenerator>();
        _dispatcher = GetRequiredService<IOutboxDispatcher>();
    }

    // ── arrangement ────────────────────────────────────────────────────────────

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

    /// <summary>The real places and master data of one scenario in tenant A: three relations (the lanes), the seeded types, a pallet unit.</summary>
    private sealed record Scenario(Place Ams, Place Depot, Place Bru, Place Carrier, Guid Pallet, Guid Load, Guid Unload, Guid OrderTypeId);

    private Task<Scenario> ScenarioAsync() => InTenantAsync(TenantA, async () =>
    {
        var suffix = TransportRunFixture.NewSuffix();
        var ams = await Run.PlaceAsync("AMS" + suffix, "Amsterdam " + suffix, RelationRoles.Customer, 52.3676, 4.9041, "Amsterdam");
        var depot = await Run.PlaceAsync("DEP" + suffix, "Depot " + suffix, RelationRoles.Depot, 52.0907, 5.1214, "Utrecht");
        var bru = await Run.PlaceAsync("BRU" + suffix, "Brussels " + suffix, RelationRoles.Customer, 50.8503, 4.3517, "Brussels");
        var carrier = await Run.PlaceAsync("CAR" + suffix, "Carrier " + suffix, RelationRoles.Carrier, 51.9244, 4.4777, "Rotterdam");
        var load = await Run.WaypointTypeAsync("LOAD", "Load", "Laden", WaypointActivity.Load, 30);
        var unload = await Run.WaypointTypeAsync("UNLOAD", "Unload", "Lossen", WaypointActivity.Unload, 30);
        var pallet = await Run.TransportUnitAsync("Pallet");
        var orderType = await Run.OrderTypeAsync(TransportOrderRequiredFields.None, suffix, load, unload);
        return new Scenario(ams, depot, bru, carrier, pallet, load, unload, orderType);
    });

    /// <summary>A list with one lane rule from <paramref name="from"/> to <paramref name="to"/> at the given unit price.</summary>
    private async Task<Guid> ListAsync(Guid tenantId, RateSide side, Guid from, Guid to, decimal unitPrice, QuantitySource basis = QuantitySource.LineQuantity)
    {
        var list = new PriceList(_guids.Create(), tenantId, "L" + Guid.NewGuid().ToString("N")[..8], "List", side, "EUR", basis, new DateOnly(2026, 1, 1));
        await _lists.InsertAsync(list, autoSave: true);
        var rule = new PriceListRule(_guids.Create(), tenantId, list.Id, 1);
        rule.SetLane(from, null, to, null);
        rule.SetBrackets([(null, new List<decimal?> { unitPrice })], 1, _guids.Create);
        await _rules.InsertAsync(rule, autoSave: true);
        return list.Id;
    }

    /// <summary>A confirmed order of one line through the given places (the first and the last define the lane) with the given lists.</summary>
    private Task<TransportOrderDto> OrderAsync(Scenario s, decimal quantity, Guid? listId, params Place[] stops) => InTenantAsync(TenantA, () => Orders.CreateAsync(new TransportOrderCreateDto
    {
        TransportOrderTypeId = s.OrderTypeId,
        CustomerRelationId = s.Ams.RelationId,
        OrderDate = Monday,
        FinancialTerms = new TransportOrderFinancialTermsInputDto { CurrencyCode = "EUR", PriceListIds = listId == null ? [] : [listId.Value] },
        Lines =
        [
            new TransportOrderLineInputDto
            {
                TransportUnitId = s.Pallet, Quantity = quantity,
                Stops = stops.Select((p, i) => TransportRunFixture.Stop(i == 0 ? s.Load : s.Unload, p.RelationId, p.AddressId, Monday)).ToList()
            }
        ],
        Confirm = true
    }));

    private Task SetQuantityAsync(TransportOrderDto order, decimal quantity) => InTenantAsync(TenantA, async () =>
    {
        var current = await Orders.GetAsync(order.Id);
        var line = current.Lines.Single();
        await Orders.UpdateLineAsync(order.Id, line.Id!.Value, new TransportOrderLineInputDto
        {
            Id = line.Id, TransportUnitId = line.TransportUnitId, Quantity = quantity,
            Stops = line.Stops.Select(st => new TransportOrderStopInputDto { Id = st.Id, WaypointTypeId = st.WaypointTypeId, RelationId = st.RelationId, RelationAddressId = st.RelationAddressId, Date = st.Date, PlannedDurationMin = st.PlannedDurationMin }).ToList()
        });
    });

    /// <summary>Planning's legs of the order, generated by its handler (the outbox is drained at the end of the test).</summary>
    private Task<List<Leg>> LegsAsync(Guid orderId) => InTenantAsync(TenantA, async () =>
    {
        using (AsBackgroundProcess())
        {
            await GetRequiredService<OrderLegsHandler>().HandleEventAsync(new TransportOrderConfirmedEto { OrderId = orderId });
        }

        await _uow.Current!.SaveChangesAsync();
        return (await GetRequiredService<IRepository<Leg, Guid>>().GetListAsync(l => l.OrderId == orderId)).OrderBy(l => l.Position).ToList();
    });

    /// <summary>A trip (own fleet, or the carrier's when given) with the legs assigned; the legs' distances are stored as the route calculator would.</summary>
    private Task<TransportTrip> TripAsync(Guid? carrierRelationId, IReadOnlyList<(Leg Leg, int DistanceM)> legs, params Guid[] vehicleIds) => InTenantAsync(TenantA, async () =>
    {
        var operations = GetRequiredService<TripOperations>();
        var typeId = (await GetRequiredService<IRepository<TransportTripType, Guid>>().GetListAsync()).OrderBy(t => t.IsDefault ? 0 : 1).First().Id;
        var trip = await operations.CreateAsync(new TransportTripCreateDto
        {
            TransportTripTypeId = typeId, TripDate = Monday, PlannedDeparture = new DateTimeOffset(2026, 10, 26, 6, 0, 0, TimeSpan.FromHours(1)),
            PlannedArrival = new DateTimeOffset(2026, 10, 26, 18, 0, 0, TimeSpan.FromHours(1)),
            CarrierRelationId = carrierRelationId, Execution = carrierRelationId == null ? TripExecution.OwnFleet : TripExecution.Subcontracted, VehicleIds = vehicleIds.ToList()
        }, null, null, null);
        await operations.AssignAsync(trip, legs.Select(l => l.Leg).ToList(), allowOutsideWindow: true, afterStopId: null, moving: false);
        foreach (var (leg, distanceM) in legs)
        {
            await Run.SetLegValuesAsync(leg.Id, distanceM);
        }

        return trip;
    });

    private Task<long> RequestOrderAsync(Guid orderId) => InTenantAsync(TenantA, () => _scheduler.RequestAsync(OrderRateLineCalculator.TargetType, orderId, "Test"));

    private Task<long> RequestTripAsync(Guid tripId) => InTenantAsync(TenantA, () => _scheduler.RequestAsync(TripRateLineCalculator.TargetType, tripId, "Test"));

    /* The job and the dispatcher run as in production: without a current user (design 56 build notes). */
    private async Task RunAsync(string targetType, Guid targetId, long version, Guid? tenantId = null)
    {
        using (AsBackgroundProcess())
        {
            await _job.ExecuteAsync(new DerivedValueCalculationArgs { TenantId = tenantId ?? TenantA, TargetType = targetType, TargetId = targetId, Version = version });
        }
    }

    private Task<List<RateLine>> OrderLinesAsync(Guid orderId, Guid? tenantId = null)
        => InTenantAsync(tenantId ?? TenantA, () => _lines.GetListAsync(l => l.AnchorOrderId == orderId), transactional: false);

    private Task<List<RateLine>> TripLinesAsync(Guid tripId)
        => InTenantAsync(TenantA, () => _lines.GetListAsync(l => l.AnchorTripId == tripId), transactional: false);

    private Task<DerivedValueState?> StateAsync(string targetType, Guid id)
        => InTenantAsync(TenantA, () => _states.FindAsync(targetType, id), transactional: false);

    private static Task<long> OutboxRowsAsync(Guid id) => PostgreSqlTestDatabases.CountOutboxRowsAsync(PostgreSqlTestDatabases.TransportTenantADatabase, id.ToString());

    private async Task DrainOutboxAsync()
    {
        using var anonymous = AsBackgroundProcess();
        while (await _dispatcher.DispatchAsync(TenantA) > 0)
        {
        }
    }

    // ── the order calculator ───────────────────────────────────────────────────

    [Fact]
    public async Task The_order_calculator_replaces_calculated_lines_keeps_manual_and_adjusted_ones_and_publishes_once_per_change()
    {
        var s = await ScenarioAsync();
        var listId = await InTenantAsync(TenantA, () => ListAsync(TenantA, RateSide.Revenue, s.Ams.RelationId, s.Bru.RelationId, 10m));
        var order = await OrderAsync(s, quantity: 4m, listId, s.Ams, s.Bru);
        var orderId = order.Id;
        var lineId = order.Lines.Single().Id!.Value;
        await InTenantAsync(TenantA, async () =>
        {
            var manual = RateLine.Manual(_guids.Create(), TenantA, RateSide.Revenue, RateLineAnchor.OrderLine(orderId, lineId), RateLineKind.Manual, "EUR", "Hand", 1m, QuantitySource.LineQuantity, 2.5m, null);
            var stale = RateLine.Calculated(_guids.Create(), TenantA, RateSide.Revenue, RateLineAnchor.OrderLine(orderId, lineId), RateLineKind.PriceList, "EUR", PriceResolver_Key(), 1m, QuantitySource.LineQuantity, 99m, 99m);
            await _lines.InsertManyAsync([manual, stale]);
        });

        /* The confirm requested the lines already (Financial's handlers run on dispatch; here the test requests itself). */
        var v1 = await RequestOrderAsync(orderId);
        await RunAsync(OrderRateLineCalculator.TargetType, orderId, v1);

        var lines = await OrderLinesAsync(orderId);
        lines.Count.ShouldBe(2);
        var calculated = lines.Single(l => l.Kind == RateLineKind.PriceList);
        (calculated.Quantity, calculated.UnitPrice, calculated.Amount, calculated.PriceListId, calculated.CalculationVersion).ShouldBe((4m, 10m, 40m, listId, v1));
        calculated.OrderLineId.ShouldBe(lineId);
        calculated.AnchorOrderId.ShouldBe(orderId);
        var manualKept = lines.Single(l => l.IsManual);
        (manualKept.Quantity, manualKept.Amount, manualKept.CalculationVersion).ShouldBe((4m, 10m, v1), "re-valued from the current line quantity (QP6)");
        lines.ShouldNotContain(l => l.UnitPrice == 99m, "the stale calculated line was replaced");
        (await StateAsync(OrderRateLineCalculator.TargetType, orderId))!.Status.ShouldBe(DerivedValueStatus.UpToDate);
        (await InTenantAsync(TenantA, () => GetRequiredService<IRateLineReader>().GetOrderTotalsAsync(orderId), transactional: false)).TotalAmount.ShouldBe(50m);

        /* Requested + recalculated + RateLinesChanged, on top of the confirm's own facts and request. */
        var afterFirst = await OutboxRowsAsync(orderId);

        /* A repeated job for the same version is one effect. */
        await RunAsync(OrderRateLineCalculator.TargetType, orderId, v1);
        (await OrderLinesAsync(orderId)).Select(l => l.Id).OrderBy(id => id).ShouldBe(lines.Select(l => l.Id).OrderBy(id => id));
        (await OutboxRowsAsync(orderId)).ShouldBe(afterFirst);

        /* Unchanged facts: a new version writes nothing and publishes no RateLinesChanged (requested + recalculated only). */
        var v2 = await RequestOrderAsync(orderId);
        await RunAsync(OrderRateLineCalculator.TargetType, orderId, v2);
        (await OrderLinesAsync(orderId)).Single(l => l.Kind == RateLineKind.PriceList).Id.ShouldBe(calculated.Id, "the same line row stays");
        (await OutboxRowsAsync(orderId)).ShouldBe(afterFirst + 2);

        /* A changed fact (the quantity, saved through Orders): the lines are replaced and the event travels. The user adjusted the unit price meanwhile: it is kept. */
        await InTenantAsync(TenantA, async () =>
        {
            var line = await _lines.GetAsync(calculated.Id);
            line.AdjustUnitPrice(9m);
            await _lines.UpdateAsync(line);
        });
        await SetQuantityAsync(order, 6m);
        var beforeChange = await OutboxRowsAsync(orderId);
        var v3 = await RequestOrderAsync(orderId);
        await RunAsync(OrderRateLineCalculator.TargetType, orderId, v3);
        var adjusted = (await OrderLinesAsync(orderId)).Single(l => l.Kind == RateLineKind.PriceList);
        (adjusted.Id, adjusted.IsAdjusted, adjusted.Quantity, adjusted.UnitPrice, adjusted.Amount).ShouldBe((calculated.Id, true, 6m, 9m, 54m));
        (await OutboxRowsAsync(orderId)).ShouldBe(beforeChange + 3, "requested, recalculated and RateLinesChanged");

        await DrainOutboxAsync();
    }

    private static string PriceResolver_Key() => OpenTms.Financial.Tariffs.Pricing.PriceResolver.PriceListDescriptionKey;

    [Fact]
    public async Task A_superseded_version_is_discarded_and_the_job_writes_only_in_the_tenant_of_its_arguments()
    {
        var s = await ScenarioAsync();
        var listId = await InTenantAsync(TenantA, () => ListAsync(TenantA, RateSide.Revenue, s.Ams.RelationId, s.Bru.RelationId, 10m));
        var orderId = (await OrderAsync(s, quantity: 1m, listId, s.Ams, s.Bru)).Id;

        var v1 = await RequestOrderAsync(orderId);
        var v2 = await RequestOrderAsync(orderId);
        await RunAsync(OrderRateLineCalculator.TargetType, orderId, v1);
        (await OrderLinesAsync(orderId)).ShouldBeEmpty("v1 is superseded by v2 before it ran");
        (await StateAsync(OrderRateLineCalculator.TargetType, orderId))!.CalculatedVersion.ShouldBe(0);

        /* The shared tenant has no state for this order: the job finds nothing to do there and writes nothing. */
        await RunAsync(OrderRateLineCalculator.TargetType, orderId, v2, PostgreSqlTestDatabases.SharedTenantId);
        (await OrderLinesAsync(orderId, PostgreSqlTestDatabases.SharedTenantId)).ShouldBeEmpty();
        (await OrderLinesAsync(orderId)).ShouldBeEmpty();

        await RunAsync(OrderRateLineCalculator.TargetType, orderId, v2);
        (await OrderLinesAsync(orderId)).ShouldHaveSingleItem().CalculationVersion.ShouldBe(v2);
        (await OrderLinesAsync(orderId, PostgreSqlTestDatabases.SharedTenantId)).ShouldBeEmpty();

        await DrainOutboxAsync();
    }

    [Fact]
    public async Task The_order_handlers_request_a_new_version_and_a_repeated_delivery_is_one_effect()
    {
        var s = await ScenarioAsync();
        var listId = await InTenantAsync(TenantA, () => ListAsync(TenantA, RateSide.Revenue, s.Ams.RelationId, s.Bru.RelationId, 10m));
        var order = await OrderAsync(s, quantity: 2m, listId, s.Ams, s.Bru);
        var orderId = order.Id;
        var lineId = order.Lines.Single().Id!.Value;

        await InTenantAsync(TenantA, () => GetRequiredService<TransportOrderLinesChangedHandler>().HandleEventAsync(new TransportOrderLinesChangedEto { OrderId = orderId, LineIds = [lineId] }));
        await InTenantAsync(TenantA, () => GetRequiredService<TransportOrderLinesChangedHandler>().HandleEventAsync(new TransportOrderLinesChangedEto { OrderId = orderId, LineIds = [lineId] }));
        await InTenantAsync(TenantA, () => GetRequiredService<TransportOrderConfirmedHandler>().HandleEventAsync(new TransportOrderConfirmedEto { OrderId = orderId }));
        await InTenantAsync(TenantA, () => GetRequiredService<TransportOrderStopsChangedHandler>().HandleEventAsync(new TransportOrderStopsChangedEto { OrderId = orderId }));
        await InTenantAsync(TenantA, () => GetRequiredService<TransportOrderFinancialTermsChangedHandler>().HandleEventAsync(new TransportOrderFinancialTermsChangedEto { OrderId = orderId }));
        await InTenantAsync(TenantA, () => GetRequiredService<LegsGeneratedHandler>().HandleEventAsync(new LegsGeneratedEto { OrderId = orderId }));
        await InTenantAsync(TenantA, () => GetRequiredService<TransportTripRecalculatedHandler>().HandleEventAsync(new TransportTripRecalculatedEto { TripId = Guid.NewGuid(), Version = 1, OrderIds = [orderId] }));

        var state = (await StateAsync(OrderRateLineCalculator.TargetType, orderId))!;
        state.RequestedVersion.ShouldBe(7, "every handler requested once");
        state.Status.ShouldBe(DerivedValueStatus.Pending);

        await RunAsync(OrderRateLineCalculator.TargetType, orderId, state.RequestedVersion);
        (await OrderLinesAsync(orderId)).ShouldHaveSingleItem().Amount.ShouldBe(20m);

        await DrainOutboxAsync();
    }

    // ── the trip calculator ────────────────────────────────────────────────────

    [Fact]
    public async Task The_trip_calculator_prices_the_segments_with_the_carriers_lists_keeps_the_agreed_price_and_reports_totals()
    {
        var s = await ScenarioAsync();
        var costListId = await InTenantAsync(TenantA, () => ListAsync(TenantA, RateSide.Cost, s.Ams.RelationId, s.Bru.RelationId, 8m));
        await InTenantAsync(TenantA, async () =>
        {
            var settings = new RelationFinancialSettings(_guids.Create(), TenantA, s.Carrier.RelationId);
            settings.SetPriceLists(RateSide.Cost, [costListId], _guids.Create);
            await GetRequiredService<IRepository<RelationFinancialSettings, Guid>>().InsertAsync(settings);
        });

        /* One order through the depot (two legs: one segment on the trip), another direct (one leg), both on the carrier's trip. */
        var order = await OrderAsync(s, quantity: 5m, null, s.Ams, s.Depot, s.Bru);
        var other = await OrderAsync(s, quantity: 2m, null, s.Ams, s.Bru);
        var legs = await LegsAsync(order.Id);
        legs.Count.ShouldBe(2);
        var otherLeg = (await LegsAsync(other.Id)).Single();
        var trip = await TripAsync(s.Carrier.RelationId, [(legs[0], 100_000), (legs[1], 200_000), (otherLeg, 150_000)]);
        var tripId = trip.Id;

        /* The agreed carrier price arrives by event, once. */
        var created = new TransportTripCreatedEto { TripId = tripId, CreatedFromOrderId = order.Id, AgreedPurchaseAmount = 500m, AgreedPurchaseCurrencyCode = "EUR" };
        await InTenantAsync(TenantA, () => GetRequiredService<TransportTripCreatedHandler>().HandleEventAsync(created));
        await InTenantAsync(TenantA, () => GetRequiredService<TransportTripCreatedHandler>().HandleEventAsync(created));
        (await TripLinesAsync(tripId)).ShouldHaveSingleItem().Kind.ShouldBe(RateLineKind.AgreedPrice);

        var version = (await StateAsync(TripRateLineCalculator.TargetType, tripId))!.RequestedVersion;
        await RunAsync(TripRateLineCalculator.TargetType, tripId, version);

        var lines = await TripLinesAsync(tripId);
        lines.Count.ShouldBe(3);
        var agreed = lines.Single(l => l.Kind == RateLineKind.AgreedPrice);
        (agreed.Amount, agreed.OverridesChildren, agreed.TripId).ShouldBe((500m, true, tripId));
        var segment = lines.Single(l => l.LegId == legs[0].Id);
        (segment.Side, segment.Kind, segment.Quantity, segment.Amount, segment.AnchorTripId).ShouldBe((RateSide.Cost, RateLineKind.PriceList, 5m, 40m, tripId));
        lines.ShouldNotContain(l => l.LegId == legs[1].Id, "one line per segment, anchored to its first leg");
        lines.Single(l => l.LegId == otherLeg.Id).Amount.ShouldBe(16m);

        var reader = GetRequiredService<IRateLineReader>();
        var totals = await InTenantAsync(TenantA, () => reader.GetTripTotalsAsync(tripId), transactional: false);
        totals.TotalAmount.ShouldBe(500m, "the agreed price overrides the breakdown (AD-08)");
        totals.HasFixedPrice.ShouldBeTrue();
        totals.ByChild[legs[0].Id].ShouldBe(40m);
        totals.ByChild[otherLeg.Id].ShouldBe(16m);
        (await InTenantAsync(TenantA, () => reader.GetLegLinesAsync([legs[0].Id, legs[1].Id, otherLeg.Id]), transactional: false)).Select(l => l.Amount).OrderBy(a => a).ShouldBe([16m, 40m]);

        /* The trip's legs changed: both the trip and the orders are requested. */
        var orderVersion = (await StateAsync(OrderRateLineCalculator.TargetType, order.Id))?.RequestedVersion ?? 0;
        await InTenantAsync(TenantA, () => GetRequiredService<TransportTripLegsChangedHandler>().HandleEventAsync(new TransportTripLegsChangedEto { TripId = tripId, OrderIds = [order.Id, other.Id] }));
        (await StateAsync(TripRateLineCalculator.TargetType, tripId))!.RequestedVersion.ShouldBe(version + 1);
        (await StateAsync(OrderRateLineCalculator.TargetType, order.Id))!.RequestedVersion.ShouldBe(orderVersion + 1);

        await DrainOutboxAsync();
    }

    [Fact]
    public async Task Scenario_62_an_own_fleet_trip_is_costed_from_the_vehicles_cost_price_else_the_tenant_setting_and_without_either_gets_no_line()
    {
        var s = await ScenarioAsync();
        var order = await OrderAsync(s, quantity: 1m, null, s.Ams, s.Bru);
        var leg = (await LegsAsync(order.Id)).Single();
        var trip = await TripAsync(null, [(leg, 200_000)]);
        var tripId = trip.Id;

        var settings = GetRequiredService<ISettingManager>();
        await InTenantAsync(TenantA, () => settings.SetForTenantAsync(TenantA, FinancialSettings.OwnFleetCostPerKm, null));
        var v1 = await RequestTripAsync(tripId);
        await RunAsync(TripRateLineCalculator.TargetType, tripId, v1);
        (await TripLinesAsync(tripId)).ShouldBeEmpty("no price source: a warning, no line (Assumption 5)");

        await InTenantAsync(TenantA, () => settings.SetForTenantAsync(TenantA, FinancialSettings.OwnFleetCostPerKm, "1.40"));
        var v2 = await RequestTripAsync(tripId);
        await RunAsync(TripRateLineCalculator.TargetType, tripId, v2);

        var line = (await TripLinesAsync(tripId)).ShouldHaveSingleItem();
        (line.Kind, line.Quantity, line.QuantitySource, line.UnitPrice, line.Amount, line.TripId).ShouldBe((RateLineKind.OwnFleetDistance, 200m, QuantitySource.TripDistanceKm, 1.4m, 280m, tripId));

        /* QP10: the vehicles' cost price per kilometre (the vehicle's own, else its type's) outranks the tenant setting: a tractor of 0.95/km and a trailer whose type says 0.30/km. */
        var vehicleIds = await InTenantAsync(TenantA, async () =>
        {
            var tractorType = await Run.VehicleTypeAsync("Tractor " + s.Pallet.ToString("N")[..6], VehicleTypeKind.Tractor, costPricePerKm: 1.10m);
            var trailerType = await Run.VehicleTypeAsync("Trailer " + s.Pallet.ToString("N")[..6], VehicleTypeKind.Trailer, costPricePerKm: 0.30m);
            var tractor = await Run.VehicleAsync("Truck " + TransportRunFixture.NewSuffix(), tractorType, s.Carrier.RelationId, costPricePerKm: 0.95m);
            var trailer = await Run.VehicleAsync("Trailer " + TransportRunFixture.NewSuffix(), trailerType, s.Carrier.RelationId, euroClass: null);
            return new List<Guid> { tractor, trailer };
        });
        await InTenantAsync(TenantA, async () =>
        {
            var reloaded = await GetRequiredService<TripOperations>().GetWithDetailsAsync(tripId);
            reloaded.SetVehicles(null, vehicleIds);
            await GetRequiredService<IRepository<TransportTrip, Guid>>().UpdateAsync(reloaded);
        });
        var v3 = await RequestTripAsync(tripId);
        await RunAsync(TripRateLineCalculator.TargetType, tripId, v3);
        var fromVehicles = (await TripLinesAsync(tripId)).ShouldHaveSingleItem();
        (fromVehicles.UnitPrice, fromVehicles.Amount).ShouldBe((1.25m, 250m), "0.95 (the tractor's own) + 0.30 (the trailer's type) per km × 200 km");

        await InTenantAsync(TenantA, () => settings.SetForTenantAsync(TenantA, FinancialSettings.OwnFleetCostPerKm, null));
        await DrainOutboxAsync();
    }
}
