using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OpenTms.Integrations.EntityFrameworkCore;
using OpenTms.MasterData.EntityFrameworkCore;
using OpenTms.Orders;
using OpenTms.Orders.EntityFrameworkCore;
using OpenTms.PostgreSql.ReferenceData;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;
using IntegrationsMigrations = OpenTms.Integrations.EntityFrameworkCore.Migrations;
using MasterDataMigrations = OpenTms.MasterData.EntityFrameworkCore.Migrations;
using OrdersMigrations = OpenTms.Orders.EntityFrameworkCore.Migrations;

namespace OpenTms.PostgreSql.Integrations
{
    /// <summary>
    /// The IntegrationsOrderIntake migration (design 72 "Migration strategy": additive only) on the databases the real
    /// DbMigrator created: applied after IntegrationsRouting on the host and a tenant database (followed by the review's
    /// IntegrationsInboundMessageSearch), nothing pending, the snapshot
    /// current; Up only creates the seven tables and their indexes (the dedupe key unique NULLS NOT DISTINCT, the client id
    /// unique globally), Down drops exactly those tables. Risk class: Critical (a migration on every tenant database).
    /// </summary>
    [Collection(PostgreSqlTestCollection.Name)]
    public class IntegrationsOrderIntakeMigrationTests : ReferenceDataPostgreSqlTestBase
    {
        public const string Migration = "20261103111747_IntegrationsOrderIntake";
        private const string PreviousMigration = "20261025183127_IntegrationsRouting";

        private static readonly string[] Tables =
            ["AiUsageRecords", "ApiClients", "ImportProfiles", "InboundMessages", "MailboxConnections", "InboundDocuments", "InboundDocumentSteps"];

        [Theory]
        [InlineData(null)]
        [InlineData("tenant-a")]
        public async Task The_migration_follows_routing_and_nothing_is_pending(string? tenant)
        {
            await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
            {
                var context = await GetRequiredService<IDbContextProvider<IntegrationsDbContext>>().GetDbContextAsync();
                var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
                applied.IndexOf(Migration).ShouldBe(applied.IndexOf(PreviousMigration) + 1);
                /* The review's search migration (design 72 review M2) came after it and is now the newest. */
                applied.IndexOf(IntegrationsInboundMessageSearchMigrationTests.Migration).ShouldBe(applied.IndexOf(Migration) + 1);
                applied.Last().ShouldBe(IntegrationsInboundMessageSearchMigrationTests.Migration);
                (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
                context.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot must describe the model that runs");
            });
        }

        [Fact]
        public void The_migration_is_expand_only_and_its_Down_drops_exactly_its_tables()
        {
            var migration = new IntegrationsMigrations.IntegrationsOrderIntake();

            migration.UpOperations.ShouldAllBe(o => o is EnsureSchemaOperation || o is CreateTableOperation || o is CreateIndexOperation);
            migration.UpOperations.OfType<CreateTableOperation>().Select(t => t.Name).ShouldBe(Tables, ignoreOrder: true);
            migration.UpOperations.OfType<CreateTableOperation>().ShouldAllBe(t => t.Schema == "integrations");

            var indexes = migration.UpOperations.OfType<CreateIndexOperation>().ToDictionary(i => i.Name);
            var dedupe = indexes["IX_InboundMessages_TenantId_Channel_SourceId_ExternalId"];
            dedupe.IsUnique.ShouldBeTrue();
            dedupe.Columns.ShouldBe(["TenantId", "Channel", "SourceId", "ExternalId"]);
            dedupe["Npgsql:NullsDistinct"].ShouldBe(false, "the shared tier's host rows (TenantId null) dedupe too");
            indexes["IX_ApiClients_ClientId"].IsUnique.ShouldBeTrue();
            indexes["IX_ApiClients_ClientId"].Columns.ShouldBe(["ClientId"], "unique globally, the token server finds the client by it");
            var mailbox = indexes["IX_MailboxConnections_TenantId_Provider_EmailAddress_FolderId"];
            mailbox.IsUnique.ShouldBeTrue();
            mailbox.Filter.ShouldNotBeNull("unique among undeleted rows");
            indexes.Keys.ShouldContain("IX_InboundMessages_TenantId_ReceivedAt");
            indexes.Keys.ShouldContain("IX_ImportProfiles_TenantId_Format_SenderKey");
            indexes.Keys.ShouldContain("IX_AiUsageRecords_TenantId_At");

            migration.DownOperations.ShouldAllBe(o => o is DropTableOperation);
            migration.DownOperations.OfType<DropTableOperation>().Select(t => t.Name).ShouldBe(Tables, ignoreOrder: true);
        }
    }

    /// <summary>
    /// The IntegrationsInboundMessageSearch migration (design 72 review M2: the Received list searches with ILIKE on the raw
    /// columns): applied after IntegrationsOrderIntake and the newest on the host and a tenant database, nothing pending; Up
    /// only enables pg_trgm and creates the three GIN trigram indexes, Down only drops them (expand only, the previous release
    /// is unaffected); on the migrated databases the indexes exist with gin_trgm_ops. Risk class: High (migration on every
    /// tenant database).
    /// </summary>
    [Collection(PostgreSqlTestCollection.Name)]
    public class IntegrationsInboundMessageSearchMigrationTests : ReferenceDataPostgreSqlTestBase
    {
        public const string Migration = "20261103124726_IntegrationsInboundMessageSearch";

        private static readonly string[] Indexes =
            ["IX_InboundMessages_ExternalId_Trigram", "IX_InboundMessages_From_Trigram", "IX_InboundMessages_Subject_Trigram"];

        [Theory]
        [InlineData(null)]
        [InlineData("tenant-a")]
        public async Task The_migration_follows_the_order_intake_is_the_newest_and_nothing_is_pending(string? tenant)
        {
            await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
            {
                var context = await GetRequiredService<IDbContextProvider<IntegrationsDbContext>>().GetDbContextAsync();
                var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
                applied.IndexOf(Migration).ShouldBe(applied.IndexOf(IntegrationsOrderIntakeMigrationTests.Migration) + 1);
                applied.Last().ShouldBe(Migration);
                (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
                context.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot must describe the model that runs");
            });
        }

        [Fact]
        public void The_migration_only_adds_the_trigram_indexes_and_its_Down_only_drops_them()
        {
            var migration = new IntegrationsMigrations.IntegrationsInboundMessageSearch();

            migration.UpOperations.ShouldAllBe(o => o is AlterDatabaseOperation || o is CreateIndexOperation);
            migration.UpOperations.OfType<AlterDatabaseOperation>().ShouldHaveSingleItem()["Npgsql:PostgresExtension:pg_trgm"].ShouldNotBeNull();
            var created = migration.UpOperations.OfType<CreateIndexOperation>().ToList();
            created.Select(i => i.Name).ShouldBe(Indexes, ignoreOrder: true);
            created.ShouldAllBe(i => i.Table == "InboundMessages" && i.Schema == "integrations" && !i.IsUnique && i.Columns.Length == 1);
            created.ShouldAllBe(i => (string?)i["Npgsql:IndexMethod"] == "gin");
            created.ShouldAllBe(i => ((string[])i["Npgsql:IndexOperators"]!).Single() == "gin_trgm_ops");

            migration.DownOperations.ShouldAllBe(o => o is DropIndexOperation || o is AlterDatabaseOperation);
            migration.DownOperations.OfType<DropIndexOperation>().Select(i => i.Name).ShouldBe(Indexes, ignoreOrder: true);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("tenant-a")]
        public async Task The_indexes_exist_with_trigram_operators_on_the_migrated_databases(string? tenant)
        {
            await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
            {
                var context = await GetRequiredService<IDbContextProvider<IntegrationsDbContext>>().GetDbContextAsync();
                var connection = context.Database.GetDbConnection();
                await using var command = connection.CreateCommand();
                command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
                command.CommandText = "SELECT indexname || ':' || indexdef FROM pg_indexes WHERE schemaname = 'integrations' AND tablename = 'InboundMessages' AND indexname LIKE '%Trigram'";
                var found = new List<string>();
                await using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        found.Add(reader.GetString(0));
                    }
                }

                found.Select(f => f.Split(':')[0]).ShouldBe(Indexes, ignoreOrder: true);
                found.ShouldAllBe(f => f.Contains("USING gin") && f.Contains("gin_trgm_ops"));
            });
        }
    }
}


namespace OpenTms.PostgreSql.MasterData
{
    /// <summary>
    /// The MasterDataRelationEmailIndex migration (design 72 build note 5: the (TenantId, Email) index the match query uses):
    /// applied last on the host and a tenant database, nothing pending; Up only creates the index, Down only drops it.
    /// Risk class: High (migration).
    /// </summary>
    [Collection(PostgreSqlTestCollection.Name)]
    public class MasterDataRelationEmailIndexMigrationTests : ReferenceDataPostgreSqlTestBase
    {
        public const string Migration = "20261103111749_MasterDataRelationEmailIndex";

        [Theory]
        [InlineData(null)]
        [InlineData("tenant-a")]
        public async Task The_migration_is_the_newest_and_nothing_is_pending(string? tenant)
        {
            await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
            {
                var context = await GetRequiredService<IDbContextProvider<MasterDataDbContext>>().GetDbContextAsync();
                var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
                applied.Last().ShouldBe(Migration);
                (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
                context.Database.HasPendingModelChanges().ShouldBeFalse();
            });
        }

        [Fact]
        public void The_migration_only_adds_the_index()
        {
            var migration = new MasterDataMigrations.MasterDataRelationEmailIndex();

            var index = migration.UpOperations.ShouldHaveSingleItem().ShouldBeOfType<CreateIndexOperation>();
            index.Table.ShouldBe("Relations");
            index.Columns.ShouldBe(["TenantId", "Email"]);
            migration.DownOperations.ShouldHaveSingleItem().ShouldBeOfType<DropIndexOperation>().Name.ShouldBe(index.Name);
        }
    }
}

namespace OpenTms.PostgreSql.Orders
{
    /// <summary>
    /// The OrdersOrderIntake migration (design 72 "Migration strategy"): applied after OrdersChargedBases and the newest, nothing
    /// pending; Up adds the two tables, the nullable SourceIntakeId and EditedAfterIntake default false on TransportOrders and
    /// indexes only; on a scratch database with data the previous release's INSERT keeps working against the migrated
    /// schema (constitution rule 2) and existing orders read as not from an intake, and Down drops exactly what Up added and
    /// keeps the rows. Risk class: Critical (a migration on every tenant database, on the busiest table).
    /// </summary>
    [Collection(PostgreSqlTestCollection.Name)]
    public class OrdersOrderIntakeMigrationTests : ReferenceDataPostgreSqlTestBase
    {
        public const string Migration = "20261103111745_OrdersOrderIntake";
        private const string PreviousMigration = OrdersChargedBasesMigrationTests.Migration;

        /* Same length as the shared tenant database's postfix, so the name stays within PostgreSQL's 63-char limit. */
        private static readonly string Scratch = PostgreSqlTestDatabases.HostDatabase + "_OrdIntk";

        static OrdersOrderIntakeMigrationTests()
        {
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        }

        private static OrdersDbContext CreateContext()
            => new(new DbContextOptionsBuilder<OrdersDbContext>()
                .UseNpgsql(PostgreSqlTestDatabases.ConnectionStringFor(Scratch), b => b.MigrationsHistoryTable("__EFMigrationsHistory", OrdersDbProperties.DbSchema))
                .Options);

        private static async Task MigrateToAsync(string target)
        {
            await using var context = CreateContext();
            await context.GetService<IMigrator>().MigrateAsync(target);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("tenant-a")]
        public async Task The_migration_follows_the_charged_bases_and_nothing_is_pending(string? tenant)
        {
            await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
            {
                var context = await GetRequiredService<IDbContextProvider<OrdersDbContext>>().GetDbContextAsync();
                var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
                applied.IndexOf(Migration).ShouldBe(applied.IndexOf(PreviousMigration) + 1);
                /* No longer the newest: build note 28 added OrdersIntakeNameSimilarityThreshold right after it. */
                applied.IndexOf(OrdersIntakeNameSimilarityThresholdMigrationTests.Migration).ShouldBe(applied.IndexOf(Migration) + 1);
                applied.Last().ShouldBe(OrdersIntakeNameSimilarityThresholdMigrationTests.Migration);
                (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
                context.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot must describe the model that runs");
            });
        }

        [Fact]
        public void The_migration_is_expand_only()
        {
            var migration = new OrdersMigrations.OrdersOrderIntake();

            migration.UpOperations.ShouldAllBe(o => o is AddColumnOperation || o is CreateTableOperation || o is CreateIndexOperation || o is EnsureSchemaOperation);
            migration.UpOperations.OfType<CreateTableOperation>().Select(t => t.Name).ShouldBe(["IntakeAliases", "OrderIntakes"], ignoreOrder: true);
            var columns = migration.UpOperations.OfType<AddColumnOperation>().ToDictionary(c => c.Name);
            columns.Keys.ShouldBe(["EditedAfterIntake", "SourceIntakeId"], ignoreOrder: true);
            columns.Values.ShouldAllBe(c => c.Table == "TransportOrders");
            (columns["EditedAfterIntake"].IsNullable, columns["EditedAfterIntake"].DefaultValue).ShouldBe((false, (object)false));
            columns["SourceIntakeId"].IsNullable.ShouldBeTrue();
            var indexes = migration.UpOperations.OfType<CreateIndexOperation>().ToDictionary(i => i.Name);
            indexes["IX_OrderIntakes_TenantId_SourceDocumentId_IntakeIndex"].IsUnique.ShouldBeTrue("Orders' idempotency key");
            var alias = indexes["IX_IntakeAliases_TenantId_Kind_ScopeRelationId_NormalizedText"];
            alias.IsUnique.ShouldBeTrue();
            alias["Npgsql:NullsDistinct"].ShouldBe(false, "a tenant-wide alias (scope null) is unique too");
        }

        private static async Task<object?> ScalarAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(Scratch));
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            return await command.ExecuteScalarAsync();
        }

        [Fact]
        public async Task Existing_orders_are_not_from_an_intake_the_previous_releases_insert_keeps_working_and_Down_keeps_the_rows()
        {
            await ChargedBasesMigrationRows.RecreateAsync(Scratch, create: true);
            try
            {
                await MigrateToAsync(PreviousMigration);
                var before = await ChargedBasesMigrationRows.InsertAsync(Scratch, OrdersDbProperties.DbSchema, "TransportOrders");

                await MigrateToAsync(Migration);

                (await ChargedBasesMigrationRows.ColumnAsync(Scratch, OrdersDbProperties.DbSchema, "TransportOrders", "EditedAfterIntake"))!.ShouldStartWith("boolean:NO");
                (await ChargedBasesMigrationRows.ColumnAsync(Scratch, OrdersDbProperties.DbSchema, "TransportOrders", "SourceIntakeId"))!.ShouldStartWith("uuid:YES");
                (await ScalarAsync($"SELECT \"EditedAfterIntake\" FROM orders.\"TransportOrders\" WHERE \"Id\" = '{before}'")).ShouldBe(false);
                (await ScalarAsync($"SELECT \"SourceIntakeId\" FROM orders.\"TransportOrders\" WHERE \"Id\" = '{before}'")).ShouldBe(DBNull.Value);

                /* The previous release keeps writing orders without the new columns. */
                var oldWrite = await ChargedBasesMigrationRows.InsertAsync(Scratch, OrdersDbProperties.DbSchema, "TransportOrders");
                (await ScalarAsync($"SELECT \"EditedAfterIntake\" FROM orders.\"TransportOrders\" WHERE \"Id\" = '{oldWrite}'")).ShouldBe(false);

                await MigrateToAsync(PreviousMigration);

                (await ChargedBasesMigrationRows.ColumnAsync(Scratch, OrdersDbProperties.DbSchema, "TransportOrders", "SourceIntakeId")).ShouldBeNull("Down drops the column");
                (await ChargedBasesMigrationRows.ColumnAsync(Scratch, OrdersDbProperties.DbSchema, "TransportOrders", "EditedAfterIntake")).ShouldBeNull();
                (await ScalarAsync("SELECT to_regclass('orders.\"OrderIntakes\"')::text")).ShouldBe(DBNull.Value, "Down drops the table");
                (await ChargedBasesMigrationRows.CountAsync(Scratch, OrdersDbProperties.DbSchema, "TransportOrders")).ShouldBe(2L, "Down keeps the rows");
            }
            finally
            {
                await ChargedBasesMigrationRows.RecreateAsync(Scratch, create: false);
            }
        }
    }

    /// <summary>
    /// The OrdersIntakeNameSimilarityThreshold migration (design 72 "Migration strategy", build note 28): the newest Orders
    /// migration, right after OrdersOrderIntake, nothing pending; Up adds only the nullable numeric(3,2) column
    /// <c>OrderIntakes.NameSimilarityThreshold</c>; on a scratch database with data existing intakes read null (the default
    /// threshold), the previous release's INSERT keeps working against the migrated schema (constitution rule 2), and Down
    /// drops exactly the column and keeps the rows. Risk class: Critical (a migration on every tenant database).
    /// </summary>
    [Collection(PostgreSqlTestCollection.Name)]
    public class OrdersIntakeNameSimilarityThresholdMigrationTests : ReferenceDataPostgreSqlTestBase
    {
        public const string Migration = "20261103180718_OrdersIntakeNameSimilarityThreshold";

        /* Same length as the shared tenant database's postfix, so the name stays within PostgreSQL's 63-char limit. */
        private static readonly string Scratch = PostgreSqlTestDatabases.HostDatabase + "_OrdNST";

        static OrdersIntakeNameSimilarityThresholdMigrationTests()
        {
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        }

        private static async Task MigrateToAsync(string target)
        {
            await using var context = new OrdersDbContext(new DbContextOptionsBuilder<OrdersDbContext>()
                .UseNpgsql(PostgreSqlTestDatabases.ConnectionStringFor(Scratch), b => b.MigrationsHistoryTable("__EFMigrationsHistory", OrdersDbProperties.DbSchema))
                .Options);
            await context.GetService<IMigrator>().MigrateAsync(target);
        }

        private static async Task<object?> ScalarAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(Scratch));
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            return await command.ExecuteScalarAsync();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("tenant-a")]
        public async Task The_migration_follows_the_order_intake_is_the_newest_and_nothing_is_pending(string? tenant)
        {
            await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
            {
                var context = await GetRequiredService<IDbContextProvider<OrdersDbContext>>().GetDbContextAsync();
                var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
                applied.IndexOf(Migration).ShouldBe(applied.IndexOf(OrdersOrderIntakeMigrationTests.Migration) + 1);
                applied.Last().ShouldBe(Migration);
                (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
                context.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot must describe the model that runs");
            });
        }

        [Fact]
        public void The_migration_is_expand_only_and_Down_drops_exactly_the_column()
        {
            var migration = new OrdersMigrations.OrdersIntakeNameSimilarityThreshold();

            var column = migration.UpOperations.ShouldHaveSingleItem().ShouldBeOfType<AddColumnOperation>();
            (column.Schema, column.Table, column.Name).ShouldBe(("orders", "OrderIntakes", "NameSimilarityThreshold"));
            column.IsNullable.ShouldBeTrue("null is the default threshold: no backfill, old writers keep working");
            column.ColumnType.ShouldBe("numeric(3,2)");
            column.DefaultValue.ShouldBeNull();

            var drop = migration.DownOperations.ShouldHaveSingleItem().ShouldBeOfType<DropColumnOperation>();
            (drop.Schema, drop.Table, drop.Name).ShouldBe(("orders", "OrderIntakes", "NameSimilarityThreshold"));
        }

        [Fact]
        public async Task Existing_intakes_take_the_default_the_previous_releases_insert_keeps_working_and_Down_keeps_the_rows()
        {
            await ChargedBasesMigrationRows.RecreateAsync(Scratch, create: true);
            try
            {
                await MigrateToAsync(OrdersOrderIntakeMigrationTests.Migration);
                var before = await ChargedBasesMigrationRows.InsertAsync(Scratch, OrdersDbProperties.DbSchema, "OrderIntakes");

                await MigrateToAsync(Migration);

                (await ChargedBasesMigrationRows.ColumnAsync(Scratch, OrdersDbProperties.DbSchema, "OrderIntakes", "NameSimilarityThreshold"))!.ShouldStartWith("numeric:YES");
                (await ScalarAsync("SELECT numeric_precision || ',' || numeric_scale FROM information_schema.columns WHERE table_schema = 'orders' AND table_name = 'OrderIntakes' AND column_name = 'NameSimilarityThreshold'"))
                    .ShouldBe("3,2");
                (await ScalarAsync($"SELECT \"NameSimilarityThreshold\" FROM orders.\"OrderIntakes\" WHERE \"Id\" = '{before}'")).ShouldBe(DBNull.Value, "an existing intake resolves with the default");

                /* The previous release keeps writing intakes without the new column. */
                var oldWrite = await ChargedBasesMigrationRows.InsertAsync(Scratch, OrdersDbProperties.DbSchema, "OrderIntakes");
                (await ScalarAsync($"SELECT \"NameSimilarityThreshold\" FROM orders.\"OrderIntakes\" WHERE \"Id\" = '{oldWrite}'")).ShouldBe(DBNull.Value);
                /* The new release writes two decimals. */
                var newWrite = await ChargedBasesMigrationRows.InsertAsync(Scratch, OrdersDbProperties.DbSchema, "OrderIntakes",
                    new Dictionary<string, object> { ["NameSimilarityThreshold"] = 0.8m });
                (await ScalarAsync($"SELECT \"NameSimilarityThreshold\" FROM orders.\"OrderIntakes\" WHERE \"Id\" = '{newWrite}'")).ShouldBe(0.80m);

                await MigrateToAsync(OrdersOrderIntakeMigrationTests.Migration);

                (await ChargedBasesMigrationRows.ColumnAsync(Scratch, OrdersDbProperties.DbSchema, "OrderIntakes", "NameSimilarityThreshold")).ShouldBeNull("Down drops the column");
                (await ChargedBasesMigrationRows.CountAsync(Scratch, OrdersDbProperties.DbSchema, "OrderIntakes")).ShouldBe(3L, "Down keeps the rows");
            }
            finally
            {
                await ChargedBasesMigrationRows.RecreateAsync(Scratch, create: false);
            }
        }
    }
}
