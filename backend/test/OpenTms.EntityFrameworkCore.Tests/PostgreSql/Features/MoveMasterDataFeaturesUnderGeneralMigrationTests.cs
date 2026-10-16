using System;
using System.Collections.Generic;
using System.Linq;
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
/// The data migration MoveMasterDataFeaturesUnderGeneral (PO decision 2026-10-16, docs/designs/tenants-and-editions.md
/// D15, "Migration for D15") against real PostgreSQL with data: a scratch database is migrated by EF Core's own
/// migrator to the migration before it (RemoveOldFeatureNames), seeded with stored tenant ("T") and edition ("E")
/// values of the 19 old master-data names plus values that must not move (other providers, unrelated names, a new-name
/// value that already exists), then migrated to this migration. Asserts the copy per provider and key (never across
/// tenants or editions), the skip of an existing new-name value, that other providers are ignored, that the old rows
/// stay (expand only, constitution rule 2), that a second run of Up changes nothing, that Down removes exactly the
/// copies, and that only its contract step (RemoveOldMasterDataFeatureNames) follows it, with nothing pending. The scratch database
/// carries this checkout's test suffix and is dropped afterwards. Risk class: Critical (Platform change; a value
/// copied to the wrong provider key switches screens on for the wrong tenant, a lost value switches them off).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class MoveMasterDataFeaturesUnderGeneralMigrationTests
{
    private const string PreviousMigration = "20261015080000_RemoveOldFeatureNames";
    private const string ThisMigration = "20261016141627_MoveMasterDataFeaturesUnderGeneral";
    private const string ContractMigration = "20261016211220_RemoveOldMasterDataFeatureNames";

    /// <summary>The 19 renames of D15: old name → new name.</summary>
    private static readonly (string Old, string New)[] Renames =
    [
        ("Apps.MasterData.Relations", "Apps.MasterData.General.Relations"),
        ("Apps.MasterData.Relations.Relations", "Apps.MasterData.General.Relations.Relations"),
        ("Apps.MasterData.Relations.AddressTypes", "Apps.MasterData.General.Relations.AddressTypes"),
        ("Apps.MasterData.Relations.RelationCategories", "Apps.MasterData.General.Relations.RelationCategories"),
        ("Apps.MasterData.Transport", "Apps.MasterData.General.Transport"),
        ("Apps.MasterData.Transport.Products", "Apps.MasterData.General.Transport.Products"),
        ("Apps.MasterData.Transport.TransportUnits", "Apps.MasterData.General.Transport.TransportUnits"),
        ("Apps.MasterData.Transport.ProductRelationCodes", "Apps.MasterData.General.Transport.ProductRelationCodes"),
        ("Apps.MasterData.Transport.Temperatures", "Apps.MasterData.General.Transport.Temperatures"),
        ("Apps.MasterData.Fleet", "Apps.MasterData.General.Fleet"),
        ("Apps.MasterData.Fleet.Vehicles", "Apps.MasterData.General.Fleet.Vehicles"),
        ("Apps.MasterData.Fleet.VehicleCombinations", "Apps.MasterData.General.Fleet.VehicleCombinations"),
        ("Apps.MasterData.Fleet.Drivers", "Apps.MasterData.General.Fleet.Drivers"),
        ("Apps.MasterData.Fleet.VehicleTypes", "Apps.MasterData.General.Fleet.VehicleTypes"),
        ("Apps.MasterData.Fleet.TransportCharacteristics", "Apps.MasterData.General.Fleet.TransportCharacteristics"),
        ("Apps.MasterData.Reference", "Apps.MasterData.General.Reference"),
        ("Apps.MasterData.Reference.Countries", "Apps.MasterData.General.Reference.Countries"),
        ("Apps.MasterData.Reference.Currencies", "Apps.MasterData.General.Reference.Currencies"),
        ("Apps.MasterData.Reference.GeographicalRegions", "Apps.MasterData.General.Reference.GeographicalRegions"),
    ];

    /* Same length as the shared tenant database's postfix, so the name stays within PostgreSQL's 63-char limit. */
    private static readonly string Database = PostgreSqlTestDatabases.HostDatabase + "_FeatGen";

    private static readonly string Tenant1 = Guid.NewGuid().ToString();
    private static readonly string Tenant2 = Guid.NewGuid().ToString();
    private static readonly string Edition1 = Guid.NewGuid().ToString();
    private static readonly string Edition2 = Guid.NewGuid().ToString();

    /// <summary>The new-name value tenant 2 already holds before Up runs; Up must not overwrite it.</summary>
    private const string PreExistingName = "Apps.MasterData.General.Fleet.Vehicles";

    private const string AppFeature = "Apps.MasterData";
    private const string GeneralFeature = "Apps.MasterData.General";

    static MoveMasterDataFeaturesUnderGeneralMigrationTests()
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
            result.ContainsKey(key).ShouldBeFalse($"duplicate row {key}");
            result[key] = reader.GetString(3);
        }

        return result;
    }

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

    /// <summary>A value per tenant/edition that differs per old name, so a copy to the wrong name shows.</summary>
    private static string ValueFor(int index, string owner) => (index + owner.Length) % 2 == 0 ? "true" : "false";

    /// <summary>The rows seeded before Up; <paramref name="withPreExisting"/> adds tenant 2's existing new-name value.</summary>
    private static Dictionary<(string Name, string? ProviderName, string? ProviderKey), string> Seeded(bool withPreExisting = true)
    {
        var rows = new Dictionary<(string, string?, string?), string>();

        /* Tenant 1 and edition 1: every old name. */
        for (var i = 0; i < Renames.Length; i++)
        {
            rows[(Renames[i].Old, "T", Tenant1)] = ValueFor(i, "tenant1");
            rows[(Renames[i].Old, "E", Edition1)] = ValueFor(i + 1, "edition1");
        }

        /* Tenant 2 and edition 2: a few, with the opposite values of tenant 1 / edition 1. */
        rows[("Apps.MasterData.Fleet.Vehicles", "T", Tenant2)] = "false";
        rows[("Apps.MasterData.Reference.Countries", "T", Tenant2)] = "true";
        rows[("Apps.MasterData.Transport", "E", Edition2)] = "false";

        /* Other providers: the configuration/default providers and a made-up one. */
        rows[("Apps.MasterData.Relations.Relations", "C", null)] = "true";
        rows[("Apps.MasterData.Fleet.Drivers", "D", null)] = "true";
        rows[("Apps.MasterData.Reference.Currencies", "U", Tenant1)] = "true";
        rows[("Apps.MasterData.Fleet", null, null)] = "false";

        /* Names that are not renamed: the app itself, the address-lookup flag, an Administration node. */
        rows[(AppFeature, "T", Tenant1)] = "true";
        rows[("Integrations.AddressLookup", "T", Tenant1)] = "true";
        rows[("Apps.Administration.Maintenance", "E", Edition1)] = "true";

        if (withPreExisting)
        {
            /* Tenant 2 already holds a value under the new name, opposite to its old one. */
            rows[(PreExistingName, "T", Tenant2)] = "true";
        }

        return rows;
    }

    private static async Task SeedAsync(bool withPreExisting = true)
    {
        foreach (var ((name, providerName, providerKey), value) in Seeded(withPreExisting))
        {
            await InsertValueAsync(name, value, providerName, providerKey);
        }
    }

    /// <summary>What Up should produce: the seed plus a copy per T/E row of an old name, unless the new name is taken.</summary>
    private static Dictionary<(string Name, string? ProviderName, string? ProviderKey), string> ExpectedAfterUp(bool withPreExisting = true)
    {
        var seeded = Seeded(withPreExisting);
        var expected = new Dictionary<(string, string?, string?), string>(seeded);
        foreach (var ((name, providerName, providerKey), value) in seeded)
        {
            if (providerName is not ("T" or "E"))
            {
                continue;
            }

            foreach (var (oldName, newName) in Renames)
            {
                if (oldName == name && !seeded.ContainsKey((newName, providerName, providerKey)))
                {
                    expected[(newName, providerName, providerKey)] = value;
                }
            }
        }

        return expected;
    }

    private static async Task RunUpSqlAgainAsync()
    {
        /* The migration's own Up operations, generated by the PostgreSQL SQL generator and executed outside the
         * migrations history: what a second run (a retried deploy, a hand-run script) would do. */
        await using var context = CreateContext();
        var commands = context.GetService<IMigrationsSqlGenerator>()
            .Generate(new MoveMasterDataFeaturesUnderGeneral().UpOperations, context.Model);
        commands.ShouldNotBeEmpty();
        foreach (var command in commands)
        {
            await ExecuteAsync(command.CommandText);
        }
    }

    [Fact]
    public void The_mapping_is_exactly_the_19_renames_of_D15()
    {
        Renames.Length.ShouldBe(19);
        Renames.Select(r => r.Old).Distinct().Count().ShouldBe(19);
        foreach (var (oldName, newName) in Renames)
        {
            oldName.ShouldStartWith(AppFeature + ".");
            newName.ShouldBe(GeneralFeature + oldName[AppFeature.Length..]);
        }
    }

    [Fact]
    public async Task Up_copies_every_tenant_and_edition_value_to_its_new_name_and_keeps_everything_else()
    {
        await RecreateDatabaseAsync(create: true);
        try
        {
            await MigrateToAsync(PreviousMigration);
            await SeedAsync();

            await MigrateToAsync(ThisMigration);

            var values = await ValuesAsync();
            values.ShouldBe(ExpectedAfterUp(), ignoreOrder: true);

            /* Every old name of tenant 1 and edition 1 has its copy, with the value of its own provider key. */
            for (var i = 0; i < Renames.Length; i++)
            {
                values[(Renames[i].New, "T", Tenant1)].ShouldBe(ValueFor(i, "tenant1"), Renames[i].New);
                values[(Renames[i].New, "E", Edition1)].ShouldBe(ValueFor(i + 1, "edition1"), Renames[i].New);
            }

            /* 19 for tenant 1, 19 for edition 1, Countries for tenant 2, Transport for edition 2; Vehicles of tenant 2
             * was already there. */
            (values.Count - Seeded().Count).ShouldBe(19 + 19 + 1 + 1);

            /* Never across tenants or editions. */
            values[("Apps.MasterData.General.Reference.Countries", "T", Tenant2)].ShouldBe("true");
            values.ShouldNotContainKey(("Apps.MasterData.General.Reference.Currencies", "T", Tenant2));
            values[("Apps.MasterData.General.Transport", "E", Edition2)].ShouldBe("false");
            values.ShouldNotContainKey(("Apps.MasterData.General.Fleet", "E", Edition2));
        }
        finally
        {
            await RecreateDatabaseAsync(create: false);
        }
    }

    [Fact]
    public async Task Up_never_overwrites_an_existing_new_name_value()
    {
        await RecreateDatabaseAsync(create: true);
        try
        {
            await MigrateToAsync(PreviousMigration);
            await SeedAsync();
            var preExistingId = (await RowsByIdAsync()).Single(r => r.Value.Name == PreExistingName && r.Value.ProviderKey == Tenant2).Key;

            await MigrateToAsync(ThisMigration);

            var rows = await RowsByIdAsync();
            rows.Count(r => r.Value.Name == PreExistingName && r.Value.ProviderName == "T" && r.Value.ProviderKey == Tenant2).ShouldBe(1);
            rows[preExistingId].ShouldBe((PreExistingName, "T", Tenant2, "true"), "the old value (false) must not replace the stored new-name value");
        }
        finally
        {
            await RecreateDatabaseAsync(create: false);
        }
    }

    [Fact]
    public async Task Up_ignores_other_providers_and_unrelated_names_and_stores_nothing_for_General_itself()
    {
        await RecreateDatabaseAsync(create: true);
        try
        {
            await MigrateToAsync(PreviousMigration);
            await SeedAsync();

            await MigrateToAsync(ThisMigration);

            var values = await ValuesAsync();
            values.Keys.Where(k => k.Name.StartsWith(GeneralFeature, StringComparison.Ordinal))
                .ShouldAllBe(k => k.ProviderName == "T" || k.ProviderName == "E");
            values.ShouldNotContainKey(("Apps.MasterData.General.Relations.Relations", "C", null));
            values.ShouldNotContainKey(("Apps.MasterData.General.Fleet.Drivers", "D", null));
            values.ShouldNotContainKey(("Apps.MasterData.General.Reference.Currencies", "U", Tenant1));
            values.ShouldNotContainKey(("Apps.MasterData.General.Fleet", null, null));
            values.Keys.ShouldNotContain(k => k.Name == GeneralFeature, "General's default is on; it gets no stored value");
            values.Keys.ShouldNotContain(k => k.Name.StartsWith("Apps.MasterData.General.Integrations") || k.Name.StartsWith("Apps.MasterData.General.Administration"));
        }
        finally
        {
            await RecreateDatabaseAsync(create: false);
        }
    }

    [Fact]
    public async Task Up_leaves_every_old_row_in_place_with_its_id_and_value()
    {
        /* Expand only (constitution rule 2): the previous release, running next to this one, still reads the old names. */
        await RecreateDatabaseAsync(create: true);
        try
        {
            await MigrateToAsync(PreviousMigration);
            await SeedAsync();
            var before = await RowsByIdAsync();

            await MigrateToAsync(ThisMigration);

            var after = await RowsByIdAsync();
            foreach (var (id, row) in before)
            {
                after.ShouldContainKey(id);
                after[id].ShouldBe(row);
            }
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
            await MigrateToAsync(PreviousMigration);
            await SeedAsync();
            await MigrateToAsync(ThisMigration);
            var afterFirst = await RowsByIdAsync();

            await RunUpSqlAgainAsync();

            (await RowsByIdAsync()).ShouldBe(afterFirst, ignoreOrder: true);
        }
        finally
        {
            await RecreateDatabaseAsync(create: false);
        }
    }

    [Fact]
    public async Task Up_down_up_gives_the_same_rows_with_the_same_ids()
    {
        await RecreateDatabaseAsync(create: true);
        try
        {
            await MigrateToAsync(PreviousMigration);
            await SeedAsync();
            await MigrateToAsync(ThisMigration);
            var afterFirst = await RowsByIdAsync();

            await MigrateToAsync(PreviousMigration);
            await MigrateToAsync(ThisMigration);

            (await RowsByIdAsync()).ShouldBe(afterFirst, ignoreOrder: true);
        }
        finally
        {
            await RecreateDatabaseAsync(create: false);
        }
    }

    [Fact]
    public async Task Down_removes_exactly_the_copies_and_keeps_a_new_name_value_that_existed_before()
    {
        await RecreateDatabaseAsync(create: true);
        try
        {
            await MigrateToAsync(PreviousMigration);
            await SeedAsync();
            var before = await RowsByIdAsync();
            await MigrateToAsync(ThisMigration);
            (await RowsByIdAsync()).Count.ShouldBeGreaterThan(before.Count);

            await MigrateToAsync(PreviousMigration);

            (await RowsByIdAsync()).ShouldBe(before, ignoreOrder: true);
            (await ValuesAsync()).ShouldContainKey((PreExistingName, "T", Tenant2));
        }
        finally
        {
            await RecreateDatabaseAsync(create: false);
        }
    }

    [Fact]
    public async Task Down_keeps_a_new_name_value_stored_after_up_for_a_key_without_an_old_value()
    {
        /* A value an administrator stores under a new name after the deploy, for a tenant that had no old value, is
         * not a copy: going back must not take it away. */
        await RecreateDatabaseAsync(create: true);
        try
        {
            await MigrateToAsync(PreviousMigration);
            await SeedAsync(withPreExisting: false);
            await MigrateToAsync(ThisMigration);
            await InsertValueAsync("Apps.MasterData.General.Reference.Currencies", "true", "T", Tenant2);
            await InsertValueAsync(GeneralFeature, "false", "T", Tenant2);
            var afterUp = await RowsByIdAsync();

            await MigrateToAsync(PreviousMigration);

            var values = await ValuesAsync();
            values[("Apps.MasterData.General.Reference.Currencies", "T", Tenant2)].ShouldBe("true");
            values[(GeneralFeature, "T", Tenant2)].ShouldBe("false");
            var expected = new Dictionary<(string, string?, string?), string>(Seeded(withPreExisting: false))
            {
                [("Apps.MasterData.General.Reference.Currencies", "T", Tenant2)] = "true",
                [(GeneralFeature, "T", Tenant2)] = "false",
            };
            values.ShouldBe(expected, ignoreOrder: true);
            afterUp.Count.ShouldBeGreaterThan(values.Count);
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
            await MigrateToAsync(PreviousMigration);
            (await ValuesAsync()).ShouldBeEmpty();
        }
        finally
        {
            await RecreateDatabaseAsync(create: false);
        }
    }

    [Fact]
    public async Task The_migration_is_applied_after_RemoveOldFeatureNames_and_nothing_is_pending()
    {
        await RecreateDatabaseAsync(create: true);
        try
        {
            await MigrateToAsync();

            await using var context = CreateContext();
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.ShouldContain(ThisMigration);
            applied.IndexOf(ThisMigration).ShouldBe(applied.IndexOf(PreviousMigration) + 1, "directly after the previous application migration");
            /* Its contract step (RemoveOldMasterDataFeatureNames, PO 2026-10-16) is the only application migration after it. */
            applied.Skip(applied.IndexOf(ThisMigration) + 1).ShouldBe([ContractMigration], "only the contract step follows the expand step");
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            context.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot must describe the model that runs");
        }
        finally
        {
            await RecreateDatabaseAsync(create: false);
        }
    }
}
