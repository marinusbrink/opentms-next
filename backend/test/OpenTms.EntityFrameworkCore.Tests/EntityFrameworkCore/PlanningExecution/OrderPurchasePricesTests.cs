using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial;
using OpenTms.Financial.Tariffs;
using OpenTms.PlanningExecution.TransportTrips;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// Design 61 "Order page review" point 4 with build note 3, on SQLite with real orders, legs and trips:
/// <c>GET /api/financial/rate-line?OrderId&amp;Side=2</c> returns the cost lines on the order's legs within the trip each leg
/// is on now (a trip-level line, a line left behind on another trip, revenue lines and another order's lines stay out),
/// each with <c>anchorTripId</c>, <c>tripNumber</c> and <c>legOrderLineId</c>, totals per leg; without a side the order's
/// revenue read is unchanged. A manual purchase line names trip, order line and leg and is refused with
/// <c>Financial:LegNotOnTrip</c> when the leg is not on that trip or not of that order line; another tenant reads nothing and
/// cannot anchor a line on the leg. Risk class: Critical (money, tenant isolation).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class OrderPurchasePricesTests : PlanningAppServiceTestBase
{
    private IRateLineAppService RateLines => GetRequiredService<IRateLineAppService>();

    private IRepository<RateLine, Guid> Lines => GetRequiredService<IRepository<RateLine, Guid>>();

    private sealed record Scenario(Guid OrderId, Guid FirstLineId, Guid SecondLineId, Guid FirstLegId, Guid SecondLegId, TransportTripDto FirstTrip, TransportTripDto SecondTrip);

    /// <summary>A confirmed order of two freight items (A→B, A→C), each leg on its own trip.</summary>
    private async Task<Scenario> ScenarioAsync()
    {
        var type = await TripTypeAsync();
        var a = await PlaceAsync("A" + TransportRun.TransportRunFixture.NewSuffix(), "Alpha", 52.0, 5.0);
        var b = await PlaceAsync("B" + TransportRun.TransportRunFixture.NewSuffix(), "Beta", 52.1, 5.1);
        var c = await PlaceAsync("C" + TransportRun.TransportRunFixture.NewSuffix(), "Gamma", 52.2, 5.2);
        var orderId = await ConfirmedOrderAsync(a.RelationId,
            LineOf(2, 200, StopAt(a, Load, Day), StopAt(b, Unload, Day)),
            LineOf(3, 300, StopAt(a, Load, Day), StopAt(c, Unload, Day)));
        var order = await Orders.GetAsync(orderId);
        var firstLine = order.Lines.Single(l => l.Position == 1).Id!.Value;
        var secondLine = order.Lines.Single(l => l.Position == 2).Id!.Value;
        var legs = await LegsOfOrderAsync(orderId);
        var firstLeg = legs.Single(l => l.OrderLineId == firstLine).Id;
        var secondLeg = legs.Single(l => l.OrderLineId == secondLine).Id;
        var firstTrip = await CreateTripAsync(type, At(6), At(18));
        var secondTrip = await CreateTripAsync(type, At(6), At(18));
        await Trips.AssignLegsAsync(firstTrip.Id, new AssignLegsDto { LegIds = [firstLeg] });
        await Trips.AssignLegsAsync(secondTrip.Id, new AssignLegsDto { LegIds = [secondLeg] });
        return new Scenario(orderId, firstLine, secondLine, firstLeg, secondLeg, await Trips.GetAsync(firstTrip.Id), await Trips.GetAsync(secondTrip.Id));
    }

    private static RateLine Cost(Guid tenantId, RateLineAnchor anchor, decimal quantity, decimal unitPrice, int position, RateLineKind kind = RateLineKind.PriceList)
    {
        var line = RateLine.Calculated(Guid.NewGuid(), tenantId, RateSide.Cost, anchor, kind, "EUR", "Financial:RateLine.PriceList", quantity, QuantitySource.LineQuantity, unitPrice, quantity * unitPrice);
        line.Position = position;
        return line;
    }

    private async Task<(RateLine OnFirst, RateLine OnSecond)> SeedLinesAsync(Scenario s, Guid tenantId)
    {
        var onFirst = Cost(tenantId, RateLineAnchor.Leg(s.FirstTrip.Id, s.FirstLegId), 2m, 40m, 1);
        var onSecond = Cost(tenantId, RateLineAnchor.Leg(s.SecondTrip.Id, s.SecondLegId), 3m, 15m, 1);
        var tripLevel = RateLine.Overriding(Guid.NewGuid(), tenantId, RateSide.Cost, RateLineAnchor.Trip(s.FirstTrip.Id), RateLineKind.AgreedPrice, "EUR", 999m);
        tripLevel.Position = 2;
        /* A line left behind on the second trip for the first leg (the leg is on the first trip now): that trip's, not the item's. */
        var leftBehind = Cost(tenantId, RateLineAnchor.Leg(s.SecondTrip.Id, s.FirstLegId), 1m, 777m, 2);
        var revenue = RateLine.Calculated(Guid.NewGuid(), tenantId, RateSide.Revenue, RateLineAnchor.OrderLine(s.OrderId, s.FirstLineId), RateLineKind.PriceList, "EUR",
            "Financial:RateLine.PriceList", 2m, QuantitySource.LineQuantity, 10m, 20m);
        revenue.Position = 1;
        await Lines.InsertManyAsync([onFirst, onSecond, tripLevel, leftBehind, revenue], autoSave: true);
        return (onFirst, onSecond);
    }

    [Fact]
    public async Task The_orders_cost_read_returns_the_lines_on_its_legs_within_their_current_trip_with_trip_and_freight_item()
    {
        using var _ = InTenantA();
        var s = await ScenarioAsync();
        var (onFirst, onSecond) = await SeedLinesAsync(s, TenantA);

        var set = await RateLines.GetAsync(new RateLineGetRequestDto { OrderId = s.OrderId, Side = RateSide.Cost });

        set.Lines.Select(l => l.Id).ShouldBe([onFirst.Id, onSecond.Id], ignoreOrder: true);
        var first = set.Lines.Single(l => l.Id == onFirst.Id);
        (first.Side, first.LegId, first.AnchorTripId, first.TripNumber, first.LegOrderLineId, first.Amount).ShouldBe((RateSide.Cost, (Guid?)s.FirstLegId, (Guid?)s.FirstTrip.Id, s.FirstTrip.Number, (Guid?)s.FirstLineId, 80m));
        var second = set.Lines.Single(l => l.Id == onSecond.Id);
        (second.AnchorTripId, second.TripNumber, second.LegOrderLineId, second.Amount).ShouldBe(((Guid?)s.SecondTrip.Id, s.SecondTrip.Number, (Guid?)s.SecondLineId, 45m));
        s.FirstTrip.Number.ShouldNotBeNullOrEmpty();
        set.Totals.ByAnchor.Select(t => (t.AnchorId, t.Amount)).ShouldBe([(s.FirstLegId, 80m), (s.SecondLegId, 45m)], ignoreOrder: true);
        set.Totals.Total.ShouldBe(125m, "per freight item: the trip's agreed price and the line left behind stay out");

        /* Without a side the order's own (revenue) lines, as before; the order read of a trip's lines carries the anchor trip but no trip number. */
        var revenue = await RateLines.GetAsync(new RateLineGetRequestDto { OrderId = s.OrderId });
        revenue.Lines.ShouldHaveSingleItem().Side.ShouldBe(RateSide.Revenue);
        (await RateLines.GetAsync(new RateLineGetRequestDto { OrderId = s.OrderId, Side = RateSide.Revenue })).Lines.ShouldHaveSingleItem().Side.ShouldBe(RateSide.Revenue);
        var tripRead = await RateLines.GetAsync(new RateLineGetRequestDto { TripId = s.FirstTrip.Id, Side = RateSide.Revenue });
        tripRead.Lines.Count.ShouldBe(2, "a trip's lines are all cost; the side is ignored");
        tripRead.Lines.ShouldAllBe(l => l.AnchorTripId == s.FirstTrip.Id && l.TripNumber == null && l.LegOrderLineId == null);
    }

    [Fact]
    public async Task An_order_whose_legs_are_on_no_trip_has_no_purchase_prices()
    {
        using var _ = InTenantA();
        var a = await PlaceAsync("A" + TransportRun.TransportRunFixture.NewSuffix(), "Alpha", 52.0, 5.0);
        var b = await PlaceAsync("B" + TransportRun.TransportRunFixture.NewSuffix(), "Beta", 52.1, 5.1);
        var orderId = await ConfirmedOrderAsync(a.RelationId, LineOf(2, 200, StopAt(a, Load, Day), StopAt(b, Unload, Day)));

        var set = await RateLines.GetAsync(new RateLineGetRequestDto { OrderId = orderId, Side = RateSide.Cost });

        set.Lines.ShouldBeEmpty();
        set.Totals.Total.ShouldBe(0m);
        set.State.ShouldBeNull();
    }

    [Fact]
    public async Task A_manual_purchase_line_names_trip_freight_item_and_leg_and_is_refused_when_the_leg_is_not_on_that_trip_or_not_of_that_item()
    {
        using var _ = InTenantA();
        var s = await ScenarioAsync();

        async Task<string> RefusedAsync(Guid tripId, Guid? orderLineId, Guid? legId)
            => (await Should.ThrowAsync<BusinessException>(() => RateLines.CreateManualAsync(new ManualRateLineCreateDto
            {
                TripId = tripId, OrderLineId = orderLineId, LegId = legId, Description = "Waiting time", Quantity = 1m, UnitPrice = 35m
            }))).Code!;

        (await RefusedAsync(s.FirstTrip.Id, s.SecondLineId, s.SecondLegId)).ShouldBe(FinancialErrorCodes.LegNotOnTrip, "the second item's leg is on the second trip");
        (await RefusedAsync(s.FirstTrip.Id, s.SecondLineId, s.FirstLegId)).ShouldBe(FinancialErrorCodes.LegNotOnTrip, "the leg is not of that freight item");
        (await RefusedAsync(s.FirstTrip.Id, null, s.SecondLegId)).ShouldBe(FinancialErrorCodes.LegNotOnTrip, "every leg-anchored trip line is checked");
        (await RefusedAsync(s.FirstTrip.Id, s.FirstLineId, Guid.NewGuid())).ShouldBe(FinancialErrorCodes.LegNotOnTrip);
        (await RefusedAsync(s.FirstTrip.Id, s.FirstLineId, null)).ShouldBe(FinancialErrorCodes.AnchorRequired, "an order line alone is no anchor of a trip");
        FinancialErrorCodes.ConflictCodes.ShouldContain(FinancialErrorCodes.LegNotOnTrip, "travels as HTTP 409");
        (await Lines.GetListAsync(l => l.AnchorTripId == s.FirstTrip.Id)).ShouldBeEmpty("nothing was written");

        var created = await RateLines.CreateManualAsync(new ManualRateLineCreateDto
        {
            TripId = s.FirstTrip.Id, OrderLineId = s.FirstLineId, LegId = s.FirstLegId, Description = "Waiting time", Quantity = 2m, UnitPrice = 35m, QuantitySource = QuantitySource.LineQuantity
        });
        (created.Side, created.IsManual, created.LegId, created.AnchorTripId, created.Amount).ShouldBe((RateSide.Cost, true, (Guid?)s.FirstLegId, (Guid?)s.FirstTrip.Id, 70m));

        var set = await RateLines.GetAsync(new RateLineGetRequestDto { OrderId = s.OrderId, Side = RateSide.Cost });
        var line = set.Lines.ShouldHaveSingleItem();
        (line.Id, line.TripNumber, line.LegOrderLineId).ShouldBe((created.Id, s.FirstTrip.Number, (Guid?)s.FirstLineId));

        /* The manual purchase line can be removed; a calculated one cannot. */
        var calculated = Cost(TenantA, RateLineAnchor.Leg(s.SecondTrip.Id, s.SecondLegId), 1m, 10m, 1);
        await Lines.InsertAsync(calculated, autoSave: true);
        (await Should.ThrowAsync<BusinessException>(() => RateLines.DeleteAsync(calculated.Id))).Code.ShouldBe(FinancialErrorCodes.CalculatedLineReadOnly);
        await RateLines.DeleteAsync(created.Id);
        (await RateLines.GetAsync(new RateLineGetRequestDto { OrderId = s.OrderId, Side = RateSide.Cost })).Lines.Select(l => l.Id).ShouldBe([calculated.Id]);
    }

    [Fact]
    public async Task Another_tenant_reads_no_purchase_prices_and_cannot_anchor_a_line_on_the_leg()
    {
        Scenario s;
        using (InTenantA())
        {
            s = await ScenarioAsync();
            await SeedLinesAsync(s, TenantA);
        }

        using (InTenantB())
        {
            var set = await RateLines.GetAsync(new RateLineGetRequestDto { OrderId = s.OrderId, Side = RateSide.Cost });
            set.Lines.ShouldBeEmpty();
            set.Totals.Total.ShouldBe(0m);
            (await Should.ThrowAsync<BusinessException>(() => RateLines.CreateManualAsync(new ManualRateLineCreateDto
            {
                TripId = s.FirstTrip.Id, OrderLineId = s.FirstLineId, LegId = s.FirstLegId, Quantity = 1m, UnitPrice = 1m
            }))).Code.ShouldBe(FinancialErrorCodes.LegNotOnTrip, "tenant A's leg does not exist for tenant B");
        }

        using (InTenantA())
        {
            (await Lines.GetListAsync(l => l.AnchorTripId == s.FirstTrip.Id)).Count.ShouldBe(2, "tenant B wrote nothing");
        }
    }
}
