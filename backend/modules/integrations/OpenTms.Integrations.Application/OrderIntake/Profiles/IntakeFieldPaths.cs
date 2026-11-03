using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using OpenTms.Orders.Intake;
using OpenTms.Orders.TransportOrders;

namespace OpenTms.Integrations.OrderIntake.Profiles;

/// <summary>
/// The field paths of Orders' canonical <see cref="OrderIntakeDto"/> (design 72 §6): <c>order.customerReference</c>,
/// <c>order.customer.name</c>, <c>order.references[0].value</c>, <c>lines[0].weightKg</c>,
/// <c>lines[0].stops[1].address.postalCode</c>. One table writes a text into a path (with the type's parsing: numbers
/// invariant, dates yyyy-MM-dd, times HH:mm), and flattens an intake back into path → canonical text, so the profile
/// executor, the AI's answer, the comparison of both and the provenance all speak the same paths.
/// </summary>
public static partial class IntakeFieldPaths
{
    /// <summary>Error codes of a value that does not fit its field.</summary>
    public const string UnknownPath = "unknownPath";
    public const string InvalidNumber = "invalidNumber";
    public const string NegativeNumber = "negativeNumber";
    public const string InvalidDate = "invalidDate";
    public const string InvalidTime = "invalidTime";
    public const string InvalidRole = "invalidRole";
    public const string TooLong = "tooLong";

    /// <summary>A path with more lines or stops than this is refused (bounds a hostile answer).</summary>
    public const int MaxIndex = 4999;

    private enum Kind { Text, Number, Date, Time }

    private sealed record Leaf<T>(Kind Kind, int MaxLength, Func<T, string?> Get, Action<T, string?> Set);

    private static readonly Dictionary<string, Leaf<OrderIntakeDto>> OrderLeaves = new(StringComparer.OrdinalIgnoreCase)
    {
        ["customerReference"] = new(Kind.Text, OrdersConsts.MaxCustomerReferenceLength, o => o.CustomerReference, (o, v) => o.CustomerReference = v),
        ["orderDate"] = new(Kind.Date, 10, o => o.OrderDate, (o, v) => o.OrderDate = v),
        ["orderTypeName"] = new(Kind.Text, IntakeConsts.MaxNameLength, o => o.OrderTypeName, (o, v) => o.OrderTypeName = v),
        ["remarks"] = new(Kind.Text, OrdersConsts.MaxRemarksLength, o => o.Remarks, (o, v) => o.Remarks = v),
        ["customer.name"] = new(Kind.Text, IntakeConsts.MaxNameLength, o => o.Customer?.Name, (o, v) => Customer(o).Name = v),
        ["customer.code"] = new(Kind.Text, IntakeConsts.MaxCodeLength, o => o.Customer?.Code, (o, v) => Customer(o).Code = v),
        ["customer.emailAddress"] = new(Kind.Text, IntakeConsts.MaxEmailAddressLength, o => o.Customer?.EmailAddress, (o, v) => Customer(o).EmailAddress = v),
        ["customer.vatNumber"] = new(Kind.Text, IntakeConsts.MaxVatNumberLength, o => o.Customer?.VatNumber, (o, v) => Customer(o).VatNumber = v),
        ["customer.chamberOfCommerceNumber"] = new(Kind.Text, IntakeConsts.MaxChamberOfCommerceNumberLength, o => o.Customer?.ChamberOfCommerceNumber, (o, v) => Customer(o).ChamberOfCommerceNumber = v)
    };

    private static readonly Dictionary<string, Leaf<OrderIntakeLineDto>> LineLeaves = new(StringComparer.OrdinalIgnoreCase)
    {
        ["transportUnitName"] = new(Kind.Text, IntakeConsts.MaxNameLength, l => l.TransportUnitName, (l, v) => l.TransportUnitName = v),
        ["quantity"] = new(Kind.Number, 32, l => Num(l.Quantity), (l, v) => l.Quantity = v == null ? 1 : decimal.Parse(v, CultureInfo.InvariantCulture)),
        ["colli"] = new(Kind.Number, 32, l => Num(l.Colli), (l, v) => l.Colli = Dec(v)),
        ["positions"] = new(Kind.Number, 32, l => Num(l.Positions), (l, v) => l.Positions = Dec(v)),
        ["weightKg"] = new(Kind.Number, 32, l => Num(l.WeightKg), (l, v) => l.WeightKg = Dbl(v)),
        ["volumeM3"] = new(Kind.Number, 32, l => Num(l.VolumeM3), (l, v) => l.VolumeM3 = Dbl(v)),
        ["loadingMeters"] = new(Kind.Number, 32, l => Num(l.LoadingMeters), (l, v) => l.LoadingMeters = Dbl(v)),
        ["lengthCm"] = new(Kind.Number, 32, l => Num(l.LengthCm), (l, v) => l.LengthCm = Dbl(v)),
        ["widthCm"] = new(Kind.Number, 32, l => Num(l.WidthCm), (l, v) => l.WidthCm = Dbl(v)),
        ["heightCm"] = new(Kind.Number, 32, l => Num(l.HeightCm), (l, v) => l.HeightCm = Dbl(v)),
        ["productName"] = new(Kind.Text, IntakeConsts.MaxNameLength, l => l.ProductName, (l, v) => l.ProductName = v),
        ["temperatureName"] = new(Kind.Text, IntakeConsts.MaxNameLength, l => l.TemperatureName, (l, v) => l.TemperatureName = v),
        ["description"] = new(Kind.Text, OrdersConsts.MaxLineTextLength, l => l.Description, (l, v) => l.Description = v),
        ["remarks"] = new(Kind.Text, OrdersConsts.MaxRemarksLength, l => l.Remarks, (l, v) => l.Remarks = v),
        ["containerNumber"] = new(Kind.Text, OrdersConsts.MaxContainerFieldLength, l => l.ContainerNumber, (l, v) => l.ContainerNumber = v),
        ["barcode"] = new(Kind.Text, OrdersConsts.MaxBarcodeLength, l => l.Barcode, (l, v) => l.Barcode = v)
    };

    private static readonly Dictionary<string, Leaf<OrderIntakeStopDto>> StopLeaves = new(StringComparer.OrdinalIgnoreCase)
    {
        ["waypointTypeName"] = new(Kind.Text, IntakeConsts.MaxNameLength, s => s.WaypointTypeName, (s, v) => s.WaypointTypeName = v),
        ["date"] = new(Kind.Date, 10, s => s.Date, (s, v) => s.Date = v),
        ["timeFrom"] = new(Kind.Time, 5, s => s.TimeFrom, (s, v) => s.TimeFrom = v),
        ["timeTill"] = new(Kind.Time, 5, s => s.TimeTill, (s, v) => s.TimeTill = v),
        ["reference"] = new(Kind.Text, OrdersConsts.MaxStopReferenceLength, s => s.Reference, (s, v) => s.Reference = v),
        ["remarks"] = new(Kind.Text, OrdersConsts.MaxStopRemarksLength, s => s.Remarks, (s, v) => s.Remarks = v),
        ["contactName"] = new(Kind.Text, IntakeConsts.MaxContactNameLength, s => s.ContactName, (s, v) => s.ContactName = v),
        ["contactPhone"] = new(Kind.Text, IntakeConsts.MaxContactPhoneLength, s => s.ContactPhone, (s, v) => s.ContactPhone = v),
        ["address.name"] = new(Kind.Text, IntakeConsts.MaxNameLength, s => s.Address.Name, (s, v) => s.Address.Name = v),
        ["address.street"] = new(Kind.Text, IntakeConsts.MaxStreetLength, s => s.Address.Street, (s, v) => s.Address.Street = v),
        ["address.houseNumber"] = new(Kind.Text, IntakeConsts.MaxHouseNumberLength, s => s.Address.HouseNumber, (s, v) => s.Address.HouseNumber = v),
        ["address.postalCode"] = new(Kind.Text, IntakeConsts.MaxPostalCodeLength, s => s.Address.PostalCode, (s, v) => s.Address.PostalCode = v),
        ["address.city"] = new(Kind.Text, OrdersConsts.MaxCityLength, s => s.Address.City, (s, v) => s.Address.City = v),
        ["address.countryCode"] = new(Kind.Text, IntakeConsts.MaxCountryCodeLength, s => s.Address.CountryCode, (s, v) => s.Address.CountryCode = v?.ToUpperInvariant()),
        ["address.fullText"] = new(Kind.Text, IntakeConsts.MaxFullTextLength, s => s.Address.FullText, (s, v) => s.Address.FullText = v)
    };

    /// <summary>The leaf names a rule target may use after <c>order.</c>, <c>line.</c> or <c>stop[Role].</c>.</summary>
    public static IReadOnlyCollection<string> OrderFields => OrderLeaves.Keys;

    public static IReadOnlyCollection<string> LineFields => LineLeaves.Keys;

    public static IReadOnlyCollection<string> StopFields => StopLeaves.Keys;

    public static bool IsOrderField(string leaf) => OrderLeaves.ContainsKey(leaf);

    public static bool IsLineField(string leaf) => LineLeaves.ContainsKey(leaf);

    public static bool IsStopField(string leaf) => StopLeaves.ContainsKey(leaf) || string.Equals(leaf, "role", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Writes <paramref name="text"/> to the concrete <paramref name="path"/>, growing the lines and stops as needed. An
    /// empty text leaves the field as it is. Returns null, or an error code.
    /// </summary>
    public static string? TrySet(OrderIntakeDto intake, string path, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim();
        var match = PathPattern().Match(path.Trim());
        if (!match.Success)
        {
            return UnknownPath;
        }

        if (match.Groups["order"].Success)
        {
            var leaf = match.Groups["orderLeaf"].Value;
            var reference = ReferencePattern().Match(leaf);
            if (reference.Success)
            {
                var index = int.Parse(reference.Groups["i"].Value, CultureInfo.InvariantCulture);
                if (index > 50)
                {
                    return UnknownPath;
                }

                while (intake.References.Count <= index)
                {
                    intake.References.Add(new OrderIntakeReferenceDto());
                }

                if (value.Length > OrdersConsts.MaxReferenceValueLength)
                {
                    return TooLong;
                }

                if (string.Equals(reference.Groups["leaf"].Value, "type", StringComparison.OrdinalIgnoreCase))
                {
                    intake.References[index].Type = value;
                }
                else
                {
                    intake.References[index].Value = value;
                }

                return null;
            }

            return OrderLeaves.TryGetValue(leaf, out var orderLeaf) ? Write(orderLeaf, intake, value) : UnknownPath;
        }

        var lineIndex = int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture);
        if (lineIndex > MaxIndex)
        {
            return UnknownPath;
        }

        while (intake.Lines.Count <= lineIndex)
        {
            intake.Lines.Add(new OrderIntakeLineDto());
        }

        var line = intake.Lines[lineIndex];
        if (!match.Groups["stop"].Success)
        {
            return LineLeaves.TryGetValue(match.Groups["lineLeaf"].Value, out var lineLeaf) ? Write(lineLeaf, line, value) : UnknownPath;
        }

        var stopIndex = int.Parse(match.Groups["stop"].Value, CultureInfo.InvariantCulture);
        if (stopIndex > 20)
        {
            return UnknownPath;
        }

        while (line.Stops.Count <= stopIndex)
        {
            line.Stops.Add(new OrderIntakeStopDto { Role = line.Stops.Count == 0 ? IntakeStopRole.Pickup : IntakeStopRole.Delivery });
        }

        var stop = line.Stops[stopIndex];
        var stopLeafName = match.Groups["stopLeaf"].Value;
        if (string.Equals(stopLeafName, "role", StringComparison.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<IntakeStopRole>(value, ignoreCase: true, out var role) || !Enum.IsDefined(role))
            {
                return InvalidRole;
            }

            stop.Role = role;
            return null;
        }

        return StopLeaves.TryGetValue(stopLeafName, out var stopLeaf) ? Write(stopLeaf, stop, value) : UnknownPath;
    }

    /// <summary>Every non-empty field of the intake as path → canonical text (numbers invariant, trimmed).</summary>
    public static SortedDictionary<string, string> Flatten(OrderIntakeDto intake)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, leaf) in OrderLeaves)
        {
            Add(result, "order." + name, leaf.Get(intake));
        }

        for (var r = 0; r < intake.References.Count; r++)
        {
            Add(result, $"order.references[{r}].type", intake.References[r].Type);
            Add(result, $"order.references[{r}].value", intake.References[r].Value);
        }

        for (var l = 0; l < intake.Lines.Count; l++)
        {
            var line = intake.Lines[l];
            foreach (var (name, leaf) in LineLeaves)
            {
                Add(result, $"lines[{l}].{name}", leaf.Get(line));
            }

            for (var s = 0; s < line.Stops.Count; s++)
            {
                var stop = line.Stops[s];
                Add(result, $"lines[{l}].stops[{s}].role", stop.Role.ToString());
                foreach (var (name, leaf) in StopLeaves)
                {
                    Add(result, $"lines[{l}].stops[{s}].{name}", leaf.Get(stop));
                }
            }
        }

        return result;
    }

    private static void Add(IDictionary<string, string> result, string path, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            result[path] = value.Trim();
        }
    }

    private static string? Write<T>(Leaf<T> leaf, T target, string value)
    {
        string? canonical;
        string? error;
        switch (leaf.Kind)
        {
            case Kind.Number:
                (canonical, error) = ValueParsers.Number(value, null);
                if (error == null && canonical != null && canonical.StartsWith('-'))
                {
                    error = NegativeNumber;
                }

                break;
            case Kind.Date:
                (canonical, error) = ValueParsers.Date(value, null, null);
                break;
            case Kind.Time:
                (canonical, error) = ValueParsers.Time(value, null);
                break;
            default:
                canonical = value;
                error = value.Length > leaf.MaxLength ? TooLong : null;
                break;
        }

        if (error != null)
        {
            return error;
        }

        leaf.Set(target, canonical);
        return null;
    }

    private static OrderIntakeCustomerDto Customer(OrderIntakeDto intake) => intake.Customer ??= new OrderIntakeCustomerDto();

    private static string? Num(decimal? value) => value?.ToString("0.############", CultureInfo.InvariantCulture);

    private static string? Num(double? value) => value == null ? null : ((decimal)value.Value).ToString("0.############", CultureInfo.InvariantCulture);

    private static decimal? Dec(string? value) => value == null ? null : decimal.Parse(value, CultureInfo.InvariantCulture);

    private static double? Dbl(string? value) => value == null ? null : double.Parse(value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?:(?<order>order)\.(?<orderLeaf>references\[\d+\]\.(?:type|value)|[A-Za-z]+(?:\.[A-Za-z]+)?)|lines\[(?<line>\d{1,5})\]\.(?:stops\[(?<stop>\d{1,3})\]\.(?<stopLeaf>[A-Za-z]+(?:\.[A-Za-z]+)?)|(?<lineLeaf>[A-Za-z]+)))$", RegexOptions.None, 1000)]
    private static partial Regex PathPattern();

    [GeneratedRegex(@"^references\[(?<i>\d+)\]\.(?<leaf>type|value)$", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex ReferencePattern();
}

/// <summary>The parsing of numbers, dates and times shared by the executor's transforms and the field table.</summary>
public static class ValueParsers
{
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy-M-d", "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd.MM.yyyy", "d.M.yyyy", "yyyy/MM/dd",
        "yyyyMMdd", "dd-MM-yy", "d-M-yy", "dd/MM/yy", "dd.MM.yy", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssK", "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm", "dd-MM-yyyy HH:mm", "dd/MM/yyyy HH:mm"
    ];

    private static readonly string[] TimeFormats = ["HH:mm", "H:mm", "HH.mm", "H.mm", "HHmm", "HH:mm:ss", "H:mm:ss", "HH"];

    /// <summary>Invariant canonical text of a number; <paramref name="culture"/> null means invariant, then nl-NL when that fails.</summary>
    public static (string? Value, string? Error) Number(string text, string? culture)
    {
        var cleaned = text.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        var cultures = culture != null ? new[] { Culture(culture) } : new[] { CultureInfo.InvariantCulture };
        foreach (var c in cultures)
        {
            if (c != null && decimal.TryParse(cleaned, NumberStyles.Number, c, out var number))
            {
                return (number.ToString("0.############", CultureInfo.InvariantCulture), null);
            }
        }

        return (null, IntakeFieldPaths.InvalidNumber);
    }

    /// <summary>yyyy-MM-dd from an exact <paramref name="format"/>, else the common formats.</summary>
    public static (string? Value, string? Error) Date(string text, string? format, string? culture)
    {
        var provider = Culture(culture) ?? CultureInfo.InvariantCulture;
        var trimmed = text.Trim();
        var formats = string.IsNullOrWhiteSpace(format) ? DateFormats : [format];
        if (DateTime.TryParseExact(trimmed, formats, provider, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var date))
        {
            return (date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null);
        }

        if (!string.IsNullOrWhiteSpace(format) && culture != null && DateTime.TryParse(trimmed, provider, DateTimeStyles.AllowWhiteSpaces, out date))
        {
            return (date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null);
        }

        return (null, IntakeFieldPaths.InvalidDate);
    }

    /// <summary>HH:mm.</summary>
    public static (string? Value, string? Error) Time(string text, string? format)
    {
        var trimmed = text.Trim();
        var formats = string.IsNullOrWhiteSpace(format) ? TimeFormats : [format];
        if (DateTime.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var time))
        {
            return (time.ToString("HH:mm", CultureInfo.InvariantCulture), null);
        }

        return (null, IntakeFieldPaths.InvalidTime);
    }

    public static CultureInfo? Culture(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(name.Trim());
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
