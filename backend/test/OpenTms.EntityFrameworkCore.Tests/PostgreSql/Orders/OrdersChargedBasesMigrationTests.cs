using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using OpenTms.Orders;
using OpenTms.Orders.EntityFrameworkCore;
using OpenTms.Orders.EntityFrameworkCore.Migrations;
using OpenTms.PostgreSql.ReferenceData;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace OpenTms.PostgreSql.Orders;

/// <summary>
/// The OrdersChargedBases migration (design 61 "Order page review" point 3, build note 1: the charged bases as a bit mask) on the
/// databases the real DbMigrator created (applied right after OrdersTripRequestLines, the newest Orders migration, nothing pending, the
/// snapshot current, the column in place on the host and a tenant database) and on a scratch database with data: rows the
/// previous release wrote get 0 (no basis), the previous release's INSERT without the column keeps working against the
/// migrated schema (expand only, constitution rule 2), and Down drops exactly the column and keeps the rows. Risk class:
/// Critical (a migration on every tenant database; the column feeds pricing).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class OrdersChargedBasesMigrationTests : ReferenceDataPostgreSqlTestBase
{
    public const string Migration = "20261028172256_OrdersChargedBases";
    private const string PreviousMigration = "20261026223019_OrdersTripRequestLines";

    private static readonly string[] Tables = ["TransportOrders", "TransportOrderTemplates"];

    /* Same length as the shared tenant database's postfix, so the name stays within PostgreSQL's 63-char limit. */
    private static readonly string Scratch = PostgreSqlTestDatabases.HostDatabase + "_OrdChgB";

    static OrdersChargedBasesMigrationTests()
    {
        /* See MoveFeatureValuesToAppTreeMigrationTests: Npgsql reads this switch once per process. */
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    private static OrdersDbContext CreateContext()
        => new(new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql(PostgreSqlTestDatabases.ConnectionStringFor(Scratch), b => b.MigrationsHistoryTable("__EFMigrationsHistory", OrdersDbProperties.DbSchema))
            .Options);

    private static async Task MigrateToAsync(string target)
    {
        await using var context = CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_migration_follows_the_previous_one_is_the_newest_and_nothing_is_pending(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var context = await GetRequiredService<IDbContextProvider<OrdersDbContext>>().GetDbContextAsync();
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.IndexOf(Migration).ShouldBe(applied.IndexOf(PreviousMigration) + 1);
            applied.Last().ShouldBe(Migration);
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            context.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot must describe the model that runs");
        });
    }

    [Fact]
    public void The_migration_is_expand_only_and_its_Down_drops_exactly_its_columns()
    {
        var migration = new OrdersChargedBases();

        var up = migration.UpOperations;
        up.ShouldAllBe(o => o is AddColumnOperation);
        up.OfType<AddColumnOperation>().Select(c => c.Table).ShouldBe(Tables, ignoreOrder: true);
        foreach (var column in up.OfType<AddColumnOperation>())
        {
            (column.Name, column.Schema, column.ClrType, column.IsNullable, column.DefaultValue).ShouldBe(("ChargedBases", OrdersDbProperties.DbSchema, typeof(int), false, (object)0), column.Table);
        }

        var down = migration.DownOperations;
        down.ShouldAllBe(o => o is DropColumnOperation);
        down.OfType<DropColumnOperation>().Select(c => (c.Table, c.Name)).ShouldBe(Tables.Select(t => (t, "ChargedBases")), ignoreOrder: true);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("tenant-a")]
    public async Task The_column_is_in_place_not_null_with_default_0(string target)
    {
        var database = target == "host" ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase;
        foreach (var table in Tables)
        {
            (await ChargedBasesMigrationRows.ColumnAsync(database, OrdersDbProperties.DbSchema, table, "ChargedBases")).ShouldBe("integer:NO:0", table);
        }
    }

    [Fact]
    public async Task Existing_rows_get_no_basis_the_previous_releases_insert_keeps_working_and_Down_keeps_the_rows()
    {
        await ChargedBasesMigrationRows.RecreateAsync(Scratch, create: true);
        try
        {
            await MigrateToAsync(PreviousMigration);
            var before = new Dictionary<string, Guid>();
            foreach (var table in Tables)
            {
                (await ChargedBasesMigrationRows.ColumnAsync(Scratch, OrdersDbProperties.DbSchema, table, "ChargedBases")).ShouldBeNull("not there before the migration");
                before[table] = await ChargedBasesMigrationRows.InsertAsync(Scratch, OrdersDbProperties.DbSchema, table);
            }

            await MigrateToAsync(Migration);

            foreach (var table in Tables)
            {
                (await ChargedBasesMigrationRows.ColumnValuesAsync(Scratch, OrdersDbProperties.DbSchema, table, "ChargedBases", [before[table]])).ShouldBe([0], table + ": an existing row charges no basis");

                /* The previous release keeps writing rows without the column: they get 0 as well. */
                var oldWrite = await ChargedBasesMigrationRows.InsertAsync(Scratch, OrdersDbProperties.DbSchema, table);
                (await ChargedBasesMigrationRows.ColumnValuesAsync(Scratch, OrdersDbProperties.DbSchema, table, "ChargedBases", [oldWrite])).ShouldBe([0], table);

                /* The new release writes a mask (weight and distance: bits 4 and 9). */
                var newWrite = await ChargedBasesMigrationRows.InsertAsync(Scratch, OrdersDbProperties.DbSchema, table, new Dictionary<string, object> { ["ChargedBases"] = (1 << 4) | (1 << 9) });
                (await ChargedBasesMigrationRows.ColumnValuesAsync(Scratch, OrdersDbProperties.DbSchema, table, "ChargedBases", [newWrite])).ShouldBe([(1 << 4) | (1 << 9)], table);
            }

            await MigrateToAsync(PreviousMigration);

            foreach (var table in Tables)
            {
                (await ChargedBasesMigrationRows.ColumnAsync(Scratch, OrdersDbProperties.DbSchema, table, "ChargedBases")).ShouldBeNull("Down drops the column");
                (await ChargedBasesMigrationRows.CountAsync(Scratch, OrdersDbProperties.DbSchema, table)).ShouldBe(3L, "Down keeps the rows");
            }
        }
        finally
        {
            await ChargedBasesMigrationRows.RecreateAsync(Scratch, create: false);
        }
    }
}
