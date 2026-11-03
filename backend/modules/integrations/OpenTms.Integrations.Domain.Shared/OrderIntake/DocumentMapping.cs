using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// What the pipeline read from one inbound document (design 72 "Inbound store", the <c>Mapping</c> jsonb): its intakes,
/// each with the provenance of every field. Stored on the document, so the trace shows which rule read which value
/// from where.
/// </summary>
public sealed class DocumentMapping
{
    public List<MappedIntake> Intakes { get; set; } = new();

    public static string ToJson(DocumentMapping mapping) => JsonSerializer.Serialize(mapping, IntakeJson.Options);

    public static DocumentMapping FromJson(string? json)
        => string.IsNullOrWhiteSpace(json) ? new DocumentMapping() : JsonSerializer.Deserialize<DocumentMapping>(json, IntakeJson.Options) ?? new DocumentMapping();
}

/// <summary>
/// One intake of a document. <see cref="Payload"/> is Orders' canonical <c>OrderIntakeDto</c> as a JSON object: the
/// domain layers of Integrations do not reference Orders' application contracts, so the application layer converts it
/// (<c>JsonSerializer.SerializeToNode</c> / <c>Deserialize&lt;OrderIntakeDto&gt;</c> with <see cref="IntakeJson.Options"/>).
/// </summary>
public sealed class MappedIntake
{
    /// <summary>The position of the intake within its document; Orders' natural key with the document id.</summary>
    public int Index { get; set; }

    public JsonObject Payload { get; set; } = new();

    /// <summary>Field path (<c>lines[0].stops[1].address.postalCode</c>) → where its value was read.</summary>
    public Dictionary<string, SourceLocation> Provenance { get; set; } = new();

    /// <summary>0–1.</summary>
    public decimal Confidence { get; set; }
}

/// <summary>Where one value was read: a cell, a line of the normalised text, an element path, a constant, the API's JSON.</summary>
public sealed class SourceLocation
{
    public SourceLocationKind Kind { get; set; }

    /// <summary>"B7", "line 14", "/Order[2]/Lines/Line[1]/Weight", "order.customerReference".</summary>
    public string Ref { get; set; } = string.Empty;

    /// <summary>At most <see cref="OrderIntakeConsts.MaxSnippetLength"/> characters of the source text; never a whole file.</summary>
    public string? Snippet { get; set; }
}

public enum SourceLocationKind
{
    Cell = 1,
    Line = 2,
    Path = 3,
    Constant = 4,
    Json = 5
}
