using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Fakes;
using OpenTms.Financial;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Tariffs;
using OpenTms.Financial.Tariffs.Calculators;
using OpenTms.Financial.Tariffs.Handlers;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.Orders.Events;
using OpenTms.PlanningExecution.Events;
using OpenTms.PlanningExecution.Published;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.Events.Outbox;
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
/// per segment, the agreed price written once by the handler and left alone, the own-fleet line from the tenant setting
/// (scenario 62); a job never writes in another tenant. The Orders and Planning readers are the fakes of
/// Fakes/FinancialFakes.cs. Risk class: Critical.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class RateLineCalculatorPostgreSqlTests : OpenTmsPostgreSqlTestBase
{
    private static readonly AbpUnitOfWorkOptions Transactional = new() { IsTransactional = true };
    private static readonly DateOnly Monday = new(2026, 10, 26);

    private readonly FinancialFakeStore _store;
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

    private static Guid TenantA => PostgreSqlTestDatabases.TenantAId;

    public RateLineCalculatorPostgreSqlTests()
    {
        _store = GetRequiredService<FinancialFakeStore>();
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

    private static PricingSnapshotDto Order(Guid orderId, Guid lineId, Guid from, Guid to, decimal quantity, Guid listId, int? distanceM = null) => new()
    {
        OrderId = orderId,
        CustomerRelationId = Guid.NewGuid(),
        OrderDate = Monday,
        CurrencyCode = "EUR",
        PriceListIds = [listId],
        Lines =
        [
            new PricingLineDto
            {
                LineId = lineId,
                Position = 1,
                TransportUnitId = Guid.NewGuid(),
                Quantity = quantity,
                DistanceM = distanceM,
                Stops =
                [
                    new PricingStopDto { StopId = Guid.NewGuid(), Position = 1, RelationId = from, Date = Monday },
                    new PricingStopDto { StopId = Guid.NewGuid(), Position = 2, RelationId = to, Date = Monday }
                ]
            }
        ]
    };

    private Task<long> RequestOrderAsync(Guid orderId) => InTenantAsync(TenantA, () => _scheduler.RequestAsync(OrderRateLineCalculator.TargetType, orderId, "Test"));

    private Task<long> RequestTripAsync(Guid tripId) => InTenantAsync(TenantA, () => _scheduler.RequestAsync(TripRateLineCalculator.TargetType, tripId, "Test"));

    private Task RunAsync(string targetType, Guid targetId, long version, Guid? tenantId = null)
        => _job.ExecuteAsync(new DerivedValueCalculationArgs { TenantId = tenantId ?? TenantA, TargetType = targetType, TargetId = targetId, Version = version });

    private Task<List<RateLine>> OrderLinesAsync(Guid orderId, Guid? tenantId = null)
        => InTenantAsync(tenantId ?? TenantA, () => _lines.GetListAsync(l => l.AnchorOrderId == orderId), transactional: false);

    private Task<List<RateLine>> TripLinesAsync(Guid tripId)
        => InTenantAsync(TenantA, () => _lines.GetListAsync(l => l.AnchorTripId == tripId), transactional: false);

    private Task<DerivedValueState?> StateAsync(string targetType, Guid id)
        => InTenantAsync(TenantA, () => _states.FindAsync(targetType, id), transactional: false);

    private static Task<long> OutboxRowsAsync(Guid id) => PostgreSqlTestDatabases.CountOutboxRowsAsync(PostgreSqlTestDatabases.TenantADatabase, id.ToString());

    // ── the order calculator ───────────────────────────────────────────────────

    [Fact]
    public async Task The_order_calculator_replaces_calculated_lines_keeps_manual_and_adjusted_ones_and_publishes_once_per_change()
    {
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var ams = Guid.NewGuid();
        var bru = Guid.NewGuid();
        var listId = await InTenantAsync(TenantA, () => ListAsync(TenantA, RateSide.Revenue, ams, bru, 10m));
        await InTenantAsync(TenantA, async () =>
        {
            var manual = RateLine.Manual(_guids.Create(), TenantA, RateSide.Revenue, RateLineAnchor.OrderLine(orderId, lineId), RateLineKind.Manual, "EUR", "Hand", 1m, QuantitySource.LineQuantity, 2.5m, null);
            var stale = RateLine.Calculated(_guids.Create(), TenantA, RateSide.Revenue, RateLineAnchor.OrderLine(orderId, lineId), RateLineKind.PriceList, "EUR", PriceResolver_Key(), 1m, QuantitySource.LineQuantity, 99m, 99m);
            await _lines.InsertManyAsync([manual, stale]);
        });
        _store.Orders[orderId] = Order(orderId, lineId, ams, bru, quantity: 4m, listId);

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

        /* Requested + recalculated + RateLinesChanged. */
        var afterFirst = await OutboxRowsAsync(orderId);
        afterFirst.ShouldBe(3);

        /* A repeated job for the same version is one effect. */
        await RunAsync(OrderRateLineCalculator.TargetType, orderId, v1);
        (await OrderLinesAsync(orderId)).Select(l => l.Id).OrderBy(id => id).ShouldBe(lines.Select(l => l.Id).OrderBy(id => id));
        (await OutboxRowsAsync(orderId)).ShouldBe(3);

        /* Unchanged facts: a new version writes nothing and publishes no RateLinesChanged (requested + recalculated only). */
        var v2 = await RequestOrderAsync(orderId);
        await RunAsync(OrderRateLineCalculator.TargetType, orderId, v2);
        (await OrderLinesAsync(orderId)).Single(l => l.Kind == RateLineKind.PriceList).Id.ShouldBe(calculated.Id, "the same line row stays");
        (await OutboxRowsAsync(orderId)).ShouldBe(5);

        /* A changed fact: the lines are replaced and the event travels. The user adjusted the unit price meanwhile: it is kept. */
        await InTenantAsync(TenantA, async () =>
        {
            var line = await _lines.GetAsync(calculated.Id);
            line.AdjustUnitPrice(9m);
            await _lines.UpdateAsync(line);
        });
        _store.Orders[orderId] = Order(orderId, lineId, ams, bru, quantity: 6m, listId);
        var v3 = await RequestOrderAsync(orderId);
        await RunAsync(OrderRateLineCalculator.TargetType, orderId, v3);
        var adjusted = (await OrderLinesAsync(orderId)).Single(l => l.Kind == RateLineKind.PriceList);
        (adjusted.Id, adjusted.IsAdjusted, adjusted.Quantity, adjusted.UnitPrice, adjusted.Amount).ShouldBe((calculated.Id, true, 6m, 9m, 54m));
        (await OutboxRowsAsync(orderId)).ShouldBe(8);

        while (await _dispatcher.DispatchAsync(TenantA) > 0)
        {
        }
    }

    private static string PriceResolver_Key() => OpenTms.Financial.Tariffs.Pricing.PriceResolver.PriceListDescriptionKey;

    [Fact]
    public async Task A_superseded_version_is_discarded_and_the_job_writes_only_in_the_tenant_of_its_arguments()
    {
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var ams = Guid.NewGuid();
        var bru = Guid.NewGuid();
        var listId = await InTenantAsync(TenantA, () => ListAsync(TenantA, RateSide.Revenue, ams, bru, 10m));
        _store.Orders[orderId] = Order(orderId, lineId, ams, bru, quantity: 1m, listId);

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

        while (await _dispatcher.DispatchAsync(TenantA) > 0)
        {
        }
    }

    [Fact]
    public async Task The_order_handlers_request_a_new_version_and_a_repeated_delivery_is_one_effect()
    {
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var ams = Guid.NewGuid();
        var bru = Guid.NewGuid();
        var listId = await InTenantAsync(TenantA, () => ListAsync(TenantA, RateSide.Revenue, ams, bru, 10m));
        _store.Orders[orderId] = Order(orderId, lineId, ams, bru, quantity: 2m, listId);

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

        while (await _dispatcher.DispatchAsync(TenantA) > 0)
        {
        }
    }

    // ── the trip calculator ────────────────────────────────────────────────────

    private LegSnapshotDto Leg(Guid tripId, Guid orderId, Guid lineId, int position, Guid from, Guid to, int? distanceM) => new()
    {
        Id = _guids.Create(),
        OrderId = orderId,
        OrderLineId = lineId,
        Position = position,
        TripId = tripId,
        Origin = new LegEndDto { StopId = Guid.NewGuid(), RelationId = from, PlannedDate = Monday },
        Destination = new LegEndDto { StopId = Guid.NewGuid(), RelationId = to, PlannedDate = Monday },
        DistanceM = distanceM,
        Quantity = 1m
    };

    [Fact]
    public async Task The_trip_calculator_prices_the_segments_with_the_carriers_lists_keeps_the_agreed_price_and_reports_totals()
    {
        var tripId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var otherOrderId = Guid.NewGuid();
        var otherLineId = Guid.NewGuid();
        var carrier = Guid.NewGuid();
        var ams = Guid.NewGuid();
        var depot = Guid.NewGuid();
        var bru = Guid.NewGuid();
        var costListId = await InTenantAsync(TenantA, () => ListAsync(TenantA, RateSide.Cost, ams, bru, 8m));
        await InTenantAsync(TenantA, async () =>
        {
            var settings = new RelationFinancialSettings(_guids.Create(), TenantA, carrier);
            settings.SetPriceLists(RateSide.Cost, [costListId], _guids.Create);
            await GetRequiredService<IRepository<RelationFinancialSettings, Guid>>().InsertAsync(settings);
        });

        _store.Trips[tripId] = new TransportTripReferenceDto { Id = tripId, Number = "T1", TripDate = Monday, CarrierRelationId = carrier };
        var first = Leg(tripId, orderId, lineId, 1, ams, depot, 100_000);
        var second = Leg(tripId, orderId, lineId, 2, depot, bru, 200_000);
        var other = Leg(tripId, otherOrderId, otherLineId, 1, ams, bru, 150_000);
        foreach (var leg in new[] { first, second, other })
        {
            _store.Legs[leg.Id] = leg;
        }

        _store.Orders[orderId] = Order(orderId, lineId, ams, bru, quantity: 5m, Guid.NewGuid());
        _store.Orders[otherOrderId] = Order(otherOrderId, otherLineId, ams, bru, quantity: 2m, Guid.NewGuid());

        /* The agreed carrier price arrives by event, once. */
        var created = new TransportTripCreatedEto { TripId = tripId, CreatedFromOrderId = orderId, AgreedPurchaseAmount = 500m, AgreedPurchaseCurrencyCode = "EUR" };
        await InTenantAsync(TenantA, () => GetRequiredService<TransportTripCreatedHandler>().HandleEventAsync(created));
        await InTenantAsync(TenantA, () => GetRequiredService<TransportTripCreatedHandler>().HandleEventAsync(created));
        (await TripLinesAsync(tripId)).ShouldHaveSingleItem().Kind.ShouldBe(RateLineKind.AgreedPrice);

        var version = (await StateAsync(TripRateLineCalculator.TargetType, tripId))!.RequestedVersion;
        await RunAsync(TripRateLineCalculator.TargetType, tripId, version);

        var lines = await TripLinesAsync(tripId);
        lines.Count.ShouldBe(3);
        var agreed = lines.Single(l => l.Kind == RateLineKind.AgreedPrice);
        (agreed.Amount, agreed.OverridesChildren, agreed.TripId).ShouldBe((500m, true, tripId));
        var segment = lines.Single(l => l.LegId == first.Id);
        (segment.Side, segment.Kind, segment.Quantity, segment.Amount, segment.AnchorTripId).ShouldBe((RateSide.Cost, RateLineKind.PriceList, 5m, 40m, tripId));
        lines.ShouldNotContain(l => l.LegId == second.Id, "one line per segment, anchored to its first leg");
        lines.Single(l => l.LegId == other.Id).Amount.ShouldBe(16m);

        var reader = GetRequiredService<IRateLineReader>();
        var totals = await InTenantAsync(TenantA, () => reader.GetTripTotalsAsync(tripId), transactional: false);
        totals.TotalAmount.ShouldBe(500m, "the agreed price overrides the breakdown (AD-08)");
        totals.HasFixedPrice.ShouldBeTrue();
        totals.ByChild[first.Id].ShouldBe(40m);
        totals.ByChild[other.Id].ShouldBe(16m);
        (await InTenantAsync(TenantA, () => reader.GetLegLinesAsync([first.Id, second.Id, other.Id]), transactional: false)).Select(l => l.Amount).OrderBy(a => a).ShouldBe([16m, 40m]);

        /* The trip's legs changed: both the trip and the orders are requested. */
        await InTenantAsync(TenantA, () => GetRequiredService<TransportTripLegsChangedHandler>().HandleEventAsync(new TransportTripLegsChangedEto { TripId = tripId, OrderIds = [orderId, otherOrderId] }));
        (await StateAsync(TripRateLineCalculator.TargetType, tripId))!.RequestedVersion.ShouldBe(version + 1);
        (await StateAsync(OrderRateLineCalculator.TargetType, orderId))!.RequestedVersion.ShouldBe(1);

        while (await _dispatcher.DispatchAsync(TenantA) > 0)
        {
        }
    }

    [Fact]
    public async Task Scenario_62_an_own_fleet_trip_is_costed_from_the_tenant_setting_and_without_one_gets_no_line()
    {
        var tripId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var ams = Guid.NewGuid();
        var bru = Guid.NewGuid();
        _store.Trips[tripId] = new TransportTripReferenceDto { Id = tripId, Number = "T2", TripDate = Monday, CarrierRelationId = null };
        var leg = Leg(tripId, orderId, lineId, 1, ams, bru, 200_000);
        _store.Legs[leg.Id] = leg;
        _store.Orders[orderId] = Order(orderId, lineId, ams, bru, quantity: 1m, Guid.NewGuid());

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

        await InTenantAsync(TenantA, () => settings.SetForTenantAsync(TenantA, FinancialSettings.OwnFleetCostPerKm, null));
        while (await _dispatcher.DispatchAsync(TenantA) > 0)
        {
        }
    }
}
