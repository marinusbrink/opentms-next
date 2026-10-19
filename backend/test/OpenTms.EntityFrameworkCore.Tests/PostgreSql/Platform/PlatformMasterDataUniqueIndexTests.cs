using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTms.Platform;
using OpenTms.Platform.DocumentTypes;
using OpenTms.Platform.EmailTemplates;
using OpenTms.Platform.EntityFrameworkCore;
using OpenTms.Platform.Localization;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.ReportDefinitions;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace OpenTms.PostgreSql.Platform;

/// <summary>
/// The unique, partial unique and check constraints of the PlatformMasterData migration, exercised through the
/// repositories (and plain SQL for the child keys) so the services' friendly checks are bypassed
/// (docs/designs/49-platform-master-data.md, test risk analysis "Unique and default indexes, migration", High): names
/// unique per tenant regardless of case and freed by a soft delete, the document-type code unique, one default per purpose
/// (templates, regardless of their language) and per purpose and language (report definitions), unique (type, kind) and
/// (sequence, year, month), NULLS NOT DISTINCT so host rows are unique too, two tenants of one database each with their
/// own key space, the position and minimum-digits checks, the cascades, the e-mail template's NOT NULL subject and body
/// and ten-character culture. A violation aborts the PostgreSQL transaction,
/// so every one is the last statement of its own rolled-back transaction. Also the services' default switch on the real
/// partial index.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PlatformMasterDataUniqueIndexTests : PlatformMasterDataPostgreSqlTestBase
{
    private static readonly string[] Cultures = ["nl", "en"];

    private IRepository<EmailTemplate, Guid> Templates => GetRequiredService<IRepository<EmailTemplate, Guid>>();
    private IRepository<ReportDefinition, Guid> Definitions => GetRequiredService<IRepository<ReportDefinition, Guid>>();
    private IRepository<DocumentType, Guid> Types => GetRequiredService<IRepository<DocumentType, Guid>>();
    private IRepository<NumberSequence, Guid> Sequences => GetRequiredService<IRepository<NumberSequence, Guid>>();

    private static EmailTemplate Template(Guid? tenant, string name, EmailTemplatePurpose purpose = EmailTemplatePurpose.Invoice, bool isDefault = false, string? culture = "en")
    {
        var template = new EmailTemplate(Guid.NewGuid(), tenant, name, purpose, culture, Cultures, "Subject", "<p>Body</p>");
        template.SetDefault(isDefault);
        return template;
    }

    private static ReportDefinition Definition(Guid? tenant, string name, ReportPurpose purpose = ReportPurpose.Invoice, string culture = "en", bool isDefault = false)
    {
        var definition = new ReportDefinition(Guid.NewGuid(), tenant, name, purpose, culture, Cultures, ReportRenderingProvider.PdfGeneratorApi, "tpl", "file-{{ n }}");
        definition.SetDefault(isDefault);
        return definition;
    }

    private static DocumentType Type(Guid? tenant, string code) => new(Guid.NewGuid(), tenant, code, LocalizedName.Create("en", "QQ " + code));

    private static NumberSequence Sequence(Guid? tenant, string name)
        => new(Guid.NewGuid(), tenant, name, new NumberSequenceFormat(NumberSequenceScheme.CounterOnly, null, null, null, null, null, 4));

    private async Task InsertAsync(Guid? tenant, string kind, string key)
    {
        switch (kind)
        {
            case "template": await Templates.InsertAsync(Template(tenant, key), autoSave: true); break;
            case "definition": await Definitions.InsertAsync(Definition(tenant, key), autoSave: true); break;
            case "sequence": await Sequences.InsertAsync(Sequence(tenant, key), autoSave: true); break;
            case "type": await Types.InsertAsync(Type(tenant, key), autoSave: true); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private async Task SoftDeleteAsync(string kind, string key)
    {
        switch (kind)
        {
            case "template": await Templates.DeleteAsync(t => t.Name == key, autoSave: true); break;
            case "definition": await Definitions.DeleteAsync(d => d.Name == key, autoSave: true); break;
            case "sequence": await Sequences.DeleteAsync(s => s.Name == key, autoSave: true); break;
            case "type": await Types.DeleteAsync(t => t.Code == key, autoSave: true); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static async Task ShouldViolateAsync(Func<Task> action, string constraint, string sqlState = PostgresErrorCodes.UniqueViolation)
    {
        var ex = await Should.ThrowAsync<Exception>(action);
        var postgres = (ex as PostgresException ?? ex.InnerException as PostgresException).ShouldNotBeNull($"{ex.GetType().Name}: {ex.Message}");
        postgres.SqlState.ShouldBe(sqlState, postgres.MessageText);
        postgres.ConstraintName.ShouldBe(constraint);
    }

    private async Task SqlAsync(string sql, params object[] parameters)
    {
        var context = await GetRequiredService<IDbContextProvider<PlatformDbContext>>().GetDbContextAsync();
        await context.Database.ExecuteSqlRawAsync(sql, parameters);
    }

    // ── Names regardless of case, the code ───────────────────────────────────

    [Theory]
    [InlineData("template", "IX_EmailTemplates_TenantId_NormalizedName")]
    [InlineData("definition", "IX_ReportDefinitions_TenantId_NormalizedName")]
    [InlineData("sequence", "IX_NumberSequences_TenantId_NormalizedName")]
    public async Task A_name_is_unique_per_tenant_regardless_of_case_and_freed_by_a_soft_delete(string kind, string index)
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await InsertAsync(TenantA, kind, "QQ name");
            await ShouldViolateAsync(() => InsertAsync(TenantA, kind, "QQ name"), index);
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await InsertAsync(TenantA, kind, "QQ name");
            await ShouldViolateAsync(() => InsertAsync(TenantA, kind, "qq NAME"), index);
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await InsertAsync(TenantA, kind, "QQ name");
            await SoftDeleteAsync(kind, "QQ name");
            await InsertAsync(TenantA, kind, "qq name");
            await InsertAsync(TenantA, kind, "QQ other name");
        });
    }

    [Fact]
    public async Task The_document_type_code_is_unique_per_tenant_and_freed_by_a_soft_delete()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await InsertAsync(TenantA, "type", "QQCODE");
            await ShouldViolateAsync(() => InsertAsync(TenantA, "type", "qqcode"), "IX_DocumentTypes_TenantId_Code");
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            /* The seeded FREIGHT exists in every tenant: a second one is refused by the index. */
            await ShouldViolateAsync(() => InsertAsync(TenantA, "type", "FREIGHT"), "IX_DocumentTypes_TenantId_Code");
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await InsertAsync(TenantA, "type", "QQCODE");
            await SoftDeleteAsync("type", "QQCODE");
            await InsertAsync(TenantA, "type", "QQCODE");
        });
    }

    [Theory]
    [InlineData("template", "IX_EmailTemplates_TenantId_NormalizedName")]
    [InlineData("definition", "IX_ReportDefinitions_TenantId_NormalizedName")]
    [InlineData("sequence", "IX_NumberSequences_TenantId_NormalizedName")]
    [InlineData("type", "IX_DocumentTypes_TenantId_Code")]
    public async Task Host_rows_are_unique_too_and_the_host_and_the_shared_tenant_have_their_own_key_space(string kind, string index)
    {
        var key = kind == "type" ? "QQHOST" : "QQ host";

        /* The shared tenant and the host share the host database: the same key twice, once per tenant. */
        await InRolledBackTransactionAsync(SharedTenant, async () =>
        {
            await InsertAsync(SharedTenant, kind, key);
            using (CurrentTenant.Change(null))
            {
                await InsertAsync(null, kind, key);
            }
        });

        /* NULLS NOT DISTINCT: two host rows with the same key collide. */
        await InRolledBackTransactionAsync(null, async () =>
        {
            await InsertAsync(null, kind, key);
            await ShouldViolateAsync(() => InsertAsync(null, kind, key), index);
        });
    }

    // ── One default ──────────────────────────────────────────────────────────

    [Fact]
    public async Task At_most_one_default_template_per_purpose()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await Templates.InsertAsync(Template(TenantA, "QQ A", isDefault: true), autoSave: true);
            await Templates.InsertAsync(Template(TenantA, "QQ B"), autoSave: true);
            await Templates.InsertAsync(Template(TenantA, "QQ C"), autoSave: true);
            await Templates.InsertAsync(Template(TenantA, "QQ order", EmailTemplatePurpose.TransportOrder, isDefault: true), autoSave: true);

            /* A soft-deleted default no longer counts. */
            var deleted = Template(TenantA, "QQ deleted", EmailTemplatePurpose.PurchaseOrder, isDefault: true);
            await Templates.InsertAsync(deleted, autoSave: true);
            await Templates.DeleteAsync(deleted, autoSave: true);
            await Templates.InsertAsync(Template(TenantA, "QQ purchase", EmailTemplatePurpose.PurchaseOrder, isDefault: true), autoSave: true);

            await ShouldViolateAsync(() => Templates.InsertAsync(Template(TenantA, "QQ D", isDefault: true), autoSave: true), "IX_EmailTemplates_TenantId_Purpose_Default");
        });

        /* One template per language, but one default per purpose: a Dutch and an all-languages default collide with the
         * English one (design 49: at most one default per purpose). */
        foreach (var culture in new[] { "nl", null })
        {
            await InRolledBackTransactionAsync(TenantA, async () =>
            {
                await Templates.InsertAsync(Template(TenantA, "QQ EN default", isDefault: true, culture: "en"), autoSave: true);
                await Templates.InsertAsync(Template(TenantA, "QQ other language", isDefault: false, culture: culture), autoSave: true);
                await ShouldViolateAsync(() => Templates.InsertAsync(Template(TenantA, "QQ other default", isDefault: true, culture: culture), autoSave: true),
                    "IX_EmailTemplates_TenantId_Purpose_Default");
            });
        }

        /* Host rows: NULLS NOT DISTINCT applies to the default index as well; the shared tenant's default is its own. */
        await InRolledBackTransactionAsync(null, async () =>
        {
            await Templates.InsertAsync(Template(null, "QQ host A", isDefault: true), autoSave: true);
            using (CurrentTenant.Change(SharedTenant))
            {
                await Templates.InsertAsync(Template(SharedTenant, "QQ shared A", isDefault: true), autoSave: true);
            }

            await ShouldViolateAsync(() => Templates.InsertAsync(Template(null, "QQ host B", isDefault: true), autoSave: true), "IX_EmailTemplates_TenantId_Purpose_Default");
        });
    }

    [Fact]
    public async Task At_most_one_default_report_definition_per_purpose_and_language()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await Definitions.InsertAsync(Definition(TenantA, "QQ EN", isDefault: true), autoSave: true);
            await Definitions.InsertAsync(Definition(TenantA, "QQ NL", culture: "nl", isDefault: true), autoSave: true);
            await Definitions.InsertAsync(Definition(TenantA, "QQ order EN", ReportPurpose.PurchaseOrder, isDefault: true), autoSave: true);
            await Definitions.InsertAsync(Definition(TenantA, "QQ EN 2"), autoSave: true);

            await ShouldViolateAsync(() => Definitions.InsertAsync(Definition(TenantA, "QQ EN 3", isDefault: true), autoSave: true), "IX_ReportDefinitions_TenantId_Purpose_Culture_Default");
        });

        await InRolledBackTransactionAsync(null, async () =>
        {
            await Definitions.InsertAsync(Definition(null, "QQ host EN", isDefault: true), autoSave: true);
            await ShouldViolateAsync(() => Definitions.InsertAsync(Definition(null, "QQ host EN 2", isDefault: true), autoSave: true), "IX_ReportDefinitions_TenantId_Purpose_Culture_Default");
        });
    }

    [Fact]
    public async Task The_services_switch_the_default_without_tripping_the_partial_indexes()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var templates = GetRequiredService<IEmailTemplateAppService>();
            EmailTemplateCreateDto TemplateDto(string name, string? culture) => new()
            {
                Name = name, Purpose = EmailTemplatePurpose.Invoice, Culture = culture, IsDefault = true, Subject = "s", HtmlBody = "b"
            };

            /* Different languages, one default per purpose: the switch clears across languages on the real index. */
            var a = await templates.CreateAsync(TemplateDto("QQ A", "nl"));
            var b = await templates.CreateAsync(TemplateDto("QQ B", "en"));
            (await templates.GetAsync(a.Id)).IsDefault.ShouldBeFalse();
            a = await templates.GetAsync(a.Id);
            var update = new EmailTemplateUpdateDto
            {
                Name = a.Name, Purpose = a.Purpose, Culture = a.Culture, IsDefault = true, Subject = a.Subject, HtmlBody = a.HtmlBody, ConcurrencyStamp = a.ConcurrencyStamp
            };
            (await templates.UpdateAsync(a.Id, update)).IsDefault.ShouldBeTrue();
            (await templates.GetAsync(b.Id)).IsDefault.ShouldBeFalse();

            var definitions = GetRequiredService<IReportDefinitionAppService>();
            ReportDefinitionCreateDto DefinitionDto(string name, string culture) => new()
            {
                Name = name, Purpose = ReportPurpose.Invoice, Culture = culture, IsDefault = true, ProviderTemplateId = "t", FileNamePattern = "f"
            };

            var en1 = await definitions.CreateAsync(DefinitionDto("QQ EN 1", "en"));
            var nl1 = await definitions.CreateAsync(DefinitionDto("QQ NL 1", "nl"));
            var en2 = await definitions.CreateAsync(DefinitionDto("QQ EN 2", "en"));
            (await definitions.GetAsync(en1.Id)).IsDefault.ShouldBeFalse();
            (await definitions.GetAsync(nl1.Id)).IsDefault.ShouldBeTrue();
            (await definitions.GetAsync(en2.Id)).IsDefault.ShouldBeTrue();
            await FlushAsync();
        });
    }

    // ── Child keys, checks, cascades ─────────────────────────────────────────

    [Fact]
    public async Task The_email_template_subject_and_body_are_not_null_and_the_culture_is_at_most_ten_characters_in_the_database()
    {
        foreach (var (column, value, sqlState) in new (string, string?, string)[]
                 {
                     ("Subject", null, PostgresErrorCodes.NotNullViolation),
                     ("HtmlBody", null, PostgresErrorCodes.NotNullViolation),
                     ("Subject", new string('s', PlatformMasterDataConsts.MaxSubjectLength + 1), PostgresErrorCodes.StringDataRightTruncation),
                     ("Culture", "nl-NL-extra", PostgresErrorCodes.StringDataRightTruncation),
                 })
        {
            await InRolledBackTransactionAsync(TenantA, async () =>
            {
                var template = Template(TenantA, "QQ columns");
                await Templates.InsertAsync(template, autoSave: true);
                var ex = await Should.ThrowAsync<Exception>(() => value == null
                    ? SqlAsync($"UPDATE platform.\"EmailTemplates\" SET \"{column}\" = NULL WHERE \"Id\" = {{0}}", template.Id)
                    : SqlAsync($"UPDATE platform.\"EmailTemplates\" SET \"{column}\" = {{0}} WHERE \"Id\" = {{1}}", value, template.Id));
                var postgres = (ex as PostgresException ?? ex.InnerException as PostgresException).ShouldNotBeNull($"{ex.GetType().Name}: {ex.Message}");
                postgres.SqlState.ShouldBe(sqlState, $"{column}: {postgres.MessageText}");
            });
        }

        /* A body of 200,000 characters and an all-languages template are stored as they are. */
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var body = new string('b', PlatformMasterDataConsts.MaxHtmlBodyLength);
            var template = new EmailTemplate(Guid.NewGuid(), TenantA, "QQ big", EmailTemplatePurpose.Invoice, null, Cultures, "s", body);
            await Templates.InsertAsync(template, autoSave: true);
            var context = await GetRequiredService<IDbContextProvider<PlatformDbContext>>().GetDbContextAsync();
            context.ChangeTracker.Clear();
            var read = await Templates.GetAsync(template.Id);
            (read.Culture, read.HtmlBody.Length).ShouldBe(((string?)null, 200_000));
        });
    }

    [Fact]
    public async Task One_transformation_per_document_type_and_kind_at_positions_one_to_four()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var type = Type(TenantA, "QQKIND");
            type.SetTransformations([(DocumentTransformationKind.ImageToPdf, null)]);
            await Types.InsertAsync(type, autoSave: true);
            await ShouldViolateAsync(() => SqlAsync(
                "INSERT INTO platform.\"DocumentTypeTransformations\" (\"DocumentTypeId\", \"Kind\", \"TenantId\", \"Position\") VALUES ({0}, {1}, {2}, 2)",
                type.Id, (int)DocumentTransformationKind.ImageToPdf, TenantA), "PK_DocumentTypeTransformations");
        });

        foreach (var position in new[] { 0, 5 })
        {
            await InRolledBackTransactionAsync(TenantA, async () =>
            {
                var type = Type(TenantA, "QQPOS");
                await Types.InsertAsync(type, autoSave: true);
                await ShouldViolateAsync(() => SqlAsync(
                    "INSERT INTO platform.\"DocumentTypeTransformations\" (\"DocumentTypeId\", \"Kind\", \"TenantId\", \"Position\") VALUES ({0}, 1, {1}, {2})",
                    type.Id, TenantA, position), "CK_DocumentTypeTransformations_Position", PostgresErrorCodes.CheckViolation);
            });
        }
    }

    [Fact]
    public async Task One_counter_row_per_sequence_and_period_and_minimum_digits_between_1_and_12()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var sequence = Sequence(TenantA, "QQ counters");
            await Sequences.InsertAsync(sequence, autoSave: true);
            await SqlAsync("INSERT INTO platform.\"NumberSequenceCounters\" (\"NumberSequenceId\", \"PeriodYear\", \"PeriodMonth\", \"TenantId\", \"LastValue\") VALUES ({0}, 2026, 0, {1}, 1)", sequence.Id, TenantA);
            await SqlAsync("INSERT INTO platform.\"NumberSequenceCounters\" (\"NumberSequenceId\", \"PeriodYear\", \"PeriodMonth\", \"TenantId\", \"LastValue\") VALUES ({0}, 2026, 1, {1}, 1)", sequence.Id, TenantA);
            await ShouldViolateAsync(() => SqlAsync(
                "INSERT INTO platform.\"NumberSequenceCounters\" (\"NumberSequenceId\", \"PeriodYear\", \"PeriodMonth\", \"TenantId\", \"LastValue\") VALUES ({0}, 2026, 0, {1}, 7)",
                sequence.Id, TenantA), "PK_NumberSequenceCounters");
        });

        foreach (var digits in new[] { 0, 13 })
        {
            await InRolledBackTransactionAsync(TenantA, async () =>
            {
                var sequence = Sequence(TenantA, "QQ digits");
                await Sequences.InsertAsync(sequence, autoSave: true);
                await ShouldViolateAsync(() => SqlAsync("UPDATE platform.\"NumberSequences\" SET \"MinimumDigits\" = {0} WHERE \"Id\" = {1}", digits, sequence.Id),
                    "CK_NumberSequences_MinimumDigits", PostgresErrorCodes.CheckViolation);
            });
        }

        /* A counter without its sequence is refused by the foreign key. */
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await ShouldViolateAsync(() => SqlAsync(
                "INSERT INTO platform.\"NumberSequenceCounters\" (\"NumberSequenceId\", \"PeriodYear\", \"PeriodMonth\", \"TenantId\", \"LastValue\") VALUES ({0}, 0, 0, {1}, 1)",
                Guid.NewGuid(), TenantA), "FK_NumberSequenceCounters_NumberSequences_NumberSequenceId", PostgresErrorCodes.ForeignKeyViolation);
        });
    }

    [Fact]
    public async Task A_refused_email_template_update_changes_nothing_on_the_production_stack()
    {
        /* Every call in its own transactional unit of work, as a request: the refusal (UnsupportedCulture, raised after the
         * name was already set on the tracked entity) must roll the whole update back. The SQLite harness runs without
         * transactions, so this is only provable here. */
        var templates = GetRequiredService<IEmailTemplateAppService>();
        EmailTemplateDto created;
        using (CurrentTenant.Change(TenantA))
        {
            created = await templates.CreateAsync(new EmailTemplateCreateDto
            {
                Name = "QQ refused " + Guid.NewGuid().ToString("N"), Purpose = EmailTemplatePurpose.Invoice, Culture = "en", Subject = "s", HtmlBody = "<p>b</p>"
            });
        }

        try
        {
            using (CurrentTenant.Change(TenantA))
            {
                var ex = await Should.ThrowAsync<Volo.Abp.BusinessException>(() => templates.UpdateAsync(created.Id, new EmailTemplateUpdateDto
                {
                    Name = "QQ changed", Purpose = EmailTemplatePurpose.TransportOrder, Culture = "de", Subject = "changed", HtmlBody = "<p>changed</p>",
                    ConcurrencyStamp = created.ConcurrencyStamp
                }));
                ex.Code.ShouldBe(PlatformErrorCodes.UnsupportedCulture);

                var read = await templates.GetAsync(created.Id);
                (read.Name, read.Purpose, read.Culture, read.Subject, read.HtmlBody, read.ConcurrencyStamp)
                    .ShouldBe((created.Name, EmailTemplatePurpose.Invoice, "en", "s", "<p>b</p>", created.ConcurrencyStamp));
            }
        }
        finally
        {
            await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(PostgreSqlTestDatabases.TenantADatabase));
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("DELETE FROM platform.\"EmailTemplates\" WHERE \"Id\" = @id", connection);
            command.Parameters.AddWithValue("id", created.Id);
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task The_children_go_with_a_hard_deleted_parent()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var type = Type(TenantA, "QQCASCADE");
            type.SetTransformations([(DocumentTransformationKind.ImageToPdf, null), (DocumentTransformationKind.LinkByFileName, null)]);
            await Types.InsertAsync(type, autoSave: true);
            var sequence = Sequence(TenantA, "QQ cascade");
            await Sequences.InsertAsync(sequence, autoSave: true);
            await GetRequiredService<INumberGenerator>().GetNextAsync(sequence.Id, new DateOnly(2026, 1, 1));

            await SqlAsync("DELETE FROM platform.\"DocumentTypes\" WHERE \"Id\" = {0}", type.Id);
            await SqlAsync("DELETE FROM platform.\"NumberSequences\" WHERE \"Id\" = {0}", sequence.Id);

            (await GetRequiredService<IRepository<DocumentTypeTransformation>>().CountAsync(t => t.DocumentTypeId == type.Id)).ShouldBe(0);
            (await GetRequiredService<IRepository<NumberSequenceCounter>>().CountAsync(c => c.NumberSequenceId == sequence.Id)).ShouldBe(0);
        });
    }
}
