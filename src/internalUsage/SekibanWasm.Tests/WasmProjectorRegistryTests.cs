using Sekiban.Dcb.WasmRuntime;
using Xunit;

namespace SekibanWasm.Tests;

public class WasmProjectorRegistryTests
{
    [Fact]
    public void Register_ShouldStoreModuleRef()
    {
        // Given
        var registry = new WasmProjectorRegistry();
        var moduleRef = new WasmModuleRef(
            "WeatherForecastProjector",
            "/path/to/module.wasm",
            "c-abi",
            "1.0.0",
            "v1");

        // When
        registry.Register(moduleRef);

        // Then
        var retrieved = registry.TryGet("WeatherForecastProjector");
        Assert.NotNull(retrieved);
        Assert.Equal("WeatherForecastProjector", retrieved.ProjectorName);
        Assert.Equal("/path/to/module.wasm", retrieved.ModulePath);
        Assert.Equal("v1", retrieved.ProjectorVersion);
    }

    [Fact]
    public void TryGet_ShouldReturnNullForUnregistered()
    {
        // Given
        var registry = new WasmProjectorRegistry();

        // When
        var result = registry.TryGet("NonExistent");

        // Then
        Assert.Null(result);
    }

    [Fact]
    public void GetAllProjectorNames_ShouldReturnRegisteredNames()
    {
        // Given
        var registry = new WasmProjectorRegistry();
        registry.Register(new WasmModuleRef("Projector1", "/p1.wasm", "c-abi", "1.0", "v1"));
        registry.Register(new WasmModuleRef("Projector2", "/p2.wasm", "c-abi", "1.0", "v1"));

        // When
        var names = registry.GetAllProjectorNames();

        // Then
        Assert.Equal(2, names.Count);
        Assert.Contains("Projector1", names);
        Assert.Contains("Projector2", names);
    }

    [Fact]
    public void MapQueryToProjector_ShouldResolveCorrectly()
    {
        // Given
        var registry = new WasmProjectorRegistry();
        registry.Register(new WasmModuleRef("WeatherProjector", "/w.wasm", "c-abi", "1.0", "v1"));
        registry.MapQueryToProjector("GetWeatherForecastListQuery", "WeatherProjector");

        // When
        var resolved = registry.ResolveProjectorForQuery("GetWeatherForecastListQuery");

        // Then
        Assert.Equal("WeatherProjector", resolved);
    }

    [Fact]
    public void ResolveProjectorForQuery_ShouldReturnNullForUnmapped()
    {
        // Given
        var registry = new WasmProjectorRegistry();

        // When
        var result = registry.ResolveProjectorForQuery("UnmappedQuery");

        // Then
        Assert.Null(result);
    }

    [Fact]
    public void Register_ShouldOverwriteExisting()
    {
        // Given
        var registry = new WasmProjectorRegistry();
        registry.Register(new WasmModuleRef("Projector", "/old.wasm", "c-abi", "1.0", "v1"));

        // When
        registry.Register(new WasmModuleRef("Projector", "/new.wasm", "c-abi", "2.0", "v2"));

        // Then
        var retrieved = registry.TryGet("Projector");
        Assert.NotNull(retrieved);
        Assert.Equal("/new.wasm", retrieved.ModulePath);
        Assert.Equal("v2", retrieved.ProjectorVersion);
    }
}
