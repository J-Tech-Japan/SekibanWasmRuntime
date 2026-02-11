using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Wasmtime;
using SekibanWasm.Domain;
using Xunit;

namespace SekibanWasm.Tests;

/// <summary>
/// Tests that verify the DI wiring logic used in Program.cs
/// for switching between native and WASM projection runtimes.
/// </summary>
public class WasmRuntimeWiringTests
{
    private const string TestModulePath = "/test/artifacts/wasm/sekibanwasm.wasm";

    [Fact]
    public void WasmWiring_ShouldRegisterWasmProjectionRuntime()
    {
        // Given
        var services = new ServiceCollection();

        // When: apply the same wiring logic as Program.cs WASM block
        WireWasmProjectionRuntime(services, TestModulePath);

        // Then
        var provider = services.BuildServiceProvider();
        var runtime = provider.GetService<IProjectionRuntime>();
        Assert.NotNull(runtime);
        Assert.IsType<WasmProjectionRuntime>(runtime);
    }

    [Fact]
    public void WasmWiring_ShouldRegisterWasmProjectorRegistry_WithBothProjectors()
    {
        // Given
        var services = new ServiceCollection();

        // When
        WireWasmProjectionRuntime(services, TestModulePath);

        // Then
        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<WasmProjectorRegistry>();
        var names = registry.GetAllProjectorNames();
        Assert.Equal(2, names.Count);
        Assert.Contains("WeatherForecastProjector", names);
        Assert.Contains("WeatherForecastMultiProjection", names);
    }

    [Fact]
    public void WasmWiring_ShouldMapQueryTypes_ToMultiProjection()
    {
        // Given
        var services = new ServiceCollection();

        // When
        WireWasmProjectionRuntime(services, TestModulePath);

        // Then
        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<WasmProjectorRegistry>();

        Assert.Equal("WeatherForecastMultiProjection",
            registry.ResolveProjectorForQuery("GetWeatherForecastCountQuery"));
        Assert.Equal("WeatherForecastMultiProjection",
            registry.ResolveProjectorForQuery("GetWeatherForecastListQuery"));
        Assert.Equal("WeatherForecastMultiProjection",
            registry.ResolveProjectorForQuery("WeatherForecastListQuery"));
    }

    [Fact]
    public void WasmWiring_ShouldSetCorrectModulePath_InHostOptions()
    {
        // Given
        var services = new ServiceCollection();

        // When
        WireWasmProjectionRuntime(services, TestModulePath);

        // Then
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<WasmtimeHostOptions>();
        Assert.Equal(TestModulePath, options.DefaultModulePath);
    }

    [Fact]
    public void WasmWiring_ShouldSetCorrectModulePath_InModuleRefs()
    {
        // Given
        var services = new ServiceCollection();

        // When
        WireWasmProjectionRuntime(services, TestModulePath);

        // Then
        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<WasmProjectorRegistry>();

        var tagRef = registry.TryGet("WeatherForecastProjector");
        Assert.NotNull(tagRef);
        Assert.Equal(TestModulePath, tagRef.ModulePath);
        Assert.Equal("wasi-preview1", tagRef.AbiKind);
        Assert.Equal("v1", tagRef.ProjectorVersion);

        var multiRef = registry.TryGet("WeatherForecastMultiProjection");
        Assert.NotNull(multiRef);
        Assert.Equal(TestModulePath, multiRef.ModulePath);
        Assert.Equal("wasi-preview1", multiRef.AbiKind);
        Assert.Equal("1.0.0", multiRef.ProjectorVersion);
    }

    [Fact]
    public void WasmWiring_ShouldRegisterJsonSerializerOptions()
    {
        // Given
        var services = new ServiceCollection();

        // When
        WireWasmProjectionRuntime(services, TestModulePath);

        // Then
        var provider = services.BuildServiceProvider();
        var jsonOptions = provider.GetService<JsonSerializerOptions>();
        Assert.NotNull(jsonOptions);
    }

    [Fact]
    public void WasmWiring_WasmOverridesNative_WhenBothRegistered()
    {
        // Given: register a fake native IProjectionRuntime first
        var services = new ServiceCollection();
        services.AddSingleton<IPrimitiveProjectionHost>(new FakePrimitiveProjectionHost());
        services.AddSingleton<IProjectionRuntime, FakeNativeProjectionRuntime>();

        // When: wire WASM (which also registers IProjectionRuntime)
        WireWasmProjectionRuntime(services, TestModulePath);

        // Then: the last registration wins
        var provider = services.BuildServiceProvider();
        var runtimes = provider.GetServices<IProjectionRuntime>().ToList();
        var lastRuntime = runtimes.Last();
        Assert.IsType<WasmProjectionRuntime>(lastRuntime);
    }

    /// <summary>
    /// Replicates the WASM wiring logic from Program.cs so it can be tested in isolation.
    /// </summary>
    private static void WireWasmProjectionRuntime(IServiceCollection services, string wasmModulePath)
    {
        var registry = new WasmProjectorRegistry();
        registry.Register(new WasmModuleRef(
            ProjectorName: "WeatherForecastProjector",
            ModulePath: wasmModulePath,
            AbiKind: "wasi-preview1",
            ModuleVersion: "1.0.0",
            ProjectorVersion: "v1"));
        registry.Register(new WasmModuleRef(
            ProjectorName: "WeatherForecastMultiProjection",
            ModulePath: wasmModulePath,
            AbiKind: "wasi-preview1",
            ModuleVersion: "1.0.0",
            ProjectorVersion: "1.0.0"));

        registry.MapQueryToProjector("GetWeatherForecastCountQuery", "WeatherForecastMultiProjection");
        registry.MapQueryToProjector("GetWeatherForecastListQuery", "WeatherForecastMultiProjection");
        registry.MapQueryToProjector("WeatherForecastListQuery", "WeatherForecastMultiProjection");

        services.AddSingleton(registry);

        services.AddWasmtimeProjectionHost(opt =>
        {
            opt.DefaultModulePath = wasmModulePath;
        });

        services.AddSingleton<JsonSerializerOptions>(_ => DomainJsonContext.Default.Options);

        services.AddSingleton<IProjectionRuntime, WasmProjectionRuntime>();
    }

    /// <summary>
    /// Minimal fake to represent a native IProjectionRuntime for override testing.
    /// </summary>
    private sealed class FakeNativeProjectionRuntime : IProjectionRuntime
    {
        public ResultBoxes.ResultBox<IProjectionState> GenerateInitialState(string projectorName) => throw new NotImplementedException();
        public ResultBoxes.ResultBox<string> GetProjectorVersion(string projectorName) => throw new NotImplementedException();
        public IReadOnlyList<string> GetAllProjectorNames() => [];
        public ResultBoxes.ResultBox<IProjectionState> ApplyEvent(string projectorName, IProjectionState currentState, Sekiban.Dcb.Events.Event ev, string safeWindowThreshold) => throw new NotImplementedException();
        public ResultBoxes.ResultBox<IProjectionState> ApplyEvents(string projectorName, IProjectionState currentState, IReadOnlyList<Sekiban.Dcb.Events.Event> events, string safeWindowThreshold) => throw new NotImplementedException();
        public Task<ResultBoxes.ResultBox<Sekiban.Dcb.Orleans.Serialization.SerializableQueryResult>> ExecuteQueryAsync(string projectorName, IProjectionState state, Sekiban.Dcb.Orleans.Serialization.SerializableQueryParameter query, IServiceProvider serviceProvider) => throw new NotImplementedException();
        public Task<ResultBoxes.ResultBox<Sekiban.Dcb.Orleans.Serialization.SerializableListQueryResult>> ExecuteListQueryAsync(string projectorName, IProjectionState state, Sekiban.Dcb.Orleans.Serialization.SerializableQueryParameter query, IServiceProvider serviceProvider) => throw new NotImplementedException();
        public ResultBoxes.ResultBox<byte[]> SerializeState(string projectorName, IProjectionState state) => throw new NotImplementedException();
        public ResultBoxes.ResultBox<IProjectionState> DeserializeState(string projectorName, byte[] data, string safeWindowThreshold) => throw new NotImplementedException();
        public ResultBoxes.ResultBox<string> ResolveProjectorName(Sekiban.Dcb.Queries.IQueryCommon query) => throw new NotImplementedException();
        public ResultBoxes.ResultBox<string> ResolveProjectorName(Sekiban.Dcb.Queries.IListQueryCommon query) => throw new NotImplementedException();
    }
}
