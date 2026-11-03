using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTms.Orders.Intake;

/// <summary>
/// The JSON shape of the order intake's jsonb columns (design 72, AD-06): the payload as received, the reviewed payload and
/// the resolution. Camel-case names, enums as strings (read case-insensitively), nulls left out.
/// </summary>
public static class OrderIntakeJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
