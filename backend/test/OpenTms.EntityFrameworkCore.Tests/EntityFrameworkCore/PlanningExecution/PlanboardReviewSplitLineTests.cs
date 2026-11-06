using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial.Tariffs.Calculators;
using OpenTms.Orders;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.PlanningExecution.ViaDepots;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// Design 74 §7 on SQLite: the split of an order line (<c>POST leg/{id}/split-order-line</c>). Risk class: Critical (the quantities
/// are what revenue and cost are calculated from, and the order's facts change): the atomicity and the tenant isolation are proven
/// on PostgreSQL in <see cref="OpenTms.PostgreSql.PlanningExecution.PlanboardReviewPostgreSqlTests"/>.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class PlanboardReviewSplitLineTests : PlanboardReviewTestBase
{
    private const double Tolerance = 0.000001;

    [Theory]
    [InlineData("10", "3", 1000d, "3", "7")]
    [InlineData("10", "3.3333", 1000d, "3.3333", "6.6667")]
    [InlineData("10", "3.33335", 1000d, "3.3334", "6.6666")]
    [InlineData("3", "1", 100d, "1", "2")]
    [InlineData("0.03", "0.01", 0.1d, "0.01", "0.02")]
    [InlineData("7", "2.5", 33.33333d, "2.5", "4.5")]
    [InlineData("100", "33.33", 999.99999d, "33.33", "66.67")]
    [InlineData("1", "0.0001", 0.00003d, "0.0001", "0.9999")]
    public async Task The_line_splits_pro_rata_and_every_total_stays_exact(string total, string part, double weight, string expectedNew, string expectedRemaining)
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var quantity = decimal.Parse(total, System.Globalization.CultureInfo.InvariantCulture);
        var input = LineOf(quantity, weight, StopAt(f.CustomerA, Load, Day), StopAt(f.CustomerB, Unload, Day));
        input.Positions = 7.3333m;
        input.Colli = 11m;
        input.VolumeM3 = 12.34567;
        input.LoadingMeters = 13.6;
        input.LoadFactor = 0.33333;
        input.Epq = 33;
        input.Measurements = [new TransportOrderMeasurementDto { Kind = MeasurementKind.WeightKg, Position = 1, Value = 500.5 }];
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, input);
        var leg = (await LegRowsAsync(orderId)).Single();

        var result = await Legs.SplitOrderLineAsync(leg.Id, new LegSplitOrderLineDto { Quantity = decimal.Parse(part, System.Globalization.CultureInfo.InvariantCulture) });

        result.NewQuantity.ShouldBe(decimal.Parse(expectedNew, System.Globalization.CultureInfo.InvariantCulture));
        result.RemainingQuantity.ShouldBe(decimal.Parse(expectedRemaining, System.Globalization.CultureInfo.InvariantCulture));
        var order = await Orders.GetAsync(orderId);
        order.Lines.Count.ShouldBe(2);
        var old = order.Lines.Single(l => l.Id == leg.OrderLineId);
        var fresh = order.Lines.Single(l => l.Id == result.NewLineId);
        (old.Quantity + fresh.Quantity).ShouldBe(quantity);
        (old.Positions!.Value + fresh.Positions!.Value).ShouldBe(7.3333m);
        (old.Colli!.Value + fresh.Colli!.Value).ShouldBe(11m);
        (old.WeightKg!.Value + fresh.WeightKg!.Value).ShouldBe(weight, Tolerance);
        (old.VolumeM3!.Value + fresh.VolumeM3!.Value).ShouldBe(12.34567, Tolerance);
        (old.LoadingMeters!.Value + fresh.LoadingMeters!.Value).ShouldBe(13.6, Tolerance);
        (old.LoadFactor!.Value + fresh.LoadFactor!.Value).ShouldBe(0.33333, Tolerance);
        (old.Epq!.Value + fresh.Epq!.Value).ShouldBe(33, Tolerance);
        (old.Measurements.Single().Value + fresh.Measurements.Single().Value).ShouldBe(500.5, Tolerance);

        var legs = await LegRowsAsync(orderId);
        legs.Sum(l => l.Quantity).ShouldBe(quantity);
        legs.Sum(l => l.WeightKg!.Value).ShouldBe(weight, Tolerance);
    }

    [Fact]
    public async Task The_new_line_goes_last_with_new_stop_ids_and_the_same_freight_stops_and_characteristics()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var characteristic = await CharacteristicAsync("COOL");
        var input = LineOf(10, 1000, StopAt(f.CustomerA, Load, Day, reference: "LOADREF"), StopAt(f.CustomerB, Unload, Day));
        input.Remarks = "Keep cool";
        input.RequiredTransportCharacteristicIds = [characteristic];
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, input, LineOf(1, 10, StopAt(f.CustomerA, Load, Day), StopAt(f.CustomerB, Unload, Day)));
        var before = await Orders.GetAsync(orderId);
        var old = before.Lines.OrderBy(l => l.Position).First();
        var leg = (await LegRowsAsync(orderId)).Single(l => l.OrderLineId == old.Id);

        var result = await Legs.SplitOrderLineAsync(leg.Id, new LegSplitOrderLineDto { Quantity = 4 });

        var after = await Orders.GetAsync(orderId);
        var fresh = after.Lines.Single(l => l.Id == result.NewLineId);
        fresh.Position.ShouldBe(after.Lines.Max(l => l.Position));
        after.Lines.Count.ShouldBe(3);
        fresh.Remarks.ShouldBe("Keep cool");
        fresh.TransportUnitId.ShouldBe(old.TransportUnitId);
        fresh.RequiredTransportCharacteristicIds.ShouldBe([characteristic]);
        fresh.Stops.Select(s => s.Id).ShouldNotContain(id => old.Stops.Any(o => o.Id == id));
        fresh.Stops.Select(s => (s.RelationId, s.RelationAddressId, s.WaypointTypeId, s.Date, s.Reference))
            .ShouldBe(old.Stops.Select(s => (s.RelationId, s.RelationAddressId, s.WaypointTypeId, s.Date, s.Reference)));
        Requests.ReasonsFor(OrderRateLineCalculator.TargetType, orderId).ShouldContain("OrderLinesChanged", "the money follows the normal fact");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("10")]
    [InlineData("11")]
    [InlineData("0.00001")]
    [InlineData("9.99999")]
    public async Task A_quantity_that_leaves_either_part_empty_is_refused_and_changes_nothing(string quantity)
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.CustomerA, f.CustomerB));
        var before = (await LegRowsAsync(orderId)).Select(Fingerprint).ToList();
        var leg = (await LegRowsAsync(orderId)).Single();

        var refusal = await Should.ThrowAsync<BusinessException>(() => Legs.SplitOrderLineAsync(leg.Id, new LegSplitOrderLineDto { Quantity = decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture) }));

        refusal.Code.ShouldBe(OrdersErrorCodes.InvalidLineSplit);
        (await Orders.GetAsync(orderId)).Lines.ShouldHaveSingleItem().Quantity.ShouldBe(10);
        (await LegRowsAsync(orderId)).Select(Fingerprint).ShouldBe(before);
    }

    [Fact]
    public async Task Generator_owned_old_legs_stay_on_their_trips_scaled_and_clones_join_them_except_the_chosen_legs()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var c = await PlaceAsync("C1", "Cee", 52.5, 5.5);
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, LineOf(10, 1000, StopAt(f.CustomerA, Load, Day), StopAt(f.CustomerB, Unload, Day), StopAt(c, Unload, Day)));
        var legs = await LegRowsAsync(orderId);
        var t1 = await TripWithAsync(f.Type, At(6), At(18));
        var t2 = await TripWithAsync(f.Type, At(6), At(18));
        await OnTripAsync(t1, legs[0].Id);
        await OnTripAsync(t2, legs[1].Id);
        var (requests1, requests2) = (TripRequests(t1.Id), TripRequests(t2.Id));

        var result = await Legs.SplitOrderLineAsync(legs[1].Id, new LegSplitOrderLineDto { Quantity = 3 });

        result.RemainingLegs.Select(l => l.Id).ShouldBe(legs.Select(l => l.Id), ignoreOrder: true);
        result.RemainingLegs.Single(l => l.Id == legs[0].Id).TripId.ShouldBe(t1.Id);
        result.RemainingLegs.Single(l => l.Id == legs[1].Id).TripId.ShouldBe(t2.Id);
        result.RemainingLegs.ShouldAllBe(l => l.Quantity == 7 && l.WeightKg == 700);
        result.NewLegs.ShouldAllBe(l => l.Quantity == 3 && l.WeightKg == 300 && l.Source == LegSource.Generated);
        result.NewLegs.Single(l => l.Position == 1).TripId.ShouldBe(t1.Id, "the clone of a leg the planner did not choose goes on its original's trip");
        result.NewLegs.Single(l => l.Position == 2).TripId.ShouldBeNull("the chosen leg's clone stays unplanned");

        var line = (await Orders.GetAsync(orderId)).Lines.Single(l => l.Id == result.NewLineId);
        result.NewLegs.Single(l => l.Position == 1).Origin.StopId.ShouldBe(line.Stops[0].Id!.Value);
        result.NewLegs.Single(l => l.Position == 2).Destination.StopId.ShouldBe(line.Stops[2].Id!.Value);

        (await Trips.GetAsync(t1.Id)).Stops.SelectMany(s => s.Legs).Select(l => l.LegId).Distinct().Count().ShouldBe(2, "the trip was recomposed");
        (await Trips.GetAsync(t2.Id)).Stops.SelectMany(s => s.Legs).Select(l => l.LegId).Distinct().ShouldHaveSingleItem();
        TripRequests(t1.Id).ShouldBeGreaterThan(requests1);
        TripRequests(t2.Id).ShouldBeGreaterThan(requests2, "the old leg's new quantity reaches the trip that got no clone");
        result.NewLegs.ShouldAllBe(l => LegRequests(l.Id) > 0);
    }

    [Fact]
    public async Task A_clone_stays_unplanned_when_its_original_is_stuck_for_either_reason()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var c = await PlaceAsync("C2", "Cee", 52.5, 5.5);
        var d = await PlaceAsync("D2", "Dee", 52.6, 5.6);
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, LineOf(10, 1000, StopAt(f.CustomerA, Load, Day), StopAt(f.CustomerB, Unload, Day), StopAt(c, Unload, Day), StopAt(d, Unload, Day)));
        var legs = await LegRowsAsync(orderId);
        var withActuals = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), legs[0].Id);
        await Trips.RecordStopActualsAsync(withActuals.Id, withActuals.Stops[0].Id, new StopActualsDto { ActualArrival = At(7) });
        var completed = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), legs[1].Id);
        await Trips.StartAsync(completed.Id, new TripTimestampDto { At = At(6) });
        await Trips.CompleteAsync(completed.Id, new TripTimestampDto { At = At(17) });

        var result = await Legs.SplitOrderLineAsync(legs[2].Id, new LegSplitOrderLineDto { Quantity = 2 });

        result.NewLegs.Count.ShouldBe(3);
        result.NewLegs.ShouldAllBe(l => l.TripId == null);
        result.RemainingLegs.Single(l => l.Id == legs[0].Id).TripId.ShouldBe(withActuals.Id);
        result.RemainingLegs.Single(l => l.Id == legs[1].Id).TripId.ShouldBe(completed.Id);
        (await Trips.GetAsync(completed.Id)).Stops.SelectMany(s => s.Legs).Select(l => l.LegId).Distinct().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_via_depot_line_splits_through_the_generator_and_the_clones_are_via_depot_legs_too()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        await GetRequiredService<IViaDepotRuleAppService>().CreateAsync(new ViaDepotRuleCreateDto
        {
            Scope = ViaDepotScope.Customer, RelationId = f.CustomerA.RelationId, StopWaypointTypeId = Load,
            DepotRelationId = f.Depot.RelationId, DepotAddressId = f.Depot.AddressId, ArrivalWaypointTypeId = DepotIn
        });
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.CustomerA, f.CustomerB));
        var legs = await LegRowsAsync(orderId);
        legs.Count.ShouldBe(2);
        var trip = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), legs[1].Id);

        var result = await Legs.SplitOrderLineAsync(legs[0].Id, new LegSplitOrderLineDto { Quantity = 4 });

        result.NewLegs.Count.ShouldBe(2);
        result.NewLegs.ShouldAllBe(l => l.Source == LegSource.ViaDepot && l.Quantity == 4);
        result.NewLegs.Single(l => l.Position == 1).TripId.ShouldBeNull();
        result.NewLegs.Single(l => l.Position == 2).TripId.ShouldBe(trip.Id);
        var before = (await LegRowsAsync(orderId)).Select(Fingerprint).ToList();
        await RedeliverLinesChangedAsync(orderId);
        (await LegRowsAsync(orderId)).Select(Fingerprint).ShouldBe(before);
    }

    [Fact]
    public async Task A_reshaped_line_clones_with_mapped_ends_kept_synthetic_ends_scaled_quantities_and_its_source()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var x = await PlaceAsync("X1", "Hub", 52.3, 5.3);
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, LineOf(10, 1000, StopAt(f.CustomerA, Load, Day), StopAt(f.CustomerB, Unload, Day)));
        var leg = (await LegRowsAsync(orderId)).Single();
        var halves = await Legs.SplitAtAddressAsync(leg.Id, new LegSplitAtAddressDto { RelationId = x.RelationId, RelationAddressId = x.AddressId, ArrivalWaypointTypeId = DepotIn, DepartureWaypointTypeId = DepotOut });
        var t1 = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), halves[0].Id);
        var t2 = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), halves[1].Id);
        var (requests1, requests2) = (TripRequests(t1.Id), TripRequests(t2.Id));

        var result = await Legs.SplitOrderLineAsync(halves[0].Id, new LegSplitOrderLineDto { Quantity = 2.5m });

        result.NewLegs.ShouldAllBe(l => l.Source == LegSource.SplitAtAddress && l.Quantity == 2.5m && l.WeightKg == 250 && l.SplitFromLegId == null);
        result.RemainingLegs.ShouldAllBe(l => l.Quantity == 7.5m && l.WeightKg == 750 && l.Source == LegSource.SplitAtAddress);
        var line = (await Orders.GetAsync(orderId)).Lines.Single(l => l.Id == result.NewLineId);
        var (first, second) = (result.NewLegs.Single(l => l.Position == 1), result.NewLegs.Single(l => l.Position == 2));
        first.Origin.StopId.ShouldBe(line.Stops[0].Id!.Value);
        second.Destination.StopId.ShouldBe(line.Stops[1].Id!.Value);
        first.Destination.StopId.ShouldBe(halves[0].Destination.StopId, "the synthetic end stays");
        second.Origin.StopId.ShouldBe(halves[1].Origin.StopId, "the synthetic end stays");
        first.TripId.ShouldBeNull("the chosen leg's clone stays unplanned");
        second.TripId.ShouldBe(t2.Id);
        TripRequests(t1.Id).ShouldBeGreaterThan(requests1, "the original's new quantity must reach the trip that got no clone");
        TripRequests(t2.Id).ShouldBeGreaterThan(requests2);
        (await Trips.GetAsync(t2.Id)).Stops.SelectMany(s => s.Legs).Select(l => l.LegId).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task A_line_with_a_part_split_or_a_return_leg_keeps_those_legs_and_each_clone_keeps_its_source()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, LineOf(10, 1000, StopAt(f.CustomerA, Load, Day), StopAt(f.CustomerB, Unload, Day)));
        var leg = (await LegRowsAsync(orderId)).Single();
        await Legs.SplitLineAsync(leg.Id, new LegSplitLineDto { Quantity = 3 });
        await Legs.ReturnLegAsync(leg.Id);
        var originals = await LegRowsAsync(orderId);
        originals.Count.ShouldBe(3);

        var result = await Legs.SplitOrderLineAsync(leg.Id, new LegSplitOrderLineDto { Quantity = 5 });

        result.NewLegs.Select(l => l.Source).OrderBy(s => s).ShouldBe(originals.Select(l => l.Source).OrderBy(s => s));
        foreach (var original in originals)
        {
            var remaining = result.RemainingLegs.Single(l => l.Id == original.Id);
            result.NewLegs.Where(l => l.Source == original.Source && l.Position == original.Position).Sum(l => l.Quantity)
                .ShouldBeGreaterThan(0);
            remaining.Quantity.ShouldBeLessThan(original.Quantity);
        }

        result.NewLegs.Sum(l => l.Quantity).ShouldBe(originals.Sum(l => l.Quantity) / 2);
        var before = (await LegRowsAsync(orderId)).Select(Fingerprint).ToList();
        await RedeliverLinesChangedAsync(orderId);
        (await LegRowsAsync(orderId)).Select(Fingerprint).ShouldBe(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_later_delivery_of_the_lines_changed_fact_changes_nothing(bool reshaped)
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var x = await PlaceAsync("X3", "Hub", 52.3, 5.3);
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, LineOf(3, 100, StopAt(f.CustomerA, Load, Day), StopAt(f.CustomerB, Unload, Day)));
        var leg = (await LegRowsAsync(orderId)).Single();
        var chosen = reshaped
            ? (await Legs.SplitAtAddressAsync(leg.Id, new LegSplitAtAddressDto { RelationId = x.RelationId, RelationAddressId = x.AddressId, ArrivalWaypointTypeId = DepotIn, DepartureWaypointTypeId = DepotOut }))[0]
            : leg;
        var trip = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), chosen.Id);
        await Legs.SplitOrderLineAsync(chosen.Id, new LegSplitOrderLineDto { Quantity = 1 });
        var before = (await LegRowsAsync(orderId)).Select(Fingerprint).ToList();
        var requests = TripRequests(trip.Id);
        var legRequests = (await LegRowsAsync(orderId)).Sum(l => LegRequests(l.Id));

        await RedeliverLinesChangedAsync(orderId);
        await RedeliverLinesChangedAsync(orderId);

        (await LegRowsAsync(orderId)).Select(Fingerprint).ShouldBe(before);
        TripRequests(trip.Id).ShouldBe(requests);
        (await LegRowsAsync(orderId)).Sum(l => LegRequests(l.Id)).ShouldBe(legRequests);
    }

    [Fact]
    public async Task Another_tenant_cannot_split_the_line()
    {
        Guid legId;
        using (InTenantA())
        {
            var f = await FleetAsync();
            var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.CustomerA, f.CustomerB));
            legId = (await LegRowsAsync(orderId)).Single().Id;
        }

        using (InTenantB())
        {
            await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(() => Legs.SplitOrderLineAsync(legId, new LegSplitOrderLineDto { Quantity = 1 }));
        }
    }
}
