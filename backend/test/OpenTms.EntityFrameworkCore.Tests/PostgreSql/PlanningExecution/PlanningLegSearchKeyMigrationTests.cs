using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OpenTms.PlanningExecution.EntityFrameworkCore;
using OpenTms.PlanningExecution.EntityFrameworkCore.Migrations;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace OpenTms.PostgreSql.PlanningExecution;

/// <summary>
/// The leg search migration (the performance gate's 355 ms unplanned-legs search): <c>planning."Legs"."SearchKey"</c>, a stored generated
/// <c>text</c> column, under the gin trigram index <c>IX_Legs_SearchKey_Trigram</c>; applied right after the design 74/75 index repair as the
/// newest Planning migration on the host and a tenant database; expand only (the previous revision neither reads nor writes the column, and
/// the database fills it on its inserts), and its Down drops exactly the index and the column. Risk class: Critical (a migration on every
/// tenant database).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PlanningLegSearchKeyMigrationTests : PlanningPostgreSqlTestBase
{
    public const string Migration = "20261118035532_LegSearchKey";

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_migration_is_the_newest_and_adds_a_generated_column_under_a_trigram_index(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var context = await GetRequiredService<IDbContextProvider<PlanningExecutionDbContext>>().GetDbContextAsync();
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.IndexOf(Migration).ShouldBe(applied.IndexOf(AiConnectorMigrationConsts.PlanningRepairIndexes) + 1);
            applied.Last().ShouldBe(Migration, "the newest Planning migration");
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            context.Database.HasPendingModelChanges().ShouldBeFalse();

            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            var transaction = (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction();

            await using (var column = new NpgsqlCommand(
                "SELECT data_type, is_nullable, is_generated, generation_expression FROM information_schema.columns WHERE table_schema = 'planning' AND table_name = 'Legs' AND column_name = 'SearchKey'",
                connection, transaction))
            await using (var reader = await column.ExecuteReaderAsync())
            {
                (await reader.ReadAsync()).ShouldBeTrue("the column exists");
                reader.GetString(0).ShouldBe("text");
                reader.GetString(1).ShouldBe("NO");
                reader.GetString(2).ShouldBe("ALWAYS", "the database fills it, never code");
                reader.GetString(3).ShouldContain("lower(");
            }

            await using var index = new NpgsqlCommand(
                "SELECT indexdef FROM pg_indexes WHERE schemaname = 'planning' AND tablename = 'Legs' AND indexname = 'IX_Legs_SearchKey_Trigram'",
                connection, transaction);
            var definition = (string?)await index.ExecuteScalarAsync();
            definition.ShouldNotBeNull();
            definition.ShouldContain("USING gin");
            definition.ShouldContain("gin_trgm_ops");
        });
    }

    [Fact]
    public void The_migration_is_expand_only_and_its_Down_drops_exactly_the_index_and_the_column()
    {
        var migration = new LegSearchKey();
        migration.UpOperations.Count.ShouldBe(2);
        var add = migration.UpOperations[0].ShouldBeOfType<AddColumnOperation>();
        (add.Schema, add.Table, add.Name, add.IsStored).ShouldBe(("planning", "Legs", "SearchKey", (bool?)true));
        add.ComputedColumnSql.ShouldNotBeNullOrEmpty();
        migration.UpOperations[1].ShouldBeOfType<CreateIndexOperation>().Name.ShouldBe("IX_Legs_SearchKey_Trigram");

        migration.DownOperations.Select(o => o switch
        {
            DropIndexOperation i => "index " + i.Name,
            DropColumnOperation c => "column " + c.Table + "." + c.Name,
            _ => o.GetType().Name,
        }).ShouldBe(new List<string> { "index IX_Legs_SearchKey_Trigram", "column Legs.SearchKey" });
    }
}
