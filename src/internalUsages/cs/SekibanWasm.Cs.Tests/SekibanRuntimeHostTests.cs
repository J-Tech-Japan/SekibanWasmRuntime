using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Sekiban.Dcb.WasmRuntime.Host;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public class SekibanRuntimeHostTests
{
    [Fact]
    public void DynamicJsonEventTypes_ShouldPreserveRawJsonPayload()
    {
        var options = ManifestDomainTypes.CreateJsonOptions();
        var eventTypes = new DynamicJsonEventTypes(
            ["WeatherForecastCreated"],
            options);

        var payload = eventTypes.DeserializeEventPayload(
            "WeatherForecastCreated",
            "{\"forecastId\":\"f-1\",\"temperatureC\":25}");

        Assert.NotNull(payload);

        var serialized = eventTypes.SerializeEventPayload(payload!);
        using var document = JsonDocument.Parse(serialized);
        Assert.Equal("f-1", document.RootElement.GetProperty("forecastId").GetString());
        Assert.Equal(25, document.RootElement.GetProperty("temperatureC").GetInt32());
    }

    [Fact]
    public void SekibanRuntimeManifest_Load_ShouldResolveRelativeModulePaths()
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "sekiban-runtime-host-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var modulePath = Path.Combine(tempDirectory, "weather.wasm");
            File.WriteAllBytes(modulePath, []);

            var manifestPath = Path.Combine(tempDirectory, "sekiban-manifest.json");
            File.WriteAllText(
                manifestPath,
                """
                {
                  "defaultModulePath": "./weather.wasm",
                  "eventTypes": ["WeatherForecastCreated"],
                  "projectors": [
                    {
                      "projectorName": "WeatherForecastProjector",
                      "modulePath": "./weather.wasm"
                    }
                  ],
                  "queryProjectors": {
                    "GetWeatherForecastListQuery": "WeatherForecastProjector"
                  }
                }
                """);

            var manifest = SekibanRuntimeManifest.Load(
                new ConfigurationBuilder().Build(),
                manifestPath);

            Assert.Equal(modulePath, manifest.DefaultModulePath);
            Assert.Single(manifest.Projectors);
            Assert.Equal(modulePath, manifest.Projectors[0].ModulePath);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
