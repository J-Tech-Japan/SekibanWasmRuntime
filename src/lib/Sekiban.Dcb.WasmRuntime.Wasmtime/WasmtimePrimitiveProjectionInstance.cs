using System.Text;
using Sekiban.Dcb.Primitives;
using global::Wasmtime;

namespace Sekiban.Dcb.WasmRuntime.Wasmtime;

public class WasmtimePrimitiveProjectionInstance : IPrimitiveProjectionInstance
{
    private readonly Store _store;
    private readonly Instance _instance;
    private readonly Memory _memory;

    private readonly Func<int, int, int>? _createInstance;
    private readonly Action<int, int, int, int, int>? _applyEvent;
    private readonly Func<int, int, int, int, int, long>? _executeQuery;
    private readonly Func<int, int, int, int, int, long>? _executeListQuery;
    private readonly Func<int, long>? _serializeState;
    private readonly Action<int, int, int>? _restoreState;
    private readonly Func<int, int>? _alloc;
    private readonly Action<int, int>? _free;
    private readonly int _instanceId = -1;

    public WasmtimePrimitiveProjectionInstance(Store store, Instance instance, string projectorType)
    {
        _store = store;
        _instance = instance;
        _memory = instance.GetMemory("memory")
            ?? throw new InvalidOperationException("WASM module does not export memory");

        var initialize = instance.GetAction("_initialize");
        initialize?.Invoke();

        _createInstance = instance.GetFunction<int, int, int>("create_instance");
        _applyEvent = instance.GetAction<int, int, int, int, int>("apply_event");
        _executeQuery = instance.GetFunction<int, int, int, int, int, long>("execute_query");
        _executeListQuery = instance.GetFunction<int, int, int, int, int, long>("execute_list_query");
        _serializeState = instance.GetFunction<int, long>("serialize_state");
        _restoreState = instance.GetAction<int, int, int>("restore_state");

        _alloc = instance.GetFunction<int, int>("alloc");
        _free = instance.GetAction<int, int>("dealloc") ?? instance.GetAction<int, int>("free");

        if (_createInstance == null)
        {
            throw new InvalidOperationException("WASM module does not export create_instance.");
        }

        var (ptr, len) = WriteString(projectorType);
        var instanceId = _createInstance(ptr, len);
        Free(ptr, len);
        if (instanceId < 0)
        {
            throw new InvalidOperationException($"create_instance failed with code {instanceId}");
        }
        _instanceId = instanceId;
    }

    public void ApplyEvent(
        string eventType,
        string eventPayloadJson,
        IReadOnlyList<string> tags,
        string? sortableUniqueId)
    {
        EnsureExport(_applyEvent, nameof(_applyEvent));
        var (eventTypePtr, eventTypeLen) = WriteString(eventType);
        var (payloadPtr, payloadLen) = WriteString(eventPayloadJson);

        _applyEvent!(_instanceId, eventTypePtr, eventTypeLen, payloadPtr, payloadLen);
        Free(eventTypePtr, eventTypeLen);
        Free(payloadPtr, payloadLen);
    }

    public string ExecuteQuery(string queryType, string queryParamsJson)
    {
        EnsureExport(_executeQuery, nameof(_executeQuery));
        var (queryTypePtr, queryTypeLen) = WriteString(queryType);
        var (paramsPtr, paramsLen) = WriteString(queryParamsJson);

        var packed = _executeQuery!(_instanceId, queryTypePtr, queryTypeLen, paramsPtr, paramsLen);
        Free(queryTypePtr, queryTypeLen);
        Free(paramsPtr, paramsLen);

        return ReadPackedString(packed);
    }

    public string ExecuteListQuery(string queryType, string queryParamsJson)
    {
        EnsureExport(_executeListQuery, nameof(_executeListQuery));
        var (queryTypePtr, queryTypeLen) = WriteString(queryType);
        var (paramsPtr, paramsLen) = WriteString(queryParamsJson);

        var packed = _executeListQuery!(_instanceId, queryTypePtr, queryTypeLen, paramsPtr, paramsLen);
        Free(queryTypePtr, queryTypeLen);
        Free(paramsPtr, paramsLen);

        return ReadPackedString(packed);
    }

    public string SerializeState()
    {
        if (_serializeState == null)
        {
            return "{}";
        }
        var packed = _serializeState(_instanceId);
        return ReadPackedString(packed);
    }

    public void RestoreState(string stateJson)
    {
        if (_restoreState == null)
        {
            return;
        }
        var (ptr, len) = WriteString(stateJson);
        _restoreState(_instanceId, ptr, len);
        Free(ptr, len);
    }

    private (int ptr, int len) WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        var ptr = _alloc?.Invoke(bytes.Length) ?? 0;
        if (ptr == 0 || bytes.Length == 0)
        {
            return (0, 0);
        }

        var span = _memory.GetSpan(ptr, bytes.Length);
        bytes.CopyTo(span);
        return (ptr, bytes.Length);
    }

    private string ReadPackedString(long packed)
    {
        if (packed == 0)
        {
            return "";
        }
        var ptr = unchecked((int)(packed >> 32));
        var len = unchecked((int)(packed & 0xFFFFFFFF));
        if (ptr == 0 || len == 0)
        {
            return "";
        }
        var span = _memory.GetSpan(ptr, len);
        var result = Encoding.UTF8.GetString(span);
        Free(ptr, len);
        return result;
    }

    private void Free(int ptr, int len)
    {
        if (ptr == 0 || len == 0 || _free == null)
        {
            return;
        }
        _free(ptr, len);
    }

    private static void EnsureExport(object? export, string name)
    {
        if (export == null)
        {
            throw new InvalidOperationException($"WASM module does not export {name}.");
        }
    }

    public void Dispose()
    {
        _store.Dispose();
    }
}
