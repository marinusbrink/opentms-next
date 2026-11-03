using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenTms.Orders.Intake;

/// <summary>
/// The field paths of the resolution (design 72 "Mapping, submit, trace"): <c>order.customer</c>, <c>order.orderType</c>,
/// <c>order.orderDate</c>, <c>lines[0].transportUnit</c>, <c>lines[0].stops[1].address</c>, … A path names the field the
/// resolver resolved, so the review can pick a row for it and the API can report it as unresolved.
/// </summary>
public static partial class IntakeFieldPaths
{
    public const string Customer = "order.customer";
    public const string OrderType = "order.orderType";
    public const string OrderDate = "order.orderDate";
    public const string Lines = "order.lines";

    public const string TransportUnitField = "transportUnit";
    public const string ProductField = "product";
    public const string TemperatureField = "temperature";
    public const string StopsField = "stops";
    public const string AddressField = "address";
    public const string WaypointTypeField = "waypointType";
    public const string DateField = "date";
    public const string TimeFromField = "timeFrom";
    public const string TimeTillField = "timeTill";

    public static string Line(int line, string field) => string.Create(CultureInfo.InvariantCulture, $"lines[{line}].{field}");

    public static string Stop(int line, int stop, string field) => string.Create(CultureInfo.InvariantCulture, $"lines[{line}].stops[{stop}].{field}");

    /// <summary>Product and temperature are left empty when unresolved (a warning, design 72 "Resolution"); every other open field blocks.</summary>
    public static bool IsOptional(string path)
        => path.EndsWith("." + ProductField, System.StringComparison.Ordinal) || path.EndsWith("." + TemperatureField, System.StringComparison.Ordinal);

    /// <summary>The kind of row a pick on <paramref name="path"/> names, with the line and stop it is on; null for a path no pick applies to.</summary>
    public static PickTarget? ParsePickable(string path)
    {
        switch (path)
        {
            case Customer:
                return new PickTarget(IntakeAliasKind.Customer, null, null);
            case OrderType:
                return new PickTarget(IntakeAliasKind.OrderType, null, null);
        }

        var stop = StopPath().Match(path);
        if (stop.Success)
        {
            var line = int.Parse(stop.Groups[1].Value, CultureInfo.InvariantCulture);
            var index = int.Parse(stop.Groups[2].Value, CultureInfo.InvariantCulture);
            return stop.Groups[3].Value switch
            {
                AddressField => new PickTarget(IntakeAliasKind.Address, line, index),
                WaypointTypeField => new PickTarget(IntakeAliasKind.WaypointType, line, index),
                _ => null
            };
        }

        var linePath = LinePath().Match(path);
        if (linePath.Success)
        {
            var line = int.Parse(linePath.Groups[1].Value, CultureInfo.InvariantCulture);
            return linePath.Groups[2].Value switch
            {
                TransportUnitField => new PickTarget(IntakeAliasKind.TransportUnit, line, null),
                ProductField => new PickTarget(IntakeAliasKind.Product, line, null),
                TemperatureField => new PickTarget(IntakeAliasKind.Temperature, line, null),
                _ => null
            };
        }

        return null;
    }

    [GeneratedRegex(@"^lines\[(\d{1,4})\]\.stops\[(\d{1,3})\]\.([a-zA-Z]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex StopPath();

    [GeneratedRegex(@"^lines\[(\d{1,4})\]\.([a-zA-Z]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex LinePath();
}

/// <summary>What a pickable field path names: the kind of row, and the line and stop index where it applies.</summary>
public sealed record PickTarget(IntakeAliasKind Kind, int? Line, int? Stop);
