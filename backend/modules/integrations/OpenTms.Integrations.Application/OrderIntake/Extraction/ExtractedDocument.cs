using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace OpenTms.Integrations.OrderIntake.Extraction;

/// <summary>
/// What an extractor read from one document (design 72 §3). <see cref="Lines"/> is the <b>normalised text</b>, numbered
/// from 1: the only text the profile rules (label, regex) and the AI refer to, so a profile run and the AI see the same
/// thing. Tabular formats also carry their <see cref="Sheets"/> (cells with A1 references); XML carries its DOM.
/// </summary>
public sealed class ExtractedDocument
{
    /// <summary>The settled format: a PDF without a usable text layer is <see cref="DocumentFormat.ScannedPdf"/>.</summary>
    public DocumentFormat Format { get; init; }

    /// <summary>The normalised text lines; line n of the trace is <c>Lines[n - 1]</c>. A page break is a form-feed line.</summary>
    public IReadOnlyList<string> Lines { get; init; } = [];

    /// <summary>Spreadsheets and CSV: the used range of each sheet.</summary>
    public IReadOnlyList<ExtractedSheet> Sheets { get; init; } = [];

    /// <summary>XML: the document, namespaces kept.</summary>
    public XDocument? Xml { get; init; }

    /// <summary>PDF: the number of pages.</summary>
    public int PageCount { get; init; }

    /// <summary>Hex SHA-256 of the layout (headers, element paths or label lines).</summary>
    public string Fingerprint { get; init; } = string.Empty;

    /// <summary>A short description of the fingerprint for the trace (no values, only layout words).</summary>
    public string FingerprintDescription { get; init; } = string.Empty;

    /// <summary>The headers, element paths or label lines a profile signature may name (case-insensitive).</summary>
    public IReadOnlyCollection<string> SignatureTokens { get; init; } = [];

    public const string PageBreak = "\f";

    /// <summary>Whether the document holds <paramref name="entry"/>: a signature token, or text on one of its lines.</summary>
    public bool Contains(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return true;
        }

        var trimmed = entry.Trim();
        return SignatureTokens.Contains(trimmed, StringComparer.OrdinalIgnoreCase)
               || Lines.Any(l => l.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The text the AI reads: every line prefixed with its number. Spreadsheets are rendered per sheet with their cell
    /// references, at most <paramref name="maxDataRowsPerSheet"/> rows after the first (header) row of each sheet.
    /// </summary>
    public string ToNumberedText(int? maxDataRowsPerSheet, out bool truncated)
    {
        truncated = false;
        var builder = new StringBuilder();
        if (Sheets.Count > 0)
        {
            foreach (var sheet in Sheets)
            {
                builder.Append("[sheet \"").Append(sheet.Name).Append("\", ").Append(sheet.Rows.Count).AppendLine(" rows]");
                var take = maxDataRowsPerSheet == null ? sheet.Rows.Count : Math.Min(sheet.Rows.Count, maxDataRowsPerSheet.Value + 1);
                if (take < sheet.Rows.Count)
                {
                    truncated = true;
                }

                foreach (var row in sheet.Rows.Take(take))
                {
                    builder.Append(row.LineNumber).Append(": ");
                    builder.AppendJoin(" | ", row.Cells.Select(c => c.Reference + "=" + c.Text));
                    builder.AppendLine();
                }

                if (take < sheet.Rows.Count)
                {
                    builder.Append("[… ").Append(sheet.Rows.Count - take).AppendLine(" more rows not shown; the profile runs on all rows]");
                }
            }

            return builder.ToString();
        }

        for (var i = 0; i < Lines.Count; i++)
        {
            builder.Append(i + 1).Append(": ").AppendLine(Lines[i] == PageBreak ? "[page break]" : Lines[i]);
        }

        return builder.ToString();
    }

    /// <summary>The text of line <paramref name="lineNumber"/> (1-based), or null.</summary>
    public string? Line(int lineNumber) => lineNumber >= 1 && lineNumber <= Lines.Count ? Lines[lineNumber - 1] : null;

    /// <summary>The cell at an A1 reference on the named (or first) sheet.</summary>
    public ExtractedCell? FindCell(string? sheetName, string reference)
    {
        var sheet = FindSheet(sheetName);
        if (sheet == null || !CellReference.TryParse(reference, out var column, out var row))
        {
            return null;
        }

        return sheet.Rows.FirstOrDefault(r => r.RowNumber == row)?.Cells.FirstOrDefault(c => c.Column == column);
    }

    public ExtractedSheet? FindSheet(string? sheetName)
        => string.IsNullOrWhiteSpace(sheetName)
            ? Sheets.FirstOrDefault()
            : Sheets.FirstOrDefault(s => string.Equals(s.Name, sheetName.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed class ExtractedSheet
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Non-empty rows in sheet order.</summary>
    public IReadOnlyList<ExtractedRow> Rows { get; init; } = [];
}

public sealed class ExtractedRow
{
    /// <summary>The 1-based row number in the sheet.</summary>
    public int RowNumber { get; init; }

    /// <summary>The 1-based line of the normalised text this row is.</summary>
    public int LineNumber { get; init; }

    /// <summary>Non-empty cells, left to right.</summary>
    public IReadOnlyList<ExtractedCell> Cells { get; init; } = [];

    /// <summary>The row as one text line: the cell texts separated by two spaces (the scope text of label and regex rules).</summary>
    public string Text => string.Join("  ", Cells.Select(c => c.Text));
}

public sealed class ExtractedCell
{
    /// <summary>1-based column.</summary>
    public int Column { get; init; }

    /// <summary>"B7".</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>The formatted value as the sheet shows it, trimmed.</summary>
    public string Text { get; init; } = string.Empty;
}

/// <summary>A1 references.</summary>
public static class CellReference
{
    public static string Format(int column, int row) => ColumnName(column) + row.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string ColumnName(int column)
    {
        var name = string.Empty;
        while (column > 0)
        {
            var remainder = (column - 1) % 26;
            name = (char)('A' + remainder) + name;
            column = (column - 1) / 26;
        }

        return name;
    }

    public static bool TryParse(string? reference, out int column, out int row)
    {
        column = 0;
        row = 0;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var text = reference.Trim().Replace("$", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var i = 0;
        while (i < text.Length && text[i] is >= 'A' and <= 'Z')
        {
            column = column * 26 + (text[i] - 'A' + 1);
            i++;
            if (column > 16_384)
            {
                return false;
            }
        }

        if (i == 0 || i == text.Length)
        {
            return false;
        }

        return int.TryParse(text[i..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out row) && row > 0;
    }
}
