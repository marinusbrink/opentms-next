using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OpenTms.MasterData.EntityFrameworkCore;
using OpenTms.MasterData.EntityFrameworkCore.Migrations;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace OpenTms.PostgreSql.MasterData;

/// <summary>
/// The QP10 migration (design 56, "own-fleet cost per km"): one nullable <c>numeric(18,4)</c> column on the vehicle types
/// and on the vehicles, applied after the combination-name migration in the host and the tenant database, expand only,
/// and its Down drops exactly those two columns. Risk class: High (migration).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class MasterDataCostPricePerKmMigrationTests : MasterDataPostgreSqlTestBase
{
    private const string PreviousMigration = "20261016211522_MasterDataVehicleCombinationNormalizedName";
    private const string Migration = "20261025212434_MasterDataCostPricePerKm";

    private readonly IDbContextProvider<MasterDataDbContext> _contextProvider;

    public MasterDataCostPricePerKmMigrationTests()
    {
        _contextProvider = GetRequiredService<IDbContextProvider<MasterDataDbContext>>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_migration_is_applied_last_and_the_columns_are_numeric_18_4_and_nullable(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var context = await _contextProvider.GetDbContextAsync();
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.Last().ShouldBe(Migration);
            applied.IndexOf(Migration).ShouldBe(applied.IndexOf(PreviousMigration) + 1);
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            context.Database.HasPendingModelChanges().ShouldBeFalse();

            await using var command = new NpgsqlCommand(
                "SELECT table_name, data_type, numeric_precision, numeric_scale, is_nullable FROM information_schema.columns WHERE table_schema = 'masterdata' AND column_name = 'CostPricePerKm' ORDER BY table_name DESC",
                (NpgsqlConnection)context.Database.GetDbConnection());
            command.Transaction = (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction();
            await using var reader = await command.ExecuteReaderAsync();
            var columns = new System.Collections.Generic.List<(string Table, string Type, int Precision, int Scale, string Nullable)>();
            while (await reader.ReadAsync())
            {
                columns.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetString(4)));
            }

            columns.ShouldBe([("VehicleTypes", "numeric", 18, 4, "YES"), ("Vehicles", "numeric", 18, 4, "YES")]);
        });
    }

    [Fact]
    public void The_migration_is_expand_only_and_its_Down_drops_exactly_the_two_columns()
    {
        var migration = new MasterDataCostPricePerKm();
        var up = migration.UpOperations;
        up.ShouldAllBe(o => o is AddColumnOperation);
        up.OfType<AddColumnOperation>().Select(c => (c.Table, c.Name, c.IsNullable)).ShouldBe([("VehicleTypes", "CostPricePerKm", true), ("Vehicles", "CostPricePerKm", true)], ignoreOrder: true);

        var down = migration.DownOperations;
        down.ShouldAllBe(o => o is DropColumnOperation);
        down.OfType<DropColumnOperation>().Select(c => (c.Table, c.Name)).ShouldBe([("VehicleTypes", "CostPricePerKm"), ("Vehicles", "CostPricePerKm")], ignoreOrder: true);
    }
}
