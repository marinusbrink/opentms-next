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
using OpenTms.PlanningExecution.TransportTrips;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace OpenTms.PostgreSql.PlanningExecution;

/// <summary>
/// The PlanningTripTypeDefaults migration (design 61 "Trip type defaults" point 1: a trip type's defaults for a new trip)
/// on the databases the real DbMigrator created (applied right after PlanningViaDepotStopWaypointType, the newest Planning
/// migration, nothing pending, the snapshot current, the twelve columns in place on the host and a tenant database, every
/// stored type readable with empty lists and the enum defaults, the id lists round-tripping in order through jsonb) and on
/// a scratch database with data: trip types the previous release stored (two tenants in one database) get empty lists,
/// MergeByLocationAndActivity (1) and FirstLastStopRule.None (0), the previous release's INSERT without the columns keeps
/// working (expand only, constitution rule 2), and Down drops exactly the twelve columns and keeps the types. Risk class:
/// Critical (a migration on every tenant database; the columns feed the server fill of every new trip).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PlanningTripTypeDefaultsMigrationTests : PlanningPostgreSqlTestBase
{
    public const string Migration = "20261029101212_PlanningTripTypeDefaults";
    private const string PreviousMigration = PlanningViaDepotStopWaypointTypeMigrationTests.Migration;
    private const string Table = "TransportTripTypes";

    private static readonly Dictionary<string, string> Columns = new()
    {
        ["DefaultCarrierRelationId"] = "uuid:YES:-",
        ["DefaultCarrierContactPersonId"] = "uuid:YES:-",
        ["DefaultVehicleCombinationId"] = "uuid:YES:-",
        ["DefaultVehicleIds"] = "jsonb:NO:'[]'::jsonb",
        ["DefaultDriverIds"] = "jsonb:NO:'[]'::jsonb",
        ["DefaultDepartureRelationId"] = "uuid:YES:-",
        ["DefaultDepartureAddressId"] = "uuid:YES:-",
        ["DefaultDepartureTime"] = "time without time zone:YES:-",
        ["DefaultArrivalRelationId"] = "uuid:YES:-",
        ["DefaultArrivalAddressId"] = "uuid:YES:-",
        ["DefaultStopCompositionMode"] = "integer:NO:1",
        ["DefaultFirstLastStopRule"] = "integer:NO:0"
    };

    /* Same length as the shared tenant database's postfix, so the name stays within PostgreSQL's 63-char limit. */
    private static readonly string Scratch = PostgreSqlTestDatabases.HostDatabase + "_PlnTtDf";

    static PlanningTripTypeDefaultsMigrationTests()
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

    private sealed record StoredDefaults(string VehicleIds, string DriverIds, int Composition, int FirstLast, Guid? Carrier, Guid? Departure, TimeOnly? DepartureTime);

    private static async Task<StoredDefaults> DefaultsOfAsync(Guid typeId)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(Scratch));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT \"DefaultVehicleIds\"::text, \"DefaultDriverIds\"::text, \"DefaultStopCompositionMode\", \"DefaultFirstLastStopRule\", \"DefaultCarrierRelationId\", \"DefaultDepartureRelationId\", \"DefaultDepartureTime\" " +
            $"FROM {PlanningExecutionDbProperties.DbSchema}.\"{Table}\" WHERE \"Id\" = @id", connection);
        command.Parameters.AddWithValue("id", typeId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        return new StoredDefaults(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.IsDBNull(5) ? null : reader.GetGuid(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<TimeOnly>(6));
    }

    /* A type as the previous release wrote it: the required columns only (TenantId names the tenant, IsDefault false keeps the one-default index quiet). */
    private static Task<Guid> InsertTypeAsync(Guid tenantId)
        => ChargedBasesMigrationRows.InsertAsync(Scratch, PlanningExecutionDbProperties.DbSchema, Table, new Dictionary<string, object> { ["TenantId"] = tenantId });

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_migration_follows_the_previous_one_is_the_newest_and_nothing_is_pending(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var context = await GetRequiredService<IDbContextProvider<PlanningExecutionDbContext>>().GetDbContextAsync();
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.IndexOf(Migration).ShouldBe(applied.IndexOf(PreviousMigration) + 1);
            applied.Last().ShouldBe(Migration);
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            context.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot must describe the model that runs");
            context.Database.GetDbConnection().Database.ShouldBe(tenant == null ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase);
        });
    }

    [Fact]
    public void The_migration_is_expand_only_and_its_Down_drops_exactly_its_columns()
    {
        var migration = new PlanningTripTypeDefaults();

        migration.UpOperations.ShouldAllBe(o => o is AddColumnOperation);
        var added = migration.UpOperations.Cast<AddColumnOperation>().ToList();
        added.Select(c => c.Name).ShouldBe(Columns.Keys, ignoreOrder: true);
        added.ShouldAllBe(c => c.Table == Table && c.Schema == PlanningExecutionDbProperties.DbSchema);
        added.Where(c => !c.IsNullable).ShouldAllBe(c => c.DefaultValue != null || c.DefaultValueSql != null, "a required column on existing rows needs a database default");
        added.Single(c => c.Name == "DefaultVehicleIds").DefaultValue.ShouldBe("[]");
        added.Single(c => c.Name == "DefaultDriverIds").DefaultValue.ShouldBe("[]");
        added.Single(c => c.Name == "DefaultStopCompositionMode").DefaultValue.ShouldBe((int)StopCompositionMode.MergeByLocationAndActivity);
        added.Single(c => c.Name == "DefaultFirstLastStopRule").DefaultValue.ShouldBe((int)FirstLastStopRule.None);

        migration.DownOperations.ShouldAllBe(o => o is DropColumnOperation);
        var dropped = migration.DownOperations.Cast<DropColumnOperation>().ToList();
        dropped.Select(c => c.Name).ShouldBe(Columns.Keys, ignoreOrder: true);
        dropped.ShouldAllBe(c => c.Table == Table && c.Schema == PlanningExecutionDbProperties.DbSchema);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("tenant-a")]
    public async Task The_columns_are_in_place_with_their_types_and_defaults(string target)
    {
        var database = target == "host" ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase;
        foreach (var (column, expected) in Columns)
        {
            (await ChargedBasesMigrationRows.ColumnAsync(database, PlanningExecutionDbProperties.DbSchema, Table, column)).ShouldBe(expected, column);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Every_stored_type_reads_with_empty_lists_and_the_enum_defaults_and_the_id_lists_keep_their_order(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;
        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            var types = GetRequiredService<IRepository<TransportTripType, Guid>>();
            var stored = await types.GetListAsync();
            stored.ShouldNotBeEmpty("the DbMigrator seeded the default type");
            stored.ShouldAllBe(t => t.TenantId == tenantId);
            foreach (var type in stored)
            {
                type.DefaultVehicleIds.ShouldNotBeNull();
                type.DefaultDriverIds.ShouldNotBeNull();
                Enum.IsDefined(type.DefaultStopCompositionMode).ShouldBeTrue();
                Enum.IsDefined(type.DefaultFirstLastStopRule).ShouldBeTrue();
            }

            var seeded = stored.Single(t => t.Code == "STD");
            seeded.DefaultVehicleIds.ShouldBeEmpty();
            seeded.DefaultDriverIds.ShouldBeEmpty();
            seeded.DefaultCarrierRelationId.ShouldBeNull();
            seeded.DefaultStopCompositionMode.ShouldBe(StopCompositionMode.MergeByLocationAndActivity);
            seeded.DefaultFirstLastStopRule.ShouldBe(FirstLastStopRule.None);

            /* The new release writes the lists; jsonb keeps their order. */
            var (v1, v2, d1, d2, d3) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            seeded.SetDefaultVehicles(null, [v2, v1]);
            seeded.SetDefaultDrivers([d3, d1, d2]);
            seeded.SetDefaultDeparture(v1, null, new TimeOnly(5, 30));
            seeded.SetDefaultComposition(StopCompositionMode.OneStopPairPerLeg, FirstLastStopRule.Both);
            await types.UpdateAsync(seeded, autoSave: true);
            var context = await GetRequiredService<IDbContextProvider<PlanningExecutionDbContext>>().GetDbContextAsync();
            context.ChangeTracker.Clear();

            var reread = await types.GetAsync(seeded.Id);
            reread.DefaultVehicleIds.ShouldBe([v2, v1]);
            reread.DefaultDriverIds.ShouldBe([d3, d1, d2]);
            reread.DefaultDepartureTime.ShouldBe(new TimeOnly(5, 30));
            reread.DefaultStopCompositionMode.ShouldBe(StopCompositionMode.OneStopPairPerLeg);
            reread.DefaultFirstLastStopRule.ShouldBe(FirstLastStopRule.Both);
        });
    }

    [Fact]
    public async Task Stored_types_get_empty_lists_and_the_enum_defaults_the_previous_releases_insert_keeps_working_and_Down_keeps_the_types()
    {
        await ChargedBasesMigrationRows.RecreateAsync(Scratch, create: true);
        try
        {
            await MigrateToAsync(PreviousMigration);
            (await ChargedBasesMigrationRows.ColumnAsync(Scratch, PlanningExecutionDbProperties.DbSchema, Table, "DefaultVehicleIds")).ShouldBeNull("not there before the migration");
            var (tenantA, tenantB) = (Guid.NewGuid(), Guid.NewGuid());
            var storedA = await InsertTypeAsync(tenantA);
            var storedB = await InsertTypeAsync(tenantB);

            await MigrateToAsync(Migration);

            foreach (var stored in new[] { storedA, storedB })
            {
                (await DefaultsOfAsync(stored)).ShouldBe(new StoredDefaults("[]", "[]", 1, 0, null, null, null), "a type stored before has no defaults, both tenants");
            }

            /* The previous release keeps writing types without the columns. */
            var oldWrite = await InsertTypeAsync(tenantA);
            (await DefaultsOfAsync(oldWrite)).ShouldBe(new StoredDefaults("[]", "[]", 1, 0, null, null, null));

            await MigrateToAsync(PreviousMigration);

            foreach (var column in Columns.Keys)
            {
                (await ChargedBasesMigrationRows.ColumnAsync(Scratch, PlanningExecutionDbProperties.DbSchema, Table, column)).ShouldBeNull($"Down drops {column}");
            }

            (await ChargedBasesMigrationRows.CountAsync(Scratch, PlanningExecutionDbProperties.DbSchema, Table)).ShouldBe(3L, "Down keeps the types");
        }
        finally
        {
            await ChargedBasesMigrationRows.RecreateAsync(Scratch, create: false);
        }
    }
}
