using Sekiban.Dcb.WasmRuntime;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class ManifestMultiProjectorTypesTests
{
    [Fact]
    public void Deserialize_ShouldWrapGuestJsonForQueryMappedProjector()
    {
        var types = new ManifestMultiProjectorTypes([
            ("WeatherForecastMultiProjection", "1.0.0")
        ]);

        var result = types.Deserialize(
            "WeatherForecastMultiProjection",
            domainTypes: null!,
            safeWindowThreshold: "20260316010101000000000000000000",
            data: """{"items":[],"totalCount":0}"""u8.ToArray());

        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<WasmProjectionPayload>(result.GetValue());
        Assert.Equal("WeatherForecastMultiProjection", payload.ProjectorName);
        Assert.Contains("totalCount", payload.StateJson);
    }

    [Fact]
    public void Deserialize_ShouldFailForUnregisteredProjector()
    {
        var types = new ManifestMultiProjectorTypes([
            ("WeatherForecastMultiProjection", "1.0.0")
        ]);

        var result = types.Deserialize(
            "MissingProjection",
            domainTypes: null!,
            safeWindowThreshold: "20260316010101000000000000000000",
            data: "{}"u8.ToArray());

        Assert.False(result.IsSuccess);
        Assert.Contains("Projector not found", result.GetException().Message);
    }
}
