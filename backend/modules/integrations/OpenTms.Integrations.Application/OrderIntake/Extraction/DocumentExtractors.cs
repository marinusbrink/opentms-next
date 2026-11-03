using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ClosedXML.Excel;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Integrations.OrderIntake.Extraction;

/// <summary>Reads one format into an <see cref="ExtractedDocument"/> (design 72 §3). Pure: bytes in, text out, no I/O.</summary>
public interface IDocumentExtractor
{
    bool CanRead(DocumentFormat format);

    ExtractedDocument Extract(byte[] content, DocumentFormat format);
}

/// <summary>The document could not be read (corrupt, encrypted, too large, unsafe); the document fails with the reason code.</summary>
public class DocumentExtractionException : Exception
{
    /// <summary>A short code for the trace: "corrupt", "encrypted", "tooLarge", "unsupported", "dtdProhibited".</summary>
    public string ReasonCode { get; }

    public DocumentExtractionException(string reasonCode, string message, Exception? inner = null)
        : base(message, inner)
    {
        ReasonCode = reasonCode;
    }
}

/// <summary>Chooses the extractor of a format; a mail body loses its quoted reply and signature first.</summary>
public class DocumentExtractionService : ITransientDependency
{
    private readonly IEnumerable<IDocumentExtractor> _extractors;

    public DocumentExtractionService(IEnumerable<IDocumentExtractor> extractors)
    {
        _extractors = extractors;
    }

    public ExtractedDocument Extract(byte[] content, DocumentFormat format, InboundDocumentKind kind)
    {
        if (kind == InboundDocumentKind.MailBody && format is DocumentFormat.Text or DocumentFormat.Html)
        {
            var text = TextEncodingDetector.Decode(content);
            if (format == DocumentFormat.Html)
            {
                text = TextNormalizer.HtmlToText(text);
            }

            return TextDocumentExtractor.FromText(TextNormalizer.StripQuotedReplyAndSignature(text), format);
        }

        var extractor = _extractors.FirstOrDefault(e => e.CanRead(format))
                        ?? throw new DocumentExtractionException("unsupported", $"No extractor reads {format}.");
        return extractor.Extract(content, format);
    }
}

/// <summary>PDF with PdfPig: words grouped into lines top to bottom, left to right; a wide gap becomes two spaces (a column); pages separated by a form-feed line. Fewer than 20 characters per page on average → ScannedPdf.</summary>
public class PdfDocumentExtractor : IDocumentExtractor, ITransientDependency
{
    public const int MinCharactersPerPage = 20;

    public bool CanRead(DocumentFormat format) => format is DocumentFormat.Pdf or DocumentFormat.ScannedPdf;

    public ExtractedDocument Extract(byte[] content, DocumentFormat format)
    {
        var lines = new List<string>();
        int pageCount;
        var characters = 0;
        try
        {
            using var document = PdfDocument.Open(content);
            pageCount = document.NumberOfPages;
            var first = true;
            foreach (var page in document.GetPages())
            {
                if (!first)
                {
                    lines.Add(ExtractedDocument.PageBreak);
                }

                first = false;
                foreach (var line in PageLines(page))
                {
                    characters += line.Count(c => !char.IsWhiteSpace(c));
                    lines.Add(line);
                }
            }
        }
        catch (Exception ex) when (ex is not DocumentExtractionException)
        {
            var encrypted = ex.GetType().Name.Contains("Encrypt", StringComparison.OrdinalIgnoreCase)
                            || ex.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase)
                            || ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase);
            throw new DocumentExtractionException(encrypted ? "encrypted" : "corrupt", "The PDF cannot be read.", ex);
        }

        var scanned = pageCount == 0 || characters / (double)pageCount < MinCharactersPerPage;
        var fingerprint = DocumentFingerprinter.ForText(lines);
        return new ExtractedDocument
        {
            Format = scanned ? DocumentFormat.ScannedPdf : DocumentFormat.Pdf,
            Lines = lines,
            PageCount = pageCount,
            Fingerprint = fingerprint.Fingerprint,
            FingerprintDescription = fingerprint.Description,
            SignatureTokens = fingerprint.SignatureTokens
        };
    }

    private static IEnumerable<string> PageLines(Page page)
    {
        var words = page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
        if (words.Count == 0)
        {
            yield break;
        }

        /* Group by baseline: a word joins the current line when its vertical centre lies within half the line's height. */
        var ordered = words.OrderByDescending(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left).ToList();
        var groups = new List<List<Word>>();
        foreach (var word in ordered)
        {
            var centre = (word.BoundingBox.Bottom + word.BoundingBox.Top) / 2;
            var group = groups.LastOrDefault();
            if (group != null)
            {
                var reference = group[0].BoundingBox;
                var tolerance = Math.Max(reference.Height, word.BoundingBox.Height) / 2;
                if (Math.Abs(centre - (reference.Bottom + reference.Top) / 2) <= Math.Max(tolerance, 1))
                {
                    group.Add(word);
                    continue;
                }
            }

            groups.Add(new List<Word> { word });
        }

        foreach (var group in groups)
        {
            var sorted = group.OrderBy(w => w.BoundingBox.Left).ToList();
            var builder = new StringBuilder();
            for (var i = 0; i < sorted.Count; i++)
            {
                if (i > 0)
                {
                    var previous = sorted[i - 1];
                    var gap = sorted[i].BoundingBox.Left - previous.BoundingBox.Right;
                    var charWidth = previous.Text.Length == 0 ? 5 : previous.BoundingBox.Width / previous.Text.Length;
                    builder.Append(gap > Math.Max(charWidth, 1) * 2 ? "  " : " ");
                }

                builder.Append(sorted[i].Text);
            }

            var line = TextNormalizer.NormalizeLine(builder.ToString());
            if (line.Length > 0)
            {
                yield return line;
            }
        }
    }
}

/// <summary>xlsx with ClosedXML: per sheet the used range as rows of formatted cell texts with A1 references.</summary>
public class XlsxDocumentExtractor : IDocumentExtractor, ITransientDependency
{
    public bool CanRead(DocumentFormat format) => format == DocumentFormat.Xlsx;

    public ExtractedDocument Extract(byte[] content, DocumentFormat format)
    {
        var sheets = new List<ExtractedSheet>();
        var lines = new List<string>();
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var workbook = new XLWorkbook(stream);
            foreach (var worksheet in workbook.Worksheets)
            {
                var range = worksheet.RangeUsed();
                if (range == null)
                {
                    continue;
                }

                var rows = new List<ExtractedRow>();
                foreach (var row in range.RowsUsed())
                {
                    var cells = row.CellsUsed()
                        .Select(c => new ExtractedCell
                        {
                            Column = c.Address.ColumnNumber,
                            Reference = CellReference.Format(c.Address.ColumnNumber, c.Address.RowNumber),
                            Text = TextNormalizer.NormalizeLine(c.GetFormattedString())
                        })
                        .Where(c => c.Text.Length > 0)
                        .ToList();
                    if (cells.Count == 0)
                    {
                        continue;
                    }

                    var extracted = new ExtractedRow { RowNumber = row.RowNumber(), LineNumber = lines.Count + 1, Cells = cells };
                    rows.Add(extracted);
                    lines.Add(extracted.Text);
                }

                if (rows.Count > 0)
                {
                    sheets.Add(new ExtractedSheet { Name = worksheet.Name, Rows = rows });
                }
            }
        }
        catch (Exception ex)
        {
            throw new DocumentExtractionException("corrupt", "The spreadsheet cannot be read.", ex);
        }

        var fingerprint = DocumentFingerprinter.ForSheets(sheets);
        return new ExtractedDocument
        {
            Format = DocumentFormat.Xlsx,
            Lines = lines,
            Sheets = sheets,
            Fingerprint = fingerprint.Fingerprint,
            FingerprintDescription = fingerprint.Description,
            SignatureTokens = fingerprint.SignatureTokens
        };
    }
}

/// <summary>CSV: encoding detected (BOM, else UTF-8, else Windows-1252), delimiter sniffed (; , tab |), quotes honoured; one sheet.</summary>
public class CsvDocumentExtractor : IDocumentExtractor, ITransientDependency
{
    private static readonly char[] Delimiters = [';', ',', '\t', '|'];

    public bool CanRead(DocumentFormat format) => format == DocumentFormat.Csv;

    public ExtractedDocument Extract(byte[] content, DocumentFormat format)
    {
        var text = TextEncodingDetector.Decode(content);
        var rawLines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var delimiter = SniffDelimiter(rawLines);
        var rows = new List<ExtractedRow>();
        var lines = new List<string>();
        var rowNumber = 0;
        foreach (var fields in ParseRecords(text, delimiter))
        {
            rowNumber++;
            var cells = fields
                .Select((value, index) => new ExtractedCell
                {
                    Column = index + 1,
                    Reference = CellReference.Format(index + 1, rowNumber),
                    Text = TextNormalizer.NormalizeLine(value)
                })
                .Where(c => c.Text.Length > 0)
                .ToList();
            if (cells.Count == 0)
            {
                continue;
            }

            var row = new ExtractedRow { RowNumber = rowNumber, LineNumber = lines.Count + 1, Cells = cells };
            rows.Add(row);
            lines.Add(row.Text);
        }

        var sheets = new List<ExtractedSheet> { new() { Name = "csv", Rows = rows } };
        var fingerprint = DocumentFingerprinter.ForSheets(sheets);
        return new ExtractedDocument
        {
            Format = DocumentFormat.Csv,
            Lines = lines,
            Sheets = sheets,
            Fingerprint = fingerprint.Fingerprint,
            FingerprintDescription = fingerprint.Description,
            SignatureTokens = fingerprint.SignatureTokens
        };
    }

    /// <summary>The candidate that splits the first 20 non-empty lines most consistently into more than one field.</summary>
    public static char SniffDelimiter(IReadOnlyList<string> lines)
    {
        var sample = lines.Where(l => l.Trim().Length > 0).Take(20).ToList();
        var best = ';';
        var bestScore = -1.0;
        foreach (var candidate in Delimiters)
        {
            var counts = sample.Select(l => CountOutsideQuotes(l, candidate)).ToList();
            if (counts.Count == 0 || counts.All(c => c == 0))
            {
                continue;
            }

            var mode = counts.GroupBy(c => c).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First();
            if (mode.Key == 0)
            {
                continue;
            }

            var score = mode.Count() / (double)counts.Count * 1000 + mode.Key;
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private static int CountOutsideQuotes(string line, char delimiter)
    {
        var count = 0;
        var quoted = false;
        foreach (var c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == delimiter && !quoted)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>RFC 4180 records: quoted fields may hold the delimiter, doubled quotes and line breaks.</summary>
    public static IEnumerable<List<string>> ParseRecords(string text, char delimiter)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (c == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                fields.Add(field.ToString());
                field.Clear();
                yield return fields;
                fields = new List<string>();
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return fields;
        }
    }
}

/// <summary>
/// XML: parsed safely (DTD processing prohibited, no resolver, so no external entities; at most 20 MB of characters),
/// namespaces kept. The normalised text lists every element with text as "path: value", the path with positions.
/// </summary>
public class XmlDocumentExtractor : IDocumentExtractor, ITransientDependency
{
    public bool CanRead(DocumentFormat format) => format == DocumentFormat.Xml;

    public static XDocument LoadSafely(byte[] content)
    {
        if (content.LongLength > OrderIntakeConsts.MaxXmlBytes)
        {
            throw new DocumentExtractionException("tooLarge", "The XML document is too large.");
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = OrderIntakeConsts.MaxXmlBytes,
            MaxCharactersFromEntities = 1024,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var reader = XmlReader.Create(stream, settings);
            return XDocument.Load(reader, System.Xml.Linq.LoadOptions.None);
        }
        catch (XmlException ex) when (ex.Message.Contains("DTD", StringComparison.OrdinalIgnoreCase))
        {
            throw new DocumentExtractionException("dtdProhibited", "The XML document declares a DTD.", ex);
        }
        catch (XmlException ex)
        {
            throw new DocumentExtractionException("corrupt", "The XML document cannot be read.", ex);
        }
    }

    public ExtractedDocument Extract(byte[] content, DocumentFormat format)
    {
        var document = LoadSafely(content);
        var lines = new List<string>();
        if (document.Root != null)
        {
            foreach (var ns in document.Root.Attributes().Where(a => a.IsNamespaceDeclaration))
            {
                lines.Add($"namespace {(ns.Name.Namespace == XNamespace.None ? "(default)" : ns.Name.LocalName)} = {ns.Value}");
            }

            foreach (var element in document.Root.DescendantsAndSelf())
            {
                var value = element.HasElements ? null : TextNormalizer.NormalizeLine(element.Value);
                var attributes = element.Attributes().Where(a => !a.IsNamespaceDeclaration).ToList();
                if (string.IsNullOrEmpty(value) && attributes.Count == 0)
                {
                    continue;
                }

                var line = XmlPaths.PositionalPath(element);
                if (attributes.Count > 0)
                {
                    line += " [" + string.Join(", ", attributes.Select(a => "@" + a.Name.LocalName + "=" + TextNormalizer.NormalizeLine(a.Value))) + "]";
                }

                if (!string.IsNullOrEmpty(value))
                {
                    line += ": " + value;
                }

                lines.Add(line);
            }
        }

        var fingerprint = DocumentFingerprinter.ForXml(document);
        return new ExtractedDocument
        {
            Format = DocumentFormat.Xml,
            Lines = lines,
            Xml = document,
            Fingerprint = fingerprint.Fingerprint,
            FingerprintDescription = fingerprint.Description,
            SignatureTokens = fingerprint.SignatureTokens
        };
    }
}

/// <summary>Plain text and HTML: normalised text lines.</summary>
public class TextDocumentExtractor : IDocumentExtractor, ITransientDependency
{
    public bool CanRead(DocumentFormat format) => format is DocumentFormat.Text or DocumentFormat.Html;

    public ExtractedDocument Extract(byte[] content, DocumentFormat format)
    {
        var text = TextEncodingDetector.Decode(content);
        return FromText(format == DocumentFormat.Html ? TextNormalizer.HtmlToText(text) : text, format);
    }

    public static ExtractedDocument FromText(string text, DocumentFormat format)
    {
        var lines = TextNormalizer.NormalizeLines(text);
        var fingerprint = DocumentFingerprinter.ForText(lines);
        return new ExtractedDocument
        {
            Format = format,
            Lines = lines,
            Fingerprint = fingerprint.Fingerprint,
            FingerprintDescription = fingerprint.Description,
            SignatureTokens = fingerprint.SignatureTokens
        };
    }
}

/// <summary>BOM first (UTF-8, UTF-16 LE/BE), else strict UTF-8, else Windows-1252.</summary>
public static class TextEncodingDetector
{
    static TextEncodingDetector()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static string Decode(byte[] content)
    {
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(content, 3, content.Length - 3);
        }

        if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(content, 2, content.Length - 2);
        }

        if (content.Length >= 2 && content[0] == 0xFE && content[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(content, 2, content.Length - 2);
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(content);
        }
    }
}

/// <summary>Element paths as the trace shows them: <c>/Order[2]/Lines/Line[1]/Weight</c> (local names, a position where siblings share the name).</summary>
public static class XmlPaths
{
    public static string PositionalPath(XElement element)
    {
        var parts = new List<string>();
        for (var current = element; current != null; current = current.Parent)
        {
            var name = current.Name.LocalName;
            if (current.Parent != null)
            {
                var siblings = current.Parent.Elements(current.Name).ToList();
                if (siblings.Count > 1)
                {
                    name += "[" + (siblings.IndexOf(current) + 1).ToString(CultureInfo.InvariantCulture) + "]";
                }
            }

            parts.Add(name);
        }

        parts.Reverse();
        return "/" + string.Join("/", parts);
    }
}
