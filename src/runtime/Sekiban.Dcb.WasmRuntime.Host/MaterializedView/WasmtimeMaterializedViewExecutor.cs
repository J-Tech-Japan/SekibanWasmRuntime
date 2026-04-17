using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.WasmRuntime.Wasmtime;
using global::Wasmtime;

namespace Sekiban.Dcb.WasmRuntime.Host.MaterializedView;

/// <summary>
///     Wasmtime-backed executor for the MV exports (<c>mv_metadata</c>, <c>mv_initialize</c>,
///     <c>mv_apply_event</c>). Maintains a dedicated Wasmtime instance for MV calls and wires
///     the <c>mv_host_query_rows</c> host import so the WASM module can read the projected
///     Postgres tables mid-apply (sync — see class docs for the rationale).
///
///     <para>
///     Instance scope: single shared <see cref="Store"/> + <see cref="Instance"/> for all MV
///     views. MV projectors are effectively stateless (all state is in Postgres), and the MV
///     dispatch inside the WASM module is view-keyed so one instance can serve every registered
///     materialized view. Keeps this pool separate from <c>WasmtimePrimitiveProjectionHost</c>
///     so MV calls do not contend with MultiProjection catch-up.
///     </para>
///
///     <para>
///     Concurrency: a <see cref="SemaphoreSlim"/> serializes access to the Wasmtime instance
///     (Wasmtime <c>Store</c> is not thread-safe). This is fine because
///     <c>MaterializedViewGrain</c> already serializes catch-up/stream-apply per view.
///     </para>
/// </summary>
public sealed class WasmtimeMaterializedViewExecutor : IWasmMaterializedViewExecutor, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Capture the apply context for the ongoing mv_apply_event call so the host import can
    // route mv_host_query_rows back to the apply-time Postgres connection/transaction.
    private static readonly AsyncLocal<IMvApplyContext?> CurrentApplyContext = new();

    private readonly WasmMaterializedViewRuntimeOptions _options;
    private readonly ILogger<WasmtimeMaterializedViewExecutor> _logger;
    private readonly WasmtimeRuntime _runtime;
    private readonly WasmtimeModuleCache _moduleCache;
    private readonly SemaphoreSlim _instanceGate = new(1, 1);

    private Store? _store;
    private Instance? _instance;
    private Memory? _memory;
    private Func<int, int>? _alloc;
    private Action<int, int>? _dealloc;
    private Func<long>? _mvMetadata;
    // Signatures: view name (ptr+len) + viewVersion + bindings json (ptr+len) [+ event json (ptr+len) for apply]
    private Func<int, int, int, int, int, long>? _mvInitialize;
    private Func<int, int, int, int, int, int, int, long>? _mvApplyEvent;

    public WasmtimeMaterializedViewExecutor(
        WasmMaterializedViewRuntimeOptions options,
        WasmtimeRuntime runtime,
        WasmtimeModuleCache moduleCache,
        ILogger<WasmtimeMaterializedViewExecutor> logger)
    {
        _options = options;
        _runtime = runtime;
        _moduleCache = moduleCache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WasmMvMetadataDto>> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        await _instanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInstance();
            long packed = _mvMetadata!.Invoke();
            var json = ReadPackedString(packed);
            var metadata = JsonSerializer.Deserialize<List<WasmMvMetadataDto>>(json, JsonOptions) ?? [];
            return metadata;
        }
        finally
        {
            _instanceGate.Release();
        }
    }

    public async Task<IReadOnlyList<WasmMvSqlStatementDto>> InitializeAsync(
        string viewName,
        int viewVersion,
        WasmMvTableBindingsDto tableBindings,
        CancellationToken cancellationToken = default)
    {
        await _instanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureInstance();
            var bindingsJson = JsonSerializer.Serialize(tableBindings, JsonOptions);
            var (vnPtr, vnLen) = WriteString(viewName);
            var (bPtr, bLen) = WriteString(bindingsJson);
            try
            {
                long packed = _mvInitialize!.Invoke(vnPtr, vnLen, viewVersion, bPtr, bLen);
                var json = ReadPackedString(packed);
                ThrowIfErrorEnvelope(json, nameof(InitializeAsync), viewName, viewVersion);
                var batch = JsonSerializer.Deserialize<WasmMvStatementBatchDto>(json, JsonOptions) ?? new();
                return batch.Statements;
            }
            finally
            {
                Free(vnPtr, vnLen);
                Free(bPtr, bLen);
            }
        }
        finally
        {
            _instanceGate.Release();
        }
    }

    public async Task<IReadOnlyList<WasmMvSqlStatementDto>> ApplyEventAsync(
        string viewName,
        int viewVersion,
        WasmMvTableBindingsDto tableBindings,
        WasmMvSerializableEventDto serializableEvent,
        IMvApplyContext applyContext,
        CancellationToken cancellationToken = default)
    {
        await _instanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var previous = CurrentApplyContext.Value;
        CurrentApplyContext.Value = applyContext;
        try
        {
            EnsureInstance();
            var bindingsJson = JsonSerializer.Serialize(tableBindings, JsonOptions);
            var eventJson = JsonSerializer.Serialize(serializableEvent, JsonOptions);
            var (vnPtr, vnLen) = WriteString(viewName);
            var (bPtr, bLen) = WriteString(bindingsJson);
            var (ePtr, eLen) = WriteString(eventJson);
            try
            {
                long packed = _mvApplyEvent!.Invoke(vnPtr, vnLen, viewVersion, bPtr, bLen, ePtr, eLen);
                var json = ReadPackedString(packed);
                ThrowIfErrorEnvelope(json, nameof(ApplyEventAsync), viewName, viewVersion);
                var batch = JsonSerializer.Deserialize<WasmMvStatementBatchDto>(json, JsonOptions) ?? new();
                return batch.Statements;
            }
            finally
            {
                Free(vnPtr, vnLen);
                Free(bPtr, bLen);
                Free(ePtr, eLen);
            }
        }
        finally
        {
            CurrentApplyContext.Value = previous;
            _instanceGate.Release();
        }
    }

    // ------------------------------------------------------------------------
    // Wasmtime instance lifecycle
    // ------------------------------------------------------------------------

    private void EnsureInstance()
    {
        if (_instance is not null) return;

        if (string.IsNullOrWhiteSpace(_options.ModulePath) || !File.Exists(_options.ModulePath))
        {
            throw new InvalidOperationException(
                $"Materialized view WASM module not found at '{_options.ModulePath}'. " +
                "Set WasmMaterializedViewRuntimeOptions.ModulePath or ensure the manifest points at a valid .wasm file.");
        }

        // Reuse the shared Engine + module cache so we inherit component→core extraction and
        // avoid loading the same ~35 MB .wasm twice.
        var module = _moduleCache.GetOrLoad(_options.ModulePath);
        _store = new Store(_runtime.Engine);
        _store.SetWasiConfiguration(new WasiConfiguration());

        var linker = _runtime.CreateLinker();

        // Host import: mv_host_query_rows(sqlPtr, sqlLen, paramsPtr, paramsLen, rowLimit) -> long
        // WASM receives a packed (ptr, len) long pointing at a JSON-serialized
        // WasmMvQueryResultDto stored in WASM linear memory. WASM is expected to free the
        // buffer via its own `dealloc` export once consumed.
        linker.Define(
            "env",
            "mv_host_query_rows",
            Function.FromCallback<int, int, int, int, int, long>(
                _store,
                (Caller caller, int sqlPtr, int sqlLen, int paramsPtr, int paramsLen, int rowLimit) =>
                    HandleHostQueryRows(caller, sqlPtr, sqlLen, paramsPtr, paramsLen, rowLimit)));

        _instance = linker.Instantiate(_store, module);
        _memory = _instance.GetMemory("memory")
            ?? throw new InvalidOperationException("WASM MV module does not export 'memory'.");

        // Run WASI _initialize so static field initializers and JsonSerializerContext generators fire.
        var initialize = _instance.GetAction("_initialize") ?? _instance.GetAction("_start");
        initialize?.Invoke();

        _alloc = _instance.GetFunction<int, int>("alloc")
            ?? throw new InvalidOperationException("WASM MV module does not export 'alloc'.");
        _dealloc = _instance.GetAction<int, int>("dealloc")
            ?? _instance.GetAction<int, int>("free")
            ?? throw new InvalidOperationException("WASM MV module does not export 'dealloc' or 'free'.");

        _mvMetadata = _instance.GetFunction<long>("mv_metadata")
            ?? throw new InvalidOperationException("WASM MV module does not export 'mv_metadata'.");
        _mvInitialize = _instance.GetFunction<int, int, int, int, int, long>("mv_initialize")
            ?? throw new InvalidOperationException("WASM MV module does not export 'mv_initialize'.");
        _mvApplyEvent = _instance.GetFunction<int, int, int, int, int, int, int, long>("mv_apply_event")
            ?? throw new InvalidOperationException("WASM MV module does not export 'mv_apply_event'.");

        _logger.LogInformation("Materialized view Wasmtime instance initialized from {ModulePath}.", _options.ModulePath);
    }

    // ------------------------------------------------------------------------
    // Host import — mv_host_query_rows
    // ------------------------------------------------------------------------

    private long HandleHostQueryRows(
        Caller caller,
        int sqlPtr, int sqlLen,
        int paramsPtr, int paramsLen,
        int rowLimit)
    {
        try
        {
            var callerMemory = caller.GetMemory("memory")
                ?? throw new InvalidOperationException("WASM caller has no 'memory' export.");

            var sql = ReadMemoryString(callerMemory, sqlPtr, sqlLen);
            var paramsJson = ReadMemoryString(callerMemory, paramsPtr, paramsLen);

            var mvParams = string.IsNullOrWhiteSpace(paramsJson)
                ? new List<WasmMvParam>()
                : JsonSerializer.Deserialize<List<WasmMvParam>>(paramsJson, JsonOptions) ?? new();
            var dapperParams = MvParamDapperBridge.ToDapperParameters(mvParams);

            var ctx = CurrentApplyContext.Value
                ?? throw new InvalidOperationException(
                    "mv_host_query_rows was invoked outside of an active apply context.");

            var resultJson = ExecuteQueryToJson(ctx, sql, dapperParams, rowLimit);
            var bytes = Encoding.UTF8.GetBytes(resultJson);
            if (bytes.Length == 0)
            {
                return 0;
            }

            var allocFn = caller.GetFunction("alloc")?.WrapFunc<int, int>()
                ?? throw new InvalidOperationException("WASM caller has no 'alloc' export.");
            var ptr = allocFn(bytes.Length);
            if (ptr == 0) return 0;

            bytes.AsSpan().CopyTo(callerMemory.GetSpan(ptr, bytes.Length));
            return ((long)(uint)ptr << 32) | (uint)bytes.Length;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "mv_host_query_rows host import failed.");
            // Surface errors by writing a JSON envelope back to WASM. The projector will fail
            // to deserialize it as MvQueryResultDto and bubble up as an apply-time exception,
            // which the grain logs and retries.
            return EncodeErrorForCaller(caller, ex.Message);
        }
    }

    private static string ExecuteQueryToJson(
        IMvApplyContext ctx,
        string sql,
        DynamicParameters parameters,
        int rowLimit)
    {
        // Async → sync: Wasmtime host imports are synchronous. We block here because the
        // MaterializedViewGrain already serializes apply operations per view. Long-running
        // queries will stall the grain; projectors should be written with that in mind.
        if (rowLimit == 1)
        {
            var row = ctx.QuerySingleOrDefaultRowAsync(sql, parameters).GetAwaiter().GetResult();
            return row is null
                ? "{\"rows\":[]}"
                : "{\"rows\":[" + row.ToJson() + "]}";
        }

        var set = ctx.QueryRowsAsync(sql, parameters).GetAwaiter().GetResult();
        var sb = new StringBuilder("{\"rows\":[");
        for (int i = 0; i < set.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(set[i].ToJson());
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static long EncodeErrorForCaller(Caller caller, string message)
    {
        try
        {
            var callerMemory = caller.GetMemory("memory");
            var allocFn = caller.GetFunction("alloc")?.WrapFunc<int, int>();
            if (callerMemory is null || allocFn is null) return 0;

            var payload = "{\"error\":" + JsonSerializer.Serialize(message, JsonOptions) + "}";
            var bytes = Encoding.UTF8.GetBytes(payload);
            var ptr = allocFn(bytes.Length);
            if (ptr == 0) return 0;
            bytes.AsSpan().CopyTo(callerMemory.GetSpan(ptr, bytes.Length));
            return ((long)(uint)ptr << 32) | (uint)bytes.Length;
        }
        catch
        {
            return 0;
        }
    }

    // ------------------------------------------------------------------------
    // Memory helpers
    // ------------------------------------------------------------------------

    private (int ptr, int len) WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        if (bytes.Length == 0) return (0, 0);
        var ptr = _alloc!.Invoke(bytes.Length);
        if (ptr == 0) return (0, 0);
        bytes.AsSpan().CopyTo(_memory!.GetSpan(ptr, bytes.Length));
        return (ptr, bytes.Length);
    }

    private string ReadPackedString(long packed)
    {
        if (packed == 0) return string.Empty;
        var ptr = unchecked((int)(packed >> 32));
        var len = unchecked((int)(packed & 0xFFFFFFFF));
        if (ptr == 0 || len == 0) return string.Empty;
        var result = Encoding.UTF8.GetString(_memory!.GetSpan(ptr, len));
        Free(ptr, len);
        return result;
    }

    private static string ReadMemoryString(Memory memory, int ptr, int len)
    {
        if (ptr == 0 || len <= 0) return string.Empty;
        return Encoding.UTF8.GetString(memory.GetSpan(ptr, len));
    }

    private void Free(int ptr, int len)
    {
        if (ptr == 0 || len == 0 || _dealloc is null) return;
        _dealloc(ptr, len);
    }

    private static void ThrowIfErrorEnvelope(string json, string op, string viewName, int viewVersion)
    {
        if (string.IsNullOrEmpty(json) || !json.StartsWith("{\"error\"", StringComparison.Ordinal))
        {
            return;
        }

        using var doc = JsonDocument.Parse(json);
        var message = doc.RootElement.TryGetProperty("error", out var err) ? err.GetString() : json;
        throw new InvalidOperationException(
            $"WASM {op} for {viewName}/{viewVersion} returned error envelope: {message}");
    }

    public void Dispose()
    {
        _instanceGate.Dispose();
        _store?.Dispose();
        // Engine and module come from the shared WasmtimeRuntime/WasmtimeModuleCache singletons
        // so disposing them here would break other consumers.
    }

    internal static IMvApplyContext? CurrentContextForTesting => CurrentApplyContext.Value;
}

public sealed class WasmMaterializedViewRuntimeOptions
{
    public string ModulePath { get; init; } = string.Empty;
    public long StaticMemoryMaximumSizeBytes { get; init; } = 64L * 1024 * 1024;
}
