using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Localization;
using OpenTms.PlanningExecution.Export;
using Volo.Abp;

namespace OpenTms.PlanningExecution.TransportTrips;

/// <summary>
/// The exportable columns of the trip list (UI-GRID-013): the keys are the grid's column ids in design 61's Trips list
/// (<c>field</c> or <c>colId</c>), each with the localization key of its default header, its cell kind and the value as
/// the grid shows it (distance in km, load factor as a percentage, lists joined, enums and warnings localized, times in
/// the tenant's zone). Unknown keys are refused, so the contract stays explicit.
/// </summary>
public static class TransportTripExportColumns
{
    public sealed record Definition(string Key, string HeaderKey, string? HeaderSuffix, XlsxCellKind Kind, Func<TransportTripRowDto, IStringLocalizer, TimeZoneInfo, object?> Value);

    /// <summary>A column chosen for one export: its header and its value.</summary>
    public sealed record Chosen(XlsxColumn Column, Func<TransportTripRowDto, object?> Value);

    public static readonly IReadOnlyList<Definition> All =
    [
        new("number", "PlanningExecution:Number", null, XlsxCellKind.Text, (r, _, _) => r.Number),
        new("status", "PlanningExecution:Status", null, XlsxCellKind.Text, (r, l, _) => l["PlanningExecution:TransportTripStatus." + r.Status].Value),
        new("tripDate", "PlanningExecution:TripDate", null, XlsxCellKind.Date, (r, _, _) => r.TripDate),
        new("name", "PlanningExecution:Name", null, XlsxCellKind.Text, (r, _, _) => r.Name),
        new("transportTripTypeName", "PlanningExecution:TransportTripType", null, XlsxCellKind.Text, (r, _, _) => r.TransportTripTypeName),
        new("carrierName", "PlanningExecution:Carrier", null, XlsxCellKind.Text, (r, _, _) => r.CarrierName),
        new("vehicles", "PlanningExecution:Vehicles", null, XlsxCellKind.Text, (r, _, _) => Join(r.Vehicles)),
        new("drivers", "PlanningExecution:Drivers", null, XlsxCellKind.Text, (r, _, _) => Join(r.Drivers)),
        new("plannedDeparture", "PlanningExecution:PlannedDeparture", null, XlsxCellKind.DateTime, (r, _, z) => Local(r.PlannedDeparture, z)),
        new("plannedArrival", "PlanningExecution:PlannedArrival", null, XlsxCellKind.DateTime, (r, _, z) => Local(r.PlannedArrival, z)),
        new("stopCount", "PlanningExecution:Stops", null, XlsxCellKind.Integer, (r, _, _) => r.StopCount),
        new("legCount", "PlanningExecution:Legs", null, XlsxCellKind.Integer, (r, _, _) => r.LegCount),
        new("customers", "PlanningExecution:Customers", null, XlsxCellKind.Text, (r, _, _) => Join(r.Customers)),
        new("orders", "PlanningExecution:Orders", null, XlsxCellKind.Text, (r, _, _) => Join(r.OrderNumbers)),
        new("plannedDistanceM", "PlanningExecution:Distance", " (km)", XlsxCellKind.Number, (r, _, _) => r.PlannedDistanceM == null ? null : Math.Round(r.PlannedDistanceM.Value / 1000m, 1)),
        /* Stored as a fraction (0.85); the cell's percentage format shows it as the grid does (85 %). */
        new("loadFactor", "PlanningExecution:LoadFactor", null, XlsxCellKind.Percent, (r, _, _) => r.LoadFactor),
        new("co2Kg", "PlanningExecution:Co2Kg", null, XlsxCellKind.Number, (r, _, _) => r.Co2Kg),
        new("costAmount", "PlanningExecution:CostAmount", null, XlsxCellKind.Amount, (r, _, _) => r.CostAmount),
        new("revenueAmount", "PlanningExecution:RevenueAmount", null, XlsxCellKind.Amount, (r, _, _) => r.RevenueAmount),
        new("warnings", "PlanningExecution:Warnings", null, XlsxCellKind.Text, (r, l, _) => Join(r.Warnings.Select(w => l["PlanningExecution:TripWarningCode." + w.Code].Value).Distinct())),
        new("isArchived", "PlanningExecution:IsArchived", null, XlsxCellKind.Boolean, (r, _, _) => r.IsArchived),
        new("execution", "PlanningExecution:Execution", null, XlsxCellKind.Text, (r, l, _) => l["PlanningExecution:TripExecution." + r.Execution].Value),
        new("route", "PlanningExecution:Route", null, XlsxCellKind.Text, (r, _, _) => Join(new[] { r.FirstStopCity, r.LastStopCity }, " → ")),
        new("actualDeparture", "PlanningExecution:ActualDeparture", null, XlsxCellKind.DateTime, (r, _, z) => Local(r.ActualDeparture, z)),
        new("actualArrival", "PlanningExecution:ActualArrival", null, XlsxCellKind.DateTime, (r, _, z) => Local(r.ActualArrival, z))
    ];

    private static readonly Dictionary<string, Definition> ByKey = All.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The requested columns in order (empty = all); a header the client did not send is localized here.</summary>
    public static IReadOnlyList<Chosen> Resolve(IReadOnlyList<ExportColumnDto> requested, IStringLocalizer localizer, TimeZoneInfo zone)
    {
        var columns = requested.Count == 0
            ? All.Select(d => (Definition: d, Header: (string?)null)).ToList()
            : requested.Select(c => (Definition: ByKey.TryGetValue(c.Field?.Trim() ?? string.Empty, out var d)
                    ? d
                    : throw new BusinessException(PlanningExecutionErrorCodes.ExportUnknownColumn).WithData("field", c.Field ?? string.Empty), c.Header)).ToList();

        return columns.Select(c => new Chosen(
            new XlsxColumn(string.IsNullOrWhiteSpace(c.Header) ? localizer[c.Definition.HeaderKey].Value + c.Definition.HeaderSuffix : c.Header.Trim(), c.Definition.Kind),
            row => c.Definition.Value(row, localizer, zone))).ToList();
    }

    private static string? Join(IEnumerable<string?> values, string separator = ", ")
    {
        var parts = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        return parts.Count == 0 ? null : string.Join(separator, parts);
    }

    private static DateTime? Local(DateTimeOffset? instant, TimeZoneInfo zone)
        => instant == null ? null : TimeZoneInfo.ConvertTime(instant.Value, zone).DateTime;
}
