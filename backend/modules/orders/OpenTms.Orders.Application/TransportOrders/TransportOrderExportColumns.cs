using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Localization;
using OpenTms.Orders.Export;
using Volo.Abp;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// The exportable columns of the order list (UI-GRID-013): the keys are the grid's column ids in design 61's Orders list
/// (<c>field</c> or <c>colId</c>), each with the localization key of its default header, its cell kind and the value as
/// the grid shows it (stop "name, city", enum names localized). Unknown keys are refused, so the contract stays explicit.
/// </summary>
public static class TransportOrderExportColumns
{
    public sealed record Definition(string Key, string HeaderKey, XlsxCellKind Kind, Func<TransportOrderRowDto, IStringLocalizer, object?> Value);

    /// <summary>A column chosen for one export: its header and its value.</summary>
    public sealed record Chosen(XlsxColumn Column, Func<TransportOrderRowDto, object?> Value);

    public static readonly IReadOnlyList<Definition> All =
    [
        new("number", "Orders:Number", XlsxCellKind.Text, (r, _) => r.Number),
        new("status", "Orders:Status", XlsxCellKind.Text, (r, l) => l["Orders:TransportOrderStatus." + r.Status].Value),
        new("orderDate", "Orders:OrderDate", XlsxCellKind.Date, (r, _) => r.OrderDate),
        new("customerName", "Orders:Customer", XlsxCellKind.Text, (r, _) => r.CustomerName),
        new("customerReference", "Orders:CustomerReference", XlsxCellKind.Text, (r, _) => r.CustomerReference),
        new("lineCount", "Orders:LineCount", XlsxCellKind.Integer, (r, _) => r.LineCount),
        new("firstStop", "Orders:FirstStop", XlsxCellKind.Text, (r, _) => Place(r.FirstStopName, r.FirstStopCity)),
        new("firstStopDate", "Orders:FirstStopDate", XlsxCellKind.Date, (r, _) => r.FirstStopDate),
        new("lastStop", "Orders:LastStop", XlsxCellKind.Text, (r, _) => Place(r.LastStopName, r.LastStopCity)),
        new("lastStopDate", "Orders:LastStopDate", XlsxCellKind.Date, (r, _) => r.LastStopDate),
        new("revenueAmount", "Orders:RevenueAmount", XlsxCellKind.Amount, (r, _) => r.RevenueAmount),
        new("costOfRevenueAmount", "Orders:CostOfRevenueAmount", XlsxCellKind.Amount, (r, _) => r.CostOfRevenueAmount),
        new("marginAmount", "Orders:MarginAmount", XlsxCellKind.Amount, (r, _) => r.MarginAmount),
        new("co2Kg", "Orders:Co2Kg", XlsxCellKind.Number, (r, _) => r.Co2Kg),
        new("tripNumbers", "Orders:TripNumbers", XlsxCellKind.Text, (r, _) => r.TripNumbers),
        new("executionProgress", "Orders:ExecutionProgress", XlsxCellKind.Text, (r, l) => l["Orders:ExecutionProgress." + r.ExecutionProgress].Value),
        new("transportOrderTypeName", "Orders:TransportOrderType", XlsxCellKind.Text, (r, _) => r.TransportOrderTypeName),
        /* Platform publishes no organisation-unit name lookup yet: the id is exported. */
        new("organizationUnitId", "Orders:OrganizationUnit", XlsxCellKind.Text, (r, _) => r.OrganizationUnitId?.ToString()),
        new("currencyCode", "Orders:CurrencyCode", XlsxCellKind.Text, (r, _) => r.CurrencyCode),
        new("stopCount", "Orders:StopCount", XlsxCellKind.Integer, (r, _) => r.StopCount),
        new("source", "Orders:Source", XlsxCellKind.Text, (r, l) => l["Orders:OrderSource." + r.Source].Value)
    ];

    private static readonly Dictionary<string, Definition> ByKey = All.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The requested columns in order (empty = all); a header the client did not send is localized here.</summary>
    public static IReadOnlyList<Chosen> Resolve(IReadOnlyList<ExportColumnDto> requested, IStringLocalizer localizer)
    {
        var columns = requested.Count == 0
            ? All.Select(d => (Definition: d, Header: (string?)null)).ToList()
            : requested.Select(c => (Definition: ByKey.TryGetValue(c.Field?.Trim() ?? string.Empty, out var d)
                    ? d
                    : throw new BusinessException(OrdersErrorCodes.ExportUnknownColumn).WithData("field", c.Field ?? string.Empty), c.Header)).ToList();

        return columns.Select(c => new Chosen(
            new XlsxColumn(string.IsNullOrWhiteSpace(c.Header) ? localizer[c.Definition.HeaderKey].Value : c.Header.Trim(), c.Definition.Kind),
            row => c.Definition.Value(row, localizer))).ToList();
    }

    private static string? Place(string? name, string? city)
    {
        var parts = new[] { name, city }.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}
