using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.Runtime;
using System.Text.Json;
using System.Linq;

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
                EnsureWasmRuntimeDependencies(services);
                services.TryAddSingleton<IProjectionRuntime, WasmProjectionRuntime>();
                break;

            case WasmRuntimeMode.Hybrid:
                EnsureWasmRuntimeDependencies(services);
                EnsureRegistered<IProjectorRuntimeResolver>(services);
                services.TryAddSingleton<IProjectionRuntime, CompositeProjectionRuntime>();
                break;

            case WasmRuntimeMode.Remote:
                break;
        }

        return services;
    }

    private static void EnsureWasmRuntimeDependencies(IServiceCollection services)
    {
        EnsureRegistered<IPrimitiveProjectionHost>(services);
        EnsureRegistered<WasmProjectorRegistry>(services);
        EnsureRegistered<JsonSerializerOptions>(services);
    }

    private static void EnsureRegistered<TService>(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(TService)))
        {
            return;
        }

        throw new InvalidOperationException(
            $"{typeof(TService).Name} must be registered before calling AddWasmTagStateRuntime for this mode.");
    }
}
