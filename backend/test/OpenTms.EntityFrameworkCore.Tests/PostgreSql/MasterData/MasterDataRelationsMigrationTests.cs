using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTms.MasterData.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
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
    private const string Migration = "20261012121014_MasterDataRelationsAndDrivers";
    private const string FirstRunMigration = "20261011191016_MasterDataFirstRun";

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

    /// <summary>Exactly one row starting with <paramref name="prefix"/>; the assertion names it when absent.</summary>
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
