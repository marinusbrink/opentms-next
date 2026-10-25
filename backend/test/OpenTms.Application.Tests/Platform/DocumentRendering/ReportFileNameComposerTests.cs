using System;
using System.Linq;
using System.Text.Json;
using OpenTms.Platform.ReportDefinitions;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.DocumentRendering;

/// <summary>
/// The file name of a rendered document (docs/designs/55-report-rendering.md, "IDocumentRenderer": the definition's
/// Scriban <c>FileNamePattern</c> rendered with the payload's number): <c>{{ number }}</c> from <c>Entity.EntityNumber</c>,
/// for the transport-unit balance the period's name <c>Entity.Entity.Period.Name</c> (PO 2026-10-23; the old
/// <c>Entity.Entity.EntityNumber</c> fallback is gone), ".pdf" replaced by the format's extension,
/// characters a file system or a Content-Disposition header cannot hold replaced, an empty or failing pattern giving a
/// random name (as OpenTMS). Risk class: High (part of the renderer's contract with the Orders, Planning and Financial runs).
/// </summary>
public class ReportFileNameComposerTests
{
    private static JsonElement Payload(string entityJson) => JsonDocument.Parse($"{{\"Entity\":{entityJson}}}").RootElement.Clone();

    private static readonly JsonElement Invoice = Payload("""{"EntityNumber":"F-2026-0001","InvoiceNumber":"F-2026-0001"}""");

    private static bool IsRandom(string name, string extension) =>
        name.Length == 12 + 1 + extension.Length
        && name.EndsWith("." + extension, StringComparison.Ordinal)
        && name[..12].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    [Theory]
    [InlineData(ReportRenderFormat.Pdf, "factuur-F-2026-0001.pdf")]
    [InlineData(ReportRenderFormat.Html, "factuur-F-2026-0001.html")]
    [InlineData(ReportRenderFormat.Zip, "factuur-F-2026-0001.zip")]
    [InlineData(ReportRenderFormat.Xlsx, "factuur-F-2026-0001.xlsx")]
    public void The_number_fills_the_pattern_and_the_pdf_extension_becomes_the_formats(ReportRenderFormat format, string expected)
    {
        ReportFileNameComposer.Compose("factuur-{{ number }}.pdf", Invoice, format).ShouldBe(expected);
    }

    [Theory]
    [InlineData("factuur-{{ number }}.PDF", "factuur-F-2026-0001.pdf")]
    [InlineData("factuur-{{ number }}", "factuur-F-2026-0001.pdf")]
    [InlineData("  {{ number }}  ", "F-2026-0001.pdf")]
    [InlineData("vaste-naam.pdf", "vaste-naam.pdf")]
    [InlineData("{{ number }}.pdf.pdf", "F-2026-0001.pdf.pdf")]
    public void The_pdf_extension_is_dropped_once_case_insensitively_and_added_once(string pattern, string expected)
    {
        ReportFileNameComposer.Compose(pattern, Invoice, ReportRenderFormat.Pdf).ShouldBe(expected);
    }

    [Fact]
    public void A_numeric_entity_number_is_used_as_written()
    {
        ReportFileNameComposer.Compose("order-{{ number }}.pdf", Payload("""{"EntityNumber":20260042}"""), ReportRenderFormat.Pdf)
            .ShouldBe("order-20260042.pdf");
    }

    [Fact]
    public void The_balance_number_is_the_periods_name_and_Entity_EntityNumber_wins_when_present()
    {
        /* Design 55, "File name" (PO 2026-10-23): the balance payload (GetSingleOutput<TransportUnitTransactionPeriodOverview>)
         * has no entity number; its number is Entity.Entity.Period.Name. Entity.EntityNumber wins wherever it exists. */
        var balance = Payload("""{"Entity":{"Period":{"Name":"Oktober 2026","Code":"P-10"}}}""");
        ReportFileNameComposer.NumberOf(balance).ShouldBe("Oktober 2026");
        ReportFileNameComposer.Compose("saldo-{{ number }}.pdf", balance, ReportRenderFormat.Pdf).ShouldBe("saldo-Oktober 2026.pdf");
        ReportFileNameComposer.NumberOf(Payload("""{"EntityNumber":"TOP","Entity":{"Period":{"Name":"Oktober 2026"}}}""")).ShouldBe("TOP");
        ReportFileNameComposer.NumberOf(Payload("""{"Entity":{"Period":{"Name":202610}}}""")).ShouldBe("202610", "a numeric period name is used as written");
    }

    [Fact]
    public void The_old_Entity_Entity_EntityNumber_fallback_is_gone()
    {
        ReportFileNameComposer.NumberOf(Payload("""{"Entity":{"EntityNumber":"S-7"}}""")).ShouldBeNull();
        ReportFileNameComposer.NumberOf(Payload("""{"Entity":{"EntityNumber":"INNER","Period":{"Name":"Oktober 2026"}}}"""))
            .ShouldBe("Oktober 2026", "the period's name, not the inner entity number");
    }

    [Theory]
    [InlineData("""{"Entity":{"InvoiceNumber":"F-1"}}""")]
    [InlineData("""{"Entity":{"EntityNumber":null}}""")]
    [InlineData("""{"Entity":{"EntityNumber":{"x":1}}}""")]
    [InlineData("""{"Entity":"F-1"}""")]
    [InlineData("""{"EntityNumber":"F-1"}""")]
    [InlineData("""[1,2]""")]
    [InlineData("""{"Entity":{"Entity":{"EntityNumber":"S-7"}}}""")]
    [InlineData("""{"Entity":{"Entity":{"Period":{}}}}""")]
    [InlineData("""{"Entity":{"Entity":{"Period":{"Name":null}}}}""")]
    [InlineData("""{"Entity":{"Entity":{"Period":"Oktober 2026"}}}""")]
    [InlineData("""{"Entity":{"Entity":"Oktober 2026"}}""")]
    [InlineData("""{"Entity":{"Period":{"Name":"Oktober 2026"}}}""")]
    public void Without_a_number_the_placeholder_is_empty(string json)
    {
        var data = JsonDocument.Parse(json).RootElement.Clone();
        ReportFileNameComposer.NumberOf(data).ShouldBeNull();
        ReportFileNameComposer.Compose("factuur-{{ number }}.pdf", data, ReportRenderFormat.Pdf).ShouldBe("factuur-.pdf");
    }

    [Fact]
    public void Null_data_leaves_the_placeholder_empty()
    {
        ReportFileNameComposer.Compose("factuur-{{ number }}.pdf", null, ReportRenderFormat.Pdf).ShouldBe("factuur-.pdf");
    }

    [Theory]
    [InlineData("F/2026\\0001", "factuur-F_2026_0001.pdf")]
    [InlineData("A:B*C?D", "factuur-A_B_C_D.pdf")]
    [InlineData("\"quoted\" <x> |y|", "factuur-_quoted_ _x_ _y_.pdf")]
    [InlineData("line\r\nbreak\ttab", "factuur-line__break_tab.pdf")]
    public void Characters_a_file_system_or_a_header_cannot_hold_become_underscores(string number, string expected)
    {
        var data = Payload(JsonSerializer.Serialize(new { EntityNumber = number }));
        var name = ReportFileNameComposer.Compose("factuur-{{ number }}.pdf", data, ReportRenderFormat.Pdf);

        name.ShouldBe(expected);
        name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|', '\r', '\n', '\t']).ShouldBe(-1);
    }

    [Fact]
    public void A_number_cannot_climb_out_of_the_download_folder()
    {
        var name = ReportFileNameComposer.Compose("{{ number }}.pdf", Payload("""{"EntityNumber":"../../etc/passwd"}"""), ReportRenderFormat.Pdf);

        name.ShouldNotContain("/");
        name.ShouldNotStartWith(".");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{{ number }}.pdf")]
    [InlineData(" ... .pdf")]
    [InlineData("{{ number ")]
    [InlineData("{{ for i in 1..100000 }}x{{ end }}.pdf")]
    public void An_empty_failing_or_unusable_pattern_gives_a_random_name(string? pattern)
    {
        /* "{{ number }}" without a number renders empty; " ... " trims away; the unclosed tag does not parse; the long
         * loop runs into the loop limit at render time. */
        var first = ReportFileNameComposer.Compose(pattern, Payload("""{"InvoiceNumber":"F-1"}"""), ReportRenderFormat.Pdf);
        var second = ReportFileNameComposer.Compose(pattern, Payload("""{"InvoiceNumber":"F-1"}"""), ReportRenderFormat.Pdf);

        IsRandom(first, "pdf").ShouldBeTrue(first);
        first.ShouldNotBe(second, "a random name each time");
    }

    [Fact]
    public void A_random_name_carries_the_formats_extension()
    {
        IsRandom(ReportFileNameComposer.Compose("", Invoice, ReportRenderFormat.Xlsx), "xlsx").ShouldBeTrue();
    }

    [Fact]
    public void The_pattern_sees_only_the_number_not_the_payload()
    {
        ReportFileNameComposer.Compose("x{{ Entity }}{{ data }}{{ invoice_number }}-{{ number }}.pdf", Invoice, ReportRenderFormat.Pdf)
            .ShouldBe("x-F-2026-0001.pdf");
    }

    [Theory]
    [InlineData(ReportRenderFormat.Pdf)]
    [InlineData(ReportRenderFormat.Xlsx)]
    public void A_long_result_is_cut_to_255_characters_extension_included(ReportRenderFormat format)
    {
        var number = new string('9', 600);
        var name = ReportFileNameComposer.Compose("factuur-{{ number }}.pdf", Payload($"{{\"EntityNumber\":\"{number}\"}}"), format);

        name.Length.ShouldBe(ReportFileNameComposer.MaxLength);
        name.ShouldEndWith("." + ReportFileNameComposer.ExtensionOf(format));
    }

    [Theory]
    [InlineData(ReportRenderFormat.Pdf, "pdf", "application/pdf")]
    [InlineData(ReportRenderFormat.Html, "html", "text/html")]
    [InlineData(ReportRenderFormat.Zip, "zip", "application/zip")]
    [InlineData(ReportRenderFormat.Xlsx, "xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public void Each_format_has_its_extension_and_content_type(ReportRenderFormat format, string extension, string contentType)
    {
        ReportFileNameComposer.ExtensionOf(format).ShouldBe(extension);
        ReportFileNameComposer.ContentTypeOf(format).ShouldBe(contentType);
    }

    // ── The composer against the catalogue (design 55: the modules produce exactly the catalogue's shape) ──

    [Theory]
    [InlineData(ReportPurpose.TransportOrderConfirmation)]
    [InlineData(ReportPurpose.TransportFile)]
    [InlineData(ReportPurpose.Invoice)]
    [InlineData(ReportPurpose.PurchaseOrder)]
    [InlineData(ReportPurpose.TransportUnitBalance)]
    [InlineData(ReportPurpose.ConsignmentNote)]
    public void Every_purposes_catalogue_payload_gives_the_file_name_its_number(ReportPurpose purpose)
    {
        /* The renderer fills {{ number }} from Entity.EntityNumber, for the transport-unit balance (no entity number) from
         * the period's name Entity.Entity.Period.Name (design 55, "File name"). The catalogue is the contract the printing
         * modules produce; a number path the catalogue does not define can never be filled by a conforming payload. */
        var sample = JsonSerializer.SerializeToElement(ReportDataCatalogue.SampleFor(purpose));
        var paths = ReportDataCatalogue.FieldsFor(purpose).Select(f => f.Path).ToHashSet();
        var numberPath = purpose == ReportPurpose.TransportUnitBalance ? "Entity.Entity.Period.Name" : "Entity.EntityNumber";

        paths.ShouldContain(numberPath, $"{purpose}: the catalogue does not define {numberPath}, the path ReportFileNameComposer reads");
        if (purpose == ReportPurpose.TransportUnitBalance)
        {
            paths.ShouldNotContain("Entity.EntityNumber", "the balance payload has no entity number (OpenTMS GetSingleOutput)");
        }

        var expected = numberPath.Split('.').Aggregate(sample, (element, name) => element.GetProperty(name)).ToString();
        expected.ShouldNotBeNullOrWhiteSpace($"{purpose}: the sample has no value at {numberPath}");
        ReportFileNameComposer.NumberOf(sample).ShouldBe(expected, $"{purpose}: the sample gives the file name the value at {numberPath}");
    }

    // ── The save-time field check (design 54 FileNamePattern row, design 55 "File name"; High) ──

    [Theory]
    [InlineData("factuur-{{ invoice.number }}.pdf", "invoice.number")]
    [InlineData("{{ number.foo }}", "number.foo")]
    [InlineData("{{ Number }}", "Number")]
    [InlineData("{{ x }}", "x")]
    [InlineData("{{ for i in items }}{{ i }}{{ end }}", "items")]
    [InlineData("{{ number }}-{{ klant.naam }}", "klant.naam")]
    [InlineData("{{ invoice.number | string.upcase }}", "invoice.number")]
    [InlineData("{{ string.foo number }}", "string.foo")]
    public void A_pattern_naming_anything_but_number_reports_the_dotted_name(string pattern, string name)
    {
        ReportFileNameComposer.FindUnknownField(pattern).ShouldBe(name);
    }

    [Theory]
    [InlineData("factuur-{{ number }}.pdf")]
    [InlineData("{{ number }}")]
    [InlineData("{{ string.upcase number }}")]
    [InlineData("{{ number | string.upcase }}")]
    [InlineData("{{ date.now }}")]
    [InlineData("vaste-naam.pdf")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Number_Scriban_built_ins_plain_text_and_an_empty_pattern_pass(string? pattern)
    {
        ReportFileNameComposer.FindUnknownField(pattern).ShouldBeNull();
    }

    [Theory]
    [InlineData("{{ 1 + }}")]
    [InlineData("{{ for i in items }}")]
    [InlineData("{{ for i in 1..100000 }}x{{ end }}.pdf")]
    public void A_syntax_error_or_a_render_limit_is_not_an_unknown_field(string pattern)
    {
        /* A pattern that does not parse is TemplateSyntaxError's on save; a loop beyond the limit is the render-time
         * fallback's (design 55: "any other render failure is left to the render-time fallback"). */
        ReportFileNameComposer.FindUnknownField(pattern).ShouldBeNull();
    }

    [Fact]
    public void A_stored_pattern_that_fails_at_render_time_gives_a_random_name_and_says_so()
    {
        /* A row saved before the field check (design 55): the renderer logs a warning when patternFailed is set. */
        var name = ReportFileNameComposer.Compose("factuur-{{ invoice.number }}.pdf", Invoice, ReportRenderFormat.Pdf, out var failed);

        failed.ShouldBeTrue();
        IsRandom(name, "pdf").ShouldBeTrue(name);
        ReportFileNameComposer.Compose("factuur-{{ number }}.pdf", Invoice, ReportRenderFormat.Pdf, out var ok).ShouldBe("factuur-F-2026-0001.pdf");
        ok.ShouldBeFalse();
        ReportFileNameComposer.Compose("", Invoice, ReportRenderFormat.Pdf, out var empty);
        empty.ShouldBeFalse("an empty pattern is not a failure: its random name is the designed outcome");
    }
}
