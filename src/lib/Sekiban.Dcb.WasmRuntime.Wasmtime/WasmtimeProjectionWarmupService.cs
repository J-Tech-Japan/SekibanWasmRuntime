using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.WasmRuntime;

namespace Sekiban.Dcb.WasmRuntime.Wasmtime;

public sealed class WasmtimeProjectionWarmupService(
    IServiceProvider services,
    ILogger<WasmtimeProjectionWarmupService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var registry = services.GetService<WasmProjectorRegistry>();
        if (registry is null)
        {
            return Task.CompletedTask;
        }

        var projectorNames = registry.GetAllProjectorNames()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (projectorNames.Length == 0)
        {
            return Task.CompletedTask;
        }

        var host = services.GetRequiredService<IPrimitiveProjectionHost>();
        foreach (var projectorName in projectorNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Component extraction can be too expensive for the first Orleans request.
                logger.LogInformation("Warming up Wasmtime projector {ProjectorName}", projectorName);
                using var instance = host.CreateInstance(projectorName);
                _ = instance.SerializeState();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to warm up Wasmtime projector {ProjectorName}", projectorName);
                throw;
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
