using System.Runtime.InteropServices;
using SekibanDcbDecider.Wasm.MaterializedView;

namespace SekibanDcbDecider.Wasm;

public static class WasmExports
{
    private static readonly object Gate = new();
    private static readonly Dictionary<int, WasmDispatcher.ProjectionInstanceState> Instances = new();
    private static int _nextInstanceId = 1;

    [UnmanagedCallersOnly(EntryPoint = "alloc")]
    public static unsafe int Alloc(int size)
    {
        if (size <= 0)
        {
            return 0;
        }

        return (int)NativeMemory.Alloc((nuint)size);
    }

    [UnmanagedCallersOnly(EntryPoint = "dealloc")]
    public static unsafe void Dealloc(int ptr, int size)
    {
        if (ptr == 0)
        {
            return;
        }

        NativeMemory.Free((void*)ptr);
    }

    [UnmanagedCallersOnly(EntryPoint = "create_instance")]
    public static int CreateInstance(int projectorTypePtr, int projectorTypeLen)
    {
        string projectorType = ReadString(projectorTypePtr, projectorTypeLen);
        WasmDispatcher.ProjectionInstanceState? instance = WasmDispatcher.CreateInstance(projectorType);
        if (instance is null)
        {
            return -1;
        }

        lock (Gate)
        {
            int instanceId = _nextInstanceId++;
            Instances[instanceId] = instance;
            return instanceId;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "apply_event")]
    public static void ApplyEvent(
        int instanceId,
        int eventTypePtr,
        int eventTypeLen,
        int payloadPtr,
        int payloadLen)
    {
        WasmDispatcher.ProjectionInstanceState? instance = GetInstance(instanceId);
        if (instance is null)
        {
            return;
        }

        string eventType = ReadString(eventTypePtr, eventTypeLen);
        string payloadJson = ReadString(payloadPtr, payloadLen);
        WasmDispatcher.ApplyEvent(instance, eventType, payloadJson, "{}");
    }

    [UnmanagedCallersOnly(EntryPoint = "apply_event_with_metadata")]
    public static void ApplyEventWithMetadata(
        int instanceId,
        int eventTypePtr,
        int eventTypeLen,
        int payloadPtr,
        int payloadLen,
        int metadataPtr,
        int metadataLen)
    {
        WasmDispatcher.ProjectionInstanceState? instance = GetInstance(instanceId);
        if (instance is null)
        {
            return;
        }

        string eventType = ReadString(eventTypePtr, eventTypeLen);
        string payloadJson = ReadString(payloadPtr, payloadLen);
        string metadataJson = ReadString(metadataPtr, metadataLen);
        WasmDispatcher.ApplyEvent(instance, eventType, payloadJson, metadataJson);
    }

    [UnmanagedCallersOnly(EntryPoint = "execute_query")]
    public static long ExecuteQuery(
        int instanceId,
        int queryTypePtr,
        int queryTypeLen,
        int paramsPtr,
        int paramsLen)
    {
        WasmDispatcher.ProjectionInstanceState? instance = GetInstance(instanceId);
        if (instance is null)
        {
            return WriteString("null");
        }

        string queryType = ReadString(queryTypePtr, queryTypeLen);
        string queryJson = ReadString(paramsPtr, paramsLen);
        return WriteString(WasmDispatcher.ExecuteQuery(instance, queryType, queryJson));
    }

    [UnmanagedCallersOnly(EntryPoint = "execute_list_query")]
    public static long ExecuteListQuery(
        int instanceId,
        int queryTypePtr,
        int queryTypeLen,
        int paramsPtr,
        int paramsLen)
    {
        WasmDispatcher.ProjectionInstanceState? instance = GetInstance(instanceId);
        if (instance is null)
        {
            return WriteString("[]");
        }

        string queryType = ReadString(queryTypePtr, queryTypeLen);
        string queryJson = ReadString(paramsPtr, paramsLen);
        return WriteString(WasmDispatcher.ExecuteListQuery(instance, queryType, queryJson));
    }

    [UnmanagedCallersOnly(EntryPoint = "serialize_state")]
    public static long SerializeState(int instanceId)
    {
        WasmDispatcher.ProjectionInstanceState? instance = GetInstance(instanceId);
        return WriteString(instance is null ? "{}" : WasmDispatcher.SerializeState(instance));
    }

    [UnmanagedCallersOnly(EntryPoint = "restore_state")]
    public static void RestoreState(int instanceId, int statePtr, int stateLen)
    {
        WasmDispatcher.ProjectionInstanceState? instance = GetInstance(instanceId);
        if (instance is null)
        {
            return;
        }

        WasmDispatcher.RestoreState(instance, ReadString(statePtr, stateLen));
    }

    // --- Materialized view exports ---

    [UnmanagedCallersOnly(EntryPoint = "mv_metadata")]
    public static long MvMetadata() => WriteString(WasmMvRegistry.Metadata());

    [UnmanagedCallersOnly(EntryPoint = "mv_initialize")]
    public static long MvInitialize(
        int viewNamePtr,
        int viewNameLen,
        int viewVersion,
        int tableBindingsPtr,
        int tableBindingsLen)
    {
        string viewName = ReadString(viewNamePtr, viewNameLen);
        string bindingsJson = ReadString(tableBindingsPtr, tableBindingsLen);
        try
        {
            return WriteString(WasmMvRegistry.Initialize(viewName, viewVersion, bindingsJson));
        }
        catch (Exception ex)
        {
            return WriteString($"{{\"error\":{EscapeJsonString(ex.Message)}}}");
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "mv_apply_event")]
    public static long MvApplyEvent(
        int viewNamePtr,
        int viewNameLen,
        int viewVersion,
        int tableBindingsPtr,
        int tableBindingsLen,
        int serializableEventPtr,
        int serializableEventLen)
    {
        string viewName = ReadString(viewNamePtr, viewNameLen);
        string bindingsJson = ReadString(tableBindingsPtr, tableBindingsLen);
        string eventJson = ReadString(serializableEventPtr, serializableEventLen);
        try
        {
            return WriteString(WasmMvRegistry.ApplyEvent(
                viewName,
                viewVersion,
                bindingsJson,
                eventJson,
                HostBackedMvQueryPort.Instance));
        }
        catch (Exception ex)
        {
            return WriteString($"{{\"error\":{EscapeJsonString(ex.Message)}}}");
        }
    }

    private static WasmDispatcher.ProjectionInstanceState? GetInstance(int instanceId)
    {
        lock (Gate)
        {
            return Instances.TryGetValue(instanceId, out WasmDispatcher.ProjectionInstanceState? instance)
                ? instance
                : null;
        }
    }

    private static unsafe string ReadString(int ptr, int len)
    {
        if (ptr == 0 || len <= 0)
        {
            return string.Empty;
        }

        return System.Text.Encoding.UTF8.GetString(new ReadOnlySpan<byte>((void*)ptr, len));
    }

    // AOT-friendly JSON string literal producer. Used by error-return paths that must be self
    // contained and not rely on reflection-based JsonSerializer.
    private static string EscapeJsonString(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static unsafe long WriteString(string value)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty);
        if (bytes.Length == 0)
        {
            return 0;
        }

        void* ptr = NativeMemory.Alloc((nuint)bytes.Length);
        bytes.AsSpan().CopyTo(new Span<byte>(ptr, bytes.Length));
        return ((long)(uint)(nint)ptr << 32) | (uint)bytes.Length;
    }
}
