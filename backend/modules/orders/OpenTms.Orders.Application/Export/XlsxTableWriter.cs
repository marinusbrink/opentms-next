using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace OpenTms.Orders.Export;

/// <summary>How a column's values are written and formatted in the sheet.</summary>
public enum XlsxCellKind
{
    Text = 0,
    Integer,
    Number,
    Amount,
    Percent,
    Date,
    DateTime,
    Boolean
}

/// <summary>A column of an exported table: the header the user reads and the kind of its values.</summary>
public sealed record XlsxColumn(string Header, XlsxCellKind Kind);

/// <summary>
/// The grid export of UI-GRID-013 (design 61): one sheet with a bold header row, an auto filter and one row per record,
/// written forward-only (OpenXML SAX writer) into a temporary file that deletes itself when the returned stream is closed,
/// so a large export never holds the workbook in memory. Strings are inline strings (never formulas); characters XML
/// cannot carry are dropped and texts are cut at Excel's cell limit. Values: string, numbers, <see cref="DateOnly"/>,
/// <see cref="System.DateTime"/> (already in the reader's zone), bool; null leaves the cell empty.
/// </summary>
public static class XlsxTableWriter
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const int MaxCellText = 32767;
    private const int MaxSheetNameLength = 31;

    /* Indexes into the CellFormats of BuildStylesheet. */
    private const uint HeaderStyle = 1;
    private const uint DateStyle = 2;
    private const uint DateTimeStyle = 3;
    private const uint AmountStyle = 4;
    private const uint NumberStyle = 5;
    private const uint PercentStyle = 6;

    public static async Task<Stream> WriteAsync(string sheetName, IReadOnlyList<XlsxColumn> columns, IAsyncEnumerable<IReadOnlyList<object?>> rows, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Path.GetTempPath(), "opentms-export-" + Guid.NewGuid().ToString("N") + ".xlsx");
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
        try
        {
            using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, autoSave: true))
            {
                var workbookPart = document.AddWorkbookPart();
                var styles = workbookPart.AddNewPart<WorkbookStylesPart>();
                styles.Stylesheet = BuildStylesheet();
                styles.Stylesheet.Save();

                var sheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var rowIndex = 1u;
                using (var writer = OpenXmlWriter.Create(sheetPart))
                {
                    writer.WriteStartElement(new Worksheet());
                    writer.WriteStartElement(new Columns());
                    for (var c = 0; c < columns.Count; c++)
                    {
                        writer.WriteElement(new Column { Min = (uint)c + 1, Max = (uint)c + 1, Width = Width(columns[c]), CustomWidth = true });
                    }

                    writer.WriteEndElement();
                    writer.WriteStartElement(new SheetData());
                    writer.WriteElement(HeaderRow(columns));
                    await foreach (var values in rows.WithCancellation(cancellationToken))
                    {
                        rowIndex++;
                        writer.WriteElement(DataRow(rowIndex, columns, values));
                    }

                    writer.WriteEndElement();
                    if (columns.Count > 0)
                    {
                        writer.WriteElement(new AutoFilter { Reference = $"A1:{ColumnName(columns.Count - 1)}{rowIndex}" });
                    }

                    writer.WriteEndElement();
                }

                workbookPart.Workbook = new Workbook(new Sheets(new Sheet { Id = workbookPart.GetIdOfPart(sheetPart), SheetId = 1, Name = SheetName(sheetName) }));
                workbookPart.Workbook.Save();
            }

            stream.Position = 0;
            return stream;
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    /// <summary>A1-style column letters for a zero-based index (0 → A, 26 → AA).</summary>
    public static string ColumnName(int index)
    {
        var name = new StringBuilder();
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name.Insert(0, (char)('A' + (n - 1) % 26));
        }

        return name.ToString();
    }

    private static Row HeaderRow(IReadOnlyList<XlsxColumn> columns)
    {
        var row = new Row { RowIndex = 1 };
        for (var c = 0; c < columns.Count; c++)
        {
            var cell = TextCell(columns[c].Header);
            cell.CellReference = ColumnName(c) + "1";
            cell.StyleIndex = HeaderStyle;
            row.Append(cell);
        }

        return row;
    }

    private static Row DataRow(uint rowIndex, IReadOnlyList<XlsxColumn> columns, IReadOnlyList<object?> values)
    {
        var row = new Row { RowIndex = rowIndex };
        for (var c = 0; c < columns.Count && c < values.Count; c++)
        {
            var cell = ValueCell(columns[c].Kind, values[c]);
            if (cell == null)
            {
                continue;
            }

            cell.CellReference = ColumnName(c) + rowIndex;
            row.Append(cell);
        }

        return row;
    }

    private static Cell? ValueCell(XlsxCellKind kind, object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string text:
                return text.Length == 0 ? null : TextCell(text);
            case bool flag:
                return new Cell { DataType = CellValues.Boolean, CellValue = new CellValue(flag) };
            case DateOnly date:
                return new Cell { CellValue = new CellValue(date.ToDateTime(TimeOnly.MinValue).ToOADate()), StyleIndex = kind == XlsxCellKind.DateTime ? DateTimeStyle : DateStyle };
            case DateTime moment:
                return new Cell { CellValue = new CellValue(moment.ToOADate()), StyleIndex = kind == XlsxCellKind.Date ? DateStyle : DateTimeStyle };
            case decimal number:
                return new Cell { CellValue = new CellValue(number), StyleIndex = NumericStyle(kind) };
            case double number:
                return double.IsFinite(number) ? new Cell { CellValue = new CellValue(number), StyleIndex = NumericStyle(kind) } : null;
            case float number:
                return float.IsFinite(number) ? new Cell { CellValue = new CellValue((double)number), StyleIndex = NumericStyle(kind) } : null;
            case int number:
                return new Cell { CellValue = new CellValue(number), StyleIndex = NumericStyle(kind) };
            case long number:
                return new Cell { CellValue = new CellValue((decimal)number), StyleIndex = NumericStyle(kind) };
            default:
                return TextCell(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }

    private static uint NumericStyle(XlsxCellKind kind) => kind switch
    {
        XlsxCellKind.Amount => AmountStyle,
        XlsxCellKind.Percent => PercentStyle,
        _ => NumberStyle
    };

    private static Cell TextCell(string value)
        => new() { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(Clean(value)) { Space = SpaceProcessingModeValues.Preserve }) };

    /// <summary>Drops characters XML 1.0 cannot carry (control characters, lone surrogates) and cuts at Excel's cell limit.</summary>
    public static string Clean(string value)
    {
        var builder = new StringBuilder(Math.Min(value.Length, MaxCellText));
        for (var i = 0; i < value.Length && builder.Length < MaxCellText; i++)
        {
            var ch = value[i];
            if (char.IsHighSurrogate(ch) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                if (builder.Length + 2 > MaxCellText)
                {
                    break;
                }

                builder.Append(ch).Append(value[++i]);
            }
            else if (XmlConvert.IsXmlChar(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static string SheetName(string name)
    {
        var cleaned = new string(Clean(name).Where(ch => ch is not ('[' or ']' or ':' or '*' or '?' or '/' or '\\')).ToArray()).Trim('\'').Trim();
        if (cleaned.Length == 0)
        {
            cleaned = "Export";
        }

        return cleaned.Length <= MaxSheetNameLength ? cleaned : cleaned[..MaxSheetNameLength];
    }

    private static DoubleValue Width(XlsxColumn column) => column.Kind switch
    {
        XlsxCellKind.Date => 12,
        XlsxCellKind.DateTime => 17,
        XlsxCellKind.Boolean => 10,
        XlsxCellKind.Integer or XlsxCellKind.Number or XlsxCellKind.Amount or XlsxCellKind.Percent => Math.Max(12, Math.Min(column.Header.Length + 2, 24)),
        _ => Math.Max(14, Math.Min(column.Header.Length + 4, 40))
    };

    private static Stylesheet BuildStylesheet() => new(
        new NumberingFormats(
            new NumberingFormat { NumberFormatId = 164, FormatCode = "yyyy-mm-dd" },
            new NumberingFormat { NumberFormatId = 165, FormatCode = "yyyy-mm-dd hh:mm" }) { Count = 2 },
        new Fonts(new Font(), new Font(new Bold())) { Count = 2 },
        new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 },
        new Borders(new Border()) { Count = 1 },
        new CellStyleFormats(new CellFormat()) { Count = 1 },
        new CellFormats(
            new CellFormat(),
            new CellFormat { FontId = 1, ApplyFont = true },
            new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true },
            new CellFormat { NumberFormatId = 165, ApplyNumberFormat = true },
            new CellFormat { NumberFormatId = 4, ApplyNumberFormat = true },
            new CellFormat { NumberFormatId = 0 },
            new CellFormat { NumberFormatId = 10, ApplyNumberFormat = true }) { Count = 7 });
}
