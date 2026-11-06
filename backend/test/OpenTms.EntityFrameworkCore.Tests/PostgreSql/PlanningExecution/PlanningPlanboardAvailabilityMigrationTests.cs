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
/// The PlanningPlanboardAvailability migration (design 68 part 2: a trip's stored route and toll totals, resource
/// unavailability and working patterns) on the databases the real DbMigrator created (applied right after
/// PlanningPlanboard, nothing pending, the snapshot current, the six trip columns, the two
/// tables with their check constraints and the three indexes in place on the host and a tenant database) and on a
/// scratch database with data: a trip the previous release stored reads with no route, the previous release's INSERT
/// without the columns keeps working (expand only, constitution rule 2), and Down drops exactly the columns and the
/// tables and keeps the trips. Risk class: Critical (a migration on every tenant database).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PlanningPlanboardAvailabilityMigrationTests : PlanningPostgreSqlTestBase
{
    public const string Migration = "20261031190521_PlanningPlanboardAvailability";
    private const string PreviousMigration = PlanningPlanboardMigrationTests.Migration;
    private const string Schema = PlanningExecutionDbProperties.DbSchema;
    private const string Trips = "TransportTrips";

    private static readonly Dictionary<string, string> TripColumns = new()
    {
        ["RouteProvider"] = "character varying:YES:-",
        ["RouteSegments"] = "jsonb:YES:-",
        ["RoutedAt"] = "timestamp with time zone:YES:-",
        ["TollAmount"] = "numeric:YES:-",
        ["TollCurrencyCode"] = "character varying:YES:-",
        ["TollDistanceM"] = "integer:YES:-"
    };

    private static readonly string[] Tables = ["ResourceUnavailabilities", "ResourceWorkingPatterns"];

    private static readonly Dictionary<string, string> CheckConstraints = new()
    {
        ["CK_ResourceUnavailabilities_Recurrence"] = "ResourceUnavailabilities",
        ["CK_ResourceWorkingPatterns_Times"] = "ResourceWorkingPatterns",
        ["CK_ResourceWorkingPatterns_Weekday"] = "ResourceWorkingPatterns"
    };

    /* Same length as the shared tenant database's postfix, so the name stays within PostgreSQL's 63-char limit. */
    private static readonly string Scratch = PostgreSqlTestDatabases.HostDatabase + "_PlnAvlb";

    static PlanningPlanboardAvailabilityMigrationTests()
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

    private static Task<List<string>> TablesInAsync(string database)
        => QueryAsync(database, $"SELECT table_name::text FROM information_schema.tables WHERE table_schema = '{Schema}' AND table_name IN ('{string.Join("', '", Tables)}') ORDER BY 1");

    /* A trip as the previous release wrote it: its type first (foreign key), then the row without the route columns. */
    private static async Task<Guid> InsertTripAsync(Guid tenantId)
    {
        var typeId = await ChargedBasesMigrationRows.InsertAsync(Scratch, Schema, "TransportTripTypes", new Dictionary<string, object> { ["TenantId"] = tenantId });
        return await ChargedBasesMigrationRows.InsertAsync(Scratch, Schema, Trips, new Dictionary<string, object> { ["TenantId"] = tenantId, ["TransportTripTypeId"] = typeId });
    }

    private static async Task<string> RouteOfAsync(Guid tripId)
        => (await QueryAsync(Scratch,
            $"SELECT concat_ws('|', coalesce(\"RouteProvider\", '-'), coalesce(\"RouteSegments\"::text, '-'), coalesce(\"RoutedAt\"::text, '-'), " +
            $"coalesce(\"TollAmount\"::text, '-'), coalesce(\"TollCurrencyCode\", '-'), coalesce(\"TollDistanceM\"::text, '-')) FROM {Schema}.\"{Trips}\" WHERE \"Id\" = '{tripId}'"))
            .ShouldHaveSingleItem();

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_migration_follows_the_previous_one_and_nothing_slipped_in_after_it_and_nothing_is_pending(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var context = await GetRequiredService<IDbContextProvider<PlanningExecutionDbContext>>().GetDbContextAsync();
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.IndexOf(Migration).ShouldBe(applied.IndexOf(PreviousMigration) + 1);
            applied[applied.IndexOf(Migration) + 1].ShouldBe(AiConnectorMigrationConsts.PlanningRepairIndexes, "nothing slipped in after it; the newest one is checked by its own test");
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            context.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot must describe the model that runs");
            context.Database.GetDbConnection().Database.ShouldBe(tenant == null ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase);
        });
    }

    [Fact]
    public void The_migration_is_expand_only_and_its_Down_drops_exactly_what_it_added()
    {
        var migration = new PlanningPlanboardAvailability();

        migration.UpOperations.ShouldAllBe(o => o is AddColumnOperation || o is CreateTableOperation || o is CreateIndexOperation);
        var columns = migration.UpOperations.OfType<AddColumnOperation>().ToList();
        columns.Select(c => c.Name).ShouldBe(TripColumns.Keys, ignoreOrder: true);
        columns.ShouldAllBe(c => c.Table == Trips && c.Schema == Schema && c.IsNullable, "a new trip column is nullable: stored trips have no route yet");
        migration.UpOperations.OfType<CreateTableOperation>().Select(t => t.Name).ShouldBe(Tables, ignoreOrder: true);
        migration.UpOperations.OfType<CreateIndexOperation>().ShouldAllBe(i => Tables.Contains(i.Table), "indexes only on the new tables");

        migration.DownOperations.ShouldAllBe(o => o is DropColumnOperation || o is DropTableOperation);
        migration.DownOperations.OfType<DropColumnOperation>().Select(c => c.Name).ShouldBe(TripColumns.Keys, ignoreOrder: true);
        migration.DownOperations.OfType<DropTableOperation>().Select(t => t.Name).ShouldBe(Tables, ignoreOrder: true);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("tenant-a")]
    public async Task The_columns_tables_and_constraints_are_in_place(string target)
    {
        var database = target == "host" ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase;
        foreach (var (column, expected) in TripColumns)
        {
            (await ChargedBasesMigrationRows.ColumnAsync(database, Schema, Trips, column)).ShouldBe(expected, column);
        }

        (await TablesInAsync(database)).ShouldBe(Tables);
        foreach (var (constraint, table) in CheckConstraints)
        {
            (await QueryAsync(database, $"SELECT conrelid::regclass::text FROM pg_constraint WHERE contype = 'c' AND conname = '{constraint}' AND connamespace = '{Schema}'::regnamespace"))
                .ShouldHaveSingleItem(constraint).ShouldContain(table);
        }

        /* pg_indexes lists the primary key's index as unique too; the check is about the one unique key besides it. */
        (await QueryAsync(database, $"SELECT indexdef FROM pg_indexes WHERE schemaname = '{Schema}' AND tablename = 'ResourceWorkingPatterns' AND indexdef LIKE 'CREATE UNIQUE%' AND indexname NOT IN (SELECT conname FROM pg_constraint WHERE contype = 'p' AND connamespace = '{Schema}'::regnamespace)"))
            .ShouldHaveSingleItem("one working pattern per resource and weekday").ShouldContain("(\"TenantId\", \"ResourceKind\", \"ResourceId\", \"Weekday\")");
    }

    [Fact]
    public async Task Stored_trips_have_no_route_the_previous_releases_insert_keeps_working_and_Down_keeps_the_trips()
    {
        await ChargedBasesMigrationRows.RecreateAsync(Scratch, create: true);
        try
        {
            await MigrateToAsync(PreviousMigration);
            (await ChargedBasesMigrationRows.ColumnAsync(Scratch, Schema, Trips, "RouteSegments")).ShouldBeNull("not there before the migration");
            (await TablesInAsync(Scratch)).ShouldBeEmpty();
            var (tenantA, tenantB) = (Guid.NewGuid(), Guid.NewGuid());
            var storedA = await InsertTripAsync(tenantA);
            var storedB = await InsertTripAsync(tenantB);

            await MigrateToAsync(Migration);

            const string noRoute = "-|-|-|-|-|-";
            (await RouteOfAsync(storedA)).ShouldBe(noRoute, "a trip stored before has no route, both tenants");
            (await RouteOfAsync(storedB)).ShouldBe(noRoute);
            (await TablesInAsync(Scratch)).ShouldBe(Tables);

            /* The previous release keeps writing trips without the columns. */
            var oldWrite = await InsertTripAsync(tenantA);
            (await RouteOfAsync(oldWrite)).ShouldBe(noRoute);

            await MigrateToAsync(PreviousMigration);

            foreach (var column in TripColumns.Keys)
            {
                (await ChargedBasesMigrationRows.ColumnAsync(Scratch, Schema, Trips, column)).ShouldBeNull($"Down drops {column}");
            }

            (await TablesInAsync(Scratch)).ShouldBeEmpty("Down drops the tables");
            (await ChargedBasesMigrationRows.CountAsync(Scratch, Schema, Trips)).ShouldBe(3L, "Down keeps the trips");
        }
        finally
        {
            await ChargedBasesMigrationRows.RecreateAsync(Scratch, create: false);
        }
    }
}
