using System.Text.Json;
using System.Text.Json.Serialization;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.WasmRuntime.Host;

public sealed record DynamicJsonEventPayload(JsonElement Json) : IEventPayload
{
    public string ToJsonString() => Json.GetRawText();
}

public sealed class DynamicJsonEventPayloadJsonConverter : JsonConverter<DynamicJsonEventPayload>
{
    public override DynamicJsonEventPayload Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return new DynamicJsonEventPayload(document.RootElement.Clone());
    }

    public override void Write(
        Utf8JsonWriter writer,
        DynamicJsonEventPayload value,
        JsonSerializerOptions options)
    {
        value.Json.WriteTo(writer);
    }
}

public sealed class DynamicJsonEventTypes : IEventTypes
{
    private readonly HashSet<string> _eventTypeNames;
    private readonly JsonSerializerOptions _jsonOptions;

    public DynamicJsonEventTypes(IEnumerable<string> eventTypeNames, JsonSerializerOptions jsonOptions)
    {
        _eventTypeNames = new HashSet<string>(
            eventTypeNames.Where(static name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.Ordinal);
        _jsonOptions = jsonOptions;
    }

    public string SerializeEventPayload(IEventPayload payload) =>
        payload switch
        {
            DynamicJsonEventPayload dynamicPayload => dynamicPayload.ToJsonString(),
            _ => JsonSerializer.Serialize(payload, payload.GetType(), _jsonOptions)
        };

    public IEventPayload? DeserializeEventPayload(string eventTypeName, string json)
    {
        if (!_eventTypeNames.Contains(eventTypeName))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return new DynamicJsonEventPayload(document.RootElement.Clone());
    }

    public Type? GetEventType(string eventTypeName) =>
        _eventTypeNames.Contains(eventTypeName)
            ? typeof(DynamicJsonEventPayload)
            : null;
}
