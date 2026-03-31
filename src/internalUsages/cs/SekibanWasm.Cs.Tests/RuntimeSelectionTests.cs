using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sekiban.Dcb.Events;
using SekibanWasm.Cs.Domain;
using SekibanWasm.Cs.Domain.Weather;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Runtime.Native;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.Tags;
using System.Text.Json;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class RuntimeSelectionTests
{
    [Fact]
    public void Validate_ShouldSucceed_ForNativeMode()
    {
        // Given
        var options = new WasmTagStateOptions { Mode = WasmRuntimeMode.Native };

        // When / Then: no exception
        options.Validate();
    }

    [Fact]
    public void Validate_ShouldThrow_WhenWasmModeWithoutModulePath()
    {
        // Given
        var options = new WasmTagStateOptions { Mode = WasmRuntimeMode.Wasm };

        // When / Then
        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());
        Assert.Contains("WasmModulePath", ex.Message);
    }

    [Fact]
    public void Validate_ShouldSucceed_WhenWasmModeWithModulePath()
    {
        // Given
        var options = new WasmTagStateOptions
        {
            Mode = WasmRuntimeMode.Wasm,
            WasmModulePath = "/path/to/module.wasm"
        };

        // When / Then: no exception
        options.Validate();
    }

    [Fact]
    public void Validate_ShouldThrow_WhenHybridModeWithoutModulePath()
    {
        // Given
        var options = new WasmTagStateOptions { Mode = WasmRuntimeMode.Hybrid };

        // When / Then
        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());
        Assert.Contains("WasmModulePath", ex.Message);
    }

    [Fact]
    public void Validate_ShouldSucceed_WhenHybridModeWithModulePath()
    {
        // Given
        var options = new WasmTagStateOptions
        {
            Mode = WasmRuntimeMode.Hybrid,
            WasmModulePath = "/path/to/module.wasm"
        };

        // When / Then: no exception
        options.Validate();
    }

    [Fact]
    public void Validate_ShouldThrow_WhenRemoteModeWithoutEndpoint()
    {
        // Given
        var options = new WasmTagStateOptions { Mode = WasmRuntimeMode.Remote };

        // When / Then
        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());
        Assert.Contains("RemoteEndpoint", ex.Message);
    }

    [Fact]
    public void Validate_ShouldSucceed_WhenRemoteModeWithEndpoint()
    {
        // Given
        var options = new WasmTagStateOptions
        {
            Mode = WasmRuntimeMode.Remote,
            RemoteEndpoint = "https://wasm-runtime.example.com"
        };

        // When / Then: no exception
        options.Validate();
    }

    [Fact]
    public void WasmProjectorRegistry_ShouldResolveRegisteredProjector()
    {
        // Given
        var registry = new WasmProjectorRegistry();
        registry.Register(new WasmModuleRef(
            ProjectorName: "TestProjector",
            ModulePath: "/test.wasm",
            AbiKind: "wasi-preview1",
            ModuleVersion: "1.0.0",
            ProjectorVersion: "v1"));

        // When
        var result = registry.TryGet("TestProjector");

        // Then
        Assert.NotNull(result);
        Assert.Equal("v1", result.ProjectorVersion);
    }

    [Fact]
    public void WasmProjectorRegistry_ShouldReturnNull_ForUnregisteredProjector()
    {
        // Given
        var registry = new WasmProjectorRegistry();

        // When
        var result = registry.TryGet("NonExistent");

        // Then
        Assert.Null(result);
    }

    [Fact]
    public void WasmProjectorRegistry_ShouldResolveQueryToProjector()
    {
        // Given
        var registry = new WasmProjectorRegistry();
        registry.Register(new WasmModuleRef(
            ProjectorName: "WeatherProjector",
            ModulePath: "/test.wasm",
            AbiKind: "wasi-preview1",
            ModuleVersion: "1.0.0",
            ProjectorVersion: "v1"));
        registry.MapQueryToProjector("GetWeatherQuery", "WeatherProjector");

        // When
        var result = registry.ResolveProjectorForQuery("GetWeatherQuery");

        // Then
        Assert.Equal("WeatherProjector", result);
    }

    [Fact]
    public void WasmProjectorRegistry_ShouldReturnNull_ForUnmappedQuery()
    {
        // Given
        var registry = new WasmProjectorRegistry();

        // When
        var result = registry.ResolveProjectorForQuery("UnmappedQuery");

        // Then
        Assert.Null(result);
    }

    [Fact]
    public void ProjectorRuntimeResolver_ShouldReturnMappedRuntime_WhenProjectorRegistered()
    {
        // Given
        var defaultRuntime = new StubProjectionRuntime("default");
        var wasmRuntime = new StubProjectionRuntime("wasm");
        var runtimeMap = new Dictionary<string, Sekiban.Dcb.Runtime.IProjectionRuntime>
        {
            ["WeatherProjector"] = wasmRuntime
        };
        var resolver = new ProjectorRuntimeResolver(defaultRuntime, runtimeMap);

        // When
        var result = resolver.Resolve("WeatherProjector");

        // Then
        Assert.Same(wasmRuntime, result);
    }

    [Fact]
    public void ProjectorRuntimeResolver_ShouldReturnDefault_WhenProjectorNotRegistered()
    {
        // Given
        var defaultRuntime = new StubProjectionRuntime("default");
        var runtimeMap = new Dictionary<string, Sekiban.Dcb.Runtime.IProjectionRuntime>();
        var resolver = new ProjectorRuntimeResolver(defaultRuntime, runtimeMap);

        // When
        var result = resolver.Resolve("UnknownProjector");

        // Then
        Assert.Same(defaultRuntime, result);
    }

    [Fact]
    public void ProjectorRuntimeResolver_GetAllRuntimes_ShouldIncludeDefault()
    {
        // Given
        var defaultRuntime = new StubProjectionRuntime("default");
        var wasmRuntime = new StubProjectionRuntime("wasm");
        var runtimeMap = new Dictionary<string, Sekiban.Dcb.Runtime.IProjectionRuntime>
        {
            ["WeatherProjector"] = wasmRuntime
        };
        var resolver = new ProjectorRuntimeResolver(defaultRuntime, runtimeMap);

        // When
        var allRuntimes = resolver.GetAllRuntimes();

        // Then
        Assert.Contains(defaultRuntime, allRuntimes);
        Assert.Contains(wasmRuntime, allRuntimes);
    }

    [Fact]
    public void AddWasmTagStateRuntime_WasmMode_ShouldRegisterWasmProjectionRuntime()
    {
        // Given
        var services = new ServiceCollection();
        services.AddSingleton<IPrimitiveProjectionHost>(new StubPrimitiveProjectionHost());
        services.AddSingleton(new WasmProjectorRegistry());
        services.AddSingleton(new JsonSerializerOptions());
        services.AddSingleton(DomainType.GetDomainTypes().EventTypes);

        // When
        services.AddWasmTagStateRuntime(o =>
        {
            o.Mode = WasmRuntimeMode.Wasm;
            o.WasmModulePath = "/test.wasm";
        });

        // Then
        Assert.Contains(
            services,
            d => d.ServiceType == typeof(IProjectionRuntime) &&
                 d.ImplementationType == typeof(WasmProjectionRuntime));
        Assert.Contains(
            services,
            d => d.ServiceType == typeof(ITagStateProjectionPrimitive) &&
                 d.ImplementationType == typeof(WasmTagStateProjectionPrimitiveFactory));
        Assert.Contains(
            services,
            d => d.ServiceType == typeof(WasmTagStateOptions));
    }

    [Fact]
    public void AddWasmTagStateRuntime_HybridMode_ShouldRegisterCompositeProjectionRuntime()
    {
        // Given
        var services = new ServiceCollection();
        services.AddSingleton<IPrimitiveProjectionHost>(new StubPrimitiveProjectionHost());
        services.AddSingleton(new WasmProjectorRegistry());
        services.AddSingleton(new JsonSerializerOptions());
        services.AddSingleton(DomainType.GetDomainTypes().EventTypes);
        var defaultRuntime = new StubProjectionRuntime("default");
        services.AddSingleton<Sekiban.Dcb.WasmRuntime.IProjectorRuntimeResolver>(
            new ProjectorRuntimeResolver(defaultRuntime, new Dictionary<string, IProjectionRuntime>()));

        // When
        services.AddWasmTagStateRuntime(o =>
        {
            o.Mode = WasmRuntimeMode.Hybrid;
            o.WasmModulePath = "/test.wasm";
        });

        // Then
        Assert.Contains(
            services,
            d => d.ServiceType == typeof(IProjectionRuntime) &&
                 d.ImplementationType == typeof(CompositeProjectionRuntime));
        Assert.Contains(
            services,
            d => d.ServiceType == typeof(ITagStateProjectionPrimitive) &&
                 d.ImplementationType == typeof(WasmTagStateProjectionPrimitiveFactory));
        Assert.Contains(
            services,
            d => d.ServiceType == typeof(WasmTagStateOptions));
    }

    [Fact]
    public void AddWasmTagStateRuntime_NativeMode_ShouldNotRegisterProjectionRuntime()
    {
        // Given
        var services = new ServiceCollection();

        // When
        services.AddWasmTagStateRuntime(o =>
        {
            o.Mode = WasmRuntimeMode.Native;
        });

        // Then
        Assert.DoesNotContain(
            services,
            d => d.ServiceType == typeof(IProjectionRuntime));
        Assert.Contains(
            services,
            d => d.ServiceType == typeof(WasmTagStateOptions));
    }

    [Fact]
    public void AddWasmTagStateRuntime_RemoteMode_ShouldNotRegisterProjectionRuntime()
    {
        // Given
        var services = new ServiceCollection();

        // When
        services.AddWasmTagStateRuntime(o =>
        {
            o.Mode = WasmRuntimeMode.Remote;
            o.RemoteEndpoint = "https://wasm-runtime.example.com";
        });

        // Then
        Assert.DoesNotContain(
            services,
            d => d.ServiceType == typeof(IProjectionRuntime));
        Assert.Contains(
            services,
            d => d.ServiceType == typeof(WasmTagStateOptions));
    }

    [Fact]
    public void AddSekibanDcbSharedRuntime_ShouldRegisterOnlySharedRuntimeServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(DomainType.GetDomainTypes());

        services.AddSekibanDcbSharedRuntime();

        Assert.Contains(services, d => d.ServiceType == typeof(ITagProjectorTypes));
        Assert.Contains(services, d => d.ServiceType == typeof(ITagTypes));
        Assert.Contains(services, d => d.ServiceType == typeof(IEventTypes));
        Assert.Contains(services, d => d.ServiceType == typeof(ITagStatePayloadTypes));
        Assert.Contains(services, d => d.ServiceType == typeof(SnapshotTempFileOptions));
        Assert.Contains(services, d => d.ServiceType == typeof(TempFileSnapshotManager));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IProjectionActorHostFactory));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ITagStateProjectionPrimitive));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IMultiProjectionProjectionPrimitive));
    }

    [Fact]
    public void AddSekibanDcbFullNativeRuntime_ShouldRegisterSharedAndNativeRuntimeServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(DomainType.GetDomainTypes());

        services.AddSekibanDcbFullNativeRuntime();

        Assert.Contains(
            services,
            d => d.ServiceType == typeof(IProjectionActorHostFactory) &&
                 d.ImplementationType == typeof(NativeProjectionActorHostFactory));
        Assert.Contains(
            services,
            d => d.ServiceType == typeof(ITagStateProjectionPrimitive) &&
                 d.ImplementationType == typeof(NativeTagStateProjectionPrimitive));
        Assert.Contains(
            services,
            d => d.ServiceType == typeof(NativeMultiProjectionProjectionPrimitive) &&
                 d.ImplementationType == typeof(NativeMultiProjectionProjectionPrimitive));
        Assert.Contains(
            services,
            d => d.ServiceType == typeof(IMultiProjectionProjectionPrimitive) &&
                 d.ImplementationFactory is not null);
        Assert.Contains(services, d => d.ServiceType == typeof(ITagProjectorTypes));
        Assert.Contains(services, d => d.ServiceType == typeof(ITagTypes));
        Assert.Contains(services, d => d.ServiceType == typeof(IEventTypes));
        Assert.Contains(services, d => d.ServiceType == typeof(ITagStatePayloadTypes));
        Assert.Contains(services, d => d.ServiceType == typeof(SnapshotTempFileOptions));
        Assert.Contains(services, d => d.ServiceType == typeof(TempFileSnapshotManager));
    }

    [Fact]
    public void AddWasmTagStateRuntime_WasmMode_ShouldThrow_WhenDependenciesAreMissing()
    {
        // Given
        var services = new ServiceCollection();

        // When / Then
        var ex = Assert.Throws<InvalidOperationException>(() => services.AddWasmTagStateRuntime(o =>
        {
            o.Mode = WasmRuntimeMode.Wasm;
            o.WasmModulePath = "/test.wasm";
        }));
        Assert.Contains(nameof(IPrimitiveProjectionHost), ex.Message);
    }

    [Fact]
    public void AddWasmTagStateRuntime_HybridMode_ShouldThrow_WhenResolverIsMissing()
    {
        // Given
        var services = new ServiceCollection();
        services.AddSingleton<IPrimitiveProjectionHost>(new StubPrimitiveProjectionHost());
        services.AddSingleton(new WasmProjectorRegistry());
        services.AddSingleton(new JsonSerializerOptions());
        services.AddSingleton(DomainType.GetDomainTypes().EventTypes);

        // When / Then
        var ex = Assert.Throws<InvalidOperationException>(() => services.AddWasmTagStateRuntime(o =>
        {
            o.Mode = WasmRuntimeMode.Hybrid;
            o.WasmModulePath = "/test.wasm";
        }));
        Assert.Contains("IProjectorRuntimeResolver", ex.Message);
    }

    [Fact]
    public void AddWasmTagStateRuntime_WasmMode_ShouldReplaceExistingTagStatePrimitive()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPrimitiveProjectionHost>(new StubPrimitiveProjectionHost());
        services.AddSingleton(new WasmProjectorRegistry());
        services.AddSingleton(new JsonSerializerOptions());
        services.AddSingleton(DomainType.GetDomainTypes().EventTypes);
        services.AddSingleton<ITagStateProjectionPrimitive>(new StubTagStateProjectionPrimitive());

        services.AddWasmTagStateRuntime(o =>
        {
            o.Mode = WasmRuntimeMode.Wasm;
            o.WasmModulePath = "/test.wasm";
        });

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ITagStateProjectionPrimitive));
        Assert.Equal(typeof(WasmTagStateProjectionPrimitiveFactory), descriptor.ImplementationType);
    }

    [Fact]
    public void MissingAccumulator_ShouldPreserveCachedState_WhenModuleIsUnavailable()
    {
        var registry = new WasmProjectorRegistry();
        var factory = new WasmTagStateProjectionPrimitiveFactory(
            new FailingPrimitiveProjectionHost(),
            registry,
            DomainType.GetDomainTypes().EventTypes,
            new JsonSerializerOptions(),
            NullLogger<WasmTagStateProjectionPrimitiveFactory>.Instance);

        var accumulator = factory.CreateAccumulator(TagStateId.Parse("weather:f-1:WeatherForecastProjector"));
        var cachedState = new SerializableTagState(
            Payload: [1, 2, 3],
            Version: 7,
            LastSortedUniqueId: "sorted-7",
            TagGroup: "weather",
            TagContent: "f-1",
            TagProjector: nameof(WeatherForecastProjector),
            TagPayloadName: nameof(WeatherForecastState),
            ProjectorVersion: "v1");

        Assert.True(accumulator.ApplyState(cachedState));
        Assert.True(accumulator.ApplyEvents([], null));

        var result = accumulator.GetSerializedState();

        Assert.Same(cachedState, result);
    }

    [Fact]
    public void MissingAccumulator_ShouldFail_WhenPendingEventsExist()
    {
        var registry = new WasmProjectorRegistry();
        var factory = new WasmTagStateProjectionPrimitiveFactory(
            new FailingPrimitiveProjectionHost(),
            registry,
            DomainType.GetDomainTypes().EventTypes,
            new JsonSerializerOptions(),
            NullLogger<WasmTagStateProjectionPrimitiveFactory>.Instance);

        var accumulator = factory.CreateAccumulator(TagStateId.Parse("weather:f-1:WeatherForecastProjector"));
        var pendingEvents = new[]
        {
            new SerializableEvent(
                Payload: JsonSerializer.SerializeToUtf8Bytes(
                    new WeatherForecastCreated("f-1", "Tokyo", 25, "Warm", DateTimeOffset.UtcNow)),
                SortableUniqueIdValue: "sorted-1",
                Id: Guid.NewGuid(),
                EventMetadata: new EventMetadata("cause", "correlation", "user"),
                Tags: ["weather:f-1"],
                EventPayloadName: nameof(WeatherForecastCreated))
        };

        Assert.True(accumulator.ApplyState(null));
        Assert.False(accumulator.ApplyEvents(pendingEvents, null));
    }

    /// <summary>
    /// Minimal stub for IProjectionRuntime used only to verify resolver routing.
    /// </summary>
    private sealed class StubProjectionRuntime(string name) : Sekiban.Dcb.Runtime.IProjectionRuntime
    {
        public string Name => name;

        public ResultBoxes.ResultBox<Sekiban.Dcb.Runtime.IProjectionState> GenerateInitialState(string projectorName) =>
            throw new NotImplementedException();

        public ResultBoxes.ResultBox<string> GetProjectorVersion(string projectorName) =>
            throw new NotImplementedException();

        public IReadOnlyList<string> GetAllProjectorNames() => [];

        public ResultBoxes.ResultBox<Sekiban.Dcb.Runtime.IProjectionState> ApplyEvent(
            string projectorName,
            Sekiban.Dcb.Runtime.IProjectionState currentState,
            Sekiban.Dcb.Events.Event ev,
            string safeWindowThreshold) =>
            throw new NotImplementedException();

        public ResultBoxes.ResultBox<Sekiban.Dcb.Runtime.IProjectionState> ApplyEvents(
            string projectorName,
            Sekiban.Dcb.Runtime.IProjectionState currentState,
            IReadOnlyList<Sekiban.Dcb.Events.Event> events,
            string safeWindowThreshold) =>
            throw new NotImplementedException();

        public Task<ResultBoxes.ResultBox<Sekiban.Dcb.Orleans.Serialization.SerializableQueryResult>> ExecuteQueryAsync(
            string projectorName,
            Sekiban.Dcb.Runtime.IProjectionState state,
            Sekiban.Dcb.Orleans.Serialization.SerializableQueryParameter query,
            IServiceProvider serviceProvider) =>
            throw new NotImplementedException();

        public Task<ResultBoxes.ResultBox<Sekiban.Dcb.Orleans.Serialization.SerializableListQueryResult>> ExecuteListQueryAsync(
            string projectorName,
            Sekiban.Dcb.Runtime.IProjectionState state,
            Sekiban.Dcb.Orleans.Serialization.SerializableQueryParameter query,
            IServiceProvider serviceProvider) =>
            throw new NotImplementedException();

        public ResultBoxes.ResultBox<byte[]> SerializeState(
            string projectorName,
            Sekiban.Dcb.Runtime.IProjectionState state) =>
            throw new NotImplementedException();

        public ResultBoxes.ResultBox<Sekiban.Dcb.Runtime.IProjectionState> DeserializeState(
            string projectorName,
            byte[] data,
            string safeWindowThreshold) =>
            throw new NotImplementedException();

        public ResultBoxes.ResultBox<string> ResolveProjectorName(Sekiban.Dcb.Queries.IQueryCommon query) =>
            throw new NotImplementedException();

        public ResultBoxes.ResultBox<string> ResolveProjectorName(Sekiban.Dcb.Queries.IListQueryCommon query) =>
            throw new NotImplementedException();
    }

    private sealed class StubPrimitiveProjectionHost : IPrimitiveProjectionHost
    {
        public IPrimitiveProjectionInstance CreateInstance(string projectorName)
        {
            return new StubPrimitiveProjectionInstance();
        }
    }

    private sealed class FailingPrimitiveProjectionHost : IPrimitiveProjectionHost
    {
        public IPrimitiveProjectionInstance CreateInstance(string projectorName) =>
            throw new InvalidOperationException($"Failed to create '{projectorName}'.");
    }

    private sealed class StubPrimitiveProjectionInstance : IPrimitiveProjectionInstance
    {
        public void ApplyEvent(
            string eventType,
            string eventPayloadJson,
            IReadOnlyList<string> tags,
            string? sortableUniqueId) { }

        public string ExecuteQuery(string queryType, string queryParamsJson) => "{}";
        public string ExecuteListQuery(string queryType, string queryParamsJson) => "[]";
        public string SerializeState() => "{}";
        public void RestoreState(string stateJson) { }
        public void Dispose() { }
    }

    private sealed class StubTagStateProjectionPrimitive : ITagStateProjectionPrimitive
    {
        public ITagStateProjectionAccumulator CreateAccumulator(Sekiban.Dcb.Tags.TagStateId tagStateId) =>
            throw new NotImplementedException();
    }
}
