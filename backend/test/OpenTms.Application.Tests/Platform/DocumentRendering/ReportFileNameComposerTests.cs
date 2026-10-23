using System;
using System.Linq;
using System.Text.Json;
using OpenTms.Platform.ReportDefinitions;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.DocumentRendering;

/// <summary>
/// The file name of a rendered document (docs/designs/50-report-rendering.md, "IDocumentRenderer": the definition's
/// Scriban <c>FileNamePattern</c> rendered with the payload's number): <c>{{ number }}</c> from <c>Entity.EntityNumber</c>,
/// the transport-unit balance's <c>Entity.Entity.EntityNumber</c> as fallback, ".pdf" replaced by the format's extension,
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
    public void The_balance_number_is_read_one_level_deeper_and_Entity_EntityNumber_wins_when_both_exist()
    {
        ReportFileNameComposer.NumberOf(Payload("""{"Entity":{"EntityNumber":"S-7"}}""")).ShouldBe("S-7");
        ReportFileNameComposer.NumberOf(Payload("""{"EntityNumber":"TOP","Entity":{"EntityNumber":"INNER"}}""")).ShouldBe("TOP");
        ReportFileNameComposer.Compose("saldo-{{ number }}.pdf", Payload("""{"Entity":{"EntityNumber":"S-7"}}"""), ReportRenderFormat.Pdf)
            .ShouldBe("saldo-S-7.pdf");
    }

    [Theory]
    [InlineData("""{"Entity":{"InvoiceNumber":"F-1"}}""")]
    [InlineData("""{"Entity":{"EntityNumber":null}}""")]
    [InlineData("""{"Entity":{"EntityNumber":{"x":1}}}""")]
    [InlineData("""{"Entity":"F-1"}""")]
    [InlineData("""{"EntityNumber":"F-1"}""")]
    [InlineData("""[1,2]""")]
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

    // ── The composer against the catalogue (design 50: the modules produce exactly the catalogue's shape) ──

    [Theory]
    [InlineData(ReportPurpose.TransportOrderConfirmation)]
    [InlineData(ReportPurpose.TransportFile)]
    [InlineData(ReportPurpose.Invoice)]
    [InlineData(ReportPurpose.PurchaseOrder)]
    [InlineData(ReportPurpose.TransportUnitBalance)]
    public void Every_purposes_catalogue_payload_gives_the_file_name_its_number(ReportPurpose purpose)
    {
        /* The renderer fills {{ number }} from Entity.EntityNumber, for the transport-unit balance from
         * Entity.Entity.EntityNumber. The catalogue is the contract the printing modules produce; a number path the
         * catalogue does not define can never be filled by a conforming payload. */
        var sample = JsonSerializer.SerializeToElement(ReportDataCatalogue.SampleFor(purpose));
        var paths = ReportDataCatalogue.FieldsFor(purpose).Select(f => f.Path).ToHashSet();

        (paths.Contains("Entity.EntityNumber") || paths.Contains("Entity.Entity.EntityNumber"))
            .ShouldBeTrue($"{purpose}: the catalogue defines neither Entity.EntityNumber nor Entity.Entity.EntityNumber, the paths ReportFileNameComposer reads");
        ReportFileNameComposer.NumberOf(sample).ShouldNotBeNullOrWhiteSpace($"{purpose}: the sample gives no number for the file name");
    }
}
