using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OpenTms.MasterData.EntityFrameworkCore;
using OpenTms.PostgreSql.ReferenceData;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;
using MasterDataMigrations = OpenTms.MasterData.EntityFrameworkCore.Migrations;

namespace OpenTms.PostgreSql.MasterData;

/// <summary>
/// The MasterDataCodeProductNameIndex migration (the performance gate's 513 to 530 ms code list sorted by product name): the
/// index <c>(TenantId, ProductId, NormalizedCode)</c> over the undeleted codes that the per-product read of
/// <c>EfCoreProductRelationCodeRepository.GetPageByProductNameAsync</c> uses; applied right after MasterDataRelationEmailIndex as the
/// newest Master Data migration on the host and a tenant database, nothing pending; Up only creates the index and Down only drops
/// it (expand only, the previous release is unaffected). Risk class: High (a migration on every tenant database).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class MasterDataCodeProductNameIndexMigrationTests : ReferenceDataPostgreSqlTestBase
{
    public const string Migration = "20261118162941_MasterDataCodeProductNameIndex";
    private const string Index = "IX_ProductRelationCodes_TenantId_ProductId_NormalizedCode";

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_migration_is_the_newest_and_the_index_exists_on_the_migrated_databases(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var context = await GetRequiredService<IDbContextProvider<MasterDataDbContext>>().GetDbContextAsync();
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.IndexOf(Migration).ShouldBe(applied.IndexOf(MasterDataRelationEmailIndexMigrationTests.Migration) + 1);
            applied.Last().ShouldBe(Migration, "the newest Master Data migration");
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            context.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot must describe the model that runs");

            await using var command = new NpgsqlCommand(
                $"SELECT indexdef FROM pg_indexes WHERE schemaname = 'masterdata' AND tablename = 'ProductRelationCodes' AND indexname = '{Index}'",
                (NpgsqlConnection)context.Database.GetDbConnection(),
                (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction());
            var definition = (string?)await command.ExecuteScalarAsync();
            definition.ShouldNotBeNull();
            definition.ShouldContain("(\"TenantId\", \"ProductId\", \"NormalizedCode\")");
            definition.ShouldContain("WHERE (\"IsDeleted\" = false)");
        });
    }

    [Fact]
    public void The_migration_only_adds_the_index_and_its_Down_only_drops_it()
    {
        var migration = new MasterDataMigrations.MasterDataCodeProductNameIndex();

        var index = migration.UpOperations.ShouldHaveSingleItem().ShouldBeOfType<CreateIndexOperation>();
        (index.Schema, index.Table, index.Name).ShouldBe(("masterdata", "ProductRelationCodes", Index));
        index.Columns.ShouldBe(["TenantId", "ProductId", "NormalizedCode"]);
        index.IsUnique.ShouldBeFalse();
        index.Filter.ShouldBe("\"IsDeleted\" = false");
        migration.DownOperations.ShouldHaveSingleItem().ShouldBeOfType<DropIndexOperation>().Name.ShouldBe(Index);
    }
}
