using Sekiban.Dcb.WasmRuntime.Wasmtime;
using Xunit;

namespace SekibanWasm.Tests;

public class WasmtimeHostOptionsTests
{
    [Fact]
    public void ResolveModulePath_ShouldReturnPerProjectorPath()
    {
        // Given
        var options = new WasmtimeHostOptions
        {
            DefaultModulePath = "/default.wasm",
            ProjectorModulePaths = { ["Weather"] = "/weather.wasm" }
        };

        // When
        var path = options.ResolveModulePath("Weather");

        // Then
        Assert.Equal("/weather.wasm", path);
    }

    [Fact]
    public void ResolveModulePath_ShouldFallbackToDefault()
    {
        // Given
        var options = new WasmtimeHostOptions
        {
            DefaultModulePath = "/default.wasm"
        };

        // When
        var path = options.ResolveModulePath("Unknown");

        // Then
        Assert.Equal("/default.wasm", path);
    }

    [Fact]
    public void ResolveModulePath_ShouldReturnNullWhenNoDefault()
    {
        // Given
        var options = new WasmtimeHostOptions();

        // When
        var path = options.ResolveModulePath("Unknown");

        // Then
        Assert.Null(path);
    }
}
