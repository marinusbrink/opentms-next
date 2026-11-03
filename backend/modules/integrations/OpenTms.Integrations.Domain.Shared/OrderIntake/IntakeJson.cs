using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// The one JSON shape of the order intake's jsonb columns (AD-06) and of the rules the AI proposes: camel-case names,
/// enums as camel-case strings (read case-insensitively, so "Pickup" and "pickup" are the same), nulls left out. The EF
/// converters and the AI's structured output use these options, so a stored profile and a proposed one read alike.
/// </summary>
public static class IntakeJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
