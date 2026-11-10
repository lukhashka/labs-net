using System.Text.Json;
using System.Text.Json.Serialization;

namespace Platform.EventBus;

// One message format for publisher and consumers: camelCase, enums as strings, dates in ISO 8601 (UTC).
// JSON can be read by a consumer in any language, not only .NET.
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
