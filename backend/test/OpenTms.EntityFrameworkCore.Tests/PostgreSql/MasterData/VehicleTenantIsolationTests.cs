using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace OpenTms.PostgreSql.MasterData;

/// <summary>
/// Tenant isolation of design 48 against real PostgreSQL (test risk analysis, Critical; constitution rule 1): a
/// vehicle with its children, a combination and a product relation code written by tenant A (own database) are
/// unreachable from the shared-tier tenant and from the host (host database) through every operation — list, search
/// on name, plate and VIN, lookup, lookup page, detail, type defaults, update, archive, delete, both bulk deletes —
/// and through the published resolver; tenant A's vehicle type, owner, vehicles, product and transport unit cannot be
/// referenced; the shared tenant has its own key space in the host database for every unique key.
/// The services are resolved per call, as a request does (see the finding in VehicleAppServiceTests).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class VehicleTenantIsolationTests : MasterDataPostgreSqlTestBase
{
    private IVehicleAppService Vehicles => GetRequiredService<IVehicleAppService>();
    private IVehicleCombinationAppService Combinations => GetRequiredService<IVehicleCombinationAppService>();
    private IProductRelationCodeAppService Codes => GetRequiredService<IProductRelationCodeAppService>();
    private IProductRelationCodeResolver Resolver => GetRequiredService<IProductRelationCodeResolver>();
    private IVehicleTypeAppService Types => GetRequiredService<IVehicleTypeAppService>();
    private IRelationAppService Relations => GetRequiredService<IRelationAppService>();
    private IDriverAppService Drivers => GetRequiredService<IDriverAppService>();
    private ITransportCharacteristicsAppService Characteristics => GetRequiredService<ITransportCharacteristicsAppService>();
    private IProductAppService Products => GetRequiredService<IProductAppService>();
    private ITransportUnitAppService Units => GetRequiredService<ITransportUnitAppService>();

    private readonly IRepository<Country, Guid> _countries;

    public VehicleTenantIsolationTests()
    {
        _countries = GetRequiredService<IRepository<Country, Guid>>();
    }

    private sealed record Probe(VehicleDto Tractor, VehicleDto Trailer, VehicleCombinationDto Combination, ProductRelationCodeDto Code, Guid TractorType, Guid TrailerType, Guid Owner, Guid Depot, Guid Driver, Guid Characteristic, Guid Product, Guid Unit);

    /// <summary>The same keys in whichever tenant is current: name, plate, VIN, external id, combination name, code.</summary>
    private async Task<Probe> WriteProbeAsync()
    {
        var nl = await _countries.GetAsync(c => c.IsoAlpha2 == "NL");
        var characteristic = (await Characteristics.CreateAsync(new TransportCharacteristicsCreateDto { Code = "QQ-COOL", Name = "Isolation cooling" })).Id;
        var tractorType = (await Types.CreateAsync(new VehicleTypeCreateDto { Name = "QQ tractor", Kind = VehicleTypeKind.Tractor, DefaultLengthCm = 600, TransportCharacteristicsIds = [characteristic] })).Id;
        var trailerType = (await Types.CreateAsync(new VehicleTypeCreateDto { Name = "QQ trailer", Kind = VehicleTypeKind.Trailer })).Id;
        var owner = (await Relations.CreateAsync(new RelationCreateDto { Code = "QQ-OWN", Name = "Isolation carrier", Roles = RelationRoles.Carrier })).Id;
        var depot = (await Relations.CreateAsync(new RelationCreateDto { Code = "QQ-DEP", Name = "Isolation depot", Roles = RelationRoles.Depot | RelationRoles.Customer })).Id;
        var driver = (await Drivers.CreateAsync(new DriverCreateDto { Code = "QQ-DRV", FirstName = "Jan", LastName = "Isolation" })).Id;
        var product = (await Products.CreateAsync(new ProductCreateDto { Name = "QQ isolation bananas" })).Id;
        var unit = (await Units.CreateAsync(new TransportUnitCreateDto { Name = "QQ isolation pallet" })).Id;

        var tractor = await Vehicles.CreateAsync(new VehicleCreateDto
        {
            Name = "QQ isolation truck", VehicleTypeId = tractorType, LicencePlate = "QQ-12-ZZ", RegistrationCountryId = nl.Id, Vin = "WDB9634031L123456",
            OwnerRelationId = owner, HomeDepotRelationId = depot, TransportCharacteristicIds = [characteristic],
            DefaultDrivers = [new VehicleDefaultDriverDto { DriverId = driver, SortOrder = 1 }],
            ExternalIdentifiers = [new VehicleExternalIdentifierDto { Source = "FMS", Identifier = "QQ-4711" }]
        });
        var trailer = await Vehicles.CreateAsync(new VehicleCreateDto { Name = "QQ isolation trailer", VehicleTypeId = trailerType, OwnerRelationId = owner });
        var combination = await Combinations.CreateAsync(new VehicleCombinationCreateDto
        {
            Name = "QQ isolation combination",
            Members = [new() { VehicleId = tractor.Id, Position = 1 }, new() { VehicleId = trailer.Id, Position = 2 }]
        });
        var code = await Codes.CreateAsync(new ProductRelationCodeCreateDto { Code = "QQ-ART-1", RelationId = depot, ProductId = product, TransportUnitId = unit });
        await FlushAsync();
        return new Probe(tractor, trailer, combination, code, tractorType, trailerType, owner, depot, driver, characteristic, product, unit);
    }

    private static VehicleUpdateDto UpdateFrom(VehicleDto dto) => new()
    {
        Name = "Changed", VehicleTypeId = dto.VehicleTypeId, OwnerRelationId = dto.OwnerRelationId, ConcurrencyStamp = dto.ConcurrencyStamp
    };

    private static VehicleListRequestDto VehicleRequest(string? search = null, List<VehicleTypeKind>? kinds = null, Guid? owner = null)
        => new() { WildcardSearch = search, IncludeArchived = true, Kinds = kinds, OwnerRelationId = owner };

    private static ArchivableListRequestDto Archivable(string? search = null) => new() { WildcardSearch = search, IncludeArchived = true };

    [Fact]
    public async Task Vehicles_combinations_codes_and_the_resolver_of_tenant_A_are_unreachable_from_the_shared_tenant_and_the_host()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var a = await WriteProbeAsync();

            foreach (var other in new Guid?[] { SharedTenant, null })
            {
                using (CurrentTenant.Change(other))
                {
                    /* Vehicles: list (and its searches over name, plate and VIN), lookups, detail and every command. */
                    (await Vehicles.GetListAsync(VehicleRequest())).TotalCount.ShouldBe(0);
                    (await Vehicles.GetListAsync(VehicleRequest(search: "isolation"))).Rows.ShouldBeEmpty();
                    (await Vehicles.GetListAsync(VehicleRequest(search: "qq-12"))).Rows.ShouldBeEmpty();
                    (await Vehicles.GetListAsync(VehicleRequest(search: "031l123"))).Rows.ShouldBeEmpty();
                    (await Vehicles.GetListAsync(VehicleRequest(kinds: [VehicleTypeKind.Tractor, VehicleTypeKind.Trailer]))).Rows.ShouldBeEmpty();
                    (await Vehicles.GetListAsync(VehicleRequest(owner: a.Owner))).Rows.ShouldBeEmpty();
                    (await Vehicles.GetLookupAsync()).ShouldBeEmpty();
                    (await Vehicles.GetLookupPageAsync("isolation")).TotalCount.ShouldBe(0);
                    (await Vehicles.GetLookupPageAsync(kinds: [VehicleTypeKind.Tractor])).Items.ShouldBeEmpty();
                    (await Vehicles.GetLookupPageAsync(ownerRelationId: a.Owner)).Items.ShouldBeEmpty();
                    await Should.ThrowAsync<EntityNotFoundException>(() => Vehicles.GetAsync(a.Tractor.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Vehicles.GetTypeDefaultsAsync(a.TractorType));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Vehicles.UpdateAsync(a.Tractor.Id, UpdateFrom(a.Tractor)));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Vehicles.ArchiveAsync(a.Tractor.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Vehicles.UnarchiveAsync(a.Tractor.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Vehicles.DeleteAsync(a.Tractor.Id));
                    (await Vehicles.BulkDeleteAsync(new VehicleBulkDeleteRequestDto { Selection = Explicit(a.Tractor.Id, a.Trailer.Id) })).DeletedCount.ShouldBe(0);
                    (await Vehicles.BulkDeleteAsync(new VehicleBulkDeleteRequestDto { Selection = FilterBased(), IncludeArchived = true })).DeletedCount.ShouldBe(0);

                    /* Combinations. */
                    (await Combinations.GetListAsync(Archivable())).TotalCount.ShouldBe(0);
                    (await Combinations.GetListAsync(Archivable("isolation"))).Rows.ShouldBeEmpty();
                    (await Combinations.GetLookupAsync()).ShouldBeEmpty();
                    await Should.ThrowAsync<EntityNotFoundException>(() => Combinations.GetAsync(a.Combination.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Combinations.UpdateAsync(a.Combination.Id, new VehicleCombinationUpdateDto { Name = "X", Members = [], ConcurrencyStamp = a.Combination.ConcurrencyStamp }));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Combinations.ArchiveAsync(a.Combination.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Combinations.UnarchiveAsync(a.Combination.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Combinations.DeleteAsync(a.Combination.Id));
                    (await Combinations.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = Explicit(a.Combination.Id) })).DeletedCount.ShouldBe(0);
                    (await Combinations.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = FilterBased(), IncludeArchived = true })).DeletedCount.ShouldBe(0);

                    /* Product relation codes and the published resolver. */
                    (await Codes.GetListAsync(Archivable())).TotalCount.ShouldBe(0);
                    (await Codes.GetListAsync(Archivable("qq-art"))).Rows.ShouldBeEmpty();
                    (await Codes.GetListAsync(Archivable("isolation"))).Rows.ShouldBeEmpty("the destination and product names of tenant A");
                    (await Codes.GetLookupAsync()).ShouldBeEmpty();
                    (await Resolver.ResolveAsync(["qq-art-1", "QQ-ART-1"])).ShouldBeEmpty();
                    await Should.ThrowAsync<EntityNotFoundException>(() => Codes.GetAsync(a.Code.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Codes.UpdateAsync(a.Code.Id, new ProductRelationCodeUpdateDto { Code = "X", ProductId = a.Product, ConcurrencyStamp = a.Code.ConcurrencyStamp }));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Codes.ArchiveAsync(a.Code.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Codes.UnarchiveAsync(a.Code.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Codes.DeleteAsync(a.Code.Id));
                    (await Codes.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = Explicit(a.Code.Id) })).DeletedCount.ShouldBe(0);
                    (await Codes.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = FilterBased(), IncludeArchived = true })).DeletedCount.ShouldBe(0);

                    /* Tenant A's reference ids are refused. */
                    (await Should.ThrowAsync<BusinessException>(() => Vehicles.CreateAsync(new VehicleCreateDto { Name = "QQ borrowed", VehicleTypeId = a.TractorType, OwnerRelationId = a.Owner })))
                        .Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);
                    (await Should.ThrowAsync<BusinessException>(() => Combinations.CreateAsync(new VehicleCombinationCreateDto
                    {
                        Name = "QQ borrowed", Members = [new() { VehicleId = a.Tractor.Id, Position = 1 }, new() { VehicleId = a.Trailer.Id, Position = 2 }]
                    }))).Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);
                    foreach (var dto in new[]
                             {
                                 new ProductRelationCodeCreateDto { Code = "QQ-X", RelationId = a.Depot },
                                 new ProductRelationCodeCreateDto { Code = "QQ-X", ProductId = a.Product },
                                 new ProductRelationCodeCreateDto { Code = "QQ-X", TransportUnitId = a.Unit },
                             })
                    {
                        (await Should.ThrowAsync<BusinessException>(() => Codes.CreateAsync(dto))).Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);
                    }

                    await FlushAsync();
                }
            }

            /* The shared tenant writes the same keys in the host database: its own key space (the per-tenant unique
             * indexes), its own owner, type, driver, characteristic, combination and code. */
            Probe shared;
            using (CurrentTenant.Change(SharedTenant))
            {
                shared = await WriteProbeAsync();
                shared.Tractor.Id.ShouldNotBe(a.Tractor.Id);

                /* And it cannot borrow tenant A's owner or driver for its own type either. */
                (await Should.ThrowAsync<BusinessException>(() => Vehicles.CreateAsync(new VehicleCreateDto { Name = "QQ borrowed", VehicleTypeId = shared.TractorType, OwnerRelationId = a.Owner })))
                    .Data["field"].ShouldBe("ownerRelationId");
                (await Should.ThrowAsync<BusinessException>(() => Vehicles.CreateAsync(new VehicleCreateDto
                {
                    Name = "QQ borrowed", VehicleTypeId = shared.TractorType, OwnerRelationId = shared.Owner, DefaultDrivers = [new VehicleDefaultDriverDto { DriverId = a.Driver, SortOrder = 1 }]
                }))).Data["field"].ShouldBe("defaultDrivers");
                (await Should.ThrowAsync<BusinessException>(() => Vehicles.CreateAsync(new VehicleCreateDto
                {
                    Name = "QQ borrowed", VehicleTypeId = shared.TractorType, OwnerRelationId = shared.Owner, TransportCharacteristicIds = [a.Characteristic]
                }))).Data["field"].ShouldBe("transportCharacteristicIds");

                (await Vehicles.GetListAsync(VehicleRequest(search: "isolation"))).Rows.Select(r => r.Id).ShouldBe([shared.Tractor.Id, shared.Trailer.Id], ignoreOrder: true);
                (await Vehicles.GetLookupPageAsync("isolation truck")).Items.ShouldHaveSingleItem().Id.ShouldBe(shared.Tractor.Id);
                (await Resolver.ResolveAsync(["qq-art-1"])).ShouldHaveSingleItem().Id.ShouldBe(shared.Code.Id);
                (await Vehicles.GetAsync(shared.Tractor.Id)).CombinationName.ShouldBe("QQ isolation combination");
            }

            /* The host shares the host database with the shared tenant and sees none of its rows. */
            using (CurrentTenant.Change(null))
            {
                (await Vehicles.GetListAsync(VehicleRequest(search: "isolation"))).Rows.ShouldBeEmpty();
                (await Vehicles.GetLookupPageAsync("isolation")).TotalCount.ShouldBe(0);
                (await Combinations.GetListAsync(Archivable("isolation"))).Rows.ShouldBeEmpty();
                (await Codes.GetListAsync(Archivable("qq-art"))).Rows.ShouldBeEmpty();
                (await Resolver.ResolveAsync(["qq-art-1"])).ShouldBeEmpty();
                await Should.ThrowAsync<EntityNotFoundException>(() => Vehicles.GetAsync(shared.Tractor.Id));
            }

            /* Tenant A still has its own rows with their children. */
            var mine = await Vehicles.GetAsync(a.Tractor.Id);
            mine.LicencePlate.ShouldBe("QQ12ZZ");
            mine.DefaultDrivers.ShouldHaveSingleItem().DriverId.ShouldBe(a.Driver);
            mine.ExternalIdentifiers.ShouldHaveSingleItem().Identifier.ShouldBe("QQ-4711");
            mine.Characteristics.Select(c => (c.Id, c.Origin)).ShouldBe([(a.Characteristic, VehicleCharacteristicOrigin.Type)]);
            mine.CombinationName.ShouldBe("QQ isolation combination");
            (await Combinations.GetAsync(a.Combination.Id)).Members.Count.ShouldBe(2);
            (await Resolver.ResolveAsync(["qq-art-1"])).ShouldHaveSingleItem().Id.ShouldBe(a.Code.Id);
            (await Vehicles.GetListAsync(VehicleRequest(search: "isolation"))).Rows.Select(r => r.Id).ShouldBe([a.Tractor.Id, a.Trailer.Id], ignoreOrder: true);
        });
    }

    [Fact]
    public async Task The_service_answers_a_duplicate_key_with_the_friendly_conflict_before_the_index_on_PostgreSQL()
    {
        /* "Friendly 409 through the service" (design 48, Uniqueness): DuplicateKey and VehicleAlreadyInCombination are
         * conflict codes, raised by the service's checks against the real database before the index would fire. */
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var a = await WriteProbeAsync();
            var nl = await _countries.GetAsync(c => c.IsoAlpha2 == "NL");

            async Task<BusinessException> Fails(Func<Task> action, string code)
            {
                var ex = await Should.ThrowAsync<BusinessException>(action);
                ex.Code.ShouldBe(code);
                MasterDataErrorCodes.ConflictCodes.ShouldContain(code);
                return ex;
            }

            VehicleCreateDto Vehicle(string name, Action<VehicleCreateDto>? configure = null)
            {
                var dto = new VehicleCreateDto { Name = name, VehicleTypeId = a.TrailerType, OwnerRelationId = a.Owner };
                configure?.Invoke(dto);
                return dto;
            }

            (await Fails(() => Vehicles.CreateAsync(Vehicle(" QQ isolation truck ")), MasterDataErrorCodes.DuplicateKey)).Data["field"].ShouldBe("name");
            (await Fails(() => Vehicles.CreateAsync(Vehicle("QQ 2", d => { d.LicencePlate = "qq 12 zz"; d.RegistrationCountryId = nl.Id; })), MasterDataErrorCodes.DuplicateKey)).Data["field"].ShouldBe("licencePlate");
            (await Fails(() => Vehicles.CreateAsync(Vehicle("QQ 2", d => d.Vin = "wdb9634031l123456")), MasterDataErrorCodes.DuplicateKey)).Data["field"].ShouldBe("vin");
            (await Fails(() => Vehicles.CreateAsync(Vehicle("QQ 2", d => d.ExternalIdentifiers = [new VehicleExternalIdentifierDto { Source = "FMS", Identifier = "QQ-4711" }])), MasterDataErrorCodes.DuplicateKey)).Data["field"].ShouldBe("externalIdentifiers");
            (await Fails(() => Codes.CreateAsync(new ProductRelationCodeCreateDto { Code = "qq-art-1", ProductId = a.Product }), MasterDataErrorCodes.DuplicateKey)).Data["field"].ShouldBe("code");

            var trailer2 = await Vehicles.CreateAsync(Vehicle("QQ trailer 2"));
            (await Fails(() => Combinations.CreateAsync(new VehicleCombinationCreateDto
            {
                Name = "QQ isolation combination", Members = [new() { VehicleId = a.Tractor.Id, Position = 1 }, new() { VehicleId = trailer2.Id, Position = 2 }]
            }), MasterDataErrorCodes.DuplicateKey)).Data["field"].ShouldBe("name");
            await Fails(() => Combinations.CreateAsync(new VehicleCombinationCreateDto
            {
                Name = "QQ second", Members = [new() { VehicleId = a.Tractor.Id, Position = 1 }, new() { VehicleId = trailer2.Id, Position = 2 }]
            }), MasterDataErrorCodes.VehicleAlreadyInCombination);
        });
    }
}
