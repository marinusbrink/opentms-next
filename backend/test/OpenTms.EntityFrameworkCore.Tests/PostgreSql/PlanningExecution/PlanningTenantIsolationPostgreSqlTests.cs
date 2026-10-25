using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Orders.Events;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.Published;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.PlanningExecution.ViaDepots;
using Shouldly;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace OpenTms.PostgreSql.PlanningExecution;

/// <summary>
/// Design 59 against real PostgreSQL, both tenant tiers (tenant A in its own database, the shared tenant in the host
/// database): legs, trips, stops, the published readers and the lists are tenant-scoped; the unique keys hold; leg
/// generation is idempotent and splits at a via depot. Writes are rolled back. Risk class: Critical.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PlanningTenantIsolationPostgreSqlTests : PlanningPostgreSqlTestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_tenant_never_sees_another_tenants_legs_trips_stops_readers_or_lists(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;
        var otherId = ownDatabase ? SharedTenant : TenantA;
        Guid orderId = Guid.Empty, tripId = Guid.Empty;
        List<Guid> legIds = [];

        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            var depot = await PlaceAsync("DEPOT", "Depot", 52.09, 5.12, "Utrecht");
            var customer = await PlaceAsync("CUST", "Customer", 52.37, 4.90);
            orderId = RegisterOrder(tenantId, customer.RelationId,
                LineOf(10, 1000, StopAt(depot.RelationId, depot.AddressId, Load, "Depot", 52.09, 5.12), StopAt(customer.RelationId, customer.AddressId, Unload, "Customer", 52.37, 4.90)));
            var legs = await ConfirmAsync(orderId);
            legIds = legs.Select(l => l.Id).ToList();
            legs.ShouldHaveSingleItem().TenantId.ShouldBe(tenantId);

            var trip = await CreateTripAsync(await SeededTripTypeAsync());
            tripId = trip.Id;
            await Operations.AssignAsync(trip, legs, allowOutsideWindow: true, afterStopId: null, moving: false);
            await FlushAsync();
            trip.Stops.Count.ShouldBe(2);
            trip.TenantId.ShouldBe(tenantId);
            trip.Stops.ShouldAllBe(s => s.TenantId == tenantId);

            /* The same transaction and connection: switch tenant, the other tenant sees nothing. */
            using (CurrentTenant.Change(otherId))
            {
                (await LegRepository.GetListAsync(l => l.OrderId == orderId)).ShouldBeEmpty();
                (await TripRepository.FindAsync(tripId)).ShouldBeNull();
                (await GetRequiredService<IRepository<TripStop, Guid>>().GetListAsync(s => s.TripId == tripId)).ShouldBeEmpty();
                (await GetRequiredService<ILegReader>().GetByOrderAsync(orderId)).ShouldBeEmpty();
                (await GetRequiredService<ILegReader>().GetByTripAsync(tripId)).ShouldBeEmpty();
                (await GetRequiredService<ILegReader>().GetAsync(legIds)).ShouldBeEmpty();
                (await GetRequiredService<ITransportTripLookup>().GetAsync([tripId])).ShouldBeEmpty();
                (await GetRequiredService<ITransportTripAppService>().GetListAsync(new TransportTripListRequestDto { StartRow = 0, EndRow = 100 })).Rows.ShouldNotContain(r => r.Id == tripId);
                (await GetRequiredService<ILegAppService>().GetListAsync(new LegListRequestDto { StartRow = 0, EndRow = 100, TripId = tripId })).Rows.ShouldBeEmpty();
                await Should.ThrowAsync<EntityNotFoundException>(() => GetRequiredService<ITransportTripAppService>().GetAsync(tripId));
                await Should.ThrowAsync<EntityNotFoundException>(() => GetRequiredService<ILegAppService>().GetAsync(legIds[0]));

                /* The other tenant's handler for the same order id: the fake reader has no such order for it; nothing appears. */
                await Handler.HandleEventAsync(new TransportOrderConfirmedEto { OrderId = orderId });
                await FlushAsync();
                (await LegRepository.GetListAsync(l => l.OrderId == orderId)).ShouldBeEmpty();
            }

            (await GetRequiredService<ILegReader>().GetByTripAsync(tripId)).ShouldHaveSingleItem().TripNumber.ShouldBe(trip.Number);
        });
    }

    [Fact]
    public async Task The_unique_keys_refuse_a_duplicate_leg_key_a_duplicate_trip_number_and_a_leg_end_on_two_stops()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var depot = await PlaceAsync("DEPOT", "Depot", 52.09, 5.12);
            var customer = await PlaceAsync("CUST", "Customer", 52.37, 4.90);
            var orderId = RegisterOrder(TenantA, customer.RelationId,
                LineOf(10, 1000, StopAt(depot.RelationId, depot.AddressId, Load, "Depot", 52.09, 5.12), StopAt(customer.RelationId, customer.AddressId, Unload, "Customer", 52.37, 4.90)));
            var leg = (await ConfirmAsync(orderId)).Single();

            var duplicate = new Leg(Guid.NewGuid(), TenantA, orderId, leg.OrderLineId, leg.CustomerRelationId, 2, LegSource.Generated, leg.Origin, leg.Destination, leg.Quantities);
            await ShouldViolateUniqueIndexAsync(async () =>
            {
                await LegRepository.InsertAsync(duplicate);
                await FlushAsync();
            }, "IX_Legs_TenantId_OrderLineId_OriginStopId_DestinationStopId");
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var type = await SeededTripTypeAsync();
            var trip = await CreateTripAsync(type);
            var twin = new TransportTrip(Guid.NewGuid(), TenantA, trip.Number, type, Day, TripExecution.OwnFleet, StopCompositionMode.MergeByLocationAndActivity, FirstLastStopRule.None, At(6));
            await ShouldViolateUniqueIndexAsync(async () =>
            {
                await TripRepository.InsertAsync(twin);
                await FlushAsync();
            }, "IX_TransportTrips_TenantId_Number");
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var trip = await CreateTripAsync(await SeededTripTypeAsync());
            var a = trip.AddManualStop(Guid.NewGuid(), null, Load, Guid.NewGuid(), null, "A", null, 52, 5, null, null, null);
            var b = trip.AddManualStop(Guid.NewGuid(), null, Load, Guid.NewGuid(), null, "B", null, 52, 5, null, null, null);
            var legId = Guid.NewGuid();
            a.AddLeg(legId, StopLegRole.Origin);
            b.AddLeg(legId, StopLegRole.Origin);
            await ShouldViolateUniqueIndexAsync(async () =>
            {
                await TripRepository.UpdateAsync(trip);
                await FlushAsync();
            }, "IX_TripStopLegs_LegId_Role");
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Generation_is_idempotent_and_a_via_depot_rule_splits_the_pair_at_the_depot(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;
        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            var depot = await PlaceAsync("DEPOT", "Depot", 52.09, 5.12, "Utrecht");
            var a = await PlaceAsync("A", "A", 52.0, 5.0);
            var b = await PlaceAsync("B", "B", 52.5, 5.5);
            var orderId = RegisterOrder(tenantId, a.RelationId,
                LineOf(10, 1000, StopAt(a.RelationId, a.AddressId, Load, "A", 52.0, 5.0), StopAt(b.RelationId, b.AddressId, Unload, "B", 52.5, 5.5)));

            var first = await ConfirmAsync(orderId);
            first.ShouldHaveSingleItem();
            var second = await ConfirmAsync(orderId);
            second.Select(l => l.Id).ShouldBe(first.Select(l => l.Id));
            (await LegRepository.CountAsync(l => l.OrderId == orderId)).ShouldBe(1);

            await GetRequiredService<IViaDepotRuleAppService>().CreateAsync(new ViaDepotRuleCreateDto
            {
                Scope = ViaDepotScope.Customer,
                RelationId = a.RelationId,
                DepotRelationId = depot.RelationId,
                DepotAddressId = depot.AddressId,
                ArrivalWaypointTypeId = DepotIn
            });
            await FlushAsync();

            await Handler.HandleEventAsync(new TransportOrderStopsChangedEto { OrderId = orderId });
            await FlushAsync();
            var split = (await LegRepository.GetListAsync(l => l.OrderId == orderId)).OrderBy(l => l.Position).ToList();
            split.Count.ShouldBe(2);
            split[0].DestinationRelationId.ShouldBe(depot.RelationId);
            split[0].DestinationCity.ShouldBe("Utrecht");
            split[0].IsViaLeg.ShouldBeTrue();
            split[1].OriginStopId.ShouldBe(split[0].DestinationStopId);
            split.ShouldAllBe(l => l.TenantId == tenantId);

            /* Again: the same two legs (the synthetic depot stop id is stable), nothing duplicated. */
            await Handler.HandleEventAsync(new TransportOrderStopsChangedEto { OrderId = orderId });
            await FlushAsync();
            (await LegRepository.GetListAsync(l => l.OrderId == orderId)).Select(l => l.Id).ShouldBe(split.Select(l => l.Id), ignoreOrder: true);
        });
    }
}
