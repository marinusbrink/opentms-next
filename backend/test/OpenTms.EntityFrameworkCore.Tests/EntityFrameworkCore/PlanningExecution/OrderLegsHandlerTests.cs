using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData.Relations;
using OpenTms.Orders.Events;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.PlanningExecution.ViaDepots;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// Planning's reaction to the Orders facts on SQLite (design 59 "Generation"; orders.md 18–28 corrected; trips.md 52):
/// legs on confirm, via-depot rules, regeneration after stop changes (unassign + warning), cancellation, and the
/// "Tevens trip" request honoured once. The orders are real Orders rows: the Orders service publishes the facts and
/// SQLite delivers them in process, so the handler under test runs on the real published readers.
/// Risk class: Critical (idempotency) / High (trip request).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class OrderLegsHandlerTests : PlanningAppServiceTestBase
{
    private TransportOrderLineInputDto Line((Guid, Guid) from, (Guid, Guid) to, decimal quantity = 10, int day = 26)
        => LineOf(quantity, 1000,
            StopAt(from, Load, new DateOnly(2026, 10, day), At(8, 0, day), At(10, 0, day)),
            StopAt(to, Unload, new DateOnly(2026, 10, day), At(12, 0, day), At(14, 0, day)));

    [Fact]
    public async Task Confirming_an_order_generates_one_leg_per_stop_pair_and_confirming_again_changes_nothing()
    {
        using var _ = InTenantA();
        var a = await PlaceAsync("A", "A", 52.0, 5.0);
        var b = await PlaceAsync("B", "B", 52.1, 5.1);
        var c = await PlaceAsync("C", "C", 52.2, 5.2);
        var order = await OrderAsync(a.RelationId,
            LineOf(10, 1000, StopAt(a, Load, Day), StopAt(b, Unload, Day), StopAt(c, Unload, Day.AddDays(1))),
            LineOf(2, 200, StopAt(a, Load, Day), StopAt(c, Unload, Day)));
        var firstLineId = order.Lines.Single(l => l.Position == 1).Id!.Value;
        (await LegsOfOrderAsync(order.Id)).ShouldBeEmpty("a Draft has no legs: its line saves generate nothing (design 59)");

        var orderId = await ConfirmOrderAsync(order);

        var legs = (await LegsOfOrderAsync(orderId)).OrderBy(l => l.OrderLineId == firstLineId ? 0 : 1).ThenBy(l => l.Position).ToList();
        legs.Count.ShouldBe(3);
        legs[0].OriginName.ShouldBe("A");
        legs[0].DestinationName.ShouldBe("B");
        legs[1].OriginName.ShouldBe("B");
        legs[1].DestinationName.ShouldBe("C");
        legs[1].DestinationPlannedDate.ShouldBe(Day.AddDays(1));
        legs[2].Quantity.ShouldBe(2);
        legs.ShouldAllBe(l => l.TenantId == TenantA && l.CustomerRelationId == a.RelationId && l.TripId == null && l.Source == LegSource.Generated);
        Requests.All.Count(r => r.TargetType == PlanningExecutionApplicationModule.LegTargetType && legs.Select(l => l.Id).Contains(r.TargetId)).ShouldBe(3);

        var before = Requests.All.Count;
        await Handler.HandleEventAsync(new TransportOrderConfirmedEto { OrderId = orderId });
        await Handler.HandleEventAsync(new TransportOrderLinesChangedEto { OrderId = orderId });
        (await LegsOfOrderAsync(orderId)).Select(l => l.Id).ShouldBe(legs.Select(l => l.Id), ignoreOrder: true);
        Requests.All.Count.ShouldBe(before, "an unchanged order requests nothing");

        var unplanned = await Legs.GetListAsync(LegList(orderId: orderId));
        unplanned.Rows.Count.ShouldBe(3);
        unplanned.Rows.ShouldAllBe(r => r.OrderNumber != null && r.CustomerName == "A");
    }

    [Fact]
    public async Task A_via_depot_rule_splits_the_pair_at_the_depot_and_the_rule_list_names_the_master_data()
    {
        using var _ = InTenantA();
        var depot = await PlaceAsync("DEPOT", "Depot", 52.09, 5.12, RelationRoles.Depot, "Utrecht");
        var a = await PlaceAsync("A", "A", 52.0, 5.0);
        var b = await PlaceAsync("B", "B", 52.5, 5.5);
        var rules = GetRequiredService<IViaDepotRuleAppService>();
        var rule = await rules.CreateAsync(new ViaDepotRuleCreateDto
        {
            Scope = ViaDepotScope.Customer,
            RelationId = a.RelationId,
            DepotRelationId = depot.RelationId,
            DepotAddressId = depot.AddressId,
            ArrivalWaypointTypeId = DepotIn,
            DepartureWaypointTypeId = DepotOut,
            Overnight = true
        });
        rule.DepotRelationName.ShouldBe("Depot");
        rule.DepotAddressCity.ShouldBe("Utrecht");
        rule.RelationName.ShouldBe("A");

        var orderId = await ConfirmedOrderAsync(a.RelationId, Line(a, b));

        var legs = (await LegsOfOrderAsync(orderId)).OrderBy(l => l.Position).ToList();
        legs.Count.ShouldBe(2);
        legs[0].DestinationRelationId.ShouldBe(depot.RelationId);
        legs[0].DestinationWaypointTypeId.ShouldBe(DepotIn);
        legs[0].DestinationCity.ShouldBe("Utrecht");
        legs[0].DestinationLatitude.ShouldBe(52.09);
        legs[0].IsViaLeg.ShouldBeTrue();
        legs[0].ViaDepotRuleId.ShouldBe(rule.Id);
        legs[1].OriginStopId.ShouldBe(legs[0].DestinationStopId);
        legs[1].OriginWaypointTypeId.ShouldBe(DepotOut);
        legs[1].OriginPlannedDate.ShouldBe(Day.AddDays(1));

        /* Another customer: no rule, one leg. */
        var other = await ConfirmedOrderAsync(b.RelationId, Line(a, b));
        (await LegsOfOrderAsync(other)).ShouldHaveSingleItem();

        /* The rule archived: regeneration of the first order drops the via legs for a direct one. */
        var detail = await rules.GetAsync(rule.Id);
        await rules.UpdateAsync(rule.Id, new ViaDepotRuleUpdateDto
        {
            Scope = detail.Scope, RelationId = detail.RelationId, DepotRelationId = detail.DepotRelationId, DepotAddressId = detail.DepotAddressId,
            ArrivalWaypointTypeId = detail.ArrivalWaypointTypeId, DepartureWaypointTypeId = detail.DepartureWaypointTypeId, Overnight = detail.Overnight,
            Position = detail.Position, IsArchived = true, ConcurrencyStamp = detail.ConcurrencyStamp
        });
        await Handler.HandleEventAsync(new TransportOrderStopsChangedEto { OrderId = orderId });
        (await LegsOfOrderAsync(orderId)).ShouldHaveSingleItem().Source.ShouldBe(LegSource.Generated);
    }

    [Fact]
    public async Task A_rule_without_departure_type_departs_the_depot_with_the_arrival_types_successor()
    {
        using var _ = InTenantA();
        var depot = await PlaceAsync("DEPOT", "Depot", 52.09, 5.12, RelationRoles.Depot, "Utrecht");
        var a = await PlaceAsync("A", "A", 52.0, 5.0);
        var b = await PlaceAsync("B", "B", 52.5, 5.5);
        await GetRequiredService<IViaDepotRuleAppService>().CreateAsync(new ViaDepotRuleCreateDto
        {
            Scope = ViaDepotScope.Customer, RelationId = a.RelationId, DepotRelationId = depot.RelationId, DepotAddressId = depot.AddressId, ArrivalWaypointTypeId = DepotIn
        });

        var orderId = await ConfirmedOrderAsync(a.RelationId, Line(a, b));

        var legs = (await LegsOfOrderAsync(orderId)).OrderBy(l => l.Position).ToList();
        legs.Count.ShouldBe(2);
        legs[0].DestinationWaypointTypeId.ShouldBe(DepotIn);
        legs[1].OriginWaypointTypeId.ShouldBe(DepotOut, "DEPOTIN's successive waypoint type (design 59), read through Orders' published lookup");
    }

    [Fact]
    public async Task A_changed_stop_takes_the_leg_off_its_trip_rebuilds_the_stops_and_warns_and_a_cancelled_order_loses_its_legs()
    {
        using var _ = InTenantA();
        var type = await TripTypeAsync();
        var a = await PlaceAsync("A", "A", 52.0, 5.0);
        var b = await PlaceAsync("B", "B", 52.1, 5.1);
        var b2 = await PlaceAsync("B2", "B two", 52.3, 5.3);
        var order = await OrderAsync(a.RelationId, Line(a, b));
        var orderId = await ConfirmOrderAsync(order);
        var legId = (await LegIdsOfOrderAsync(orderId)).Single();
        var trip = await CreateTripAsync(type, At(6), At(18));
        trip = await Trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = [legId] });
        trip.Stops.Count.ShouldBe(2);

        /* The destination moves to another address: the leg is unassigned, the trip rebuilt and warned. */
        var reloaded = await Orders.GetAsync(orderId);
        var line = LinesOf(reloaded).Single();
        line.Stops![1].RelationId = b2.RelationId;
        line.Stops[1].RelationAddressId = b2.AddressId;
        await Orders.UpdateLineAsync(orderId, line.Id!.Value, line);

        var leg = (await LegsOfOrderAsync(orderId)).Single();
        leg.Id.ShouldBe(legId, "the key did not change, so the leg kept its id");
        leg.TripId.ShouldBeNull();
        leg.DestinationName.ShouldBe("B two");
        trip = await Trips.GetAsync(trip.Id);
        trip.Stops.ShouldBeEmpty();
        trip.Warnings.ShouldContain(w => w.Code == TripWarningCode.LegChangedAfterPlanning && w.LegId == legId);
        Requests.For(PlanningExecutionApplicationModule.TripTargetType, trip.Id).ShouldContain(r => r.Reason == "LegChangedAfterPlanning");

        /* Replan, then cancel the order: the leg leaves the trip and is deleted. */
        await Trips.AssignLegsAsync(trip.Id, new AssignLegsDto { LegIds = [legId] });
        await Orders.CancelAsync(orderId);
        (await LegsOfOrderAsync(orderId)).ShouldBeEmpty();
        (await Trips.GetAsync(trip.Id)).Stops.ShouldBeEmpty();

        /* Delivered again (at least once): harmless. */
        await Handler.HandleEventAsync(new TransportOrderCancelledEto { OrderId = orderId });
        (await LegsOfOrderAsync(orderId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_removed_line_deletes_its_legs_and_an_order_the_reader_no_longer_has_deletes_everything()
    {
        using var _ = InTenantA();
        var a = await PlaceAsync("A", "A", 52.0, 5.0);
        var b = await PlaceAsync("B", "B", 52.1, 5.1);
        var order = await OrderAsync(a.RelationId, Line(a, b), Line(b, a, quantity: 3));
        var orderId = await ConfirmOrderAsync(order);
        (await LegsOfOrderAsync(orderId)).Count.ShouldBe(2);

        await Orders.DeleteLineAsync(orderId, order.Lines.Single(l => l.Position == 2).Id!.Value);
        (await LegsOfOrderAsync(orderId)).ShouldHaveSingleItem().OrderLineId.ShouldBe(order.Lines.Single(l => l.Position == 1).Id!.Value);

        /* The order row is gone (whatever the fact that removed it): the next regeneration finds no snapshot and clears the legs. */
        await Run.RemoveWithoutEventsAsync(orderId);
        await Handler.HandleEventAsync(new TransportOrderStopsChangedEto { OrderId = orderId });
        (await LegsOfOrderAsync(orderId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_trip_request_creates_the_trip_once_with_all_legs_and_publishes_the_agreed_price()
    {
        using var _ = InTenantA();
        var type = await TripTypeAsync();
        var depot = await PlaceAsync("DEPOT", "Depot", 52.09, 5.12, RelationRoles.Depot | RelationRoles.OwnCompany);
        var a = await PlaceAsync("A", "A", 52.0, 5.0);
        var carrier = await PlaceAsync("CARR", "Carrier BV", 52, 5, RelationRoles.Carrier);
        var request = new TransportOrderTripRequestInputDto
        {
            TransportTripTypeId = type,
            DepartureRelationId = depot.RelationId,
            DepartureAddressId = depot.AddressId,
            ArrivalRelationId = depot.RelationId,
            ArrivalAddressId = depot.AddressId,
            PlannedDeparture = At(6),
            PlannedArrival = At(18),
            CarrierRelationId = carrier.RelationId,
            AgreedPurchaseAmount = 450m,
            AgreedPurchaseCurrencyCode = "EUR"
        };
        var order = await OrderAsync(a.RelationId, request, Line(depot, a), Line(depot, a, quantity: 2));
        GetRequiredService<TripCreatedRecorder>().Created.Clear();

        var orderId = await ConfirmOrderAsync(order);

        var trips = await TripRepository.GetListAsync(t => t.CreatedFromOrderId == orderId);
        var trip = trips.ShouldHaveSingleItem();
        trip.Number.ShouldBe($"TR-{Run.Today.Year}-00001");
        trip.CarrierRelationId.ShouldBe(carrier.RelationId);
        trip.Execution.ShouldBe(TripExecution.Subcontracted, "a carrier means subcontracted");
        trip.DepartureRelationId.ShouldBe(depot.RelationId);
        (await LegsOfOrderAsync(orderId)).ShouldAllBe(l => l.TripId == trip.Id);
        var detail = await Trips.GetAsync(trip.Id);
        detail.Stops.Count.ShouldBe(2, "two loads at the depot and two unloads at A merge");
        var eto = GetRequiredService<TripCreatedRecorder>().Created.ShouldHaveSingleItem();
        eto.CreatedFromOrderId.ShouldBe(orderId);
        eto.AgreedPurchaseAmount.ShouldBe(450m);
        eto.AgreedPurchaseCurrencyCode.ShouldBe("EUR");
        (await Orders.GetAsync(orderId)).TripRequest.ShouldNotBeNull().TripId.ShouldBe(trip.Id, "Orders recorded the created trip on the request");

        /* Delivered again, and later edits: the same trip, nothing new. */
        await Handler.HandleEventAsync(new TransportOrderConfirmedEto { OrderId = orderId });
        await Handler.HandleEventAsync(new TransportOrderStopsChangedEto { OrderId = orderId });
        (await TripRepository.GetListAsync(t => t.CreatedFromOrderId == orderId)).ShouldHaveSingleItem();
        GetRequiredService<TripCreatedRecorder>().Created.ShouldHaveSingleItem();

        /* A request Orders already marked as handled is skipped too. */
        var another = await OrderAsync(a.RelationId, request, Line(depot, a));
        await Run.RecordTripAsync(another.Id, Guid.NewGuid());
        await ConfirmOrderAsync(another);
        (await TripRepository.GetListAsync(t => t.CreatedFromOrderId == another.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_trip_request_Planning_cannot_resolve_is_not_honoured_and_the_legs_stay_unplanned_instead_of_failing_the_event()
    {
        using var _ = InTenantA();
        await TripTypeAsync();
        var depot = await PlaceAsync("DEPOT", "Depot", 52.09, 5.12, RelationRoles.Depot);
        var a = await PlaceAsync("A", "A", 52.0, 5.0);
        var request = new TransportOrderTripRequestInputDto
        {
            TransportTripTypeId = Guid.NewGuid(), DepartureRelationId = depot.RelationId, DepartureAddressId = depot.AddressId,
            ArrivalRelationId = depot.RelationId, ArrivalAddressId = depot.AddressId, PlannedDeparture = At(6)
        };
        var order = await OrderAsync(a.RelationId, request, Line(depot, a));

        var orderId = await ConfirmOrderAsync(order);

        (await TripRepository.GetListAsync(t => t.CreatedFromOrderId == orderId)).ShouldBeEmpty();
        (await LegsOfOrderAsync(orderId)).ShouldHaveSingleItem().TripId.ShouldBeNull("the leg is generated and stays unplanned");
        var tripRequest = (await Orders.GetAsync(orderId)).TripRequest.ShouldNotBeNull();
        tripRequest.TripId.ShouldBeNull();

        /* Not silent: Planning's fact reached Orders, and the order carries a warning the screens show (design 59 build note 13). */
        tripRequest.WarningCode.ShouldBe(OpenTms.Orders.OrdersErrorCodes.TripRequestNotHonoured);
        tripRequest.WarningField.ShouldBe("transportTripTypeId");
        tripRequest.WarningAt.ShouldNotBeNull();
        (await Orders.GetListAsync(new TransportOrderListRequestDto())).Rows.Single(r => r.Id == orderId).TripRequestWarningCode
            .ShouldBe(OpenTms.Orders.OrdersErrorCodes.TripRequestNotHonoured);

        /* Delivered again: still one warning, still no trip; the order's later edits of the request keep the confirm's warning. */
        await Handler.HandleEventAsync(new TransportOrderConfirmedEto { OrderId = orderId });
        (await TripRepository.GetListAsync(t => t.CreatedFromOrderId == orderId)).ShouldBeEmpty();
        var again = (await Orders.GetAsync(orderId)).TripRequest.ShouldNotBeNull();
        again.WarningCode.ShouldBe(OpenTms.Orders.OrdersErrorCodes.TripRequestNotHonoured);
        again.WarningAt.ShouldBe(tripRequest.WarningAt);
    }

    [Fact]
    public async Task An_honoured_trip_request_carries_no_warning()
    {
        using var _ = InTenantA();
        var type = await TripTypeAsync();
        var depot = await PlaceAsync("DEPOT", "Depot", 52.09, 5.12, RelationRoles.Depot);
        var a = await PlaceAsync("A", "A", 52.0, 5.0);
        var order = await OrderAsync(a.RelationId, new TransportOrderTripRequestInputDto
        {
            TransportTripTypeId = type, DepartureRelationId = depot.RelationId, DepartureAddressId = depot.AddressId,
            ArrivalRelationId = depot.RelationId, ArrivalAddressId = depot.AddressId, PlannedDeparture = At(6)
        }, Line(depot, a));

        var orderId = await ConfirmOrderAsync(order);

        var tripRequest = (await Orders.GetAsync(orderId)).TripRequest.ShouldNotBeNull();
        tripRequest.TripId.ShouldNotBeNull();
        tripRequest.WarningCode.ShouldBeNull();
        tripRequest.WarningField.ShouldBeNull();
    }

    [Fact]
    public async Task Legs_of_one_tenant_are_invisible_to_another()
    {
        Guid orderId;
        using (InTenantA())
        {
            var a = await PlaceAsync("A", "A", 52.0, 5.0);
            var b = await PlaceAsync("B", "B", 52.1, 5.1);
            orderId = await ConfirmedOrderAsync(a.RelationId, Line(a, b));
            (await LegsOfOrderAsync(orderId)).ShouldHaveSingleItem();
        }

        using var _ = InTenantB();
        (await LegsOfOrderAsync(orderId)).ShouldBeEmpty();
        (await Legs.GetListAsync(LegList(orderId: orderId))).Rows.ShouldBeEmpty();
        /* Tenant B confirming the same id regenerates nothing: Orders' reader knows no such order for B. */
        await Handler.HandleEventAsync(new TransportOrderConfirmedEto { OrderId = orderId });
        (await LegsOfOrderAsync(orderId)).ShouldBeEmpty();
    }
}

/// <summary>Records the trip-created facts (SQLite delivers events in process).</summary>
public class TripCreatedRecorder : Volo.Abp.EventBus.Distributed.IDistributedEventHandler<OpenTms.PlanningExecution.Events.TransportTripCreatedEto>, Volo.Abp.DependencyInjection.ISingletonDependency
{
    public List<OpenTms.PlanningExecution.Events.TransportTripCreatedEto> Created { get; } = new();

    public Task HandleEventAsync(OpenTms.PlanningExecution.Events.TransportTripCreatedEto eventData)
    {
        lock (Created)
        {
            Created.Add(eventData);
        }

        return Task.CompletedTask;
    }
}
