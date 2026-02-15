using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.Runtime;

namespace Sekiban.Dcb.WasmRuntime;

public static class WasmRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddWasmTagStateRuntime(
        this IServiceCollection services,
        Action<WasmTagStateOptions> configureOptions)
    {
        var options = new WasmTagStateOptions();
        configureOptions(options);
        options.Validate();

        services.AddSingleton(options);

        switch (options.Mode)
        {
            case WasmRuntimeMode.Native:
                break;

            case WasmRuntimeMode.Wasm:
                services.AddSingleton<IProjectionRuntime, WasmProjectionRuntime>();
                break;

            case WasmRuntimeMode.Hybrid:
                services.AddSingleton<IProjectionRuntime, CompositeProjectionRuntime>();
                break;

            case WasmRuntimeMode.Remote:
                break;
        }

        return services;
    }
}
