using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using OpenTms.MasterData.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace OpenTms.PostgreSql.MasterData;

/// <summary>
/// The MasterDataFirstRun migration on the databases the real DbMigrator created (design 41,
/// "Migration strategy"). Risk class: Critical — applied on the host and on every tenant database,
/// expand-only, the model snapshot in sync with the model, the partial indexes present.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class MasterDataMigrationTests : MasterDataPostgreSqlTestBase
{
    private const string Migration = "20261011170938_MasterDataFirstRun";

    private static readonly string[] Tables =
    [
        "Currencies", "Countries", "AddressTypes", "RelationCategories", "TransportCharacteristics", "Temperatures",
        "Products", "TransportUnits", "TransportUnitLoadFactors", "VehicleTypes", "VehicleTypeTransportCharacteristics",
        "GeographicalRegions", "GeographicalRegionPostalCodeRanges", "TemperatureIncompatibilities", "ProductIncompatibilities",
        "TransportUnitIncompatibilities"
    ];

    private readonly IDbContextProvider<MasterDataDbContext> _contextProvider;

    public MasterDataMigrationTests()
    {
        _contextProvider = GetRequiredService<IDbContextProvider<MasterDataDbContext>>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_migration_is_applied_and_the_model_has_no_pending_changes(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var context = await _contextProvider.GetDbContextAsync();

            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.ShouldContain(Migration);
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();

            /* Design 41, "Migration strategy": the snapshot describes the model that runs. */
            var differences = ModelDifferences(context);
            differences.ShouldBeEmpty("the model snapshot and the runtime model differ (EF Core would scaffold these operations into a new migration)");
            context.Database.HasPendingModelChanges().ShouldBeFalse();
            context.Database.GetDbConnection().Database.ShouldBe(tenant == null ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase);
        });
    }

    [Theory]
    [InlineData(PostgreSqlTestDatabases.HostDatabase)]
    [InlineData(PostgreSqlTestDatabases.TenantADatabase)]
    public async Task Every_table_and_the_partial_unique_indexes_exist_in_the_masterdata_schema(string database)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();

        var tables = await QueryAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'masterdata'");
        tables.ShouldContain("__EFMigrationsHistory");
        foreach (var table in Tables)
        {
            tables.ShouldContain(table);
        }

        var indexes = await QueryAsync(connection, "SELECT indexname || ' :: ' || indexdef FROM pg_indexes WHERE schemaname = 'masterdata'");
        indexes.ShouldContain(i => i.StartsWith("IX_Currencies_TenantId_Default :: ", StringComparison.Ordinal) && i.Contains("UNIQUE") && i.Contains("NULLS NOT DISTINCT") && i.Contains("\"IsDefault\" = true") && i.Contains("\"IsDeleted\" = false"));
        indexes.ShouldContain(i => i.StartsWith("IX_Currencies_TenantId_IsoCode :: ", StringComparison.Ordinal) && i.Contains("UNIQUE") && i.Contains("NULLS NOT DISTINCT") && i.Contains("\"IsDeleted\" = false"));
        indexes.ShouldContain(i => i.StartsWith("IX_AddressTypes_TenantId_Kind :: ", StringComparison.Ordinal) && i.Contains("UNIQUE") && i.Contains("\"Kind\" <> 0"));
        indexes.ShouldContain(i => i.StartsWith("IX_Countries_TenantId_IsoAlpha2 :: ", StringComparison.Ordinal) && i.Contains("UNIQUE"));
        indexes.ShouldContain(i => i.StartsWith("IX_Countries_TenantId_IsoAlpha3 :: ", StringComparison.Ordinal) && i.Contains("UNIQUE"));
        indexes.ShouldContain(i => i.StartsWith("IX_GeographicalRegionPostalCodeRanges_CountryId :: ", StringComparison.Ordinal));
        indexes.ShouldContain(i => i.StartsWith("IX_TransportUnitLoadFactors_VehicleTypeId :: ", StringComparison.Ordinal));
        indexes.ShouldContain(i => i.StartsWith("IX_TransportUnitLoadFactors_TransportUnitId_VehicleTypeId :: ", StringComparison.Ordinal) && i.Contains("UNIQUE"));
        foreach (var table in new[] { "Products", "TransportUnits", "VehicleTypes", "GeographicalRegions", "Temperatures", "RelationCategories" })
        {
            indexes.ShouldContain(i => i.StartsWith($"IX_{table}_TenantId_Name :: ", StringComparison.Ordinal) && i.Contains("UNIQUE") && i.Contains("NULLS NOT DISTINCT") && i.Contains("\"IsDeleted\" = false"), table);
        }

        indexes.ShouldContain(i => i.StartsWith("IX_TransportCharacteristics_TenantId_Code :: ", StringComparison.Ordinal) && i.Contains("UNIQUE") && i.Contains("\"IsDeleted\" = false"));
        foreach (var table in new[] { "Currencies", "Countries", "AddressTypes", "Products", "TransportUnits", "VehicleTypes", "GeographicalRegions", "Temperatures", "RelationCategories", "TransportCharacteristics" })
        {
            indexes.ShouldContain(i => i.StartsWith($"IX_{table}_TenantId :: ", StringComparison.Ordinal), table);
        }

        var checks = await QueryAsync(connection, "SELECT conname FROM pg_constraint WHERE contype = 'c' AND connamespace = 'masterdata'::regnamespace");
        checks.ShouldContain("CK_Currencies_MinorUnits");
        checks.ShouldContain("CK_Temperatures_Range");
        checks.ShouldContain("CK_GeographicalRegionPostalCodeRanges_Form");
        checks.ShouldContain("CK_ProductIncompatibilities_NotSelf");
        checks.ShouldContain("CK_TemperatureIncompatibilities_NotSelf");
        checks.ShouldContain("CK_TransportUnitIncompatibilities_NotSelf");

        var foreignKeys = await QueryAsync(connection, "SELECT conname || ' :: ' || confdeltype::text FROM pg_constraint WHERE contype = 'f' AND connamespace = 'masterdata'::regnamespace");
        foreignKeys.ShouldContain("FK_Countries_Currencies_DefaultCurrencyId :: r");
        foreignKeys.ShouldContain("FK_GeographicalRegionPostalCodeRanges_Countries_CountryId :: r");
        foreignKeys.ShouldContain("FK_TransportUnitLoadFactors_VehicleTypes_VehicleTypeId :: r");
        /* PostgreSQL truncates identifiers at 63 characters, so this name ends in "~". */
        foreignKeys.ShouldContain(fk => fk.StartsWith("FK_GeographicalRegionPostalCodeRanges_GeographicalRegions_Geog", StringComparison.Ordinal) && fk.EndsWith(":: c", StringComparison.Ordinal));
        foreignKeys.ShouldContain("FK_TransportUnitLoadFactors_TransportUnits_TransportUnitId :: c");
        foreignKeys.ShouldContain("FK_ProductIncompatibilities_Products_LeftId :: c");
        foreignKeys.ShouldContain("FK_ProductIncompatibilities_Products_RightId :: c");
    }

    /// <summary>The operations EF Core would put into the next migration: snapshot model versus runtime model.</summary>
    private static List<string> ModelDifferences(DbContext context)
    {
        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        var snapshotModel = context.GetService<IModelRuntimeInitializer>().Initialize(((IMutableModel)snapshot).FinalizeModel());
        var runtimeModel = context.GetService<IDesignTimeModel>().Model;
        return context.GetService<IMigrationsModelDiffer>()
            .GetDifferences(snapshotModel.GetRelationalModel(), runtimeModel.GetRelationalModel())
            .Select(Describe)
            .ToList();
    }

    private static string Describe(MigrationOperation operation) => operation switch
    {
        AlterColumnOperation alter => $"AlterColumn {alter.Schema}.{alter.Table}.{alter.Name}: {alter.OldColumn.ColumnType} -> {alter.ColumnType}",
        AddColumnOperation add => $"AddColumn {add.Schema}.{add.Table}.{add.Name} ({add.ColumnType})",
        DropColumnOperation drop => $"DropColumn {drop.Schema}.{drop.Table}.{drop.Name}",
        CreateIndexOperation index => $"CreateIndex {index.Schema}.{index.Table} {index.Name}",
        DropIndexOperation index => $"DropIndex {index.Schema}.{index.Table} {index.Name}",
        CreateTableOperation table => $"CreateTable {table.Schema}.{table.Name}",
        DropTableOperation table => $"DropTable {table.Schema}.{table.Name}",
        _ => operation.GetType().Name
    };

    private static async Task<List<string>> QueryAsync(NpgsqlConnection connection, string sql)
    {
        var rows = new List<string>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
