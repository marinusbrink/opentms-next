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
/// The PlanningViaDepotStopWaypointType migration (design 61 "Via-depot rules on the stop's waypoint type": the waypoint type
/// of the order's stop that triggers a rule) on the databases the real DbMigrator created (applied right after
/// PlanningLegSourceKeys and followed by PlanningTripTypeDefaults, nothing pending, the snapshot current, the column in place on the
/// host and a tenant database) and on a scratch database with data: a rule the previous release stored keeps no type (it
/// applies to any stop until it is edited), the previous release's INSERT without the column keeps working against the
/// migrated schema (expand only, constitution rule 2), and Down drops exactly the column and keeps the rules. Risk class:
/// Critical (a migration on every tenant database; the column steers leg generation).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PlanningViaDepotStopWaypointTypeMigrationTests : PlanningPostgreSqlTestBase
{
    public const string Migration = "20261029072305_PlanningViaDepotStopWaypointType";
    private const string PreviousMigration = "20261026013555_PlanningLegSourceKeys";
    private const string Table = "ViaDepotRules";
    private const string Column = "StopWaypointTypeId";

    /* Same length as the shared tenant database's postfix, so the name stays within PostgreSQL's 63-char limit. */
    private static readonly string Scratch = PostgreSqlTestDatabases.HostDatabase + "_PlnVdSt";

    static PlanningViaDepotStopWaypointTypeMigrationTests()
    {
        /* See MoveFeatureValuesToAppTreeMigrationTests: Npgsql reads this switch once per process. */
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    private static PlanningExecutionDbContext CreateContext()
        => new(new DbContextOptionsBuilder<PlanningExecutionDbContext>()
            .UseNpgsql(PostgreSqlTestDatabases.ConnectionStringFor(Scratch), b => b.MigrationsHistoryTable("__EFMigrationsHistory", PlanningExecutionDbProperties.DbSchema))
            .Options);

    private static async Task MigrateToAsync(string target)
    {
        await using var context = CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task<Guid?> StopTypeOfAsync(Guid ruleId)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(Scratch));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT \"{Column}\" FROM {PlanningExecutionDbProperties.DbSchema}.\"{Table}\" WHERE \"Id\" = @id", connection);
        command.Parameters.AddWithValue("id", ruleId);
        var value = await command.ExecuteScalarAsync();
        return value is Guid id ? id : null;
    }

    /* The check constraint wants a position of at least 1; everything else the previous release's row needs is neutral. */
    private static Task<Guid> InsertRuleAsync(IReadOnlyDictionary<string, object>? values = null)
        => ChargedBasesMigrationRows.InsertAsync(Scratch, PlanningExecutionDbProperties.DbSchema, Table, new Dictionary<string, object>(values ?? new Dictionary<string, object>()) { ["Position"] = 1 });

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_migration_follows_the_previous_one_is_followed_by_the_trip_type_defaults_and_nothing_is_pending(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var context = await GetRequiredService<IDbContextProvider<PlanningExecutionDbContext>>().GetDbContextAsync();
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.IndexOf(Migration).ShouldBe(applied.IndexOf(PreviousMigration) + 1);
            applied[applied.IndexOf(Migration) + 1].ShouldBe(PlanningTripTypeDefaultsMigrationTests.Migration,
                "nothing slipped in after it (design 61 \"Trip type defaults\" follows); the newest one is checked by its own test");
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            context.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot must describe the model that runs");
        });
    }

    [Fact]
    public void The_migration_is_expand_only_and_its_Down_drops_exactly_its_column()
    {
        var migration = new PlanningViaDepotStopWaypointType();

        var added = migration.UpOperations.ShouldHaveSingleItem().ShouldBeOfType<AddColumnOperation>();
        (added.Table, added.Name, added.Schema, added.ClrType, added.IsNullable).ShouldBe((Table, Column, PlanningExecutionDbProperties.DbSchema, typeof(Guid), true));

        var dropped = migration.DownOperations.ShouldHaveSingleItem().ShouldBeOfType<DropColumnOperation>();
        (dropped.Table, dropped.Name).ShouldBe((Table, Column));
    }

    [Theory]
    [InlineData("host")]
    [InlineData("tenant-a")]
    public async Task The_column_is_in_place_and_nullable(string target)
    {
        var database = target == "host" ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase;
        (await ChargedBasesMigrationRows.ColumnAsync(database, PlanningExecutionDbProperties.DbSchema, Table, Column)).ShouldBe("uuid:YES:-");
    }

    [Fact]
    public async Task Stored_rules_keep_no_type_the_previous_releases_insert_keeps_working_and_Down_keeps_the_rules()
    {
        await ChargedBasesMigrationRows.RecreateAsync(Scratch, create: true);
        try
        {
            await MigrateToAsync(PreviousMigration);
            (await ChargedBasesMigrationRows.ColumnAsync(Scratch, PlanningExecutionDbProperties.DbSchema, Table, Column)).ShouldBeNull("not there before the migration");
            var stored = await InsertRuleAsync();

            await MigrateToAsync(Migration);

            (await StopTypeOfAsync(stored)).ShouldBeNull("a rule stored before applies to any stop until it is edited");

            /* The previous release keeps writing rules without the column. */
            var oldWrite = await InsertRuleAsync();
            (await StopTypeOfAsync(oldWrite)).ShouldBeNull();

            /* The new release writes the stop's waypoint type. */
            var unload = Guid.NewGuid();
            var newWrite = await InsertRuleAsync(new Dictionary<string, object> { [Column] = unload });
            (await StopTypeOfAsync(newWrite)).ShouldBe(unload);

            await MigrateToAsync(PreviousMigration);

            (await ChargedBasesMigrationRows.ColumnAsync(Scratch, PlanningExecutionDbProperties.DbSchema, Table, Column)).ShouldBeNull("Down drops the column");
            (await ChargedBasesMigrationRows.CountAsync(Scratch, PlanningExecutionDbProperties.DbSchema, Table)).ShouldBe(3L, "Down keeps the rules");
        }
        finally
        {
            await ChargedBasesMigrationRows.RecreateAsync(Scratch, create: false);
        }
    }
}
