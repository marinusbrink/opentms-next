using System.Text.Json.Nodes;

namespace OpenTms.Platform.DocumentRendering;

/// <summary>
/// The JSON kind of a report data field (design 55, "Report data catalogue"), in the serialization OpenTMS used
/// (Newtonsoft defaults).
/// </summary>
public enum ReportDataFieldType
{
    /// <summary>A JSON string.</summary>
    String,

    /// <summary>A JSON number without a fraction.</summary>
    Integer,

    /// <summary>A JSON number (OpenTMS doubles, sent unrounded unless the description says otherwise).</summary>
    Decimal,

    /// <summary>A JSON boolean.</summary>
    Boolean,

    /// <summary>A date-only JSON string, <c>yyyy-MM-dd</c> (OpenTMS's <c>ESDateConverter</c> fields).</summary>
    Date,

    /// <summary>An ISO 8601 JSON string, <c>yyyy-MM-ddTHH:mm:ss</c> (no offset: OpenTMS sent local, unspecified times).</summary>
    DateTime,

    /// <summary>A duration JSON string, <c>hh:mm:ss</c> (<c>d.hh:mm:ss</c> from one day on).</summary>
    TimeSpan,

    /// <summary>A GUID JSON string, <c>xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx</c>.</summary>
    Guid,

    /// <summary>An enum sent as its integer value; the description lists the values.</summary>
    Enum,

    /// <summary>A JSON object; its fields follow as paths below it.</summary>
    Object,

    /// <summary>A JSON array; its element fields follow as paths below <c>Path[]</c>.</summary>
    Array
}

/// <summary>
/// One field of the JSON a template receives (design 55, "Report data catalogue").
/// </summary>
/// <param name="Path">
/// Dot path from the envelope root; array elements are marked with <c>[]</c>, e.g. <c>Entity.InvoiceLines[].Price</c>.
/// An array of plain values has an element field of its own (<c>Entity.OrderCustomProperty0.CustomPropertyValues[]</c>).
/// </param>
/// <param name="Type">The JSON kind.</param>
/// <param name="Description">Short English description for template developers.</param>
/// <param name="Sample">
/// The value in <see cref="ReportDataCatalogue.SampleFor"/> (first array element where the path crosses an array);
/// <see langword="null"/> for objects and arrays, and for fields whose sample is null.
/// </param>
public sealed record ReportDataField(string Path, ReportDataFieldType Type, string Description, JsonNode? Sample);

/// <summary>A place where a payload does not match the catalogue (<see cref="ReportDataCatalogue.Validate"/>).</summary>
/// <param name="Path">Concrete path in the payload, with array indices, e.g. <c>Entity.InvoiceLines[1].Price</c>.</param>
/// <param name="Message">What is wrong (unknown field, wrong JSON kind, wrong format).</param>
public sealed record ReportDataDeviation(string Path, string Message)
{
    public override string ToString() => $"{Path}: {Message}";
}
