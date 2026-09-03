using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Primitives;

namespace Sekiban.Dcb.WasmRuntime.Wasmtime;

/// <summary>
/// Projection instance backed by a WASI Preview2 WebAssembly component.
///
/// The native shim deliberately exposes a small JSON-array call bridge because the
/// current Wasmtime .NET API is core-module oriented. This adapter keeps that bridge
/// at the component boundary while preserving the existing primitive projection API.
/// </summary>
public sealed class WasmtimeComponentProjectionInstance :
    IPrimitiveProjectionInstance,
    ISerializableEventBatchProjectionInstance
{
    private const string ShimLibraryName = "wasmtime_preview2_shim";

    private readonly object _syncRoot = new();
    private readonly string _projectorType;
    private IntPtr _handle;
    private uint _instanceId;
    private bool _disposed;

    public WasmtimeComponentProjectionInstance(string componentPath, string projectorType)
    {
        if (string.IsNullOrWhiteSpace(componentPath) || !File.Exists(componentPath))
        {
            throw new InvalidOperationException($"WASM component was not found: {componentPath}");
        }

        _projectorType = projectorType;
        string? shimPath = WasmtimePreview2ShimResolver.EnsureAvailableFor(GetType().Assembly);
        if (string.IsNullOrWhiteSpace(shimPath))
        {
            throw new InvalidOperationException(
                "The Wasmtime preview2 shim is unavailable. Build external/wasmtime-dotnet/native/wasmtime-preview2-shim or set WASMTIME_PREVIEW2_SHIM_PATH.");
        }

        _handle = Instantiate(componentPath, inheritStdio: false);
        try
        {
            _instanceId = ReadUInt32(CallCore("create-instance", projectorType));
        }
        catch
        {
            FreeInstance();
            throw;
        }
    }

    public void ApplyEvent(
        string eventType,
        string eventPayloadJson,
        IReadOnlyList<string> tags,
        string? sortableUniqueId)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            _ = CallCore("apply-event", _instanceId, eventType, eventPayloadJson);
        }
    }

    public void ApplyEvents(IReadOnlyList<PrimitiveProjectionEventEnvelope> events)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            foreach (var ev in events)
            {
                _ = CallCore("apply-event", _instanceId, ev.EventType, ev.EventPayloadJson);
            }
        }
    }

    public void ApplySerializableEvents(IReadOnlyList<SerializableEvent> events)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            foreach (var ev in events)
            {
                _ = CallCore(
                    "apply-event",
                    _instanceId,
                    ev.EventPayloadName,
                    Encoding.UTF8.GetString(ev.Payload));
            }
        }
    }

    public string ExecuteQuery(string queryType, string queryParamsJson)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            return ReadString(CallCore("execute-query", _instanceId, queryType, queryParamsJson));
        }
    }

    public string ExecuteListQuery(string queryType, string queryParamsJson)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            return ReadString(CallCore("execute-list-query", _instanceId, queryType, queryParamsJson));
        }
    }

    public string SerializeState()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            return ReadString(CallCore("serialize-state", _instanceId));
        }
    }

    public byte[] SerializeStateUtf8() => Encoding.UTF8.GetBytes(SerializeState());

    public IReadOnlyList<string> GetEventTypes()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            using JsonDocument document = JsonDocument.Parse(CallCore("get-event-types"));
            return document.RootElement[0].EnumerateArray()
                .Select(element => element.GetString() ?? string.Empty)
                .ToArray();
        }
    }

    public string SerializeEvent(string eventType, string payloadJson)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            return ReadString(CallCore("serialize-event", eventType, payloadJson));
        }
    }

    public string DeserializeEvent(string eventType, string json)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            return ReadString(CallCore("deserialize-event", eventType, json));
        }
    }

    public void RestoreState(string stateJson)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            _ = CallCore("restore-state", _instanceId, stateJson);
        }
    }

    public void RestoreStateUtf8(byte[] stateJsonUtf8) =>
        RestoreState(Encoding.UTF8.GetString(stateJsonUtf8));

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            FreeInstance();
        }
    }

    private string CallCore(string functionName, params object?[] args)
    {
        string argsJson = JsonSerializer.Serialize(args);
        int resultCode = wasmtime_preview2_call_func(
            _handle,
            functionName,
            argsJson,
            out IntPtr resultJson,
            out IntPtr errorMessage);
        if (resultCode != 0)
        {
            string error = ReadAndFreeString(errorMessage) ?? "component call failed";
            throw new InvalidOperationException(
                $"Component export '{functionName}' failed for projector '{_projectorType}': {error}");
        }

        return ReadAndFreeString(resultJson) ?? "[]";
    }

    private static IntPtr Instantiate(string componentPath, bool inheritStdio)
    {
        IntPtr handle = wasmtime_preview2_instantiate_component(
            componentPath,
            inheritStdio,
            out IntPtr errorMessage);
        if (handle == IntPtr.Zero)
        {
            string error = ReadAndFreeString(errorMessage) ?? "component instantiation failed";
            throw new InvalidOperationException(error);
        }

        return handle;
    }

    private void FreeInstance()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        wasmtime_preview2_free_instance(_handle);
        _handle = IntPtr.Zero;
    }

    private static uint ReadUInt32(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement[0].GetUInt32();
    }

    private static string ReadString(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement[0].GetString() ?? string.Empty;
    }

    private static string? ReadAndFreeString(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(pointer);
        }
        finally
        {
            wasmtime_preview2_free_string(pointer);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WasmtimeComponentProjectionInstance));
        }
    }

    [DllImport(ShimLibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr wasmtime_preview2_instantiate_component(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string componentPath,
        [MarshalAs(UnmanagedType.I1)] bool inheritStdio,
        out IntPtr errorMessageOut);

    [DllImport(ShimLibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int wasmtime_preview2_call_func(
        IntPtr handle,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string functionName,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string argsJson,
        out IntPtr resultJsonOut,
        out IntPtr errorMessageOut);

    [DllImport(ShimLibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void wasmtime_preview2_free_instance(IntPtr handle);

    [DllImport(ShimLibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void wasmtime_preview2_free_string(IntPtr pointer);
}
