using System.Collections.Generic;
using System.Text.Json;
using OpenTms.Orders.Intake;

namespace OpenTms.Integrations.OrderIntake.Profiles;

/// <summary>
/// The rules of an import profile (design 72 "Rules", the <c>Rules</c> jsonb): how a document of one layout splits into
/// orders, lines and stops, and where each field of Orders' canonical model is read, with its transforms. Executed by
/// the deterministic <c>ImportProfileExecutor</c>; the AI proposes them in exactly this shape (<see cref="IntakeJson"/>).
/// Targets are the canonical model's paths: <c>order.customerReference</c>, <c>line.weightKg</c>,
/// <c>stop[Pickup].address.postalCode</c>.
/// </summary>
public sealed class ImportProfileRules
{
    public OrderSplitRule Orders { get; set; } = new();

    public LineSplitRule Lines { get; set; } = new();

    public List<StopRule> Stops { get; set; } = new();

    public List<FieldRule> Fields { get; set; } = new();

    /// <summary>Spreadsheets: the sheet the orders are on; null for the first sheet.</summary>
    public string? Sheet { get; set; }

    /// <summary>Tabular formats: the 1-based row that holds the headers.</summary>
    public int? HeaderRow { get; set; }

    /// <summary>XML: prefix → namespace URI, for the XPath expressions.</summary>
    public Dictionary<string, string>? Namespaces { get; set; }

    public static string ToJson(ImportProfileRules rules) => JsonSerializer.Serialize(rules, IntakeJson.Options);

    public static ImportProfileRules FromJson(string? json)
        => string.IsNullOrWhiteSpace(json) ? new ImportProfileRules() : JsonSerializer.Deserialize<ImportProfileRules>(json, IntakeJson.Options) ?? new ImportProfileRules();
}

public sealed class OrderSplitRule
{
    public OrderScope Scope { get; set; } = new();
}

/// <summary>How a document splits into orders: one per document, per group of rows, per XML node, or per text block.</summary>
public sealed class OrderScope
{
    public OrderScopeKind Kind { get; set; } = OrderScopeKind.Document;

    /// <summary>The XPath (xpath) or the regex that starts a new order (textSplit).</summary>
    public string? Value { get; set; }

    /// <summary>rowGroup: the headers whose values together identify one order ("Ordernr").</summary>
    public List<string>? GroupBy { get; set; }
}

public enum OrderScopeKind
{
    Document = 1,
    RowGroup = 2,
    Xpath = 3,
    TextSplit = 4
}

public sealed class LineSplitRule
{
    public LineScope Scope { get; set; } = new();
}

/// <summary>How an order splits into lines: one line, one per row, per XML node, or per text block.</summary>
public sealed class LineScope
{
    public LineScopeKind Kind { get; set; } = LineScopeKind.Single;

    public string? Value { get; set; }
}

public enum LineScopeKind
{
    Single = 1,
    Row = 2,
    Xpath = 3,
    TextBlock = 4
}

/// <summary>A stop of each line; without a stop rule a line gets one Pickup and one Delivery stop (Assumption 3).</summary>
public sealed class StopRule
{
    public IntakeStopRole Role { get; set; }

    public StopScope Scope { get; set; } = new();
}

public sealed class StopScope
{
    public StopScopeKind Kind { get; set; } = StopScopeKind.Inherit;

    public string? Value { get; set; }
}

public enum StopScopeKind
{
    Inherit = 1,
    Xpath = 2
}

/// <summary>One field of the canonical model: where it is read and how the text is turned into the value.</summary>
public sealed class FieldRule
{
    /// <summary>The canonical path, e.g. <c>order.customerReference</c>, <c>line.weightKg</c>, <c>stop[Pickup].address.postalCode</c>.</summary>
    public string Target { get; set; } = string.Empty;

    public FieldSource Source { get; set; } = new();

    public List<FieldTransform> Transforms { get; set; } = new();
}

/// <summary>
/// column = a header of the scope row; cell = an A1 reference; xpath = relative to the scope node; label = the value
/// after a label on the same line (or the next line when the label ends it); regex = a .NET regex with a capture group
/// over the scope text (timeout 100 ms); constant = a literal.
/// </summary>
public sealed class FieldSource
{
    public FieldSourceKind Kind { get; set; }

    public string Value { get; set; } = string.Empty;

    /// <summary>regex: the capture group (default 1).</summary>
    public int? Group { get; set; }

    /// <summary>label/regex/xpath: which occurrence (1-based, default 1).</summary>
    public int? Occurrence { get; set; }
}

public enum FieldSourceKind
{
    Column = 1,
    Cell = 2,
    Xpath = 3,
    Label = 4,
    Regex = 5,
    Constant = 6
}

/// <summary>One transform of a read text, applied in order.</summary>
public sealed class FieldTransform
{
    public FieldTransformKind Kind { get; set; }

    /// <summary>date/time/number: the .NET format, e.g. "dd-MM-yyyy".</summary>
    public string? Format { get; set; }

    /// <summary>date/number: the culture, e.g. "nl-NL".</summary>
    public string? Culture { get; set; }

    /// <summary>map: source text → value, e.g. "EUR-pallet" → "Europallet".</summary>
    public Dictionary<string, string>? Map { get; set; }

    /// <summary>split/join: the separator.</summary>
    public string? Separator { get; set; }

    /// <summary>split: the 0-based part to keep.</summary>
    public int? Index { get; set; }
}

public enum FieldTransformKind
{
    Trim = 1,
    Date = 2,
    Time = 3,
    Number = 4,
    Map = 5,
    Split = 6,
    Join = 7,
    Upper = 8
}
