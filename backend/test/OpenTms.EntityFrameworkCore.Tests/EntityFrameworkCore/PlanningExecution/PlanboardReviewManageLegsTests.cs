using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Platform.ReferenceData;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// Design 74 §4 on SQLite: Manage legs, <c>GET</c> and <c>PUT leg/line/{lineId}</c>: the shape, add and remove, the server's rules, the
/// stamps, the read-only rows, and a reshaped line across a later regeneration. Risk class: High (the order chain, planning).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class PlanboardReviewManageLegsTests : PlanboardReviewTestBase
{
    private async Task<(FleetSet F, (Guid RelationId, Guid AddressId) X, (Guid RelationId, Guid AddressId) Y)> PlacesAsync()
    {
        var f = await FleetAsync();
        return (f, await PlaceAsync("HUBX", "Hub X", 52.3, 5.3), await PlaceAsync("HUBY", "Hub Y", 52.2, 5.2));
    }

    private Task<LegChainDto> SaveAsync(LegChainDto read, params LegChainRowInputDto[] rows)
        => Legs.UpdateLineAsync(read.OrderLineId, new LegChainUpdateDto { Stamps = StampsOf(read), Rows = rows.ToList() });

    private async Task<string> RefusedAsync(LegChainDto read, params LegChainRowInputDto[] rows)
        => (await Should.ThrowAsync<BusinessException>(() => SaveAsync(read, rows))).Code!;

    private static LegDto At(LegChainDto chain, int index) => chain.Rows[index].Leg;

    [Fact]
    public async Task The_line_reads_in_route_order_with_its_read_only_rows()
    {
        using var _ = InTenantA();
        var (f, x, y) = await PlacesAsync();
        var (orderId, chain, original) = await ThreeLegChainAsync(f, x, y);
        var trip = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), At(chain, 1).Id);
        var withActuals = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), At(chain, 2).Id);
        await Trips.RecordStopActualsAsync(withActuals.Id, withActuals.Stops[0].Id, new StopActualsDto { ActualArrival = At(7) });

        var read = await Legs.GetLineAsync(original.OrderLineId);

        read.OrderId.ShouldBe(orderId);
        read.OrderLineId.ShouldBe(original.OrderLineId);
        read.IsChain.ShouldBeTrue();
        read.Rows.Select(r => r.Leg.Position).ShouldBe([1, 2, 3]);
        read.Rows[0].IsReadOnly.ShouldBeFalse();
        read.Rows[0].HasActuals.ShouldBeFalse();
        read.Rows[1].IsReadOnly.ShouldBeTrue();
        read.Rows[1].HasActuals.ShouldBeFalse();
        read.Rows[1].Leg.TripNumber.ShouldBe(trip.Number);
        read.Rows[2].IsReadOnly.ShouldBeTrue();
        read.Rows[2].HasActuals.ShouldBeTrue();
        await Should.ThrowAsync<EntityNotFoundException>(() => Legs.GetLineAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Add_makes_new_and_changed_legs_the_planners_keeps_the_outer_stops_and_gives_the_new_place_a_synthetic_stop()
    {
        using var _ = InTenantA();
        var (f, x, y) = await PlacesAsync();
        var (orderId, chain, original) = await ThreeLegChainAsync(f, x, y);

        chain.Rows.Count.ShouldBe(3);
        chain.Rows.ShouldAllBe(r => r.Leg.Source == LegSource.Split);
        At(chain, 0).Id.ShouldBe(original.Id, "the existing leg keeps its id");
        At(chain, 0).Origin.StopId.ShouldBe(original.Origin.StopId);
        At(chain, 2).Destination.StopId.ShouldBe(original.Destination.StopId);
        At(chain, 0).Destination.StopId.ShouldBe(At(chain, 1).Origin.StopId);
        At(chain, 1).Destination.StopId.ShouldBe(At(chain, 2).Origin.StopId);
        var orderStops = (await Orders.GetAsync(orderId)).Lines.Single().Stops.Select(s => s.Id!.Value).ToList();
        orderStops.ShouldNotContain(At(chain, 1).Origin.StopId, "an intermediate end exists only in Planning");
        chain.Rows.ShouldAllBe(r => LegRequests(r.Leg.Id) > 0, "routing is requested per new or changed leg");
        chain.Rows.ShouldAllBe(r => r.Leg.Quantity == 10, "a chain carries the line's quantities on every leg");
    }

    [Fact]
    public async Task Removing_the_first_leg_gives_the_next_its_origin()
    {
        using var _ = InTenantA();
        var (f, x, y) = await PlacesAsync();
        var (orderId, chain, original) = await ThreeLegChainAsync(f, x, y);
        var second = Row(At(chain, 1));
        second.Origin = End(At(chain, 0).Origin);

        var saved = await SaveAsync(chain, second, Row(At(chain, 2)));

        saved.Rows.Select(r => r.Leg.Id).ShouldBe([At(chain, 1).Id, At(chain, 2).Id]);
        saved.Rows.Select(r => r.Leg.Position).ShouldBe([1, 2]);
        At(saved, 0).Origin.StopId.ShouldBe(original.Origin.StopId);
        await Should.ThrowAsync<EntityNotFoundException>(() => Legs.GetAsync(At(chain, 0).Id));
        (await LegRowsAsync(orderId)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Removing_a_middle_leg_gives_the_next_its_origin()
    {
        using var _ = InTenantA();
        var (f, x, y) = await PlacesAsync();
        var (_, chain, _) = await ThreeLegChainAsync(f, x, y);
        var third = Row(At(chain, 2));
        third.Origin = End(At(chain, 1).Origin);

        var saved = await SaveAsync(chain, Row(At(chain, 0)), third);

        saved.Rows.Select(r => r.Leg.Id).ShouldBe([At(chain, 0).Id, At(chain, 2).Id]);
        At(saved, 1).Origin.StopId.ShouldBe(At(saved, 0).Destination.StopId);
        At(saved, 1).Origin.RelationId.ShouldBe(x.RelationId);
    }

    [Fact]
    public async Task Removing_the_last_leg_gives_the_previous_its_destination()
    {
        using var _ = InTenantA();
        var (f, x, y) = await PlacesAsync();
        var (_, chain, original) = await ThreeLegChainAsync(f, x, y);
        var second = Row(At(chain, 1));
        second.Destination = End(At(chain, 2).Destination);

        var saved = await SaveAsync(chain, Row(At(chain, 0)), second);

        saved.Rows.Select(r => r.Leg.Id).ShouldBe([At(chain, 0).Id, At(chain, 1).Id]);
        At(saved, 1).Destination.StopId.ShouldBe(original.Destination.StopId);
    }

    [Fact]
    public async Task The_last_leg_cannot_go()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.CustomerA, f.CustomerB));
        var read = await Legs.GetLineAsync((await LegRowsAsync(orderId)).Single().OrderLineId);
        (await RefusedAsync(read)).ShouldBe(PlanningExecutionErrorCodes.LegChainEmpty);
        (await LegRowsAsync(orderId)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_place_removed_and_added_again_gets_its_leg_back_under_the_unique_key()
    {
        using var _ = InTenantA();
        var (f, x, y) = await PlacesAsync();
        var (_, chain, _) = await ThreeLegChainAsync(f, x, y);
        var second = Row(At(chain, 1));
        second.Destination = End(At(chain, 2).Destination);
        var two = await SaveAsync(chain, Row(At(chain, 0)), second);
        var again = Row(At(two, 1));
        again.Destination = EndAt(y, DepotIn);

        var three = await SaveAsync(two, Row(At(two, 0)), again, new LegChainRowInputDto { Origin = EndAt(y, DepotOut), Destination = End(At(two, 1).Destination) });

        three.Rows.Count.ShouldBe(3);
    }

    [Fact]
    public async Task The_server_keeps_the_chain_contiguous()
    {
        using var _ = InTenantA();
        var (f, x, y) = await PlacesAsync();
        var (_, chain, _) = await ThreeLegChainAsync(f, x, y);
        var broken = Row(At(chain, 1));
        broken.Origin = EndAt(y, DepotOut);
        (await RefusedAsync(chain, Row(At(chain, 0)), broken, Row(At(chain, 2)))).ShouldBe(PlanningExecutionErrorCodes.LegChainNotContiguous);
    }

    [Fact]
    public async Task Dates_do_not_run_backwards_within_a_leg_or_between_legs()
    {
        using var _ = InTenantA();
        var (f, x, y) = await PlacesAsync();
        var (_, chain, _) = await ThreeLegChainAsync(f, x, y);

        var within = Row(At(chain, 1));
        within.Origin.PlannedDate = Day.AddDays(2);
        within.Destination.PlannedDate = Day.AddDays(1);
        (await RefusedAsync(chain, Row(At(chain, 0)), within, Row(At(chain, 2)))).ShouldBe(PlanningExecutionErrorCodes.LegChainDatesBackwards);

        var between = Row(At(chain, 1));
        between.Origin.PlannedDate = Day.AddDays(-1);
        between.Destination.PlannedDate = Day.AddDays(-1);
        (await RefusedAsync(chain, Row(At(chain, 0)), between, Row(At(chain, 2)))).ShouldBe(PlanningExecutionErrorCodes.LegChainDatesBackwards);
    }

    [Fact]
    public async Task The_first_origin_and_the_last_destination_are_fixed_in_activity_relation_and_address()
    {
        using var _ = InTenantA();
        var (f, x, y) = await PlacesAsync();
        var (_, chain, _) = await ThreeLegChainAsync(f, x, y);

        var otherOrigin = Row(At(chain, 0));
        otherOrigin.Origin = EndAt(f.Depot, otherOrigin.Origin.WaypointTypeId);
        (await RefusedAsync(chain, otherOrigin, Row(At(chain, 1)), Row(At(chain, 2)))).ShouldBe(PlanningExecutionErrorCodes.LegChainEndsFixed);

        var otherActivity = Row(At(chain, 0));
        otherActivity.Origin.WaypointTypeId = DepotOut;
        (await RefusedAsync(chain, otherActivity, Row(At(chain, 1)), Row(At(chain, 2)))).ShouldBe(PlanningExecutionErrorCodes.LegChainEndsFixed);

        var otherDestination = Row(At(chain, 2));
        otherDestination.Destination = EndAt(f.Depot, otherDestination.Destination.WaypointTypeId);
        (await RefusedAsync(chain, Row(At(chain, 0)), Row(At(chain, 1)), otherDestination)).ShouldBe(PlanningExecutionErrorCodes.LegChainEndsFixed);
    }

    [Fact]
    public async Task Activities_and_relations_are_required_and_must_exist()
    {
        using var _ = InTenantA();
        var (f, x, y) = await PlacesAsync();
        var (_, chain, _) = await ThreeLegChainAsync(f, x, y);

        var noRelation = Row(At(chain, 0));
        noRelation.Destination.RelationId = Guid.Empty;
        (await RefusedAsync(chain, noRelation, Row(At(chain, 1)), Row(At(chain, 2)))).ShouldBe(PlanningExecutionErrorCodes.LegChainIncomplete);

        var noActivity = Row(At(chain, 0));
        noActivity.Destination.WaypointTypeId = Guid.Empty;
        (await RefusedAsync(chain, noActivity, Row(At(chain, 1)), Row(At(chain, 2)))).ShouldBe(PlanningExecutionErrorCodes.LegChainIncomplete);

        var unknownActivity = Row(At(chain, 0));
        unknownActivity.Destination.WaypointTypeId = Guid.NewGuid();
        (await RefusedAsync(chain, unknownActivity, Row(At(chain, 1)), Row(At(chain, 2)))).ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);

        var unknownRelation = Row(At(chain, 1));
        unknownRelation.Destination = new LegChainEndInputDto { WaypointTypeId = DepotIn, RelationId = Guid.NewGuid(), PlannedDate = Day };
        var nextOrigin = Row(At(chain, 2));
        nextOrigin.Origin = new LegChainEndInputDto { WaypointTypeId = DepotOut, RelationId = unknownRelation.Destination.RelationId, PlannedDate = Day };
        (await RefusedAsync(chain, Row(At(chain, 0)), unknownRelation, nextOrigin)).ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);

        var foreignAddress = Row(At(chain, 1));
        foreignAddress.Destination = new LegChainEndInputDto { WaypointTypeId = DepotIn, RelationId = f.Depot.RelationId, RelationAddressId = x.AddressId, PlannedDate = Day };
        var foreignNext = Row(At(chain, 2));
        foreignNext.Origin = new LegChainEndInputDto { WaypointTypeId = DepotOut, RelationId = f.Depot.RelationId, RelationAddressId = x.AddressId, PlannedDate = Day };
        (await RefusedAsync(chain, Row(At(chain, 0)), foreignAddress, foreignNext)).ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
    }

    [Fact]
    public async Task A_route_over_the_same_two_stops_twice_is_refused()
    {
        using var _ = InTenantA();
        var (f, x, _) = await PlacesAsync();
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, LineOf(10, 1000, StopAt(f.CustomerA, Load, Day), StopAt(f.CustomerB, Unload, Day)));
        var leg = (await LegRowsAsync(orderId)).Single();
        var read = await Legs.GetLineAsync(leg.OrderLineId);
        var first = Row(leg);
        first.Destination = EndAt(f.CustomerB, Unload);
        var back = new LegChainRowInputDto { Origin = EndAt(f.CustomerB, Unload), Destination = End(leg.Origin) };
        var again = new LegChainRowInputDto { Origin = End(leg.Origin), Destination = End(leg.Destination) };
        (await RefusedAsync(read, first, back, again)).ShouldBe(PlanningExecutionErrorCodes.LegChainDuplicateLeg);
    }

    [Fact]
    public async Task A_leg_on_a_trip_cannot_change_and_add_or_remove_around_it_is_refused_when_it_would_change_its_ends()
    {
        using var _ = InTenantA();
        var (f, x, y) = await PlacesAsync();
        var (_, chain, _) = await ThreeLegChainAsync(f, x, y);
        await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), At(chain, 1).Id);
        var read = await Legs.GetLineAsync(chain.OrderLineId);

        var remarks = Row(At(read, 1));
        remarks.Remarks = "changed";
        (await RefusedAsync(read, Row(At(read, 0)), remarks, Row(At(read, 2)))).ShouldBe(PlanningExecutionErrorCodes.LegChainReadOnlyLeg);

        var date = Row(At(read, 1));
        date.Destination.PlannedDate = Day.AddDays(1);
        var nextDate = Row(At(read, 2));
        nextDate.Origin.PlannedDate = Day.AddDays(1);
        nextDate.Destination.PlannedDate = Day.AddDays(1);
        (await RefusedAsync(read, Row(At(read, 0)), date, nextDate)).ShouldBe(PlanningExecutionErrorCodes.LegChainReadOnlyLeg);

        var removeFirst = Row(At(read, 1));
        removeFirst.Origin = End(At(read, 0).Origin);
        (await RefusedAsync(read, removeFirst, Row(At(read, 2)))).ShouldBe(PlanningExecutionErrorCodes.LegChainReadOnlyLeg, "removing the first leg would change its origin");

        var withoutIt = Row(At(read, 2));
        withoutIt.Origin = End(At(read, 0).Destination);
        (await RefusedAsync(read, Row(At(read, 0)), withoutIt)).ShouldBe(PlanningExecutionErrorCodes.LegChainReadOnlyLeg, "the leg itself cannot go");
    }

    [Fact]
    public async Task Legs_around_a_leg_on_a_trip_can_change_when_its_ends_stay()
    {
        using var _ = InTenantA();
        var (f, x, y) = await PlacesAsync();
        var (_, chain, _) = await ThreeLegChainAsync(f, x, y);
        var trip = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), At(chain, 1).Id);
        var read = await Legs.GetLineAsync(chain.OrderLineId);
        var requests = TripRequests(trip.Id);
        var third = Row(At(read, 2));
        third.Remarks = "around it";

        var saved = await SaveAsync(read, Row(At(read, 0)), Row(At(read, 1)), third);

        At(saved, 1).TripId.ShouldBe(trip.Id);
        At(saved, 2).Remarks.ShouldBe("around it");
        TripRequests(trip.Id).ShouldBe(requests, "the legs on trips stay as they are");
    }

    [Fact]
    public async Task A_leg_with_actuals_is_read_only_too()
    {
        using var _ = InTenantA();
        var (f, x, y) = await PlacesAsync();
        var (_, chain, _) = await ThreeLegChainAsync(f, x, y);
        var trip = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), At(chain, 0).Id);
        await Trips.RecordStopActualsAsync(trip.Id, trip.Stops[0].Id, new StopActualsDto { ActualArrival = At(7) });
        var read = await Legs.GetLineAsync(chain.OrderLineId);
        var row = Row(At(read, 0));
        row.Remarks = "no";
        (await RefusedAsync(read, row, Row(At(read, 1)), Row(At(read, 2)))).ShouldBe(PlanningExecutionErrorCodes.LegChainReadOnlyLeg);
    }

    [Fact]
    public async Task A_stale_stamp_a_leg_added_or_removed_by_someone_else_and_an_unknown_row_are_conflicts()
    {
        using var _ = InTenantA();
        var (f, x, y) = await PlacesAsync();
        var (orderId, chain, _) = await ThreeLegChainAsync(f, x, y);

        var stale = await Legs.GetLineAsync(chain.OrderLineId);
        await Legs.UpdateAsync(At(stale, 0).Id, new LegUpdateDto { Remarks = "someone else", ConcurrencyStamp = At(stale, 0).ConcurrencyStamp });
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => SaveAsync(stale, Row(At(stale, 0)), Row(At(stale, 1)), Row(At(stale, 2))));

        var read = await Legs.GetLineAsync(chain.OrderLineId);
        await Legs.SplitLineAsync(At(read, 1).Id, new LegSplitLineDto { Quantity = 1 });
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => SaveAsync(read, Row(At(read, 0)), Row(At(read, 1)), Row(At(read, 2))));
        (await LegRowsAsync(orderId)).Count.ShouldBe(4);

        var other = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.CustomerA, f.CustomerB));
        var line = await Legs.GetLineAsync((await LegRowsAsync(other)).Single().OrderLineId);
        var unknown = Row(At(line, 0));
        unknown.Id = Guid.NewGuid();
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => SaveAsync(line, unknown));
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => SaveAsync(line, Row(At(line, 0)), Row(At(line, 0))));
    }

    [Fact]
    public async Task A_line_whose_legs_run_in_parallel_is_no_chain_and_cannot_be_saved()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.CustomerA, f.CustomerB));
        var leg = (await LegRowsAsync(orderId)).Single();
        await Legs.SplitLineAsync(leg.Id, new LegSplitLineDto { Quantity = 3 });

        var read = await Legs.GetLineAsync(leg.OrderLineId);

        read.IsChain.ShouldBeFalse();
        (await RefusedAsync(read, Row(At(read, 0)), Row(At(read, 1)))).ShouldBe(PlanningExecutionErrorCodes.LegLineNotAChain);
    }

    [Fact]
    public async Task The_line_stays_reshaped_across_a_later_regeneration_and_only_the_order_ends_follow_the_order()
    {
        using var _ = InTenantA();
        var (f, x, y) = await PlacesAsync();
        var (orderId, chain, _) = await ThreeLegChainAsync(f, x, y);
        var ids = chain.Rows.Select(r => r.Leg.Id).ToList();

        await RedeliverLinesChangedAsync(orderId);
        (await Legs.GetLineAsync(chain.OrderLineId)).Rows.Select(r => r.Leg.Id).ShouldBe(ids);

        var line = LinesOf(await Orders.GetAsync(orderId)).Single();
        line.Stops![1].Reference = "NEWREF";
        await Orders.UpdateLineAsync(orderId, line.Id!.Value, line);

        var after = await Legs.GetLineAsync(chain.OrderLineId);
        after.Rows.Select(r => r.Leg.Id).ShouldBe(ids);
        At(after, 2).Destination.Reference.ShouldBe("NEWREF", "an end at an order stop follows the stop");
        At(after, 1).Destination.RelationId.ShouldBe(y.RelationId, "the planner's synthetic ends stay");
    }

    [Fact]
    public async Task Saving_the_line_unchanged_does_nothing()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.CustomerA, f.CustomerB));
        var before = (await LegRowsAsync(orderId)).Single();
        var read = await Legs.GetLineAsync(before.OrderLineId);
        var requests = LegRequests(before.Id);

        var saved = await SaveAsync(read, Row(At(read, 0)));

        At(saved, 0).Source.ShouldBe(LegSource.Generated);
        At(saved, 0).ConcurrencyStamp.ShouldBe(before.ConcurrencyStamp);
        LegRequests(before.Id).ShouldBe(requests);
    }

    /// <summary>
    /// The design's rule as built (§4 "New and changed legs become LegSource.Split"): a remarks edit counts as a change, so it reshapes
    /// the line and freezes its quantities. The same edit through <c>PUT leg/{id}</c> leaves the line the generator's. Reported to the PO
    /// as a question; this test pins the behaviour and does not bless it.
    /// </summary>
    [Fact]
    public async Task A_remarks_only_edit_keeps_the_line_the_generators_needs_no_route_and_quantities_keep_following()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var c = await PlaceAsync("REM", "Cee", 52.5, 5.5);
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, LineOf(10, 1000, StopAt(f.CustomerA, Load, Day), StopAt(f.CustomerB, Unload, Day), StopAt(c, Unload, Day)));
        var legs = await LegRowsAsync(orderId);
        var read = await Legs.GetLineAsync(legs[0].OrderLineId);
        var untouched = LegRequests(legs[0].Id);
        var changed = LegRequests(legs[1].Id);
        var row = Row(At(read, 1));
        row.Remarks = "ring first";

        var saved = await SaveAsync(read, Row(At(read, 0)), row);

        At(saved, 0).Source.ShouldBe(LegSource.Generated);
        At(saved, 1).Source.ShouldBe(LegSource.Generated, "a remark is no reshape");
        At(saved, 1).Remarks.ShouldBe("ring first");
        At(saved, 1).Origin.StopId.ShouldBe(legs[1].Origin.StopId, "a remark moves no stop");
        LegRequests(legs[0].Id).ShouldBe(untouched);
        LegRequests(legs[1].Id).ShouldBe(changed, "the route does not depend on a remark");

        var line = LinesOf(await Orders.GetAsync(orderId)).Single();
        line.Quantity = 20;
        await Orders.UpdateLineAsync(orderId, line.Id!.Value, line);
        (await LegRowsAsync(orderId)).ShouldAllBe(l => l.Quantity == 20, "the line still follows the order's quantity");
    }

    [Fact]
    public async Task The_same_remark_through_the_single_leg_endpoint_keeps_the_line_the_generators()
    {
        using var _ = InTenantA();
        var f = await FleetAsync();
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, LineOf(10, 1000, StopAt(f.CustomerA, Load, Day), StopAt(f.CustomerB, Unload, Day)));
        var leg = (await LegRowsAsync(orderId)).Single();
        await Legs.UpdateAsync(leg.Id, new LegUpdateDto { Remarks = "ring first", ConcurrencyStamp = leg.ConcurrencyStamp });

        var line = LinesOf(await Orders.GetAsync(orderId)).Single();
        line.Quantity = 20;
        await Orders.UpdateLineAsync(orderId, line.Id!.Value, line);

        var after = (await LegRowsAsync(orderId)).Single();
        after.Source.ShouldBe(LegSource.Generated);
        after.Quantity.ShouldBe(20);
        after.Remarks.ShouldBe("ring first");
    }

    /* Windows are compared at the minute: the dialog keeps minutes, an order or a single-leg edit may hold seconds. */

    private static DateTimeOffset? Minute(DateTimeOffset? value)
        => value == null ? null : new DateTimeOffset(value.Value.UtcTicks / TimeSpan.TicksPerMinute * TimeSpan.TicksPerMinute, TimeSpan.Zero).ToOffset(value.Value.Offset);

    /// <summary>The row as the dialog sends it back: every window cut to the minute.</summary>
    private static LegChainRowInputDto AtTheMinute(LegDto leg)
    {
        var row = Row(leg);
        (row.Origin.WindowFrom, row.Origin.WindowTill) = (Minute(row.Origin.WindowFrom), Minute(row.Origin.WindowTill));
        (row.Destination.WindowFrom, row.Destination.WindowTill) = (Minute(row.Destination.WindowFrom), Minute(row.Destination.WindowTill));
        return row;
    }

    /// <summary>A → B → C, the first leg's windows holding seconds, as stored.</summary>
    private async Task<(FleetSet F, Guid OrderId, LegDto WithSeconds, LegDto Other)> LegWithSecondsAsync(string code)
    {
        var f = await FleetAsync();
        var c = await PlaceAsync(code, "Cee", 52.5, 5.5);
        var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, LineOf(10, 1000, StopAt(f.CustomerA, Load, Day), StopAt(f.CustomerB, Unload, Day), StopAt(c, Unload, Day)));
        var legs = await LegRowsAsync(orderId);
        var saved = await Legs.UpdateAsync(legs[0].Id, new LegUpdateDto
        {
            OriginWindow = new LegTimeWindowDto { From = At(8).AddSeconds(30), Till = At(10).AddSeconds(45) },
            DestinationWindow = new LegTimeWindowDto { From = At(12).AddSeconds(15), Till = At(14).AddSeconds(5) },
            ConcurrencyStamp = legs[0].ConcurrencyStamp
        });
        saved.Origin.WindowFrom!.Value.Second.ShouldBe(30, "the control: the seconds are stored");
        return (f, orderId, saved, legs[1]);
    }

    private static void ShouldKeepItsWindows(LegDto leg, LegDto stored)
    {
        leg.Origin.WindowFrom.ShouldBe(stored.Origin.WindowFrom);
        leg.Origin.WindowTill.ShouldBe(stored.Origin.WindowTill);
        leg.Destination.WindowFrom.ShouldBe(stored.Destination.WindowFrom);
        leg.Destination.WindowTill.ShouldBe(stored.Destination.WindowTill);
    }

    [Fact]
    public async Task A_read_only_leg_with_seconds_does_not_fail_the_save_of_the_line()
    {
        using var _ = InTenantA();
        var (f, _, stored, _) = await LegWithSecondsAsync("SECR");
        var trip = await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), stored.Id);
        var read = await Legs.GetLineAsync(stored.OrderLineId);
        read.Rows[0].IsReadOnly.ShouldBeTrue();
        var requests = TripRequests(trip.Id);
        var other = Row(At(read, 1));
        other.Remarks = "around it";

        var saved = await SaveAsync(read, AtTheMinute(At(read, 0)), other);

        ShouldKeepItsWindows(At(saved, 0), stored);
        At(saved, 0).ConcurrencyStamp.ShouldBe(At(read, 0).ConcurrencyStamp, "it stays exactly as it is");
        At(saved, 0).TripId.ShouldBe(trip.Id);
        At(saved, 1).Remarks.ShouldBe("around it");
        TripRequests(trip.Id).ShouldBe(requests);
    }

    [Fact]
    public async Task A_read_only_leg_still_refuses_a_window_another_minute_on()
    {
        using var _ = InTenantA();
        var (f, _, stored, _) = await LegWithSecondsAsync("SECM");
        await OnTripAsync(await TripWithAsync(f.Type, At(6), At(18)), stored.Id);
        var read = await Legs.GetLineAsync(stored.OrderLineId);
        var moved = AtTheMinute(At(read, 0));
        moved.Origin.WindowFrom = moved.Origin.WindowFrom!.Value.AddMinutes(1);

        (await RefusedAsync(read, moved, Row(At(read, 1)))).ShouldBe(PlanningExecutionErrorCodes.LegChainReadOnlyLeg);

        var cleared = AtTheMinute(At(read, 0));
        cleared.Destination.WindowTill = null;
        (await RefusedAsync(read, cleared, Row(At(read, 1)))).ShouldBe(PlanningExecutionErrorCodes.LegChainReadOnlyLeg);
    }

    [Fact]
    public async Task An_editable_leg_with_seconds_is_not_truncated_and_does_not_count_as_edited()
    {
        using var _ = InTenantA();
        var (_, orderId, stored, _) = await LegWithSecondsAsync("SECE");
        var read = await Legs.GetLineAsync(stored.OrderLineId);
        read.Rows[0].IsReadOnly.ShouldBeFalse();
        var before = (await LegRowsAsync(orderId)).Select(Fingerprint).ToList();
        var (notices, routes) = (NoticesOf(stored.Id).Count, LegRequests(stored.Id));

        var saved = await SaveAsync(read, AtTheMinute(At(read, 0)), Row(At(read, 1)));

        ShouldKeepItsWindows(At(saved, 0), stored);
        (await LegRowsAsync(orderId)).Select(Fingerprint).ShouldBe(before, "nothing was stored");
        NoticesOf(stored.Id).Count.ShouldBe(notices, "no notice of a change that is none");
        LegRequests(stored.Id).ShouldBe(routes);
        At(saved, 0).Source.ShouldBe(LegSource.Generated);
    }

    [Fact]
    public async Task Other_seconds_in_the_same_minute_are_the_same_window_and_the_stored_ones_stay()
    {
        using var _ = InTenantA();
        var (_, _, stored, _) = await LegWithSecondsAsync("SECO");
        var read = await Legs.GetLineAsync(stored.OrderLineId);
        var row = AtTheMinute(At(read, 0));
        row.Origin.WindowFrom = row.Origin.WindowFrom!.Value.AddSeconds(59);

        var saved = await SaveAsync(read, row, Row(At(read, 1)));

        ShouldKeepItsWindows(At(saved, 0), stored);
        At(saved, 0).ConcurrencyStamp.ShouldBe(At(read, 0).ConcurrencyStamp);
    }

    [Fact]
    public async Task A_real_change_on_a_leg_with_seconds_still_counts_and_its_other_windows_keep_their_seconds()
    {
        using var _ = InTenantA();
        var (_, _, stored, _) = await LegWithSecondsAsync("SECC");
        var read = await Legs.GetLineAsync(stored.OrderLineId);
        var notices = NoticesOf(stored.Id).Count;
        var row = AtTheMinute(At(read, 0));
        var wanted = row.Origin.WindowFrom!.Value.AddMinutes(1);
        row.Origin.WindowFrom = wanted;

        var saved = await SaveAsync(read, row, Row(At(read, 1)));

        At(saved, 0).Origin.WindowFrom.ShouldBe(wanted, "another minute is a change, stored as sent");
        At(saved, 0).Origin.WindowTill.ShouldBe(stored.Origin.WindowTill);
        At(saved, 0).Destination.WindowFrom.ShouldBe(stored.Destination.WindowFrom);
        At(saved, 0).Destination.WindowTill.ShouldBe(stored.Destination.WindowTill);
        At(saved, 0).ConcurrencyStamp.ShouldNotBe(At(read, 0).ConcurrencyStamp);
        At(saved, 0).Source.ShouldBe(LegSource.Generated, "a window is no reshape");
        NoticesOf(stored.Id).Count.ShouldBeGreaterThan(notices);

        /* A text on the same leg counts too, and leaves the seconds alone. */
        var again = await Legs.GetLineAsync(stored.OrderLineId);
        var remark = AtTheMinute(At(again, 0));
        remark.Origin.WindowFrom = At(again, 0).Origin.WindowFrom;
        remark.Remarks = "ring first";
        var remarked = await SaveAsync(again, remark, Row(At(again, 1)));
        At(remarked, 0).Remarks.ShouldBe("ring first");
        At(remarked, 0).Origin.WindowTill.ShouldBe(stored.Origin.WindowTill);
        At(remarked, 0).ConcurrencyStamp.ShouldNotBe(At(again, 0).ConcurrencyStamp);

        /* Clearing a window is a change as well. */
        var third = await Legs.GetLineAsync(stored.OrderLineId);
        var cleared = AtTheMinute(At(third, 0));
        cleared.Destination.WindowFrom = null;
        cleared.Destination.WindowTill = null;
        var empty = await SaveAsync(third, cleared, Row(At(third, 1)));
        At(empty, 0).Destination.WindowFrom.ShouldBeNull();
        At(empty, 0).Destination.WindowTill.ShouldBeNull();
    }

    [Fact]
    public async Task Another_tenant_reaches_no_line_and_cannot_name_another_tenants_relation()
    {
        Guid lineId;
        LegChainDto read;
        (Guid RelationId, Guid AddressId) foreign;
        using (InTenantB())
        {
            foreign = await PlaceAsync("FOREIGN", "Foreign", 52.1, 5.1);
        }

        using (InTenantA())
        {
            var f = await FleetAsync();
            var orderId = await ConfirmedOrderAsync(f.CustomerA.RelationId, Line(f.CustomerA, f.CustomerB));
            var leg = (await LegRowsAsync(orderId)).Single();
            lineId = leg.OrderLineId;
            read = await Legs.GetLineAsync(lineId);
            var first = Row(leg);
            first.Destination = new LegChainEndInputDto { WaypointTypeId = DepotIn, RelationId = foreign.RelationId, RelationAddressId = foreign.AddressId, PlannedDate = Day };
            var second = new LegChainRowInputDto { Origin = new LegChainEndInputDto { WaypointTypeId = DepotOut, RelationId = foreign.RelationId, RelationAddressId = foreign.AddressId, PlannedDate = Day }, Destination = End(leg.Destination) };
            (await RefusedAsync(read, first, second)).ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        }

        using (InTenantB())
        {
            await Should.ThrowAsync<EntityNotFoundException>(() => Legs.GetLineAsync(lineId));
            await Should.ThrowAsync<EntityNotFoundException>(() => SaveAsync(read, Row(At(read, 0))));
        }
    }
}
