using System.Runtime.InteropServices;
using System.Text.Json;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using SekibanWasm.Cs.Domain;
using SekibanWasm.Cs.Domain.Weather;

namespace SekibanWasm.Cs.Wasm;

public static class WasmExports
{
    private enum ProjectorKind
    {
        Unknown,
        WeatherTag,
        WeatherList
    }

    private sealed class ProjectorInstanceState
    {
        public ProjectorKind Kind { get; init; }
        public ITagStatePayload TagState { get; set; } = new EmptyTagStatePayload();
        public WeatherForecastMultiProjection WeatherMultiState { get; set; } =
            WeatherForecastMultiProjection.GenerateInitialPayload();
    }

    private static readonly object _gate = new();
    private static readonly Dictionary<int, ProjectorInstanceState> _instances = new();
    private static int _nextInstanceId = 1;

    private static DcbDomainTypes DomainTypes => WasmDomainTypes.Create();

    [UnmanagedCallersOnly(EntryPoint = "alloc")]
    public static unsafe int Alloc(int size)
    {
        if (size <= 0) return 0;
        var ptr = NativeMemory.Alloc((nuint)size);
        return (int)ptr;
    }

    [UnmanagedCallersOnly(EntryPoint = "dealloc")]
    public static unsafe void Dealloc(int ptr, int size)
    {
        if (ptr == 0) return;
        NativeMemory.Free((void*)ptr);
    }

    [UnmanagedCallersOnly(EntryPoint = "create_instance")]
    public static int CreateInstance(int projectorTypePtr, int projectorTypeLen)
    {
        var projectorType = ReadString(projectorTypePtr, projectorTypeLen);
        var kind = ResolveProjectorKind(projectorType);

        if (kind == ProjectorKind.Unknown)
        {
            throw new InvalidOperationException(
                $"Unknown projector type: '{projectorType}'");
        }

        var instance = new ProjectorInstanceState
        {
            Kind = kind,
            TagState = new EmptyTagStatePayload(),
            WeatherMultiState = WeatherForecastMultiProjection.GenerateInitialPayload()
        };

        lock (_gate)
        {
            var id = _nextInstanceId++;
            _instances[id] = instance;
            return id;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "apply_event")]
    public static void ApplyEvent(
        int instanceId,
        int eventTypePtr, int eventTypeLen,
        int payloadPtr, int payloadLen)
    {
        var instance = GetRequiredInstance(instanceId);
        var eventType = ReadString(eventTypePtr, eventTypeLen);
        var payloadJson = ReadString(payloadPtr, payloadLen);
        ApplyEventInternal(instance, eventType, payloadJson);
    }

    [UnmanagedCallersOnly(EntryPoint = "apply_events_batch")]
    public static int ApplyEventsBatch(int instanceId, int jsonPtr, int jsonLen)
    {
        var instance = GetRequiredInstance(instanceId);
        var json = ReadString(jsonPtr, jsonLen);
        if (string.IsNullOrWhiteSpace(json)) return 0;

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Expected JSON array for batch events");
        }

        var applied = 0;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"Expected JSON object at batch index {applied}");
            }
            if (!TryGetStringProperty(item, "eventType", out var eventType) ||
                !TryGetStringProperty(item, "payloadJson", out var payloadJson))
            {
                throw new InvalidOperationException(
                    $"Missing eventType or payloadJson at batch index {applied}");
            }
            if (string.IsNullOrWhiteSpace(eventType))
            {
                throw new InvalidOperationException(
                    $"Empty eventType at batch index {applied}");
            }
            ApplyEventInternal(instance, eventType, payloadJson);
            applied++;
        }
        return applied;
    }

    [UnmanagedCallersOnly(EntryPoint = "execute_query")]
    public static long ExecuteQuery(
        int instanceId,
        int queryTypePtr, int queryTypeLen,
        int paramsPtr, int paramsLen)
    {
        var instance = GetRequiredInstance(instanceId);
        var queryType = ReadString(queryTypePtr, queryTypeLen);
        var queryParamsJson = ReadString(paramsPtr, paramsLen);

        if (instance.Kind is not ProjectorKind.WeatherList)
        {
            throw new InvalidOperationException(
                $"Query execution not supported for projector kind: {instance.Kind}");
        }

        var result = queryType switch
        {
            "GetWeatherForecastCountQuery" => ExecuteCountQuery(queryParamsJson, instance.WeatherMultiState),
            _ => throw new InvalidOperationException($"Unknown query type: '{queryType}'")
        };

        return WriteString(result);
    }

    [UnmanagedCallersOnly(EntryPoint = "execute_list_query")]
    public static long ExecuteListQuery(
        int instanceId,
        int queryTypePtr, int queryTypeLen,
        int paramsPtr, int paramsLen)
    {
        var instance = GetRequiredInstance(instanceId);
        var queryType = ReadString(queryTypePtr, queryTypeLen);
        var queryParamsJson = ReadString(paramsPtr, paramsLen);

        if (instance.Kind != ProjectorKind.WeatherList)
        {
            throw new InvalidOperationException(
                $"List query execution not supported for projector kind: {instance.Kind}");
        }

        var result = queryType switch
        {
            "GetWeatherForecastListQuery" or "WeatherForecastListQuery" =>
                ExecuteListQueryInternal(queryParamsJson, instance.WeatherMultiState),
            _ => throw new InvalidOperationException($"Unknown list query type: '{queryType}'")
        };

        return WriteString(result);
    }

    [UnmanagedCallersOnly(EntryPoint = "serialize_state")]
    public static long SerializeState(int instanceId)
    {
        var instance = GetRequiredInstance(instanceId);
        var json = instance.Kind switch
        {
            ProjectorKind.WeatherList => JsonSerializer.Serialize(
                instance.WeatherMultiState,
                DomainJsonContext.Default.WeatherForecastMultiProjection),
            ProjectorKind.WeatherTag => instance.TagState is EmptyTagStatePayload
                ? "{}"
                : JsonSerializer.Serialize(
                    (WeatherForecastState)instance.TagState,
                    DomainJsonContext.Default.WeatherForecastState),
            _ => throw new InvalidOperationException(
                $"Cannot serialize state for unknown projector kind")
        };

        return WriteString(json);
    }

    [UnmanagedCallersOnly(EntryPoint = "restore_state")]
    public static void RestoreState(int instanceId, int statePtr, int stateLen)
    {
        var instance = GetRequiredInstance(instanceId);
        var json = ReadString(statePtr, stateLen);

        switch (instance.Kind)
        {
            case ProjectorKind.WeatherList:
                instance.WeatherMultiState = DeserializeWeatherMultiState(json);
                break;
            case ProjectorKind.WeatherTag:
                if (string.IsNullOrWhiteSpace(json) || json == "{}")
                {
                    instance.TagState = new EmptyTagStatePayload();
                    break;
                }
                var deserialized = JsonSerializer.Deserialize(
                    json, DomainJsonContext.Default.WeatherForecastState);
                if (deserialized is null)
                {
                    throw new InvalidOperationException(
                        $"Failed to deserialize WeatherForecastState from: {json[..Math.Min(json.Length, 100)]}");
                }
                instance.TagState = deserialized;
                break;
            default:
                throw new InvalidOperationException(
                    $"Cannot restore state for unknown projector kind");
        }
    }

    private static void ApplyEventInternal(ProjectorInstanceState instance, string eventType, string payloadJson)
    {
        var payload = DomainTypes.EventTypes.DeserializeEventPayload(eventType, payloadJson);
        if (payload is null)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize event payload for type: '{eventType}'");
        }

        var ev = CreateEvent(payload, eventType);
        var tags = ExtractTags(payload);

        switch (instance.Kind)
        {
            case ProjectorKind.WeatherList:
                instance.WeatherMultiState = WeatherForecastMultiProjection.Project(
                    instance.WeatherMultiState, ev, tags, DomainTypes,
                    SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()));
                break;
            case ProjectorKind.WeatherTag:
                instance.TagState = WeatherForecastProjector.Project(instance.TagState, ev);
                break;
        }
    }

    private static bool TryGetStringProperty(JsonElement obj, string name, out string value)
    {
        if (obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString() ?? "";
            return true;
        }
        var pascal = char.ToUpperInvariant(name[0]) + name[1..];
        if (obj.TryGetProperty(pascal, out prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString() ?? "";
            return true;
        }
        value = "";
        return false;
    }

    private static string ExecuteCountQuery(string queryParamsJson, WeatherForecastMultiProjection state)
    {
        var query = ParseLocationQuery(queryParamsJson);
        var active = state.Forecasts.Values.Where(f => !f.IsDeleted);
        var count = string.IsNullOrEmpty(query.LocationFilter)
            ? active.Count()
            : active.Count(f => f.Location.Contains(query.LocationFilter, StringComparison.OrdinalIgnoreCase));
        return JsonSerializer.Serialize(new CountResult(count), WasmJsonContext.Default.CountResult);
    }

    private static string ExecuteListQueryInternal(string queryParamsJson, WeatherForecastMultiProjection state)
    {
        var query = ParseLocationQuery(queryParamsJson);
        var items = state.Forecasts.Values.Where(f => !f.IsDeleted);
        if (!string.IsNullOrEmpty(query.LocationFilter))
            items = items.Where(f => f.Location.Contains(query.LocationFilter, StringComparison.OrdinalIgnoreCase));

        return JsonSerializer.Serialize(
            items.OrderByDescending(f => f.CreatedAt).ToList(),
            DomainJsonContext.Default.ListWeatherForecastItem);
    }

    private static LocationQuery ParseLocationQuery(string json) =>
        string.IsNullOrWhiteSpace(json) || json == "{}"
            ? new LocationQuery(null)
            : JsonSerializer.Deserialize(json, WasmJsonContext.Default.LocationQuery) ?? new LocationQuery(null);

    private static List<ITag> ExtractTags(IEventPayload payload) => payload switch
    {
        WeatherForecastCreated created => [new WeatherForecastTag(created.ForecastId)],
        WeatherForecastLocationUpdated updated => [new WeatherForecastTag(updated.ForecastId)],
        WeatherForecastDeleted deleted => [new WeatherForecastTag(deleted.ForecastId)],
        _ => []
    };

    private static Event CreateEvent(IEventPayload payload, string eventType)
    {
        var id = Guid.NewGuid();
        var sortableId = SortableUniqueId.Generate(DateTime.UtcNow, id);
        var metadata = new EventMetadata(id.ToString(), eventType, "wasm");
        return new Event(payload, sortableId, eventType, id, metadata, []);
    }

    private static WeatherForecastMultiProjection DeserializeWeatherMultiState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return WeatherForecastMultiProjection.GenerateInitialPayload();

        var state = JsonSerializer.Deserialize(json, DomainJsonContext.Default.WeatherForecastMultiProjection);
        if (state == null)
            return WeatherForecastMultiProjection.GenerateInitialPayload();

        return state.Forecasts == null
            ? state with { Forecasts = new Dictionary<string, WeatherForecastItem>() }
            : state;
    }

    private static ProjectorKind ResolveProjectorKind(string projectorType)
    {
        var normalized = projectorType.Trim().ToLowerInvariant();

        if (normalized.Contains("weatherforecastprojector"))
            return ProjectorKind.WeatherTag;

        if (normalized.Contains("weatherforecast") || normalized.Contains("weather"))
            return ProjectorKind.WeatherList;

        return ProjectorKind.Unknown;
    }

    private static ProjectorInstanceState GetRequiredInstance(int instanceId)
    {
        lock (_gate)
        {
            if (!_instances.TryGetValue(instanceId, out var instance))
            {
                throw new InvalidOperationException(
                    $"Instance not found: {instanceId}");
            }
            return instance;
        }
    }

    private static unsafe string ReadString(int ptr, int len)
    {
        if (ptr == 0 || len <= 0) return "";
        return System.Text.Encoding.UTF8.GetString(new ReadOnlySpan<byte>((void*)ptr, len));
    }

    private static unsafe long WriteString(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty);
        if (bytes.Length == 0) return 0;
        var ptr = NativeMemory.Alloc((nuint)bytes.Length);
        var span = new Span<byte>((void*)ptr, bytes.Length);
        bytes.AsSpan().CopyTo(span);
        return Pack((nint)ptr, bytes.Length);
    }

    private static long Pack(nint ptr, int len)
    {
        return ((long)(uint)ptr << 32) | (uint)len;
    }
}
