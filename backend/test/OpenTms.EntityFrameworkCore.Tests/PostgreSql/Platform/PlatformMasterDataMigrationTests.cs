using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using OpenTms.Platform.EntityFrameworkCore;
using OpenTms.Platform.EntityFrameworkCore.Migrations;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace OpenTms.PostgreSql.Platform;

/// <summary>
/// The PlatformMasterData migration on the databases the real DbMigrator created (docs/designs/54-platform-master-data.md,
/// "Migration strategy"; test risk analysis "Unique and default indexes, migration", High): applied on the host and on a
/// tenant database after AddDerivedValueStates (the last Platform migration of main when this branch merged), nothing pending, the model snapshot current, the replaced migration
/// 20261016213354 never applied; expand only (new tables and indexes, so the previous application version keeps working on
/// the migrated schema) and Down drops exactly its four tables (no EmailTemplateContents since PO 2026-10-18: one template
/// per language, Culture, Subject and HtmlBody on EmailTemplates, no SenderName); every per-tenant unique index (NULLS NOT DISTINCT, soft-delete filter), the two partial default indexes, the
/// natural-key primary key of the children, the position check, the cascading foreign key and the
/// column types and lengths.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PlatformMasterDataMigrationTests : PlatformMasterDataPostgreSqlTestBase
{
    private const string Migration = "20261025154657_PlatformMasterData";
    private const string ReplacedMigration = "20261016213354_PlatformMasterData";
    private const string PreviousMigration = "20261022234332_AddDerivedValueStates";

    private static readonly string[] Tables =
    [
        "EmailTemplates", "ReportDefinitions", "DocumentTypes", "DocumentTypeTransformations"
    ];

    [Theory]
    [InlineData(null)]
    [InlineData("tenant-a")]
    public async Task The_migration_is_applied_after_the_previous_one_and_nothing_is_pending(string? tenant)
    {
        await InRolledBackTransactionAsync(tenant == null ? null : TenantA, async () =>
        {
            var context = await GetRequiredService<IDbContextProvider<PlatformDbContext>>().GetDbContextAsync();

            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            applied.ShouldContain(Migration);
            applied.ShouldNotContain(ReplacedMigration, "the per-language content migration was replaced before it reached main");
            applied.IndexOf(Migration).ShouldBe(applied.IndexOf(PreviousMigration) + 1);
            applied.Last().ShouldBe(Migration, "the newest Platform migration");
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            context.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot must describe the model that runs");
            context.Database.GetDbConnection().Database.ShouldBe(tenant == null ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase);
        });
    }

    [Fact]
    public void The_migration_is_expand_only_and_its_Down_drops_exactly_its_own_tables()
    {
        var migration = new PlatformMasterData();

        var up = migration.UpOperations;
        up.ShouldAllBe(o => o is CreateTableOperation || o is CreateIndexOperation, string.Join(", ", up.Select(o => o.GetType().Name).Distinct()));
        up.OfType<CreateTableOperation>().Select(t => t.Name).ShouldBe(Tables, ignoreOrder: true);
        up.OfType<CreateTableOperation>().ShouldAllBe(t => t.Schema == "platform");
        up.OfType<CreateIndexOperation>().Select(i => i.Table).Distinct().ShouldAllBe(t => Tables.Contains(t), "indexes only on the new tables");

        var down = migration.DownOperations;
        down.ShouldAllBe(o => o is DropTableOperation);
        down.OfType<DropTableOperation>().Select(t => t.Name).ShouldBe(Tables, ignoreOrder: true);
        down.OfType<DropTableOperation>().ShouldAllBe(t => t.Schema == "platform");
    }

    [Theory]
    [InlineData("host")]
    [InlineData("tenant-a")]
    public async Task Every_table_index_check_and_foreign_key_is_in_place(string target)
    {
        var database = target == "host" ? PostgreSqlTestDatabases.HostDatabase : PostgreSqlTestDatabases.TenantADatabase;
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();

        var tables = await QueryAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'platform'");
        foreach (var table in Tables)
        {
            tables.ShouldContain(table);
        }

        var indexes = await QueryAsync(connection, "SELECT indexname || ' :: ' || indexdef FROM pg_indexes WHERE schemaname = 'platform'");

        foreach (var (name, keyColumns, where) in new[]
                 {
                     ("IX_EmailTemplates_TenantId_NormalizedName", "(\"TenantId\", \"NormalizedName\")", "(\"IsDeleted\" = false)"),
                     ("IX_ReportDefinitions_TenantId_NormalizedName", "(\"TenantId\", \"NormalizedName\")", "(\"IsDeleted\" = false)"),
                     ("IX_DocumentTypes_TenantId_Code", "(\"TenantId\", \"Code\")", "(\"IsDeleted\" = false)"),
                     ("IX_EmailTemplates_TenantId_Purpose_Default", "(\"TenantId\", \"Purpose\")", "\"IsDefault\" = true"),
                     ("IX_ReportDefinitions_TenantId_Purpose_Culture_Default", "(\"TenantId\", \"Purpose\", \"Culture\")", "\"IsDefault\" = true"),
                 })
        {
            var index = One(indexes, name + " :: ");
            index.ShouldContain("UNIQUE", Case.Sensitive, name);
            index.ShouldContain("NULLS NOT DISTINCT", Case.Sensitive, name);
            index.ShouldContain(keyColumns, Case.Sensitive, name);
            index.ShouldContain(where, Case.Sensitive, name);
            index.ShouldContain("\"IsDeleted\" = false", Case.Sensitive, name);
        }

        indexes.ShouldNotContain(i => i.Contains("EmailTemplateContents", StringComparison.Ordinal));
        One(indexes, "PK_EmailTemplates :: ").ShouldContain("(\"Id\")");
        One(indexes, "PK_DocumentTypeTransformations :: ").ShouldContain("(\"DocumentTypeId\", \"Kind\")");

        var checks = await QueryAsync(connection, "SELECT conname || ' :: ' || pg_get_constraintdef(oid) FROM pg_constraint WHERE contype = 'c' AND connamespace = 'platform'::regnamespace");
        var position = One(checks, "CK_DocumentTypeTransformations_Position :: ");
        position.ShouldContain("\"Position\" >= 1");
        position.ShouldContain("\"Position\" <= 4");

        var foreignKeys = await QueryAsync(connection, "SELECT conname || ' :: ' || confdeltype::text FROM pg_constraint WHERE contype = 'f' AND connamespace = 'platform'::regnamespace");
        foreignKeys.ShouldNotContain(f => f.Contains("EmailTemplate", StringComparison.Ordinal), "an e-mail template has no child table");
        foreignKeys.ShouldContain("FK_DocumentTypeTransformations_DocumentTypes_DocumentTypeId :: c");

        var columns = await QueryAsync(connection,
            "SELECT table_name || '.' || column_name || ' :: ' || data_type || '(' || COALESCE(character_maximum_length::text, '-') || ') :: ' || is_nullable " +
            "FROM information_schema.columns WHERE table_schema = 'platform' AND table_name IN ('EmailTemplates', 'ReportDefinitions', 'DocumentTypes', 'DocumentTypeTransformations')");
        foreach (var expected in new[]
                 {
                     "EmailTemplates.Name :: character varying(128) :: NO",
                     "EmailTemplates.NormalizedName :: character varying(128) :: NO",
                     "EmailTemplates.TenantId :: uuid(-) :: YES",
                     "EmailTemplates.SenderAddress :: character varying(512) :: YES",
                     "EmailTemplates.Bcc :: character varying(512) :: YES",
                     "EmailTemplates.Description :: character varying(2500) :: YES",
                     "EmailTemplates.Purpose :: integer(-) :: NO",
                     "EmailTemplates.Culture :: character varying(10) :: YES",
                     "EmailTemplates.IsDefault :: boolean(-) :: NO",
                     "EmailTemplates.ReplyTo :: character varying(512) :: YES",
                     "EmailTemplates.To :: character varying(512) :: YES",
                     "EmailTemplates.Cc :: character varying(512) :: YES",
                     "EmailTemplates.SendCopyToSender :: boolean(-) :: NO",
                     "EmailTemplates.Subject :: character varying(512) :: NO",
                     "EmailTemplates.HtmlBody :: text(-) :: NO",
                     "ReportDefinitions.Culture :: character varying(10) :: NO",
                     "ReportDefinitions.ProviderTemplateId :: character varying(128) :: NO",
                     "ReportDefinitions.FileNamePattern :: character varying(255) :: NO",
                     "DocumentTypes.Code :: character varying(32) :: NO",
                     "DocumentTypes.Name :: jsonb(-) :: NO",
                     "DocumentTypeTransformations.Argument :: character varying(255) :: YES",
                 })
        {
            columns.ShouldContain(expected);
        }

        /* The e-mail template's exact column set: the mail fields on the template, no sender name (OpenTMS's
         * SenderDisplayName had no input; dropped, design 54 "Deliberate differences"). */
        columns.Where(c => c.StartsWith("EmailTemplates.", StringComparison.Ordinal)).Select(c => c.Split(" :: ")[0]["EmailTemplates.".Length..]).ShouldBe(
        [
            "Id", "Name", "NormalizedName", "Purpose", "Culture", "IsDefault", "SenderAddress", "ReplyTo", "To", "Cc", "Bcc", "SendCopyToSender",
            "Subject", "HtmlBody", "Description", "ExtraProperties", "ConcurrencyStamp", "CreationTime", "CreatorId", "LastModificationTime",
            "LastModifierId", "IsDeleted", "DeleterId", "DeletionTime", "TenantId"
        ], ignoreOrder: true);
        tables.ShouldNotContain("EmailTemplateContents");

        /* AD-06: no jsonb except the document types' LocalizedName. */
        columns.Where(c => c.Contains(":: jsonb(", StringComparison.Ordinal)).ShouldBe(["DocumentTypes.Name :: jsonb(-) :: NO"]);

        /* Earlier Platform tables untouched. */
        tables.ShouldContain("AbpEventOutbox");
        tables.ShouldContain("DataProtectionKeys");
    }

    private static string One(List<string> rows, string prefix)
    {
        rows.ShouldContain(r => r.StartsWith(prefix, StringComparison.Ordinal), prefix);
        return rows.Single(r => r.StartsWith(prefix, StringComparison.Ordinal));
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
