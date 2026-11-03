using System;
using System.Linq;
using System.Text;
using OpenTms.Integrations.OrderIntake.Extraction;
using Shouldly;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// Extraction and fingerprint (design 72 §3): scanned-PDF detection on PDFs generated with PdfPig's writer, XML read
/// securely (DTD and external entities refused, size limit), encoding detection, the CSV delimiter sniffer, the mail body
/// without quote and signature, the format detector and the fingerprint (same layout → same fingerprint, other values
/// do not matter). Risk class: Medium per the design (extraction), the XXE part is security-relevant.
/// </summary>
public class DocumentExtractionTests
{
    private static byte[] Pdf(params string[][] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var lines in pages)
        {
            var page = builder.AddPage(PageSize.A4);
            var y = 780d;
            foreach (var line in lines)
            {
                page.AddText(line, 11, new PdfPoint(40, y), font);
                y -= 18;
            }
        }

        return builder.Build();
    }

    [Fact]
    public void A_pdf_with_a_text_layer_is_read_into_lines_with_a_page_break_between_pages()
    {
        var bytes = Pdf(
            ["Transportopdracht ACME", "Onze referentie: PO-778", "Laadpostcode: 1234 AB"],
            ["Pagina twee met genoeg tekst om te tellen", "Losplaats Rotterdam"]);

        var document = new PdfDocumentExtractor().Extract(bytes, DocumentFormat.Pdf);

        document.Format.ShouldBe(DocumentFormat.Pdf);
        document.PageCount.ShouldBe(2);
        document.Lines.ShouldContain("Onze referentie: PO-778");
        document.Lines.ShouldContain(ExtractedDocument.PageBreak);
        document.Lines.ToList().IndexOf("Transportopdracht ACME").ShouldBeLessThan(document.Lines.ToList().IndexOf("Laadpostcode: 1234 AB"), "top to bottom");
        document.Fingerprint.Length.ShouldBe(64);
    }

    [Fact]
    public void A_pdf_without_a_text_layer_is_a_scanned_pdf()
    {
        /* Fewer than 20 characters per page on average: what a scan looks like to the text extractor. */
        var document = new PdfDocumentExtractor().Extract(Pdf([], ["p. 2"]), DocumentFormat.Pdf);

        document.Format.ShouldBe(DocumentFormat.ScannedPdf);
        document.PageCount.ShouldBe(2);
    }

    [Fact]
    public void A_pdf_that_is_not_a_pdf_is_corrupt()
    {
        Should.Throw<DocumentExtractionException>(() => new PdfDocumentExtractor().Extract(Encoding.ASCII.GetBytes("%PDF-1.7 nonsense"), DocumentFormat.Pdf))
            .ReasonCode.ShouldBe("corrupt");
    }

    [Fact]
    public void An_xml_with_a_dtd_is_refused()
    {
        const string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE order [ <!ENTITY name "ACME"> ]>
            <order><customer>&name;</customer></order>
            """;
        Should.Throw<DocumentExtractionException>(() => new XmlDocumentExtractor().Extract(Encoding.UTF8.GetBytes(xml), DocumentFormat.Xml))
            .ReasonCode.ShouldBe("dtdProhibited");
    }

    [Fact]
    public void An_xml_external_entity_is_never_resolved()
    {
        const string xxe = """
            <?xml version="1.0"?>
            <!DOCTYPE order [ <!ENTITY secret SYSTEM "file:///etc/passwd"> ]>
            <order><customer>&secret;</customer></order>
            """;
        var ex = Should.Throw<DocumentExtractionException>(() => XmlDocumentExtractor.LoadSafely(Encoding.UTF8.GetBytes(xxe)));
        ex.ReasonCode.ShouldBe("dtdProhibited");
    }

    [Fact]
    public void An_xml_over_twenty_megabytes_is_refused_before_parsing()
    {
        var big = new byte[OrderIntakeConsts.MaxXmlBytes + 1];
        Should.Throw<DocumentExtractionException>(() => XmlDocumentExtractor.LoadSafely(big)).ReasonCode.ShouldBe("tooLarge");
    }

    [Fact]
    public void A_broken_xml_is_corrupt()
    {
        Should.Throw<DocumentExtractionException>(() => XmlDocumentExtractor.LoadSafely(Encoding.UTF8.GetBytes("<order><open></order>"))).ReasonCode.ShouldBe("corrupt");
    }

    [Fact]
    public void Xml_text_keeps_namespaces_and_positional_paths()
    {
        const string xml = """<o:Orders xmlns:o="urn:x"><o:Order><o:Ref>A</o:Ref></o:Order><o:Order><o:Ref code="7">B</o:Ref></o:Order></o:Orders>""";
        var document = new XmlDocumentExtractor().Extract(Encoding.UTF8.GetBytes(xml), DocumentFormat.Xml);

        document.Lines.ShouldContain("namespace o = urn:x");
        document.Lines.ShouldContain("/Orders/Order[1]/Ref: A");
        document.Lines.ShouldContain("/Orders/Order[2]/Ref [@code=7]: B");
    }

    [Fact]
    public void Text_encodings_are_detected_from_the_bom_else_utf8_else_windows_1252()
    {
        TextEncodingDetector.Decode([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("Müller")]).ShouldBe("Müller");
        TextEncodingDetector.Decode([0xFF, 0xFE, .. Encoding.Unicode.GetBytes("Müller")]).ShouldBe("Müller");
        TextEncodingDetector.Decode([0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("Müller")]).ShouldBe("Müller");
        TextEncodingDetector.Decode(Encoding.UTF8.GetBytes("Café € 5")).ShouldBe("Café € 5");
        TextEncodingDetector.Decode([0x4D, 0xFC, 0x6C, 0x6C, 0x65, 0x72, 0x20, 0x80]).ShouldBe("Müller €", "invalid UTF-8 is Windows-1252");
    }

    [Theory]
    [InlineData("a;b;c\n1;2;3", ';')]
    [InlineData("a,b,c\n1,2,3", ',')]
    [InlineData("a\tb\tc\n1\t2\t3", '\t')]
    [InlineData("a|b|c\n1|2|3", '|')]
    [InlineData("a;b\n\"1,5\";\"2,5\"", ';')]
    public void The_csv_delimiter_is_sniffed(string text, char expected)
    {
        CsvDocumentExtractor.SniffDelimiter(text.Split('\n')).ShouldBe(expected);
    }

    [Fact]
    public void A_mail_body_loses_its_quoted_reply_and_its_signature()
    {
        var body = string.Join("\r\n",
            "Graag 3 pallets ophalen bij ons magazijn in Utrecht morgen.",
            "> eerder bericht",
            "-- ",
            "Jan Jansen, 06-12345678");
        var service = new DocumentExtractionService([new TextDocumentExtractor()]);

        var document = service.Extract(Encoding.UTF8.GetBytes(body), DocumentFormat.Text, InboundDocumentKind.MailBody);

        document.Lines.ShouldBe(["Graag 3 pallets ophalen bij ons magazijn in Utrecht morgen."]);

        var reply = "Zie bijlage.\n-----Original Message-----\nFrom: someone";
        service.Extract(Encoding.UTF8.GetBytes(reply), DocumentFormat.Text, InboundDocumentKind.MailBody).Lines.ShouldBe(["Zie bijlage."]);
    }

    [Fact]
    public void An_html_body_becomes_text_without_scripts_and_with_cells_two_spaces_apart()
    {
        var html = "<html><head><style>p{}</style></head><body><p>Order 12</p><table><tr><td>Laden</td><td>Utrecht</td></tr></table><script>alert(1)</script></body></html>";
        var text = TextNormalizer.HtmlToText(html);

        text.ShouldNotContain("alert");
        text.ShouldNotContain("p{}");
        TextNormalizer.NormalizeLines(text).ShouldContain("Order 12");
        TextNormalizer.NormalizeLines(text).ShouldContain("Laden  Utrecht");
    }

    [Fact]
    public void An_unsupported_format_is_refused_by_the_extraction_service()
    {
        Should.Throw<DocumentExtractionException>(() => new DocumentExtractionService([]).Extract([1, 2, 3], DocumentFormat.Unknown, InboundDocumentKind.Attachment))
            .ReasonCode.ShouldBe("unsupported");
    }

    [Theory]
    [InlineData("order.pdf", "application/octet-stream", "%PDF-1.4", DocumentFormat.Pdf)]
    [InlineData("orders.csv", "text/plain", "a;b", DocumentFormat.Csv)]
    [InlineData("orders.xml", null, "<a/>", DocumentFormat.Xml)]
    [InlineData(null, "text/xml", "<a/>", DocumentFormat.Xml)]
    [InlineData("noname", null, "<?xml version=\"1.0\"?><a/>", DocumentFormat.Xml)]
    [InlineData("invite.ics", "text/calendar", "BEGIN:VCALENDAR", DocumentFormat.Unknown)]
    [InlineData("fwd.eml", "message/rfc822", "From: a", DocumentFormat.Unknown)]
    [InlineData("page.html", null, "<html></html>", DocumentFormat.Html)]
    public void The_format_comes_from_the_content_then_the_name_then_the_type(string? fileName, string? contentType, string content, DocumentFormat expected)
    {
        DocumentFormatDetector.Detect(Encoding.UTF8.GetBytes(content), fileName, contentType).ShouldBe(expected);
    }

    [Fact]
    public void An_image_is_not_a_document_format()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, 0x00, 0xFF];
        DocumentFormatDetector.Detect(png, "logo.png", "image/png").ShouldBe(DocumentFormat.Unknown);
    }

    [Fact]
    public void The_fingerprint_of_a_layout_ignores_the_values_and_changes_with_the_headers()
    {
        var csv = new CsvDocumentExtractor();
        var a = csv.Extract(Encoding.UTF8.GetBytes("Ref;Van;Naar\nR-1;1234AB;5678CD"), DocumentFormat.Csv);
        var b = csv.Extract(Encoding.UTF8.GetBytes("Ref;Van;Naar\nR-9;1111AA;2222BB\nR-10;3333CC;4444DD"), DocumentFormat.Csv);
        var c = csv.Extract(Encoding.UTF8.GetBytes("Referentie;Van;Naar\nR-1;1234AB;5678CD"), DocumentFormat.Csv);

        a.Fingerprint.ShouldBe(b.Fingerprint);
        a.Fingerprint.ShouldNotBe(c.Fingerprint);

        var text1 = TextDocumentExtractor.FromText("Onze referentie: 1\nLaadadres\nKenmerk 1234", DocumentFormat.Text);
        var text2 = TextDocumentExtractor.FromText("Onze referentie: 2\nLaadadres\nKenmerk 9999", DocumentFormat.Text);
        text1.Fingerprint.ShouldBe(text2.Fingerprint, "label lines ignore digits and punctuation");
    }

    [Fact]
    public void The_sender_key_is_the_lowercased_address_and_api_clients_get_their_own()
    {
        DocumentFingerprinter.SenderKeyFromAddress("Orders@ACME.nl").ShouldBe("orders@acme.nl");
        DocumentFingerprinter.SenderKeyFromAddress(null).ShouldBeNull();
        DocumentFingerprinter.ApiSenderKey("otms_ABC").ShouldBe("api:otms_abc");
    }

    [Fact]
    public void A_spreadsheet_shows_the_ai_its_header_and_at_most_forty_rows()
    {
        var csvText = new StringBuilder("Ref;Plaats\n");
        for (var i = 1; i <= 60; i++)
        {
            csvText.Append("R-").Append(i).Append(";Utrecht\n");
        }

        var document = new CsvDocumentExtractor().Extract(Encoding.UTF8.GetBytes(csvText.ToString()), DocumentFormat.Csv);
        var text = document.ToNumberedText(40, out var truncated);

        truncated.ShouldBeTrue();
        text.ShouldContain("R-40");
        text.ShouldNotContain("R-41");
        document.ToNumberedText(null, out var whole).ShouldContain("R-60");
        whole.ShouldBeFalse();
    }
}
