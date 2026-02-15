using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.WasmRuntime;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class WasmProjectionRuntimeDeserializeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void DeserializeState_ShouldResetToInitial_WhenProjectorVersionMismatches()
    {
        // Given
        var host = new StubProjectionHost();
        var registry = new WasmProjectorRegistry();
        registry.Register(new WasmModuleRef(
            ProjectorName: "TestProjector",
            ModulePath: "/test.wasm",
            AbiKind: "wasi-preview1",
            ModuleVersion: "1.0.0",
            ProjectorVersion: "v2"));

        var runtime = new WasmProjectionRuntime(host, registry, JsonOptions);

        var snapshot = new WasmStateSnapshot(
            StateJson: "{\"count\":10}",
            SafeVersion: 5,
            UnsafeVersion: 5,
            SafeLastSortableUniqueId: "sorted-1",
            LastSortableUniqueId: "sorted-1",
            LastEventId: Guid.NewGuid(),
            ProjectorVersion: "v1",
            TagProjector: "TestProjector");
        var data = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);

        // When
        var result = runtime.DeserializeState("TestProjector", data, "");

        // Then: version mismatch resets to initial state
        Assert.True(result.IsSuccess);
        var state = (WasmProjectionState)result.GetValue();
        Assert.Equal(0, state.SafeVersion);
        Assert.Equal(0, state.UnsafeVersion);
        Assert.Null(state.LastSortableUniqueId);
        // Initial state means RestoreState was NOT called on this instance
        Assert.Null(host.LastCreatedInstance!.LastRestoredState);
    }

    [Fact]
    public void DeserializeState_ShouldResetToInitial_WhenTagProjectorMismatches()
    {
        // Given
        var host = new StubProjectionHost();
        var registry = new WasmProjectorRegistry();
        registry.Register(new WasmModuleRef(
            ProjectorName: "TestProjector",
            ModulePath: "/test.wasm",
            AbiKind: "wasi-preview1",
            ModuleVersion: "1.0.0",
            ProjectorVersion: "v1"));

        var runtime = new WasmProjectionRuntime(host, registry, JsonOptions);

        var snapshot = new WasmStateSnapshot(
            StateJson: "{\"count\":10}",
            SafeVersion: 5,
            UnsafeVersion: 5,
            SafeLastSortableUniqueId: "sorted-1",
            LastSortableUniqueId: "sorted-1",
            LastEventId: Guid.NewGuid(),
            ProjectorVersion: "v1",
            TagGroup: "weather",
            TagProjector: "DifferentProjector");
        var data = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);

        // When
        var result = runtime.DeserializeState("TestProjector", data, "");

        // Then: identity mismatch resets to initial state
        Assert.True(result.IsSuccess);
        var state = (WasmProjectionState)result.GetValue();
        Assert.Equal(0, state.SafeVersion);
        Assert.Equal(0, state.UnsafeVersion);
    }

    [Fact]
    public void DeserializeState_ShouldRestore_WhenVersionAndIdentityMatch()
    {
        // Given
        var host = new StubProjectionHost();
        var registry = new WasmProjectorRegistry();
        registry.Register(new WasmModuleRef(
            ProjectorName: "TestProjector",
            ModulePath: "/test.wasm",
            AbiKind: "wasi-preview1",
            ModuleVersion: "1.0.0",
            ProjectorVersion: "v1"));

        var runtime = new WasmProjectionRuntime(host, registry, JsonOptions);

        var snapshot = new WasmStateSnapshot(
            StateJson: "{\"count\":10}",
            SafeVersion: 5,
            UnsafeVersion: 7,
            SafeLastSortableUniqueId: "sorted-5",
            LastSortableUniqueId: "sorted-7",
            LastEventId: Guid.NewGuid(),
            ProjectorVersion: "v1",
            TagProjector: "TestProjector");
        var data = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);

        // When
        var result = runtime.DeserializeState("TestProjector", data, "");

        // Then: matching version/identity restores the state
        Assert.True(result.IsSuccess);
        var state = (WasmProjectionState)result.GetValue();
        Assert.Equal(5, state.SafeVersion);
        Assert.Equal(7, state.UnsafeVersion);
        Assert.Equal("sorted-7", state.LastSortableUniqueId);
        Assert.Equal("{\"count\":10}", host.LastCreatedInstance!.LastRestoredState);
    }

    [Fact]
    public void DeserializeState_ShouldRestore_WhenProjectorVersionIsNull()
    {
        // Given: old-format snapshot without ProjectorVersion
        var host = new StubProjectionHost();
        var registry = new WasmProjectorRegistry();
        registry.Register(new WasmModuleRef(
            ProjectorName: "TestProjector",
            ModulePath: "/test.wasm",
            AbiKind: "wasi-preview1",
            ModuleVersion: "1.0.0",
            ProjectorVersion: "v1"));

        var runtime = new WasmProjectionRuntime(host, registry, JsonOptions);

        var snapshot = new WasmStateSnapshot(
            StateJson: "{\"count\":10}",
            SafeVersion: 3,
            UnsafeVersion: 3,
            SafeLastSortableUniqueId: "sorted-3",
            LastSortableUniqueId: "sorted-3",
            LastEventId: Guid.NewGuid(),
            ProjectorVersion: null,
            TagProjector: null);
        var data = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);

        // When
        var result = runtime.DeserializeState("TestProjector", data, "");

        // Then: null ProjectorVersion skips guard, restores state
        Assert.True(result.IsSuccess);
        var state = (WasmProjectionState)result.GetValue();
        Assert.Equal(3, state.SafeVersion);
        Assert.Equal("{\"count\":10}", host.LastCreatedInstance!.LastRestoredState);
    }

    [Fact]
    public void SerializeState_ShouldIncludeProjectorVersionAndTagProjector()
    {
        // Given
        var host = new StubProjectionHost();
        var registry = new WasmProjectorRegistry();
        registry.Register(new WasmModuleRef(
            ProjectorName: "TestProjector",
            ModulePath: "/test.wasm",
            AbiKind: "wasi-preview1",
            ModuleVersion: "1.0.0",
            ProjectorVersion: "v1"));

        var runtime = new WasmProjectionRuntime(host, registry, JsonOptions);

        var initialResult = runtime.GenerateInitialState("TestProjector");
        Assert.True(initialResult.IsSuccess);
        var state = initialResult.GetValue();

        // When
        var serializeResult = runtime.SerializeState("TestProjector", state);

        // Then
        Assert.True(serializeResult.IsSuccess);
        var bytes = serializeResult.GetValue();
        var snapshot = JsonSerializer.Deserialize<WasmStateSnapshot>(bytes, JsonOptions);
        Assert.NotNull(snapshot);
        Assert.Equal("v1", snapshot.ProjectorVersion);
        Assert.Equal("TestProjector", snapshot.TagProjector);
    }

    /// <summary>
    /// Stub IPrimitiveProjectionHost that creates StubProjectionInstance instances.
    /// </summary>
    private sealed class StubProjectionHost : IPrimitiveProjectionHost
    {
        public StubProjectionInstance? LastCreatedInstance { get; private set; }

        public IPrimitiveProjectionInstance CreateInstance(string projectorName)
        {
            var instance = new StubProjectionInstance();
            LastCreatedInstance = instance;
            return instance;
        }
    }

    /// <summary>
    /// In-process stub for IPrimitiveProjectionInstance.
    /// </summary>
    internal sealed class StubProjectionInstance : IPrimitiveProjectionInstance
    {
        public string? LastRestoredState { get; private set; }

        public void ApplyEvent(
            string eventType,
            string eventPayloadJson,
            IReadOnlyList<string> tags,
            string? sortableUniqueId) { }

        public string ExecuteQuery(string queryType, string queryParamsJson) => "null";

        public string ExecuteListQuery(string queryType, string queryParamsJson) => "[]";

        public string SerializeState() => "{}";

        public void RestoreState(string stateJson)
        {
            LastRestoredState = stateJson;
        }

        public void Dispose() { }
    }
}
