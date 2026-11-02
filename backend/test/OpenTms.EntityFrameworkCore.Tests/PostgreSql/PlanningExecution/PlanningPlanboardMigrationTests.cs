using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.EntityFrameworkCore;
using OpenTms.PlanningExecution.EntityFrameworkCore.Migrations;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace OpenTms.PostgreSql.PlanningExecution;

/// <summary>
/// The PlanningPlanboard migration (design 68 part 1: the combination flag on a trip's vehicles and the scheduler's
/// lookup indexes) on the databases the real DbMigrator created (applied right after PlanningTripTypeDefaults and followed
/// by PlanningPlanboardAvailability, nothing pending, the snapshot current, the column and the three indexes in place on
/// the host and a tenant database) and on a scratch database with data: a trip vehicle the previous release stored gets
/// FromCombination false, the previous release's INSERT without the column keeps working (expand only, constitution rule
/// 2), and Down drops exactly the column and the indexes and keeps the rows. Risk class: Critical (a migration on every
/// tenant database).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PlanningPlanboardMigrationTests : PlanningPostgreSqlTestBase
{
    public const string Migration = "20261031183434_PlanningPlanboard";
    private const string PreviousMigration = PlanningTripTypeDefaultsMigrationTests.Migration;
    private const string Schema = PlanningExecutionDbProperties.DbSchema;

    private static readonly Dictionary<string, (string Table, string Columns)> Indexes = new()
    {
        ["IX_TripVehicles_TenantId_VehicleId"] = ("TripVehicles", "(\"TenantId\", \"VehicleId\")"),
        ["IX_TripDrivers_TenantId_DriverId"] = ("TripDrivers", "(\"TenantId\", \"DriverId\")"),
        ["IX_TransportTrips_TenantId_PlannedArrivalUtc"] = ("TransportTrips", "(\"TenantId\", \"PlannedArrivalUtc\")")
    };

    /* Same length as the shared tenant database's postfix, so the name stays within PostgreSQL's 63-char limit. */
    private static readonly string Scratch = PostgreSqlTestDatabases.HostDatabase + "_PlnPbrd";

    static PlanningPlanboardMigrationTests()
    {
        /* See MoveFeatureValuesToAppTreeMigrationTests: Npgsql reads this switch once per process. */
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    private static PlanningExecutionDbContext CreateContext()
        => new(new DbContextOptionsBuilder<PlanningExecutionDbContext>()
            .UseNpgsql(PostgreSqlTestDatabases.ConnectionStringFor(Scratch), b => b.MigrationsHistoryTable("__EFMigrationsHistory", Schema))
            .Options);

    private static async Task MigrateToAsync(string target)
    {
        await using var context = CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task<List<string>> QueryAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static Task<List<string>> IndexDefinitionAsync(string database, string index)
        => QueryAsync(database, $"SELECT indexdef FROM pg_indexes WHERE schemaname = '{Schema}' AND indexname = '{index}'");

    /* A trip vehicle as the previous release wrote it: its type and trip first (foreign keys), then the row without FromCombination. */
    private static async Task<Guid> InsertTripAsync(Guid tenantId)
    {
        var typeId = await ChargedBasesMigrationRows.InsertAsync(Scratch, Schema, "TransportTripTypes", new Dictionary<string, object> { ["TenantId"] = tenantId });
        return await ChargedBasesMigrationRows.InsertAsync(Scratch, Schema, "TransportTrips", new Dictionary<string, object> { ["TenantId"] = tenantId, ["TransportTripTypeId"] = typeId });
    }

    private static Task InsertTripVehicleAsync(Guid tenantId, Guid tripId, int position)
        => ChargedBasesMigrationRows.InsertAsync(Scratch, Schema, "TripVehicles", new Dictionary<string, object>
        {
            ["TenantId"] = tenantId, ["TripId"] = tripId, ["Position"] = position, ["VehicleId"] = Guid.NewGuid()
        });

    private static async Task<List<string>> FromCombinationOfAsync(Guid tripId)
        => await QueryAsync(Scratch, $"SELECT \"FromCombination\"::text FROM {Schema}.\"TripVehicles\" WHERE \"TripId\" = '{tripId}' ORDER BY \"Position\"");

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_migration_follows_the_previous_one_and_nothing_is_pending(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var context = await GetRequiredService<IDbContextProvider<PlanningExecutionDbContext>>().GetDbContextAsync();
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.IndexOf(Migration).ShouldBe(applied.IndexOf(PreviousMigration) + 1);
            applied.IndexOf(PlanningPlanboardAvailabilityMigrationTests.Migration).ShouldBe(applied.IndexOf(Migration) + 1, "the availability migration follows (design 68 part 2)");
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            context.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot must describe the model that runs");
            context.Database.GetDbConnection().Database.ShouldBe(tenant == null ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase);
        });
    }

    [Fact]
    public void The_migration_is_expand_only_and_its_Down_drops_exactly_what_it_added()
    {
        var migration = new PlanningPlanboard();

        migration.UpOperations.ShouldAllBe(o => o is AddColumnOperation || o is CreateIndexOperation);
        var column = migration.UpOperations.OfType<AddColumnOperation>().ShouldHaveSingleItem();
        column.Name.ShouldBe("FromCombination");
        column.Table.ShouldBe("TripVehicles");
        column.Schema.ShouldBe(Schema);
        column.IsNullable.ShouldBeFalse();
        column.DefaultValue.ShouldBe(false, "a required column on existing rows needs a database default");
        var indexes = migration.UpOperations.OfType<CreateIndexOperation>().ToList();
        indexes.Select(i => i.Name).ShouldBe(Indexes.Keys, ignoreOrder: true);
        indexes.ShouldAllBe(i => !i.IsUnique && i.Schema == Schema);

        migration.DownOperations.ShouldAllBe(o => o is DropColumnOperation || o is DropIndexOperation);
        migration.DownOperations.OfType<DropColumnOperation>().ShouldHaveSingleItem().Name.ShouldBe("FromCombination");
        migration.DownOperations.OfType<DropIndexOperation>().Select(i => i.Name).ShouldBe(Indexes.Keys, ignoreOrder: true);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("tenant-a")]
    public async Task The_column_and_the_indexes_are_in_place(string target)
    {
        var database = target == "host" ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase;
        (await ChargedBasesMigrationRows.ColumnAsync(database, Schema, "TripVehicles", "FromCombination")).ShouldBe("boolean:NO:false");
        foreach (var (index, (table, columns)) in Indexes)
        {
            var definition = (await IndexDefinitionAsync(database, index)).ShouldHaveSingleItem(index);
            definition.ShouldContain($"\"{table}\"");
            definition.ShouldContain(columns);
        }
    }

    [Fact]
    public async Task Stored_trip_vehicles_are_not_from_a_combination_the_previous_releases_insert_keeps_working_and_Down_keeps_the_rows()
    {
        await ChargedBasesMigrationRows.RecreateAsync(Scratch, create: true);
        try
        {
            await MigrateToAsync(PreviousMigration);
            (await ChargedBasesMigrationRows.ColumnAsync(Scratch, Schema, "TripVehicles", "FromCombination")).ShouldBeNull("not there before the migration");
            var (tenantA, tenantB) = (Guid.NewGuid(), Guid.NewGuid());
            var tripA = await InsertTripAsync(tenantA);
            var tripB = await InsertTripAsync(tenantB);
            await InsertTripVehicleAsync(tenantA, tripA, 1);
            await InsertTripVehicleAsync(tenantB, tripB, 1);

            await MigrateToAsync(Migration);

            (await FromCombinationOfAsync(tripA)).ShouldBe(["false"], "a vehicle stored before is not from a combination, both tenants");
            (await FromCombinationOfAsync(tripB)).ShouldBe(["false"]);
            foreach (var index in Indexes.Keys)
            {
                (await IndexDefinitionAsync(Scratch, index)).ShouldHaveSingleItem(index);
            }

            /* The previous release keeps writing trip vehicles without the column. */
            await InsertTripVehicleAsync(tenantA, tripA, 2);
            (await FromCombinationOfAsync(tripA)).ShouldBe(["false", "false"]);

            await MigrateToAsync(PreviousMigration);

            (await ChargedBasesMigrationRows.ColumnAsync(Scratch, Schema, "TripVehicles", "FromCombination")).ShouldBeNull("Down drops the column");
            foreach (var index in Indexes.Keys)
            {
                (await IndexDefinitionAsync(Scratch, index)).ShouldBeEmpty($"Down drops {index}");
            }

            (await ChargedBasesMigrationRows.CountAsync(Scratch, Schema, "TripVehicles")).ShouldBe(3L, "Down keeps the rows");
        }
        finally
        {
            await ChargedBasesMigrationRows.RecreateAsync(Scratch, create: false);
        }
    }
}
