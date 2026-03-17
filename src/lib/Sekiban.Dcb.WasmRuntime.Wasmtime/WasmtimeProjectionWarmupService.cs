using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.WasmRuntime;

namespace Sekiban.Dcb.WasmRuntime.Wasmtime;

public sealed class WasmtimeProjectionWarmupService(
    IServiceProvider services,
    ILogger<WasmtimeProjectionWarmupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var registry = services.GetService<WasmProjectorRegistry>();
        if (registry is null)
        {
            return;
        }

        var projectorNames = registry.GetAllProjectorNames()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (projectorNames.Length == 0)
        {
            return;
        }

        // Do not block API readiness on Wasmtime component extraction/shim initialization.
        await Task.Yield();

        IPrimitiveProjectionHost host;
        try
        {
            host = services.GetRequiredService<IPrimitiveProjectionHost>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize Wasmtime projection host for warmup");
            return;
        }

        foreach (var projectorName in projectorNames)
        {
            stoppingToken.ThrowIfCancellationRequested();

            try
            {
                logger.LogInformation("Warming up Wasmtime projector {ProjectorName}", projectorName);
                using var instance = host.CreateInstance(projectorName);
                _ = instance.SerializeState();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to warm up Wasmtime projector {ProjectorName}", projectorName);
                return;
            }
        }
    }
}
