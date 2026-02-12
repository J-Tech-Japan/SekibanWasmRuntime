using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Wasmtime;
using SekibanWasm.Domain;
using Xunit;

namespace SekibanWasm.Tests;

/// <summary>
/// Tests verifying the hybrid runtime DI wiring pattern that uses
/// factory delegates instead of BuildServiceProvider().
/// </summary>
public class HybridRuntimeWiringTests
{
    private const string TestModulePath = "/test/artifacts/wasm/sekibanwasm.wasm";

    [Fact]
    public void HybridWiring_ShouldResolveCompositeProjectionRuntime()
    {
        // Given
        var services = new ServiceCollection();

        // When
        WireHybridProjectionRuntime(services, TestModulePath);

        // Then
        var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IProjectionRuntime>();
        Assert.IsType<CompositeProjectionRuntime>(runtime);
    }

    [Fact]
    public void HybridWiring_ShouldResolveResolverWithNativeDefault()
    {
        // Given
        var services = new ServiceCollection();

        // When
        WireHybridProjectionRuntime(services, TestModulePath);

        // Then
        var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<Sekiban.Dcb.WasmRuntime.IProjectorRuntimeResolver>();

        var nativeResult = resolver.Resolve("SomeNativeProjector");
        Assert.IsType<FakeNativeRuntime>(nativeResult);
    }

    [Fact]
    public void HybridWiring_ShouldRouteWasmProjectorToWasmRuntime()
    {
        // Given
        var services = new ServiceCollection();

        // When
        WireHybridProjectionRuntime(services, TestModulePath);

        // Then
        var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<Sekiban.Dcb.WasmRuntime.IProjectorRuntimeResolver>();

        var wasmResult = resolver.Resolve("WeatherForecastMultiProjection");
        Assert.IsType<WasmProjectionRuntime>(wasmResult);
    }

    [Fact]
    public void HybridWiring_ShouldReturnBothRuntimes_FromGetAllRuntimes()
    {
        // Given
        var services = new ServiceCollection();

        // When
        WireHybridProjectionRuntime(services, TestModulePath);

        // Then
        var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<Sekiban.Dcb.WasmRuntime.IProjectorRuntimeResolver>();
        var allRuntimes = resolver.GetAllRuntimes();

        Assert.Equal(2, allRuntimes.Count);
        Assert.Single(allRuntimes, r => r is FakeNativeRuntime);
        Assert.Single(allRuntimes, r => r is WasmProjectionRuntime);
    }

    [Fact]
    public void HybridWiring_ShouldRegisterRegistryWithQueryMappings()
    {
        // Given
        var services = new ServiceCollection();

        // When
        WireHybridProjectionRuntime(services, TestModulePath);

        // Then
        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<WasmProjectorRegistry>();
        Assert.Equal(
            "WeatherForecastMultiProjection",
            registry.ResolveProjectorForQuery("GetWeatherForecastCountQuery"));
    }

    /// <summary>
    /// Replicates the hybrid wiring logic from Program.cs using the factory delegate
    /// pattern (no BuildServiceProvider).
    /// </summary>
    private static void WireHybridProjectionRuntime(IServiceCollection services, string wasmModulePath)
    {
        // Simulate AddSekibanDcbNativeRuntime() registering IProjectionRuntime
        // Also register as concrete type so the resolver factory can resolve it directly
        // without triggering circular dependency via GetServices<IProjectionRuntime>().
        services.AddSingleton<FakeNativeRuntime>();
        services.AddSingleton<IProjectionRuntime>(sp => sp.GetRequiredService<FakeNativeRuntime>());

        // Hybrid wiring (same pattern as Program.cs)
        var registry = new WasmProjectorRegistry();
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
        services.AddSingleton<IPrimitiveProjectionHost>(new FakePrimitiveProjectionHost());
        services.AddSingleton(new WasmtimeHostOptions { DefaultModulePath = wasmModulePath });
        services.AddSingleton<JsonSerializerOptions>(_ => DomainJsonContext.Default.Options);

        services.AddSingleton<WasmProjectionRuntime>();
        services.AddSingleton<Sekiban.Dcb.WasmRuntime.IProjectorRuntimeResolver>(sp =>
        {
            // Resolve concrete types directly to avoid circular dependency:
            // CompositeProjectionRuntime → IProjectorRuntimeResolver → GetServices<IProjectionRuntime>
            //   → CompositeProjectionRuntime (deadlock!)
            return new ProjectorRuntimeResolver(
                defaultRuntime: sp.GetRequiredService<FakeNativeRuntime>(),
                runtimeMap: new Dictionary<string, IProjectionRuntime>
                {
                    ["WeatherForecastMultiProjection"] = sp.GetRequiredService<WasmProjectionRuntime>()
                });
        });

        services.AddSingleton<IProjectionRuntime, CompositeProjectionRuntime>();
    }

    /// <summary>
    /// Simulates the native runtime registered by AddSekibanDcbNativeRuntime().
    /// </summary>
    internal sealed class FakeNativeRuntime : IProjectionRuntime
    {
        public ResultBox<IProjectionState> GenerateInitialState(string projectorName) => throw new NotImplementedException();
        public ResultBox<string> GetProjectorVersion(string projectorName) => throw new NotImplementedException();
        public IReadOnlyList<string> GetAllProjectorNames() => [];
        public ResultBox<IProjectionState> ApplyEvent(string projectorName, IProjectionState currentState, Event ev, string safeWindowThreshold) => throw new NotImplementedException();
        public ResultBox<IProjectionState> ApplyEvents(string projectorName, IProjectionState currentState, IReadOnlyList<Event> events, string safeWindowThreshold) => throw new NotImplementedException();
        public Task<ResultBox<SerializableQueryResult>> ExecuteQueryAsync(string projectorName, IProjectionState state, SerializableQueryParameter query, IServiceProvider serviceProvider) => throw new NotImplementedException();
        public Task<ResultBox<SerializableListQueryResult>> ExecuteListQueryAsync(string projectorName, IProjectionState state, SerializableQueryParameter query, IServiceProvider serviceProvider) => throw new NotImplementedException();
        public ResultBox<byte[]> SerializeState(string projectorName, IProjectionState state) => throw new NotImplementedException();
        public ResultBox<IProjectionState> DeserializeState(string projectorName, byte[] data, string safeWindowThreshold) => throw new NotImplementedException();
        public ResultBox<string> ResolveProjectorName(IQueryCommon query) => throw new NotImplementedException();
        public ResultBox<string> ResolveProjectorName(IListQueryCommon query) => throw new NotImplementedException();
    }
}
