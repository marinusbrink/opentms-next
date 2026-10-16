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
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.ReportDefinitions;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace OpenTms.PostgreSql.Platform;

/// <summary>
/// VOLUME TEST (docs/designs/49-platform-master-data.md, "Expected volumes" × 5; "Cost &amp; SLO impact": list reads
/// p95 &lt; 300 ms, generation p95 &lt; 20 ms): in tenant A, 1,000 e-mail templates with content in two languages (the
/// templates of the first list page carry the maximum body of 200,000 characters per language, the rest 10,000),
/// 1,000 report definitions, 500 document types with 0–4 transformations and 250 number sequences with 12 counter rows
/// each. The lists (first page, searched, filtered, non-default sorts), the lookups and the template detail are timed cold
/// and warm after ANALYZE; every warm read is held to the 300 ms interactive-read budget (performance-budgets.md). Then
/// 200 numbers are drawn and the p95 of one draw is held to 20 ms. Risk class: Critical/High parts (test-engineer
/// obligation). The data lives in one rolled-back transaction, so nothing stays behind.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PlatformMasterDataVolumeTests : PlatformMasterDataPostgreSqlTestBase
{
    private const int TemplateCount = 1_000;
    private const int DefinitionCount = 1_000;
    private const int TypeCount = 500;
    private const int SequenceCount = 250;
    private const int CountersPerSequence = 12;
    private const int Draws = 200;
    private const int MaxBodyTemplates = 100;
    private const int SmallBody = 10_000;
    private static readonly TimeSpan ReadBudget = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan DrawBudget = TimeSpan.FromMilliseconds(20);
    private static readonly string[] Cultures = ["nl", "en"];

    private readonly ITestOutputHelper _output;

    private IEmailTemplateAppService Templates => GetRequiredService<IEmailTemplateAppService>();
    private IReportDefinitionAppService Definitions => GetRequiredService<IReportDefinitionAppService>();
    private IDocumentTypeAppService Types => GetRequiredService<IDocumentTypeAppService>();
    private INumberSequenceAppService Sequences => GetRequiredService<INumberSequenceAppService>();
    private INumberGenerator Generator => GetRequiredService<INumberGenerator>();

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
            var (firstTemplate, sequenceIds) = await InsertVolumeAsync();

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

            await TimeAsync("template list page 1 (100 rows whose bodies are 2 × 200,000 characters)", () => Templates.GetListAsync(PlatformList<EmailTemplateListRequestDto>()));
            await TimeAsync("template list page 10", () => Templates.GetListAsync(PlatformList<EmailTemplateListRequestDto>(start: 900, end: 1_000)));
            await TimeAsync("template list searched", () => Templates.GetListAsync(PlatformList<EmailTemplateListRequestDto>("template 05")));
            await TimeAsync("template list filtered on a purpose, sorted by default desc", () => Templates.GetListAsync(new EmailTemplateListRequestDto { Purpose = EmailTemplatePurpose.Invoice, SortBy = "isDefault", SortDir = "desc" }));
            await TimeAsync("template lookup (500)", () => Templates.GetLookupAsync());
            await TimeAsync("template detail with two 200,000-character bodies", () => Templates.GetAsync(firstTemplate));
            await TimeAsync("report definition list page 1", () => Definitions.GetListAsync(PlatformList<ReportDefinitionListRequestDto>()));
            await TimeAsync("report definition list searched, archived included", () => Definitions.GetListAsync(new ReportDefinitionListRequestDto { WildcardSearch = "layout 07", IncludeArchived = true }));
            await TimeAsync("report definition list filtered on a purpose, sorted by culture", () => Definitions.GetListAsync(new ReportDefinitionListRequestDto { Purpose = ReportPurpose.Invoice, SortBy = "culture" }));
            await TimeAsync("report definition lookup (500)", () => Definitions.GetLookupAsync());
            await TimeAsync("document type list page 1 (sorted on the localized name)", () => Types.GetListAsync(PlatformList<DocumentTypeListRequestDto>()));
            await TimeAsync("document type list searched", () => Types.GetListAsync(PlatformList<DocumentTypeListRequestDto>("type 04")));
            await TimeAsync("document type list sorted by code desc", () => Types.GetListAsync(new DocumentTypeListRequestDto { SortBy = "code", SortDir = "desc", IncludeArchived = true }));
            await TimeAsync("document type lookup (500)", () => Types.GetLookupAsync());
            await TimeAsync("number sequence list page 1 (in use and example per row)", () => Sequences.GetListAsync(PlatformList<PlatformMasterDataListRequestDto>()));
            await TimeAsync("number sequence list sorted by scheme", () => Sequences.GetListAsync(PlatformList<PlatformMasterDataListRequestDto>(sortBy: "scheme")));
            await TimeAsync("number sequence lookup (500)", () => Sequences.GetLookupAsync());
            await TimeAsync("number sequence detail", () => Sequences.GetAsync(sequenceIds[123]));

            foreach (var ((name, c), (_, w)) in cold.Zip(warm))
            {
                _output.WriteLine($"{name}: cold {c.TotalMilliseconds:F0} ms, warm {w.TotalMilliseconds:F0} ms (budget {ReadBudget.TotalMilliseconds} ms)");
            }

            /* The generator: one draw per created record, spread over the sequences and periods. */
            var draws = new List<double>(Draws);
            for (var i = 0; i < Draws; i++)
            {
                var date = new DateOnly(2026, 1 + i % 12, 1 + i % 28);
                var watch = Stopwatch.StartNew();
                await Generator.GetNextAsync(sequenceIds[i % 50], date);
                draws.Add(watch.Elapsed.TotalMilliseconds);
            }

            draws.Sort();
            var p95 = draws[(int)Math.Ceiling(Draws * 0.95) - 1];
            _output.WriteLine($"number generation over {Draws} draws: p50 {draws[Draws / 2]:F1} ms, p95 {p95:F1} ms, max {draws[^1]:F1} ms (budget p95 {DrawBudget.TotalMilliseconds} ms)");

            /* The results are the right ones, not just fast. */
            var page = await Templates.GetListAsync(PlatformList<EmailTemplateListRequestDto>());
            page.TotalCount.ShouldBe(TemplateCount);
            page.Rows.Count.ShouldBe(100);
            page.Rows.ShouldAllBe(r => r.Cultures == "en, nl");
            (await Templates.GetLookupAsync()).Count.ShouldBe(PlatformMasterDataConsts.MaxLookupTake);
            (await Definitions.GetListAsync(PlatformList<ReportDefinitionListRequestDto>())).TotalCount.ShouldBe(DefinitionCount - DefinitionCount / 10, "every tenth definition is archived");
            (await Types.GetListAsync(new DocumentTypeListRequestDto { IncludeArchived = true })).TotalCount.ShouldBe(TypeCount + 2, "the volume plus the seeded FREIGHT and GENERAL");
            var sequences = await Sequences.GetListAsync(PlatformList<PlatformMasterDataListRequestDto>());
            sequences.TotalCount.ShouldBe(SequenceCount);
            sequences.Rows.ShouldAllBe(r => r.IsInUse);

            var overBudget = warm.Where(m => m.Elapsed > ReadBudget).Select(m => $"{m.Name}: {m.Elapsed.TotalMilliseconds:F0} ms").ToList();
            _output.WriteLine(overBudget.Count == 0 ? "every warm read within the 300 ms budget" : "warm reads over the 300 ms budget: " + string.Join("; ", overBudget));
            overBudget.ShouldBeEmpty("warm reads over the 300 ms interactive-read budget");
            p95.ShouldBeLessThan(DrawBudget.TotalMilliseconds, $"generation p95 {p95:F1} ms over the {DrawBudget.TotalMilliseconds} ms budget");
        });
    }

    private async Task<(Guid FirstTemplate, List<Guid> Sequences)> InsertVolumeAsync()
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
                var template = new EmailTemplate(Guid.NewGuid(), TenantA, $"Template {i:D4}", purposes[i % purposes.Length]);
                var body = i < MaxBodyTemplates ? maxBody : smallBody;
                template.SetContents([("en", $"Subject {i} {{{{ number }}}}", body), ("nl", $"Onderwerp {i}", body)], Cultures);
                template.SetAddresses("{{ company.email }}", "{{ company.name }}", null, null, null, null, false);
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

        var schemes = Enum.GetValues<NumberSequenceScheme>();
        var sequenceIds = new List<Guid>(SequenceCount);
        for (var i = 0; i < SequenceCount; i++)
        {
            var sequence = new NumberSequence(Guid.NewGuid(), TenantA, $"Sequence {i:D3}",
                new NumberSequenceFormat(schemes[i % schemes.Length], $"S{i}-", null, "-", null, "/", 5));
            context.Add(sequence);
            sequenceIds.Add(sequence.Id);
            for (var m = 1; m <= CountersPerSequence; m++)
            {
                context.Add(new NumberSequenceCounter(TenantA, sequence.Id, 2025, m, 100 + m));
            }
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        _output.WriteLine($"inserted {TemplateCount} templates ({TemplateCount * 2} content rows, {MaxBodyTemplates} with 200,000-character bodies), {DefinitionCount} report definitions, " +
                          $"{TypeCount} document types, {SequenceCount} sequences with {SequenceCount * CountersPerSequence} counters in {insert.Elapsed.TotalSeconds:F1} s");

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction();
        foreach (var table in new[] { "EmailTemplates", "EmailTemplateContents", "ReportDefinitions", "DocumentTypes", "DocumentTypeTransformations", "NumberSequences", "NumberSequenceCounters" })
        {
            await using var command = new NpgsqlCommand($"ANALYZE platform.\"{table}\"", connection, transaction);
            await command.ExecuteNonQueryAsync();
        }

        return (firstTemplate, sequenceIds);
    }
}
