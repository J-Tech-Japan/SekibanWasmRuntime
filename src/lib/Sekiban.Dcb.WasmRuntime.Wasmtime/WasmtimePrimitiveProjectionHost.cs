using Sekiban.Dcb.Primitives;
using global::Wasmtime;

namespace Sekiban.Dcb.WasmRuntime.Wasmtime;

public sealed class WasmtimePrimitiveProjectionHost : IPrimitiveProjectionHost
{
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
        var modulePath = _options.ResolveModulePath(projectorName);
        if (string.IsNullOrWhiteSpace(modulePath))
        {
            throw new InvalidOperationException(
                $"No WASM module path configured for projector '{projectorName}'.");
        }

        var module = _moduleCache.GetOrLoad(modulePath);
        var store = new Store(_runtime.Engine);
        store.SetWasiConfiguration(new WasiConfiguration()
            .WithInheritedStandardOutput()
            .WithInheritedStandardError());

        var instance = _runtime.Linker.Instantiate(store, module);
        return new WasmtimePrimitiveProjectionInstance(store, instance, projectorName);
    }
}
