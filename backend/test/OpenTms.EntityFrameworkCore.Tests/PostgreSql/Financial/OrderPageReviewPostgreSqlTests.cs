using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Published;
using OpenTms.Financial.Tariffs;
using OpenTms.Financial.Tariffs.Calculators;
using OpenTms.Financial.Tariffs.Handlers;
using OpenTms.Financial.Tariffs.Pricing;
using OpenTms.Financial.Tariffs.Published;
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
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.Financial;

/// <summary>
/// Design 61 "Order page review" points 3 and 4 with build notes 1–3 and 5 through the real derived-value job against
/// PostgreSQL: the order's charged bases (bit mask) give open lines ("To be priced") per freight item; the clerk prices one
/// with Adjust unit price and it is kept and re-valued, also after its basis is switched off; an unadjusted one vanishes
/// with its basis; removing an open line while its basis is charged brings back a fresh one; a list of the basis applied
/// later writes no second line next to an adjusted open line; the customer's revenue charged bases are stored and copied
/// into an order without terms; the order's purchase prices are the cost lines of its legs on their trips, with trip and
/// freight item, and a manual purchase line is checked against the leg. Every read and job of another tenant finds
/// nothing. The orders, legs and trips are real rows of Orders and Planning in the transport tenant's own database; the
/// facts they raise are drained from the outbox at the end of each test. Risk class: Critical (money, tenant isolation).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class OrderPageReviewPostgreSqlTests : OpenTmsTestBase<TransportRunPostgreSqlTestModule>
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

    private static Guid TenantA => PostgreSqlTestDatabases.TransportTenantAId;

    private static Guid OtherTenant => PostgreSqlTestDatabases.TransportSharedTenantId;

    private TransportRunFixture Run => GetRequiredService<TransportRunFixture>();

    private ITransportOrderAppService Orders => GetRequiredService<ITransportOrderAppService>();

    private IRateLineAppService RateLines => GetRequiredService<IRateLineAppService>();

    public OrderPageReviewPostgreSqlTests()
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

    // ── arrangement (as RateLineCalculatorPostgreSqlTests) ─────────────────────

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

    private static TransportOrderLineInputDto LineOf(Scenario s, decimal quantity, double weightKg, Guid? id = null, params Place[] stops) => new()
    {
        Id = id, TransportUnitId = s.Pallet, Quantity = quantity, WeightKg = weightKg,
        Stops = stops.Select((p, i) => TransportRunFixture.Stop(i == 0 ? s.Load : s.Unload, p.RelationId, p.AddressId, Monday)).ToList()
    };

    private Task<TransportOrderDto> OrderAsync(Scenario s, IReadOnlyList<Guid> lists, IReadOnlyList<QuantitySource> bases, params TransportOrderLineInputDto[] lines)
        => InTenantAsync(TenantA, () => Orders.CreateAsync(new TransportOrderCreateDto
        {
            TransportOrderTypeId = s.OrderTypeId,
            CustomerRelationId = s.Ams.RelationId,
            OrderDate = Monday,
            FinancialTerms = new TransportOrderFinancialTermsInputDto { CurrencyCode = "EUR", PriceListIds = lists.ToList(), ChargedBases = bases.ToList() },
            Lines = lines.ToList(),
            Confirm = true
        }));

    private Task SetTermsAsync(Guid orderId, IReadOnlyList<Guid> lists, params QuantitySource[] bases) => InTenantAsync(TenantA, async () =>
    {
        var current = await Orders.GetAsync(orderId);
        await Orders.UpdateFinancialTermsAsync(orderId, new TransportOrderFinancialTermsUpdateDto
        {
            CurrencyCode = "EUR", PriceListIds = lists.ToList(), ChargedBases = bases.ToList(), ConcurrencyStamp = current.ConcurrencyStamp
        });
    });

    private Task SetWeightAsync(Scenario s, Guid orderId, double weightKg) => InTenantAsync(TenantA, async () =>
    {
        var current = await Orders.GetAsync(orderId);
        var line = current.Lines.Single();
        await Orders.UpdateLineAsync(orderId, line.Id!.Value, new TransportOrderLineInputDto
        {
            Id = line.Id, TransportUnitId = line.TransportUnitId, Quantity = line.Quantity, WeightKg = weightKg,
            Stops = line.Stops.Select(st => new TransportOrderStopInputDto { Id = st.Id, WaypointTypeId = st.WaypointTypeId, RelationId = st.RelationId, RelationAddressId = st.RelationAddressId, Date = st.Date, PlannedDurationMin = st.PlannedDurationMin }).ToList()
        });
    });

    private async Task CalculateOrderAsync(Guid orderId)
    {
        var version = await InTenantAsync(TenantA, () => _scheduler.RequestAsync(OrderRateLineCalculator.TargetType, orderId, "Test"));
        using (AsBackgroundProcess())
        {
            await _job.ExecuteAsync(new DerivedValueCalculationArgs { TenantId = TenantA, TargetType = OrderRateLineCalculator.TargetType, TargetId = orderId, Version = version });
        }
    }

    private async Task CalculateTripAsync(Guid tripId)
    {
        var version = await InTenantAsync(TenantA, () => _scheduler.RequestAsync(TripRateLineCalculator.TargetType, tripId, "Test"));
        using (AsBackgroundProcess())
        {
            await _job.ExecuteAsync(new DerivedValueCalculationArgs { TenantId = TenantA, TargetType = TripRateLineCalculator.TargetType, TargetId = tripId, Version = version });
        }
    }

    private Task<List<RateLine>> OrderLinesAsync(Guid orderId, Guid? tenantId = null)
        => InTenantAsync(tenantId ?? TenantA, () => _lines.GetListAsync(l => l.AnchorOrderId == orderId), transactional: false);

    private static bool IsOpen(RateLine l) => l.Kind == RateLineKind.PriceList && l.PriceListId == null && !l.IsManual;

    private Task AdjustAsync(Guid lineId, decimal unitPrice) => InTenantAsync(TenantA, async () =>
    {
        var line = await RateLines.GetAsync(new RateLineGetRequestDto { OrderId = (await _lines.GetAsync(lineId)).AnchorOrderId });
        var dto = line.Lines.Single(l => l.Id == lineId);
        await RateLines.UpdateAsync(lineId, new RateLineUpdateDto { UnitPrice = unitPrice, ConcurrencyStamp = dto.ConcurrencyStamp });
    });

    private Task<List<Leg>> LegsAsync(Guid orderId) => InTenantAsync(TenantA, async () =>
    {
        using (AsBackgroundProcess())
        {
            await GetRequiredService<OrderLegsHandler>().HandleEventAsync(new TransportOrderConfirmedEto { OrderId = orderId });
        }

        await _uow.Current!.SaveChangesAsync();
        return (await GetRequiredService<IRepository<Leg, Guid>>().GetListAsync(l => l.OrderId == orderId)).OrderBy(l => l.Position).ToList();
    });

    private Task<TransportTrip> TripAsync(Guid? carrierRelationId, IReadOnlyList<(Leg Leg, int DistanceM)> legs) => InTenantAsync(TenantA, async () =>
    {
        var operations = GetRequiredService<TripOperations>();
        var typeId = (await GetRequiredService<IRepository<TransportTripType, Guid>>().GetListAsync()).OrderBy(t => t.IsDefault ? 0 : 1).First().Id;
        var trip = await operations.CreateAsync(new TransportTripCreateDto
        {
            TransportTripTypeId = typeId, TripDate = Monday, PlannedDeparture = new DateTimeOffset(2026, 10, 26, 6, 0, 0, TimeSpan.FromHours(1)),
            PlannedArrival = new DateTimeOffset(2026, 10, 26, 18, 0, 0, TimeSpan.FromHours(1)),
            CarrierRelationId = carrierRelationId, Execution = carrierRelationId == null ? TripExecution.OwnFleet : TripExecution.Subcontracted, VehicleIds = []
        }, null, null, null);
        await operations.AssignAsync(trip, legs.Select(l => l.Leg).ToList(), allowOutsideWindow: true, afterStopId: null, moving: false);
        foreach (var (leg, distanceM) in legs)
        {
            await Run.SetLegValuesAsync(leg.Id, distanceM);
        }

        return trip;
    });

    private async Task DrainOutboxAsync()
    {
        using var anonymous = AsBackgroundProcess();
        while (await _dispatcher.DispatchAsync(TenantA) > 0)
        {
        }
    }

    // ── open lines through the real job ─────────────────────────────────────────

    [Fact]
    public async Task Open_lines_are_written_priced_kept_re_valued_removed_and_return_while_their_basis_is_charged()
    {
        var s = await ScenarioAsync();
        var quantityList = await InTenantAsync(TenantA, () => ListAsync(TenantA, RateSide.Revenue, s.Ams.RelationId, s.Bru.RelationId, 10m));
        var order = await OrderAsync(s, [quantityList], [QuantitySource.WeightKg], LineOf(s, 4m, 500, null, s.Ams, s.Bru));
        var orderId = order.Id;
        var lineId = order.Lines.Single().Id!.Value;
        order.FinancialTerms.ChargedBases.ShouldBe([QuantitySource.WeightKg], "stored as a mask on PostgreSQL and read back");

        /* 1. The charged basis without a list: one open line next to the list's line. */
        await CalculateOrderAsync(orderId);
        var lines = await OrderLinesAsync(orderId);
        lines.Count.ShouldBe(2);
        lines.Single(l => l.PriceListId == quantityList).Amount.ShouldBe(40m);
        var open = lines.Single(IsOpen);
        (open.OrderLineId, open.QuantitySource, open.Quantity, open.UnitPrice, open.Amount, open.DescriptionKey, open.IsAdjusted)
            .ShouldBe(((Guid?)lineId, QuantitySource.WeightKg, 500m, 0m, 0m, PriceResolver.OpenBasisDescriptionKey, false));

        /* 2. Priced with Adjust unit price: kept on the next run, no second open line. */
        await AdjustAsync(open.Id, 0.1m);
        await CalculateOrderAsync(orderId);
        lines = await OrderLinesAsync(orderId);
        lines.Count.ShouldBe(2);
        (lines.Single(IsOpen).Id, lines.Single(IsOpen).IsAdjusted, lines.Single(IsOpen).Amount).ShouldBe((open.Id, true, 50m));

        /* 3. The weight changes: the adjusted open line is re-valued from it. */
        await SetWeightAsync(s, orderId, 600);
        await CalculateOrderAsync(orderId);
        (await OrderLinesAsync(orderId)).Single(IsOpen).ShouldSatisfyAllConditions(l => l.Quantity.ShouldBe(600m), l => l.Amount.ShouldBe(60m), l => l.Id.ShouldBe(open.Id));

        /* 4. Removed while the basis is charged: the next run writes a fresh, unadjusted open line (legacy). */
        await InTenantAsync(TenantA, () => RateLines.DeleteAsync(open.Id));
        await CalculateOrderAsync(orderId);
        var fresh = (await OrderLinesAsync(orderId)).Single(IsOpen);
        (fresh.Id == open.Id, fresh.IsAdjusted, fresh.UnitPrice, fresh.Quantity).ShouldBe((false, false, 0m, 600m));

        /* 5. Priced again, then the basis is switched off: the adjusted open line stays and keeps following the weight. */
        await AdjustAsync(fresh.Id, 0.2m);
        await SetTermsAsync(orderId, [quantityList]);
        await SetWeightAsync(s, orderId, 700);
        await CalculateOrderAsync(orderId);
        lines = await OrderLinesAsync(orderId);
        lines.Count.ShouldBe(2);
        lines.Single(IsOpen).ShouldSatisfyAllConditions(l => l.Id.ShouldBe(fresh.Id), l => l.Quantity.ShouldBe(700m), l => l.Amount.ShouldBe(140m));
        (await InTenantAsync(TenantA, () => GetRequiredService<IRateLineReader>().GetOrderTotalsAsync(orderId), transactional: false)).TotalAmount.ShouldBe(180m);

        /* 6. Removed with the basis off: gone for good. */
        await InTenantAsync(TenantA, () => RateLines.DeleteAsync(fresh.Id));
        await CalculateOrderAsync(orderId);
        (await OrderLinesAsync(orderId)).ShouldNotContain(l => IsOpen(l));

        /* 7. On again: an unadjusted open line; off again: it vanishes. */
        await SetTermsAsync(orderId, [quantityList], QuantitySource.WeightKg);
        await CalculateOrderAsync(orderId);
        (await OrderLinesAsync(orderId)).Single(IsOpen).IsAdjusted.ShouldBeFalse();
        await SetTermsAsync(orderId, [quantityList]);
        await CalculateOrderAsync(orderId);
        (await OrderLinesAsync(orderId)).ShouldNotContain(l => IsOpen(l), "an open line that was never adjusted disappears with its basis");

        await DrainOutboxAsync();
    }

    [Fact]
    public async Task A_list_of_the_basis_applied_later_writes_no_second_line_next_to_an_adjusted_open_line_and_a_fixed_price_writes_none()
    {
        var s = await ScenarioAsync();
        var weightList = await InTenantAsync(TenantA, () => ListAsync(TenantA, RateSide.Revenue, s.Ams.RelationId, s.Bru.RelationId, 0.08m, QuantitySource.WeightKg));
        var order = await OrderAsync(s, [], [QuantitySource.WeightKg, QuantitySource.LineQuantity], LineOf(s, 2m, 1000, null, s.Ams, s.Bru), LineOf(s, 1m, 500, null, s.Ams, s.Bru));
        var orderId = order.Id;
        var first = order.Lines.Single(l => l.Position == 1).Id!.Value;
        var second = order.Lines.Single(l => l.Position == 2).Id!.Value;

        await CalculateOrderAsync(orderId);
        var lines = await OrderLinesAsync(orderId);
        lines.Count.ShouldBe(4, "two charged bases × two freight items");
        lines.ShouldAllBe(l => IsOpen(l));
        var firstWeight = lines.Single(l => l.OrderLineId == first && l.QuantitySource == QuantitySource.WeightKg);
        await AdjustAsync(firstWeight.Id, 0.1m);

        await SetTermsAsync(orderId, [weightList], QuantitySource.WeightKg, QuantitySource.LineQuantity);
        await CalculateOrderAsync(orderId);
        lines = await OrderLinesAsync(orderId);
        lines.Where(l => l.OrderLineId == first && l.QuantitySource == QuantitySource.WeightKg).ShouldHaveSingleItem().Id.ShouldBe(firstWeight.Id, "no double charge");
        lines.Single(l => l.OrderLineId == second && l.PriceListId == weightList).Amount.ShouldBe(40m);
        lines.ShouldNotContain(l => l.OrderLineId == second && IsOpen(l) && l.QuantitySource == QuantitySource.WeightKg);
        lines.Where(l => IsOpen(l) && !l.IsAdjusted).Select(l => l.QuantitySource).Distinct().ShouldBe([QuantitySource.LineQuantity]);

        /* A fixed price: no open lines (the adjusted one is the user's and stays). */
        await InTenantAsync(TenantA, async () =>
        {
            var current = await Orders.GetAsync(orderId);
            await Orders.UpdateFinancialTermsAsync(orderId, new TransportOrderFinancialTermsUpdateDto
            {
                CurrencyCode = "EUR", PriceListIds = [weightList], ChargedBases = [QuantitySource.LineQuantity], FixedPriceAmount = 900m, ConcurrencyStamp = current.ConcurrencyStamp
            });
        });
        await CalculateOrderAsync(orderId);
        lines = await OrderLinesAsync(orderId);
        lines.Where(l => IsOpen(l)).Select(l => l.Id).ShouldBe([firstWeight.Id]);
        lines.ShouldContain(l => l.Kind == RateLineKind.FixedPrice);

        await DrainOutboxAsync();
    }

    [Fact]
    public async Task The_customers_revenue_charged_bases_are_stored_and_copied_and_another_tenant_reaches_neither_them_nor_the_open_lines()
    {
        var s = await ScenarioAsync();
        await InTenantAsync(TenantA, async () =>
        {
            var settings = new RelationFinancialSettings(_guids.Create(), TenantA, s.Ams.RelationId);
            settings.SetRevenueChargedBases([QuantitySource.TollDistanceKm, QuantitySource.LineQuantity]);
            await GetRequiredService<IRepository<RelationFinancialSettings, Guid>>().InsertAsync(settings);
        });

        var reference = await InTenantAsync(TenantA, () => GetRequiredService<IFinancialReferenceLookup>().GetRelationFinancialSettingsAsync(s.Ams.RelationId, Monday), transactional: false);
        reference.ShouldNotBeNull().RevenueChargedBases.ShouldBe([QuantitySource.LineQuantity, QuantitySource.TollDistanceKm]);

        var order = await InTenantAsync(TenantA, () => Orders.CreateAsync(new TransportOrderCreateDto
        {
            TransportOrderTypeId = s.OrderTypeId, CustomerRelationId = s.Ams.RelationId, OrderDate = Monday, Confirm = true,
            Lines = [LineOf(s, 3m, 300, null, s.Ams, s.Bru)]
        }));
        order.FinancialTerms.ChargedBases.ShouldBe([QuantitySource.LineQuantity, QuantitySource.TollDistanceKm], "copied from the customer through Financial's published lookup");
        await CalculateOrderAsync(order.Id);
        var open = await OrderLinesAsync(order.Id);
        open.Count.ShouldBe(2);
        open.ShouldAllBe(l => IsOpen(l));

        /* Another tenant: no settings, no lines, no removal, and its job writes nothing for this order. */
        (await InTenantAsync(OtherTenant, () => GetRequiredService<IFinancialReferenceLookup>().GetRelationFinancialSettingsAsync(s.Ams.RelationId, Monday), transactional: false)).ShouldBeNull();
        (await OrderLinesAsync(order.Id, OtherTenant)).ShouldBeEmpty();
        (await InTenantAsync(OtherTenant, () => RateLines.GetAsync(new RateLineGetRequestDto { OrderId = order.Id }), transactional: false)).Lines.ShouldBeEmpty();
        await Should.ThrowAsync<EntityNotFoundException>(() => InTenantAsync(OtherTenant, () => RateLines.DeleteAsync(open[0].Id)));
        await Should.ThrowAsync<EntityNotFoundException>(() => InTenantAsync(OtherTenant, () => RateLines.UpdateAsync(open[0].Id, new RateLineUpdateDto { UnitPrice = 1m, ConcurrencyStamp = open[0].ConcurrencyStamp })));
        using (AsBackgroundProcess())
        {
            await _job.ExecuteAsync(new DerivedValueCalculationArgs { TenantId = OtherTenant, TargetType = OrderRateLineCalculator.TargetType, TargetId = order.Id, Version = 99 });
        }

        (await OrderLinesAsync(order.Id, OtherTenant)).ShouldBeEmpty();
        (await OrderLinesAsync(order.Id)).Select(l => l.Id).ShouldBe(open.Select(l => l.Id), ignoreOrder: true, "tenant A's lines are untouched");

        await DrainOutboxAsync();
    }

    // ── purchase prices (point 4) ──────────────────────────────────────────────

    [Fact]
    public async Task The_orders_purchase_prices_are_the_cost_lines_of_its_legs_on_their_trips_and_a_manual_purchase_line_is_checked_against_the_leg()
    {
        var s = await ScenarioAsync();
        var costListId = await InTenantAsync(TenantA, () => ListAsync(TenantA, RateSide.Cost, s.Ams.RelationId, s.Bru.RelationId, 8m));
        await InTenantAsync(TenantA, async () =>
        {
            var settings = new RelationFinancialSettings(_guids.Create(), TenantA, s.Carrier.RelationId);
            settings.SetPriceLists(RateSide.Cost, [costListId], _guids.Create);
            await GetRequiredService<IRepository<RelationFinancialSettings, Guid>>().InsertAsync(settings);
        });

        var order = await OrderAsync(s, [], [], LineOf(s, 5m, 500, null, s.Ams, s.Depot, s.Bru));
        var other = await OrderAsync(s, [], [], LineOf(s, 2m, 200, null, s.Ams, s.Bru));
        var legs = await LegsAsync(order.Id);
        var otherLeg = (await LegsAsync(other.Id)).Single();
        var trip = await TripAsync(s.Carrier.RelationId, [(legs[0], 100_000), (legs[1], 200_000), (otherLeg, 150_000)]);
        var created = new TransportTripCreatedEto { TripId = trip.Id, CreatedFromOrderId = order.Id, AgreedPurchaseAmount = 500m, AgreedPurchaseCurrencyCode = "EUR" };
        await InTenantAsync(TenantA, () => GetRequiredService<TransportTripCreatedHandler>().HandleEventAsync(created));
        await CalculateTripAsync(trip.Id);

        var set = await InTenantAsync(TenantA, () => RateLines.GetAsync(new RateLineGetRequestDto { OrderId = order.Id, Side = RateSide.Cost }), transactional: false);
        var segment = set.Lines.ShouldHaveSingleItem("the segment's line on the order's first leg; the agreed price and the other order's line stay out");
        (segment.LegId, segment.AnchorTripId, segment.TripNumber, segment.LegOrderLineId, segment.Amount)
            .ShouldBe(((Guid?)legs[0].Id, (Guid?)trip.Id, trip.Number, (Guid?)order.Lines.Single().Id, 40m));
        set.Totals.Total.ShouldBe(40m);
        set.State.ShouldNotBeNull().Status.ShouldBe(DerivedValueStatus.UpToDate);

        /* A manual purchase line on this freight item's leg on that trip; another order's leg or line is refused. */
        var lineId = order.Lines.Single().Id!.Value;
        var refusal = await Should.ThrowAsync<BusinessException>(() => InTenantAsync(TenantA, () => RateLines.CreateManualAsync(new ManualRateLineCreateDto
        {
            TripId = trip.Id, OrderLineId = lineId, LegId = otherLeg.Id, Description = "Waiting", Quantity = 1m, UnitPrice = 30m
        })));
        refusal.Code.ShouldBe(FinancialErrorCodes.LegNotOnTrip);
        var manual = await InTenantAsync(TenantA, () => RateLines.CreateManualAsync(new ManualRateLineCreateDto
        {
            TripId = trip.Id, OrderLineId = lineId, LegId = legs[0].Id, Description = "Waiting", Quantity = 1m, UnitPrice = 30m
        }));
        var after = await InTenantAsync(TenantA, () => RateLines.GetAsync(new RateLineGetRequestDto { OrderId = order.Id, Side = RateSide.Cost }), transactional: false);
        after.Lines.Select(l => l.Id).ShouldContain(manual.Id);
        after.Lines.Single(l => l.Id == manual.Id).TripNumber.ShouldBe(trip.Number);
        after.Totals.Total.ShouldBe(70m);

        /* Another tenant reads no purchase prices and cannot anchor a line on the leg. */
        (await InTenantAsync(OtherTenant, () => RateLines.GetAsync(new RateLineGetRequestDto { OrderId = order.Id, Side = RateSide.Cost }), transactional: false)).Lines.ShouldBeEmpty();
        (await Should.ThrowAsync<BusinessException>(() => InTenantAsync(OtherTenant, () => RateLines.CreateManualAsync(new ManualRateLineCreateDto
        {
            TripId = trip.Id, OrderLineId = lineId, LegId = legs[0].Id, Quantity = 1m, UnitPrice = 1m
        })))).Code.ShouldBe(FinancialErrorCodes.LegNotOnTrip);

        await DrainOutboxAsync();
    }
}
