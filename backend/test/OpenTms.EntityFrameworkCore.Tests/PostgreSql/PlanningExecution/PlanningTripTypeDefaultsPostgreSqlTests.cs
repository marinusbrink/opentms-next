using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Published;
using OpenTms.MasterData.Relations;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.ReferenceData;
using OpenTms.TransportRun;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace OpenTms.PostgreSql.PlanningExecution;

/// <summary>
/// Design 61 "Trip type defaults" against real PostgreSQL, both tenant tiers (tenant A in its own database, the shared
/// tenant in the host database): the server fill of a new trip on both paths (the trips service and the order's trip
/// request on Confirm through the real handler), an archived default skipped, and the negative tenant tests — the other
/// tenant cannot save a type with this tenant's Master Data, a trip of the other tenant never gets this tenant's ids even
/// when they are stored on its type, and this tenant's type does not exist for it; the PO's rules of 2026-10-29 (a default
/// contact must be the carrier's, a carrier from the type subcontracts) and the contact lookup's tenant filter. Writes are
/// rolled back. Risk class:
/// Critical (the server fill, tenant isolation); the full group-by-group matrix runs on SQLite (TripTypeDefaultsTests).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PlanningTripTypeDefaultsPostgreSqlTests : PlanningPostgreSqlTestBase
{
    private ITransportTripTypeAppService TripTypes => GetRequiredService<ITransportTripTypeAppService>();

    private ITransportTripAppService Trips => GetRequiredService<ITransportTripAppService>();

    private sealed record World((Guid RelationId, Guid AddressId) Depot, (Guid RelationId, Guid AddressId) Customer, Guid Carrier, Guid Contact, Guid Tractor, Guid Van, Guid Driver1, Guid Driver2);

    private async Task<World> WorldAsync()
    {
        var suffix = TransportRunFixture.NewSuffix();
        var depot = await PlaceAsync("DEP" + suffix, "Depot " + suffix, 52.09, 5.12, "Utrecht");
        var customer = await PlaceAsync("CUS" + suffix, "Customer " + suffix, 52.37, 4.90);
        var relations = GetRequiredService<IRelationAppService>();
        var carrier = await relations.CreateAsync(new RelationCreateDto
        {
            Code = "CAR" + suffix,
            Name = "Carrier " + suffix,
            Roles = RelationRoles.Carrier,
            Addresses =
            [
                new RelationAddressDto
                {
                    CountryId = await Run.CountryAsync(), Street = "Carrier road", HouseNumber = "5", PostalCode = "3500 AA", City = "Utrecht",
                    Latitude = 52.1, Longitude = 5.1, TimeZoneId = "Europe/Amsterdam"
                }
            ],
            ContactPersons = [new RelationContactPersonDto { FirstName = "Kees", LastName = "Visser", Role = ContactPersonRole.Planning }]
        });
        await FlushAsync();
        var contact = (await relations.GetAsync(carrier.Id)).ContactPersons.Single().Id!.Value;
        var tractorType = await Run.VehicleTypeAsync("Tractor " + suffix, VehicleTypeKind.Tractor);
        var vanType = await Run.VehicleTypeAsync("Van " + suffix, VehicleTypeKind.Rigid, maxEpq: 8);
        var tractor = await Run.VehicleAsync("Truck " + suffix, tractorType, carrier.Id);
        var van = await Run.VehicleAsync("Van " + suffix, vanType, carrier.Id, grossKg: 3_500);
        var driver1 = await Run.DriverAsync("D1" + suffix, "Jan", "Jansen");
        var driver2 = await Run.DriverAsync("D2" + suffix, "Piet", "Peters");
        await FlushAsync();
        return new World(depot, customer, carrier.Id, contact, tractor, van, driver1, driver2);
    }

    private async Task<TransportTripTypeDto> TypeAsync(World w)
    {
        var suffix = TransportRunFixture.NewSuffix();
        var type = await TripTypes.CreateAsync(new TransportTripTypeCreateDto
        {
            Code = "DEF" + suffix,
            Names = new LocalizedNameDto { ["en"] = "Defaults " + suffix },
            Numbering = new NumberSequenceFormatDto { Prefix = "D" + suffix, Separator = "-", Digits = 5 },
            DefaultCarrierRelationId = w.Carrier,
            DefaultCarrierContactPersonId = w.Contact,
            DefaultVehicleIds = [w.Van, w.Tractor],
            DefaultDriverIds = [w.Driver2, w.Driver1],
            DefaultDepartureRelationId = w.Depot.RelationId,
            DefaultDepartureAddressId = w.Depot.AddressId,
            DefaultArrivalRelationId = w.Customer.RelationId,
            DefaultArrivalAddressId = w.Customer.AddressId,
            DefaultStopCompositionMode = StopCompositionMode.OneStopPairPerLeg,
            DefaultFirstLastStopRule = FirstLastStopRule.None
        });
        await FlushAsync();
        return type;
    }

    private static TransportTripCreateDto Empty(Guid typeId) => new() { TransportTripTypeId = typeId, PlannedDeparture = At(9) };

    private static void ShouldCarryEveryDefault(TransportTripDto trip, World w)
    {
        trip.CarrierRelationId.ShouldBe(w.Carrier);
        trip.CarrierContactPersonId.ShouldBe(w.Contact);
        trip.Vehicles.OrderBy(v => v.Position).Select(v => v.VehicleId).ShouldBe([w.Van, w.Tractor]);
        trip.Drivers.OrderBy(d => d.Position).Select(d => d.DriverId).ShouldBe([w.Driver2, w.Driver1]);
        trip.StopCompositionMode.ShouldBe(StopCompositionMode.OneStopPairPerLeg);
        trip.PlannedDeparture.ShouldBe(At(9));
        trip.Execution.ShouldBe(TripExecution.Subcontracted, "the type's execution is OwnFleet, but its carrier filled the trip (PO 2026-10-29)");
    }

    /// <summary>A second carrier of the current tenant with one contact.</summary>
    private async Task<(Guid RelationId, Guid ContactId)> OtherCarrierAsync()
    {
        var suffix = TransportRunFixture.NewSuffix();
        var relations = GetRequiredService<IRelationAppService>();
        var carrier = await relations.CreateAsync(new RelationCreateDto
        {
            Code = "OCA" + suffix,
            Name = "Other carrier " + suffix,
            Roles = RelationRoles.Carrier,
            ContactPersons = [new RelationContactPersonDto { FirstName = "Anna", LastName = "Bakker", Role = ContactPersonRole.Planning }]
        });
        await FlushAsync();
        return (carrier.Id, (await relations.GetAsync(carrier.Id)).ContactPersons.Single().Id!.Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_new_trip_is_filled_from_its_type_on_both_paths_and_an_archived_default_is_skipped(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;
        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            var w = await WorldAsync();
            var type = await TypeAsync(w);

            /* The trips service. */
            var created = await Trips.CreateAsync(Empty(type.Id));
            await FlushAsync();
            ShouldCarryEveryDefault(created, w);
            (created.DepartureRelationId, created.DepartureAddressId).ShouldBe(((Guid?)w.Depot.RelationId, (Guid?)w.Depot.AddressId));
            (created.ArrivalRelationId, created.ArrivalAddressId).ShouldBe(((Guid?)w.Customer.RelationId, (Guid?)w.Customer.AddressId));
            (await TripRepository.GetAsync(created.Id)).TenantId.ShouldBe(tenantId);

            /* The order's trip request on Confirm, through the real handler. */
            await OrdersMasterDataAsync();
            var order = await Orders.CreateAsync(new TransportOrderCreateDto
            {
                TransportOrderTypeId = OrderTypeId,
                CustomerRelationId = w.Customer.RelationId,
                OrderDate = Day,
                Lines = [LineOf(10, 1000, StopAt(w.Depot.RelationId, w.Depot.AddressId, Load, windowFrom: At(8), windowTill: At(10)), StopAt(w.Customer.RelationId, w.Customer.AddressId, Unload, windowFrom: At(12), windowTill: At(14)))],
                TripRequest = new TransportOrderTripRequestInputDto
                {
                    TransportTripTypeId = type.Id,
                    DepartureRelationId = w.Depot.RelationId, DepartureAddressId = w.Depot.AddressId,
                    ArrivalRelationId = w.Depot.RelationId, ArrivalAddressId = w.Depot.AddressId,
                    PlannedDeparture = At(9)
                }
            });
            await FlushAsync();
            var legs = await ConfirmAsync(order.Id);
            var fromOrder = (await TripRepository.GetListAsync(t => t.CreatedFromOrderId == order.Id)).ShouldHaveSingleItem();
            var detail = await Trips.GetAsync(fromOrder.Id);
            ShouldCarryEveryDefault(detail, w);
            (detail.ArrivalRelationId, detail.ArrivalAddressId).ShouldBe(((Guid?)w.Depot.RelationId, (Guid?)w.Depot.AddressId), "the request's arrival wins");
            legs.ShouldAllBe(l => l.TripId == fromOrder.Id);
            var request = (await Orders.GetAsync(order.Id)).TripRequest.ShouldNotBeNull();
            request.CarrierRelationId.ShouldBeNull("the fill never writes back into the order");
            request.VehicleIds.ShouldBeEmpty();

            /* An archived default is skipped, no error. */
            await GetRequiredService<IVehicleAppService>().ArchiveAsync(w.Van);
            await GetRequiredService<IDriverAppService>().ArchiveAsync(w.Driver2);
            await FlushAsync();
            var afterArchive = await Trips.CreateAsync(Empty(type.Id));
            afterArchive.Vehicles.Select(v => v.VehicleId).ShouldBe([w.Tractor]);
            afterArchive.Drivers.Select(d => d.DriverId).ShouldBe([w.Driver1]);
            afterArchive.CarrierRelationId.ShouldBe(w.Carrier);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_contact_of_another_carrier_is_refused_on_save_and_a_moved_contact_is_skipped_on_both_paths(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;
        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            var w = await WorldAsync();
            var other = await OtherCarrierAsync();

            /* Saving: another relation's contact on the carrier is not found, on create and on update. */
            var suffix = TransportRunFixture.NewSuffix();
            var create = await Should.ThrowAsync<BusinessException>(() => TripTypes.CreateAsync(new TransportTripTypeCreateDto
            {
                Code = "BAD" + suffix, Names = new LocalizedNameDto { ["en"] = "Bad " + suffix }, Numbering = new NumberSequenceFormatDto { Prefix = "B" + suffix, Separator = "-", Digits = 5 },
                DefaultCarrierRelationId = w.Carrier, DefaultCarrierContactPersonId = other.ContactId
            }));
            create.Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
            create.Data["field"].ShouldBe("defaultCarrierContactPersonId");
            create.Data["value"].ShouldBe(other.ContactId);

            var type = await TypeAsync(w);
            var update = new TransportTripTypeUpdateDto
            {
                Code = type.Code, Names = type.Names, Numbering = type.Numbering, DefaultExecution = type.DefaultExecution, ConcurrencyStamp = type.ConcurrencyStamp,
                DefaultCarrierRelationId = w.Carrier, DefaultCarrierContactPersonId = other.ContactId
            };
            var refused = await Should.ThrowAsync<BusinessException>(() => TripTypes.UpdateAsync(type.Id, update));
            refused.Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
            refused.Data["field"].ShouldBe("defaultCarrierContactPersonId");
            await FlushAsync();
            (await TripTypes.GetAsync(type.Id)).DefaultCarrierContactPersonId.ShouldBe(w.Contact, "the refused update wrote nothing");

            /* Stored past the validation, as if the contact belongs to another relation since the save. */
            var types = GetRequiredService<IRepository<TransportTripType, Guid>>();
            var stored = await types.GetAsync(type.Id);
            stored.SetDefaultCarrier(w.Carrier, other.ContactId);
            await types.UpdateAsync(stored, autoSave: true);

            var created = await Trips.CreateAsync(Empty(type.Id));
            await FlushAsync();
            created.CarrierRelationId.ShouldBe(w.Carrier, "the carrier still fills");
            created.CarrierContactPersonId.ShouldBeNull("the type's contact is not the carrier's");
            created.Execution.ShouldBe(TripExecution.Subcontracted);

            var given = Empty(type.Id);
            given.CarrierRelationId = w.Carrier;
            (await Trips.CreateAsync(given)).CarrierContactPersonId.ShouldBeNull("the type's carrier given gets the type's contact only when it is that carrier's");

            /* The order's trip request without a carrier, through the real handler. */
            await OrdersMasterDataAsync();
            var order = await Orders.CreateAsync(new TransportOrderCreateDto
            {
                TransportOrderTypeId = OrderTypeId,
                CustomerRelationId = w.Customer.RelationId,
                OrderDate = Day,
                Lines = [LineOf(10, 1000, StopAt(w.Depot.RelationId, w.Depot.AddressId, Load, windowFrom: At(8), windowTill: At(10)), StopAt(w.Customer.RelationId, w.Customer.AddressId, Unload, windowFrom: At(12), windowTill: At(14)))],
                TripRequest = new TransportOrderTripRequestInputDto
                {
                    TransportTripTypeId = type.Id,
                    DepartureRelationId = w.Depot.RelationId, DepartureAddressId = w.Depot.AddressId,
                    ArrivalRelationId = w.Depot.RelationId, ArrivalAddressId = w.Depot.AddressId,
                    PlannedDeparture = At(9)
                }
            });
            await FlushAsync();
            await ConfirmAsync(order.Id);
            var fromOrder = await Trips.GetAsync((await TripRepository.GetListAsync(t => t.CreatedFromOrderId == order.Id)).ShouldHaveSingleItem().Id);
            fromOrder.CarrierRelationId.ShouldBe(w.Carrier);
            fromOrder.CarrierContactPersonId.ShouldBeNull();
            fromOrder.Execution.ShouldBe(TripExecution.Subcontracted, "a request without a carrier on a type with a carrier is subcontracted");
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Another_tenant_can_neither_save_nor_receive_this_tenants_defaults(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;
        var otherId = ownDatabase ? SharedTenant : TenantA;
        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            var w = await WorldAsync();
            var type = await TypeAsync(w);
            var lookup = GetRequiredService<IMasterDataReferenceLookup>();
            (await lookup.GetContactPersonsAsync([w.Contact])).ShouldHaveSingleItem().RelationId.ShouldBe(w.Carrier);

            using (CurrentTenant.Change(otherId))
            {
                /* The contact lookup is tenant-filtered: this tenant's contact does not exist for the other tenant. */
                (await lookup.GetContactPersonsAsync([w.Contact])).ShouldBeEmpty();

                /* Saving a type with this tenant's Master Data: every id is unknown to the other tenant. */
                foreach (var (field, apply) in new (string, Action<TransportTripTypeCreateDto>)[]
                         {
                             ("defaultCarrierRelationId", t => t.DefaultCarrierRelationId = w.Carrier),
                             ("defaultVehicleIds", t => t.DefaultVehicleIds = [w.Van]),
                             ("defaultDriverIds", t => t.DefaultDriverIds = [w.Driver1]),
                             ("defaultDepartureRelationId", t => t.DefaultDepartureRelationId = w.Depot.RelationId),
                             ("defaultArrivalRelationId", t => t.DefaultArrivalRelationId = w.Customer.RelationId)
                         })
                {
                    var input = new TransportTripTypeCreateDto
                    {
                        Code = "X" + TransportRunFixture.NewSuffix(), Names = new LocalizedNameDto { ["en"] = "X" }, Numbering = new NumberSequenceFormatDto { Prefix = "X", Separator = "-", Digits = 5 }
                    };
                    apply(input);
                    var error = await Should.ThrowAsync<BusinessException>(() => TripTypes.CreateAsync(input));
                    error.Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound, field);
                    error.Data["field"].ShouldBe(field);
                }

                /* This tenant's type does not exist for the other tenant. */
                var notFound = await Should.ThrowAsync<BusinessException>(() => Trips.CreateAsync(Empty(type.Id)));
                notFound.Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
                notFound.Data["field"].ShouldBe("transportTripTypeId");

                /* This tenant's ids stored on the other tenant's own type (past the validation): its new trip gets none of them. */
                var types = GetRequiredService<IRepository<TransportTripType, Guid>>();
                var own = (await types.GetListAsync()).OrderBy(t => t.IsDefault ? 0 : 1).First();
                own.TenantId.ShouldBe(otherId);
                own.SetDefaultCarrier(w.Carrier, w.Contact);
                own.SetDefaultVehicles(null, [w.Van, w.Tractor]);
                own.SetDefaultDrivers([w.Driver1]);
                own.SetDefaultDeparture(w.Depot.RelationId, w.Depot.AddressId, null);
                own.SetDefaultArrival(w.Customer.RelationId, w.Customer.AddressId);
                await types.UpdateAsync(own, autoSave: true);

                var trip = await Trips.CreateAsync(Empty(own.Id));
                trip.CarrierRelationId.ShouldBeNull();
                trip.CarrierContactPersonId.ShouldBeNull();
                trip.Vehicles.ShouldBeEmpty();
                trip.Drivers.ShouldBeEmpty();
                trip.DepartureRelationId.ShouldBeNull();
                trip.DepartureAddressId.ShouldBeNull();
                trip.ArrivalRelationId.ShouldBeNull();
                trip.ArrivalAddressId.ShouldBeNull();

                /* And its detail never names them. */
                var detail = await TripTypes.GetAsync(own.Id);
                detail.DefaultCarrierRelationName.ShouldNotStartWith("Carrier ");
                detail.DefaultDepartureRelationName.ShouldNotStartWith("Depot ");
                detail.DefaultDrivers.ShouldHaveSingleItem().Name.ShouldNotBe("Jan Jansen");
            }
        });
    }
}
