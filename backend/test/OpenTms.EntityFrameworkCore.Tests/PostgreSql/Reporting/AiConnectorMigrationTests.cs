using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using OpenTms.Integrations.EntityFrameworkCore;
using OpenTms.Orders.EntityFrameworkCore;
using OpenTms.PlanningExecution.EntityFrameworkCore;
using OpenTms.PostgreSql.ReferenceData;
using OpenTms.Reporting.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;
using IntegrationsMigrations = OpenTms.Integrations.EntityFrameworkCore.Migrations;
using OrdersMigrations = OpenTms.Orders.EntityFrameworkCore.Migrations;
using PlanningMigrations = OpenTms.PlanningExecution.EntityFrameworkCore.Migrations;
using ReportingMigrations = OpenTms.Reporting.EntityFrameworkCore.Migrations;

namespace OpenTms.PostgreSql;

/// <summary>The migrations of designs 74 and 75, named once: the migration tests before them point at these.</summary>
public static class AiConnectorMigrationConsts
{
    public const string IntegrationsAiConnections = "20261105233310_IntegrationsAiConnections";
    public const string OrdersCustomerRevenueIndex = "20261105233312_OrdersCustomerRevenueIndex";
    public const string OrdersRepairIndexes = "20261106003316_OrdersRepairIndexes";
    public const string PlanningRepairIndexes = "20261106003317_PlanningRepairIndexes";
    public const string ReportingMetricFacts = "20261106002630_ReportingMetricFacts";
}

/// <summary>
/// The five migrations of designs 74 and 75 (SaaS obligation "Migration test"; both designs "Migration strategy": expand-only) on the databases the real DbMigrator created, the host and a tenant:
/// in their place, nothing pending, the snapshot current, the tables and indexes there. Up creates only and Down removes exactly that, so the previous revision keeps working during a canary; an
/// insert the previous release writes still works against the migrated orders table. Risk class: Critical (a migration on every tenant database).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class AiConnectorMigrationTests : ReferenceDataPostgreSqlTestBase
{
    private async Task<List<string>> AppliedAsync<TContext>()
        where TContext : DbContext, IEfCoreDbContext
    {
        var context = await GetRequiredService<IDbContextProvider<TContext>>().GetDbContextAsync();
        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        context.Database.HasPendingModelChanges().ShouldBeFalse(typeof(TContext).Name + ": the model snapshot must describe the model that runs");
        return (await context.Database.GetAppliedMigrationsAsync()).ToList();
    }

    private static async Task<List<string>> IndexesAsync(string database, string schema, string table)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT indexname FROM pg_indexes WHERE schemaname = @s AND tablename = @t", connection);
        command.Parameters.AddWithValue("s", schema);
        command.Parameters.AddWithValue("t", table);
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task Each_migration_is_in_its_place_on_the_host_and_a_tenant_database_with_nothing_pending(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var integrations = await AppliedAsync<IntegrationsDbContext>();
            integrations.IndexOf(AiConnectorMigrationConsts.IntegrationsAiConnections).ShouldBe(integrations.IndexOf(OpenTms.PostgreSql.Integrations.IntegrationsInboundMessageSearchMigrationTests.Migration) + 1);
            integrations.Last().ShouldBe(AiConnectorMigrationConsts.IntegrationsAiConnections, "the newest Integrations migration");

            var orders = await AppliedAsync<OrdersDbContext>();
            orders.IndexOf(AiConnectorMigrationConsts.OrdersCustomerRevenueIndex).ShouldBe(orders.IndexOf(OpenTms.PostgreSql.Orders.OrdersIntakeNameSimilarityThresholdMigrationTests.Migration) + 1);
            orders.IndexOf(AiConnectorMigrationConsts.OrdersRepairIndexes).ShouldBe(orders.IndexOf(AiConnectorMigrationConsts.OrdersCustomerRevenueIndex) + 1);
            orders.Last().ShouldBe(AiConnectorMigrationConsts.OrdersRepairIndexes, "the newest Orders migration");

            var planning = await AppliedAsync<PlanningExecutionDbContext>();
            planning.Last().ShouldBe(AiConnectorMigrationConsts.PlanningRepairIndexes, "the newest Planning migration");

            var reporting = await AppliedAsync<ReportingDbContext>();
            reporting.Last().ShouldBe(AiConnectorMigrationConsts.ReportingMetricFacts, "the newest Reporting migration");
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_tables_and_indexes_exist_on_the_migrated_databases(bool host)
    {
        var database = host ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase;
        var connections = await IndexesAsync(database, "integrations", "AiConnections");
        connections.ShouldContain("IX_AiConnections_AuthorizationId");
        connections.ShouldContain(i => i.Contains("UserId", StringComparison.Ordinal));

        var orders = await IndexesAsync(database, "orders", "TransportOrders");
        orders.ShouldContain("IX_TransportOrders_TenantId_CustomerRelationId_OrderDate");
        orders.ShouldContain("IX_TransportOrders_TenantId_CreationTime");
        orders.ShouldContain("IX_TransportOrders_TenantId_LastModificationTime");

        var trips = await IndexesAsync(database, "planning", "TransportTrips");
        trips.ShouldContain("IX_TransportTrips_TenantId_CreationTime");
        trips.ShouldContain("IX_TransportTrips_TenantId_LastModificationTime");

        var orderFacts = await IndexesAsync(database, "reporting", "OrderFacts");
        foreach (var index in new[] { "OrderDate", "CustomerRelationId_OrderDate", "TransportOrderTypeId_OrderDate", "OrganizationUnitId_OrderDate" })
        {
            orderFacts.ShouldContain("IX_OrderFacts_TenantId_" + index);
        }

        var tripFacts = await IndexesAsync(database, "reporting", "TripFacts");
        foreach (var index in new[] { "TripDate", "CarrierRelationId_TripDate", "PrimaryVehicleId_TripDate", "TransportTripTypeId_TripDate", "OrganizationUnitId_TripDate" })
        {
            tripFacts.ShouldContain("IX_TripFacts_TenantId_" + index);
        }
    }

    public static IEnumerable<object[]> IndexOnlyMigrations()
    {
        yield return [new OrdersMigrations.OrdersCustomerRevenueIndex()];
        yield return [new OrdersMigrations.OrdersRepairIndexes()];
        yield return [new PlanningMigrations.PlanningRepairIndexes()];
    }

    [Theory]
    [MemberData(nameof(IndexOnlyMigrations))]
    public void An_index_migration_only_adds_indexes_and_its_Down_only_drops_them(Migration migration)
    {
        migration.UpOperations.ShouldNotBeEmpty();
        migration.UpOperations.ShouldAllBe(o => o is CreateIndexOperation);
        migration.DownOperations.ShouldAllBe(o => o is DropIndexOperation);
        migration.DownOperations.OfType<DropIndexOperation>().Select(o => o.Name)
            .ShouldBe(migration.UpOperations.OfType<CreateIndexOperation>().Select(o => o.Name), ignoreOrder: true);
    }

    [Fact]
    public void The_ai_connections_migration_only_creates_its_table_with_a_unique_authorization_id()
    {
        var migration = new IntegrationsMigrations.IntegrationsAiConnections();
        migration.UpOperations.ShouldAllBe(o => o is CreateTableOperation || o is CreateIndexOperation);
        var table = migration.UpOperations.OfType<CreateTableOperation>().ShouldHaveSingleItem();
        (table.Schema, table.Name).ShouldBe(("integrations", "AiConnections"));
        table.Columns.Select(c => c.Name).ShouldBe(
            ["Id", "TenantId", "UserId", "ClientId", "AuthorizationId", "ConnectedAt", "LastUsedAt", "RevokedAt", "RevokedByUserId"], ignoreOrder: true);
        var unique = migration.UpOperations.OfType<CreateIndexOperation>().Single(i => i.Name == "IX_AiConnections_AuthorizationId");
        unique.IsUnique.ShouldBeTrue();
        migration.DownOperations.ShouldHaveSingleItem().ShouldBeOfType<DropTableOperation>().Name.ShouldBe("AiConnections");
    }

    [Fact]
    public void The_metric_facts_migration_only_creates_its_two_tables_and_indexes_in_its_own_schema()
    {
        var migration = new ReportingMigrations.ReportingMetricFacts();
        migration.UpOperations.ShouldAllBe(o => o is EnsureSchemaOperation || o is CreateTableOperation || o is CreateIndexOperation);
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToList();
        tables.Select(t => t.Name).ShouldBe(["OrderFacts", "TripFacts"], ignoreOrder: true);
        tables.ShouldAllBe(t => t.Schema == "reporting");
        migration.UpOperations.OfType<CreateIndexOperation>().ShouldAllBe(i => i.Schema == "reporting");
        migration.DownOperations.OfType<DropTableOperation>().Select(t => t.Name).ShouldBe(["OrderFacts", "TripFacts"], ignoreOrder: true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_metric_facts_map_only_their_own_schema(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var context = await GetRequiredService<IDbContextProvider<ReportingDbContext>>().GetDbContextAsync();
            context.Model.GetEntityTypes().Select(t => t.GetSchema()).Distinct().ShouldBe(["reporting"], "no cross-schema mapping");
        });
    }
}
