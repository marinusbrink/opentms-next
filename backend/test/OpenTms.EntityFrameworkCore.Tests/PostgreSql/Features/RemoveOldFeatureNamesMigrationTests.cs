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
/// The contract migration RemoveOldFeatureNames (constitution rule 2, docs/designs/tenants-and-editions.md,
/// "Migration") against real PostgreSQL with data: a scratch database is migrated to the expand step
/// (MoveFeatureValuesToAppTree) with stored values of the old features, then to the contract step. Asserts that the
/// old values and definitions are gone, that the app-tree copies and unrelated values of every tenant and edition stay
/// exactly as they were, and that a group still holding a feature is kept. The scratch database carries this checkout's
/// test suffix and is dropped afterwards. Risk class: High (deletes stored feature values) on a Critical boundary (a
/// wrong delete switches screens off for a tenant).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class RemoveOldFeatureNamesMigrationTests
{
    private const string BeforeExpand = "20261013223937_AddTenantEdition";
    private const string ExpandMigration = "20261014093818_MoveFeatureValuesToAppTree";
    private const string ThisMigration = "20261015080000_RemoveOldFeatureNames";

    private static readonly string[] OldFeatures = ["MasterData.FirstRun", "MasterData.Relations", "Platform.Maintenance", "UI.CommonToolbar"];

    /* Same length as the shared tenant database's postfix, so the name stays within PostgreSQL's 63-char limit. */
    private static readonly string Database = PostgreSqlTestDatabases.HostDatabase + "_FeatDel";

    private static readonly string Tenant1 = Guid.NewGuid().ToString();
    private static readonly string Edition1 = Guid.NewGuid().ToString();

    static RemoveOldFeatureNamesMigrationTests()
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

    private static async Task MigrateToAsync(string target)
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

    private static Task InsertGroupAsync(string name) =>
        ExecuteAsync(
            "INSERT INTO \"AbpFeatureGroups\" (\"Id\", \"Name\", \"DisplayName\") VALUES (@id, @name, @name)",
            ("id", Guid.NewGuid()), ("name", name));

    private static Task InsertDefinitionAsync(string group, string name) =>
        ExecuteAsync(
            "INSERT INTO \"AbpFeatures\" (\"Id\", \"GroupName\", \"Name\", \"DisplayName\", \"DefaultValue\", \"IsVisibleToClients\", \"IsAvailableToHost\") " +
            "VALUES (@id, @group, @name, @name, 'false', true, true)",
            ("id", Guid.NewGuid()), ("group", group), ("name", name));

    private static async Task<Dictionary<(string Name, string? ProviderName, string? ProviderKey), string>> ValuesAsync()
    {
        var result = new Dictionary<(string, string?, string?), string>();
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(Database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT \"Name\", \"ProviderName\", \"ProviderKey\", \"Value\" FROM \"AbpFeatureValues\"", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2))] = reader.GetString(3);
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

    private static async Task SeedAsync()
    {
        await InsertValueAsync("MasterData.FirstRun", "true", "T", Tenant1);
        await InsertValueAsync("MasterData.Relations", "false", "T", Tenant1);
        await InsertValueAsync("Platform.Maintenance", "true", "T", Tenant1);
        await InsertValueAsync("UI.CommonToolbar", "true", "T", Tenant1);
        await InsertValueAsync("MasterData.Relations", "true", "E", Edition1);
        await InsertValueAsync("Integrations.AddressLookup", "true", "T", Tenant1);

        /* The old groups; "Platform" also holds a feature that is not an old one, so it must stay. */
        foreach (var group in new[] { "MasterData", "Platform", "UI", "Integrations" })
        {
            await InsertGroupAsync(group);
        }

        foreach (var old in OldFeatures)
        {
            await InsertDefinitionAsync(old.Split('.')[0], old);
        }

        await InsertDefinitionAsync("Platform", "Platform.SomethingStillDefined");
        await InsertDefinitionAsync("Integrations", "Integrations.AddressLookup");
    }

    [Fact]
    public async Task Up_removes_the_old_values_definitions_and_empty_groups_and_keeps_everything_else()
    {
        await RecreateDatabaseAsync(create: true);
        try
        {
            await MigrateToAsync(BeforeExpand);
            await SeedAsync();
            await MigrateToAsync(ExpandMigration);
            var afterExpand = await ValuesAsync();

            await MigrateToAsync(ThisMigration);

            var values = await ValuesAsync();
            foreach (var (key, _) in values)
            {
                OldFeatures.ShouldNotContain(key.Name);
            }

            /* Every other row of the expand step — the app-tree copies of each tenant and edition, the unrelated
             * value — is still there with its value. */
            var expected = new Dictionary<(string, string?, string?), string>();
            foreach (var (key, value) in afterExpand)
            {
                if (Array.IndexOf(OldFeatures, key.Name) < 0)
                {
                    expected[key] = value;
                }
            }

            expected.Count.ShouldBe(10 + 2 + 1 + 2 + 1, "ten FirstRun nodes, two Relations nodes, Maintenance and AddressLookup for tenant 1; two Relations nodes for the edition");
            values.ShouldBe(expected, ignoreOrder: true);
            values[("Apps.MasterData.Fleet.Drivers", "E", Edition1)].ShouldBe("true");
            values[("Apps.Administration.Maintenance", "T", Tenant1)].ShouldBe("true");

            (await NamesAsync("AbpFeatures")).ShouldBe(["Platform.SomethingStillDefined", "Integrations.AddressLookup"], ignoreOrder: true);
            (await NamesAsync("AbpFeatureGroups")).ShouldBe(["Platform", "Integrations"], ignoreOrder: true);
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
            (await ValuesAsync()).ShouldBeEmpty();
            await MigrateToAsync(ExpandMigration);
            (await ValuesAsync()).ShouldBeEmpty();
        }
        finally
        {
            await RecreateDatabaseAsync(create: false);
        }
    }
}
