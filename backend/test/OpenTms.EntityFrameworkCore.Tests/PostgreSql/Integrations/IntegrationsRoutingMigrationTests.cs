using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using OpenTms.Integrations.EntityFrameworkCore;
using OpenTms.Integrations.EntityFrameworkCore.Migrations;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.Integrations;

/// <summary>
/// The IntegrationsRouting migration on the databases the real DbMigrator created (design 60, "Migration strategy":
/// expand only — the RouteCache table with the unique (TenantId, Key) NULLS NOT DISTINCT index and the index on
/// ExpiresAt). Risk class: Critical — applied on the host and on every tenant database after Initial; the model snapshot
/// must describe the model that runs.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class IntegrationsRoutingMigrationTests : OpenTmsPostgreSqlTestBase
{
    private const string Migration = "20261025183127_IntegrationsRouting";
    private const string PreviousMigration = "20260812110454_Initial";

    private readonly IDbContextProvider<IntegrationsDbContext> _contextProvider;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public IntegrationsRoutingMigrationTests()
    {
        _contextProvider = GetRequiredService<IDbContextProvider<IntegrationsDbContext>>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_migration_is_applied_after_Initial_and_nothing_is_pending(string? tenant)
    {
        using (_currentTenant.Change(tenant == null ? null : PostgreSqlTestDatabases.TenantAId))
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            try
            {
                var context = await _contextProvider.GetDbContextAsync();

                var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
                applied.ShouldContain(Migration);
                applied.IndexOf(Migration).ShouldBeGreaterThan(applied.IndexOf(PreviousMigration));
                applied.Last().ShouldBe(Migration, "the newest Integrations migration");
                (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
                context.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot must describe the model that runs");
                context.Database.GetDbConnection().Database.ShouldBe(tenant == null ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase);
            }
            finally
            {
                await unitOfWork.RollbackAsync();
            }
        }
    }

    [Fact]
    public void The_migration_is_expand_only_and_its_Down_drops_exactly_the_cache_table()
    {
        var migration = new IntegrationsRouting();

        var up = migration.UpOperations;
        up.ShouldAllBe(o => o is EnsureSchemaOperation || o is CreateTableOperation || o is CreateIndexOperation, string.Join(", ", up.Select(o => o.GetType().Name).Distinct()));
        var table = up.OfType<CreateTableOperation>().ShouldHaveSingleItem();
        table.Name.ShouldBe("RouteCache");
        table.Schema.ShouldBe("integrations");
        table.Columns.Select(c => c.Name).ShouldBe(
            ["Id", "TenantId", "Key", "Provider", "DistanceM", "DurationS", "TollDistanceM", "TollAmount", "TollCurrencyCode", "Co2Kg", "Polyline", "CreatedAt", "ExpiresAt"],
            ignoreOrder: true);
        table.Columns.ShouldNotContain(c => c.Name.Contains("RelationId") || c.Name.Contains("OrderId"), "rows carry rounded coordinates only (security quickscan)");
        up.OfType<CreateIndexOperation>().Select(i => i.Name).ShouldBe(["IX_RouteCache_ExpiresAt", "IX_RouteCache_TenantId_Key"], ignoreOrder: true);

        var down = migration.DownOperations;
        down.OfType<DropTableOperation>().ShouldHaveSingleItem().Name.ShouldBe("RouteCache");
        down.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("tenant-a")]
    public async Task The_table_its_unique_key_and_its_expiry_index_are_in_place(string target)
    {
        var database = target == "host" ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase;
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();

        (await QueryAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'integrations'")).ShouldContain("RouteCache");

        var indexes = await QueryAsync(connection, "SELECT indexname || ' :: ' || indexdef FROM pg_indexes WHERE schemaname = 'integrations' AND tablename = 'RouteCache'");
        var unique = indexes.Single(i => i.StartsWith("IX_RouteCache_TenantId_Key :: ", StringComparison.Ordinal));
        unique.ShouldContain("UNIQUE", Case.Sensitive);
        unique.ShouldContain("NULLS NOT DISTINCT", Case.Sensitive, "host rows (TenantId null) are unique too");
        unique.ShouldContain("(\"TenantId\", \"Key\")", Case.Sensitive);
        unique.ShouldNotContain("WHERE", Case.Sensitive, "no soft delete on a cache row");
        var expiry = indexes.Single(i => i.StartsWith("IX_RouteCache_ExpiresAt :: ", StringComparison.Ordinal));
        expiry.ShouldNotContain("UNIQUE", Case.Sensitive);
        expiry.ShouldContain("(\"ExpiresAt\")", Case.Sensitive);

        var columns = await QueryAsync(connection,
            "SELECT column_name || ' :: ' || data_type || '(' || COALESCE(character_maximum_length::text, COALESCE(numeric_precision::text || ',' || numeric_scale::text, '-')) || ') :: ' || is_nullable " +
            "FROM information_schema.columns WHERE table_schema = 'integrations' AND table_name = 'RouteCache'");
        columns.ShouldContain("Key :: character varying(160) :: NO");
        columns.ShouldContain("Provider :: character varying(32) :: NO");
        columns.ShouldContain("TenantId :: uuid(-) :: YES");
        columns.ShouldContain("DistanceM :: integer(32,0) :: NO");
        columns.ShouldContain("DurationS :: integer(32,0) :: NO");
        columns.ShouldContain("TollDistanceM :: integer(32,0) :: YES");
        columns.ShouldContain("TollAmount :: numeric(18,2) :: YES");
        columns.ShouldContain("TollCurrencyCode :: character varying(3) :: YES");
        columns.ShouldContain("Co2Kg :: numeric(18,3) :: YES");
        columns.ShouldContain("Polyline :: text(-) :: YES");
        columns.ShouldContain("ExpiresAt :: timestamp without time zone(-) :: NO");
    }

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
