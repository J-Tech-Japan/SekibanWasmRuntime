using System.Collections.Concurrent;
using System.Threading;
using Sekiban.Dcb.Primitives;
using global::Wasmtime;

namespace Sekiban.Dcb.WasmRuntime.Wasmtime;

public sealed class WasmtimePrimitiveProjectionHost :
    IPrimitiveProjectionHost,
    IFreshPrimitiveProjectionHost,
    IDisposable
{
    private const string DefaultNullDevicePath = "/dev/null";
    private const string DefaultStdoutDevicePath = "/dev/stdout";
    private const string DefaultStderrDevicePath = "/dev/stderr";
    private const string EmptyStateJson = "{}";
    private static readonly bool TraceLifecycle =
        string.Equals(
            Environment.GetEnvironmentVariable("WASM_RUNTIME_TRACE_LIFECYCLE"),
            "1",
            StringComparison.Ordinal);

    private readonly WasmtimeRuntime _runtime;
    private readonly WasmtimeModuleCache _moduleCache;
    private readonly WasmtimeHostOptions _options;
    private readonly ConcurrentDictionary<string, PooledProjectorBucket> _pools =
        new(StringComparer.Ordinal);

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
        if (TryRent(projectorName, out WasmtimePrimitiveProjectionInstance? pooled) &&
            pooled is not null)
        {
            Trace($"host:create_instance:pool_hit projector={projectorName}");
            return new PooledPrimitiveProjectionLease(projectorName, pooled, ReturnToPool);
        }

        Trace($"host:create_instance:pool_miss projector={projectorName}");
        WasmtimePrimitiveProjectionInstance core = CreateCoreInstance(projectorName);
        return new PooledPrimitiveProjectionLease(projectorName, core, ReturnToPool);
    }

    public IPrimitiveProjectionInstance CreateFreshInstance(string projectorName)
    {
        Trace($"host:create_fresh_instance:start projector={projectorName}");
        WasmtimePrimitiveProjectionInstance core = CreateCoreInstance(projectorName);
        return new PooledPrimitiveProjectionLease(projectorName, core, ReturnToPool);
    }

    public void Dispose()
    {
        foreach ((_, PooledProjectorBucket bucket) in _pools)
        {
            bucket.Dispose();
        }
        _pools.Clear();
    }

    private WasmtimePrimitiveProjectionInstance CreateCoreInstance(string projectorName)
    {
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
            return new WasmtimePrimitiveProjectionInstance(store, instance, projectorName);
        }
    }

    private bool TryRent(string projectorName, out WasmtimePrimitiveProjectionInstance? instance)
    {
        instance = null;
        if (!_options.EnableInstancePooling || _options.MaxPooledInstancesPerProjector <= 0)
        {
            return false;
        }

        if (!_pools.TryGetValue(projectorName, out PooledProjectorBucket? bucket))
        {
            return false;
        }

        return bucket.TryRent(out instance);
    }

    private void ReturnToPool(string projectorName, WasmtimePrimitiveProjectionInstance instance)
    {
        if (!_options.EnableInstancePooling || _options.MaxPooledInstancesPerProjector <= 0)
        {
            instance.Dispose();
            return;
        }

        try
        {
            instance.RestoreState(EmptyStateJson);
        }
        catch (Exception ex)
        {
            Trace(
                $"host:return_to_pool:reset_failed projector={projectorName} message={SanitizeForTrace(ex.Message)}");
            instance.Dispose();
            return;
        }

        PooledProjectorBucket bucket = _pools.GetOrAdd(
            projectorName,
            _ => new PooledProjectorBucket(_options.MaxPooledInstancesPerProjector));
        if (!bucket.Return(instance))
        {
            Trace($"host:return_to_pool:pool_full projector={projectorName}");
            instance.Dispose();
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

    private static string SanitizeForTrace(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ');

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

    private sealed class PooledProjectorBucket : IDisposable
    {
        private readonly object _gate = new();
        private readonly Queue<WasmtimePrimitiveProjectionInstance> _instances = new();
        private readonly int _maxCount;

        public PooledProjectorBucket(int maxCount)
        {
            _maxCount = Math.Max(1, maxCount);
        }

        public bool TryRent(out WasmtimePrimitiveProjectionInstance? instance)
        {
            lock (_gate)
            {
                if (_instances.Count == 0)
                {
                    instance = null;
                    return false;
                }

                instance = _instances.Dequeue();
                return true;
            }
        }

        public bool Return(WasmtimePrimitiveProjectionInstance instance)
        {
            lock (_gate)
            {
                if (_instances.Count >= _maxCount)
                {
                    return false;
                }

                _instances.Enqueue(instance);
                return true;
            }
        }

        public void Dispose()
        {
            while (true)
            {
                WasmtimePrimitiveProjectionInstance? instance;
                lock (_gate)
                {
                    if (_instances.Count == 0)
                    {
                        return;
                    }

                    instance = _instances.Dequeue();
                }

                instance.Dispose();
            }
        }
    }

    private sealed class PooledPrimitiveProjectionLease :
        IPrimitiveProjectionInstance,
        IPooledPrimitiveProjectionLeaseControl
    {
        private readonly string _projectorName;
        private readonly WasmtimePrimitiveProjectionInstance _inner;
        private readonly Action<string, WasmtimePrimitiveProjectionInstance> _release;
        private int _disposed;
        private bool _returnToPool = true;

        public PooledPrimitiveProjectionLease(
            string projectorName,
            WasmtimePrimitiveProjectionInstance inner,
            Action<string, WasmtimePrimitiveProjectionInstance> release)
        {
            _projectorName = projectorName;
            _inner = inner;
            _release = release;
        }

        public void ApplyEvent(
            string eventType,
            string eventPayloadJson,
            IReadOnlyList<string> tags,
            string? sortableUniqueId)
        {
            ThrowIfDisposed();
            _inner.ApplyEvent(eventType, eventPayloadJson, tags, sortableUniqueId);
        }

        public void ApplyEvents(IReadOnlyList<PrimitiveProjectionEventEnvelope> events)
        {
            ThrowIfDisposed();
            _inner.ApplyEvents(events);
        }

        public string ExecuteQuery(string queryType, string queryParamsJson)
        {
            ThrowIfDisposed();
            return _inner.ExecuteQuery(queryType, queryParamsJson);
        }

        public string ExecuteListQuery(string queryType, string queryParamsJson)
        {
            ThrowIfDisposed();
            return _inner.ExecuteListQuery(queryType, queryParamsJson);
        }

        public string SerializeState()
        {
            ThrowIfDisposed();
            return _inner.SerializeState();
        }

        public byte[] SerializeStateUtf8()
        {
            ThrowIfDisposed();
            return _inner.SerializeStateUtf8();
        }

        public void RestoreState(string stateJson)
        {
            ThrowIfDisposed();
            _inner.RestoreState(stateJson);
        }

        public void RestoreStateUtf8(byte[] stateJsonUtf8)
        {
            ThrowIfDisposed();
            _inner.RestoreStateUtf8(stateJsonUtf8);
        }

        public void MarkDoNotPool()
        {
            ThrowIfDisposed();
            _returnToPool = false;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (_returnToPool)
            {
                _release(_projectorName, _inner);
                return;
            }

            _inner.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(PooledPrimitiveProjectionLease));
            }
        }
    }
}
