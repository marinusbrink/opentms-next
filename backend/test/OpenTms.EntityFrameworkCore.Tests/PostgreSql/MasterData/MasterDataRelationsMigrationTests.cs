using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OpenTms.MasterData;
using OpenTms.MasterData.EntityFrameworkCore;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Relations;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;
using Xunit;

namespace OpenTms.PostgreSql.MasterData;

/// <summary>
/// The MasterDataRelationsAndDrivers migration on the databases the real DbMigrator created (design 43,
/// "Migration strategy"). Risk class: Critical — applied on the host and on every tenant database,
/// expand-only after design 41's migration, the pg_trgm extension present, the trigram GIN index, the
/// roles check constraint, the own-company and default-account partial indexes, the per-tenant unique
/// keys (NULLS NOT DISTINCT, soft-delete filter) and the restrict/cascade foreign keys in place.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class MasterDataRelationsMigrationTests : MasterDataPostgreSqlTestBase
{
    private const string Migration = "20261013121217_MasterDataRelationsAndDrivers";
    private const string FirstRunMigration = "20261011191016_MasterDataFirstRun";
    private const string AdministrationMigration = "20261013153113_MasterDataRelationAdministration";

    private static readonly string[] Tables =
    [
        "Relations", "RelationAddresses", "RelationContactPersons", "RelationBankAccounts", "RelationCategoryMemberships",
        "RelationGeographicalRegions", "RelationOrganizationUnits", "Drivers", "DriverQualifications"
    ];

    private readonly IDbContextProvider<MasterDataDbContext> _contextProvider;

    public MasterDataRelationsMigrationTests()
    {
        _contextProvider = GetRequiredService<IDbContextProvider<MasterDataDbContext>>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_migration_is_applied_after_the_first_run_one_and_nothing_is_pending(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var context = await _contextProvider.GetDbContextAsync();

            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.ShouldContain(Migration);
            applied.IndexOf(Migration).ShouldBeGreaterThan(applied.IndexOf(FirstRunMigration), "run 2 builds on run 1");
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            context.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot must describe the model that runs");
            context.Database.GetDbConnection().Database.ShouldBe(tenant == null ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase);
        });
    }

    [Theory]
    [InlineData("host")]
    [InlineData("tenant-a")]
    public async Task Every_table_the_extension_the_trigram_index_the_check_and_the_partial_unique_indexes_exist(string target)
    {
        var database = target == "host" ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase;
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();

        var tables = await QueryAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'masterdata'");
        foreach (var table in Tables)
        {
            tables.ShouldContain(table);
        }

        /* The extension is in the database (design 43, assumption 3) and the trigram index uses it. */
        var extensions = await QueryAsync(connection, "SELECT extname FROM pg_extension");
        extensions.ShouldContain("pg_trgm");

        var indexes = await QueryAsync(connection, "SELECT indexname || ' :: ' || indexdef FROM pg_indexes WHERE schemaname = 'masterdata'");
        var trigram = One(indexes, "IX_Relations_SearchKey_Trigram :: ");
        trigram.ShouldContain("USING gin");
        trigram.ShouldContain("gin_trgm_ops");
        trigram.ShouldContain("\"SearchKey\"");
        trigram.ShouldNotContain("UNIQUE");

        /* Per-tenant unique keys: NULLS NOT DISTINCT so host rows (TenantId null) are unique too, freed by a soft delete. */
        foreach (var (table, column) in new[] { ("Relations", "Code"), ("Relations", "VatNumber"), ("Relations", "ChamberOfCommerceNumber"), ("Drivers", "Code"), ("Drivers", "UserId") })
        {
            var index = One(indexes, $"IX_{table}_TenantId_{column} :: ", $"{table}.{column}");
            index.ShouldContain("UNIQUE", Case.Sensitive, $"{table}.{column}");
            index.ShouldContain("NULLS NOT DISTINCT", Case.Sensitive, $"{table}.{column}");
            index.ShouldContain("\"IsDeleted\" = false", Case.Sensitive, $"{table}.{column}");
        }

        One(indexes, "IX_Relations_TenantId_VatNumber :: ").ShouldContain("\"VatNumber\" IS NOT NULL");
        One(indexes, "IX_Relations_TenantId_ChamberOfCommerceNumber :: ").ShouldContain("\"ChamberOfCommerceNumber\" IS NOT NULL");
        One(indexes, "IX_Drivers_TenantId_UserId :: ").ShouldContain("\"UserId\" IS NOT NULL");

        /* Exactly one own company per tenant: unique on TenantId where the OwnCompany bit (16) is set. */
        var ownCompany = One(indexes, "IX_Relations_TenantId_OwnCompany :: ");
        ownCompany.ShouldContain("UNIQUE");
        ownCompany.ShouldContain("NULLS NOT DISTINCT");
        ownCompany.ShouldContain("(\"Roles\" & 16) = 16");
        ownCompany.ShouldContain("\"IsDeleted\" = false");
        ownCompany.ShouldContain("(\"TenantId\")");

        /* One address per type and one default address (type null) per relation (PO 2026-10-12: NULLS NOT DISTINCT), one IBAN and one default account per relation. */
        var addressType = One(indexes, "IX_RelationAddresses_RelationId_AddressTypeId :: ");
        addressType.ShouldContain("UNIQUE");
        addressType.ShouldContain("NULLS NOT DISTINCT");
        addressType.ShouldContain("(\"RelationId\", \"AddressTypeId\")");
        addressType.ShouldNotContain("WHERE", Case.Sensitive, "every row takes part, the default address included");
        One(indexes, "IX_RelationBankAccounts_RelationId_Iban :: ").ShouldContain("UNIQUE");
        var defaultAccount = One(indexes, "IX_RelationBankAccounts_RelationId_Default :: ");
        defaultAccount.ShouldContain("UNIQUE");
        defaultAccount.ShouldContain("\"IsDefault\" = true");

        /* The read-path indexes of the list, the lookup and the matcher. */
        foreach (var name in new[]
                 {
                     "IX_Relations_TenantId", "IX_Relations_TenantId_SearchKey", "IX_Relations_TenantId_Roles", "IX_RelationAddresses_CountryId",
                     "IX_RelationAddresses_AddressTypeId", "IX_RelationAddresses_TenantId_PostalCodeKey", "IX_RelationContactPersons_RelationId",
                     "IX_RelationContactPersons_TenantId_Email", "IX_RelationCategoryMemberships_RelationCategoryId", "IX_RelationGeographicalRegions_GeographicalRegionId",
                     "IX_RelationOrganizationUnits_OrganizationUnitId", "IX_Drivers_TenantId", "IX_Drivers_TenantId_SearchKey", "IX_Drivers_EmployerRelationId",
                     "IX_DriverQualifications_DriverId", "IX_DriverQualifications_TenantId_ValidUntil"
                 })
        {
            indexes.ShouldContain(i => i.StartsWith(name + " :: ", StringComparison.Ordinal), name);
        }

        var checks = await QueryAsync(connection, "SELECT conname || ' :: ' || pg_get_constraintdef(oid) FROM pg_constraint WHERE contype = 'c' AND connamespace = 'masterdata'::regnamespace");
        var roles = One(checks, "CK_Relations_Roles :: ");
        roles.ShouldContain("\"Roles\" > 0");
        roles.ShouldContain("\"Roles\" <= 31");

        var foreignKeys = await QueryAsync(connection, "SELECT conname || ' :: ' || confdeltype::text FROM pg_constraint WHERE contype = 'f' AND connamespace = 'masterdata'::regnamespace");
        foreignKeys.ShouldContain("FK_RelationAddresses_Countries_CountryId :: r");
        foreignKeys.ShouldContain("FK_RelationAddresses_AddressTypes_AddressTypeId :: r");
        foreignKeys.ShouldContain("FK_Drivers_Relations_EmployerRelationId :: r");
        foreignKeys.ShouldContain("FK_RelationAddresses_Relations_RelationId :: c");
        foreignKeys.ShouldContain("FK_RelationContactPersons_Relations_RelationId :: c");
        foreignKeys.ShouldContain("FK_RelationBankAccounts_Relations_RelationId :: c");
        foreignKeys.ShouldContain("FK_RelationCategoryMemberships_Relations_RelationId :: c");
        /* PostgreSQL truncates identifiers at 63 characters, so this name ends in "~". */
        foreignKeys.ShouldContain(fk => fk.StartsWith("FK_RelationCategoryMemberships_RelationCategories_RelationCate", StringComparison.Ordinal) && fk.EndsWith(":: c", StringComparison.Ordinal));
        foreignKeys.ShouldContain("FK_RelationGeographicalRegions_Relations_RelationId :: c");
        foreignKeys.ShouldContain(fk => fk.StartsWith("FK_RelationGeographicalRegions_GeographicalRegions_", StringComparison.Ordinal) && fk.EndsWith(":: c", StringComparison.Ordinal));
        foreignKeys.ShouldContain("FK_RelationOrganizationUnits_Relations_RelationId :: c");
        foreignKeys.ShouldContain("FK_DriverQualifications_Drivers_DriverId :: c");
        /* Platform ids carry no constraint (module rule 1). */
        foreignKeys.ShouldNotContain(fk => fk.Contains("OrganizationUnitId") && !fk.Contains("Relations_RelationId"));
        foreignKeys.ShouldNotContain(fk => fk.Contains("UserId"));

        /* The PO revisions of 2026-10-12: the address type is optional (the default address has none), the blocked flag is a non-null boolean with a non-null integer reason (None = 0). */
        var columns = await QueryAsync(connection, "SELECT table_name || '.' || column_name || ' :: ' || data_type || ' :: ' || is_nullable FROM information_schema.columns WHERE table_schema = 'masterdata' AND table_name IN ('Relations', 'RelationAddresses')");
        columns.ShouldContain("RelationAddresses.AddressTypeId :: uuid :: YES");
        columns.ShouldContain("RelationAddresses.RelationId :: uuid :: NO");
        columns.ShouldContain("RelationAddresses.CountryId :: uuid :: NO");
        columns.ShouldContain("Relations.IsBlocked :: boolean :: NO");
        columns.ShouldContain("Relations.BlockReason :: integer :: NO");
        columns.ShouldContain("Relations.IsArchived :: boolean :: NO");

        /* The run-1 tables are untouched: expand only. */
        tables.ShouldContain("Currencies");
        tables.ShouldContain("GeographicalRegionPostalCodeRanges");
    }

    [Theory]
    [InlineData("host")]
    [InlineData("tenant-a")]
    public async Task The_full_name_column_and_the_wider_search_keys_are_in_place(string target)
    {
        var database = target == "host" ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase;
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();

        /* PO 2026-10-12: the optional full name shares the name length; the search key holds name, full name and code (128 + 128 + 32 with separators) on both searchable tables. */
        var columns = await QueryAsync(connection, "SELECT table_name || '.' || column_name || ' :: ' || data_type || '(' || COALESCE(character_maximum_length::text, '-') || ') :: ' || is_nullable FROM information_schema.columns WHERE table_schema = 'masterdata' AND column_name IN ('FullName', 'SearchKey', 'Name') AND table_name IN ('Relations', 'Drivers')");
        columns.ShouldContain("Relations.FullName :: character varying(128) :: YES");
        columns.ShouldContain("Relations.Name :: character varying(128) :: NO");
        columns.ShouldContain("Relations.SearchKey :: character varying(320) :: NO");
        columns.ShouldContain("Drivers.SearchKey :: character varying(320) :: NO");

        /* The search-key indexes are the same three on the wider column: the per-tenant B-trees and the trigram GIN. */
        var indexes = await QueryAsync(connection, "SELECT indexname || ' :: ' || indexdef FROM pg_indexes WHERE schemaname = 'masterdata' AND indexname LIKE '%SearchKey%'");
        indexes.Count.ShouldBe(3, string.Join("\n", indexes));
        One(indexes, "IX_Relations_TenantId_SearchKey :: ").ShouldContain("(\"TenantId\", \"SearchKey\")");
        One(indexes, "IX_Drivers_TenantId_SearchKey :: ").ShouldContain("(\"TenantId\", \"SearchKey\")");
        One(indexes, "IX_Relations_SearchKey_Trigram :: ").ShouldContain("gin_trgm_ops");
    }

    [Theory]
    [InlineData("host")]
    [InlineData("tenant-a")]
    public async Task The_house_number_suffix_column_sits_next_to_the_house_number_at_the_same_length(string target)
    {
        var database = target == "host" ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase;
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();

        /* PO 2026-10-13: the suffix is its own optional column, bounded like the number (MaxHouseNumberLength = 16); the number itself is unchanged. */
        var columns = await QueryAsync(connection, "SELECT column_name || ' :: ' || data_type || '(' || COALESCE(character_maximum_length::text, '-') || ') :: ' || is_nullable FROM information_schema.columns WHERE table_schema = 'masterdata' AND table_name = 'RelationAddresses' AND column_name IN ('HouseNumber', 'HouseNumberSuffix')");
        columns.Count.ShouldBe(2, string.Join("\n", columns));
        columns.ShouldContain($"HouseNumber :: character varying({MasterDataConsts.MaxHouseNumberLength}) :: YES");
        columns.ShouldContain($"HouseNumberSuffix :: character varying({MasterDataConsts.MaxHouseNumberLength}) :: YES");

        /* No index or constraint targets the suffix: it is a display field, not a key. */
        var indexes = await QueryAsync(connection, "SELECT indexname || ' :: ' || indexdef FROM pg_indexes WHERE schemaname = 'masterdata' AND tablename = 'RelationAddresses'");
        indexes.ShouldNotContain(i => i.Contains("HouseNumberSuffix"), "the suffix carries no index");
    }

    [Fact]
    public async Task A_relation_address_with_number_and_suffix_round_trips_in_tenant_A()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var repository = GetRequiredService<IRepository<Relation, Guid>>();
            var context = await _contextProvider.GetDbContextAsync();
            var guids = GetRequiredService<IGuidGenerator>();
            var country = await context.Set<Country>().Where(c => c.IsoAlpha2 == "NL").Select(c => c.Id).SingleAsync();

            var relation = new Relation(guids.Create(), TenantA, "SFX01", "Suffix B.V.", RelationRoles.Customer);
            relation.SetAddresses([new RelationAddressInput(null, null, country, "Dorpsstraat", "12", " a ", null, "1234 AB", "Utrecht", null, null, null)], guids.Create);
            await repository.InsertAsync(relation, autoSave: true);
            context.ChangeTracker.Clear();

            var stored = await context.Set<RelationAddress>().Where(a => a.RelationId == relation.Id).Select(a => new { a.HouseNumber, a.HouseNumberSuffix, a.TenantId }).SingleAsync();
            stored.HouseNumber.ShouldBe("12");
            stored.HouseNumberSuffix.ShouldBe("a", "PostgreSQL stores the trimmed suffix in its own column");
            stored.TenantId.ShouldBe(TenantA);
            context.Database.GetDbConnection().Database.ShouldBe(PostgreSqlTestDatabases.TenantADatabase);
        });
    }

    [Fact]
    public async Task A_relation_with_name_full_name_and_code_at_their_limits_is_stored_in_tenant_A_and_found_on_a_full_name_fragment()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var repository = GetRequiredService<IRepository<Relation, Guid>>();
            var context = await _contextProvider.GetDbContextAsync();
            var name = new string('n', MasterDataConsts.MaxNameLength);
            var code = new string('c', MasterDataConsts.MaxCodeLength);
            var fullName = "Müller & Zn. " + new string('f', MasterDataConsts.MaxNameLength - 13);
            fullName.Length.ShouldBe(MasterDataConsts.MaxNameLength);

            var relation = new Relation(GetRequiredService<IGuidGenerator>().Create(), TenantA, code, name, RelationRoles.Customer);
            relation.SetName(name, fullName);
            relation.SearchKey.Length.ShouldBeGreaterThan(160, "the key no longer fits the pre-PO column; the migration must have widened it");
            await repository.InsertAsync(relation, autoSave: true);
            context.ChangeTracker.Clear();

            var key = MasterDataValues.SearchKey("zn. f");
            var stored = await context.Set<Relation>().Where(r => r.SearchKey.Contains(key)).Select(r => new { r.Id, r.FullName, r.SearchKey }).SingleAsync();
            stored.Id.ShouldBe(relation.Id);
            stored.FullName.ShouldBe(fullName);
            stored.SearchKey.ShouldBe(relation.SearchKey, "PostgreSQL stores the whole key");
            context.Database.GetDbConnection().Database.ShouldBe(PostgreSqlTestDatabases.TenantADatabase);
        });
    }

    [Fact]
    public async Task The_trigram_index_is_usable_for_a_contains_search_on_the_search_key()
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(PostgreSqlTestDatabases.TenantADatabase));
        await connection.OpenAsync();

        /* With sequential scans discouraged the planner must be able to pick the GIN index for LIKE '%…%'. */
        await using (var setup = new NpgsqlCommand("SET enable_seqscan = off", connection))
        {
            await setup.ExecuteNonQueryAsync();
        }

        var plan = string.Join("\n", await QueryAsync(connection, "EXPLAIN SELECT \"Id\" FROM masterdata.\"Relations\" WHERE \"SearchKey\" LIKE '%acme transport%'"));
        plan.ShouldContain("IX_Relations_SearchKey_Trigram", Case.Sensitive, "the planner can use the trigram index for a contains search: " + plan);
    }

    // ── MasterDataRelationAdministration (PO 2026-10-13) ─────────────────────
    // Risk class: Critical — applied on the host and every tenant database, expand only after run 2: new
    // nullable columns and new tables, so the run-2 application keeps working on the migrated schema.

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_administration_migration_is_applied_after_the_run_2_one_and_nothing_is_pending(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var context = await _contextProvider.GetDbContextAsync();

            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.ShouldContain(AdministrationMigration);
            applied.IndexOf(AdministrationMigration).ShouldBeGreaterThan(applied.IndexOf(Migration), "the administration columns build on run 2");
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            context.Database.HasPendingModelChanges().ShouldBeFalse();
            context.Database.GetDbConnection().Database.ShouldBe(tenant == null ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase);
        });
    }

    [Theory]
    [InlineData("host")]
    [InlineData("tenant-a")]
    public async Task The_administration_columns_tables_indexes_and_foreign_keys_are_in_place(string target)
    {
        var database = target == "host" ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase;
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();

        /* Expand only: every new Relations column is nullable without a default, so a run-2 INSERT that does not name them still works. */
        var columns = await QueryAsync(connection,
            "SELECT table_name || '.' || column_name || ' :: ' || data_type || '(' || COALESCE(character_maximum_length::text, '-') || ') :: ' || is_nullable || ' :: ' || COALESCE(column_default, '-') " +
            "FROM information_schema.columns WHERE table_schema = 'masterdata' AND table_name IN ('Relations', 'RelationExternalIdentifiers', 'RelationTransportCharacteristics')");
        columns.ShouldContain("Relations.DebtorNumber :: character varying(32) :: YES :: -");
        columns.ShouldContain("Relations.CreditorNumber :: character varying(32) :: YES :: -");
        columns.ShouldContain("Relations.CarrierCode :: character varying(32) :: YES :: -");
        columns.ShouldContain("Relations.CurrencyId :: uuid(-) :: YES :: -");

        columns.Where(c => c.StartsWith("RelationExternalIdentifiers.", StringComparison.Ordinal)).ShouldBe(
        [
            "RelationExternalIdentifiers.Id :: uuid(-) :: NO :: -",
            "RelationExternalIdentifiers.TenantId :: uuid(-) :: YES :: -",
            "RelationExternalIdentifiers.RelationId :: uuid(-) :: NO :: -",
            "RelationExternalIdentifiers.Source :: character varying(64) :: NO :: -",
            "RelationExternalIdentifiers.Identifier :: character varying(128) :: NO :: -"
        ], ignoreOrder: true);
        columns.Where(c => c.StartsWith("RelationTransportCharacteristics.", StringComparison.Ordinal)).ShouldBe(
        [
            "RelationTransportCharacteristics.RelationId :: uuid(-) :: NO :: -",
            "RelationTransportCharacteristics.TransportCharacteristicsId :: uuid(-) :: NO :: -",
            "RelationTransportCharacteristics.TenantId :: uuid(-) :: YES :: -"
        ], ignoreOrder: true);

        var indexes = await QueryAsync(connection, "SELECT indexname || ' :: ' || indexdef FROM pg_indexes WHERE schemaname = 'masterdata'");
        /* Debtor and creditor numbers: unique per tenant where set and not deleted, NULLS NOT DISTINCT so host rows are unique too. */
        foreach (var column in new[] { "DebtorNumber", "CreditorNumber" })
        {
            var index = One(indexes, $"IX_Relations_TenantId_{column} :: ", column);
            index.ShouldContain("UNIQUE", Case.Sensitive, column);
            index.ShouldContain("NULLS NOT DISTINCT", Case.Sensitive, column);
            index.ShouldContain($"(\"TenantId\", \"{column}\")", Case.Sensitive, column);
            index.ShouldContain($"\"{column}\" IS NOT NULL", Case.Sensitive, column);
            index.ShouldContain("\"IsDeleted\" = false", Case.Sensitive, column);
        }

        One(indexes, "IX_Relations_CurrencyId :: ").ShouldNotContain("UNIQUE");
        var external = One(indexes, "IX_RelationExternalIdentifiers_TenantId_Source_Identifier :: ");
        external.ShouldContain("UNIQUE");
        external.ShouldContain("NULLS NOT DISTINCT");
        external.ShouldContain("(\"TenantId\", \"Source\", \"Identifier\")");
        external.ShouldNotContain("WHERE", Case.Sensitive, "a child row has no soft delete: every row takes part");
        One(indexes, "IX_RelationExternalIdentifiers_RelationId :: ").ShouldNotContain("UNIQUE");
        One(indexes, "PK_RelationTransportCharacteristics :: ").ShouldContain("(\"RelationId\", \"TransportCharacteristicsId\")");
        One(indexes, "IX_RelationTransportCharacteristics_TransportCharacteristicsId :: ").ShouldNotContain("UNIQUE");

        var foreignKeys = await QueryAsync(connection, "SELECT conname || ' :: ' || confdeltype::text FROM pg_constraint WHERE contype = 'f' AND connamespace = 'masterdata'::regnamespace");
        foreignKeys.ShouldContain("FK_Relations_Currencies_CurrencyId :: r");
        foreignKeys.ShouldContain("FK_RelationExternalIdentifiers_Relations_RelationId :: c");
        foreignKeys.ShouldContain("FK_RelationTransportCharacteristics_Relations_RelationId :: c");
        /* PostgreSQL truncates identifiers at 63 characters, so this name ends in "~". */
        foreignKeys.ShouldContain(fk => fk.StartsWith("FK_RelationTransportCharacteristics_TransportCharacteristics_", StringComparison.Ordinal) && fk.EndsWith(":: c", StringComparison.Ordinal));

        /* The run-2 tables and keys are untouched. */
        One(indexes, "IX_Relations_TenantId_Code :: ").ShouldContain("UNIQUE");
        foreignKeys.ShouldContain("FK_RelationBankAccounts_Relations_RelationId :: c");
    }

    [Fact]
    public async Task A_run_2_style_insert_without_the_new_columns_still_works_and_reads_back_nulls()
    {
        /* The expand guarantee (constitution rule 2): the previous application version inserts relations without the
         * administration columns; the migrated schema must take them and the new code must read them. */
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var context = await _contextProvider.GetDbContextAsync();
            var guids = GetRequiredService<IGuidGenerator>();
            var template = new Relation(guids.Create(), TenantA, "OLD01", "Old app B.V.", RelationRoles.Customer);
            context.Add(template);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            /* The old app's INSERT: every column of the table except the four this migration added, copied from a row. */
            var newColumns = new[] { "DebtorNumber", "CreditorNumber", "CarrierCode", "CurrencyId" };
            var connection = context.Database.GetDbConnection();
            var oldColumns = new List<string>();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
                command.CommandText = "SELECT column_name FROM information_schema.columns WHERE table_schema = 'masterdata' AND table_name = 'Relations' ORDER BY ordinal_position";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var column = reader.GetString(0);
                    if (!newColumns.Contains(column))
                    {
                        oldColumns.Add(column);
                    }
                }
            }

            oldColumns.ShouldContain("Code");
            var id = guids.Create();
            var target = string.Join(", ", oldColumns.Select(c => $"\"{c}\""));
            var source = string.Join(", ", oldColumns.Select(c => c switch
            {
                "Id" => "@id",
                "Code" => "'OLD02'",
                _ => $"\"{c}\""
            }));
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
                insert.CommandText = $"INSERT INTO masterdata.\"Relations\" ({target}) SELECT {source} FROM masterdata.\"Relations\" WHERE \"Id\" = @template";
                insert.Parameters.Add(new NpgsqlParameter("id", id));
                insert.Parameters.Add(new NpgsqlParameter("template", template.Id));
                (await insert.ExecuteNonQueryAsync()).ShouldBe(1);
            }

            var stored = await context.Set<Relation>().Include(r => r.ExternalIdentifiers).Include(r => r.TransportCharacteristics).SingleAsync(r => r.Id == id);
            stored.DebtorNumber.ShouldBeNull();
            stored.CreditorNumber.ShouldBeNull();
            stored.CarrierCode.ShouldBeNull();
            stored.CurrencyId.ShouldBeNull();
            stored.ExternalIdentifiers.ShouldBeEmpty();
            stored.TransportCharacteristics.ShouldBeEmpty();
        });
    }

    [Fact]
    public async Task A_relation_with_every_administration_value_round_trips_in_tenant_A()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var repository = GetRequiredService<IRepository<Relation, Guid>>();
            var context = await _contextProvider.GetDbContextAsync();
            var guids = GetRequiredService<IGuidGenerator>();
            var eur = await context.Set<Currency>().Where(c => c.IsoCode == "EUR").Select(c => c.Id).SingleAsync();
            var characteristic = new OpenTms.MasterData.Fleet.TransportCharacteristics(guids.Create(), TenantA, "QQ-ADR", "Dangerous goods");
            context.Add(characteristic);
            await context.SaveChangesAsync();

            var relation = new Relation(guids.Create(), TenantA, "ADM01", "Administration B.V.", RelationRoles.Customer);
            relation.SetAccountNumbers(" deb-01 ", new string('c', MasterDataConsts.MaxAccountNumberLength));
            relation.SetCarrierCode(new string('k', MasterDataConsts.MaxCarrierCodeLength));
            relation.SetCurrency(eur);
            relation.SetExternalIdentifiers(
                [new RelationExternalIdentifierInput(null, new string('s', MasterDataConsts.MaxExternalSourceLength), new string('i', MasterDataConsts.MaxExternalIdentifierLength)), new RelationExternalIdentifierInput(null, "EDI", "8712345000001")],
                guids.Create);
            relation.SetTransportCharacteristics([characteristic.Id]);
            await repository.InsertAsync(relation, autoSave: true);
            context.ChangeTracker.Clear();

            var stored = await context.Set<Relation>().Include(r => r.ExternalIdentifiers).Include(r => r.TransportCharacteristics).SingleAsync(r => r.Id == relation.Id);
            stored.DebtorNumber.ShouldBe("DEB-01");
            stored.CreditorNumber.ShouldBe(new string('C', MasterDataConsts.MaxAccountNumberLength));
            stored.CarrierCode!.Length.ShouldBe(MasterDataConsts.MaxCarrierCodeLength);
            stored.CurrencyId.ShouldBe(eur);
            stored.ExternalIdentifiers.Count.ShouldBe(2);
            stored.ExternalIdentifiers.ShouldAllBe(e => e.TenantId == TenantA);
            stored.ExternalIdentifiers.ShouldContain(e => e.Identifier.Length == MasterDataConsts.MaxExternalIdentifierLength);
            stored.TransportCharacteristics.ShouldHaveSingleItem().TransportCharacteristicsId.ShouldBe(characteristic.Id);
            stored.TransportCharacteristics.Single().TenantId.ShouldBe(TenantA);
            context.Database.GetDbConnection().Database.ShouldBe(PostgreSqlTestDatabases.TenantADatabase);
        });
    }

    /// <summary>Exactly one row starting with <paramref name="prefix"/>; the assertion names it when absent.</summary>
    [Theory]
    [InlineData("host")]
    [InlineData("tenant-a")]
    public async Task The_default_country_column_and_its_partial_unique_index_mirror_the_currency_default(string target)
    {
        /* PO 2026-10-13: Countries.IsDefault arrives in this migration, non-null with default false (expand only), and one
         * default per tenant is enforced like the currency default: unique on TenantId, NULLS NOT DISTINCT, filtered. */
        var database = target == "host" ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase;
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();

        var columns = await QueryAsync(connection, "SELECT column_name || ' :: ' || data_type || ' :: ' || is_nullable || ' :: ' || COALESCE(column_default, '-') FROM information_schema.columns WHERE table_schema = 'masterdata' AND table_name = 'Countries' AND column_name = 'IsDefault'");
        columns.ShouldHaveSingleItem().ShouldBe("IsDefault :: boolean :: NO :: false");

        var indexes = await QueryAsync(connection, "SELECT indexname || ' :: ' || indexdef FROM pg_indexes WHERE schemaname = 'masterdata' AND tablename IN ('Countries', 'Currencies')");
        var countryDefault = One(indexes, "IX_Countries_TenantId_Default :: ");
        countryDefault.ShouldContain("UNIQUE");
        countryDefault.ShouldContain("NULLS NOT DISTINCT");
        countryDefault.ShouldContain("(\"TenantId\")");
        countryDefault.ShouldContain("\"IsDefault\" = true");
        countryDefault.ShouldContain("\"IsDeleted\" = false");
        countryDefault.ShouldNotContain("\"IsoAlpha2\"", Case.Sensitive, "the default index is on the tenant alone");

        var currencyDefault = One(indexes, "IX_Currencies_TenantId_Default :: ");
        countryDefault.Replace("Countries", "Currencies").ShouldBe(currencyDefault, "the same definition as the run-1 currency default");

        /* One index enforces it: no second definition on the column. */
        indexes.Count(i => i.Contains("\"IsDefault\" = true") && i.Contains("\"Countries\"")).ShouldBe(1);
    }

    private static string One(List<string> rows, string prefix, string? because = null)
    {
        rows.ShouldContain(r => r.StartsWith(prefix, StringComparison.Ordinal), because ?? prefix);
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
