using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Sekiban.Dcb.WasmRuntime.Host;
using Sekiban.Dcb.WasmRuntime.Wasmtime;
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

    [Fact]
    public void ManifestPathResolver_Resolve_ShouldThrowForMissingExplicitManifestPath()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sekiban:ManifestPath"] = "./missing-manifest.json"
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => ManifestPathResolver.Resolve(configuration));
        Assert.Contains("missing-manifest.json", ex.Message);
    }

    [Fact]
    public void ManifestPathResolver_Resolve_ShouldUseOnlyCurrentDirectoryManifestByDefault()
    {
        var originalCurrentDirectory = Directory.GetCurrentDirectory();
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "sekiban-runtime-manifest-resolver-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            Directory.SetCurrentDirectory(tempDirectory);
            var expectedPath = Path.Combine(Directory.GetCurrentDirectory(), "sekiban-manifest.json");

            var resolvedPath = ManifestPathResolver.Resolve(new ConfigurationBuilder().Build());

            Assert.Equal(expectedPath, resolvedPath);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCurrentDirectory);
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void SekibanRuntimeManifest_Load_ShouldUseConfiguredWasmModuleWhenManifestIsMissing()
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "sekiban-runtime-default-manifest-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var modulePath = Path.Combine(tempDirectory, "weather.wasm");
            File.WriteAllBytes(modulePath, []);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["WASM_MODULE_PATH"] = modulePath
                })
                .Build();

            var manifest = SekibanRuntimeManifest.Load(
                configuration,
                Path.Combine(tempDirectory, "missing-manifest.json"));

            Assert.Equal(modulePath, manifest.DefaultModulePath);
            Assert.All(manifest.Projectors, projector => Assert.Equal(modulePath, projector.ModulePath));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ProjectionInstanceStore_ShouldEvictIdleInstances()
    {
        var currentTime = new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
        var instance = new StubProjectionInstance();
        using var store = new ProjectionInstanceStore(
            TimeSpan.FromMinutes(5),
            () => currentTime);

        var instanceId = store.Add(instance);
        Assert.True(store.TryGet(instanceId, out var resolvedInstance));
        Assert.Same(instance, resolvedInstance);

        currentTime = currentTime.AddMinutes(6);

        Assert.False(store.TryGet(instanceId, out _));
        Assert.True(instance.Disposed);
    }

    [Fact]
    public void ProjectionInstanceStore_Remove_ShouldDisposeInstance()
    {
        var instance = new StubProjectionInstance();
        using var store = new ProjectionInstanceStore(TimeSpan.FromMinutes(5));
        var instanceId = store.Add(instance);

        Assert.True(store.Remove(instanceId));
        Assert.True(instance.Disposed);
        Assert.False(store.Remove(instanceId));
    }

    [Fact]
    public async Task InstanceEndpoints_CreateInstance_ShouldReturnBadRequestForInvalidProjector()
    {
        using var store = new ProjectionInstanceStore(TimeSpan.FromMinutes(5));
        var result = InstanceEndpoints.CreateInstanceResult(
            new CreateInstanceRequest("MissingProjector"),
            new ThrowingProjectionHost(new InvalidOperationException("Projector 'MissingProjector' is not registered.")),
            store);

        var (statusCode, body) = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status400BadRequest, statusCode);
        Assert.Contains("MissingProjector", body);
    }

    [Fact]
    public async Task InstanceEndpoints_CreateInstance_ShouldReturnNotFoundForMissingProjectorCatalogEntry()
    {
        using var store = new ProjectionInstanceStore(TimeSpan.FromMinutes(5));
        var result = InstanceEndpoints.CreateInstanceResult(
            new CreateInstanceRequest("MissingProjector"),
            new ThrowingProjectionHost(new KeyNotFoundException("Projector 'MissingProjector' was not found.")),
            store);

        var (statusCode, body) = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status404NotFound, statusCode);
        Assert.Contains("MissingProjector", body);
    }

    [Fact]
    public void WasmtimePrimitiveProjectionHost_ShouldReuseCoreInstanceAfterDispose()
    {
        var options = new WasmtimeHostOptions
        {
            DefaultModulePath = GetWeatherModulePath(),
            EnableInstancePooling = true,
            MaxPooledInstancesPerProjector = 1
        };

        using var runtime = new WasmtimeRuntime();
        var moduleCache = new WasmtimeModuleCache(runtime);
        using var host = new WasmtimePrimitiveProjectionHost(runtime, moduleCache, options);

        var first = host.CreateInstance("WeatherForecastProjector");
        WasmtimePrimitiveProjectionInstance firstCore = GetLeaseCore(first);
        string initialState = first.SerializeState();
        first.Dispose();

        var second = host.CreateInstance("WeatherForecastProjector");
        WasmtimePrimitiveProjectionInstance secondCore = GetLeaseCore(second);
        Assert.Same(firstCore, secondCore);
        Assert.Equal(initialState, second.SerializeState());
        second.Dispose();
    }

    private static async Task<(int StatusCode, string Body)> ExecuteResultAsync(IResult result)
    {
        using var app = WebApplication.CreateBuilder().Build();
        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = app.Services;
        httpContext.Response.Body = new MemoryStream();

        await result.ExecuteAsync(httpContext);

        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Response.Body);
        return (httpContext.Response.StatusCode, await reader.ReadToEndAsync());
    }

    private static string GetWeatherModulePath()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            string[] candidates =
            [
                Path.Combine(current.FullName, "src", "internalUsages", "rust", "modules", "rust-weather.wasm"),
                Path.Combine(current.FullName, "internalUsages", "rust", "modules", "rust-weather.wasm"),
                Path.Combine(current.FullName, "modules", "rust-weather.wasm"),
            ];

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            current = current.Parent;
        }

        Assert.Fail($"Test WASM module not found under {AppContext.BaseDirectory}");
        return string.Empty;
    }

    private static WasmtimePrimitiveProjectionInstance GetLeaseCore(Sekiban.Dcb.Primitives.IPrimitiveProjectionInstance instance)
    {
        var field = instance.GetType().GetField("_inner", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<WasmtimePrimitiveProjectionInstance>(field!.GetValue(instance));
    }

    private sealed class StubProjectionInstance : Sekiban.Dcb.Primitives.IPrimitiveProjectionInstance
    {
        public bool Disposed { get; private set; }

        public void ApplyEvent(
            string eventType,
            string eventPayloadJson,
            IReadOnlyList<string> tags,
            string? sortableUniqueId)
        {
        }

        public string ExecuteQuery(string queryType, string queryParamsJson) => "{}";
        public string ExecuteListQuery(string queryType, string queryParamsJson) => "[]";
        public string SerializeState() => "{}";
        public void RestoreState(string stateJson) { }
        public void Dispose() => Disposed = true;
    }

    private sealed class ThrowingProjectionHost(Exception exception) : Sekiban.Dcb.Primitives.IPrimitiveProjectionHost
    {
        public Sekiban.Dcb.Primitives.IPrimitiveProjectionInstance CreateInstance(string projectorName)
            => throw exception;
    }
}
