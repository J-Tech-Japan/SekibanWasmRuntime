using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.WasmRuntime;
using Xunit;

namespace SekibanWasm.Tests;

public class HybridRuntimeRoutingTests
{
    private readonly StubProjectionRuntime _nativeRuntime;
    private readonly StubProjectionRuntime _wasmRuntime;
    private readonly ProjectorRuntimeResolver _resolver;
    private readonly CompositeProjectionRuntime _compositeRuntime;

    public HybridRuntimeRoutingTests()
    {
        _nativeRuntime = new StubProjectionRuntime("native", ["NativeProjector"]);
        _wasmRuntime = new StubProjectionRuntime("wasm", ["WeatherForecastMultiProjection"]);

        _resolver = new ProjectorRuntimeResolver(
            defaultRuntime: _nativeRuntime,
            runtimeMap: new Dictionary<string, IProjectionRuntime>
            {
                ["WeatherForecastMultiProjection"] = _wasmRuntime
            });

        _compositeRuntime = new CompositeProjectionRuntime(_resolver);
    }

    [Fact]
    public void Resolve_RegisteredProjector_ShouldReturnWasmRuntime()
    {
        // When
        var resolved = _resolver.Resolve("WeatherForecastMultiProjection");

        // Then
        Assert.Same(_wasmRuntime, resolved);
    }

    [Fact]
    public void Resolve_UnregisteredProjector_ShouldReturnNativeRuntime()
    {
        // When
        var resolved = _resolver.Resolve("NativeProjector");

        // Then
        Assert.Same(_nativeRuntime, resolved);
    }

    [Fact]
    public void Resolve_UnknownProjector_ShouldReturnDefaultRuntime()
    {
        // When
        var resolved = _resolver.Resolve("CompletelyUnknown");

        // Then
        Assert.Same(_nativeRuntime, resolved);
    }

    [Fact]
    public void GetAllRuntimes_ShouldReturnAllDistinctRuntimes()
    {
        // When
        var runtimes = _resolver.GetAllRuntimes();

        // Then
        Assert.Equal(2, runtimes.Count);
        Assert.Contains(_nativeRuntime, runtimes);
        Assert.Contains(_wasmRuntime, runtimes);
    }

    [Fact]
    public void CompositeRuntime_GetAllProjectorNames_ShouldMergeAllRuntimes()
    {
        // When
        var names = _compositeRuntime.GetAllProjectorNames();

        // Then
        Assert.Equal(2, names.Count);
        Assert.Contains("NativeProjector", names);
        Assert.Contains("WeatherForecastMultiProjection", names);
    }

    [Fact]
    public void CompositeRuntime_GenerateInitialState_ShouldDelegateToCorrectRuntime()
    {
        // When
        var wasmResult = _compositeRuntime.GenerateInitialState("WeatherForecastMultiProjection");
        var nativeResult = _compositeRuntime.GenerateInitialState("NativeProjector");

        // Then
        Assert.True(wasmResult.IsSuccess);
        Assert.True(nativeResult.IsSuccess);
        Assert.Equal("wasm", _wasmRuntime.LastCalledMethod);
        Assert.Equal("native", _nativeRuntime.LastCalledMethod);
    }

    [Fact]
    public void CompositeRuntime_GetProjectorVersion_ShouldDelegateToCorrectRuntime()
    {
        // When
        var wasmVersion = _compositeRuntime.GetProjectorVersion("WeatherForecastMultiProjection");
        var nativeVersion = _compositeRuntime.GetProjectorVersion("NativeProjector");

        // Then
        Assert.True(wasmVersion.IsSuccess);
        Assert.Equal("wasm-v1", wasmVersion.GetValue());
        Assert.True(nativeVersion.IsSuccess);
        Assert.Equal("native-v1", nativeVersion.GetValue());
    }

    /// <summary>
    /// Minimal stub that records which runtime was called and returns predictable results.
    /// </summary>
    private sealed class StubProjectionRuntime : IProjectionRuntime
    {
        private readonly string _name;
        private readonly List<string> _projectorNames;

        public string? LastCalledMethod { get; private set; }

        public StubProjectionRuntime(string name, List<string> projectorNames)
        {
            _name = name;
            _projectorNames = projectorNames;
        }

        public ResultBox<IProjectionState> GenerateInitialState(string projectorName)
        {
            LastCalledMethod = _name;
            return ResultBox<IProjectionState>.FromValue(new StubProjectionState());
        }

        public ResultBox<string> GetProjectorVersion(string projectorName)
        {
            return ResultBox<string>.FromValue($"{_name}-v1");
        }

        public IReadOnlyList<string> GetAllProjectorNames() => _projectorNames;

        public ResultBox<IProjectionState> ApplyEvent(string projectorName, IProjectionState currentState, Event ev, string safeWindowThreshold)
            => ResultBox<IProjectionState>.FromValue(currentState);

        public ResultBox<IProjectionState> ApplyEvents(string projectorName, IProjectionState currentState, IReadOnlyList<Event> events, string safeWindowThreshold)
            => ResultBox<IProjectionState>.FromValue(currentState);

        public Task<ResultBox<SerializableQueryResult>> ExecuteQueryAsync(string projectorName, IProjectionState state, SerializableQueryParameter query, IServiceProvider serviceProvider)
            => Task.FromResult(ResultBox<SerializableQueryResult>.FromException(new NotImplementedException()));

        public Task<ResultBox<SerializableListQueryResult>> ExecuteListQueryAsync(string projectorName, IProjectionState state, SerializableQueryParameter query, IServiceProvider serviceProvider)
            => Task.FromResult(ResultBox<SerializableListQueryResult>.FromException(new NotImplementedException()));

        public ResultBox<byte[]> SerializeState(string projectorName, IProjectionState state)
            => ResultBox<byte[]>.FromValue(System.Text.Encoding.UTF8.GetBytes("{}"));

        public ResultBox<IProjectionState> DeserializeState(string projectorName, byte[] data, string safeWindowThreshold)
            => ResultBox<IProjectionState>.FromValue(new StubProjectionState());

        public ResultBox<string> ResolveProjectorName(IQueryCommon query)
            => ResultBox<string>.FromException(new InvalidOperationException("Not mapped"));

        public ResultBox<string> ResolveProjectorName(IListQueryCommon query)
            => ResultBox<string>.FromException(new InvalidOperationException("Not mapped"));

        private sealed class StubProjectionState : IProjectionState
        {
            public int SafeVersion => 0;
            public int UnsafeVersion => 0;
            public string? SafeLastSortableUniqueId => null;
            public string? LastSortableUniqueId => null;
            public Guid? LastEventId => null;
            public object? GetSafePayload() => null;
            public object? GetUnsafePayload() => null;
            public long EstimatePayloadSizeBytes(JsonSerializerOptions? options) => 0;
        }
    }
}
