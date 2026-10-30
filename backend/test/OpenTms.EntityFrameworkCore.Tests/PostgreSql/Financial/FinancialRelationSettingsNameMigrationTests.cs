using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using OpenTms.Financial.EntityFrameworkCore;
using OpenTms.Financial.EntityFrameworkCore.Migrations;
using OpenTms.PostgreSql.ReferenceData;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace OpenTms.PostgreSql.Financial;

/// <summary>
/// The FinancialRelationSettingsName migration (review M2): applied right after FinancialTariffs on the host and on a
/// tenant database; expand only (one non-null column with an empty default, so existing rows keep working, and its sort
/// index); Down drops exactly that index and column. Risk class: Critical (migrations).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class FinancialRelationSettingsNameMigrationTests : ReferenceDataPostgreSqlTestBase
{
    public const string Migration = "20261026100702_FinancialRelationSettingsName";
    private const string PreviousMigration = "20261025191117_FinancialTariffs";
    private const string Index = "IX_InvoicingRelationFinancialSettings_TenantId_RelationName";

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_migration_follows_FinancialTariffs_and_nothing_is_pending(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var context = await GetRequiredService<IDbContextProvider<FinancialDbContext>>().GetDbContextAsync();
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.IndexOf(Migration).ShouldBe(applied.IndexOf(PreviousMigration) + 1);
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            context.Database.HasPendingModelChanges().ShouldBeFalse();
        });
    }

    [Fact]
    public void The_migration_is_expand_only_and_its_Down_drops_exactly_its_column_and_index()
    {
        var migration = new FinancialRelationSettingsName();

        var column = migration.UpOperations.OfType<AddColumnOperation>().ShouldHaveSingleItem();
        column.Name.ShouldBe("RelationName");
        column.Table.ShouldBe("InvoicingRelationFinancialSettings");
        column.Schema.ShouldBe("financial");
        column.IsNullable.ShouldBeFalse();
        column.DefaultValue.ShouldBe(string.Empty, "existing rows get an empty name, never a failing insert");
        migration.UpOperations.OfType<CreateIndexOperation>().ShouldHaveSingleItem().Name.ShouldBe(Index);
        migration.UpOperations.Count.ShouldBe(2);

        migration.DownOperations.OfType<DropColumnOperation>().ShouldHaveSingleItem().Name.ShouldBe("RelationName");
        migration.DownOperations.OfType<DropIndexOperation>().ShouldHaveSingleItem().Name.ShouldBe(Index);
        migration.DownOperations.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("tenant-a")]
    public async Task The_column_and_its_sort_index_are_in_place(string target)
    {
        var database = target == "host" ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase;
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();

        (await QueryAsync(connection, "SELECT data_type || ':' || character_maximum_length || ':' || is_nullable FROM information_schema.columns WHERE table_schema = 'financial' AND table_name = 'InvoicingRelationFinancialSettings' AND column_name = 'RelationName'"))
            .ShouldHaveSingleItem().ShouldBe("character varying:128:NO");
        (await QueryAsync(connection, $"SELECT indexdef FROM pg_indexes WHERE schemaname = 'financial' AND indexname = '{Index}'"))
            .ShouldHaveSingleItem().ShouldContain("(\"TenantId\", \"RelationName\")");
    }

    private static async Task<List<string>> QueryAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }
}
