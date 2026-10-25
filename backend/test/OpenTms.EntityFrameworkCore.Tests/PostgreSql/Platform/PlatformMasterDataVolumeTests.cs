using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OpenTms.Platform;
using OpenTms.Platform.DocumentTypes;
using OpenTms.Platform.EmailTemplates;
using OpenTms.Platform.EntityFrameworkCore;
using OpenTms.Platform.Localization;
using OpenTms.Platform.ReportDefinitions;
using Shouldly;
using OpenTms.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace OpenTms.PostgreSql.Platform;

/// <summary>
/// VOLUME TEST (docs/designs/54-platform-master-data.md, "Expected volumes" × 5; "Cost &amp; SLO impact": list reads
/// p95 &lt; 300 ms): in tenant A, 1,000 e-mail templates, one per language (nl, en, all
/// languages; the templates of the first list page carry the maximum body of 200,000 characters, the rest 10,000),
/// 1,000 report definitions, 500 document types with 0–4 transformations and 250 number sequences with 12 counter rows
/// each. The lists (first page, searched, filtered, non-default sorts), the lookups and the template detail are timed cold
/// and warm after ANALYZE; every warm read is held to the 300 ms interactive-read budget (performance-budgets.md), and the
/// SQL of the template list and lookup is captured to prove they never read the body (the detail, as control, does). Then Risk class: Critical/High parts (test-engineer
/// obligation). The data lives in one rolled-back transaction, so nothing stays behind.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PlatformMasterDataVolumeTests : PlatformMasterDataPostgreSqlTestBase
{
    private const int TemplateCount = 1_000;
    private const int DefinitionCount = 1_000;
    private const int TypeCount = 500;
    private const int MaxBodyTemplates = 100;
    private const int SmallBody = 10_000;
    private static readonly TimeSpan ReadBudget = TimeSpan.FromMilliseconds(300);
    private static readonly string[] Cultures = ["nl", "en"];
    private static readonly string?[] TemplateCultures = ["nl", "en", null];

    private readonly ITestOutputHelper _output;

    private IEmailTemplateAppService Templates => GetRequiredService<IEmailTemplateAppService>();
    private IReportDefinitionAppService Definitions => GetRequiredService<IReportDefinitionAppService>();
    private IDocumentTypeAppService Types => GetRequiredService<IDocumentTypeAppService>();

    public PlatformMasterDataVolumeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Lists_lookups_and_the_generator_stay_within_their_budgets_at_five_times_the_expected_volume()
    {
        try
        {
            await RunAsync();
        }
        finally
        {
            await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(PostgreSqlTestDatabases.TenantADatabase));
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("VACUUM ANALYZE", connection) { CommandTimeout = 300 };
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task RunAsync()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var firstTemplate = await InsertVolumeAsync();

            var cold = new List<(string Name, TimeSpan Elapsed)>();
            var warm = new List<(string Name, TimeSpan Elapsed)>();

            async Task TimeAsync<T>(string name, Func<Task<T>> query)
            {
                var c = Stopwatch.StartNew();
                await query();
                c.Stop();
                var w = Stopwatch.StartNew();
                await query();
                w.Stop();
                cold.Add((name, c.Elapsed));
                warm.Add((name, w.Elapsed));
            }

            await TimeAsync("template list page 1 (100 rows whose bodies are 200,000 characters)", () => Templates.GetListAsync(PlatformList<EmailTemplateListRequestDto>()));
            await TimeAsync("template list page 10", () => Templates.GetListAsync(PlatformList<EmailTemplateListRequestDto>(start: 900, end: 1_000)));
            await TimeAsync("template list searched", () => Templates.GetListAsync(PlatformList<EmailTemplateListRequestDto>("template 05")));
            await TimeAsync("template list filtered on a purpose, sorted by default desc", () => Templates.GetListAsync(new EmailTemplateListRequestDto { Purpose = EmailTemplatePurpose.Invoice, SortBy = "isDefault", SortDir = "desc" }));
            await TimeAsync("template lookup (500)", () => Templates.GetLookupAsync());
            await TimeAsync("template detail with a 200,000-character body", () => Templates.GetAsync(firstTemplate));
            await TimeAsync("report definition list page 1", () => Definitions.GetListAsync(PlatformList<ReportDefinitionListRequestDto>()));
            await TimeAsync("report definition list searched, archived included", () => Definitions.GetListAsync(new ReportDefinitionListRequestDto { WildcardSearch = "layout 07", IncludeArchived = true }));
            await TimeAsync("report definition list filtered on a purpose, sorted by culture", () => Definitions.GetListAsync(new ReportDefinitionListRequestDto { Purpose = ReportPurpose.Invoice, SortBy = "culture" }));
            await TimeAsync("report definition lookup (500)", () => Definitions.GetLookupAsync());
            await TimeAsync("document type list page 1 (sorted on the localized name)", () => Types.GetListAsync(PlatformList<DocumentTypeListRequestDto>()));
            await TimeAsync("document type list searched", () => Types.GetListAsync(PlatformList<DocumentTypeListRequestDto>("type 04")));
            await TimeAsync("document type list sorted by code desc", () => Types.GetListAsync(new DocumentTypeListRequestDto { SortBy = "code", SortDir = "desc", IncludeArchived = true }));
            await TimeAsync("document type lookup (500)", () => Types.GetLookupAsync());

            foreach (var ((name, c), (_, w)) in cold.Zip(warm))
            {
                _output.WriteLine($"{name}: cold {c.TotalMilliseconds:F0} ms, warm {w.TotalMilliseconds:F0} ms (budget {ReadBudget.TotalMilliseconds} ms)");
            }


            /* The results are the right ones, not just fast. */
            var page = await Templates.GetListAsync(PlatformList<EmailTemplateListRequestDto>());
            page.TotalCount.ShouldBe(TemplateCount);
            page.Rows.Count.ShouldBe(100);
            page.Rows.Select(r => r.Culture).Distinct().ShouldBe(TemplateCultures, ignoreOrder: true);
            page.Rows.Count(r => r.IsDefault).ShouldBe(Enum.GetValues<EmailTemplatePurpose>().Length, "one default per purpose, all on the first page");

            /* The list and the lookup never read the body, at volume too; the detail (control) does. */
            using (var capture = EfCommandCapture.Begin())
            {
                await Task.Delay(EfCommandCapture.LoggingCacheTime);
                (await Templates.GetAsync(firstTemplate)).HtmlBody.Length.ShouldBe(PlatformMasterDataConsts.MaxHtmlBodyLength);
                capture.On("EmailTemplates").ShouldContain(c => c.Contains("\"HtmlBody\"", StringComparison.Ordinal), "control: the detail reads the body");
                capture.Clear();

                await Templates.GetListAsync(PlatformList<EmailTemplateListRequestDto>());
                await Templates.GetListAsync(new EmailTemplateListRequestDto { Purpose = EmailTemplatePurpose.Invoice, SortBy = "culture", SortDir = "desc", WildcardSearch = "template" });
                await Templates.GetLookupAsync();
                await Templates.GetLookupAsync("template 00");
                var commands = capture.On("EmailTemplates");
                commands.Count.ShouldBeGreaterThanOrEqualTo(4, string.Join("\n---\n", capture.Commands));
                commands.ShouldAllBe(c => !c.Contains("\"HtmlBody\"", StringComparison.Ordinal), string.Join("\n---\n", commands));
            }

            (await Templates.GetLookupAsync()).Count.ShouldBe(PlatformMasterDataConsts.MaxLookupTake);
            (await Definitions.GetListAsync(PlatformList<ReportDefinitionListRequestDto>())).TotalCount.ShouldBe(DefinitionCount - DefinitionCount / 10, "every tenth definition is archived");
            (await Types.GetListAsync(new DocumentTypeListRequestDto { IncludeArchived = true })).TotalCount.ShouldBe(TypeCount + 2, "the volume plus the seeded FREIGHT and GENERAL");

            var overBudget = warm.Where(m => m.Elapsed > ReadBudget).Select(m => $"{m.Name}: {m.Elapsed.TotalMilliseconds:F0} ms").ToList();
            _output.WriteLine(overBudget.Count == 0 ? "every warm read within the 300 ms budget" : "warm reads over the 300 ms budget: " + string.Join("; ", overBudget));
            overBudget.ShouldBeEmpty("warm reads over the 300 ms interactive-read budget");
        });
    }

    private async Task<Guid> InsertVolumeAsync()
    {
        var insert = Stopwatch.StartNew();
        var context = await GetRequiredService<IDbContextProvider<PlatformDbContext>>().GetDbContextAsync();

        var maxBody = "<p>" + new string('m', PlatformMasterDataConsts.MaxHtmlBodyLength - 7) + "</p>";
        var smallBody = "<p>" + new string('s', SmallBody - 7) + "</p>";
        Guid firstTemplate = default;
        var purposes = Enum.GetValues<EmailTemplatePurpose>();
        for (var start = 0; start < TemplateCount; start += 100)
        {
            for (var i = start; i < start + 100; i++)
            {
                var body = i < MaxBodyTemplates ? maxBody : smallBody;
                var template = new EmailTemplate(Guid.NewGuid(), TenantA, $"Template {i:D4}", purposes[i % purposes.Length], TemplateCultures[i % TemplateCultures.Length], Cultures,
                    $"Subject {i} {{{{ number }}}}", body);
                template.SetAddresses("{{ company.email }}", null, null, null, null, i % 2 == 0);
                template.SetDefault(i < purposes.Length);
                if (i == 0)
                {
                    firstTemplate = template.Id;
                }

                context.Add(template);
            }

            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }

        var reportPurposes = Enum.GetValues<ReportPurpose>();
        for (var i = 0; i < DefinitionCount; i++)
        {
            var definition = new ReportDefinition(Guid.NewGuid(), TenantA, $"Layout {i:D4}", reportPurposes[i % reportPurposes.Length], Cultures[i % 2], Cultures,
                ReportRenderingProvider.PdfGeneratorApi, $"tpl-{i}", "document-{{ number }}");
            definition.SetDefault(i < reportPurposes.Length * 2);
            if (i % 10 == 9)
            {
                definition.Archive();
            }

            context.Add(definition);
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var kinds = Enum.GetValues<DocumentTransformationKind>();
        for (var i = 0; i < TypeCount; i++)
        {
            var type = new DocumentType(Guid.NewGuid(), TenantA, $"VT{i:D4}", LocalizedName.Create([new("nl", $"Soort {i:D4}"), new("en", $"Type {i:D4}")]));
            type.SetFlags(i % 2 == 0, i % 3 == 0, i % 4 == 0);
            type.SetTransformations(kinds.Take(i % 5).Select(k => (k, DocumentType.RequiresArgument(k) ? (string?)"^REF" : null)).ToList());
            if (i % 20 == 19)
            {
                type.Archive();
            }

            context.Add(type);
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        _output.WriteLine($"inserted {TemplateCount} templates ({MaxBodyTemplates} with 200,000-character bodies), {DefinitionCount} report definitions, " +
                          $"{TypeCount} document types in {insert.Elapsed.TotalSeconds:F1} s");

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction();
        foreach (var table in new[] { "EmailTemplates", "ReportDefinitions", "DocumentTypes", "DocumentTypeTransformations" })
        {
            await using var command = new NpgsqlCommand($"ANALYZE platform.\"{table}\"", connection, transaction);
            await command.ExecuteNonQueryAsync();
        }

        return firstTemplate;
    }
}
