using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using OpenTms.MasterData;
using OpenTms.MasterData.EntityFrameworkCore;
using OpenTms.MasterData.EntityFrameworkCore.Migrations;
using Shouldly;
using Xunit;

namespace OpenTms.PostgreSql.MasterData;

/// <summary>
/// The migration MasterDataVehicleCombinationNormalizedName (PO 2026-10-16: combination names are unique per tenant
/// regardless of case) against real PostgreSQL with data: a scratch database is migrated by EF Core's own migrator to
/// the migration before it (MasterDataVehicleNormalizedName), seeded with combinations of two tenants and the host
/// (one soft-deleted), then migrated. Asserts the backfill (the trimmed name in upper case for every row, deleted rows
/// included), that the unique key now ignores case while a soft delete and another tenant keep their own key space,
/// that a database already holding a case-duplicate pair stops at the unique index (the case the migration's comment
/// describes), that Down restores the previous schema, and the operation order (expand only: column, backfill, then
/// the index). The scratch database carries this checkout's test suffix and is dropped afterwards. Risk class:
/// Critical (a migration on every tenant database; the index is the one enforced place for the name).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class MasterDataVehicleCombinationNormalizedNameMigrationTests
{
    private const string PreviousMigration = "20261016071559_MasterDataVehicleNormalizedName";
    private const string ThisMigration = "20261016211522_MasterDataVehicleCombinationNormalizedName";

    /* Same length as the shared tenant database's postfix, so the name stays within PostgreSQL's 63-char limit. */
    private static readonly string Database = PostgreSqlTestDatabases.HostDatabase + "_MdCombN";

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    static MasterDataVehicleCombinationNormalizedNameMigrationTests()
    {
        /* See MoveFeatureValuesToAppTreeMigrationTests: Npgsql reads this switch once per process. */
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    private static MasterDataDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MasterDataDbContext>()
            .UseNpgsql(
                PostgreSqlTestDatabases.ConnectionStringFor(Database),
                b => b.MigrationsHistoryTable("__EFMigrationsHistory", MasterDataDbProperties.DbSchema))
            .Options;
        return new MasterDataDbContext(options);
    }

    private static async Task MigrateToAsync(string? target = null)
    {
        await using var context = CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task RecreateDatabaseAsync(bool create)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor("postgres"));
        await connection.OpenAsync();
        await using (var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{Database}\" WITH (FORCE)", connection))
        {
            await drop.ExecuteNonQueryAsync();
        }

        if (create)
        {
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{Database}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        NpgsqlConnection.ClearAllPools();
    }

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(Database));
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>A combination row as the previous release stored it (no NormalizedName column yet).</summary>
    private static async Task<Guid> InsertCombinationAsync(Guid? tenant, string name, bool deleted = false)
    {
        var id = Guid.NewGuid();
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO masterdata.\"VehicleCombinations\" (\"Id\", \"TenantId\", \"Name\", \"IsArchived\", \"ExtraProperties\", \"ConcurrencyStamp\", \"CreationTime\", \"IsDeleted\") " +
            "VALUES (@id, @tenant, @name, false, '{}', 'stamp', now(), @deleted)",
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant", (object?)tenant ?? DBNull.Value);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("deleted", deleted);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task<Dictionary<Guid, string>> NormalizedNamesAsync()
    {
        var result = new Dictionary<Guid, string>();
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("SELECT \"Id\", \"NormalizedName\" FROM masterdata.\"VehicleCombinations\"", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetGuid(0)] = reader.GetString(1);
        }

        return result;
    }

    private static async Task<List<string>> IndexesAsync()
    {
        var rows = new List<string>();
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("SELECT indexname || ' :: ' || indexdef FROM pg_indexes WHERE schemaname = 'masterdata' AND tablename = 'VehicleCombinations'", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static async Task InsertWithNormalizedNameAsync(Guid? tenant, string name)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO masterdata.\"VehicleCombinations\" (\"Id\", \"TenantId\", \"Name\", \"NormalizedName\", \"IsArchived\", \"ExtraProperties\", \"ConcurrencyStamp\", \"CreationTime\", \"IsDeleted\") " +
            "VALUES (@id, @tenant, @name, upper(@name), false, '{}', 'stamp', now(), false)",
            connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("tenant", (object?)tenant ?? DBNull.Value);
        command.Parameters.AddWithValue("name", name);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public void The_migration_backfills_before_it_moves_the_unique_key_and_Down_restores_it()
    {
        var migration = new MasterDataVehicleCombinationNormalizedName();

        var up = migration.UpOperations.ToList();
        up.ShouldAllBe(o => o.GetType() == typeof(DropIndexOperation) || o.GetType() == typeof(AddColumnOperation) || o.GetType() == typeof(SqlOperation) || o.GetType() == typeof(CreateIndexOperation));
        up.OfType<DropIndexOperation>().ShouldHaveSingleItem().Name.ShouldBe("IX_VehicleCombinations_TenantId_Name");
        var add = up.OfType<AddColumnOperation>().ShouldHaveSingleItem();
        (add.Table, add.Name, add.MaxLength, add.IsNullable).ShouldBe(("VehicleCombinations", "NormalizedName", (int?)MasterDataConsts.MaxVehicleCombinationNameLength, false));
        var backfill = up.OfType<SqlOperation>().ShouldHaveSingleItem();
        backfill.Sql.ShouldContain("SET \"NormalizedName\" = upper(trim(\"Name\"))");
        backfill.Sql.ShouldNotContain("WHERE", Case.Insensitive, "every row, deleted ones included");
        up.IndexOf(backfill).ShouldBeGreaterThan(up.IndexOf(add));
        var unique = up.OfType<CreateIndexOperation>().Single(i => i.IsUnique);
        unique.Columns.ShouldBe(["TenantId", "NormalizedName"]);
        unique.Filter.ShouldBe("\"IsDeleted\" = false");
        unique["Npgsql:NullsDistinct"].ShouldBe(false);
        up.IndexOf(unique).ShouldBeGreaterThan(up.IndexOf(backfill), "the unique index is built on filled rows");
        up.OfType<CreateIndexOperation>().Single(i => !i.IsUnique).Columns.ShouldBe(["TenantId", "Name"]);

        var down = migration.DownOperations;
        down.OfType<DropColumnOperation>().ShouldHaveSingleItem().Name.ShouldBe("NormalizedName");
        down.OfType<CreateIndexOperation>().ShouldHaveSingleItem().IsUnique.ShouldBeTrue("Down restores the case-sensitive unique name");
    }

    [Fact]
    public async Task Up_fills_every_row_from_its_name_and_the_key_then_ignores_case_per_tenant()
    {
        await RecreateDatabaseAsync(create: true);
        try
        {
            await MigrateToAsync(PreviousMigration);
            var night = await InsertCombinationAsync(TenantA, "Night shift");
            var deleted = await InsertCombinationAsync(TenantA, "NIGHT SHIFT", deleted: true);
            var other = await InsertCombinationAsync(TenantB, "night shift");
            var host = await InsertCombinationAsync(null, "Ëxtra Day 1");

            await MigrateToAsync(ThisMigration);

            var keys = await NormalizedNamesAsync();
            keys.ShouldBe(new Dictionary<Guid, string>
            {
                [night] = "NIGHT SHIFT",
                [deleted] = "NIGHT SHIFT",
                [other] = "NIGHT SHIFT",
                [host] = "ËXTRA DAY 1",
            }, ignoreOrder: true);

            /* The key the entity computes is the one the migration wrote. */
            foreach (var name in new[] { "Night shift", "Ëxtra Day 1" })
            {
                OpenTms.MasterData.Fleet.VehicleCombination.NormalizedNameOf(name).ShouldBe(name.ToUpperInvariant());
            }

            var indexes = await IndexesAsync();
            var unique = indexes.Single(i => i.StartsWith("IX_VehicleCombinations_TenantId_NormalizedName :: ", StringComparison.Ordinal));
            unique.ShouldContain("UNIQUE");
            unique.ShouldContain("NULLS NOT DISTINCT");
            unique.ShouldContain("\"IsDeleted\" = false");
            indexes.Single(i => i.StartsWith("IX_VehicleCombinations_TenantId_Name :: ", StringComparison.Ordinal)).ShouldNotContain("UNIQUE");

            /* A different case in the same tenant is refused; another tenant and the host have their own key space. */
            var ex = await Should.ThrowAsync<PostgresException>(() => InsertWithNormalizedNameAsync(TenantA, "night SHIFT"));
            ex.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
            ex.ConstraintName.ShouldBe("IX_VehicleCombinations_TenantId_NormalizedName");
            await InsertWithNormalizedNameAsync(TenantB, "Ëxtra Day 1");
            var hostDuplicate = await Should.ThrowAsync<PostgresException>(() => InsertWithNormalizedNameAsync(null, "ëxtra day 1"));
            hostDuplicate.ConstraintName.ShouldBe("IX_VehicleCombinations_TenantId_NormalizedName", "NULLS NOT DISTINCT: host rows are unique too");
        }
        finally
        {
            await RecreateDatabaseAsync(create: false);
        }
    }

    [Fact]
    public async Task A_database_already_holding_a_case_duplicate_pair_stops_at_the_unique_index_and_stays_on_the_previous_migration()
    {
        /* The case the migration's comment names: possible only in a development database (no release shipped
         * combinations); the DbMigrator reports it, the data is not changed behind the user's back. */
        await RecreateDatabaseAsync(create: true);
        try
        {
            await MigrateToAsync(PreviousMigration);
            await InsertCombinationAsync(TenantA, "Night shift");
            await InsertCombinationAsync(TenantA, "NIGHT SHIFT");

            var ex = await Should.ThrowAsync<PostgresException>(() => MigrateToAsync(ThisMigration));
            ex.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
            ex.Message.ShouldContain("IX_VehicleCombinations_TenantId_NormalizedName");

            await using var context = CreateContext();
            (await context.Database.GetAppliedMigrationsAsync()).Last().ShouldBe(PreviousMigration, "the migration runs in one transaction and is not recorded");
        }
        finally
        {
            await RecreateDatabaseAsync(create: false);
        }
    }

    [Fact]
    public async Task Down_drops_the_column_and_restores_the_case_sensitive_unique_name()
    {
        await RecreateDatabaseAsync(create: true);
        try
        {
            await MigrateToAsync(PreviousMigration);
            var night = await InsertCombinationAsync(TenantA, "Night shift");
            await MigrateToAsync(ThisMigration);

            await MigrateToAsync(PreviousMigration);

            var indexes = await IndexesAsync();
            indexes.ShouldNotContain(i => i.Contains("NormalizedName"));
            var name = indexes.Single(i => i.StartsWith("IX_VehicleCombinations_TenantId_Name :: ", StringComparison.Ordinal));
            name.ShouldContain("UNIQUE");
            name.ShouldContain("NULLS NOT DISTINCT");

            await using var connection = await OpenAsync();
            await using var command = new NpgsqlCommand("SELECT count(*) FROM information_schema.columns WHERE table_schema = 'masterdata' AND table_name = 'VehicleCombinations' AND column_name = 'NormalizedName'", connection);
            ((long)(await command.ExecuteScalarAsync())!).ShouldBe(0);
            await using var row = new NpgsqlCommand($"SELECT \"Name\" FROM masterdata.\"VehicleCombinations\" WHERE \"Id\" = '{night}'", connection);
            ((string)(await row.ExecuteScalarAsync())!).ShouldBe("Night shift");
        }
        finally
        {
            await RecreateDatabaseAsync(create: false);
        }
    }
}
