using System.Text;
using System.Text.Json;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WasmRuntime;
using SekibanWasm.Cs.Domain;
using SekibanWasm.Cs.Domain.Weather;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class WasmTagStatePrimitiveTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly Sekiban.Dcb.Domains.IEventTypes EventTypes = DomainType.GetDomainTypes().EventTypes;

    [Fact]
    public void GetSerializedState_ShouldReturnInitialState_WhenNoEventsApplied()
    {
        // Given
        using var instance = new StubProjectionInstance();
        var primitive = new WasmTagStateProjectionPrimitive(
            instance, "WeatherForecastProjector", "v1", EventTypes, JsonOptions);

        // When
        var result = primitive.GetSerializedState();

        // Then
        Assert.Equal(0, result.Version);
        Assert.Equal("v1", result.ProjectorVersion);
        Assert.Equal("WeatherForecastProjector", result.TagProjector);
    }

    [Fact]
    public void ApplyEvents_ShouldIncrementVersionAndTrackMetadata()
    {
        // Given
        using var instance = new StubProjectionInstance();
        var primitive = new WasmTagStateProjectionPrimitive(
            instance, "WeatherForecastProjector", "v1", EventTypes, JsonOptions);

        var events = new List<Event>
        {
            CreateEvent(
                new WeatherForecastCreated("f-1", "Tokyo", 25, "Warm", DateTimeOffset.UtcNow),
                nameof(WeatherForecastCreated),
                ["weather:f-1"])
        };

        // When
        primitive.ApplyEvents(events, null);

        // Then
        Assert.Equal(1, primitive.Version);
        Assert.NotNull(primitive.LastSortedUniqueId);
        Assert.Equal("weather", primitive.TagGroup);
        Assert.Equal("f-1", primitive.TagContent);
        Assert.Equal("WeatherForecastCreated", primitive.TagPayloadName);
    }

    [Fact]
    public void ApplyEvents_ShouldIncrementVersionForEachEvent()
    {
        // Given
        using var instance = new StubProjectionInstance();
        var primitive = new WasmTagStateProjectionPrimitive(
            instance, "WeatherForecastProjector", "v1", EventTypes, JsonOptions);

        var events = new List<Event>
        {
            CreateEvent(
                new WeatherForecastCreated("f-1", "Tokyo", 25, "Warm", DateTimeOffset.UtcNow),
                nameof(WeatherForecastCreated),
                ["weather:f-1"]),
            CreateEvent(
                new WeatherForecastLocationUpdated("f-1", "Osaka", DateTimeOffset.UtcNow),
                nameof(WeatherForecastLocationUpdated),
                ["weather:f-1"])
        };

        // When
        primitive.ApplyEvents(events, null);

        // Then
        Assert.Equal(2, primitive.Version);
    }

    [Fact]
    public void ApplyState_ShouldRestoreFromSerializedState_WhenVersionMatches()
    {
        // Given
        using var instance = new StubProjectionInstance();
        var primitive = new WasmTagStateProjectionPrimitive(
            instance, "WeatherForecastProjector", "v1", EventTypes, JsonOptions);

        var state = new SerializableTagState(
            Payload: Encoding.UTF8.GetBytes("{\"forecastId\":\"f-1\"}"),
            Version: 5,
            LastSortedUniqueId: "sorted-id-1",
            ProjectorVersion: "v1",
            TagPayloadName: "WeatherForecastState",
            TagGroup: "weather",
            TagContent: "f-1",
            TagProjector: "WeatherForecastProjector");

        // When
        primitive.ApplyState(state);

        // Then
        Assert.Equal(5, primitive.Version);
        Assert.Equal("sorted-id-1", primitive.LastSortedUniqueId);
        Assert.Equal("WeatherForecastState", primitive.TagPayloadName);
        Assert.Equal("weather", primitive.TagGroup);
        Assert.Equal("f-1", primitive.TagContent);
        Assert.Equal("{\"forecastId\":\"f-1\"}", instance.LastRestoredState);
    }

    [Fact]
    public void ApplyState_ShouldResetToInitial_WhenProjectorVersionMismatches()
    {
        // Given
        using var instance = new StubProjectionInstance();
        var primitive = new WasmTagStateProjectionPrimitive(
            instance, "WeatherForecastProjector", "v2", EventTypes, JsonOptions);

        var state = new SerializableTagState(
            Payload: Encoding.UTF8.GetBytes("{\"forecastId\":\"f-1\"}"),
            Version: 5,
            LastSortedUniqueId: "sorted-id-1",
            ProjectorVersion: "v1",
            TagPayloadName: "WeatherForecastState",
            TagGroup: "weather",
            TagContent: "f-1",
            TagProjector: "WeatherForecastProjector");

        // When
        primitive.ApplyState(state);

        // Then: version mismatch causes reset, so state remains initial
        Assert.Equal(0, primitive.Version);
        Assert.Null(primitive.LastSortedUniqueId);
        Assert.Null(instance.LastRestoredState);
    }

    [Fact]
    public void ApplyState_ShouldDoNothing_WhenStateIsNull()
    {
        // Given
        using var instance = new StubProjectionInstance();
        var primitive = new WasmTagStateProjectionPrimitive(
            instance, "WeatherForecastProjector", "v1", EventTypes, JsonOptions);

        // When
        primitive.ApplyState(null);

        // Then
        Assert.Equal(0, primitive.Version);
        Assert.Null(instance.LastRestoredState);
    }

    [Fact]
    public void GetSerializedState_ShouldSerializeCurrentState()
    {
        // Given
        using var instance = new StubProjectionInstance();
        instance.StateToReturn = "{\"forecastId\":\"f-1\",\"location\":\"Tokyo\"}";
        var primitive = new WasmTagStateProjectionPrimitive(
            instance, "WeatherForecastProjector", "v1", EventTypes, JsonOptions);

        var events = new List<Event>
        {
            CreateEvent(
                new WeatherForecastCreated("f-1", "Tokyo", 25, "Warm", DateTimeOffset.UtcNow),
                nameof(WeatherForecastCreated),
                ["weather:f-1"])
        };
        primitive.ApplyEvents(events, null);

        // When
        var result = primitive.GetSerializedState();

        // Then
        Assert.Equal(1, result.Version);
        Assert.Equal("v1", result.ProjectorVersion);
        Assert.Equal("WeatherForecastProjector", result.TagProjector);
        var payloadJson = Encoding.UTF8.GetString(result.Payload);
        Assert.Equal("{\"forecastId\":\"f-1\",\"location\":\"Tokyo\"}", payloadJson);
    }

    [Fact]
    public void GetSerializedState_ShouldUseSnapshotMetadata_WhenGuestReturnsWasmStateSnapshot()
    {
        using var instance = new StubProjectionInstance();
        instance.StateToReturn = JsonSerializer.Serialize(
            new WasmStateSnapshot(
                StateJson: "{\"forecastId\":\"f-1\",\"location\":\"Tokyo\"}",
                SafeVersion: 0,
                UnsafeVersion: 0,
                SafeLastSortableUniqueId: string.Empty,
                LastSortableUniqueId: string.Empty,
                LastEventId: null,
                TagPayloadName: "WeatherForecastState",
                ProjectorVersion: "v1",
                TagGroup: "weather",
                TagContent: "f-1",
                TagProjector: "WeatherForecastProjector"),
            JsonOptions);

        var primitive = new WasmTagStateProjectionPrimitive(
            instance, "WeatherForecastProjector", "v1", EventTypes, JsonOptions);

        primitive.ApplyEvents(
            [
                CreateEvent(
                    new WeatherForecastCreated("f-1", "Tokyo", 25, "Warm", DateTimeOffset.UtcNow),
                    nameof(WeatherForecastCreated),
                    ["weather:f-1"])
            ],
            null);

        var result = primitive.GetSerializedState();

        Assert.Equal("WeatherForecastState", result.TagPayloadName);
        Assert.Equal("weather", result.TagGroup);
        Assert.Equal("f-1", result.TagContent);
        Assert.Equal("WeatherForecastProjector", result.TagProjector);
        Assert.Equal("{\"forecastId\":\"f-1\",\"location\":\"Tokyo\"}", Encoding.UTF8.GetString(result.Payload));
    }

    [Fact]
    public void ApplyState_ThenApplyEvents_ShouldContinueFromRestoredVersion()
    {
        // Given
        using var instance = new StubProjectionInstance();
        var primitive = new WasmTagStateProjectionPrimitive(
            instance, "WeatherForecastProjector", "v1", EventTypes, JsonOptions);

        var state = new SerializableTagState(
            Payload: Encoding.UTF8.GetBytes("{\"forecastId\":\"f-1\"}"),
            Version: 3,
            LastSortedUniqueId: "sorted-id-3",
            ProjectorVersion: "v1",
            TagPayloadName: "WeatherForecastState",
            TagGroup: "weather",
            TagContent: "f-1",
            TagProjector: "WeatherForecastProjector");

        primitive.ApplyState(state);

        var events = new List<Event>
        {
            CreateEvent(
                new WeatherForecastLocationUpdated("f-1", "Osaka", DateTimeOffset.UtcNow),
                nameof(WeatherForecastLocationUpdated),
                ["weather:f-1"])
        };

        // When
        primitive.ApplyEvents(events, null);

        // Then
        Assert.Equal(4, primitive.Version);
    }

    [Fact]
    public void ApplySerializableEvents_ShouldUseBatchPath_WhenInstanceSupportsBatchApply()
    {
        using var instance = new StubBatchProjectionInstance();
        var primitive = new WasmTagStateProjectionPrimitive(
            instance, "WeatherForecastProjector", "v1", EventTypes, JsonOptions);

        var eventA = CreateSerializableEvent(
            nameof(WeatherForecastCreated),
            "{\"forecastId\":\"f-1\"}",
            "weather:f-1",
            "063910000000000000000000000001");
        var eventB = CreateSerializableEvent(
            nameof(WeatherForecastLocationUpdated),
            "{\"forecastId\":\"f-1\",\"location\":\"Osaka\"}",
            "weather:f-1",
            "063910000000000000000000000002");

        primitive.ApplyEvents([eventB, eventA], null);

        Assert.Equal(2, instance.BatchedEvents.Count);
        Assert.Empty(instance.AppliedEvents);
        Assert.Equal("063910000000000000000000000002", primitive.LastSortedUniqueId);
        Assert.Equal(2, primitive.Version);
        Assert.Equal("weather", primitive.TagGroup);
        Assert.Equal("f-1", primitive.TagContent);
    }

    private static Event CreateEvent(
        IEventPayload payload,
        string eventType,
        List<string> tags)
    {
        var id = Guid.NewGuid();
        var sortableId = SortableUniqueId.Generate(DateTime.UtcNow, id);
        var metadata = new EventMetadata(id.ToString(), eventType, "test");
        return new Event(payload, sortableId, eventType, id, metadata, tags);
    }

    private static SerializableEvent CreateSerializableEvent(
        string eventType,
        string payloadJson,
        string tag,
        string sortableUniqueId)
    {
        var id = Guid.NewGuid();
        var metadata = new EventMetadata(id.ToString(), eventType, "test");
        return new SerializableEvent(
            Encoding.UTF8.GetBytes(payloadJson),
            sortableUniqueId,
            id,
            metadata,
            [tag],
            eventType);
    }

    /// <summary>
    /// In-process stub for IPrimitiveProjectionInstance, used for unit tests
    /// without requiring actual WASM module.
    /// </summary>
    private sealed class StubProjectionInstance : IPrimitiveProjectionInstance
    {
        public string StateToReturn { get; set; } = "{}";
        public string? LastRestoredState { get; private set; }
        public List<(string EventType, string PayloadJson)> AppliedEvents { get; } = [];

        public void ApplyEvent(
            string eventType,
            string eventPayloadJson,
            IReadOnlyList<string> tags,
            string? sortableUniqueId)
        {
            AppliedEvents.Add((eventType, eventPayloadJson));
        }

        public string ExecuteQuery(string queryType, string queryParamsJson) => "null";

        public string ExecuteListQuery(string queryType, string queryParamsJson) => "[]";

        public string SerializeState() => StateToReturn;

        public void RestoreState(string stateJson)
        {
            LastRestoredState = stateJson;
        }

        public void Dispose() { }
    }

    private sealed class StubBatchProjectionInstance :
        IPrimitiveProjectionInstance,
        ISerializableEventBatchProjectionInstance
    {
        public string StateToReturn { get; set; } = "{}";
        public List<(string EventType, string PayloadJson)> AppliedEvents { get; } = [];
        public List<SerializableEvent> BatchedEvents { get; } = [];

        public void ApplyEvent(
            string eventType,
            string eventPayloadJson,
            IReadOnlyList<string> tags,
            string? sortableUniqueId)
        {
            AppliedEvents.Add((eventType, eventPayloadJson));
        }

        public void ApplySerializableEvents(IReadOnlyList<SerializableEvent> events)
        {
            BatchedEvents.AddRange(events);
        }

        public string ExecuteQuery(string queryType, string queryParamsJson) => "null";

        public string ExecuteListQuery(string queryType, string queryParamsJson) => "[]";

        public string SerializeState() => StateToReturn;

        public void RestoreState(string stateJson)
        {
        }

        public void Dispose()
        {
        }
    }
}
