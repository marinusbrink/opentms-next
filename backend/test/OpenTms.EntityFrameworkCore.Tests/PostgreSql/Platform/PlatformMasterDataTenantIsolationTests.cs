using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Platform;
using OpenTms.Platform.DocumentTypes;
using OpenTms.Platform.EmailTemplates;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.ReportDefinitions;
using Shouldly;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace OpenTms.PostgreSql.Platform;

/// <summary>
/// Tenant isolation of the four Platform master-data lists and their counters against real PostgreSQL
/// (docs/designs/49-platform-master-data.md, test risk analysis "Tenant isolation of all four lists and of the counters",
/// Critical; constitution rule 1): rows written by tenant A (own database) are unreachable from the shared-tier tenant and
/// from the host through every operation (list with and without search, lookup, detail, update, delete, archive,
/// unarchive, both bulk deletes, drawing a number) and their child rows (transformations, counters) are
/// invisible; the shared tenant and the host, which share the host database, each have their own key space, their own
/// default per purpose and their own counters, and never see each other's rows. The services are resolved per call, as a
/// request does.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PlatformMasterDataTenantIsolationTests : PlatformMasterDataPostgreSqlTestBase
{
    private IEmailTemplateAppService Templates => GetRequiredService<IEmailTemplateAppService>();
    private IReportDefinitionAppService Definitions => GetRequiredService<IReportDefinitionAppService>();
    private IDocumentTypeAppService Types => GetRequiredService<IDocumentTypeAppService>();
    private INumberSequenceAppService Sequences => GetRequiredService<INumberSequenceAppService>();
    private INumberGenerator Generator => GetRequiredService<INumberGenerator>();

    private sealed record Probe(EmailTemplateDto Template, ReportDefinitionDto Definition, DocumentTypeDto Type, NumberSequenceDto Sequence, string FirstNumber);

    private static readonly DateOnly Date = new(2026, 10, 17);

    /// <summary>The same names, codes and defaults in whichever tenant is current.</summary>
    private async Task<Probe> WriteProbeAsync()
    {
        var template = await Templates.CreateAsync(new EmailTemplateCreateDto
        {
            Name = "QQ isolation mail", Purpose = EmailTemplatePurpose.Invoice, Culture = "nl", IsDefault = true, Description = "isolation probe",
            SenderAddress = "{{ company.email }}", Subject = "Factuur {{ number }}", HtmlBody = "<p>isolatie</p>"
        });
        var definition = await Definitions.CreateAsync(new ReportDefinitionCreateDto
        {
            Name = "QQ isolation layout", Purpose = ReportPurpose.Invoice, Culture = "en", IsDefault = true, ProviderTemplateId = "qq-1", FileNamePattern = "qq-{{ number }}"
        });
        var type = await Types.CreateAsync(new DocumentTypeCreateDto
        {
            Code = "QQISO", Names = PlatformNames("QQ isolation document", "QQ isolatiedocument"),
            Transformations = [new() { Kind = DocumentTransformationKind.SplitPdfByReferencePattern, Position = 1, Argument = "^QQ" }]
        });
        var sequence = await Sequences.CreateAsync(new NumberSequenceCreateDto { Name = "QQ isolation numbers", Scheme = NumberSequenceScheme.TwoDigitYear, Prefix = "Q-", YearSuffix = "-", MinimumDigits = 4 });
        var first = await Generator.GetNextAsync(sequence.Id, Date);
        await FlushAsync();
        return new Probe(template, definition, type, sequence, first);
    }

    [Fact]
    public async Task Every_list_of_tenant_A_is_unreachable_from_the_shared_tenant_and_the_host()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var a = await WriteProbeAsync();
            a.FirstNumber.ShouldBe("Q-26-0001");

            foreach (var other in new Guid?[] { SharedTenant, null })
            {
                using (CurrentTenant.Change(other))
                {
                    var who = NameFor(other);

                    /* E-mail templates (the list filtered on the purpose and the lookup searched too). */
                    (await Templates.GetListAsync(PlatformList<EmailTemplateListRequestDto>())).TotalCount.ShouldBe(0, who);
                    (await Templates.GetListAsync(PlatformList<EmailTemplateListRequestDto>("isolation"))).Rows.ShouldBeEmpty(who);
                    (await Templates.GetListAsync(new EmailTemplateListRequestDto { Purpose = EmailTemplatePurpose.Invoice, SortBy = "culture" })).TotalCount.ShouldBe(0, who);
                    (await Templates.GetLookupAsync()).ShouldBeEmpty(who);
                    (await Templates.GetLookupAsync("isolation")).ShouldBeEmpty(who);
                    await Should.ThrowAsync<EntityNotFoundException>(() => Templates.GetAsync(a.Template.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Templates.UpdateAsync(a.Template.Id, new EmailTemplateUpdateDto
                    {
                        Name = "Stolen", Purpose = EmailTemplatePurpose.Invoice, Culture = "nl", Subject = "x", HtmlBody = "x", ConcurrencyStamp = a.Template.ConcurrencyStamp
                    }));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Templates.DeleteAsync(a.Template.Id));
                    (await Templates.BulkDeleteAsync(new EmailTemplateBulkDeleteRequestDto { Selection = Explicit(a.Template.Id) })).DeletedCount.ShouldBe(0, who);
                    (await Templates.BulkDeleteAsync(new EmailTemplateBulkDeleteRequestDto { Selection = FilterBased() })).DeletedCount.ShouldBe(0, who);
                    (await GetRequiredService<IRepository<EmailTemplate, Guid>>().CountAsync(t => t.Id == a.Template.Id)).ShouldBe(0, who);

                    /* Report definitions. */
                    (await Definitions.GetListAsync(new ReportDefinitionListRequestDto { IncludeArchived = true })).TotalCount.ShouldBe(0, who);
                    (await Definitions.GetListAsync(new ReportDefinitionListRequestDto { WildcardSearch = "isolation", IncludeArchived = true })).Rows.ShouldBeEmpty(who);
                    (await Definitions.GetLookupAsync()).ShouldBeEmpty(who);
                    await Should.ThrowAsync<EntityNotFoundException>(() => Definitions.GetAsync(a.Definition.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Definitions.UpdateAsync(a.Definition.Id, new ReportDefinitionUpdateDto
                    {
                        Name = "Stolen", Purpose = ReportPurpose.Invoice, Culture = "en", ProviderTemplateId = "x", FileNamePattern = "x", ConcurrencyStamp = a.Definition.ConcurrencyStamp
                    }));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Definitions.ArchiveAsync(a.Definition.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Definitions.UnarchiveAsync(a.Definition.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Definitions.DeleteAsync(a.Definition.Id));
                    (await Definitions.BulkDeleteAsync(new ReportDefinitionBulkDeleteRequestDto { Selection = Explicit(a.Definition.Id) })).DeletedCount.ShouldBe(0, who);
                    (await Definitions.BulkDeleteAsync(new ReportDefinitionBulkDeleteRequestDto { Selection = FilterBased(), IncludeArchived = true })).DeletedCount.ShouldBe(0, who);

                    /* Document types (the shared tenant has its own seeded FREIGHT and GENERAL) and their transformations. */
                    var typeRows = (await Types.GetListAsync(new DocumentTypeListRequestDto { IncludeArchived = true })).Rows;
                    typeRows.ShouldNotContain(r => r.Id == a.Type.Id, who);
                    typeRows.Select(r => r.Code).ShouldBe(other == null ? Array.Empty<string>() : new[] { "FREIGHT", "GENERAL" }, ignoreOrder: true, who);
                    (await Types.GetListAsync(new DocumentTypeListRequestDto { WildcardSearch = "qqiso", IncludeArchived = true })).Rows.ShouldBeEmpty(who);
                    (await Types.GetListAsync(new DocumentTypeListRequestDto { WildcardSearch = "isolation", IncludeArchived = true })).Rows.ShouldBeEmpty(who);
                    (await Types.GetLookupAsync()).ShouldNotContain(l => l.Id == a.Type.Id || l.Code == "QQISO", who);
                    await Should.ThrowAsync<EntityNotFoundException>(() => Types.GetAsync(a.Type.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Types.UpdateAsync(a.Type.Id, new DocumentTypeUpdateDto { Code = "STOLEN", Names = PlatformNames("Stolen"), ConcurrencyStamp = a.Type.ConcurrencyStamp }));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Types.ArchiveAsync(a.Type.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Types.UnarchiveAsync(a.Type.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Types.DeleteAsync(a.Type.Id));
                    (await Types.BulkDeleteAsync(new DocumentTypeBulkDeleteRequestDto { Selection = Explicit(a.Type.Id) })).DeletedCount.ShouldBe(0, who);
                    (await Types.BulkDeleteAsync(new DocumentTypeBulkDeleteRequestDto { Selection = FilterBased("isolation"), IncludeArchived = true })).DeletedCount.ShouldBe(0, who);
                    (await GetRequiredService<IRepository<DocumentTypeTransformation>>().CountAsync(t => t.DocumentTypeId == a.Type.Id)).ShouldBe(0, who);

                    /* Number sequences and their counters. */
                    (await Sequences.GetListAsync(PlatformList<PlatformMasterDataListRequestDto>())).TotalCount.ShouldBe(0, who);
                    (await Sequences.GetLookupAsync()).ShouldBeEmpty(who);
                    await Should.ThrowAsync<EntityNotFoundException>(() => Sequences.GetAsync(a.Sequence.Id));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Sequences.UpdateAsync(a.Sequence.Id, new NumberSequenceUpdateDto
                    {
                        Name = "Stolen", Scheme = NumberSequenceScheme.CounterOnly, MinimumDigits = 4, ConcurrencyStamp = a.Sequence.ConcurrencyStamp
                    }));
                    await Should.ThrowAsync<EntityNotFoundException>(() => Sequences.DeleteAsync(a.Sequence.Id));
                    (await Sequences.BulkDeleteAsync(new PlatformMasterDataBulkDeleteRequestDto { Selection = Explicit(a.Sequence.Id) })).DeletedCount.ShouldBe(0, who);
                    (await Sequences.BulkDeleteAsync(new PlatformMasterDataBulkDeleteRequestDto { Selection = FilterBased() })).DeletedCount.ShouldBe(0, who);
                    await Should.ThrowAsync<EntityNotFoundException>(() => Generator.GetNextAsync(a.Sequence.Id, Date));
                    (await GetRequiredService<IRepository<NumberSequenceCounter>>().CountAsync(c => c.NumberSequenceId == a.Sequence.Id)).ShouldBe(0, who);

                    await FlushAsync();
                }
            }

            /* The shared tenant writes the same names, code and defaults in the host database: its own key space. */
            Probe shared;
            using (CurrentTenant.Change(SharedTenant))
            {
                shared = await WriteProbeAsync();
                shared.FirstNumber.ShouldBe("Q-26-0001", "the shared tenant's counter is its own");
                shared.Template.Id.ShouldNotBe(a.Template.Id);
                shared.Template.IsDefault.ShouldBeTrue();
            }

            /* The host shares the host database with the shared tenant: it sees none of its rows, and its own default and
             * counter leave the shared tenant's alone. */
            using (CurrentTenant.Change(null))
            {
                (await Templates.GetListAsync(PlatformList<EmailTemplateListRequestDto>("isolation"))).Rows.ShouldBeEmpty();
                (await Definitions.GetListAsync(new ReportDefinitionListRequestDto { WildcardSearch = "isolation", IncludeArchived = true })).Rows.ShouldBeEmpty();
                (await Types.GetListAsync(new DocumentTypeListRequestDto { IncludeArchived = true })).TotalCount.ShouldBe(0, "the host has no document types (the seed skips it)");
                (await Sequences.GetLookupAsync()).ShouldBeEmpty();
                await Should.ThrowAsync<EntityNotFoundException>(() => Templates.GetAsync(shared.Template.Id));
                await Should.ThrowAsync<EntityNotFoundException>(() => Generator.GetNextAsync(shared.Sequence.Id, Date));

                var host = await WriteProbeAsync();
                host.FirstNumber.ShouldBe("Q-26-0001");
                host.Template.IsDefault.ShouldBeTrue();
                (await Templates.GetListAsync(PlatformList<EmailTemplateListRequestDto>("isolation"))).Rows.ShouldHaveSingleItem().Id.ShouldBe(host.Template.Id);
            }

            using (CurrentTenant.Change(SharedTenant))
            {
                (await Templates.GetAsync(shared.Template.Id)).IsDefault.ShouldBeTrue("the host's default did not clear the shared tenant's");
                (await Definitions.GetAsync(shared.Definition.Id)).IsDefault.ShouldBeTrue();
                (await Generator.GetNextAsync(shared.Sequence.Id, Date)).ShouldBe("Q-26-0002");
                (await Templates.GetListAsync(PlatformList<EmailTemplateListRequestDto>("isolation"))).Rows.ShouldHaveSingleItem().Id.ShouldBe(shared.Template.Id);
                await FlushAsync();
            }

            /* Tenant A still has all its rows, children and defaults. */
            var template = await Templates.GetAsync(a.Template.Id);
            (template.Name, template.IsDefault, template.Culture, template.Subject, template.HtmlBody)
                .ShouldBe(("QQ isolation mail", true, "nl", "Factuur {{ number }}", "<p>isolatie</p>"), "tenant B's update attempts changed nothing");
            var definition = await Definitions.GetAsync(a.Definition.Id);
            (definition.IsDefault, definition.IsArchived).ShouldBe((true, false));
            (await Types.GetAsync(a.Type.Id)).Transformations.ShouldHaveSingleItem().Argument.ShouldBe("^QQ");
            (await Sequences.GetAsync(a.Sequence.Id)).IsInUse.ShouldBeTrue();
            (await Generator.GetNextAsync(a.Sequence.Id, Date)).ShouldBe("Q-26-0002", "tenant A's counter continues where it was");
            (await Types.GetListAsync(new DocumentTypeListRequestDto { IncludeArchived = true })).Rows.Select(r => r.Code).ShouldBe(["FREIGHT", "GENERAL", "QQISO"], ignoreOrder: true);
        });
    }

    [Fact]
    public async Task The_friendly_duplicate_check_and_the_in_use_check_read_only_the_current_tenants_rows()
    {
        await InRolledBackTransactionAsync(SharedTenant, async () =>
        {
            /* The host writes a sequence with drawn numbers and a template in the host database. */
            Guid hostSequence;
            using (CurrentTenant.Change(null))
            {
                hostSequence = (await Sequences.CreateAsync(new NumberSequenceCreateDto { Name = "QQ same name", Scheme = NumberSequenceScheme.CounterOnly, MinimumDigits = 4 })).Id;
                await Generator.GetNextAsync(hostSequence, Date);
                await Templates.CreateAsync(new EmailTemplateCreateDto { Name = "QQ same name", Purpose = EmailTemplatePurpose.Invoice, Culture = "en", Subject = "s", HtmlBody = "b" });
                await FlushAsync();
            }

            /* The shared tenant, same database: the same names are free, and its own sequence is not in use. */
            var mine = await Sequences.CreateAsync(new NumberSequenceCreateDto { Name = "qq SAME name", Scheme = NumberSequenceScheme.CounterOnly, MinimumDigits = 4 });
            mine.IsInUse.ShouldBeFalse();
            (await Templates.CreateAsync(new EmailTemplateCreateDto { Name = "QQ SAME NAME", Purpose = EmailTemplatePurpose.Invoice, Culture = "en", Subject = "s", HtmlBody = "b" }))
                .Name.ShouldBe("QQ SAME NAME");
            await Sequences.DeleteAsync(mine.Id);
        });
    }
}
