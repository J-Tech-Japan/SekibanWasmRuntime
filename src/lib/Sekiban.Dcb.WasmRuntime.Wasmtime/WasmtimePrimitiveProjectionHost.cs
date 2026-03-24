using Sekiban.Dcb.Primitives;
using global::Wasmtime;

namespace Sekiban.Dcb.WasmRuntime.Wasmtime;

public sealed class WasmtimePrimitiveProjectionHost : IPrimitiveProjectionHost
{
    private static readonly bool TraceLifecycle =
        string.Equals(
            Environment.GetEnvironmentVariable("WASM_RUNTIME_TRACE_LIFECYCLE"),
            "1",
            StringComparison.Ordinal);

    private readonly WasmtimeRuntime _runtime;
    private readonly WasmtimeModuleCache _moduleCache;
    private readonly WasmtimeHostOptions _options;

    public WasmtimePrimitiveProjectionHost(
        WasmtimeRuntime runtime,
        WasmtimeModuleCache moduleCache,
        WasmtimeHostOptions options)
    {
        _runtime = runtime;
        _moduleCache = moduleCache;
        _options = options;
    }

    public IPrimitiveProjectionInstance CreateInstance(string projectorName)
    {
        Trace($"host:create_instance:start projector={projectorName}");
        var modulePath = _options.ResolveModulePath(projectorName);
        if (string.IsNullOrWhiteSpace(modulePath))
        {
            throw new InvalidOperationException(
                $"No WASM module path configured for projector '{projectorName}'.");
        }

        var module = _moduleCache.GetOrLoad(modulePath);
        Trace($"host:create_instance:module_loaded projector={projectorName} path={modulePath}");
        var store = new Store(_runtime.Engine);
        store.SetWasiConfiguration(new WasiConfiguration()
            .WithInheritedStandardOutput()
            .WithInheritedStandardError());

        using var linker = _runtime.CreateLinker();
        Trace($"host:create_instance:before_instantiate projector={projectorName}");
        var instance = linker.Instantiate(store, module);
        Trace($"host:create_instance:after_instantiate projector={projectorName}");
        return new WasmtimePrimitiveProjectionInstance(store, instance, projectorName, new object());
    }

    private static void Trace(string message)
    {
        if (!TraceLifecycle)
        {
            return;
        }

        Console.WriteLine($"[wasmtime-trace] {message}");
    }
}
