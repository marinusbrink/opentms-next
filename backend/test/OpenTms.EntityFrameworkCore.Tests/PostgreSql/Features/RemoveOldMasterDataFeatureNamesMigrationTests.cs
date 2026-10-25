using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OpenTms.EntityFrameworkCore;
using OpenTms.Migrations;
using Shouldly;
using Xunit;

namespace OpenTms.PostgreSql.Features;

/// <summary>
/// The contract migration RemoveOldMasterDataFeatureNames (constitution rule 2, docs/designs/tenants-and-editions.md,
/// "Migration for D15", PO 2026-10-16) against real PostgreSQL with data: a scratch database is migrated to the
/// migration before the expand step (RemoveOldFeatureNames), seeded with stored values and definition rows of the 19
/// old master-data names, migrated to the expand step (MoveMasterDataFeaturesUnderGeneral), then to the contract step.
/// Asserts that the old values and definitions are gone, that the General copies and every other value of every tenant
/// and edition stay exactly as they were (same id, same value), that the group still holding features is kept, that
/// the list of removed names is exactly the old side of the expand step's mapping, that a second run of Up changes
/// nothing, and that the migration is the newest of the application context. The scratch database carries this
/// checkout's test suffix and is dropped afterwards. Risk class: High (deletes stored feature values) on a Critical
/// boundary (a wrong delete switches screens off for a tenant).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class RemoveOldMasterDataFeatureNamesMigrationTests
{
    private const string BeforeExpand = "20261015080000_RemoveOldFeatureNames";
    private const string ExpandMigration = "20261016141627_MoveMasterDataFeaturesUnderGeneral";
    private const string ThisMigration = "20261016211220_RemoveOldMasterDataFeatureNames";

    private static readonly string[] OldNames =
    [
        "Apps.MasterData.Relations", "Apps.MasterData.Relations.Relations", "Apps.MasterData.Relations.AddressTypes",
        "Apps.MasterData.Relations.RelationCategories", "Apps.MasterData.Transport", "Apps.MasterData.Transport.Products",
        "Apps.MasterData.Transport.TransportUnits", "Apps.MasterData.Transport.ProductRelationCodes", "Apps.MasterData.Transport.Temperatures",
        "Apps.MasterData.Fleet", "Apps.MasterData.Fleet.Vehicles", "Apps.MasterData.Fleet.VehicleCombinations", "Apps.MasterData.Fleet.Drivers",
        "Apps.MasterData.Fleet.VehicleTypes", "Apps.MasterData.Fleet.TransportCharacteristics", "Apps.MasterData.Reference",
        "Apps.MasterData.Reference.Countries", "Apps.MasterData.Reference.Currencies", "Apps.MasterData.Reference.GeographicalRegions",
    ];

    private const string General = "Apps.MasterData.General";

    /* Same length as the shared tenant database's postfix, so the name stays within PostgreSQL's 63-char limit. */
    private static readonly string Database = PostgreSqlTestDatabases.HostDatabase + "_FeatDMD";

    private static readonly string Tenant1 = Guid.NewGuid().ToString();
    private static readonly string Tenant2 = Guid.NewGuid().ToString();
    private static readonly string Edition1 = Guid.NewGuid().ToString();

    static RemoveOldMasterDataFeatureNamesMigrationTests()
    {
        /* See MoveFeatureValuesToAppTreeMigrationTests: Npgsql reads this switch once per process. */
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    private static OpenTmsDbContext CreateContext()
    {
        OpenTmsEfCoreEntityExtensionMappings.Configure();
        var options = new DbContextOptionsBuilder<OpenTmsDbContext>()
            .UseNpgsql(PostgreSqlTestDatabases.ConnectionStringFor(Database))
            .Options;
        return new OpenTmsDbContext(options);
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

    private static async Task ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(Database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static Task InsertValueAsync(string name, string value, string? providerName, string? providerKey) =>
        ExecuteAsync(
            "INSERT INTO \"AbpFeatureValues\" (\"Id\", \"Name\", \"Value\", \"ProviderName\", \"ProviderKey\") VALUES (@id, @name, @value, @pn, @pk)",
            ("id", Guid.NewGuid()), ("name", name), ("value", value), ("pn", providerName), ("pk", providerKey));

    private static Task InsertGroupAsync(string name) =>
        ExecuteAsync(
            "INSERT INTO \"AbpFeatureGroups\" (\"Id\", \"Name\", \"DisplayName\") VALUES (@id, @name, @name)",
            ("id", Guid.NewGuid()), ("name", name));

    private static Task InsertDefinitionAsync(string group, string name) =>
        ExecuteAsync(
            "INSERT INTO \"AbpFeatures\" (\"Id\", \"GroupName\", \"Name\", \"DisplayName\", \"DefaultValue\", \"IsVisibleToClients\", \"IsAvailableToHost\") " +
            "VALUES (@id, @group, @name, @name, 'true', true, true)",
            ("id", Guid.NewGuid()), ("group", group), ("name", name));

    private static async Task<Dictionary<Guid, (string Name, string? ProviderName, string? ProviderKey, string Value)>> RowsByIdAsync()
    {
        var result = new Dictionary<Guid, (string, string?, string?, string)>();
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(Database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT \"Id\", \"Name\", \"ProviderName\", \"ProviderKey\", \"Value\" FROM \"AbpFeatureValues\"", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetGuid(0)] = (reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4));
        }

        return result;
    }

    private static async Task<List<string>> NamesAsync(string table)
    {
        var names = new List<string>();
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(Database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT \"Name\" FROM \"{table}\"", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static string NewNameOf(string oldName) => General + oldName["Apps.MasterData".Length..];

    private static async Task SeedAsync()
    {
        /* Every old name for tenant 1 and edition 1, alternating values, so a lost or swapped copy shows. */
        for (var i = 0; i < OldNames.Length; i++)
        {
            await InsertValueAsync(OldNames[i], i % 2 == 0 ? "true" : "false", "T", Tenant1);
            await InsertValueAsync(OldNames[i], i % 3 == 0 ? "false" : "true", "E", Edition1);
        }

        /* Tenant 2: one old name, and a General value it already held before the expand step (not overwritten there). */
        await InsertValueAsync("Apps.MasterData.Fleet.Vehicles", "false", "T", Tenant2);
        await InsertValueAsync("Apps.MasterData.General.Fleet.Vehicles", "true", "T", Tenant2);

        /* Other providers of an old name: removed as well, nothing reads the old name for any provider. */
        await InsertValueAsync("Apps.MasterData.Relations.Relations", "true", "C", null);
        await InsertValueAsync("Apps.MasterData.Fleet", "false", null, null);

        /* Names that are not old ones: the app itself, General, an unrelated flag, a name that only starts like an old one. */
        await InsertValueAsync("Apps.MasterData", "true", "T", Tenant1);
        await InsertValueAsync(General, "false", "T", Tenant2);
        await InsertValueAsync("Integrations.AddressLookup", "true", "T", Tenant1);
        await InsertValueAsync("Apps.Administration.Maintenance", "true", "E", Edition1);
        await InsertValueAsync("Apps.MasterData.FleetX", "true", "T", Tenant1);

        /* Definition rows: the old names and their General successors in the group Apps, which must stay. */
        await InsertGroupAsync("Apps");
        await InsertGroupAsync("Integrations");
        foreach (var old in OldNames)
        {
            await InsertDefinitionAsync("Apps", old);
            await InsertDefinitionAsync("Apps", NewNameOf(old));
        }

        await InsertDefinitionAsync("Apps", "Apps.MasterData");
        await InsertDefinitionAsync("Apps", General);
        await InsertDefinitionAsync("Integrations", "Integrations.AddressLookup");
    }

    private static async Task RunUpSqlAgainAsync()
    {
        /* The migration's own Up operations, generated by the PostgreSQL SQL generator and executed outside the
         * migrations history: what a second run (a retried deploy, a hand-run script) would do. */
        await using var context = CreateContext();
        var commands = context.GetService<IMigrationsSqlGenerator>()
            .Generate(new RemoveOldMasterDataFeatureNames().UpOperations, context.Model);
        commands.ShouldNotBeEmpty();
        foreach (var command in commands)
        {
            await ExecuteAsync(command.CommandText);
        }
    }

    private static string Sql(IEnumerable<Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation> operations)
        => string.Join("\n", operations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>().Select(o => o.Sql));

    [Fact]
    public void The_removed_names_are_exactly_the_old_side_of_the_expand_steps_mapping()
    {
        var expandPairs = Regex.Matches(Sql(new MoveMasterDataFeaturesUnderGeneral().UpOperations), @"\('(?<old>Apps\.[A-Za-z.]+)', '(?<new>Apps\.[A-Za-z.]+)'\)")
            .Select(m => m.Groups["old"].Value)
            .ToList();
        expandPairs.Count.ShouldBe(19);
        expandPairs.ShouldBe(OldNames, ignoreOrder: true);

        var contractSql = Sql(new RemoveOldMasterDataFeatureNames().UpOperations);
        var removed = Regex.Matches(contractSql, @"'(?<name>Apps\.[A-Za-z.]+)'").Select(m => m.Groups["name"].Value).Distinct().ToList();
        removed.ShouldBe(OldNames, ignoreOrder: true);
        contractSql.ShouldNotContain("AbpFeatureGroups", Case.Sensitive, "no group goes: Apps still holds every app feature");
        contractSql.ShouldNotContain(General, Case.Sensitive);
    }

    [Fact]
    public async Task Up_removes_the_old_values_and_definitions_and_keeps_the_General_values_and_everything_else()
    {
        await RecreateDatabaseAsync(create: true);
        try
        {
            await MigrateToAsync(BeforeExpand);
            await SeedAsync();
            await MigrateToAsync(ExpandMigration);
            var afterExpand = await RowsByIdAsync();
            afterExpand.Values.Count(r => r.Name.StartsWith(General + ".", StringComparison.Ordinal)).ShouldBe(19 + 19 + 1, "the expand step copied tenant 1's and edition 1's values; tenant 2 already had its own");

            await MigrateToAsync(ThisMigration);

            var rows = await RowsByIdAsync();
            rows.Values.ShouldNotContain(r => OldNames.Contains(r.Name), "no old name is left for any provider");

            /* Every other row of the expand step — the General copies of each tenant and edition, the value tenant 2
             * held before, the unrelated values — is still there with the same id and value. */
            var expected = afterExpand.Where(r => !OldNames.Contains(r.Value.Name)).ToDictionary(r => r.Key, r => r.Value);
            rows.ShouldBe(expected, ignoreOrder: true);

            for (var i = 0; i < OldNames.Length; i++)
            {
                var newName = NewNameOf(OldNames[i]);
                rows.Values.Single(r => r.Name == newName && r.ProviderName == "T" && r.ProviderKey == Tenant1).Value.ShouldBe(i % 2 == 0 ? "true" : "false", newName);
                rows.Values.Single(r => r.Name == newName && r.ProviderName == "E" && r.ProviderKey == Edition1).Value.ShouldBe(i % 3 == 0 ? "false" : "true", newName);
            }

            rows.Values.Single(r => r.Name == "Apps.MasterData.General.Fleet.Vehicles" && r.ProviderKey == Tenant2).Value.ShouldBe("true");
            rows.Values.ShouldContain(r => r.Name == "Apps.MasterData.FleetX", "a name that only starts like an old one is not an old one");

            var definitions = await NamesAsync("AbpFeatures");
            definitions.ShouldNotContain(n => OldNames.Contains(n));
            definitions.ShouldBe(OldNames.Select(NewNameOf).Concat(["Apps.MasterData", General, "Integrations.AddressLookup"]), ignoreOrder: true);
            (await NamesAsync("AbpFeatureGroups")).ShouldBe(["Apps", "Integrations"], ignoreOrder: true);
        }
        finally
        {
            await RecreateDatabaseAsync(create: false);
        }
    }

    [Fact]
    public async Task Up_run_again_changes_nothing()
    {
        await RecreateDatabaseAsync(create: true);
        try
        {
            await MigrateToAsync(BeforeExpand);
            await SeedAsync();
            await MigrateToAsync(ThisMigration);
            var afterFirst = await RowsByIdAsync();
            var definitionsAfterFirst = await NamesAsync("AbpFeatures");

            await RunUpSqlAgainAsync();

            (await RowsByIdAsync()).ShouldBe(afterFirst, ignoreOrder: true);
            (await NamesAsync("AbpFeatures")).ShouldBe(definitionsAfterFirst, ignoreOrder: true);
        }
        finally
        {
            await RecreateDatabaseAsync(create: false);
        }
    }

    [Fact]
    public async Task Down_restores_nothing_and_up_again_gives_the_same_rows()
    {
        await RecreateDatabaseAsync(create: true);
        try
        {
            await MigrateToAsync(BeforeExpand);
            await SeedAsync();
            await MigrateToAsync(ThisMigration);
            var afterUp = await RowsByIdAsync();

            await MigrateToAsync(ExpandMigration);
            (await RowsByIdAsync()).ShouldBe(afterUp, ignoreOrder: true);

            await MigrateToAsync(ThisMigration);
            (await RowsByIdAsync()).ShouldBe(afterUp, ignoreOrder: true);
        }
        finally
        {
            await RecreateDatabaseAsync(create: false);
        }
    }

    [Fact]
    public async Task Up_and_down_on_a_database_without_feature_values_change_nothing()
    {
        await RecreateDatabaseAsync(create: true);
        try
        {
            /* A tenant database holds no feature values. */
            await MigrateToAsync(ThisMigration);
            (await RowsByIdAsync()).ShouldBeEmpty();
            await MigrateToAsync(ExpandMigration);
            (await RowsByIdAsync()).ShouldBeEmpty();
        }
        finally
        {
            await RecreateDatabaseAsync(create: false);
        }
    }

    [Fact]
    public async Task The_migration_is_applied_after_MoveMasterDataFeaturesUnderGeneral_and_nothing_is_pending()
    {
        await RecreateDatabaseAsync(create: true);
        try
        {
            await MigrateToAsync();

            await using var context = CreateContext();
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.ShouldContain(ThisMigration);
            applied.IndexOf(ThisMigration).ShouldBe(applied.IndexOf(ExpandMigration) + 1, "directly after its expand step");
            applied.Last().ShouldBe(ThisMigration, "the newest application migration");
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            context.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot must describe the model that runs");
        }
        finally
        {
            await RecreateDatabaseAsync(create: false);
        }
    }
}
