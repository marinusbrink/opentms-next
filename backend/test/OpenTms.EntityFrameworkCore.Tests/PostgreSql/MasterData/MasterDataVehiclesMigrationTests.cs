using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using OpenTms.MasterData.EntityFrameworkCore;
using OpenTms.MasterData.EntityFrameworkCore.Migrations;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace OpenTms.PostgreSql.MasterData;

/// <summary>
/// The MasterDataVehicles migration on the databases the real DbMigrator created (design 48, "Migration strategy").
/// Risk class: Critical — applied on the host and on every tenant database after MasterDataRelationAdministration,
/// expand only (new tables and indexes, nothing on an existing table, so the previous application version keeps
/// working), the eight tables, every per-tenant unique index (NULLS NOT DISTINCT, soft-delete and "where set"
/// filters), the one-active-combination partial index, the trigram GIN on the normalised code, the position check,
/// and the restrict / cascade foreign keys.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class MasterDataVehiclesMigrationTests : MasterDataPostgreSqlTestBase
{
    private const string Migration = "20261015111108_MasterDataVehicles";
    private const string PreviousMigration = "20261013153113_MasterDataRelationAdministration";
    private const string NormalizedNameMigration = "20261016071559_MasterDataVehicleNormalizedName";
    private const string CombinationNormalizedNameMigration = "20261016211522_MasterDataVehicleCombinationNormalizedName";

    private static readonly string[] Tables =
    [
        "Vehicles", "VehicleTransportCharacteristics", "VehicleDefaultDrivers", "VehicleOrganizationUnits", "VehicleExternalIdentifiers",
        "VehicleCombinations", "VehicleCombinationMembers", "ProductRelationCodes"
    ];

    private readonly IDbContextProvider<MasterDataDbContext> _contextProvider;

    public MasterDataVehiclesMigrationTests()
    {
        _contextProvider = GetRequiredService<IDbContextProvider<MasterDataDbContext>>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_migration_is_applied_after_the_administration_one_and_nothing_is_pending(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var context = await _contextProvider.GetDbContextAsync();

            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.ShouldContain(Migration);
            applied.IndexOf(Migration).ShouldBeGreaterThan(applied.IndexOf(PreviousMigration));
            applied.Last().ShouldBe(CombinationNormalizedNameMigration, "the newest Master Data migration");
            applied.IndexOf(NormalizedNameMigration).ShouldBeGreaterThan(applied.IndexOf(Migration));
            applied.IndexOf(CombinationNormalizedNameMigration).ShouldBe(applied.IndexOf(NormalizedNameMigration) + 1);
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            context.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot must describe the model that runs");
            context.Database.GetDbConnection().Database.ShouldBe(tenant == null ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase);
        });
    }

    [Fact]
    public void The_migration_is_expand_only_and_its_Down_drops_exactly_its_own_tables()
    {
        /* Constitution rule 2: Up only creates tables and indexes; no operation touches an existing table, so the
         * run-2 application keeps working on the migrated schema. Down drops the eight tables and nothing else. */
        var migration = new MasterDataVehicles();

        var up = migration.UpOperations;
        up.ShouldAllBe(o => o is CreateTableOperation || o is CreateIndexOperation, string.Join(", ", up.Select(o => o.GetType().Name).Distinct()));
        up.OfType<CreateTableOperation>().Select(t => t.Name).ShouldBe(Tables, ignoreOrder: true);
        up.OfType<CreateTableOperation>().ShouldAllBe(t => t.Schema == "masterdata");
        up.OfType<CreateIndexOperation>().Select(i => i.Table).Distinct().ShouldAllBe(t => Tables.Contains(t), "indexes only on the new tables");

        var down = migration.DownOperations;
        down.ShouldAllBe(o => o is DropTableOperation);
        down.OfType<DropTableOperation>().Select(t => t.Name).ShouldBe(Tables, ignoreOrder: true);
    }

    [Fact]
    public void The_normalized_name_migration_backfills_before_it_moves_the_unique_key_and_Down_restores_it()
    {
        /* PO 2026-10-16: vehicle names are unique regardless of case. The column is added, existing rows are filled
         * from the name before the unique index on it is created, and the index on Name stays for the default sort,
         * no longer unique. No release shipped vehicles before this migration, so no rolling deploy writes rows
         * without the column. */
        var migration = new MasterDataVehicleNormalizedName();

        var up = migration.UpOperations;
        up.ShouldAllBe(o => o.GetType() == typeof(DropIndexOperation) || o.GetType() == typeof(AddColumnOperation) || o.GetType() == typeof(SqlOperation) || o.GetType() == typeof(CreateIndexOperation));
        var add = up.OfType<AddColumnOperation>().ShouldHaveSingleItem();
        (add.Table, add.Name).ShouldBe(("Vehicles", "NormalizedName"));
        var backfill = up.OfType<SqlOperation>().ShouldHaveSingleItem();
        backfill.Sql.ShouldContain("SET \"NormalizedName\" = upper(\"Name\")");
        up.ToList().IndexOf(backfill).ShouldBeGreaterThan(up.ToList().IndexOf(add));
        var unique = up.OfType<CreateIndexOperation>().Single(i => i.IsUnique);
        unique.Columns.ShouldBe(["TenantId", "NormalizedName"]);
        up.ToList().IndexOf(unique).ShouldBeGreaterThan(up.ToList().IndexOf(backfill), "the unique index is built on filled rows");
        up.OfType<CreateIndexOperation>().Single(i => !i.IsUnique).Columns.ShouldBe(["TenantId", "Name"]);

        var down = migration.DownOperations;
        down.OfType<DropColumnOperation>().ShouldHaveSingleItem().Name.ShouldBe("NormalizedName");
        down.OfType<CreateIndexOperation>().ShouldHaveSingleItem().IsUnique.ShouldBeTrue("Down restores the case-sensitive unique name");
    }

    [Theory]
    [InlineData("host")]
    [InlineData("tenant-a")]
    public async Task Every_table_unique_index_partial_index_check_and_foreign_key_is_in_place(string target)
    {
        var database = target == "host" ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase;
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();

        var tables = await QueryAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'masterdata'");
        foreach (var table in Tables)
        {
            tables.ShouldContain(table);
        }

        var indexes = await QueryAsync(connection, "SELECT indexname || ' :: ' || indexdef FROM pg_indexes WHERE schemaname = 'masterdata'");
        One(indexes, "IX_Vehicles_TenantId_Name :: ").ShouldNotContain("UNIQUE", Case.Sensitive, "the name index only serves the sort; the key is NormalizedName");
        One(indexes, "IX_VehicleCombinations_TenantId_Name :: ").ShouldNotContain("UNIQUE", Case.Sensitive, "the name index only serves the sort; the key is NormalizedName (PO 2026-10-16)");

        /* Per-tenant unique keys: NULLS NOT DISTINCT (host rows unique too), freed by a soft delete. */
        foreach (var (name, keyColumns, where) in new[]
                 {
                     ("IX_Vehicles_TenantId_NormalizedName", "(\"TenantId\", \"NormalizedName\")", (string?)null),
                     ("IX_Vehicles_TenantId_RegistrationCountryId_LicencePlate", "(\"TenantId\", \"RegistrationCountryId\", \"LicencePlate\")", "\"LicencePlate\" IS NOT NULL"),
                     ("IX_Vehicles_TenantId_Vin", "(\"TenantId\", \"Vin\")", "\"Vin\" IS NOT NULL"),
                     ("IX_VehicleCombinations_TenantId_NormalizedName", "(\"TenantId\", \"NormalizedName\")", null),
                     ("IX_ProductRelationCodes_TenantId_NormalizedCode", "(\"TenantId\", \"NormalizedCode\")", null),
                 })
        {
            var index = One(indexes, name + " :: ");
            index.ShouldContain("UNIQUE", Case.Sensitive, name);
            index.ShouldContain("NULLS NOT DISTINCT", Case.Sensitive, name);
            index.ShouldContain(keyColumns, Case.Sensitive, name);
            index.ShouldContain("\"IsDeleted\" = false", Case.Sensitive, name);
            if (where != null)
            {
                index.ShouldContain(where, Case.Sensitive, name);
            }
        }

        /* The external id is a child row without soft delete: every row takes part. */
        var external = One(indexes, "IX_VehicleExternalIdentifiers_TenantId_Source_Identifier :: ");
        external.ShouldContain("UNIQUE");
        external.ShouldContain("NULLS NOT DISTINCT");
        external.ShouldContain("(\"TenantId\", \"Source\", \"Identifier\")");
        external.ShouldNotContain("WHERE");

        /* One active combination per vehicle: unique on (TenantId, VehicleId) over the active members only. */
        var active = One(indexes, "IX_VehicleCombinationMembers_TenantId_VehicleId_Active :: ");
        active.ShouldContain("UNIQUE");
        active.ShouldContain("NULLS NOT DISTINCT");
        active.ShouldContain("(\"TenantId\", \"VehicleId\")");
        active.ShouldContain("\"IsActive\" = true");

        /* The contains search over 50,000 codes: trigram GIN on the normalised code. */
        var trigram = One(indexes, "IX_ProductRelationCodes_NormalizedCode_Trigram :: ");
        trigram.ShouldContain("USING gin");
        trigram.ShouldContain("gin_trgm_ops");
        trigram.ShouldContain("\"NormalizedCode\"");
        trigram.ShouldNotContain("UNIQUE");

        /* The in-use checks and the tenant filters read through plain indexes. */
        foreach (var name in new[]
                 {
                     "IX_Vehicles_TenantId", "IX_Vehicles_VehicleTypeId", "IX_Vehicles_OwnerRelationId", "IX_Vehicles_HomeDepotRelationId", "IX_Vehicles_RegistrationCountryId",
                     "IX_VehicleDefaultDrivers_DriverId", "IX_VehicleCombinationMembers_VehicleId", "IX_VehicleTransportCharacteristics_TransportCharacteristicsId",
                     "IX_VehicleOrganizationUnits_OrganizationUnitId", "IX_VehicleExternalIdentifiers_VehicleId", "IX_VehicleCombinations_TenantId",
                     "IX_ProductRelationCodes_TenantId", "IX_ProductRelationCodes_RelationId", "IX_ProductRelationCodes_ProductId", "IX_ProductRelationCodes_TransportUnitId",
                 })
        {
            One(indexes, name + " :: ").ShouldNotContain("UNIQUE", Case.Sensitive, name);
        }

        One(indexes, "PK_VehicleTransportCharacteristics :: ").ShouldContain("(\"VehicleId\", \"TransportCharacteristicsId\")");
        One(indexes, "PK_VehicleDefaultDrivers :: ").ShouldContain("(\"VehicleId\", \"DriverId\")");
        One(indexes, "PK_VehicleOrganizationUnits :: ").ShouldContain("(\"VehicleId\", \"OrganizationUnitId\")");
        One(indexes, "PK_VehicleCombinationMembers :: ").ShouldContain("(\"VehicleCombinationId\", \"VehicleId\")");

        var checks = await QueryAsync(connection, "SELECT conname || ' :: ' || pg_get_constraintdef(oid) FROM pg_constraint WHERE contype = 'c' AND connamespace = 'masterdata'::regnamespace");
        var position = One(checks, "CK_VehicleCombinationMembers_Position :: ");
        position.ShouldContain("\"Position\" >= 1");
        position.ShouldContain("\"Position\" <= 4");

        var foreignKeys = await QueryAsync(connection, "SELECT conname || ' :: ' || confdeltype::text FROM pg_constraint WHERE contype = 'f' AND connamespace = 'masterdata'::regnamespace");
        foreach (var restrict in new[]
                 {
                     "FK_Vehicles_VehicleTypes_VehicleTypeId", "FK_Vehicles_Countries_RegistrationCountryId", "FK_Vehicles_Relations_OwnerRelationId",
                     "FK_Vehicles_Relations_HomeDepotRelationId", "FK_VehicleDefaultDrivers_Drivers_DriverId", "FK_VehicleCombinationMembers_Vehicles_VehicleId",
                     "FK_ProductRelationCodes_Relations_RelationId", "FK_ProductRelationCodes_Products_ProductId", "FK_ProductRelationCodes_TransportUnits_TransportUnitId",
                 })
        {
            foreignKeys.ShouldContain(restrict + " :: r");
        }

        foreach (var cascade in new[]
                 {
                     "FK_VehicleTransportCharacteristics_Vehicles_VehicleId", "FK_VehicleDefaultDrivers_Vehicles_VehicleId", "FK_VehicleOrganizationUnits_Vehicles_VehicleId",
                     "FK_VehicleExternalIdentifiers_Vehicles_VehicleId",
                 })
        {
            foreignKeys.ShouldContain(cascade + " :: c");
        }

        /* PostgreSQL truncates identifiers at 63 characters, so these names end in "~". */
        foreignKeys.ShouldContain(fk => fk.StartsWith("FK_VehicleCombinationMembers_VehicleCombinations_", StringComparison.Ordinal) && fk.EndsWith(":: c", StringComparison.Ordinal));
        foreignKeys.ShouldContain(fk => fk.StartsWith("FK_VehicleTransportCharacteristics_TransportCharacteristics_", StringComparison.Ordinal) && fk.EndsWith(":: c", StringComparison.Ordinal));
        /* Platform ids carry no constraint (module rule 1). */
        foreignKeys.ShouldNotContain(fk => fk.StartsWith("FK_VehicleOrganizationUnits_", StringComparison.Ordinal) && !fk.Contains("Vehicles_VehicleId"));

        /* Columns: lengths from MasterDataConsts, the kind is not stored on the vehicle, every key nullable where the design says so. */
        var columns = await QueryAsync(connection,
            "SELECT table_name || '.' || column_name || ' :: ' || data_type || '(' || COALESCE(character_maximum_length::text, '-') || ') :: ' || is_nullable " +
            "FROM information_schema.columns WHERE table_schema = 'masterdata' AND table_name IN ('Vehicles', 'VehicleCombinations', 'VehicleCombinationMembers', 'ProductRelationCodes')");
        columns.ShouldContain("Vehicles.Name :: character varying(64) :: NO");
        columns.ShouldContain("Vehicles.LicencePlate :: character varying(16) :: YES");
        columns.ShouldContain("Vehicles.Vin :: character varying(17) :: YES");
        columns.ShouldContain("Vehicles.VehicleTypeId :: uuid(-) :: NO");
        columns.ShouldContain("Vehicles.OwnerRelationId :: uuid(-) :: NO");
        columns.ShouldContain("Vehicles.HomeDepotRelationId :: uuid(-) :: YES");
        columns.ShouldContain("Vehicles.RegistrationCountryId :: uuid(-) :: YES");
        columns.ShouldContain("Vehicles.IsArchived :: boolean(-) :: NO");
        columns.ShouldContain("Vehicles.Description :: character varying(2500) :: YES");
        columns.ShouldNotContain(c => c.StartsWith("Vehicles.Kind ", StringComparison.Ordinal), "the kind is the type's (design 48, assumption 3)");
        columns.ShouldNotContain(c => c.StartsWith("Vehicles.LastSeen", StringComparison.Ordinal), "no telematics state (AO-12)");
        columns.ShouldContain("VehicleCombinations.Name :: character varying(64) :: NO");
        columns.ShouldContain("VehicleCombinations.NormalizedName :: character varying(64) :: NO");
        columns.ShouldContain("VehicleCombinationMembers.IsActive :: boolean(-) :: NO");
        columns.ShouldContain("VehicleCombinationMembers.Position :: integer(-) :: NO");
        columns.ShouldContain("ProductRelationCodes.Code :: character varying(64) :: NO");
        columns.ShouldContain("ProductRelationCodes.NormalizedCode :: character varying(64) :: NO");
        columns.ShouldContain("ProductRelationCodes.RelationId :: uuid(-) :: YES");
        columns.ShouldContain("ProductRelationCodes.ProductId :: uuid(-) :: YES");
        columns.ShouldContain("ProductRelationCodes.TransportUnitId :: uuid(-) :: YES");

        /* Earlier runs untouched. */
        One(indexes, "IX_Relations_TenantId_Code :: ").ShouldContain("UNIQUE");
        One(indexes, "IX_Relations_SearchKey_Trigram :: ").ShouldContain("gin_trgm_ops");
    }

    [Fact]
    public async Task The_trigram_index_is_usable_for_a_contains_search_on_the_normalised_code()
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(PostgreSqlTestDatabases.TenantADatabase));
        await connection.OpenAsync();
        await using (var setup = new NpgsqlCommand("SET enable_seqscan = off", connection))
        {
            await setup.ExecuteNonQueryAsync();
        }

        var plan = string.Join("\n", await QueryAsync(connection, "EXPLAIN SELECT \"Id\" FROM masterdata.\"ProductRelationCodes\" WHERE \"NormalizedCode\" LIKE '%ART-47%'"));
        plan.ShouldContain("IX_ProductRelationCodes_NormalizedCode_Trigram", Case.Sensitive, plan);
    }

    private static string One(List<string> rows, string prefix)
    {
        rows.ShouldContain(r => r.StartsWith(prefix, StringComparison.Ordinal), prefix);
        return rows.Single(r => r.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static async Task<List<string>> QueryAsync(NpgsqlConnection connection, string sql)
    {
        var rows = new List<string>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
