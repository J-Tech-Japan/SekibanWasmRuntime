using System.Text.Json;
using Sekiban.Dcb.WasmRuntime;
using Xunit;

namespace SekibanWasm.Tests;

public class SnapshotRoundTripTests
{
    private readonly WasmProjectorRegistry _registry;
    private readonly FakePrimitiveProjectionHost _host;
    private readonly WasmProjectionRuntime _runtime;
    private readonly JsonSerializerOptions _jsonOptions;

    public SnapshotRoundTripTests()
    {
        _registry = new WasmProjectorRegistry();
        _registry.Register(new WasmModuleRef(
            "WeatherForecast", "/test.wasm", "c-abi", "1.0.0", "v1"));
        _registry.MapQueryToProjector("GetWeatherQuery", "WeatherForecast");

        _host = new FakePrimitiveProjectionHost();
        _host.RegisterProjector("WeatherForecast", () =>
        {
            var instance = new FakePrimitiveProjectionInstance();
            instance.QueryResponses["GetWeatherQuery"] = "{\"location\":\"Tokyo\",\"temp\":25}";
            return instance;
        });

        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        _runtime = new WasmProjectionRuntime(_host, _registry, _jsonOptions);
    }

    [Fact]
    public void SerializeAndDeserialize_ShouldPreserveState()
    {
        // Given: an initial state with some state JSON
        var stateResult = _runtime.GenerateInitialState("WeatherForecast");
        Assert.True(stateResult.IsSuccess);
        var state = stateResult.GetValue();

        // When: serialize
        var serializeResult = _runtime.SerializeState("WeatherForecast", state);
        Assert.True(serializeResult.IsSuccess);
        var serializedBytes = serializeResult.GetValue();

        // Then: deserialize into a new state
        var deserializeResult = _runtime.DeserializeState("WeatherForecast", serializedBytes, "threshold");
        Assert.True(deserializeResult.IsSuccess);
        var restoredState = deserializeResult.GetValue();

        // Both states should be valid WasmProjectionState
        Assert.IsType<WasmProjectionState>(state);
        Assert.IsType<WasmProjectionState>(restoredState);
    }

    [Fact]
    public void SerializeAndDeserialize_ShouldPreserveVersionMetadata()
    {
        // Given: a snapshot with specific version data
        var eventId = Guid.NewGuid();
        var snapshot = new WasmStateSnapshot(
            StateJson: "{\"forecasts\":{\"f1\":{\"location\":\"Tokyo\"}}}",
            SafeVersion: 10,
            UnsafeVersion: 15,
            SafeLastSortableUniqueId: "safe-001",
            LastSortableUniqueId: "last-005",
            LastEventId: eventId);
        var data = JsonSerializer.SerializeToUtf8Bytes(snapshot, _jsonOptions);

        // When: deserialize
        var result = _runtime.DeserializeState("WeatherForecast", data, "threshold");

        // Then: metadata should be preserved
        Assert.True(result.IsSuccess);
        var state = result.GetValue();
        Assert.Equal(10, state.SafeVersion);
        Assert.Equal(15, state.UnsafeVersion);
        Assert.Equal("safe-001", state.SafeLastSortableUniqueId);
        Assert.Equal("last-005", state.LastSortableUniqueId);
        Assert.Equal(eventId, state.LastEventId);
    }

    [Fact]
    public void SerializeAndDeserialize_RestoredInstance_ShouldReturnSameQueryResult()
    {
        // Given: create state and serialize
        var stateResult = _runtime.GenerateInitialState("WeatherForecast");
        Assert.True(stateResult.IsSuccess);
        var originalState = (WasmProjectionState)stateResult.GetValue();

        // Set specific state JSON on the original instance
        var fakeInstance = (FakePrimitiveProjectionInstance)originalState.Instance;
        fakeInstance.SetStateJson("{\"forecasts\":{\"f1\":{\"location\":\"Tokyo\",\"temp\":25}}}");

        var serializeResult = _runtime.SerializeState("WeatherForecast", originalState);
        Assert.True(serializeResult.IsSuccess);
        var serializedBytes = serializeResult.GetValue();

        // When: deserialize into new state
        var deserializeResult = _runtime.DeserializeState("WeatherForecast", serializedBytes, "threshold");
        Assert.True(deserializeResult.IsSuccess);
        var restoredState = (WasmProjectionState)deserializeResult.GetValue();

        // Then: the restored instance's serialized state should match
        var restoredFake = (FakePrimitiveProjectionInstance)restoredState.Instance;
        var restoredStateJson = restoredFake.SerializeState();
        Assert.Equal("{\"forecasts\":{\"f1\":{\"location\":\"Tokyo\",\"temp\":25}}}", restoredStateJson);
    }

    [Fact]
    public void Serialize_EmptyState_ShouldRoundTrip()
    {
        // Given: a fresh state with default (empty) JSON
        var stateResult = _runtime.GenerateInitialState("WeatherForecast");
        Assert.True(stateResult.IsSuccess);
        var state = stateResult.GetValue();

        // When: serialize then deserialize
        var serialized = _runtime.SerializeState("WeatherForecast", state);
        Assert.True(serialized.IsSuccess);
        var deserialized = _runtime.DeserializeState("WeatherForecast", serialized.GetValue(), "threshold");

        // Then: should succeed
        Assert.True(deserialized.IsSuccess);
        Assert.Equal(0, deserialized.GetValue().SafeVersion);
        Assert.Equal(0, deserialized.GetValue().UnsafeVersion);
    }
}
