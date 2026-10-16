using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.EntityFrameworkCore;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;
using Xunit;

namespace OpenTms.PostgreSql.MasterData;

/// <summary>
/// The unique, partial unique and check constraints and the foreign keys of design 48, exercised through the
/// repositories so the application service's friendly check is bypassed (test risk analysis "Uniqueness" and
/// "Migration and every index"). Risk class: Critical — the index is the one enforced place for the vehicle name,
/// the plate per registration country, the VIN, the combination name, the normalised product relation code, the
/// vehicle external id and the one-active-combination-per-vehicle rule; NULLS NOT DISTINCT makes host rows unique
/// too, a soft delete frees a key, and two tenants of one database each have their own key space.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class VehicleUniqueIndexTests : MasterDataPostgreSqlTestBase
{
    private readonly IRepository<Vehicle, Guid> _vehicles;
    private readonly IRepository<VehicleType, Guid> _types;
    private readonly IRepository<Relation, Guid> _relations;
    private readonly IRepository<VehicleCombination, Guid> _combinations;
    private readonly IRepository<ProductRelationCode, Guid> _codes;
    private readonly IRepository<Product, Guid> _products;
    private readonly IRepository<Driver, Guid> _drivers;
    private readonly IRepository<Country, Guid> _countries;
    private readonly IDbContextProvider<MasterDataDbContext> _contextProvider;
    private readonly IGuidGenerator _guids;

    public VehicleUniqueIndexTests()
    {
        _vehicles = GetRequiredService<IRepository<Vehicle, Guid>>();
        _types = GetRequiredService<IRepository<VehicleType, Guid>>();
        _relations = GetRequiredService<IRepository<Relation, Guid>>();
        _combinations = GetRequiredService<IRepository<VehicleCombination, Guid>>();
        _codes = GetRequiredService<IRepository<ProductRelationCode, Guid>>();
        _products = GetRequiredService<IRepository<Product, Guid>>();
        _drivers = GetRequiredService<IRepository<Driver, Guid>>();
        _countries = GetRequiredService<IRepository<Country, Guid>>();
        _contextProvider = GetRequiredService<IDbContextProvider<MasterDataDbContext>>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    private sealed record Refs(Guid Tractor, Guid Trailer, Guid Owner);

    /// <summary>A tractor type, a trailer type and an owner in <paramref name="tenant"/>: the FK targets every vehicle needs.</summary>
    private async Task<Refs> RefsAsync(Guid? tenant)
    {
        var tractor = new VehicleType(_guids.Create(), tenant, "QQ tractor", VehicleTypeKind.Tractor);
        var trailer = new VehicleType(_guids.Create(), tenant, "QQ trailer", VehicleTypeKind.Trailer);
        var owner = new Relation(_guids.Create(), tenant, "QQ-OWN", "QQ owner", RelationRoles.Carrier);
        await _types.InsertAsync(tractor, autoSave: true);
        await _types.InsertAsync(trailer, autoSave: true);
        await _relations.InsertAsync(owner, autoSave: true);
        return new Refs(tractor.Id, trailer.Id, owner.Id);
    }

    private Vehicle NewVehicle(Guid? tenant, Refs refs, string name, string? plate = null, Guid? country = null, string? vin = null, bool trailer = false)
    {
        var vehicle = new Vehicle(_guids.Create(), tenant, name, trailer ? refs.Trailer : refs.Tractor, refs.Owner);
        vehicle.SetRegistration(plate, country, vin);
        return vehicle;
    }

    private VehicleCombination NewCombination(Guid? tenant, string name, Guid tractor, Guid trailer, bool archived = false)
    {
        var combination = new VehicleCombination(_guids.Create(), tenant, name);
        if (archived)
        {
            combination.Archive();
        }

        combination.SetMembers([(tractor, VehicleTypeKind.Tractor), (trailer, VehicleTypeKind.Trailer)]);
        return combination;
    }

    private async Task<ProductRelationCode> NewCodeAsync(Guid? tenant, string code)
    {
        var product = new Product(_guids.Create(), tenant, "QQ product " + code);
        await _products.InsertAsync(product, autoSave: true);
        var entity = new ProductRelationCode(_guids.Create(), tenant, code);
        entity.SetTargets(null, product.Id, null);
        return entity;
    }

    private static async Task<PostgresException> ShouldViolateAsync(Func<Task> action, string constraint, string sqlState)
    {
        var ex = await Should.ThrowAsync<DbUpdateException>(action);
        var postgres = ex.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(sqlState);
        postgres.ConstraintName.ShouldBe(constraint);
        return postgres;
    }

    private static Task<PostgresException> ShouldViolateUniqueIndexAsync(Func<Task> insert, string indexName)
        => ShouldViolateAsync(insert, indexName, PostgresErrorCodes.UniqueViolation);

    // ── Vehicle keys ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_duplicate_vehicle_name_is_refused_by_the_index_and_freed_by_a_soft_delete()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var refs = await RefsAsync(TenantA);
            await _vehicles.InsertAsync(NewVehicle(TenantA, refs, "QQ truck"), autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _vehicles.InsertAsync(NewVehicle(TenantA, refs, "QQ truck"), autoSave: true), "IX_Vehicles_TenantId_NormalizedName");
        });

        /* The key ignores case (PO 2026-10-16): "qq TRUCK" is the same name. */
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var refs = await RefsAsync(TenantA);
            await _vehicles.InsertAsync(NewVehicle(TenantA, refs, "QQ truck"), autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _vehicles.InsertAsync(NewVehicle(TenantA, refs, "qq TRUCK"), autoSave: true), "IX_Vehicles_TenantId_NormalizedName");
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var refs = await RefsAsync(TenantA);
            var first = NewVehicle(TenantA, refs, "QQ truck", "QQ12ZZ", (await _countries.GetAsync(c => c.IsoAlpha2 == "NL")).Id, "WDB9634031L123456");
            await _vehicles.InsertAsync(first, autoSave: true);
            await _vehicles.DeleteAsync(first, autoSave: true);

            /* Name, plate and VIN are all free again. */
            var second = NewVehicle(TenantA, refs, "QQ truck", "QQ12ZZ", first.RegistrationCountryId, "WDB9634031L123456");
            await _vehicles.InsertAsync(second, autoSave: true);
            (await _vehicles.GetAsync(v => v.Name == "QQ truck")).Id.ShouldBe(second.Id);
        });
    }

    [Fact]
    public async Task A_plate_is_unique_per_registration_country_and_vehicles_without_a_plate_are_fine()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var refs = await RefsAsync(TenantA);
            var nl = (await _countries.GetAsync(c => c.IsoAlpha2 == "NL")).Id;
            var be = (await _countries.GetAsync(c => c.IsoAlpha2 == "BE")).Id;

            await _vehicles.InsertAsync(NewVehicle(TenantA, refs, "QQ 1", "12-abc-3", nl), autoSave: true);
            await _vehicles.InsertAsync(NewVehicle(TenantA, refs, "QQ 2", "12ABC3", be), autoSave: true);
            await _vehicles.InsertAsync(NewVehicle(TenantA, refs, "QQ 3"), autoSave: true);
            await _vehicles.InsertAsync(NewVehicle(TenantA, refs, "QQ 4"), autoSave: true);
            await _vehicles.InsertAsync(NewVehicle(TenantA, refs, "QQ 5", country: nl), autoSave: true);
            await _vehicles.InsertAsync(NewVehicle(TenantA, refs, "QQ 6", country: nl), autoSave: true);

            await ShouldViolateUniqueIndexAsync(() => _vehicles.InsertAsync(NewVehicle(TenantA, refs, "QQ 7", "12 ABC 3", nl), autoSave: true), "IX_Vehicles_TenantId_RegistrationCountryId_LicencePlate");
        });
    }

    [Fact]
    public async Task A_duplicate_VIN_is_refused_by_the_index_but_vehicles_without_one_are_fine()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var refs = await RefsAsync(TenantA);
            await _vehicles.InsertAsync(NewVehicle(TenantA, refs, "QQ 1", vin: "wdb9634031l123456"), autoSave: true);
            await _vehicles.InsertAsync(NewVehicle(TenantA, refs, "QQ 2"), autoSave: true);
            await _vehicles.InsertAsync(NewVehicle(TenantA, refs, "QQ 3"), autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _vehicles.InsertAsync(NewVehicle(TenantA, refs, "QQ 4", vin: "WDB9634031L123456"), autoSave: true), "IX_Vehicles_TenantId_Vin");
        });
    }

    [Fact]
    public async Task The_same_source_and_identifier_on_two_vehicles_of_a_tenant_is_refused_by_the_index()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var refs = await RefsAsync(TenantA);
            var one = NewVehicle(TenantA, refs, "QQ 1");
            one.SetExternalIdentifiers([new VehicleExternalIdentifierInput(null, "FMS", "4711")], _guids.Create);
            await _vehicles.InsertAsync(one, autoSave: true);

            var two = NewVehicle(TenantA, refs, "QQ 2");
            two.SetExternalIdentifiers([new VehicleExternalIdentifierInput(null, "EDI", "4711"), new VehicleExternalIdentifierInput(null, "FMS", "4712")], _guids.Create);
            await _vehicles.InsertAsync(two, autoSave: true);

            var three = NewVehicle(TenantA, refs, "QQ 3");
            three.SetExternalIdentifiers([new VehicleExternalIdentifierInput(null, "FMS", "4711")], _guids.Create);
            await ShouldViolateUniqueIndexAsync(() => _vehicles.InsertAsync(three, autoSave: true), "IX_VehicleExternalIdentifiers_TenantId_Source_Identifier");
        });
    }

    // ── Combination keys ─────────────────────────────────────────────────────

    [Fact]
    public async Task A_duplicate_combination_name_is_refused_by_the_index()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var refs = await RefsAsync(TenantA);
            var vehicles = Enumerable.Range(0, 4).Select(i => NewVehicle(TenantA, refs, $"QQ {i}", trailer: i % 2 == 1)).ToList();
            await _vehicles.InsertManyAsync(vehicles, autoSave: true);

            await _combinations.InsertAsync(NewCombination(TenantA, "QQ combination", vehicles[0].Id, vehicles[1].Id), autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _combinations.InsertAsync(NewCombination(TenantA, "QQ combination", vehicles[2].Id, vehicles[3].Id), autoSave: true), "IX_VehicleCombinations_TenantId_Name");
        });
    }

    [Fact]
    public async Task A_vehicle_in_two_active_combinations_is_refused_by_the_partial_index_but_an_archived_one_does_not_count()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var refs = await RefsAsync(TenantA);
            var tractor = NewVehicle(TenantA, refs, "QQ tractor");
            var tractor2 = NewVehicle(TenantA, refs, "QQ tractor 2");
            var trailer = NewVehicle(TenantA, refs, "QQ trailer", trailer: true);
            await _vehicles.InsertManyAsync([tractor, tractor2, trailer], autoSave: true);

            /* Archived combinations hold the trailer inactively, as many as there are. */
            await _combinations.InsertAsync(NewCombination(TenantA, "QQ archived 1", tractor.Id, trailer.Id, archived: true), autoSave: true);
            await _combinations.InsertAsync(NewCombination(TenantA, "QQ archived 2", tractor2.Id, trailer.Id, archived: true), autoSave: true);
            var active = NewCombination(TenantA, "QQ active", tractor.Id, trailer.Id);
            await _combinations.InsertAsync(active, autoSave: true);

            await ShouldViolateUniqueIndexAsync(() => _combinations.InsertAsync(NewCombination(TenantA, "QQ second active", tractor2.Id, trailer.Id), autoSave: true), "IX_VehicleCombinationMembers_TenantId_VehicleId_Active");
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            /* Unarchiving through the aggregate into a conflict is refused by the index too. */
            var refs = await RefsAsync(TenantA);
            var tractor = NewVehicle(TenantA, refs, "QQ tractor");
            var tractor2 = NewVehicle(TenantA, refs, "QQ tractor 2");
            var trailer = NewVehicle(TenantA, refs, "QQ trailer", trailer: true);
            await _vehicles.InsertManyAsync([tractor, tractor2, trailer], autoSave: true);
            var archived = NewCombination(TenantA, "QQ archived", tractor.Id, trailer.Id, archived: true);
            await _combinations.InsertAsync(archived, autoSave: true);
            await _combinations.InsertAsync(NewCombination(TenantA, "QQ active", tractor2.Id, trailer.Id), autoSave: true);

            var loaded = await _combinations.GetAsync(archived.Id);
            loaded.Unarchive();
            await ShouldViolateUniqueIndexAsync(() => _combinations.UpdateAsync(loaded, autoSave: true), "IX_VehicleCombinationMembers_TenantId_VehicleId_Active");
        });
    }

    [Fact]
    public async Task A_member_position_outside_1_to_4_is_refused_by_the_check_constraint()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var refs = await RefsAsync(TenantA);
            var tractor = NewVehicle(TenantA, refs, "QQ tractor");
            var trailer = NewVehicle(TenantA, refs, "QQ trailer", trailer: true);
            await _vehicles.InsertManyAsync([tractor, trailer], autoSave: true);
            var combination = NewCombination(TenantA, "QQ", tractor.Id, trailer.Id);
            await _combinations.InsertAsync(combination, autoSave: true);
            var context = await _contextProvider.GetDbContextAsync();

            foreach (var position in new[] { 0, 5 })
            {
                var transaction = (NpgsqlTransaction)Microsoft.EntityFrameworkCore.Storage.DbContextTransactionExtensions.GetDbTransaction(context.Database.CurrentTransaction!);
                await transaction.SaveAsync("check_probe");
                var ex = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlAsync($"UPDATE masterdata.\"VehicleCombinationMembers\" SET \"Position\" = {position} WHERE \"VehicleId\" = {trailer.Id}"));
                ex.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
                ex.ConstraintName.ShouldBe("CK_VehicleCombinationMembers_Position");
                await transaction.RollbackAsync("check_probe");
            }
        });
    }

    // ── Product relation code ────────────────────────────────────────────────

    [Fact]
    public async Task A_code_differing_only_in_case_is_refused_by_the_normalised_code_index_and_freed_by_a_soft_delete()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await _codes.InsertAsync(await NewCodeAsync(TenantA, "Art-4711"), autoSave: true);
            await ShouldViolateUniqueIndexAsync(async () => await _codes.InsertAsync(await NewCodeAsync(TenantA, " ART-4711 "), autoSave: true), "IX_ProductRelationCodes_TenantId_NormalizedCode");
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var first = await NewCodeAsync(TenantA, "Art-4711");
            await _codes.InsertAsync(first, autoSave: true);
            await _codes.DeleteAsync(first, autoSave: true);
            var second = await NewCodeAsync(TenantA, "art-4711");
            await _codes.InsertAsync(second, autoSave: true);
            (await _codes.GetAsync(c => c.NormalizedCode == "ART-4711")).Id.ShouldBe(second.Id);
        });
    }

    // ── Host rows and two tenants of one database ────────────────────────────

    [Fact]
    public async Task Two_tenants_of_the_host_database_have_their_own_key_spaces_and_host_rows_are_unique_too_NULLS_NOT_DISTINCT()
    {
        await InRolledBackTransactionAsync(SharedTenant, async () =>
        {
            var refs = await RefsAsync(SharedTenant);
            var shared = NewVehicle(SharedTenant, refs, "QQ truck", vin: "WDB9634031L123456");
            shared.SetExternalIdentifiers([new VehicleExternalIdentifierInput(null, "FMS", "4711")], _guids.Create);
            await _vehicles.InsertAsync(shared, autoSave: true);
            await _codes.InsertAsync(await NewCodeAsync(SharedTenant, "QQ-ART"), autoSave: true);

            using (CurrentTenant.Change(null))
            {
                var hostRefs = await RefsAsync(null);
                var host = NewVehicle(null, hostRefs, "QQ truck", vin: "WDB9634031L123456");
                host.SetExternalIdentifiers([new VehicleExternalIdentifierInput(null, "FMS", "4711")], _guids.Create);
                await _vehicles.InsertAsync(host, autoSave: true);
                await _codes.InsertAsync(await NewCodeAsync(null, "QQ-ART"), autoSave: true);
                (await _vehicles.CountAsync(v => v.Name == "QQ truck")).ShouldBe(1);

                /* NULLS NOT DISTINCT: a second host row with the same key is refused, the external id last. */
                await ShouldViolateUniqueIndexAsync(() => _vehicles.InsertAsync(NewVehicle(null, hostRefs, "QQ truck"), autoSave: true), "IX_Vehicles_TenantId_NormalizedName");
            }
        });

        await InRolledBackTransactionAsync(null, async () =>
        {
            var refs = await RefsAsync(null);
            await _vehicles.InsertAsync(NewVehicle(null, refs, "QQ 1", vin: "WDB9634031L123456"), autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _vehicles.InsertAsync(NewVehicle(null, refs, "QQ 2", vin: "WDB9634031L123456"), autoSave: true), "IX_Vehicles_TenantId_Vin");
        });

        await InRolledBackTransactionAsync(null, async () =>
        {
            await _codes.InsertAsync(await NewCodeAsync(null, "QQ-ART"), autoSave: true);
            await ShouldViolateUniqueIndexAsync(async () => await _codes.InsertAsync(await NewCodeAsync(null, "qq-art"), autoSave: true), "IX_ProductRelationCodes_TenantId_NormalizedCode");
        });

        await InRolledBackTransactionAsync(null, async () =>
        {
            var refs = await RefsAsync(null);
            var one = NewVehicle(null, refs, "QQ 1");
            one.SetExternalIdentifiers([new VehicleExternalIdentifierInput(null, "FMS", "4711")], _guids.Create);
            await _vehicles.InsertAsync(one, autoSave: true);
            var two = NewVehicle(null, refs, "QQ 2");
            two.SetExternalIdentifiers([new VehicleExternalIdentifierInput(null, "FMS", "4711")], _guids.Create);
            await ShouldViolateUniqueIndexAsync(() => _vehicles.InsertAsync(two, autoSave: true), "IX_VehicleExternalIdentifiers_TenantId_Source_Identifier");
        });

        await InRolledBackTransactionAsync(null, async () =>
        {
            var refs = await RefsAsync(null);
            var vehicles = Enumerable.Range(0, 3).Select(i => NewVehicle(null, refs, $"QQ {i}", trailer: i == 2)).ToList();
            await _vehicles.InsertManyAsync(vehicles, autoSave: true);
            await _combinations.InsertAsync(NewCombination(null, "QQ 1", vehicles[0].Id, vehicles[2].Id), autoSave: true);
            await ShouldViolateUniqueIndexAsync(() => _combinations.InsertAsync(NewCombination(null, "QQ 2", vehicles[1].Id, vehicles[2].Id), autoSave: true), "IX_VehicleCombinationMembers_TenantId_VehicleId_Active");
        });
    }

    // ── Foreign keys: restrict and cascade ───────────────────────────────────

    [Fact]
    public async Task The_references_of_a_vehicle_code_and_member_are_restricted_and_the_children_cascade_on_a_hard_delete()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var refs = await RefsAsync(TenantA);
            var vehicle = new Vehicle(_guids.Create(), TenantA, "QQ", refs.Tractor, Guid.NewGuid());
            await ShouldViolateAsync(() => _vehicles.InsertAsync(vehicle, autoSave: true), "FK_Vehicles_Relations_OwnerRelationId", PostgresErrorCodes.ForeignKeyViolation);
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var refs = await RefsAsync(TenantA);
            var driver = new Driver(_guids.Create(), TenantA, "QQ-D1", "Jan", "Jansen");
            await _drivers.InsertAsync(driver, autoSave: true);
            var context = await _contextProvider.GetDbContextAsync();
            var characteristic = new TransportCharacteristics(_guids.Create(), TenantA, "QQ-ADR", "Dangerous goods");
            context.Add(characteristic);
            await context.SaveChangesAsync();

            var tractor = NewVehicle(TenantA, refs, "QQ tractor");
            tractor.SetDefaultDrivers([driver.Id]);
            tractor.SetTransportCharacteristics([characteristic.Id]);
            tractor.SetOrganizationUnits([Guid.NewGuid()]);
            tractor.SetExternalIdentifiers([new VehicleExternalIdentifierInput(null, "FMS", "1")], _guids.Create);
            var trailer = NewVehicle(TenantA, refs, "QQ trailer", trailer: true);
            await _vehicles.InsertManyAsync([tractor, trailer], autoSave: true);
            var combination = NewCombination(TenantA, "QQ", tractor.Id, trailer.Id);
            await _combinations.InsertAsync(combination, autoSave: true);
            var code = await NewCodeAsync(TenantA, "QQ-ART");
            await _codes.InsertAsync(code, autoSave: true);

            async Task Restricted(FormattableString sql, string constraint)
            {
                var transaction = (NpgsqlTransaction)Microsoft.EntityFrameworkCore.Storage.DbContextTransactionExtensions.GetDbTransaction(context.Database.CurrentTransaction!);
                await transaction.SaveAsync("fk_probe");
                var ex = await Should.ThrowAsync<PostgresException>(() => context.Database.ExecuteSqlAsync(sql));
                ex.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
                ex.ConstraintName.ShouldBe(constraint);
                await transaction.RollbackAsync("fk_probe");
            }

            /* Restrict: soft deletes never remove the row; a hard delete from under a reference is refused. */
            await Restricted($"DELETE FROM masterdata.\"VehicleTypes\" WHERE \"Id\" = {refs.Tractor}", "FK_Vehicles_VehicleTypes_VehicleTypeId");
            await Restricted($"DELETE FROM masterdata.\"Relations\" WHERE \"Id\" = {refs.Owner}", "FK_Vehicles_Relations_OwnerRelationId");
            await Restricted($"DELETE FROM masterdata.\"Drivers\" WHERE \"Id\" = {driver.Id}", "FK_VehicleDefaultDrivers_Drivers_DriverId");
            await Restricted($"DELETE FROM masterdata.\"Vehicles\" WHERE \"Id\" = {trailer.Id}", "FK_VehicleCombinationMembers_Vehicles_VehicleId");
            await Restricted($"DELETE FROM masterdata.\"Products\" WHERE \"Id\" = {code.ProductId}", "FK_ProductRelationCodes_Products_ProductId");

            /* Cascade: the combination takes its members; the vehicle its four child tables; a characteristic its join rows. */
            await context.Database.ExecuteSqlAsync($"DELETE FROM masterdata.\"VehicleCombinations\" WHERE \"Id\" = {combination.Id}");
            await context.Database.ExecuteSqlAsync($"DELETE FROM masterdata.\"Vehicles\" WHERE \"Id\" = {tractor.Id}");
            context.ChangeTracker.Clear();
            (await context.Set<VehicleCombinationMember>().CountAsync(m => m.VehicleCombinationId == combination.Id)).ShouldBe(0);
            (await context.Set<VehicleTransportCharacteristic>().CountAsync(c => c.VehicleId == tractor.Id)).ShouldBe(0);
            (await context.Set<VehicleDefaultDriver>().CountAsync(d => d.VehicleId == tractor.Id)).ShouldBe(0);
            (await context.Set<VehicleOrganizationUnit>().CountAsync(o => o.VehicleId == tractor.Id)).ShouldBe(0);
            (await context.Set<VehicleExternalIdentifier>().CountAsync(e => e.VehicleId == tractor.Id)).ShouldBe(0);
            await context.Database.ExecuteSqlAsync($"DELETE FROM masterdata.\"Vehicles\" WHERE \"Id\" = {trailer.Id}");
        });
    }
}
