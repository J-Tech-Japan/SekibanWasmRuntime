using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.Primitives;

namespace Sekiban.Dcb.WasmRuntime.Wasmtime;

public static class WasmtimeServiceCollectionExtensions
{
    public static IServiceCollection AddWasmtimeProjectionHost(
        this IServiceCollection services,
        Action<WasmtimeHostOptions> configureOptions)
    {
        var options = new WasmtimeHostOptions();
        configureOptions(options);

        services.AddSingleton(options);
        services.AddSingleton<WasmtimeRuntime>();
        services.AddSingleton<WasmtimeModuleCache>();
        services.AddSingleton<IPrimitiveProjectionHost, WasmtimePrimitiveProjectionHost>();
        services.AddHostedService<WasmtimeProjectionWarmupService>();

        return services;
    }
}
