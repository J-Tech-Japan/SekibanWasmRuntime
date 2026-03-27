using Sekiban.Dcb.Primitives;
using global::Wasmtime;

namespace Sekiban.Dcb.WasmRuntime.Wasmtime;

public sealed class WasmtimePrimitiveProjectionHost : IPrimitiveProjectionHost
{
    private const string DefaultNullDevicePath = "/dev/null";
    private const string DefaultStdoutDevicePath = "/dev/stdout";
    private const string DefaultStderrDevicePath = "/dev/stderr";
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

        lock (_runtime.SyncRoot)
        {
            var module = _moduleCache.GetOrLoad(modulePath);
            Trace($"host:create_instance:module_loaded projector={projectorName} path={modulePath}");
            var store = new Store(_runtime.Engine);
            var wasiConfiguration = new WasiConfiguration()
                .WithStandardOutput(ResolveWasiOutputPath("WASM_RUNTIME_STDOUT_PATH"))
                .WithStandardError(ResolveWasiOutputPath("WASM_RUNTIME_STDERR_PATH"));
            wasiConfiguration = wasiConfiguration.WithEnvironmentVariables(
                _options.ResolveGuestEnvironmentVariables()
                    .Select(pair => (pair.Key, pair.Value)));
            store.SetWasiConfiguration(wasiConfiguration);

            using var linker = _runtime.CreateLinker();
            Trace($"host:create_instance:before_instantiate projector={projectorName}");
            var instance = linker.Instantiate(store, module);
            Trace($"host:create_instance:after_instantiate projector={projectorName}");
            return new WasmtimePrimitiveProjectionInstance(store, instance, projectorName, _runtime.SyncRoot);
        }
    }

    private static void Trace(string message)
    {
        if (!TraceLifecycle)
        {
            return;
        }

        Console.WriteLine($"[wasmtime-trace] {message}");
    }

    private static string ResolveWasiOutputPath(string environmentVariableName)
    {
        var configuredPath = Environment.GetEnvironmentVariable(environmentVariableName);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        bool guestTraceEnabled = string.Equals(
            Environment.GetEnvironmentVariable("KENBAI_WASM_GUEST_TRACE"),
            "1",
            StringComparison.Ordinal);
        if (!guestTraceEnabled)
        {
            return DefaultNullDevicePath;
        }

        return string.Equals(environmentVariableName, "WASM_RUNTIME_STDERR_PATH", StringComparison.Ordinal)
            ? DefaultStderrDevicePath
            : DefaultStdoutDevicePath;
    }
}
