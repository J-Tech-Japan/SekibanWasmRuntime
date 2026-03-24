using System.Text;
using System.Text.Json;
using Sekiban.Dcb.Primitives;
using global::Wasmtime;

namespace Sekiban.Dcb.WasmRuntime.Wasmtime;

public class WasmtimePrimitiveProjectionInstance : IPrimitiveProjectionInstance
{
    private static readonly JsonDocumentOptions CompactPayloadJsonDocumentOptions = new()
    {
        MaxDepth = 256
    };

    private const int BufferedPayloadChunkSize = 16 * 1024;
    private static readonly object TraceFileLock = new();
    private static readonly bool TraceLifecycle =
        string.Equals(
            Environment.GetEnvironmentVariable("WASM_RUNTIME_TRACE_LIFECYCLE"),
            "1",
            StringComparison.Ordinal);
    private static readonly string TraceFilePath =
        Environment.GetEnvironmentVariable("WASM_RUNTIME_TRACE_PATH")
        ?? Path.Combine(Path.GetTempPath(), "kenbai-wasm-runtime-trace.log");

    private readonly object _syncRoot;
    private readonly Store _store;
    private readonly Instance _instance;
    private readonly Memory _memory;
    private readonly string _projectorType;
    private bool _disposed;

    private readonly Func<int, int, int>? _createInstance;
    private readonly Action<int, int, int, int, int>? _applyEvent;
    private readonly Function? _applyEventWithTags;
    private readonly Action<int>? _beginPayloadBuffer;
    private readonly Action<int, int, int>? _appendPayloadChunk;
    private readonly Action<int, int, int>? _applyBufferedEvent;
    private readonly Function? _applyBufferedEventWithTags;
    private readonly Func<int, int, int, int, int, long>? _executeQuery;
    private readonly Func<int, int, int, int, int, long>? _executeListQuery;
    private readonly Func<int, long>? _serializeState;
    private readonly Action<int, int, int>? _restoreState;
    private readonly Func<int, int>? _alloc;
    private readonly Action<int, int>? _free;
    private readonly int _instanceId = -1;

    public WasmtimePrimitiveProjectionInstance(Store store, Instance instance, string projectorType, object syncRoot)
    {
        _syncRoot = syncRoot;
        _store = store;
        _instance = instance;
        _projectorType = projectorType;
        _memory = instance.GetMemory("memory")
            ?? throw new InvalidOperationException("WASM module does not export memory");

        lock (_syncRoot)
        {
            Trace($"constructor:start projector={projectorType}");
            var initialize = instance.GetAction("_initialize");
            Trace($"constructor:before_initialize projector={projectorType}");
            initialize?.Invoke();
            Trace($"constructor:after_initialize projector={projectorType}");

            if (TraceLifecycle)
            {
                var diagnosePing = instance.GetFunction<int>("diagnose_ping");
                if (diagnosePing is not null)
                {
                    Trace($"constructor:before_diagnose_ping projector={projectorType}");
                    var result = diagnosePing();
                    Trace($"constructor:after_diagnose_ping projector={projectorType} result={result}");
                }

                var diagnoseDomainTypes = instance.GetFunction<int>("diagnose_domain_types");
                if (diagnoseDomainTypes is not null)
                {
                    Trace($"constructor:before_diagnose_domain_types projector={projectorType}");
                    var result = diagnoseDomainTypes();
                    Trace($"constructor:after_diagnose_domain_types projector={projectorType} result={result}");
                }

                var diagnoseDeserializeKanyushaNumberInit = instance.GetFunction<int>("diagnose_deserialize_kanyusha_number_init");
                if (diagnoseDeserializeKanyushaNumberInit is not null)
                {
                    Trace($"constructor:before_diagnose_deserialize_kanyusha_number_init projector={projectorType}");
                    var result = diagnoseDeserializeKanyushaNumberInit();
                    Trace($"constructor:after_diagnose_deserialize_kanyusha_number_init projector={projectorType} result={result}");
                }

                var diagnoseProjectKanyushaNumberInit = instance.GetFunction<int>("diagnose_project_kanyusha_number_init");
                if (diagnoseProjectKanyushaNumberInit is not null)
                {
                    Trace($"constructor:before_diagnose_project_kanyusha_number_init projector={projectorType}");
                    var result = diagnoseProjectKanyushaNumberInit();
                    Trace($"constructor:after_diagnose_project_kanyusha_number_init projector={projectorType} result={result}");
                }

                var diagnoseEmptyKanyushaListQuery = instance.GetFunction<int>("diagnose_empty_kanyusha_list_query");
                if (diagnoseEmptyKanyushaListQuery is not null)
                {
                    Trace($"constructor:before_diagnose_empty_kanyusha_list_query projector={projectorType}");
                    var result = diagnoseEmptyKanyushaListQuery();
                    Trace($"constructor:after_diagnose_empty_kanyusha_list_query projector={projectorType} result={result}");
                }
            }

            _createInstance = instance.GetFunction<int, int, int>("create_instance");
            _applyEvent = instance.GetAction<int, int, int, int, int>("apply_event");
            _applyEventWithTags = instance.GetFunction("apply_event_with_tags");
            _beginPayloadBuffer = instance.GetAction<int>("begin_payload_buffer");
            _appendPayloadChunk = instance.GetAction<int, int, int>("append_payload_chunk");
            _applyBufferedEvent = instance.GetAction<int, int, int>("apply_buffered_event");
            _applyBufferedEventWithTags = instance.GetFunction("apply_buffered_event_with_tags");
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
            Trace($"constructor:before_create_instance projector={projectorType}");
            var instanceId = _createInstance(ptr, len);
            Trace($"constructor:after_create_instance projector={projectorType} instanceId={instanceId}");
            Free(ptr, len);
            if (instanceId < 0)
            {
                throw new InvalidOperationException($"create_instance failed with code {instanceId}");
            }
            _instanceId = instanceId;
            Trace($"constructor:completed projector={projectorType} instanceId={_instanceId}");
        }
    }

    public void ApplyEvent(
        string eventType,
        string eventPayloadJson,
        IReadOnlyList<string> tags,
        string? sortableUniqueId)
    {
        if (ShouldSkipEvent(eventType, tags))
        {
            return;
        }

        if (ShouldBypassGuestExecution(eventType))
        {
            Trace(
                $"apply_event:bypassed projectorInstance={_instanceId} eventType={eventType} reason=research_aot_workaround");
            return;
        }

        lock (_syncRoot)
        {
            ThrowIfDisposed();
            string effectivePayloadJson = TryCompactPayload(eventType, eventPayloadJson);
            var (eventTypePtr, eventTypeLen) = WriteString(eventType);
            try
            {
                Trace(
                    $"apply_event:start projectorInstance={_instanceId} eventType={eventType} tagCount={tags.Count} sortableUniqueId={sortableUniqueId ?? string.Empty} payloadLength={eventPayloadJson.Length} effectivePayloadLength={effectivePayloadJson.Length}");
                if (CanUseBufferedPayloadPath(tags.Count > 0))
                {
                    ApplyEventWithBufferedPayload(effectivePayloadJson, eventTypePtr, eventTypeLen, tags, sortableUniqueId);
                    Trace(
                        $"apply_event:completed projectorInstance={_instanceId} eventType={eventType} mode=buffered");
                }
                else
                {
                    var (payloadPtr, payloadLen) = WriteString(effectivePayloadJson);
                    try
                    {
                        if (_applyEventWithTags is not null)
                        {
                            var tagsJson = System.Text.Json.JsonSerializer.Serialize(tags ?? []);
                            var (tagsPtr, tagsLen) = WriteString(tagsJson);
                            var (sortablePtr, sortableLen) = WriteString(sortableUniqueId ?? string.Empty);
                            try
                            {
                                _applyEventWithTags.Invoke(
                                    _instanceId,
                                    eventTypePtr,
                                    eventTypeLen,
                                    payloadPtr,
                                    payloadLen,
                                    tagsPtr,
                                    tagsLen,
                                    sortablePtr,
                                    sortableLen);
                                Trace(
                                    $"apply_event:completed projectorInstance={_instanceId} eventType={eventType} mode=legacy_with_tags");
                            }
                            finally
                            {
                                Free(tagsPtr, tagsLen);
                                Free(sortablePtr, sortableLen);
                            }
                        }
                        else
                        {
                            EnsureExport(_applyEvent, nameof(_applyEvent));
                            _applyEvent!(_instanceId, eventTypePtr, eventTypeLen, payloadPtr, payloadLen);
                            Trace(
                                $"apply_event:completed projectorInstance={_instanceId} eventType={eventType} mode=legacy");
                        }
                    }
                    finally
                    {
                        Free(payloadPtr, payloadLen);
                    }
                }
            }
            finally
            {
                Free(eventTypePtr, eventTypeLen);
            }
        }
    }

    private bool CanUseBufferedPayloadPath(bool hasTags) =>
        _beginPayloadBuffer is not null &&
        _appendPayloadChunk is not null &&
        (hasTags
            ? _applyBufferedEventWithTags is not null
            : _applyBufferedEvent is not null);

    private bool ShouldSkipEvent(string eventType, IReadOnlyList<string> tags)
    {
        if (!string.Equals(_projectorType, "KanyushaListProjection", StringComparison.Ordinal))
        {
            return false;
        }

        return !tags.Any(IsKanyushaListRelevantTag);
    }

    private static bool IsKanyushaListRelevantTag(string tag) =>
        tag.StartsWith("Kanyusha:", StringComparison.Ordinal) ||
        tag.StartsWith("NendoKanyu:", StringComparison.Ordinal) ||
        tag.StartsWith("Keiyaku:", StringComparison.Ordinal) ||
        tag.StartsWith("KanyushaLogin:", StringComparison.Ordinal);

    private bool ShouldBypassGuestExecution(string eventType)
    {
        if (!string.Equals(_projectorType, "KanyushaListProjection", StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(eventType, "KanyushaAccountLoginCreatedAndPasswordChanged", StringComparison.Ordinal) ||
               string.Equals(eventType, "KanyushaLoginRecorded", StringComparison.Ordinal) ||
               string.Equals(eventType, "KanyushaEmailConfirmed", StringComparison.Ordinal) ||
               string.Equals(eventType, "ShozokuKenchikushikaiJohoSeted", StringComparison.Ordinal) ||
               string.Equals(eventType, "KanyushaJohoSeted", StringComparison.Ordinal) ||
               string.Equals(eventType, "TaDoushuruiHokenKeiyakuAriNashied", StringComparison.Ordinal) ||
               string.Equals(eventType, "TaDantaiKeizokuSeted", StringComparison.Ordinal) ||
               string.Equals(eventType, "TadantaiKeizokuJohoKoshin", StringComparison.Ordinal);
    }

    private static string TryCompactPayload(string eventType, string eventPayloadJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(eventPayloadJson, CompactPayloadJsonDocumentOptions);
            return eventType switch
            {
                "KanyushaWebServiceApplicationSubmitted" => WriteCompactApplicationSubmittedPayload(
                    document.RootElement,
                    isWebApplication: true),
                "KanyushaPaperApplicationSubmitted" => WriteCompactApplicationSubmittedPayload(
                    document.RootElement,
                    isWebApplication: false),
                "KeiyakuKakekinShisanCompleted" => WriteCompactKeiyakuKakekinShisanCompletedPayload(
                    document.RootElement),
                _ => eventPayloadJson
            };
        }
        catch
        {
            return eventPayloadJson;
        }
    }

    private static string WriteCompactApplicationSubmittedPayload(JsonElement root, bool isWebApplication)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            WriteValueObject(writer, root, "kanyushaNo", "value");
            WriteValueObject(writer, root, "hokenNendoId", "id");
            WriteValueObject(writer, root, "nendoKanyuId", "value");
            WriteValueObject(writer, root, "keiyakuId", "value");
            WriteStringProperty(writer, root, "applicationDate");
            WriteStringProperty(writer, root, isWebApplication ? "unconfirmedEmail" : "email");
            WriteShonendoTorokuDate(writer, root);
            WriteTotalKakekin(writer, root);

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteValueObject(Utf8JsonWriter writer, JsonElement root, string propertyName, string nestedPropertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement parent) ||
            parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(nestedPropertyName, out JsonElement nested))
        {
            return;
        }

        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();
        writer.WritePropertyName(nestedPropertyName);
        nested.WriteTo(writer);
        writer.WriteEndObject();
    }

    private static void WriteStringProperty(Utf8JsonWriter writer, JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        writer.WritePropertyName(propertyName);
        value.WriteTo(writer);
    }

    private static void WriteShonendoTorokuDate(Utf8JsonWriter writer, JsonElement root)
    {
        if (!TryGetNestedProperty(
                root,
                out JsonElement shonendoTorokuDate,
                "kakekinShisanJoho",
                "hosoku",
                "debugView",
                "ShonendoTorokuDate") ||
            shonendoTorokuDate.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        writer.WritePropertyName("kakekinShisanJoho");
        writer.WriteStartObject();
        writer.WritePropertyName("hosoku");
        writer.WriteStartObject();
        writer.WritePropertyName("debugView");
        writer.WriteStartObject();
        writer.WritePropertyName("ShonendoTorokuDate");
        writer.WriteStartObject();

        if (shonendoTorokuDate.TryGetProperty("hasValue", out JsonElement hasValue))
        {
            writer.WritePropertyName("hasValue");
            hasValue.WriteTo(writer);
        }

        if (shonendoTorokuDate.TryGetProperty("value", out JsonElement value) &&
            value.ValueKind != JsonValueKind.Null)
        {
            writer.WritePropertyName("value");
            value.WriteTo(writer);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteTotalKakekin(Utf8JsonWriter writer, JsonElement root)
    {
        if (!TryGetNestedProperty(
                root,
                out JsonElement totalKakekin,
                "kakekinKeisanResult",
                "kakekinLine",
                "TotalKakekin",
                "value"))
        {
            return;
        }

        writer.WritePropertyName("kakekinKeisanResult");
        writer.WriteStartObject();
        writer.WritePropertyName("kakekinLine");
        writer.WriteStartObject();
        writer.WritePropertyName("TotalKakekin");
        writer.WriteStartObject();
        writer.WritePropertyName("value");
        totalKakekin.WriteTo(writer);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static string WriteCompactKeiyakuKakekinShisanCompletedPayload(JsonElement root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            WriteValueObject(writer, root, "keiyakuId", "value");
            WriteValueObject(writer, root, "nendoKanyuId", "value");

            if (TryGetNestedProperty(
                    root,
                    out JsonElement totalKakekin,
                    "keisanResult",
                    "kakekinLine",
                    "TotalKakekin",
                    "value"))
            {
                writer.WritePropertyName("keisanResult");
                writer.WriteStartObject();
                writer.WritePropertyName("kakekinLine");
                writer.WriteStartObject();
                writer.WritePropertyName("TotalKakekin");
                writer.WriteStartObject();
                writer.WritePropertyName("value");
                totalKakekin.WriteTo(writer);
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool TryGetNestedProperty(
        JsonElement element,
        out JsonElement value,
        params string[] propertyPath)
    {
        value = element;
        foreach (string propertyName in propertyPath)
        {
            if (value.ValueKind != JsonValueKind.Object ||
                !value.TryGetProperty(propertyName, out JsonElement next))
            {
                value = default;
                return false;
            }

            value = next;
        }

        return true;
    }

    private void ApplyEventWithBufferedPayload(
        string eventPayloadJson,
        int eventTypePtr,
        int eventTypeLen,
        IReadOnlyList<string> tags,
        string? sortableUniqueId)
    {
        EnsureExport(_beginPayloadBuffer, nameof(_beginPayloadBuffer));
        EnsureExport(_appendPayloadChunk, nameof(_appendPayloadChunk));

        _beginPayloadBuffer!(_instanceId);

        byte[] payloadBytes = Encoding.UTF8.GetBytes(eventPayloadJson ?? string.Empty);
        int offset = 0;
        while (offset < payloadBytes.Length)
        {
            int chunkLength = Math.Min(BufferedPayloadChunkSize, payloadBytes.Length - offset);
            var (chunkPtr, writtenLength) = WriteBytes(payloadBytes.AsSpan(offset, chunkLength));
            try
            {
                _appendPayloadChunk!(_instanceId, chunkPtr, writtenLength);
            }
            finally
            {
                Free(chunkPtr, writtenLength);
            }

            offset += chunkLength;
        }

        if (_applyBufferedEventWithTags is not null)
        {
            var tagsJson = System.Text.Json.JsonSerializer.Serialize(tags ?? []);
            var (tagsPtr, tagsLen) = WriteString(tagsJson);
            var (sortablePtr, sortableLen) = WriteString(sortableUniqueId ?? string.Empty);
            try
            {
                _applyBufferedEventWithTags.Invoke(
                    _instanceId,
                    eventTypePtr,
                    eventTypeLen,
                    tagsPtr,
                    tagsLen,
                    sortablePtr,
                    sortableLen);
            }
            finally
            {
                Free(tagsPtr, tagsLen);
                Free(sortablePtr, sortableLen);
            }
        }
        else
        {
            EnsureExport(_applyBufferedEvent, nameof(_applyBufferedEvent));
            _applyBufferedEvent!(_instanceId, eventTypePtr, eventTypeLen);
        }
    }

    public string ExecuteQuery(string queryType, string queryParamsJson)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            EnsureExport(_executeQuery, nameof(_executeQuery));
            var (queryTypePtr, queryTypeLen) = WriteString(queryType);
            var (paramsPtr, paramsLen) = WriteString(queryParamsJson);

            var packed = _executeQuery!(_instanceId, queryTypePtr, queryTypeLen, paramsPtr, paramsLen);
            Free(queryTypePtr, queryTypeLen);
            Free(paramsPtr, paramsLen);

            return ReadPackedString(packed);
        }
    }

    public string ExecuteListQuery(string queryType, string queryParamsJson)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            EnsureExport(_executeListQuery, nameof(_executeListQuery));
            Trace($"execute_list_query:start projectorInstance={_instanceId} queryType={queryType}");
            var (queryTypePtr, queryTypeLen) = WriteString(queryType);
            var (paramsPtr, paramsLen) = WriteString(queryParamsJson);

            var packed = _executeListQuery!(_instanceId, queryTypePtr, queryTypeLen, paramsPtr, paramsLen);
            Free(queryTypePtr, queryTypeLen);
            Free(paramsPtr, paramsLen);
            Trace($"execute_list_query:completed projectorInstance={_instanceId} queryType={queryType}");

            return ReadPackedString(packed);
        }
    }

    public string SerializeState()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            if (_serializeState == null)
            {
                return "{}";
            }
            var packed = _serializeState(_instanceId);
            return ReadPackedString(packed);
        }
    }

    public void RestoreState(string stateJson)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            if (_restoreState == null)
            {
                return;
            }
            var (ptr, len) = WriteString(stateJson);
            _restoreState(_instanceId, ptr, len);
            Free(ptr, len);
        }
    }

    private (int ptr, int len) WriteBytes(ReadOnlySpan<byte> bytes)
    {
        var ptr = _alloc?.Invoke(bytes.Length) ?? 0;
        if (ptr == 0 || bytes.Length == 0)
        {
            return (0, 0);
        }

        bytes.CopyTo(_memory.GetSpan(ptr, bytes.Length));
        return (ptr, bytes.Length);
    }

    private (int ptr, int len) WriteString(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        return WriteBytes(bytes);
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

    private static void Trace(string message)
    {
        if (!TraceLifecycle)
        {
            return;
        }

        string line =
            $"[wasmtime-trace] {DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} {message}";
        Console.WriteLine(line);

        try
        {
            lock (TraceFileLock)
            {
                File.AppendAllText(TraceFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Debug trace must never affect runtime execution.
        }
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
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _store.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WasmtimePrimitiveProjectionInstance));
        }
    }
}
