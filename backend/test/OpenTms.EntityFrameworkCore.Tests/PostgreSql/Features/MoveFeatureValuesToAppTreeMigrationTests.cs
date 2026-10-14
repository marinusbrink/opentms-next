using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OpenTms.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace OpenTms.PostgreSql.Features;

/// <summary>
/// The data migration MoveFeatureValuesToAppTree (PO decision 2026-10-14, docs/designs/tenants-and-editions.md,
/// "App features") against real PostgreSQL with data: a scratch database is migrated by EF Core's own migrator to
/// the migration before it, seeded with stored tenant ("T") and edition ("E") values of the old features, then
/// migrated to the latest version. Asserts the mapping, the skip of an existing value, that the old values stay (expand
/// only, constitution rule 2: the previous release keeps reading them), the Down path, and that
/// values never cross from one tenant or edition to another. The developer databases (OpenTms, OpenTms_Demo) and the
/// shared PostgreSQL test databases are never touched: the scratch database carries this checkout's test suffix and
/// is dropped afterwards. Risk class: High (data migration of stored feature values) on a Critical boundary (a value
/// copied to the wrong provider key switches screens on for the wrong tenant).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class MoveFeatureValuesToAppTreeMigrationTests
{
    private const string PreviousMigration = "20261013223937_AddTenantEdition";
    private const string ThisMigration = "20261014093818_MoveFeatureValuesToAppTree";

    private static readonly string[] FirstRunNodes =
    [
        "Apps.MasterData.Relations.AddressTypes",
        "Apps.MasterData.Relations.RelationCategories",
        "Apps.MasterData.Transport.Products",
        "Apps.MasterData.Transport.TransportUnits",
        "Apps.MasterData.Transport.Temperatures",
        "Apps.MasterData.Fleet.VehicleTypes",
        "Apps.MasterData.Fleet.TransportCharacteristics",
        "Apps.MasterData.Reference.Countries",
        "Apps.MasterData.Reference.Currencies",
        "Apps.MasterData.Reference.GeographicalRegions",
    ];

    private static readonly string[] RelationsNodes = ["Apps.MasterData.Relations.Relations", "Apps.MasterData.Fleet.Drivers"];

    private static readonly string[] OldFeatures = ["MasterData.FirstRun", "MasterData.Relations", "Platform.Maintenance", "UI.CommonToolbar"];

    /* Same length as the shared tenant database's postfix, so the name stays within PostgreSQL's 63-char limit. */
    private static readonly string Database = PostgreSqlTestDatabases.HostDatabase + "_FeatMig";

    private static readonly string Tenant1 = Guid.NewGuid().ToString();
    private static readonly string Tenant2 = Guid.NewGuid().ToString();
    private static readonly string Edition1 = Guid.NewGuid().ToString();

    static MoveFeatureValuesToAppTreeMigrationTests()
    {
        /* Npgsql reads this switch once per process, at its first use. The application sets it in
         * OpenTmsEntityFrameworkCoreModule.PreConfigureServices; this class touches Npgsql without an ABP application,
         * so it must set it first too, or every later PostgreSQL test in the process runs without the legacy timestamp
         * behaviour (and fails writing UTC DateTimes, e.g. InstanceRegistryIntegrationTests). */
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

    private static async Task MigrateToAsync(string? target)
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

    private static Task InsertValueAsync(string name, string value, string providerName, string? providerKey) =>
        ExecuteAsync(
            "INSERT INTO \"AbpFeatureValues\" (\"Id\", \"Name\", \"Value\", \"ProviderName\", \"ProviderKey\") VALUES (@id, @name, @value, @pn, @pk)",
            ("id", Guid.NewGuid()), ("name", name), ("value", value), ("pn", providerName), ("pk", providerKey));

    private static Task InsertDefinitionAsync(string group, string name) =>
        ExecuteAsync(
            "INSERT INTO \"AbpFeatures\" (\"Id\", \"GroupName\", \"Name\", \"DisplayName\", \"DefaultValue\", \"IsVisibleToClients\", \"IsAvailableToHost\") " +
            "VALUES (@id, @group, @name, @name, 'false', true, true)",
            ("id", Guid.NewGuid()), ("group", group), ("name", name));

    /// <summary>All stored values as (name, providerName, providerKey) → value.</summary>
    private static async Task<Dictionary<(string Name, string? ProviderName, string? ProviderKey), string>> ValuesAsync()
    {
        var result = new Dictionary<(string, string?, string?), string>();
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(Database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT \"Name\", \"ProviderName\", \"ProviderKey\", \"Value\" FROM \"AbpFeatureValues\"", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var key = (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
            result.ContainsKey(key).ShouldBeFalse($"duplicate feature value {key}");
            result[key] = reader.GetString(3);
        }

        return result;
    }

    private static async Task<List<string>> DefinitionNamesAsync()
    {
        var names = new List<string>();
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(Database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT \"Name\" FROM \"AbpFeatures\"", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task SeedOldValuesAsync()
    {
        /* Tenant 1: the run-1 flag on, run 2 off, maintenance on, the dead toolbar flag on; Countries already has a
         * value of its own (set after an edition admin used the new tree), which must survive. */
        await InsertValueAsync("MasterData.FirstRun", "true", "T", Tenant1);
        await InsertValueAsync("MasterData.Relations", "false", "T", Tenant1);
        await InsertValueAsync("Platform.Maintenance", "true", "T", Tenant1);
        await InsertValueAsync("UI.CommonToolbar", "true", "T", Tenant1);
        await InsertValueAsync("Apps.MasterData.Reference.Countries", "false", "T", Tenant1);

        /* Tenant 2: only run 2. */
        await InsertValueAsync("MasterData.Relations", "true", "T", Tenant2);

        /* An edition: run 1 off, run 2 on. */
        await InsertValueAsync("MasterData.FirstRun", "false", "E", Edition1);
        await InsertValueAsync("MasterData.Relations", "true", "E", Edition1);

        /* Untouched: an unrelated feature. */
        await InsertValueAsync("Integrations.AddressLookup", "true", "T", Tenant1);

        foreach (var old in OldFeatures)
        {
            await InsertDefinitionAsync(old.Split('.')[0], old);
        }

        await InsertDefinitionAsync("Integrations", "Integrations.AddressLookup");
    }

    /// <summary>The rows as seeded by <see cref="SeedOldValuesAsync"/>, which an expand-only migration must leave as they are.</summary>
    private static Dictionary<(string Name, string? ProviderName, string? ProviderKey), string> Seeded(bool withPreExistingNode = true)
    {
        var seeded = new Dictionary<(string, string?, string?), string>
        {
            [("MasterData.FirstRun", "T", Tenant1)] = "true",
            [("MasterData.Relations", "T", Tenant1)] = "false",
            [("Platform.Maintenance", "T", Tenant1)] = "true",
            [("UI.CommonToolbar", "T", Tenant1)] = "true",
            [("MasterData.Relations", "T", Tenant2)] = "true",
            [("MasterData.FirstRun", "E", Edition1)] = "false",
            [("MasterData.Relations", "E", Edition1)] = "true",
            [("Integrations.AddressLookup", "T", Tenant1)] = "true",
        };
        if (withPreExistingNode)
        {
            seeded[("Apps.MasterData.Reference.Countries", "T", Tenant1)] = "false";
        }

        return seeded;
    }

    [Fact]
    public async Task Up_copies_every_old_value_to_its_nodes_per_provider_skips_existing_ones_and_keeps_the_old_features()
    {
        await RecreateDatabaseAsync(create: true);
        try
        {
            await MigrateToAsync(PreviousMigration);
            await SeedOldValuesAsync();

            await MigrateToAsync(null);

            var values = await ValuesAsync();

            /* Tenant 1: FirstRun → the ten list nodes, except Countries, which kept its own value. */
            foreach (var node in FirstRunNodes)
            {
                values[(node, "T", Tenant1)].ShouldBe(node == "Apps.MasterData.Reference.Countries" ? "false" : "true", node);
            }

            foreach (var node in RelationsNodes)
            {
                values[(node, "T", Tenant1)].ShouldBe("false", node);
                values[(node, "T", Tenant2)].ShouldBe("true", node);
                values[(node, "E", Edition1)].ShouldBe("true", node);
            }

            values[("Apps.Administration.Maintenance", "T", Tenant1)].ShouldBe("true");

            foreach (var node in FirstRunNodes)
            {
                values[(node, "E", Edition1)].ShouldBe("false", node);
                values.ShouldNotContainKey((node, "T", Tenant2), $"tenant 2 had no FirstRun value; {node} must not appear for it");
            }

            values.ShouldNotContainKey(("Apps.Administration.Maintenance", "T", Tenant2));
            values.ShouldNotContainKey(("Apps.Administration.Maintenance", "E", Edition1));

            /* Expand only (constitution rule 2, review B3): every seeded row is still there with its value, so the
             * previous release, which reads the old names, keeps working against the migrated schema. */
            foreach (var (key, value) in Seeded())
            {
                values[key].ShouldBe(value, key.ToString());
            }

            /* Nothing else is created: the 9 seeded rows plus the copies — 9 + 2 + 1 for tenant 1 (Countries pre-existed),
             * 2 for tenant 2, 12 for the edition. */
            values.Count.ShouldBe(Seeded().Count + (9 + 2 + 1) + 2 + 12);

            var definitions = await DefinitionNamesAsync();
            definitions.ShouldBe([.. OldFeatures, "Integrations.AddressLookup"], ignoreOrder: true);
        }
        finally
        {
            await RecreateDatabaseAsync(create: false);
        }
    }

    [Fact]
    public async Task Up_on_a_database_without_feature_values_changes_nothing()
    {
        await RecreateDatabaseAsync(create: true);
        try
        {
            /* A tenant database holds no feature values: the migration is a no-op there. */
            await MigrateToAsync(PreviousMigration);
            await MigrateToAsync(ThisMigration);
            (await ValuesAsync()).ShouldBeEmpty();
            (await DefinitionNamesAsync()).ShouldBeEmpty();
        }
        finally
        {
            await RecreateDatabaseAsync(create: false);
        }
    }

    [Fact]
    public async Task Down_removes_the_copies_and_leaves_the_old_values_exactly_as_they_were()
    {
        await RecreateDatabaseAsync(create: true);
        try
        {
            await MigrateToAsync(PreviousMigration);
            await SeedOldValuesAsync();
            await ExecuteAsync("DELETE FROM \"AbpFeatureValues\" WHERE \"Name\" = 'Apps.MasterData.Reference.Countries'");
            await MigrateToAsync(ThisMigration);
            (await ValuesAsync()).Count.ShouldBeGreaterThan(Seeded(withPreExistingNode: false).Count);

            await MigrateToAsync(PreviousMigration);

            (await ValuesAsync()).ShouldBe(Seeded(withPreExistingNode: false), ignoreOrder: true);
            (await DefinitionNamesAsync()).ShouldBe([.. OldFeatures, "Integrations.AddressLookup"], ignoreOrder: true);
        }
        finally
        {
            await RecreateDatabaseAsync(create: false);
        }
    }

    [Fact]
    public async Task Down_keeps_a_node_value_that_existed_before_up_because_it_is_not_a_copy()
    {
        /* Review B3: "Down deletes only the copies". A node value that was already stored when Up ran (Up skipped it)
         * is not a copy; going back must not take it away. */
        await RecreateDatabaseAsync(create: true);
        try
        {
            await MigrateToAsync(PreviousMigration);
            await SeedOldValuesAsync();
            await MigrateToAsync(ThisMigration);

            await MigrateToAsync(PreviousMigration);

            var values = await ValuesAsync();
            values.ShouldContainKey(("Apps.MasterData.Reference.Countries", "T", Tenant1), "the pre-existing node value is not a copy");
            values.ShouldBe(Seeded(), ignoreOrder: true);
        }
        finally
        {
            await RecreateDatabaseAsync(create: false);
        }
    }
}
