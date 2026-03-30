using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.WasmRuntime;
using Sekiban.Dcb.WasmRuntime.Wasmtime;
using Xunit;

namespace SekibanWasm.Cs.Tests;

public sealed class WasmtimeProjectionWarmupServiceTests
{
    [Fact]
    public void AddWasmtimeProjectionHost_Registers_WarmupService()
    {
        var services = new ServiceCollection();

        services.AddWasmtimeProjectionHost(_ => { });

        var descriptor = Assert.Single(
            services,
            static service => service.ServiceType == typeof(IHostedService)
                && service.ImplementationType == typeof(WasmtimeProjectionWarmupService));

        Assert.Equal(typeof(WasmtimeProjectionWarmupService), descriptor.ImplementationType);
    }

    [Fact]
    public async Task StartAsync_WarmsUp_And_Disposes_All_Registered_Projectors()
    {
        var registry = new WasmProjectorRegistry();
        registry.Register(new WasmModuleRef(
            ProjectorName: "WeatherForecastProjector",
            ModulePath: "/tmp/weather.wasm",
            AbiKind: "wasi-preview1",
            ModuleVersion: "1.0.0",
            ProjectorVersion: "v1"));
        registry.Register(new WasmModuleRef(
            ProjectorName: "WeatherForecastMultiProjection",
            ModulePath: "/tmp/weather.wasm",
            AbiKind: "wasi-preview1",
            ModuleVersion: "1.0.0",
            ProjectorVersion: "v1"));

        var projectionHost = new RecordingPrimitiveProjectionHost();
        var services = new ServiceCollection()
            .AddSingleton(registry)
            .AddSingleton<IPrimitiveProjectionHost>(projectionHost)
            .BuildServiceProvider();

        var service = new WasmtimeProjectionWarmupService(
            services,
            NullLogger<WasmtimeProjectionWarmupService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => projectionHost.DisposeCount == 2);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(
            ["WeatherForecastProjector", "WeatherForecastMultiProjection"],
            projectionHost.CreatedProjectors);
        Assert.Equal(2, projectionHost.DisposeCount);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < TimeSpan.FromSeconds(1))
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was not reached in time.");
    }

    private sealed class RecordingPrimitiveProjectionHost : IPrimitiveProjectionHost
    {
        public List<string> CreatedProjectors { get; } = [];
        public int DisposeCount { get; private set; }

        public IPrimitiveProjectionInstance CreateInstance(string projectorName)
        {
            CreatedProjectors.Add(projectorName);
            return new RecordingPrimitiveProjectionInstance(() => DisposeCount++);
        }
    }

    private sealed class RecordingPrimitiveProjectionInstance(Action onDispose) : IPrimitiveProjectionInstance
    {
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

        public void RestoreState(string stateJson)
        {
        }

        public void Dispose() => onDispose();
    }
}
