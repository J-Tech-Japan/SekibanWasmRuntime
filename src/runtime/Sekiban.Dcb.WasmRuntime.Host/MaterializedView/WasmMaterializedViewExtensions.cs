using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Orleans;
using Sekiban.Dcb.MaterializedView.Postgres;

namespace Sekiban.Dcb.WasmRuntime.Host.MaterializedView;

/// <summary>
///     Shared DI wiring for the WASM materialized view runtime.
///     Consumed by any Sekiban WASM runtime host (C# sample, Rust sample, future language hosts) so
///     the Program.cs call site stays a single line and we avoid copy-pasting the Sekiban MV
///     registration dance.
///
///     <para>Responsibilities:</para>
///     <list type="bullet">
///       <item>Validate per-view <c>ModulePath</c> overrides against the single shared module (the
///         current Wasmtime executor pools one instance for all views).</item>
///       <item>Register <see cref="WasmtimeMaterializedViewExecutor"/> as the WASM MV backend.</item>
///       <item>Register Sekiban's MV base/Postgres/Orleans packages so <see cref="MaterializedViewGrain"/>
///         + catch-up worker drive the apply loop.</item>
///       <item>Swap Sekiban's <c>NativeMvApplyHostFactory</c> for <see cref="WasmMvApplyHostFactory"/>
///         via <c>services.Replace</c>.</item>
///     </list>
///
///     <para>Gating: MV is only wired when the caller supplies at least one registration AND the
///     named connection string is configured. Passing an empty list or a missing connection string
///     turns this into a no-op — matches the original Program.cs behaviour.</para>
/// </summary>
public static class WasmMaterializedViewExtensions
{
    public const string DefaultConnectionStringName = "DcbMaterializedViewPostgres";

    /// <summary>
    ///     Registers the WASM MV runtime for the given <paramref name="registrations"/>.
    /// </summary>
    /// <returns>
    ///     <c>true</c> when the MV runtime was wired (connection string present and at least one
    ///     registration); <c>false</c> when the call was a no-op.
    /// </returns>
    public static bool AddSekibanWasmMaterializedViewRuntime(
        this IServiceCollection services,
        IConfiguration configuration,
        string defaultModulePath,
        IReadOnlyList<WasmMvApplyHostRegistration> registrations,
        string connectionStringName = DefaultConnectionStringName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(registrations);

        if (registrations.Count == 0)
        {
            return false;
        }

        var connectionString = configuration.GetConnectionString(connectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(defaultModulePath))
        {
            throw new InvalidOperationException(
                "WASM materialized view runtime requires a non-empty defaultModulePath.");
        }

        // Dapper's default snake_case → PascalCase mapping is what the MV read endpoints assume,
        // so enable it once globally when MV is active. Safe even if callers also set it.
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        services.AddSingleton(new WasmMaterializedViewRuntimeOptions
        {
            ModulePath = defaultModulePath
        });
        services.AddSingleton<IWasmMaterializedViewExecutor, WasmtimeMaterializedViewExecutor>();

        services.AddSekibanDcbMaterializedView(options =>
        {
            options.BatchSize = 100;
            options.PollInterval = TimeSpan.FromSeconds(1);
        });

        // registerHostedWorker:true enables the MvCatchUpWorker BackgroundService that polls the
        // event store and drives IMvExecutor.CatchUpOnceAsync outside the Orleans grain. The grain
        // still handles stream-driven apply when events are published to the Orleans stream, but
        // the worker guarantees progress even when the Orleans stream has no publisher wired up
        // (which is the current state in the WASM runtime host — commits go through the WASM
        // serialized commit path, not through IEventPublisher → OrleansStream). Both paths
        // coordinate via MvRegistryStore positions so no double-apply occurs.
        services.AddSekibanDcbMaterializedViewPostgres(
            configuration,
            connectionStringName: connectionStringName,
            registerHostedWorker: true);
        services.AddSekibanDcbMaterializedViewOrleans();

        // Replace Sekiban's default NativeMvApplyHostFactory (which looks up CLR
        // IMaterializedViewProjector instances) with WasmMvApplyHostFactory. In WASM mode the
        // projector lives inside the .wasm module and there are no CLR projectors to enumerate;
        // the factory hands out fresh WasmMvApplyHost per (viewName, viewVersion) from the
        // registration list, and each host delegates init/apply through
        // IWasmMaterializedViewExecutor to the WASM exports.
        services.Replace(ServiceDescriptor.Singleton<IMvApplyHostFactory>(sp =>
            new WasmMvApplyHostFactory(
                sp.GetRequiredService<IWasmMaterializedViewExecutor>(),
                registrations)));

        return true;
    }

    /// <summary>
    ///     Validates that per-view <c>ModulePath</c> overrides match the shared default module.
    ///     Kept separate from <see cref="AddSekibanWasmMaterializedViewRuntime"/> so callers that
    ///     carry manifest-level metadata (e.g. <c>SekibanRuntimeManifest</c>) can surface the exact
    ///     field that triggered the mismatch.
    /// </summary>
    public static void ValidateModulePathAlignment(
        string defaultModulePath,
        IEnumerable<(string ViewName, int ViewVersion, string? ModulePath)> views)
    {
        foreach (var (viewName, viewVersion, modulePath) in views)
        {
            if (!string.IsNullOrWhiteSpace(modulePath) &&
                !string.Equals(modulePath, defaultModulePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Materialized view '{viewName}/{viewVersion}' declares ModulePath " +
                    $"'{modulePath}', but the MV Wasmtime executor currently uses a single shared " +
                    $"module ('{defaultModulePath}'). Either omit the per-view ModulePath or " +
                    "extend WasmtimeMaterializedViewExecutor to support per-view modules.");
            }
        }
    }
}
