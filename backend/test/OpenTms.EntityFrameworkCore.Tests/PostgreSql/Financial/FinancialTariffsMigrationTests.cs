using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using OpenTms.Financial.EntityFrameworkCore;
using OpenTms.Financial.EntityFrameworkCore.Migrations;
using OpenTms.PostgreSql.ReferenceData;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace OpenTms.PostgreSql.Financial;

/// <summary>
/// The FinancialTariffs migration (design 57 "Migration strategy") on the databases the real DbMigrator created: applied
/// after FinancialMasterData on the host and on a tenant database, nothing pending, the snapshot current; expand only
/// (new tables, indexes and the four discount columns), Down drops exactly its own tables and columns; the per-tenant
/// unique indexes (NULLS NOT DISTINCT, soft-delete filter), the natural keys, the anchor check, the cascading and
/// restricting foreign keys, the three rate-line indexes and the numeric precisions. Risk class: Critical.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class FinancialTariffsMigrationTests : ReferenceDataPostgreSqlTestBase
{
    private const string Migration = "20261025191117_FinancialTariffs";
    private const string PreviousMigration = "20261022234846_FinancialMasterData";

    private static readonly string[] Tables =
    [
        "TariffsPriceLists", "TariffsPriceListColumns", "TariffsPriceListRules", "TariffsPriceListBrackets", "TariffsPriceListCells",
        "TariffsRateLines", "InvoicingRelationFinancialSettings", "InvoicingRelationPriceLists", "InvoicingRelationDiscountsOrSurcharges"
    ];

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_migration_is_applied_after_the_previous_one_and_nothing_is_pending(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var context = await GetRequiredService<IDbContextProvider<FinancialDbContext>>().GetDbContextAsync();

            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.ShouldContain(Migration);
            applied.IndexOf(Migration).ShouldBe(applied.IndexOf(PreviousMigration) + 1);
            applied.Last().ShouldBe(FinancialRevenueChargedBasesMigrationTests.Migration, "the newest Financial migration (design 61 \"Order page review\": the revenue charged bases)");
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            context.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot must describe the model that runs");
            context.Database.GetDbConnection().Database.ShouldBe(tenant == null ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase);
        });
    }

    [Fact]
    public void The_migration_is_expand_only_and_its_Down_drops_exactly_its_own_tables_and_columns()
    {
        var migration = new FinancialTariffs();

        var up = migration.UpOperations;
        up.ShouldAllBe(o => o is CreateTableOperation || o is CreateIndexOperation || o is AddColumnOperation, string.Join(", ", up.Select(o => o.GetType().Name).Distinct()));
        up.OfType<CreateTableOperation>().Select(t => t.Name).ShouldBe(Tables, ignoreOrder: true);
        up.OfType<CreateTableOperation>().ShouldAllBe(t => t.Schema == "financial");
        up.OfType<AddColumnOperation>().Select(c => c.Name).ShouldBe(["AppliesAfterOthers", "BillableKilometrePercentage", "KilometreBasis", "RoundTrip"], ignoreOrder: true);
        up.OfType<AddColumnOperation>().ShouldAllBe(c => c.Table == "TariffsDiscountsOrSurcharges" && !c.IsNullable, "the discount columns have defaults, so the existing rows keep working");
        up.OfType<CreateIndexOperation>().Select(i => i.Table).Distinct().ShouldAllBe(t => Tables.Contains(t), "indexes only on the new tables");

        var down = migration.DownOperations;
        down.ShouldAllBe(o => o is DropTableOperation || o is DropColumnOperation);
        down.OfType<DropTableOperation>().Select(t => t.Name).ShouldBe(Tables, ignoreOrder: true);
        down.OfType<DropColumnOperation>().Select(c => c.Name).ShouldBe(["AppliesAfterOthers", "BillableKilometrePercentage", "KilometreBasis", "RoundTrip"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("tenant-a")]
    public async Task Every_table_index_check_and_foreign_key_is_in_place(string target)
    {
        var database = target == "host" ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase;
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();

        var tables = await QueryAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'financial'");
        foreach (var table in Tables)
        {
            tables.ShouldContain(table);
        }

        var indexes = await QueryAsync(connection, "SELECT indexname || ' :: ' || indexdef FROM pg_indexes WHERE schemaname = 'financial'");

        foreach (var (name, keyColumns) in new[]
                 {
                     ("IX_TariffsPriceLists_TenantId_Code", "(\"TenantId\", \"Code\")"),
                     ("IX_InvoicingRelationFinancialSettings_TenantId_RelationId", "(\"TenantId\", \"RelationId\")")
                 })
        {
            var index = One(indexes, name + " :: ");
            index.ShouldContain("UNIQUE", Case.Sensitive, name);
            index.ShouldContain("NULLS NOT DISTINCT", Case.Sensitive, name);
            index.ShouldContain(keyColumns, Case.Sensitive, name);
            index.ShouldContain("\"IsDeleted\" = false", Case.Sensitive, name);
        }

        foreach (var (name, keyColumns) in new[]
                 {
                     ("IX_TariffsPriceListBrackets_RuleId_Position", "(\"PriceListRuleId\", \"Position\")"),
                     ("IX_InvoicingRelationPriceLists_Settings_Side_PriceList", "(\"RelationFinancialSettingsId\", \"Side\", \"PriceListId\")"),
                     ("IX_InvoicingRelationDiscountsOrSurcharges_Settings_Item", "(\"RelationFinancialSettingsId\", \"DiscountOrSurchargeId\")")
                 })
        {
            var index = One(indexes, name + " :: ");
            index.ShouldContain("UNIQUE", Case.Sensitive, name);
            index.ShouldContain(keyColumns, Case.Sensitive, name);
        }

        /* The three rate-line reads of design 57 and the rule page. */
        One(indexes, "IX_TariffsRateLines_TenantId_AnchorOrderId :: ").ShouldContain("(\"TenantId\", \"AnchorOrderId\")");
        One(indexes, "IX_TariffsRateLines_TenantId_AnchorTripId :: ").ShouldContain("(\"TenantId\", \"AnchorTripId\")");
        One(indexes, "IX_TariffsRateLines_TenantId_LegId :: ").ShouldContain("(\"TenantId\", \"LegId\")");
        One(indexes, "IX_TariffsPriceListRules_PriceListId_Position :: ").ShouldContain("(\"PriceListId\", \"Position\")");
        One(indexes, "PK_TariffsPriceListColumns :: ").ShouldContain("(\"PriceListId\", \"Position\")");
        One(indexes, "PK_TariffsPriceListCells :: ").ShouldContain("(\"PriceListBracketId\", \"ColumnPosition\")");

        var checks = await QueryAsync(connection, "SELECT conname || ' :: ' || pg_get_constraintdef(oid) FROM pg_constraint WHERE contype = 'c' AND connamespace = 'financial'::regnamespace");
        var anchor = One(checks, "CK_TariffsRateLines_OneAnchor :: ");
        foreach (var column in new[] { "OrderId", "OrderLineId", "LegId", "StopId", "TripId" })
        {
            anchor.ShouldContain($"\"{column}\" IS NOT NULL");
        }

        anchor.ShouldContain("= 1");

        var foreignKeys = await QueryAsync(connection, "SELECT conname || ' :: ' || confdeltype::text FROM pg_constraint WHERE contype = 'f' AND connamespace = 'financial'::regnamespace");
        foreignKeys.ShouldContain("FK_TariffsPriceListColumns_TariffsPriceLists_PriceListId :: c");
        foreignKeys.ShouldContain("FK_TariffsPriceListRules_TariffsPriceLists_PriceListId :: c");
        foreignKeys.ShouldContain(f => f.StartsWith("FK_TariffsPriceListBrackets_TariffsPriceListRules_", StringComparison.Ordinal) && f.EndsWith(":: c", StringComparison.Ordinal));
        foreignKeys.ShouldContain(f => f.StartsWith("FK_TariffsPriceListCells_TariffsPriceListBrackets_", StringComparison.Ordinal) && f.EndsWith(":: c", StringComparison.Ordinal));
        foreignKeys.ShouldContain("FK_InvoicingRelationPriceLists_TariffsPriceLists_PriceListId :: r");
        foreignKeys.ShouldContain(f => f.StartsWith("FK_InvoicingRelationDiscountsOrSurcharges_TariffsDiscountsOrSu", StringComparison.Ordinal) && f.EndsWith(":: r", StringComparison.Ordinal));
        foreignKeys.ShouldNotContain(f => f.StartsWith("FK_TariffsRateLines_", StringComparison.Ordinal), "a rate line's anchors and provenance are ids without keys (AD-20)");

        var columns = await QueryAsync(connection,
            "SELECT table_name || '.' || column_name || ' :: ' || data_type || '(' || COALESCE(numeric_precision::text, character_maximum_length::text, '-') || ',' || COALESCE(numeric_scale::text, '-') || ') :: ' || is_nullable " +
            "FROM information_schema.columns WHERE table_schema = 'financial' AND table_name IN ('TariffsPriceLists', 'TariffsPriceListColumns', 'TariffsPriceListRules', 'TariffsPriceListCells', 'TariffsRateLines', 'TariffsDiscountsOrSurcharges', 'InvoicingRelationFinancialSettings')");
        foreach (var expected in new[]
                 {
                     "TariffsPriceLists.Code :: character varying(16,-) :: NO",
                     "TariffsPriceLists.CurrencyCode :: character varying(3,-) :: NO",
                     "TariffsPriceLists.UnitConversionFactor :: numeric(18,6) :: NO",
                     "TariffsPriceLists.ValidTill :: date(-,-) :: YES",
                     "TariffsPriceListColumns.UpperBound :: numeric(18,4) :: YES",
                     "TariffsPriceListRules.MinAmount :: numeric(18,2) :: YES",
                     "TariffsPriceListRules.FromRelationId :: uuid(-,-) :: YES",
                     "TariffsPriceListCells.UnitPrice :: numeric(18,4) :: YES",
                     "TariffsRateLines.Quantity :: numeric(18,4) :: NO",
                     "TariffsRateLines.UnitPrice :: numeric(18,4) :: NO",
                     "TariffsRateLines.Amount :: numeric(18,2) :: NO",
                     "TariffsRateLines.DescriptionKey :: character varying(64,-) :: YES",
                     "TariffsRateLines.Description :: character varying(256,-) :: YES",
                     "TariffsRateLines.CalculationVersion :: bigint(64,0) :: YES",
                     "TariffsDiscountsOrSurcharges.BillableKilometrePercentage :: numeric(5,2) :: NO",
                     "InvoicingRelationFinancialSettings.RelationId :: uuid(-,-) :: NO"
                 })
        {
            columns.ShouldContain(expected);
        }
    }

    private static string One(List<string> rows, string prefix) => rows.ShouldHaveSingleItem(prefix, rows.Where(r => r.StartsWith(prefix, StringComparison.Ordinal)));

    private static async Task<List<string>> QueryAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}

file static class ShouldlyRows
{
    public static string ShouldHaveSingleItem(this List<string> rows, string prefix, IEnumerable<string> matching)
    {
        var list = matching.ToList();
        list.Count.ShouldBe(1, $"expected exactly one row starting with '{prefix}' among {rows.Count} rows");
        return list[0];
    }
}
