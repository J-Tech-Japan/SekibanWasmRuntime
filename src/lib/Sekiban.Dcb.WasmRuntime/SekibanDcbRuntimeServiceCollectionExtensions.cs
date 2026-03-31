using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Runtime.Native;
using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.WasmRuntime;

public static class SekibanDcbRuntimeServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the shared infrastructure services needed by ALL runtime modes
    ///     (native, WASM, hybrid): domain type interfaces extracted from DcbDomainTypes
    ///     and snapshot temp-file infrastructure.
    ///     Call this after DcbDomainTypes has been registered in the container.
    /// </summary>
    public static IServiceCollection AddSekibanDcbSharedRuntime(this IServiceCollection services)
    {
        services.TryAddSingleton<ITagProjectorTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().TagProjectorTypes);
        services.TryAddSingleton<ITagTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().TagTypes);
        services.TryAddSingleton<IEventTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().EventTypes);
        services.TryAddSingleton<ITagStatePayloadTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().TagStatePayloadTypes);

        services.TryAddSingleton<SnapshotTempFileOptions>();
        services.TryAddSingleton<TempFileSnapshotManager>();

        return services;
    }

    /// <summary>
    ///     Registers the full native runtime: shared infrastructure plus native-specific
    ///     projection implementations (NativeProjectionActorHostFactory,
    ///     NativeTagStateProjectionPrimitive, NativeMultiProjectionProjectionPrimitive).
    ///     Use this for services that run projections natively in-process.
    ///     For pure WASM mode, call <see cref="AddSekibanDcbSharedRuntime"/> instead.
    /// </summary>
    public static IServiceCollection AddSekibanDcbFullNativeRuntime(this IServiceCollection services)
    {
        services.AddSingleton<IProjectionActorHostFactory, NativeProjectionActorHostFactory>();
        services.AddSingleton<ITagStateProjectionPrimitive, NativeTagStateProjectionPrimitive>();
        services.AddSingleton<NativeMultiProjectionProjectionPrimitive>();
        services.AddSingleton<IMultiProjectionProjectionPrimitive>(sp =>
            sp.GetRequiredService<NativeMultiProjectionProjectionPrimitive>());

        services.AddSekibanDcbSharedRuntime();

        return services;
    }
}
