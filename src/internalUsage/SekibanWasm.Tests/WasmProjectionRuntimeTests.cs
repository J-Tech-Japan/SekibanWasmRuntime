using System.Text.Json;
using Sekiban.Dcb.WasmRuntime;
using Xunit;

namespace SekibanWasm.Tests;

public class WasmProjectionRuntimeTests
{
    private readonly WasmProjectorRegistry _registry;
    private readonly FakePrimitiveProjectionHost _host;
    private readonly WasmProjectionRuntime _runtime;
    private readonly JsonSerializerOptions _jsonOptions;

    public WasmProjectionRuntimeTests()
    {
        _registry = new WasmProjectorRegistry();
        _registry.Register(new WasmModuleRef(
            "WeatherForecast", "/test.wasm", "c-abi", "1.0.0", "v1"));
        _registry.MapQueryToProjector("GetWeatherForecastListQuery", "WeatherForecast");

        _host = new FakePrimitiveProjectionHost();
        _host.RegisterProjector("WeatherForecast");

        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        _runtime = new WasmProjectionRuntime(_host, _registry, _jsonOptions);
    }

    [Fact]
    public void GenerateInitialState_ShouldReturnSuccessForRegisteredProjector()
    {
        // When
        var result = _runtime.GenerateInitialState("WeatherForecast");

        // Then
        Assert.True(result.IsSuccess);
        var state = result.GetValue();
        Assert.IsType<WasmProjectionState>(state);
        Assert.Equal(0, state.SafeVersion);
        Assert.Equal(0, state.UnsafeVersion);
    }

    [Fact]
    public void GenerateInitialState_ShouldReturnErrorForUnregisteredProjector()
    {
        // When
        var result = _runtime.GenerateInitialState("NonExistent");

        // Then
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void GetProjectorVersion_ShouldReturnVersionForRegistered()
    {
        // When
        var result = _runtime.GetProjectorVersion("WeatherForecast");

        // Then
        Assert.True(result.IsSuccess);
        Assert.Equal("v1", result.GetValue());
    }

    [Fact]
    public void GetProjectorVersion_ShouldReturnErrorForUnregistered()
    {
        // When
        var result = _runtime.GetProjectorVersion("NonExistent");

        // Then
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void GetAllProjectorNames_ShouldReturnRegisteredNames()
    {
        // When
        var names = _runtime.GetAllProjectorNames();

        // Then
        Assert.Single(names);
        Assert.Equal("WeatherForecast", names[0]);
    }

    [Fact]
    public void SerializeState_ShouldReturnSnapshotBytes()
    {
        // Given
        var stateResult = _runtime.GenerateInitialState("WeatherForecast");
        Assert.True(stateResult.IsSuccess);
        var state = stateResult.GetValue();

        // When
        var serializeResult = _runtime.SerializeState("WeatherForecast", state);

        // Then
        Assert.True(serializeResult.IsSuccess);
        var bytes = serializeResult.GetValue();
        Assert.NotEmpty(bytes);

        var snapshot = JsonSerializer.Deserialize<WasmStateSnapshot>(bytes, _jsonOptions);
        Assert.NotNull(snapshot);
        Assert.Equal("{}", snapshot.StateJson);
    }

    [Fact]
    public void DeserializeState_ShouldRestoreState()
    {
        // Given
        var snapshot = new WasmStateSnapshot(
            "{\"test\":true}",
            SafeVersion: 3,
            UnsafeVersion: 5,
            SafeLastSortableUniqueId: "safe-id",
            LastSortableUniqueId: "last-id",
            LastEventId: Guid.NewGuid());
        var data = JsonSerializer.SerializeToUtf8Bytes(snapshot, _jsonOptions);

        // When
        var result = _runtime.DeserializeState("WeatherForecast", data, "threshold");

        // Then
        Assert.True(result.IsSuccess);
        var state = result.GetValue();
        Assert.IsType<WasmProjectionState>(state);
        Assert.Equal(3, state.SafeVersion);
        Assert.Equal(5, state.UnsafeVersion);
        Assert.Equal("safe-id", state.SafeLastSortableUniqueId);
        Assert.Equal("last-id", state.LastSortableUniqueId);
    }
}
