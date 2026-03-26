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
    private const int BufferedPayloadThresholdBytes = 8 * 1024;
    private static readonly object TraceFileLock = new();
    private static readonly bool TraceLifecycle =
        string.Equals(
            Environment.GetEnvironmentVariable("WASM_RUNTIME_TRACE_LIFECYCLE"),
            "1",
            StringComparison.Ordinal);
    private static readonly string TraceFilePath =
        Environment.GetEnvironmentVariable("WASM_RUNTIME_TRACE_PATH")
        ?? Path.Combine(
            Path.GetTempPath(),
            $"kenbai-wasm-runtime-trace-{Environment.ProcessId}.log");

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
    private readonly Function? _applyBufferedEventWithSortable;
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
            _applyBufferedEventWithSortable = instance.GetFunction("apply_buffered_event_with_sortable");
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

        lock (_syncRoot)
        {
            ThrowIfDisposed();
            string effectivePayloadJson = TryCompactPayload(eventType, eventPayloadJson);
            var (eventTypePtr, eventTypeLen) = WriteString(eventType);
            try
            {
                Trace(
                    $"apply_event:start projectorInstance={_instanceId} eventType={eventType} tagCount={tags.Count} sortableUniqueId={sortableUniqueId ?? string.Empty} payloadLength={eventPayloadJson.Length} effectivePayloadLength={effectivePayloadJson.Length}");
                if (effectivePayloadJson.Length <= 512)
                {
                    Trace(
                        $"apply_event:payload projectorInstance={_instanceId} eventType={eventType} payload={effectivePayloadJson}");
                }
                if (CanUseBufferedPayloadPath(eventType, effectivePayloadJson.Length, tags.Count > 0))
                {
                    Trace(
                        $"apply_event:path projectorInstance={_instanceId} eventType={eventType} mode=buffered");
                    ApplyEventWithBufferedPayload(eventType, effectivePayloadJson, eventTypePtr, eventTypeLen, tags, sortableUniqueId);
                    Trace(
                        $"apply_event:completed projectorInstance={_instanceId} eventType={eventType} mode=buffered");
                }
                else
                {
                    Trace(
                        $"apply_event:path projectorInstance={_instanceId} eventType={eventType} mode=legacy");
                    var (payloadPtr, payloadLen) = WriteString(effectivePayloadJson);
                    try
                    {
                        if (ShouldUseLegacyApplyWithoutTags(eventType))
                        {
                            EnsureExport(_applyEvent, nameof(_applyEvent));
                            _applyEvent!(_instanceId, eventTypePtr, eventTypeLen, payloadPtr, payloadLen);
                            Trace(
                                $"apply_event:completed projectorInstance={_instanceId} eventType={eventType} mode=legacy_no_tags");
                        }
                        else if (_applyEventWithTags is not null)
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

    private bool CanUseBufferedPayloadPath(string eventType, int payloadLength, bool hasTags)
    {
        if (_beginPayloadBuffer is null || _appendPayloadChunk is null)
        {
            return false;
        }

        if (hasTags ? _applyBufferedEventWithTags is null : _applyBufferedEvent is null)
        {
            return false;
        }

        // These events still rely on the non-buffered/no-op path in the research branch.
        if (string.Equals(
                eventType,
                "KanyushaAccountLoginCreatedAndPasswordChanged",
                StringComparison.Ordinal) ||
            string.Equals(
                eventType,
                "OsusumeKekkaSet",
                StringComparison.Ordinal) ||
            string.Equals(
                eventType,
                "GyomuSaigaiSogoTokuyakuKakuninRecorded",
                StringComparison.Ordinal) ||
            string.Equals(
                eventType,
                "KeiyakuKakekinShisanCompleted",
                StringComparison.Ordinal))
        {
            return false;
        }

        return payloadLength > BufferedPayloadThresholdBytes || ShouldForceBufferedPayloadPath(eventType);
    }

    private bool ShouldSkipEvent(string eventType, IReadOnlyList<string> tags)
    {
        if (string.Equals(_projectorType, "KanyushaNumberKanriTagProjector", StringComparison.Ordinal))
        {
            return !string.Equals(
                       eventType,
                       "KanyushaNumberHaraidashiInitialized",
                       StringComparison.Ordinal) &&
                   !string.Equals(
                       eventType,
                       "KanyushaNumberHaraidashiSucceeded",
                       StringComparison.Ordinal);
        }

        if (string.Equals(_projectorType, "KanyushaListProjection", StringComparison.Ordinal))
        {
            return !tags.Any(IsKanyushaListRelevantTag) || !IsKanyushaListRelevantEvent(eventType);
        }

        if (string.Equals(_projectorType, "HokenNendoShosaiListProjection", StringComparison.Ordinal))
        {
            if (!tags.Any(IsHokenNendoShosaiListRelevantTag))
            {
                return true;
            }

            return !string.Equals(
                       eventType,
                       "HokenNendoShosaiRegistered",
                       StringComparison.Ordinal) &&
                   !string.Equals(
                       eventType,
                       "HokenNendoShosaiReceptionPeriodUpdated",
                       StringComparison.Ordinal) &&
                   !string.Equals(
                       eventType,
                       "HokenNendoShosaiPaymentScheduleUpdated",
                       StringComparison.Ordinal) &&
                   !string.Equals(
                       eventType,
                       "HokenNendoShosaiTokuyakuShokenNoUpdated",
                       StringComparison.Ordinal) &&
                   !string.Equals(
                       eventType,
                       "HokenNendoShosaiHokenShosaisUpdated",
                       StringComparison.Ordinal);
        }

        return false;
    }

    private static bool IsKanyushaListRelevantTag(string tag) =>
        tag.StartsWith("Kanyusha:", StringComparison.Ordinal) ||
        tag.StartsWith("NendoKanyu:", StringComparison.Ordinal) ||
        tag.StartsWith("Keiyaku:", StringComparison.Ordinal) ||
        tag.StartsWith("KanyushaLogin:", StringComparison.Ordinal);

    private static bool ShouldForceBufferedPayloadPath(string eventType) =>
        eventType switch
        {
            "KanyushaImported" => true,
            "NendoKanyuImported" => true,
            "KanyushaWebServiceApplicationSubmitted" => true,
            "KanyushaPaperApplicationSubmitted" => true,
            "KanyushaJohoSeted" => true,
            "KanyushaJohoByKanriSiteUpdated" => true,
            _ => false
        };

    private static bool ShouldUseLegacyApplyWithoutTags(string eventType) =>
        eventType switch
        {
            "KanyushaSakujoed" => true,
            "NendoKanyuSakujoed" => true,
            "KeiyakuImported" => true,
            "KeiyakuReplaced" => true,
            "KeiyakuKakekinShisanCompleted" => true,
            "CpdNoSeted" => true,
            "FurikaeKozaJohoSeted" => true,
            "KihonShiharaiMethodSeted" => true,
            "KonnendoShiharaiMethodSeted" => true,
            "ShozokuKenchikushikaiJohoByKanriSiteUpdated" => true,
            "KenchikushikaiSaikakuninKanryoed" => true,
            "KanyushaAccountLoginCreateRequested" => true,
            "KanyushaAccountLoginCreatedTokenIssued" => true,
            "KanyushaLoginRecorded" => true,
            "BunshoInsatsuKirokued" => true,
            "KanyushaNumberLoginCreated" => true,
            "KanyushaNumberLoginRequested" => true,
            _ => false
        };

    private static bool ShouldBypassTagsForSpecialCaseEvent(string eventType) =>
        eventType switch
        {
            "KanyushaImported" => true,
            "KanyushaSakujoed" => true,
            "NendoKanyuImported" => true,
            "KanyushaWebServiceApplicationSubmitted" => true,
            "KanyushaPaperApplicationSubmitted" => true,
            "KanyushaJohoSeted" => true,
            "KanyushaJohoByKanriSiteUpdated" => true,
            "KeiyakuImported" => true,
            _ => false
        };

    private static bool IsKanyushaListRelevantEvent(string eventType) =>
        eventType switch
        {
            "KanyushaImported" => true,
            "KanyushaWebServiceApplicationSubmitted" => true,
            "KanyushaPaperApplicationSubmitted" => true,
            "KanyushaNoteKoshin" => true,
            "KenchikushikaiKakuninConfirmed" => true,
            "KenchikushikaiSaikakuninKanryoed" => true,
            "KenchikushikaiSaikakuninShippaied" => true,
            "KanyushaEmailChanged" => true,
            "KanyushaEmailChangedByKanri" => true,
            "KanyushaEmailChangeConfirmed" => true,
            "KanyushaEmailConfirmed" => true,
            "KanyushaAccountLoginCreatedTokenIssued" => true,
            "KanyushaNumberLoginCreated" => true,
            "KanyushaAccountLoginCreatedAndPasswordChanged" => true,
            "KanyushaLoginMigratedToWeb" => true,
            "KanyushaJotaiToHaigyogoKeizokuChanged" => true,
            "KanyushaJotaiToHaitokuChanged" => true,
            "KanyushaJotaiToHikeizokuUketsukeChanged" => true,
            "KanyushaJotaiFromHikeizokuUketsukeCanceled" => true,
            "HaigyogoTokuyakuJohoUpdated" => true,
            "KanyuMoushikomiTorikeshied" => true,
            "KeiyakuKaiyakuChutoed" => true,
            "TadantaiKeizokuJohoKoshin" => true,
            "TaDantaiKeizokuSeted" => true,
            "KanyushaLoginRecorded" => true,
            "KanyushaSakujoed" => true,
            "NendoKanyuImported" => true,
            "NendoKoshinKaishi" => true,
            "KanyushaJohoSeted" => true,
            "KanyushaJohoByKanriSiteUpdated" => true,
            "ShozokuKenchikushikaiJohoSeted" => true,
            "ShozokuKenchikushikaiJohoByKanriSiteUpdated" => true,
            "ShozokuKenchikushikaiIsekied" => true,
            "MoushikomiMethodCodeSeted" => true,
            "CpdNoSeted" => true,
            "TaDoushuruiHokenKeiyakuAriAried" => true,
            "TaDoushuruiHokenKeiyakuAriNashied" => true,
            "FurikomiIraiKakunined" => true,
            "NendoReminderTantoshaSeted" => true,
            "NendoReminderTantoshaCleared" => true,
            "ReminderMemoUpdated" => true,
            "FurikaeKozaJohoSeted" => true,
            "FurikaeKozaJohoCleared" => true,
            "FurikaeKozaKakunined" => true,
            "KihonShiharaiMethodSeted" => true,
            "KonnendoShiharaiMethodSeted" => true,
            "NyukinTorokuked" => true,
            "NyukinKabusokuChoseied" => true,
            "HenreikinShiharaiTorokued" => true,
            "GyomuSaigaiSogoTokuyakuKakuninRecorded" => true,
            "GyomuSaigaiSogoTokuyakuKakuninDeleted" => true,
            "KanyujiKakunined" => true,
            "KenchikushiKakunined" => true,
            "NendoKanyuConfirmed" => true,
            "BunshoInsatsuKirokued" => true,
            "KanyushaShoInsatsuLogDeleted" => true,
            "NendoKeiyakuMitsumoriMitsumoriTekiyo" => true,
            "NendoKanyuSakujoed" => true,
            "KeiyakuImported" => true,
            "KeiyakuKakekinShisanCompleted" => true,
            "KeiyakuKakuninZumiNiIko" => true,
            "KeiyakuKaiyakuManryoed" => true,
            _ => false
        };

    private static bool IsHokenNendoShosaiListRelevantTag(string tag) =>
        tag.StartsWith("HokenNendoShosai:", StringComparison.Ordinal);

    private static string TryCompactPayload(string eventType, string eventPayloadJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(eventPayloadJson, CompactPayloadJsonDocumentOptions);
            return eventType switch
            {
                "KanyushaImported" => WriteCompactKanyushaImportedPayload(document.RootElement),
                "NendoKanyuImported" => WriteCompactNendoKanyuImportedPayload(document.RootElement),
                "KanyushaWebServiceApplicationSubmitted" => WriteCompactApplicationSubmittedPayload(
                    document.RootElement,
                    isWebApplication: true),
                "KanyushaPaperApplicationSubmitted" => WriteCompactApplicationSubmittedPayload(
                    document.RootElement,
                    isWebApplication: false),
                "KanyushaJohoSeted" => WriteCompactKanyushaJohoPayload(document.RootElement),
                "KanyushaJohoByKanriSiteUpdated" => WriteCompactKanyushaJohoPayload(document.RootElement),
                "KeiyakuImported" => WriteCompactKeiyakuImportedPayload(document.RootElement),
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

    private static string WriteCompactKanyushaImportedPayload(JsonElement root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            if (root.TryGetProperty("kanyusha", out JsonElement kanyushaRoot) &&
                kanyushaRoot.ValueKind == JsonValueKind.Object)
            {
                writer.WritePropertyName("kanyusha");
                writer.WriteStartObject();
                WriteValueObject(writer, kanyushaRoot, "kanyushaNo", "value");

                if (TryGetNestedProperty(kanyushaRoot, out JsonElement emailValue, "emailAddress", "emailValue"))
                {
                    writer.WritePropertyName("emailAddress");
                    writer.WriteStartObject();
                    writer.WritePropertyName("emailValue");
                    emailValue.WriteTo(writer);
                    writer.WriteEndObject();
                }

                WriteExistingProperty(writer, kanyushaRoot, "shonendoTorokuDate");

                if (TryGetNestedProperty(kanyushaRoot, out JsonElement noteValue, "note", "value"))
                {
                    writer.WritePropertyName("note");
                    writer.WriteStartObject();
                    writer.WritePropertyName("value");
                    noteValue.WriteTo(writer);
                    writer.WriteEndObject();
                }

                if (TryGetNestedProperty(kanyushaRoot, out JsonElement kanyushaJotaiType, "kanyushaJotai", "$type"))
                {
                    writer.WritePropertyName("kanyushaJotai");
                    writer.WriteStartObject();
                    writer.WritePropertyName("$type");
                    kanyushaJotaiType.WriteTo(writer);
                    writer.WriteEndObject();
                }

                if (TryGetNestedProperty(kanyushaRoot, out JsonElement kenchikushikaiType, "kenchikushikaiKakunin", "$type"))
                {
                    writer.WritePropertyName("kenchikushikaiKakunin");
                    writer.WriteStartObject();
                    writer.WritePropertyName("$type");
                    kenchikushikaiType.WriteTo(writer);
                    writer.WriteEndObject();
                }

                if (TryGetNestedProperty(kanyushaRoot, out JsonElement tadantaiType, "tadantaiKeizokuJoho", "$type"))
                {
                    writer.WritePropertyName("tadantaiKeizokuJoho");
                    writer.WriteStartObject();
                    writer.WritePropertyName("$type");
                    tadantaiType.WriteTo(writer);

                    if (TryGetNestedProperty(kanyushaRoot, out JsonElement isTadantaiKeizoku, "tadantaiKeizokuJoho", "isTadantaiKeizoku"))
                    {
                        writer.WritePropertyName("isTadantaiKeizoku");
                        isTadantaiKeizoku.WriteTo(writer);
                    }

                    if (TryGetNestedProperty(kanyushaRoot, out JsonElement tadantaiMujikoJuryoDate, "tadantaiKeizokuJoho", "tadantaiMujikoJuryoDate"))
                    {
                        writer.WritePropertyName("tadantaiMujikoJuryoDate");
                        tadantaiMujikoJuryoDate.WriteTo(writer);
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string WriteCompactNendoKanyuImportedPayload(JsonElement root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            if (root.TryGetProperty("nendoKanyu", out JsonElement nendoKanyuRoot) &&
                nendoKanyuRoot.ValueKind == JsonValueKind.Object)
            {
                writer.WritePropertyName("nendoKanyu");
                writer.WriteStartObject();
                WriteValueObject(writer, nendoKanyuRoot, "nendoKanyuId", "value");
                WriteValueObject(writer, nendoKanyuRoot, "hokenNendoId", "id");
                WriteValueObject(writer, nendoKanyuRoot, "kanyushaNo", "value");

                if (nendoKanyuRoot.TryGetProperty("kanyuKubun", out JsonElement kanyuKubun) &&
                    kanyuKubun.ValueKind == JsonValueKind.Object)
                {
                    writer.WritePropertyName("kanyuKubun");
                    writer.WriteStartObject();
                    WriteExistingProperty(writer, kanyuKubun, "$type");
                    WriteExistingProperty(writer, kanyuKubun, "label");
                    writer.WriteEndObject();
                }

                if (nendoKanyuRoot.TryGetProperty("jotai", out JsonElement jotai) &&
                    jotai.ValueKind == JsonValueKind.Object)
                {
                    writer.WritePropertyName("jotai");
                    writer.WriteStartObject();
                    WriteExistingProperty(writer, jotai, "$type");
                    WriteValueObject(writer, jotai, "activeKeiyakuId", "value");
                    writer.WriteEndObject();
                }

                if (TryGetNestedProperty(
                        nendoKanyuRoot,
                        out JsonElement kihonShiharaiMethod,
                        "shiharaiMethodJotai",
                        "kihonShiharaiMethod",
                        "value"))
                {
                    writer.WritePropertyName("shiharaiMethodJotai");
                    writer.WriteStartObject();
                    writer.WritePropertyName("kihonShiharaiMethod");
                    writer.WriteStartObject();
                    writer.WritePropertyName("value");
                    kihonShiharaiMethod.WriteTo(writer);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }

                if (TryGetNestedProperty(nendoKanyuRoot, out JsonElement nyukinJotaiType, "nyukinJotai", "$type"))
                {
                    writer.WritePropertyName("nyukinJotai");
                    writer.WriteStartObject();
                    writer.WritePropertyName("$type");
                    nyukinJotaiType.WriteTo(writer);
                    writer.WriteEndObject();
                }

                if (nendoKanyuRoot.TryGetProperty("shozokuKenchikushikaiJoho", out JsonElement shozokuKenchikushikaiJoho) &&
                    shozokuKenchikushikaiJoho.ValueKind == JsonValueKind.Object)
                {
                    writer.WritePropertyName("shozokuKenchikushikaiJoho");
                    writer.WriteStartObject();
                    WriteExistingProperty(writer, shozokuKenchikushikaiJoho, "$type");
                    WriteValueObject(writer, shozokuKenchikushikaiJoho, "kenchikushikaiCode", "value");

                    if (TryGetNestedProperty(
                            shozokuKenchikushikaiJoho,
                            out JsonElement kaiinBangoShinkokuType,
                            "kaiinBangoShinkoku",
                            "$type"))
                    {
                        writer.WritePropertyName("kaiinBangoShinkoku");
                        writer.WriteStartObject();
                        writer.WritePropertyName("$type");
                        kaiinBangoShinkokuType.WriteTo(writer);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndObject();
                }

                if (TryGetNestedProperty(nendoKanyuRoot, out JsonElement kanyushaJohoHasValue, "kanyushaJoho", "hasValue") &&
                    kanyushaJohoHasValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    writer.WritePropertyName("kanyushaJoho");
                    writer.WriteStartObject();
                    writer.WritePropertyName("hasValue");
                    kanyushaJohoHasValue.WriteTo(writer);

                    if (kanyushaJohoHasValue.GetBoolean() &&
                        TryGetNestedProperty(nendoKanyuRoot, out JsonElement kanyushaJohoValue, "kanyushaJoho", "value") &&
                        kanyushaJohoValue.ValueKind == JsonValueKind.Object)
                    {
                        writer.WritePropertyName("value");
                        writer.WriteStartObject();
                        WriteExistingProperty(writer, kanyushaJohoValue, "jigyoshoMei");
                        WriteExistingProperty(writer, kanyushaJohoValue, "daihyoshaMei");
                        WriteExistingProperty(writer, kanyushaJohoValue, "kanyushaMataHaDaihyoshaKana");
                        WriteExistingProperty(writer, kanyushaJohoValue, "yubinBango");
                        WriteExistingProperty(writer, kanyushaJohoValue, "jusho");
                        WriteExistingProperty(writer, kanyushaJohoValue, "jushoFurigana");
                        WriteExistingProperty(writer, kanyushaJohoValue, "tel");
                        WriteExistingProperty(writer, kanyushaJohoValue, "fax", includeNull: true);
                        WriteExistingProperty(writer, kanyushaJohoValue, "tantoshaMei");
                        WriteExistingProperty(writer, kanyushaJohoValue, "nicchuRenrakusaki");
                        writer.WriteEndObject();
                    }

                    writer.WriteEndObject();
                }

                if (TryGetNestedProperty(nendoKanyuRoot, out JsonElement moushikomiMethodCode, "moushikomiMethodCode", "value", "value"))
                {
                    writer.WritePropertyName("moushikomiMethodCode");
                    writer.WriteStartObject();
                    writer.WritePropertyName("value");
                    writer.WriteStartObject();
                    writer.WritePropertyName("value");
                    moushikomiMethodCode.WriteTo(writer);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }

                if (TryGetNestedProperty(
                        nendoKanyuRoot,
                        out JsonElement taDoushuruiHokenKeiyakuAriType,
                        "taDoushuruiHokenKeiyakuAri",
                        "$type"))
                {
                    writer.WritePropertyName("taDoushuruiHokenKeiyakuAri");
                    writer.WriteStartObject();
                    writer.WritePropertyName("$type");
                    taDoushuruiHokenKeiyakuAriType.WriteTo(writer);
                    writer.WriteEndObject();
                }

                if (TryGetNestedProperty(
                        nendoKanyuRoot,
                        out JsonElement gyomuSaigaiSogoTokuyakuKakuninJotaiType,
                        "gyomuSaigaiSogoTokuyakuKakuninJotai",
                        "$type"))
                {
                    writer.WritePropertyName("gyomuSaigaiSogoTokuyakuKakuninJotai");
                    writer.WriteStartObject();
                    writer.WritePropertyName("$type");
                    gyomuSaigaiSogoTokuyakuKakuninJotaiType.WriteTo(writer);
                    writer.WriteEndObject();
                }

                if (nendoKanyuRoot.TryGetProperty("webMoushikomiKakuteiNichiji", out JsonElement webMoushikomiKakuteiNichiji) &&
                    webMoushikomiKakuteiNichiji.ValueKind == JsonValueKind.Object)
                {
                    writer.WritePropertyName("webMoushikomiKakuteiNichiji");
                    writer.WriteStartObject();
                    WriteExistingProperty(writer, webMoushikomiKakuteiNichiji, "hasValue");
                    WriteExistingProperty(writer, webMoushikomiKakuteiNichiji, "value", includeNull: true);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

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

    private static void WriteExistingProperty(
        Utf8JsonWriter writer,
        JsonElement root,
        string propertyName,
        bool includeNull = false)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            return;
        }

        if (!includeNull && value.ValueKind == JsonValueKind.Null)
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

    private static string WriteCompactKanyushaJohoPayload(JsonElement root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            WriteValueObject(writer, root, "kanyushaNo", "value");
            WriteValueObject(writer, root, "nendoKanyuId", "value");

            if (root.TryGetProperty("kanyushaJoho", out JsonElement kanyushaJoho) &&
                kanyushaJoho.ValueKind == JsonValueKind.Object)
            {
                writer.WritePropertyName("kanyushaJoho");
                writer.WriteStartObject();
                WriteExistingProperty(writer, kanyushaJoho, "jigyoshoMei");
                WriteExistingProperty(writer, kanyushaJoho, "daihyoshaMei");
                WriteExistingProperty(writer, kanyushaJoho, "kanyushaMataHaDaihyoshaKana");
                WriteExistingProperty(writer, kanyushaJoho, "yubinBango");
                WriteExistingProperty(writer, kanyushaJoho, "jusho");
                WriteExistingProperty(writer, kanyushaJoho, "jushoFurigana");
                WriteExistingProperty(writer, kanyushaJoho, "tel");
                WriteExistingProperty(writer, kanyushaJoho, "fax", includeNull: true);
                WriteExistingProperty(writer, kanyushaJoho, "tantoshaMei");
                WriteExistingProperty(writer, kanyushaJoho, "nicchuRenrakusaki");
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
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

    private static string WriteCompactKeiyakuImportedPayload(JsonElement root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            if (root.TryGetProperty("keiyaku", out JsonElement keiyakuRoot) &&
                keiyakuRoot.ValueKind == JsonValueKind.Object)
            {
                WriteValueObject(writer, keiyakuRoot, "keiyakuId", "value");
                WriteValueObject(writer, keiyakuRoot, "nendoKanyuId", "value");
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
        string eventType,
        string eventPayloadJson,
        int eventTypePtr,
        int eventTypeLen,
        IReadOnlyList<string> tags,
        string? sortableUniqueId)
    {
        EnsureExport(_beginPayloadBuffer, nameof(_beginPayloadBuffer));
        EnsureExport(_appendPayloadChunk, nameof(_appendPayloadChunk));

        Trace($"apply_buffered:start projectorInstance={_instanceId} payloadLength={eventPayloadJson.Length} tagCount={tags.Count}");
        _beginPayloadBuffer!(_instanceId);
        Trace($"apply_buffered:after_begin projectorInstance={_instanceId}");

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
        Trace($"apply_buffered:after_chunks projectorInstance={_instanceId} chunkBytes={payloadBytes.Length}");

        if (ShouldBypassTagsForSpecialCaseEvent(eventType) &&
            _applyBufferedEventWithSortable is not null)
        {
            var (sortablePtr, sortableLen) = WriteString(sortableUniqueId ?? string.Empty);
            try
            {
                Trace($"apply_buffered:before_invoke_with_sortable projectorInstance={_instanceId}");
                _applyBufferedEventWithSortable.Invoke(
                    _instanceId,
                    eventTypePtr,
                    eventTypeLen,
                    sortablePtr,
                    sortableLen);
                Trace($"apply_buffered:after_invoke_with_sortable projectorInstance={_instanceId}");
            }
            finally
            {
                Free(sortablePtr, sortableLen);
            }
        }
        else if (_applyBufferedEventWithTags is not null)
        {
            var tagsJson = System.Text.Json.JsonSerializer.Serialize(tags ?? []);
            var (tagsPtr, tagsLen) = WriteString(tagsJson);
            var (sortablePtr, sortableLen) = WriteString(sortableUniqueId ?? string.Empty);
            try
            {
                Trace($"apply_buffered:before_invoke_with_tags projectorInstance={_instanceId}");
                _applyBufferedEventWithTags.Invoke(
                    _instanceId,
                    eventTypePtr,
                    eventTypeLen,
                    tagsPtr,
                    tagsLen,
                    sortablePtr,
                    sortableLen);
                Trace($"apply_buffered:after_invoke_with_tags projectorInstance={_instanceId}");
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
            Trace($"apply_buffered:before_invoke projectorInstance={_instanceId}");
            _applyBufferedEvent!(_instanceId, eventTypePtr, eventTypeLen);
            Trace($"apply_buffered:after_invoke projectorInstance={_instanceId}");
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
        if (bytes.Length == 0)
        {
            return (0, 0);
        }

        var ptr = _alloc?.Invoke(bytes.Length) ?? 0;
        if (ptr == 0)
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
