using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Volo.Abp;

namespace OpenTms.Financial.Tariffs;

/// <summary>
/// The xlsx shape of a price list's rules (design 57 "API contract", export and import; Assumption 4): one sheet
/// "Rules" with one row per bracket — the rule's position, lane, dimensions, row quantity and min/max repeated per
/// row, the bracket's upper bound and one price column per column of the list — and a sheet "Columns" with the
/// column bounds for reference. Import reads "Rules" only and validates every cell; an error names its row and column.
/// </summary>
public static class PriceListWorkbook
{
    public const string RulesSheet = "Rules";
    public const string ColumnsSheet = "Columns";

    private static readonly string[] FixedHeaders =
    [
        "Position", "FromRelationId", "FromRegionId", "ToRelationId", "ToRegionId", "TransportUnitId", "ProductId", "VehicleTypeId",
        "RowQuantity", "MinAmount", "MaxAmount", "BracketUpperBound"
    ];

    /// <summary>A rule as the import reads it, before it becomes an entity.</summary>
    public sealed record ImportedRule(
        int Position,
        Guid? FromRelationId, Guid? FromRegionId, Guid? ToRelationId, Guid? ToRegionId,
        Guid? TransportUnitId, Guid? ProductId, Guid? VehicleTypeId,
        QuantitySource RowQuantity, decimal? MinAmount, decimal? MaxAmount,
        List<(decimal? UpperBound, List<decimal?> UnitPrices)> Brackets);

    public static byte[] Export(PriceList list, IReadOnlyList<PriceListRule> rules)
    {
        using var workbook = new XLWorkbook();
        var columns = list.Columns.OrderBy(c => c.Position).ToList();

        var sheet = workbook.Worksheets.Add(RulesSheet);
        var header = 1;
        foreach (var title in FixedHeaders)
        {
            sheet.Cell(1, header++).Value = title;
        }

        foreach (var column in columns)
        {
            sheet.Cell(1, header++).Value = "Price" + column.Position;
        }

        var row = 2;
        foreach (var rule in rules.OrderBy(r => r.Position))
        {
            foreach (var bracket in rule.Brackets.OrderBy(b => b.Position))
            {
                var cell = 1;
                sheet.Cell(row, cell++).Value = rule.Position;
                sheet.Cell(row, cell++).Value = rule.FromRelationId?.ToString() ?? string.Empty;
                sheet.Cell(row, cell++).Value = rule.FromRegionId?.ToString() ?? string.Empty;
                sheet.Cell(row, cell++).Value = rule.ToRelationId?.ToString() ?? string.Empty;
                sheet.Cell(row, cell++).Value = rule.ToRegionId?.ToString() ?? string.Empty;
                sheet.Cell(row, cell++).Value = rule.TransportUnitId?.ToString() ?? string.Empty;
                sheet.Cell(row, cell++).Value = rule.ProductId?.ToString() ?? string.Empty;
                sheet.Cell(row, cell++).Value = rule.VehicleTypeId?.ToString() ?? string.Empty;
                sheet.Cell(row, cell++).Value = rule.RowQuantity.ToString();
                SetDecimal(sheet.Cell(row, cell++), rule.MinAmount);
                SetDecimal(sheet.Cell(row, cell++), rule.MaxAmount);
                SetDecimal(sheet.Cell(row, cell++), bracket.UpperBound);
                foreach (var column in columns)
                {
                    SetDecimal(sheet.Cell(row, cell++), bracket.UnitPriceAt(column.Position));
                }

                row++;
            }
        }

        var columnsSheet = workbook.Worksheets.Add(ColumnsSheet);
        columnsSheet.Cell(1, 1).Value = "Position";
        columnsSheet.Cell(1, 2).Value = "UpperBound";
        for (var i = 0; i < columns.Count; i++)
        {
            columnsSheet.Cell(i + 2, 1).Value = columns[i].Position;
            SetDecimal(columnsSheet.Cell(i + 2, 2), columns[i].UpperBound);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Reads the rules; every problem is <c>Financial:ImportInvalid</c> with the row and column it was found in.</summary>
    public static List<ImportedRule> Import(Stream stream, int columnCount)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception ex) when (ex is not BusinessException)
        {
            throw Invalid(0, 0, "NotAWorkbook");
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault(s => s.Name == RulesSheet) ?? throw Invalid(0, 0, "RulesSheetMissing");
            var expectedHeaders = FixedHeaders.Concat(Enumerable.Range(1, columnCount).Select(i => "Price" + i)).ToList();
            for (var i = 0; i < expectedHeaders.Count; i++)
            {
                if (!string.Equals(sheet.Cell(1, i + 1).GetString().Trim(), expectedHeaders[i], StringComparison.OrdinalIgnoreCase))
                {
                    throw Invalid(1, i + 1, "Header");
                }
            }

            if (!sheet.Cell(1, expectedHeaders.Count + 1).IsEmpty())
            {
                throw Invalid(1, expectedHeaders.Count + 1, "ColumnCount");
            }

            var rules = new List<ImportedRule>();
            ImportedRule? current = null;
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
            for (var row = 2; row <= lastRow; row++)
            {
                if (Enumerable.Range(1, expectedHeaders.Count).All(c => sheet.Cell(row, c).IsEmpty()))
                {
                    continue;
                }

                var position = ReadInt(sheet, row, 1);
                if (current == null || current.Position != position)
                {
                    if (rules.Any(r => r.Position == position))
                    {
                        throw Invalid(row, 1, "PositionNotGrouped");
                    }

                    current = new ImportedRule(
                        position,
                        ReadGuid(sheet, row, 2), ReadGuid(sheet, row, 3), ReadGuid(sheet, row, 4), ReadGuid(sheet, row, 5),
                        ReadGuid(sheet, row, 6), ReadGuid(sheet, row, 7), ReadGuid(sheet, row, 8),
                        ReadQuantitySource(sheet, row, 9), ReadDecimal(sheet, row, 10), ReadDecimal(sheet, row, 11),
                        new List<(decimal?, List<decimal?>)>());
                    rules.Add(current);
                }

                var prices = new List<decimal?>();
                for (var c = 0; c < columnCount; c++)
                {
                    prices.Add(ReadDecimal(sheet, row, FixedHeaders.Length + 1 + c));
                }

                current.Brackets.Add((ReadDecimal(sheet, row, 12), prices));
            }

            return rules;
        }
    }

    private static void SetDecimal(IXLCell cell, decimal? value)
    {
        if (value != null)
        {
            cell.Value = value.Value;
        }
    }

    private static int ReadInt(IXLWorksheet sheet, int row, int column)
    {
        var cell = sheet.Cell(row, column);
        if (cell.TryGetValue<int>(out var number) && number > 0)
        {
            return number;
        }

        if (int.TryParse(cell.GetString().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number) && number > 0)
        {
            return number;
        }

        throw Invalid(row, column, "Position");
    }

    private static Guid? ReadGuid(IXLWorksheet sheet, int row, int column)
    {
        var text = sheet.Cell(row, column).GetString().Trim();
        if (text.Length == 0)
        {
            return null;
        }

        return Guid.TryParse(text, out var id) && id != Guid.Empty ? id : throw Invalid(row, column, "Id");
    }

    private static decimal? ReadDecimal(IXLWorksheet sheet, int row, int column)
    {
        var cell = sheet.Cell(row, column);
        if (cell.IsEmpty())
        {
            return null;
        }

        if (cell.TryGetValue<decimal>(out var value))
        {
            return value;
        }

        var text = cell.GetString().Trim();
        if (text.Length == 0)
        {
            return null;
        }

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value) ? value : throw Invalid(row, column, "Number");
    }

    private static QuantitySource ReadQuantitySource(IXLWorksheet sheet, int row, int column)
    {
        var text = sheet.Cell(row, column).GetString().Trim();
        if (text.Length == 0)
        {
            return QuantitySource.Manual;
        }

        return Enum.TryParse<QuantitySource>(text, ignoreCase: true, out var source) && Enum.IsDefined(source) ? source : throw Invalid(row, column, "RowQuantity");
    }

    private static BusinessException Invalid(int row, int column, string problem)
        => new BusinessException(FinancialErrorCodes.ImportInvalid).WithData("row", row).WithData("column", column).WithData("problem", problem);
}
