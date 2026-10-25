using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using OpenTms.PlanningExecution.EntityFrameworkCore;
using OpenTms.PlanningExecution.EntityFrameworkCore.Migrations;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace OpenTms.PostgreSql.PlanningExecution;

/// <summary>
/// The PlanningLegsAndTrips migration on the databases the real DbMigrator created (design 59 "Migration strategy"):
/// applied after PlanningTransportTripTypes on the host and on a tenant database, nothing pending, the snapshot current;
/// expand only (the pg_trgm extension, eight tables, their indexes) and Down drops exactly those tables; the unique keys
/// (NULLS NOT DISTINCT, soft-delete filter), the partial unplanned-legs index, the trigram indexes, the check constraints,
/// the foreign keys and the money, measure and jsonb column types. Risk class: Critical.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PlanningLegsAndTripsMigrationTests : PlanningPostgreSqlTestBase
{
    private const string Migration = "20261025192744_PlanningLegsAndTrips";
    private const string PreviousMigration = "20261022234410_PlanningTransportTripTypes";

    private static readonly string[] Tables =
    [
        "Legs", "TransportTrips", "TripVehicles", "TripDrivers", "TripStops", "TripStopLegs", "ViaDepotRules", "TripAttributeDefinitions"
    ];

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_migration_is_applied_after_the_previous_one_and_nothing_is_pending(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var context = await GetRequiredService<IDbContextProvider<PlanningExecutionDbContext>>().GetDbContextAsync();

            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.ShouldContain(Migration);
            applied.IndexOf(Migration).ShouldBe(applied.IndexOf(PreviousMigration) + 1);
            applied.Last().ShouldBe(Migration, "the newest Planning migration");
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            context.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot must describe the model that runs");
            context.Database.GetDbConnection().Database.ShouldBe(tenant == null ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase);
        });
    }

    [Fact]
    public void The_migration_is_expand_only_and_its_Down_drops_exactly_its_own_tables()
    {
        var migration = new PlanningLegsAndTrips();

        var up = migration.UpOperations;
        up.ShouldAllBe(o => o is CreateTableOperation || o is CreateIndexOperation || o is AlterDatabaseOperation, string.Join(", ", up.Select(o => o.GetType().Name).Distinct()));
        up.OfType<CreateTableOperation>().Select(t => t.Name).ShouldBe(Tables, ignoreOrder: true);
        up.OfType<CreateTableOperation>().ShouldAllBe(t => t.Schema == "planning");
        up.OfType<CreateIndexOperation>().Select(i => i.Table).Distinct().ShouldAllBe(t => Tables.Contains(t), "indexes only on the new tables");

        var down = migration.DownOperations;
        down.ShouldAllBe(o => o is DropTableOperation || o is AlterDatabaseOperation);
        down.OfType<DropTableOperation>().Select(t => t.Name).ShouldBe(Tables, ignoreOrder: true);
        down.OfType<DropTableOperation>().ShouldAllBe(t => t.Schema == "planning");
    }

    [Theory]
    [InlineData("host")]
    [InlineData("tenant-a")]
    public async Task Every_table_index_check_and_foreign_key_is_in_place(string target)
    {
        var database = target == "host" ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase;
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();

        var tables = await QueryAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'planning'");
        foreach (var table in Tables)
        {
            tables.ShouldContain(table);
        }

        var indexes = await QueryAsync(connection, "SELECT indexname || ' :: ' || indexdef FROM pg_indexes WHERE schemaname = 'planning'");

        foreach (var (name, keyColumns) in new[]
                 {
                     ("IX_Legs_TenantId_OrderLineId_OriginStopId_DestinationStopId", "(\"TenantId\", \"OrderLineId\", \"OriginStopId\", \"DestinationStopId\")"),
                     ("IX_TransportTrips_TenantId_Number", "(\"TenantId\", \"Number\")"),
                     ("IX_TripAttributeDefinitions_TenantId_Key", "(\"TenantId\", \"Key\")")
                 })
        {
            var index = One(indexes, name + " :: ");
            index.ShouldContain("UNIQUE", Case.Sensitive, name);
            index.ShouldContain("NULLS NOT DISTINCT", Case.Sensitive, name);
            index.ShouldContain(keyColumns, Case.Sensitive, name);
            index.ShouldContain("\"IsDeleted\" = false", Case.Sensitive, name);
        }

        var stopLegs = One(indexes, "IX_TripStopLegs_LegId_Role :: ");
        stopLegs.ShouldContain("UNIQUE");
        stopLegs.ShouldContain("(\"LegId\", \"Role\")");

        var unplanned = One(indexes, "IX_Legs_TenantId_Unplanned :: ");
        unplanned.ShouldContain("\"TripId\" IS NULL");
        unplanned.ShouldContain("(\"TenantId\", \"OriginPlannedDate\")");

        foreach (var trigram in new[] { "IX_TransportTrips_Number_Trigram", "IX_TransportTrips_Name_Trigram", "IX_TransportTrips_SearchKey_Trigram" })
        {
            One(indexes, trigram + " :: ").ShouldContain("gin_trgm_ops", Case.Sensitive, trigram);
        }

        One(indexes, "IX_TransportTrips_TenantId_Status_TripDate :: ").ShouldContain("(\"TenantId\", \"Status\", \"TripDate\")");
        One(indexes, "IX_TransportTrips_TenantId_CarrierRelationId :: ").ShouldContain("(\"TenantId\", \"CarrierRelationId\")");
        One(indexes, "PK_TripStopLegs :: ").ShouldContain("(\"TripStopId\", \"LegId\", \"Role\")");
        One(indexes, "PK_TripVehicles :: ").ShouldContain("(\"TripId\", \"Position\")");

        var checks = await QueryAsync(connection, "SELECT conname || ' :: ' || pg_get_constraintdef(oid) FROM pg_constraint WHERE contype = 'c' AND connamespace = 'planning'::regnamespace");
        One(checks, "CK_TripVehicles_Position :: ").ShouldContain("\"Position\" <= 3");
        One(checks, "CK_TripDrivers_Position :: ").ShouldContain("\"Position\" <= 3");
        One(checks, "CK_Legs_Position :: ").ShouldContain("\"Position\" >= 1");
        One(checks, "CK_TripStops_Position :: ").ShouldContain("\"Position\" >= 1");

        var foreignKeys = await QueryAsync(connection, "SELECT conname || ' :: ' || confdeltype::text FROM pg_constraint WHERE contype = 'f' AND connamespace = 'planning'::regnamespace");
        foreignKeys.ShouldContain("FK_TransportTrips_TransportTripTypes_TransportTripTypeId :: r", "a type in use is never deleted");
        foreignKeys.ShouldContain("FK_TripStops_TransportTrips_TripId :: c");
        foreignKeys.ShouldContain("FK_TripStopLegs_TripStops_TripStopId :: c");
        foreignKeys.ShouldContain("FK_TripVehicles_TransportTrips_TripId :: c");
        foreignKeys.ShouldContain("FK_TripDrivers_TransportTrips_TripId :: c");
        foreignKeys.ShouldNotContain(f => f.StartsWith("FK_Legs_", StringComparison.Ordinal), "cross-module ids carry no foreign key (design 56)");

        var columns = await QueryAsync(connection,
            "SELECT table_name || '.' || column_name || ' :: ' || data_type || '(' || COALESCE(numeric_precision::text || ',' || numeric_scale::text, character_maximum_length::text, '-') || ') :: ' || is_nullable " +
            "FROM information_schema.columns WHERE table_schema = 'planning' AND table_name IN ('Legs', 'TransportTrips', 'TripStops', 'ViaDepotRules')");
        foreach (var expected in new[]
                 {
                     "Legs.AllocatedCostAmount :: numeric(18,2) :: YES",
                     "Legs.AllocatedRevenueAmount :: numeric(18,2) :: YES",
                     "Legs.EstimatedCo2Kg :: numeric(18,3) :: YES",
                     "Legs.Quantity :: numeric(18,4) :: NO",
                     "Legs.DistanceM :: integer(32,0) :: YES",
                     "Legs.OriginPlannedDate :: date(-) :: NO",
                     "Legs.OriginWindowFrom :: timestamp with time zone(-) :: YES",
                     "Legs.RequiredTransportCharacteristicIds :: jsonb(-) :: NO",
                     "Legs.OriginName :: character varying(256) :: NO",
                     "Legs.TenantId :: uuid(-) :: YES",
                     "TransportTrips.Number :: character varying(32) :: NO",
                     "TransportTrips.Warnings :: jsonb(-) :: NO",
                     "TransportTrips.Attributes :: jsonb(-) :: NO",
                     "TransportTrips.LoadFactor :: numeric(9,4) :: YES",
                     "TransportTrips.Co2Kg :: numeric(18,3) :: YES",
                     "TransportTrips.CostAmount :: numeric(18,2) :: YES",
                     "TransportTrips.PlannedDeparture :: timestamp with time zone(-) :: NO",
                     "TransportTrips.PlannedDepartureUtc :: timestamp without time zone(-) :: NO",
                     "TransportTrips.TripDate :: date(-) :: NO",
                     "TransportTrips.SearchKey :: character varying(2000) :: NO",
                     "TripStops.PlannedCumulativeDistanceM :: integer(32,0) :: YES",
                     "TripStops.Remarks :: character varying(2500) :: YES",
                     "ViaDepotRules.Scope :: integer(32,0) :: NO"
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
