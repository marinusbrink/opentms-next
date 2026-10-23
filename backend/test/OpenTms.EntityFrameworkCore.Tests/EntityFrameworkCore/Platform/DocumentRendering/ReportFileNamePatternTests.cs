using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;
using OpenTms.Platform;
using OpenTms.Platform.DocumentRendering;
using OpenTms.Platform.Localization;
using OpenTms.Platform.ReportDefinitions;
using Shouldly;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.DocumentRendering;

/// <summary>
/// The file-name pattern rule of docs/designs/50-report-rendering.md ("File name") and design 49's FileNamePattern row
/// (PO 2026-10-23): a pattern may use only <c>{{ number }}</c>; any other name is refused on create and on update with
/// FileNamePatternUnknownField (data field = fileNamePattern, name = the dotted name), whose localized text keeps
/// <c>{{ number }}</c> literally; Scriban's built-ins stay available; a row saved before the check whose pattern fails at
/// render time still renders under a random name and the renderer logs a warning with the definition id. Also the
/// localization keys the change added. Tenant isolation: another tenant's definition is not found before the pattern
/// is looked at, and a refused update leaves the owner's row untouched. Risk class: High (design: "File-name pattern:
/// only {{ number }}"); isolation Critical in the department matrix. SQLite with the fake provider.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class ReportFileNamePatternTests : ReportRenderingTestBase<ReportRenderingTestModule>
{
    private const string PayloadMarker = "QQ-PATTERN-PAYLOAD-Visser";

    private readonly FakeReportRenderingProvider _provider;

    public ReportFileNamePatternTests()
    {
        _provider = GetRequiredService<FakeReportRenderingProvider>();
        _provider.CurrentTenant = CurrentTenant;
    }

    private static ReportDefinitionUpdateDto UpdateFrom(ReportDefinitionDto dto, string pattern) => new()
    {
        Name = dto.Name, Purpose = dto.Purpose, Culture = dto.Culture, Provider = dto.Provider, ProviderTemplateId = dto.ProviderTemplateId,
        FileNamePattern = pattern, IsDefault = dto.IsDefault, IsArchived = dto.IsArchived,
        AvailableInCustomerPortal = dto.AvailableInCustomerPortal, Description = dto.Description, ConcurrencyStamp = dto.ConcurrencyStamp
    };

    private Task<long> CountAsync() => Definitions.GetListAsync(new ReportDefinitionListRequestDto { StartRow = 0, EndRow = 100, IncludeArchived = true })
        .ContinueWith(t => (long)t.Result.TotalCount);

    private static JsonElement Invoice(string number) =>
        JsonDocument.Parse($$$"""{"Entity":{"EntityNumber":"{{{number}}}","Remarks":"{{{PayloadMarker}}}"},"EntityType":"Invoice"}""").RootElement.Clone();

    // ── Refused on save ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("factuur-{{ invoice.number }}.pdf", "invoice.number")]
    [InlineData("factuur-{{ number.foo }}", "number.foo")]
    [InlineData("factuur-{{ Number }}", "Number")]
    [InlineData("{{ x }}", "x")]
    [InlineData("{{ for i in items }}{{ i }}{{ end }}", "items")]
    public async Task A_pattern_naming_another_field_is_refused_on_create_and_on_update(string pattern, string name)
    {
        using var _ = InTenantA();
        var ex = await ShouldFailAsync(() => CreateDefinitionAsync(name: "Refused", pattern: pattern), PlatformErrorCodes.FileNamePatternUnknownField);
        ex.Data["field"].ShouldBe("fileNamePattern");
        ex.Data["name"].ShouldBe(name);
        (await CountAsync()).ShouldBe(0);

        var created = await CreateDefinitionAsync(name: "Valid", pattern: "factuur-{{ number }}");
        var update = await ShouldFailAsync(() => Definitions.UpdateAsync(created.Id, UpdateFrom(created, pattern)), PlatformErrorCodes.FileNamePatternUnknownField);
        update.Data["field"].ShouldBe("fileNamePattern");
        update.Data["name"].ShouldBe(name);
        (await Definitions.GetAsync(created.Id)).FileNamePattern.ShouldBe("factuur-{{ number }}.pdf");
    }

    [Fact]
    public async Task An_unterminated_loop_is_a_syntax_error_not_an_unknown_field()
    {
        using var _ = InTenantA();
        var ex = await ShouldFailAsync(() => CreateDefinitionAsync(pattern: "{{ for i in items }}"), PlatformErrorCodes.TemplateSyntaxError);
        ex.Data["field"].ShouldBe("fileNamePattern");
    }

    [Theory]
    [InlineData("factuur-{{ string.upcase number }}", "f-1", "factuur-F-1.pdf")]
    [InlineData("factuur-{{ number | string.upcase }}", "f-1", "factuur-F-1.pdf")]
    [InlineData("{{ number }}", "F-1", "F-1.pdf")]
    [InlineData("vaste-naam", "F-1", "vaste-naam.pdf")]
    public async Task Number_and_Scriban_built_ins_are_accepted_and_render(string pattern, string number, string fileName)
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var created = await CreateDefinitionAsync(pattern: pattern);
        var updated = await Definitions.UpdateAsync(created.Id, UpdateFrom(created, pattern));

        updated.FileNamePattern.ShouldBe(pattern.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? pattern : pattern + ".pdf");
        (await GetRequiredService<IDocumentRenderer>().RenderAsync(created.Id, Invoice(number))).FileName.ShouldBe(fileName);
    }

    [Theory]
    [InlineData("en", "The file name pattern uses 'invoice.number', which does not exist: only {{ number }} is available.")]
    [InlineData("nl", "Het bestandsnaampatroon gebruikt 'invoice.number', maar dat bestaat niet: alleen {{ number }} is beschikbaar.")]
    public async Task The_refusal_names_the_field_and_keeps_the_number_placeholder_literally(string culture, string message)
    {
        using var _ = InTenantA();
        using var ui = new CultureScope(culture);
        var ex = await ShouldFailAsync(() => CreateDefinitionAsync(pattern: "factuur-{{ invoice.number }}"), PlatformErrorCodes.FileNamePatternUnknownField);

        var error = GetRequiredService<IExceptionToErrorInfoConverter>().Convert(ex);

        error.Code.ShouldBe(PlatformErrorCodes.FileNamePatternUnknownField);
        error.Message.ShouldBe(message);
    }

    // ── Tenant isolation (negative) ────────────────────────────────────────────

    [Fact]
    public async Task Another_tenant_cannot_reach_a_definition_through_an_update_with_any_pattern()
    {
        ReportDefinitionDto ofA;
        using (InTenantA())
        {
            ofA = await CreateDefinitionAsync(pattern: "factuur-{{ number }}");
        }

        foreach (var other in new Guid?[] { TenantB, null })
        {
            using (CurrentTenant.Change(other))
            {
                foreach (var pattern in new[] { "factuur-{{ invoice.number }}", "b-{{ number }}" })
                {
                    await Should.ThrowAsync<EntityNotFoundException>(() => Definitions.UpdateAsync(ofA.Id, UpdateFrom(ofA, pattern)),
                        $"{pattern} from {(other == null ? "the host" : "tenant B")}");
                }
            }
        }

        using (InTenantA())
        {
            (await Definitions.GetAsync(ofA.Id)).FileNamePattern.ShouldBe("factuur-{{ number }}.pdf");
        }

        using (InTenantB())
        {
            (await CountAsync()).ShouldBe(0);
        }
    }

    // ── A row saved before the check ───────────────────────────────────────────

    [Theory]
    [InlineData("factuur-{{ invoice.number }}.pdf")]
    [InlineData("{{ for i in 1..100000 }}x{{ end }}.pdf")]
    public async Task A_stored_pattern_that_fails_at_render_time_renders_under_a_random_name_with_a_warning(string pattern)
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var id = Guid.NewGuid();
        /* Inserted directly: the row predates the save-time check (design 50, "File name"). */
        await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<ReportDefinition, Guid>>().InsertAsync(
            new ReportDefinition(id, TenantA, "Legacy", ReportPurpose.Invoice, "nl", ["nl", "en"], ReportRenderingProvider.PdfGeneratorApi, "tpl-1", pattern),
            autoSave: true));

        var document = await GetRequiredService<IDocumentRenderer>().RenderAsync(id, Invoice("F-2026-0001"));

        document.FileName.Length.ShouldBe(12 + 4, document.FileName);
        document.FileName[..12].ShouldAllBe(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
        document.FileName.ShouldEndWith(".pdf");
        _provider.LastRender!.FileName.ShouldBe(document.FileName, "the provider got the random name");

        var entries = GetRequiredService<AllLevelsLog>().Snapshot();
        var warning = entries.Where(e => e.Level == LogLevel.Warning && e.Category.EndsWith(nameof(DocumentRenderer), StringComparison.Ordinal))
            .ShouldHaveSingleItem("the renderer warns with the definition id");
        warning.Values.ShouldContain("ReportDefinitionId=" + id);
        warning.Values.ShouldContain("TenantId=" + TenantA);
        entries.ShouldAllBe(e => !e.Everything.Contains(PayloadMarker), "no payload in any log line");
    }

    [Fact]
    public async Task A_working_pattern_logs_no_file_name_warning()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync(pattern: "factuur-{{ number }}");

        (await GetRequiredService<IDocumentRenderer>().RenderAsync(definition.Id, Invoice("F-1"))).FileName.ShouldBe("factuur-F-1.pdf");

        GetRequiredService<AllLevelsLog>().Snapshot()
            .ShouldNotContain(e => e.Level == LogLevel.Warning && e.Category.EndsWith(nameof(DocumentRenderer), StringComparison.Ordinal));
    }

    // ── Localization (Low) ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("Platform:DocumentRenderingBusy")]
    [InlineData("Platform:DocumentRenderingTimedOut")]
    [InlineData("OpenTms.Platform:FileNamePatternUnknownField")]
    [InlineData("Platform:FileNamePatternHint")]
    public void Each_new_text_exists_in_English_and_Dutch(string key)
    {
        var localizer = GetRequiredService<IStringLocalizer<PlatformResource>>();
        string Text(string culture)
        {
            using var ui = new CultureScope(culture);
            var text = localizer[key];
            text.ResourceNotFound.ShouldBeFalse($"{key} in {culture}");
            text.Value.ShouldNotBeNullOrWhiteSpace();
            return text.Value;
        }

        Text("en").ShouldNotBe(Text("nl"), $"{key} is translated");
    }

    [Fact]
    public void The_texts_say_what_the_design_says()
    {
        var localizer = GetRequiredService<IStringLocalizer<PlatformResource>>();
        using (new CultureScope("en"))
        {
            localizer["Platform:DocumentRenderingBusy"].Value.ShouldBe(PdfGeneratorApiClient.BusyMessage, "the client's English fallback is the same text");
            localizer["Platform:DocumentRenderingTimedOut"].Value.ShouldBe("the document service did not answer in time.");
            localizer["Platform:FileNamePatternHint"].Value.ShouldBe("Only {{ number }} is available; \".pdf\" is added when missing.");
        }

        using (new CultureScope("nl"))
        {
            localizer["Platform:FileNamePatternHint"].Value.ShouldBe("Alleen {{ number }} is beschikbaar; \".pdf\" wordt toegevoegd als het ontbreekt.");
        }
    }
}
