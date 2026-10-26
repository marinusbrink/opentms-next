using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using OpenTms.Orders.EntityFrameworkCore;
using OpenTms.Orders.EntityFrameworkCore.Migrations;
using OpenTms.PostgreSql.ReferenceData;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace OpenTms.PostgreSql.Orders;

/// <summary>
/// The OrdersTransportOrders migration on the databases the real DbMigrator created (design 58 "Migration strategy"):
/// applied after OrdersMasterData on the host and on a tenant database, nothing pending, the model snapshot current;
/// expand only (tables, indexes, the pg_trgm extension) and Down drops exactly its tables; the per-tenant unique indexes
/// (NULLS NOT DISTINCT, soft-delete filter, the number and template-copy filters), the trigram indexes, the check
/// constraints, the composite keys of the children and the money precisions. Risk class: Critical.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class OrdersTransportOrdersMigrationTests : ReferenceDataPostgreSqlTestBase
{
    private const string Migration = "20261025200600_OrdersTransportOrders";
    private const string PreviousMigration = "20261022235137_OrdersMasterData";

    private static readonly string[] Tables =
    [
        "TransportOrders", "TransportOrderLines", "TransportOrderLineMeasurements", "TransportOrderLineCharacteristics", "TransportOrderStops",
        "TransportOrderDefaultStops", "TransportOrderReferences", "TransportOrderPriceLists", "TransportOrderDiscountsOrSurcharges", "TransportOrderRemarks",
        "TransportOrderTemplates", "TransportOrderTemplateLines", "TransportOrderTemplateStops", "TransportOrderTemplateDefaultStops", "TransportOrderTemplateReferences",
        "TransportOrderTemplatePriceLists", "TransportOrderTemplateDiscountsOrSurcharges",
        "ConsignmentNoteProfiles", "ConsignmentNoteFixedDescriptions", "ConsignmentNoteDocuments", "ConsignmentNoteDocumentLines", "OrderAttributeDefinitions"
    ];

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_migration_is_applied_after_the_previous_one_and_nothing_is_pending(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var context = await GetRequiredService<IDbContextProvider<OrdersDbContext>>().GetDbContextAsync();
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.ShouldContain(Migration);
            applied.IndexOf(Migration).ShouldBe(applied.IndexOf(PreviousMigration) + 1);
            applied.Last().ShouldBe("20261026223019_OrdersTripRequestLines", "the newest Orders migration (the trip request's freight items)");
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            context.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot must describe the model that runs");
            context.Database.GetDbConnection().Database.ShouldBe(tenant == null ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase);
        });
    }

    [Fact]
    public void The_migration_is_expand_only_and_its_Down_drops_exactly_its_own_tables()
    {
        var migration = new OrdersTransportOrders();

        var up = migration.UpOperations;
        up.ShouldAllBe(o => o is CreateTableOperation || o is CreateIndexOperation || o is AlterDatabaseOperation, string.Join(", ", up.Select(o => o.GetType().Name).Distinct()));
        up.OfType<CreateTableOperation>().Select(t => t.Name).ShouldBe(Tables, ignoreOrder: true);
        up.OfType<CreateTableOperation>().ShouldAllBe(t => t.Schema == "orders");
        up.OfType<CreateIndexOperation>().Select(i => i.Table).Distinct().ShouldAllBe(t => Tables.Contains(t));

        var down = migration.DownOperations;
        down.OfType<DropTableOperation>().Select(t => t.Name).ShouldBe(Tables, ignoreOrder: true);
        down.OfType<DropTableOperation>().ShouldAllBe(t => t.Schema == "orders");
    }

    [Theory]
    [InlineData("host")]
    [InlineData("tenant-a")]
    public async Task Every_table_index_check_and_key_is_in_place(string target)
    {
        var database = target == "host" ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase;
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();

        var tables = await QueryAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'orders'");
        foreach (var table in Tables)
        {
            tables.ShouldContain(table);
        }

        var indexes = await QueryAsync(connection, "SELECT indexname || ' :: ' || indexdef FROM pg_indexes WHERE schemaname = 'orders'");
        foreach (var (name, keyColumns, where) in new[]
                 {
                     ("IX_TransportOrders_TenantId_Number", "(\"TenantId\", \"Number\")", "\"Number\" IS NOT NULL"),
                     ("IX_TransportOrders_TenantId_Template_Date_Copy", "(\"TenantId\", \"SourceTemplateId\", \"OrderDate\", \"CopyIndex\")", "\"SourceTemplateId\" IS NOT NULL"),
                     ("IX_TransportOrderTemplates_TenantId_NormalizedName", "(\"TenantId\", \"NormalizedName\")", "\"IsDeleted\" = false"),
                     ("IX_ConsignmentNoteProfiles_TenantId_NormalizedName", "(\"TenantId\", \"NormalizedName\")", "\"IsDeleted\" = false"),
                     ("IX_ConsignmentNoteProfiles_TenantId_Default", "(\"TenantId\")", "\"IsDefault\" = true"),
                     ("IX_ConsignmentNoteFixedDescriptions_TenantId_NormalizedName", "(\"TenantId\", \"NormalizedName\")", "\"IsDeleted\" = false"),
                     ("IX_ConsignmentNoteDocuments_TenantId_Number", "(\"TenantId\", \"Number\")", "\"IsDeleted\" = false"),
                     ("IX_OrderAttributeDefinitions_TenantId_Target_Key", "(\"TenantId\", \"Target\", \"Key\")", "\"IsDeleted\" = false")
                 })
        {
            var index = One(indexes, name + " :: ");
            index.ShouldContain("UNIQUE", Case.Sensitive, name);
            index.ShouldContain("NULLS NOT DISTINCT", Case.Sensitive, name);
            index.ShouldContain(keyColumns, Case.Sensitive, name);
            index.ShouldContain(where, Case.Sensitive, name);
            index.ShouldContain("\"IsDeleted\" = false", Case.Sensitive, name);
        }

        foreach (var name in new[] { "IX_TransportOrders_Number_Trigram", "IX_TransportOrders_CustomerReference_Trigram", "IX_TransportOrders_CustomerName_Trigram", "IX_TransportOrders_FirstStopName_Trigram", "IX_TransportOrders_LastStopName_Trigram" })
        {
            var index = One(indexes, name + " :: ");
            index.ShouldContain("USING gin", Case.Sensitive, name);
            index.ShouldContain("gin_trgm_ops", Case.Sensitive, name);
        }

        One(indexes, "IX_TransportOrders_TenantId_Status_OrderDate :: ").ShouldContain("(\"TenantId\", \"Status\", \"OrderDate\")");
        One(indexes, "IX_TransportOrders_TenantId_CustomerRelationId :: ").ShouldContain("(\"TenantId\", \"CustomerRelationId\")");
        One(indexes, "IX_ConsignmentNoteDocuments_TenantId_OrderId :: ").ShouldContain("(\"TenantId\", \"OrderId\")");
        One(indexes, "PK_TransportOrderLineMeasurements :: ").ShouldContain("(\"LineId\", \"Kind\", \"Position\")");
        One(indexes, "PK_TransportOrderLineCharacteristics :: ").ShouldContain("(\"LineId\", \"TransportCharacteristicId\")");
        One(indexes, "PK_TransportOrderDefaultStops :: ").ShouldContain("(\"OrderId\", \"Position\")");
        One(indexes, "PK_TransportOrderTemplateStops :: ").ShouldContain("(\"LineId\", \"Position\")");
        One(indexes, "PK_ConsignmentNoteDocumentLines :: ").ShouldContain("(\"DocumentId\", \"LineId\")");

        var checks = await QueryAsync(connection, "SELECT conname || ' :: ' || pg_get_constraintdef(oid) FROM pg_constraint WHERE contype = 'c' AND connamespace = 'orders'::regnamespace");
        One(checks, "CK_TransportOrders_Status :: ").ShouldContain("\"Status\" >= 1");
        One(checks, "CK_TransportOrders_Status :: ").ShouldContain("\"Status\" <= 6");
        One(checks, "CK_TransportOrderLines_Position :: ").ShouldContain("\"Position\" >= 1");
        One(checks, "CK_TransportOrderStops_Position :: ").ShouldContain("\"Position\" >= 1");
        One(checks, "CK_TransportOrderLineMeasurements_Position :: ").ShouldContain("\"Position\" <= 3");
        One(checks, "CK_ConsignmentNoteProfiles_Copies :: ").ShouldContain("\"Copies\" <= 5");

        var foreignKeys = await QueryAsync(connection, "SELECT conname || ' :: ' || confdeltype::text FROM pg_constraint WHERE contype = 'f' AND connamespace = 'orders'::regnamespace");
        foreignKeys.ShouldContain("FK_TransportOrderLines_TransportOrders_OrderId :: c");
        foreignKeys.ShouldContain("FK_TransportOrderStops_Line :: c");
        foreignKeys.ShouldContain("FK_TransportOrderTemplateLines_Template :: c");
        foreignKeys.ShouldContain("FK_ConsignmentNoteDocumentLines_Document :: c");
        /* The design-51 constraints of the master data keep their provider-truncated names; none of this migration's is truncated. */
        foreignKeys.Where(f => !f.StartsWith("FK_TransportOrderType", StringComparison.Ordinal) && !f.StartsWith("FK_WaypointType", StringComparison.Ordinal))
            .ShouldAllBe(f => !f.Contains('~'), "no constraint name of design 58 was truncated by PostgreSQL");
        indexes.Where(i => Tables.Any(t => i.Contains("_" + t + "_", StringComparison.Ordinal) || i.StartsWith("IX_" + t, StringComparison.Ordinal)))
            .ShouldAllBe(i => !i.Split(" :: ")[0].Contains('~'), "no index name of design 58 was truncated by PostgreSQL");
        foreignKeys.ShouldNotContain(f => f.StartsWith("FK_TransportOrders_TransportOrderTypes", StringComparison.Ordinal), "cross-aggregate ids carry no constraint; the lookup validates");

        var columns = await QueryAsync(connection,
            "SELECT table_name || '.' || column_name || ' :: ' || data_type || COALESCE('(' || numeric_precision || ',' || numeric_scale || ')', COALESCE('(' || character_maximum_length::text || ')', '')) || ' :: ' || is_nullable " +
            "FROM information_schema.columns WHERE table_schema = 'orders' AND table_name IN ('TransportOrders', 'TransportOrderLines', 'TransportOrderStops', 'ConsignmentNoteDocuments')");
        foreach (var expected in new[]
                 {
                     "TransportOrders.Number :: character varying(32) :: YES",
                     "TransportOrders.CustomerName :: character varying(128) :: NO",
                     "TransportOrders.Attributes :: jsonb :: NO",
                     "TransportOrders.FixedPriceAmount :: numeric(18,2) :: YES",
                     "TransportOrders.RevenueAmount :: numeric(18,2) :: YES",
                     "TransportOrders.Co2Kg :: numeric(18,3) :: YES",
                     "TransportOrders.DraftConfidence :: numeric(5,4) :: YES",
                     "TransportOrders.CurrencyCode :: character varying(3) :: NO",
                     "TransportOrders.TripRequest_PlannedDeparture :: timestamp with time zone :: YES",
                     "TransportOrders.TripRequest_VehicleIds :: jsonb :: YES",
                     "TransportOrderLines.Freight_Quantity :: numeric(18,4) :: NO",
                     "TransportOrderLines.Attributes :: jsonb :: NO",
                     "TransportOrderStops.Date :: date :: NO",
                     "TransportOrderStops.Details_WindowFrom :: time without time zone :: YES",
                     "ConsignmentNoteDocuments.DataSnapshot :: jsonb :: NO",
                     "ConsignmentNoteDocuments.Sha256 :: character varying(64) :: NO"
                 })
        {
            columns.ShouldContain(expected);
        }
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

    private static string One(IEnumerable<string> rows, string prefix)
        => rows.Where(r => r.StartsWith(prefix, StringComparison.Ordinal)).ToList().ShouldHaveSingleItem(prefix);
}
