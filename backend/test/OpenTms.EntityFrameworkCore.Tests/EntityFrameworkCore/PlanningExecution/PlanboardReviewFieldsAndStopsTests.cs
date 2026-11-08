using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial.Tariffs.Calculators;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using OpenTms.Orders;
using OpenTms.Orders.Events;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Events;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.Planboard;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// Design 74 on SQLite: §3 the stop window's backend, §6 the inline edits (the single leg on a final trip, Orders' two field endpoints,
/// <c>RefreshReshapedLine</c>) and §9 the work after a save. Risk class: High (planning, order facts); the requests to Financial's rate
/// job are checked here because they are what starts the money calculation.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class PlanboardReviewFieldsAndStopsTests : PlanboardReviewTestBase
{
    private int RateRequests(Guid tripId) => Requests.ReasonsFor(FinancialRateLineTypes.Trip, tripId).Count;

    private static class FinancialRateLineTypes
    {
        public const string Trip = TripRateLineCalculator.TargetType;
    }

    private async Task<(FleetSet F, Guid OrderId, LegDto Leg, TransportTripDto Trip)> OneLegOnTripAsync()
    {
        var f = await FleetAsync();
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.CustomerA, f.CustomerB));
        var leg = (await LegRowsAsync(orderId)).Single();
        var trip = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), leg.Id);
        return (f, orderId, leg, trip);
    }

    private async Task CompleteAsync(Guid tripId)
    {
        await Trips.StartAsync(tripId, new TripTimestampDto { At = At(6) });
        await Trips.CompleteAsync(tripId, new TripTimestampDto { At = At(17) });
    }

    private async Task<string> StampAsync(Guid orderId) => (await Orders.GetAsync(orderId)).ConcurrencyStamp;

    /* §3 */

    [Fact]
    public async Task A_stop_without_legs_changes_relation_and_address_and_that_requests_the_recalculation()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var trip = await TripWithAsync(f.Type, At(6), At(18));
        var stop = (await Trips.AddStopAsync(trip.Id, new AddManualStopDto { WaypointTypeId = Load, RelationId = f.CustomerA.RelationId, RelationAddressId = f.CustomerA.AddressId })).Stops.Single();
        var requests = TripRequests(trip.Id);

        var moved = (await Trips.UpdateStopAsync(trip.Id, stop.Id, new UpdateStopDto { RelationId = f.Depot.RelationId })).Stops.Single();

        moved.RelationId.ShouldBe(f.Depot.RelationId);
        moved.RelationAddressId.ShouldBe(f.Depot.AddressId, "a new relation without an address takes the relation's first");
        moved.City.ShouldBe("Utrecht");
        moved.Name.ShouldBe("Depot Utrecht");
        TripRequests(trip.Id).ShouldBe(requests + 1);
    }

    [Fact]
    public async Task An_address_of_another_relation_or_tenant_is_refused()
    {
        var foreign = default((Guid RelationId, Guid AddressId));
        using (InTenantB())
        {
            foreign = await PlaceAsync("FOREIGN", "Foreign", 52.1, 5.1);
        }

        using var _ = InTenantA();
        var f = await FleetAsync();
        var trip = await TripWithAsync(f.Type, At(6), At(18));
        var stop = (await Trips.AddStopAsync(trip.Id, new AddManualStopDto { WaypointTypeId = Load, RelationId = f.CustomerA.RelationId, RelationAddressId = f.CustomerA.AddressId })).Stops.Single();

        (await Should.ThrowAsync<BusinessException>(() => Trips.UpdateStopAsync(trip.Id, stop.Id, new UpdateStopDto { RelationAddressId = f.CustomerB.AddressId })))
            .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => Trips.UpdateStopAsync(trip.Id, stop.Id, new UpdateStopDto { RelationId = foreign.RelationId, RelationAddressId = foreign.AddressId })))
            .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Trips.GetAsync(trip.Id)).Stops.Single().RelationId.ShouldBe(f.CustomerA.RelationId);
    }

    [Fact]
    public async Task A_stop_with_legs_refuses_a_change_of_place_but_accepts_its_own_values_again()
    {
        using var _ = InTenantA();
        var (f, _, _, trip) = await OneLegOnTripAsync();
        var stop = trip.Stops[0];

        (await Should.ThrowAsync<BusinessException>(() => Trips.UpdateStopAsync(trip.Id, stop.Id, new UpdateStopDto { RelationId = f.Depot.RelationId })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.StopHasLegs);
        (await Should.ThrowAsync<BusinessException>(() => Trips.UpdateStopAsync(trip.Id, stop.Id, new UpdateStopDto { RelationAddressId = Guid.NewGuid() })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.StopHasLegs);

        var same = await Trips.UpdateStopAsync(trip.Id, stop.Id, new UpdateStopDto { RelationId = stop.RelationId, RelationAddressId = stop.RelationAddressId, Remarks = "same place" });
        same.Stops.Single(s => s.Id == stop.Id).Remarks.ShouldBe("same place");
    }

    [Fact]
    public async Task A_stop_with_actuals_keeps_its_place()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var trip = await TripWithAsync(f.Type, At(6), At(18));
        var stop = (await Trips.AddStopAsync(trip.Id, new AddManualStopDto { WaypointTypeId = Load, RelationId = f.CustomerA.RelationId, RelationAddressId = f.CustomerA.AddressId })).Stops.Single();
        await Trips.RecordStopActualsAsync(trip.Id, stop.Id, new StopActualsDto { ActualArrival = At(7) });
        (await Should.ThrowAsync<BusinessException>(() => Trips.UpdateStopAsync(trip.Id, stop.Id, new UpdateStopDto { RelationId = f.Depot.RelationId })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.StopHasActuals);
    }

    [Fact]
    public async Task Remarks_alone_request_no_recalculation_a_changed_stay_does()
    {
        using var _ = InTenantA();
        var (_, _, _, trip) = await OneLegOnTripAsync();
        var stop = trip.Stops[0];
        var requests = TripRequests(trip.Id);

        await Trips.UpdateStopAsync(trip.Id, stop.Id, new UpdateStopDto { Remarks = "gate 4" });
        TripRequests(trip.Id).ShouldBe(requests);

        await Trips.UpdateStopAsync(trip.Id, stop.Id, new UpdateStopDto { Remarks = "gate 4", PlannedDurationMin = 95 });
        TripRequests(trip.Id).ShouldBe(requests + 1);
    }

    [Fact]
    public async Task A_stop_remarks_edit_starts_no_rate_job_either()
    {
        using var _ = InTenantA();
        var (_, _, _, trip) = await OneLegOnTripAsync();
        var requests = RateRequests(trip.Id);

        await Trips.UpdateStopAsync(trip.Id, trip.Stops[0].Id, new UpdateStopDto { Remarks = "gate 4" });

        RateRequests(trip.Id).ShouldBe(requests, "design 74 §3 and §9: a text edit is nothing a rate line depends on");
    }

    [Fact]
    public async Task The_stop_carries_the_address_the_relations_contact_details_and_its_legs_ends()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var relation = await GetRequiredService<IRelationAppService>().CreateAsync(new RelationCreateDto
        {
            Code = "CONTACT", Name = "Contact Ltd", Roles = RelationRoles.Customer, Phone = "+31 30 123", Mobile = "+31 6 123", Email = "planning@contact.example",
            Addresses = [new RelationAddressDto { CountryId = await Run.CountryAsync(), Street = "Kade", HouseNumber = "12", PostalCode = "3511 AA", City = "Utrecht", Latitude = 52.1, Longitude = 5.1, TimeZoneId = "Europe/Amsterdam" }]
        });
        var address = (await GetRequiredService<IRelationAppService>().GetAddressesAsync(relation.Id)).Single();
        var orderId = await ConfirmedOrderAsync(relation.Id, LineOf(1, 10, StopAt((relation.Id, address.Id!.Value), Load, Day), StopAt(f.CustomerB, Unload, Day)));
        var leg = (await LegRowsAsync(orderId)).Single();

        var trip = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), leg.Id);

        var stop = trip.Stops.Single(s => s.RelationId == relation.Id);
        stop.Street.ShouldBe("Kade");
        stop.HouseNumber.ShouldBe("12");
        stop.PostalCode.ShouldBe("3511 AA");
        stop.CountryCode.ShouldNotBeNullOrEmpty();
        stop.RelationPhone.ShouldBe("+31 30 123");
        stop.RelationMobile.ShouldBe("+31 6 123");
        stop.RelationEmail.ShouldBe("planning@contact.example");
        var link = stop.Legs.ShouldHaveSingleItem();
        link.OriginName.ShouldBe("Contact Ltd");
        link.OriginCity.ShouldBe("Utrecht");
        link.DestinationName.ShouldBe("Customer B");
        link.DestinationCity.ShouldBe("Rotterdam");
    }

    /* §6 the single leg */

    [Fact]
    public async Task A_leg_on_a_completed_trip_refuses_the_update_one_on_a_running_trip_accepts_it()
    {
        using var _ = InTenantA();
        var (_, _, leg, trip) = await OneLegOnTripAsync();
        await Trips.StartAsync(trip.Id, new TripTimestampDto { At = At(6) });
        var running = await Legs.GetAsync(leg.Id);
        var updated = await Legs.UpdateAsync(leg.Id, new LegUpdateDto { Remarks = "still open", ConcurrencyStamp = running.ConcurrencyStamp });
        updated.Remarks.ShouldBe("still open");

        await Trips.CompleteAsync(trip.Id, new TripTimestampDto { At = At(17) });
        var final = await Legs.GetAsync(leg.Id);

        (await Should.ThrowAsync<BusinessException>(() => Legs.UpdateAsync(leg.Id, new LegUpdateDto { Remarks = "no", ConcurrencyStamp = final.ConcurrencyStamp })))
            .Code.ShouldBe(PlanningExecutionErrorCodes.LegOnFinalTrip);
        (await Legs.GetAsync(leg.Id)).Remarks.ShouldBe("still open");
    }

    /* §6 Orders' order field */

    [Theory]
    [InlineData(PlanboardOrderField.CustomerReference)]
    [InlineData(PlanboardOrderField.InternalRemarks)]
    public async Task An_order_field_saves_trimmed_moves_the_stamp_clears_on_empty_and_a_stale_stamp_conflicts(PlanboardOrderField field)
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.CustomerA, f.CustomerB));
        var stale = await StampAsync(orderId);

        var saved = await OrderFields.UpdateFieldAsync(orderId, new PlanboardOrderFieldUpdateDto { Field = field, Value = "  R1 ", ConcurrencyStamp = stale });

        (field == PlanboardOrderField.CustomerReference ? saved.CustomerReference : saved.InternalRemarks).ShouldBe("R1");
        saved.Id.ShouldBe(orderId);
        saved.ConcurrencyStamp.ShouldNotBe(stale);
        saved.ConcurrencyStamp.ShouldBe(await StampAsync(orderId));

        await Should.ThrowAsync<AbpDbConcurrencyException>(() => OrderFields.UpdateFieldAsync(orderId, new PlanboardOrderFieldUpdateDto { Field = field, Value = "late", ConcurrencyStamp = stale }));

        var cleared = await OrderFields.UpdateFieldAsync(orderId, new PlanboardOrderFieldUpdateDto { Field = field, Value = "  ", ConcurrencyStamp = await StampAsync(orderId) });
        (field == PlanboardOrderField.CustomerReference ? cleared.CustomerReference : cleared.InternalRemarks).ShouldBeNull();
    }

    [Fact]
    public async Task An_order_field_refuses_an_unknown_field_too_long_a_value_and_a_cancelled_order_and_asks_only_the_summary()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.CustomerA, f.CustomerB));

        (await Should.ThrowAsync<BusinessException>(() => OrderFieldAsync(orderId, (PlanboardOrderField)99, "x"))).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        (await Should.ThrowAsync<BusinessException>(() => OrderFieldAsync(orderId, PlanboardOrderField.CustomerReference, new string('x', OrdersConsts.MaxCustomerReferenceLength + 1)))).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);

        Events.Clear();
        var summaries = Requests.ReasonsFor(OrdersConsts.OrderSummaryTargetType, orderId).Count;
        await OrderFieldAsync(orderId, PlanboardOrderField.InternalRemarks, "note");
        Requests.ReasonsFor(OrdersConsts.OrderSummaryTargetType, orderId).Count.ShouldBe(summaries + 1);
        Events.Of<TransportOrderLinesChangedEto>().ShouldNotContain(e => e.OrderId == orderId, "no freight changed");

        await Orders.CancelAsync(orderId);
        (await Should.ThrowAsync<BusinessException>(() => OrderFieldAsync(orderId, PlanboardOrderField.InternalRemarks, "x"))).Code.ShouldBe(OrdersErrorCodes.OrderNotEditable);
    }

    private async Task<PlanboardOrderFieldResultDto> OrderFieldAsync(Guid orderId, PlanboardOrderField field, string value)
        => await OrderFields.UpdateFieldAsync(orderId, new PlanboardOrderFieldUpdateDto { Field = field, Value = value, ConcurrencyStamp = await StampAsync(orderId) });

    /* §6 Orders' line field */

    [Fact]
    public async Task A_line_field_saves_names_the_value_moves_the_stamp_and_reaches_the_legs_through_the_fact()
    {
        using var _ = InTenantA();
        var (_, orderId, leg, trip) = await OneLegOnTripAsync();
        var crate = await Run.TransportUnitAsync("Crate");
        var product = (await GetRequiredService<IProductAppService>().CreateAsync(new ProductCreateDto { Name = "Cheese" })).Id;
        var temperature = (await GetRequiredService<ITemperatureAppService>().CreateAsync(new TemperatureCreateDto { Name = "Frozen" })).Id;
        Events.Clear();

        var unit = await LineFieldAsync(orderId, leg.OrderLineId, PlanboardOrderLineField.TransportUnit, crate);
        unit.TransportUnitName.ShouldBe("Crate");
        unit.LineId.ShouldBe(leg.OrderLineId);
        unit.ConcurrencyStamp.ShouldBe(await StampAsync(orderId));
        Events.Of<TransportOrderLinesChangedEto>().ShouldContain(e => e.OrderId == orderId && e.LineIds.Contains(leg.OrderLineId));

        (await LineFieldAsync(orderId, leg.OrderLineId, PlanboardOrderLineField.Product, product)).ProductName.ShouldBe("Cheese");
        (await LineFieldAsync(orderId, leg.OrderLineId, PlanboardOrderLineField.Temperature, temperature)).TemperatureName.ShouldBe("Frozen");

        var after = (await LegRowsAsync(orderId)).Single();
        after.Id.ShouldBe(leg.Id);
        after.TripId.ShouldBe(trip.Id, "freight changes no stop: the leg stays on its trip");
        after.TransportUnitId.ShouldBe(crate);
        after.TransportUnitName.ShouldBe("Crate");
        after.ProductName.ShouldBe("Cheese");
        after.TemperatureName.ShouldBe("Frozen");
        after.Quantity.ShouldBe(leg.Quantity);
        after.OrderConcurrencyStamp.ShouldBe(await StampAsync(orderId));

        (await LineFieldAsync(orderId, leg.OrderLineId, PlanboardOrderLineField.Product, null)).ProductId.ShouldBeNull();
        (await LineFieldAsync(orderId, leg.OrderLineId, PlanboardOrderLineField.Temperature, null)).TemperatureId.ShouldBeNull();
        var line = (await Orders.GetAsync(orderId)).Lines.Single();
        line.Quantity.ShouldBe(10);
        line.WeightKg.ShouldBe(1000);
        line.TransportUnitId.ShouldBe(crate);
    }

    [Fact]
    public async Task A_line_field_refuses_a_stale_stamp_no_unit_an_unknown_reference_and_another_tenants_row()
    {
        var foreignUnit = Guid.Empty;
        using (InTenantB())
        {
            await OrdersMasterDataAsync();
            foreignUnit = Pallet;
        }

        using var _ = InTenantA();
        var f = await FleetAsync();
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.CustomerA, f.CustomerB));
        var lineId = (await LegRowsAsync(orderId)).Single().OrderLineId;
        var crate = await Run.TransportUnitAsync("Crate");
        var stale = await StampAsync(orderId);
        await OrderFields.UpdateFieldAsync(orderId, new PlanboardOrderFieldUpdateDto { Field = PlanboardOrderField.InternalRemarks, Value = "moves the stamp", ConcurrencyStamp = stale });

        await Should.ThrowAsync<AbpDbConcurrencyException>(() => LineFields.UpdateLineFieldAsync(orderId, lineId, new PlanboardOrderLineFieldUpdateDto { Field = PlanboardOrderLineField.TransportUnit, Value = crate, ConcurrencyStamp = stale }));
        /* That the refused save leaves the freight is proven on PostgreSQL (Stale_stamps_conflict_on_real_transactions_and_leave_the_row_alone): it takes
         * a transaction, and the SQLite test module runs without any, so here the line's row is already written when the order's row refuses the stamp. */

        (await Should.ThrowAsync<BusinessException>(() => LineFieldAsync(orderId, lineId, PlanboardOrderLineField.TransportUnit, null))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => LineFieldAsync(orderId, lineId, PlanboardOrderLineField.TransportUnit, Guid.NewGuid()))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => LineFieldAsync(orderId, lineId, PlanboardOrderLineField.TransportUnit, foreignUnit))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => LineFieldAsync(orderId, lineId, PlanboardOrderLineField.Product, Guid.NewGuid()))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => LineFieldAsync(orderId, lineId, (PlanboardOrderLineField)99, crate))).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
    }

    [Fact]
    public async Task A_line_with_a_leg_on_a_completed_trip_is_read_only_another_line_of_the_order_is_not()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.CustomerA, f.CustomerB), Line(f.CustomerA, f.CustomerB));
        var legs = await LegRowsAsync(orderId);
        var trip = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), legs[0].Id);
        await CompleteAsync(trip.Id);
        var crate = await Run.TransportUnitAsync("Crate");

        (await Should.ThrowAsync<BusinessException>(() => LineFieldAsync(orderId, legs[0].OrderLineId, PlanboardOrderLineField.TransportUnit, crate))).Code.ShouldBe(OrdersErrorCodes.LineOnFinalTrip);

        (await LineFieldAsync(orderId, legs[1].OrderLineId, PlanboardOrderLineField.TransportUnit, crate)).TransportUnitId.ShouldBe(crate);
    }

    private async Task<PlanboardOrderLineFieldResultDto> LineFieldAsync(Guid orderId, Guid lineId, PlanboardOrderLineField field, Guid? value)
        => await LineFields.UpdateLineFieldAsync(orderId, lineId, new PlanboardOrderLineFieldUpdateDto { Field = field, Value = value, ConcurrencyStamp = await StampAsync(orderId) });

    [Fact]
    public async Task Another_tenant_reaches_neither_field_endpoint()
    {
        Guid orderId, lineId, crate;
        string stamp;
        using (InTenantA())
        {
            var f = await FleetAsync();
            orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.CustomerA, f.CustomerB));
            lineId = (await LegRowsAsync(orderId)).Single().OrderLineId;
            stamp = await StampAsync(orderId);
        }

        using (InTenantB())
        {
            await OrdersMasterDataAsync();
            crate = Pallet;
            await Should.ThrowAsync<EntityNotFoundException>(() => OrderFields.UpdateFieldAsync(orderId, new PlanboardOrderFieldUpdateDto { Field = PlanboardOrderField.InternalRemarks, Value = "x", ConcurrencyStamp = stamp }));
            await Should.ThrowAsync<EntityNotFoundException>(() => LineFields.UpdateLineFieldAsync(orderId, lineId, new PlanboardOrderLineFieldUpdateDto { Field = PlanboardOrderLineField.TransportUnit, Value = crate, ConcurrencyStamp = stamp }));
        }

        using (InTenantA())
        {
            (await StampAsync(orderId)).ShouldBe(stamp);
        }
    }

    /* §6 RefreshReshapedLine */

    [Fact]
    public async Task A_reshaped_line_takes_over_unit_product_and_temperature_and_keeps_its_quantities_and_trips()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var x = await PlaceAsync("X", "Hub", 52.3, 5.3);
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, LineOf(10, 1000, StopAt(f.CustomerA, Load, Day), StopAt(f.CustomerB, Unload, Day)));
        var leg = (await LegRowsAsync(orderId)).Single();
        await Legs.SplitLineAsync(leg.Id, new LegSplitLineDto { Quantity = 4 });
        var trip = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), leg.Id);
        var crate = await Run.TransportUnitAsync("Crate");
        var product = (await GetRequiredService<IProductAppService>().CreateAsync(new ProductCreateDto { Name = "Cheese" })).Id;
        var temperature = (await GetRequiredService<ITemperatureAppService>().CreateAsync(new TemperatureCreateDto { Name = "Frozen" })).Id;
        var requests = TripRequests(trip.Id);

        await LineFieldAsync(orderId, leg.OrderLineId, PlanboardOrderLineField.TransportUnit, crate);
        await LineFieldAsync(orderId, leg.OrderLineId, PlanboardOrderLineField.Product, product);
        await LineFieldAsync(orderId, leg.OrderLineId, PlanboardOrderLineField.Temperature, temperature);

        var legs = await LegRowsAsync(orderId);
        legs.Count.ShouldBe(2);
        legs.ShouldAllBe(l => l.TransportUnitId == crate && l.ProductId == product && l.TemperatureId == temperature);
        legs.Single(l => l.Id == leg.Id).Quantity.ShouldBe(6, "how much stays the planner's");
        legs.Single(l => l.Id != leg.Id).Quantity.ShouldBe(4);
        legs.Single(l => l.Id == leg.Id).WeightKg.ShouldBe(600);
        legs.Single(l => l.Id == leg.Id).TripId.ShouldBe(trip.Id, "the stops are the same: the leg stays on its trip");
        TripRequests(trip.Id).ShouldBeGreaterThan(requests);

        var before = legs.Select(Fingerprint).ToList();
        await RedeliverLinesChangedAsync(orderId);
        (await LegRowsAsync(orderId)).Select(Fingerprint).ShouldBe(before);
    }

    /* §9 */

    [Theory]
    [InlineData(PlanboardTripField.Name)]
    [InlineData(PlanboardTripField.Remarks)]
    [InlineData(PlanboardTripField.DriverInstructions)]
    public async Task A_text_edit_starts_no_calculation_and_no_rate_job_but_the_notice_and_the_fact_still_go(PlanboardTripField field)
    {
        using var _ = InTenantA();
        var (_, _, _, trip) = await OneLegOnTripAsync();
        var fresh = await Trips.GetAsync(trip.Id);
        var (calculations, rates, notices) = (TripRequests(trip.Id), RateRequests(trip.Id), NoticesOf(trip.Id).Count);
        Events.Clear();

        var change = await Planboard.SetFieldAsync(trip.Id, new PlanboardUpdateFieldDto { Field = field, Value = "text", ConcurrencyStamp = fresh.ConcurrencyStamp });

        change.ConcurrencyStamp.ShouldNotBe(fresh.ConcurrencyStamp);
        TripRequests(trip.Id).ShouldBe(calculations);
        RateRequests(trip.Id).ShouldBe(rates);
        NoticesOf(trip.Id).Count.ShouldBeGreaterThan(notices);
        Events.Of<TransportTripChangedEto>().Where(e => e.TripId == trip.Id).ShouldHaveSingleItem().Change.ShouldBe(TripChangeKind.HeaderText);
    }

    [Fact]
    public async Task The_control_a_header_change_still_starts_the_rate_job_and_the_calculation()
    {
        using var _ = InTenantA();
        var (_, _, _, trip) = await OneLegOnTripAsync();
        var fresh = await Trips.GetAsync(trip.Id);
        var (calculations, rates) = (TripRequests(trip.Id), RateRequests(trip.Id));

        await Planboard.RescheduleAsync(trip.Id, new PlanboardRescheduleDto { PlannedDeparture = At(7), ConcurrencyStamp = fresh.ConcurrencyStamp });

        TripRequests(trip.Id).ShouldBeGreaterThan(calculations);
        RateRequests(trip.Id).ShouldBeGreaterThan(rates);
    }

    [Fact]
    public async Task A_text_only_save_of_the_trip_form_starts_no_rate_job_either()
    {
        using var _ = InTenantA();
        var (_, _, _, trip) = await OneLegOnTripAsync();
        var input = UpdateFrom(await Trips.GetAsync(trip.Id));
        input.Remarks = "only text";
        input.DriverInstructions = "only text";
        var (calculations, rates) = (TripRequests(trip.Id), RateRequests(trip.Id));

        await Trips.UpdateAsync(trip.Id, input);

        TripRequests(trip.Id).ShouldBe(calculations);
        RateRequests(trip.Id).ShouldBe(rates, "design 74 §9: \"other text-only edits (name, remarks, driver instructions)\"");
    }

    [Fact]
    public async Task Unassigning_several_legs_requests_one_trip_calculation_and_one_route_per_leg()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var c = await PlaceAsync("U", "Cee", 52.5, 5.5);
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, LineOf(10, 1000, StopAt(f.CustomerA, Load, Day), StopAt(f.CustomerB, Unload, Day), StopAt(c, Unload, Day)), Line(f.CustomerA, f.CustomerB));
        var legs = await LegRowsAsync(orderId);
        legs.Count.ShouldBe(3);
        var trip = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), legs.Select(l => l.Id).ToArray());
        var calculations = TripRequests(trip.Id);
        var routes = legs.ToDictionary(l => l.Id, l => LegRequests(l.Id));

        await Trips.UnassignLegsAsync(trip.Id, new UnassignLegsDto { LegIds = legs.Select(l => l.Id).ToList() });

        TripRequests(trip.Id).ShouldBe(calculations + 1);
        legs.ShouldAllBe(l => LegRequests(l.Id) == routes[l.Id] + 1);
    }
}
