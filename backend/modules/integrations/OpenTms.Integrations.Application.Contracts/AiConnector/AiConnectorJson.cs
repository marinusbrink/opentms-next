using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTms.Integrations.AiConnector;

/// <summary>How tool inputs are read and tool results written: camelCase, enums by name, nulls left out.</summary>
public static class AiConnectorJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            /* The web defaults also read numbers from strings, which would make every number's schema a union. */
            NumberHandling = JsonNumberHandling.Strict,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
