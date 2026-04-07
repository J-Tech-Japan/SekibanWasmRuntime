using System.Runtime.InteropServices;

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
