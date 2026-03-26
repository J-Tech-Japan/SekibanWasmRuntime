using System.Collections;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.WasmRuntime;

public sealed class WasmProjectionActorHostFactory(
    IPrimitiveProjectionHost primitiveProjectionHost,
    WasmProjectorRegistry registry,
    JsonSerializerOptions jsonOptions,
    ILoggerFactory loggerFactory) : IProjectionActorHostFactory
{
    public IProjectionActorHost Create(
        string projectorName,
        GeneralMultiProjectionActorOptions? options = null,
        ILogger? logger = null) =>
        new WasmProjectionActorHost(
            primitiveProjectionHost,
            registry,
            jsonOptions,
            projectorName,
            logger ?? loggerFactory.CreateLogger<WasmProjectionActorHost>());
}

public sealed class WasmProjectionActorHost : IProjectionActorHost, IDisposable
{
    public sealed record WasmCheckpointState(
        string StateJson,
        int Version,
        string? LastSortableUniqueId,
        Guid? LastEventId,
        bool IsCatchedUp);

    private static readonly object TraceFileLock = new();
    private static readonly string HokenTraceFilePath =
        Path.Combine(Path.GetTempPath(), $"kenbai-wasm-hoken-{Environment.ProcessId}.log");
    private static readonly HashSet<string> SkippedEventTypes = LoadSkippedEventTypes();
    private static readonly Dictionary<string, HashSet<string>> AllowedEventTypesByProjector =
        LoadAllowedEventTypesByProjector();
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
    private static readonly HashSet<string> KanyushaListOrphanSkippableEventTypes =
        [
            "TaDoushuruiHokenKeiyakuAriAried",
            "TaDoushuruiHokenKeiyakuAriNashied",
            "TaDantaiKeizokuSeted",
            "FurikaeKozaJohoSeted",
            "KihonShiharaiMethodSeted",
            "KonnendoShiharaiMethodSeted",
            "CpdNoSeted",
            "FurikomiIraiKakunined",
            "NendoReminderTantoshaSeted",
            "NendoReminderTantoshaCleared",
            "ReminderMemoUpdated",
            "NyukinTorokuked",
            "NyukinKabusokuChoseied",
            "HenreikinShiharaiTorokued",
            "MoushikomiMethodCodeSeted",
            "KenchikushiKakunined",
            "KanyujiKakunined"
        ];

    private readonly IPrimitiveProjectionHost _host;
    private readonly WasmProjectorRegistry _registry;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger _logger;
    private readonly string _projectorName;
    private readonly string _projectorVersion;

    private IPrimitiveProjectionInstance? _instance;
    private int _version;
    private string? _lastSortableUniqueId;
    private Guid? _lastEventId;
    private bool _isCatchedUp = true;
    private string? _cachedSerializedState;
    private int _cachedSerializedStateVersion = -1;
    private HashSet<int> _kanyushaListKnownKanyushaNos = [];
    private Dictionary<string, int> _kanyushaListKnownNendoIndex = new(StringComparer.Ordinal);

    public WasmProjectionActorHost(
        IPrimitiveProjectionHost host,
        WasmProjectorRegistry registry,
        JsonSerializerOptions jsonOptions,
        string projectorName,
        ILogger logger)
    {
        _host = host;
        _registry = registry;
        _jsonOptions = jsonOptions;
        _projectorName = projectorName;
        _logger = logger;

        _projectorVersion = _registry.TryGet(projectorName)?.ProjectorVersion
            ?? throw new InvalidOperationException($"Projector not found: {projectorName}");
    }

    public Task AddSerializableEventsAsync(
        IReadOnlyList<SerializableEvent> events,
        bool finishedCatchUp = true)
    {
        var instance = EnsureInstance();
        foreach (var serializedEvent in events)
        {
            bool traceHokenProjection = string.Equals(
                _projectorName,
                "HokenNendoShosaiListProjection",
                StringComparison.Ordinal);
            Trace(
                $"host_add_events:start projector={_projectorName} eventId={serializedEvent.Id} eventType={serializedEvent.EventPayloadName} sortableUniqueId={serializedEvent.SortableUniqueIdValue} tagCount={serializedEvent.Tags.Count}");
            if (ShouldSkipEvent(serializedEvent.EventPayloadName, serializedEvent.Tags))
            {
                Trace(
                    $"host_add_events:skipped projector={_projectorName} eventId={serializedEvent.Id} eventType={serializedEvent.EventPayloadName} sortableUniqueId={serializedEvent.SortableUniqueIdValue}");
            }
            else
            {
                var payloadJson = Encoding.UTF8.GetString(serializedEvent.Payload);
                if (ShouldSkipOrphanKanyushaListEvent(serializedEvent.EventPayloadName, payloadJson))
                {
                    Trace(
                        $"host_add_events:skipped_orphan projector={_projectorName} eventId={serializedEvent.Id} eventType={serializedEvent.EventPayloadName} sortableUniqueId={serializedEvent.SortableUniqueIdValue}");
                }
                else
                {
                    if (traceHokenProjection)
                    {
                        WriteHokenTraceLine(
                            $"start projector={_projectorName} eventType={serializedEvent.EventPayloadName} eventId={serializedEvent.Id} sortableUniqueId={serializedEvent.SortableUniqueIdValue} tagCount={serializedEvent.Tags.Count}");
                        _logger.LogWarning(
                            "WASM HokenNendo event apply start: Projector={ProjectorName}, EventType={EventType}, EventId={EventId}, SortableUniqueId={SortableUniqueId}, TagCount={TagCount}",
                            _projectorName,
                            serializedEvent.EventPayloadName,
                            serializedEvent.Id,
                            serializedEvent.SortableUniqueIdValue,
                            serializedEvent.Tags.Count);
                    }

                    instance.ApplyEvent(
                        serializedEvent.EventPayloadName,
                        payloadJson,
                        serializedEvent.Tags,
                        serializedEvent.SortableUniqueIdValue);

                    if (traceHokenProjection)
                    {
                        WriteHokenTraceLine(
                            $"completed projector={_projectorName} eventType={serializedEvent.EventPayloadName} eventId={serializedEvent.Id} sortableUniqueId={serializedEvent.SortableUniqueIdValue}");
                        _logger.LogWarning(
                            "WASM HokenNendo event apply completed: Projector={ProjectorName}, EventType={EventType}, EventId={EventId}, SortableUniqueId={SortableUniqueId}",
                            _projectorName,
                            serializedEvent.EventPayloadName,
                            serializedEvent.Id,
                            serializedEvent.SortableUniqueIdValue);
                    }

                    Trace(
                        $"host_add_events:completed projector={_projectorName} eventId={serializedEvent.Id} eventType={serializedEvent.EventPayloadName} sortableUniqueId={serializedEvent.SortableUniqueIdValue}");
                }
            }

            _version++;
            InvalidateSerializedStateCache();
            _lastSortableUniqueId = serializedEvent.SortableUniqueIdValue;
            _lastEventId = serializedEvent.Id;
        }

        _isCatchedUp = finishedCatchUp;
        return Task.CompletedTask;
    }

    public Task<ResultBox<ProjectionStateMetadata>> GetStateMetadataAsync(bool includeUnsafe = true) =>
        Task.FromResult(ResultBox.FromValue(new ProjectionStateMetadata(
            ProjectorName: _projectorName,
            ProjectorVersion: _projectorVersion,
            IsCatchedUp: _isCatchedUp,
            UnsafeVersion: _version,
            UnsafeLastSortableUniqueId: _lastSortableUniqueId,
            UnsafeLastEventId: _lastEventId,
            SafeVersion: _version,
            SafeLastSortableUniqueId: _lastSortableUniqueId)));

    public Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true)
    {
        var payload = new WasmProjectionPayload(_projectorName, PeekSerializedState());
        return Task.FromResult(ResultBox.FromValue(new MultiProjectionState(
            Payload: payload,
            ProjectorName: _projectorName,
            ProjectorVersion: _projectorVersion,
            LastSortableUniqueId: _lastSortableUniqueId ?? string.Empty,
            LastEventId: _lastEventId ?? Guid.Empty,
            Version: _version,
            IsCatchedUp: _isCatchedUp,
            IsSafeState: true)));
    }

    public async Task<ResultBox<bool>> WriteSnapshotToStreamAsync(
        Stream target,
        bool canGetUnsafeState,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await CreateSnapshotAsync();
            var snapshotBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, _jsonOptions);
            var inlineState = SerializableMultiProjectionState.FromBytes(
                payload: snapshotBytes,
                multiProjectionPayloadType: typeof(WasmStateSnapshot).FullName ?? nameof(WasmStateSnapshot),
                projectorName: _projectorName,
                projectorVersion: _projectorVersion,
                lastSortableUniqueId: _lastSortableUniqueId ?? string.Empty,
                lastEventId: _lastEventId ?? Guid.Empty,
                version: _version,
                isCatchedUp: _isCatchedUp,
                isSafeState: true,
                originalSizeBytes: snapshotBytes.LongLength,
                compressedSizeBytes: snapshotBytes.LongLength);
            var envelope = new SerializableMultiProjectionStateEnvelope(false, inlineState, null);

            await JsonSerializer.SerializeAsync(target, envelope, _jsonOptions, cancellationToken);
            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    public async Task<ResultBox<bool>> RestoreSnapshotFromStreamAsync(Stream source, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await DeserializeEnvelopeAsync(source, cancellationToken);
            var inlineState = envelope?.InlineState
                ?? throw new InvalidOperationException("Snapshot stream does not contain an inline state.");
            var snapshot = JsonSerializer.Deserialize<WasmStateSnapshot>(inlineState.GetPayloadBytes(), _jsonOptions)
                ?? throw new InvalidOperationException("Snapshot stream did not contain a valid WASM state snapshot.");

            string stateJson = await ReadSnapshotStateJsonAsync(snapshot);
            EnsureInstance().RestoreState(stateJson);
            _version = snapshot.UnsafeVersion;
            _lastSortableUniqueId = snapshot.LastSortableUniqueId;
            _lastEventId = snapshot.LastEventId;
            _isCatchedUp = true;
            SetSerializedStateCache(stateJson);
            RefreshKanyushaListShadowState(stateJson);

            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    public async Task<ResultBox<SerializableQueryResult>> ExecuteQueryAsync(
        SerializableQueryParameter query,
        int? safeVersion,
        string? safeThreshold,
        DateTime? safeThresholdTime,
        int? unsafeVersion)
    {
        try
        {
            var queryJson = await DecompressToStringAsync(query.CompressedQueryJson);
            var resultJson = EnsureInstance().ExecuteQuery(query.QueryTypeName, queryJson);

            return ResultBox.FromValue(new SerializableQueryResult
            {
                ResultTypeName = string.Empty,
                QueryTypeName = query.QueryTypeName,
                CompressedResultJson = await CompressStringAsync(resultJson),
                CompressedQueryJson = query.CompressedQueryJson,
                ResultAssemblyVersion = string.Empty
            });
        }
        catch (Exception ex)
        {
            return ResultBox.Error<SerializableQueryResult>(ex);
        }
    }

    public async Task<ResultBox<SerializableListQueryResult>> ExecuteListQueryAsync(
        SerializableQueryParameter query,
        int? safeVersion,
        string? safeThreshold,
        DateTime? safeThresholdTime,
        int? unsafeVersion)
    {
        try
        {
            var queryJson = await DecompressToStringAsync(query.CompressedQueryJson);
            var resultJson = EnsureInstance().ExecuteListQuery(query.QueryTypeName, queryJson);
            var parsed = ParseListQueryResult(resultJson);

            return ResultBox.FromValue(new SerializableListQueryResult
            {
                TotalCount = parsed.TotalCount,
                TotalPages = parsed.TotalPages,
                CurrentPage = parsed.CurrentPage,
                PageSize = parsed.PageSize,
                RecordTypeName = string.Empty,
                QueryTypeName = query.QueryTypeName,
                CompressedItemsJson = await CompressStringAsync(parsed.ItemsJson),
                CompressedQueryJson = query.CompressedQueryJson,
                ItemsAssemblyVersion = string.Empty
            });
        }
        catch (Exception ex)
        {
            return ResultBox.Error<SerializableListQueryResult>(ex);
        }
    }

    public void ForcePromoteBufferedEvents()
    {
    }

    public void CompactSafeHistory()
    {
        // WASM runtime keeps only the current safe state in memory, so there is no
        // independent safe-history buffer to compact here.
    }

    public void ForcePromoteAllBufferedEvents()
    {
    }

    public Task<string> GetSafeLastSortableUniqueIdAsync() =>
        Task.FromResult(_lastSortableUniqueId ?? string.Empty);

    public Task<bool> IsSortableUniqueIdReceivedAsync(string sortableUniqueId)
    {
        if (string.IsNullOrWhiteSpace(sortableUniqueId) || string.IsNullOrWhiteSpace(_lastSortableUniqueId))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(string.CompareOrdinal(sortableUniqueId, _lastSortableUniqueId) <= 0);
    }

    public long EstimateStateSizeBytes(bool includeUnsafeDetails) =>
        Encoding.UTF8.GetByteCount(PeekSerializedState());

    public string PeekCurrentSafeWindowThreshold() => _lastSortableUniqueId ?? string.Empty;

    public string GetProjectorVersion() => _projectorVersion;

    public async Task<ResultBox<bool>> RewriteSnapshotVersionAsync(
        Stream source,
        Stream target,
        string newVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await DeserializeEnvelopeAsync(source, cancellationToken);
            var inlineState = envelope?.InlineState
                ?? throw new InvalidOperationException("Snapshot stream does not contain an inline state.");
            var snapshot = JsonSerializer.Deserialize<WasmStateSnapshot>(inlineState.GetPayloadBytes(), _jsonOptions)
                ?? throw new InvalidOperationException("Snapshot stream did not contain a valid WASM state snapshot.");
            var rewritten = snapshot with { ProjectorVersion = newVersion };
            var rewrittenBytes = JsonSerializer.SerializeToUtf8Bytes(rewritten, _jsonOptions);
            var rewrittenInlineState = SerializableMultiProjectionState.FromBytes(
                payload: rewrittenBytes,
                multiProjectionPayloadType: inlineState.MultiProjectionPayloadType,
                projectorName: inlineState.ProjectorName,
                projectorVersion: newVersion,
                lastSortableUniqueId: inlineState.LastSortableUniqueId,
                lastEventId: inlineState.LastEventId,
                version: inlineState.Version,
                isCatchedUp: inlineState.IsCatchedUp,
                isSafeState: inlineState.IsSafeState,
                originalSizeBytes: rewrittenBytes.LongLength,
                compressedSizeBytes: rewrittenBytes.LongLength);

            await JsonSerializer.SerializeAsync(
                target,
                new SerializableMultiProjectionStateEnvelope(false, rewrittenInlineState, null),
                _jsonOptions,
                cancellationToken);

            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    public WasmCheckpointState ExportCheckpointState() =>
        new(
            PeekSerializedState(),
            _version,
            _lastSortableUniqueId,
            _lastEventId,
            _isCatchedUp);

    public void RestoreCheckpointState(WasmCheckpointState checkpoint)
    {
        EnsureInstance().RestoreState(checkpoint.StateJson);
        _version = checkpoint.Version;
        _lastSortableUniqueId = checkpoint.LastSortableUniqueId;
        _lastEventId = checkpoint.LastEventId;
        _isCatchedUp = checkpoint.IsCatchedUp;
        SetSerializedStateCache(checkpoint.StateJson);
        RefreshKanyushaListShadowState(checkpoint.StateJson);
    }

    public void Dispose()
    {
        _instance?.Dispose();
        _instance = null;
    }

    private IPrimitiveProjectionInstance EnsureInstance()
    {
        if (_instance is not null)
        {
            return _instance;
        }

        _instance = _host.CreateInstance(_projectorName);
        _logger.LogDebug("Created WASM projection instance for {ProjectorName}", _projectorName);
        return _instance;
    }

    private bool ShouldSkipEvent(string eventPayloadName, IReadOnlyList<string>? tags)
    {
        if (string.IsNullOrWhiteSpace(eventPayloadName))
        {
            return false;
        }

        if (SkippedEventTypes.Contains(eventPayloadName))
        {
            return true;
        }

        if (AllowedEventTypesByProjector.TryGetValue(_projectorName, out HashSet<string>? allowedEventTypes) &&
            !allowedEventTypes.Contains(eventPayloadName))
        {
            return true;
        }

        if (string.Equals(_projectorName, "KanyushaListProjection", StringComparison.Ordinal))
        {
            return !HasKanyushaListRelevantTags(tags) || !IsKanyushaListRelevantEvent(eventPayloadName);
        }

        if (string.Equals(_projectorName, "HokenNendoShosaiListProjection", StringComparison.Ordinal))
        {
            return !HasHokenNendoShosaiListRelevantTags(tags) || !IsHokenNendoShosaiListRelevantEvent(eventPayloadName);
        }

        return false;
    }

    private static bool HasKanyushaListRelevantTags(IReadOnlyList<string>? tags) =>
        tags?.Any(tag =>
            tag.StartsWith("Kanyusha:", StringComparison.Ordinal) ||
            tag.StartsWith("NendoKanyu:", StringComparison.Ordinal) ||
            tag.StartsWith("Keiyaku:", StringComparison.Ordinal) ||
            tag.StartsWith("KanyushaLogin:", StringComparison.Ordinal)) == true;

    private static bool HasHokenNendoShosaiListRelevantTags(IReadOnlyList<string>? tags) =>
        tags?.Any(tag => tag.StartsWith("HokenNendoShosai:", StringComparison.Ordinal)) == true;

    private static bool IsHokenNendoShosaiListRelevantEvent(string eventPayloadName) =>
        string.Equals(eventPayloadName, "HokenNendoShosaiRegistered", StringComparison.Ordinal) ||
        string.Equals(eventPayloadName, "HokenNendoShosaiReceptionPeriodUpdated", StringComparison.Ordinal) ||
        string.Equals(eventPayloadName, "HokenNendoShosaiPaymentScheduleUpdated", StringComparison.Ordinal) ||
        string.Equals(eventPayloadName, "HokenNendoShosaiTokuyakuShokenNoUpdated", StringComparison.Ordinal) ||
        string.Equals(eventPayloadName, "HokenNendoShosaiHokenShosaisUpdated", StringComparison.Ordinal);

    private static bool IsKanyushaListRelevantEvent(string eventPayloadName) =>
        eventPayloadName switch
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

    private static HashSet<string> LoadSkippedEventTypes()
    {
        string? raw = Environment.GetEnvironmentVariable("WASM_RUNTIME_SKIP_EVENT_TYPES");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static Dictionary<string, HashSet<string>> LoadAllowedEventTypesByProjector()
    {
        const string prefix = "WASM_RUNTIME_ALLOWED_EVENT_TYPES__";
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is not string key ||
                !key.StartsWith(prefix, StringComparison.Ordinal) ||
                entry.Value is not string raw ||
                string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string projectorName = key[prefix.Length..];
            result[projectorName] = raw
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.Ordinal);
        }

        return result;
    }

    private string PeekSerializedState()
    {
        if (_cachedSerializedState is not null && _cachedSerializedStateVersion == _version)
        {
            return _cachedSerializedState;
        }

        if (_instance is null)
        {
            return "{}";
        }

        string stateJson = _instance.SerializeState();
        SetSerializedStateCache(stateJson);
        return stateJson;
    }

    private async Task<WasmStateSnapshot> CreateSnapshotAsync()
    {
        string stateJson = PeekSerializedState();
        byte[] compressedStateJson = await CompressStringAsync(stateJson);

        return new WasmStateSnapshot(
            StateJson: null,
            SafeVersion: _version,
            UnsafeVersion: _version,
            SafeLastSortableUniqueId: _lastSortableUniqueId,
            LastSortableUniqueId: _lastSortableUniqueId,
            LastEventId: _lastEventId,
            ProjectorVersion: _projectorVersion,
            TagProjector: _projectorName,
            CompressedStateJson: compressedStateJson);
    }

    private static async Task<string> ReadSnapshotStateJsonAsync(WasmStateSnapshot snapshot)
    {
        if (snapshot.CompressedStateJson is { Length: > 0 } compressedStateJson)
        {
            return await DecompressToStringAsync(compressedStateJson);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.StateJson))
        {
            return snapshot.StateJson;
        }

        throw new InvalidOperationException("WASM snapshot does not contain any serialized state payload.");
    }

    private bool ShouldSkipOrphanKanyushaListEvent(string eventType, string payloadJson)
    {
        if (!string.Equals(_projectorName, "KanyushaListProjection", StringComparison.Ordinal) ||
            !KanyushaListOrphanSkippableEventTypes.Contains(eventType) ||
            !TryReadNendoScopedReference(payloadJson, out string? nendoKanyuId, out int? kanyushaNo))
        {
            return false;
        }

        bool knownByNendo = !string.IsNullOrWhiteSpace(nendoKanyuId) &&
            _kanyushaListKnownNendoIndex.ContainsKey(nendoKanyuId);
        bool knownByKanyusha = kanyushaNo is not null && _kanyushaListKnownKanyushaNos.Contains(kanyushaNo.Value);

        return !knownByNendo && !knownByKanyusha;
    }

    private void RefreshKanyushaListShadowState(string stateJson)
    {
        _kanyushaListKnownKanyushaNos.Clear();
        _kanyushaListKnownNendoIndex.Clear();

        if (!string.Equals(_projectorName, "KanyushaListProjection", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(stateJson) ||
            stateJson == "{}")
        {
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(stateJson);
            JsonElement root = document.RootElement;
            JsonElement projectionRoot = root.TryGetProperty("Projection", out JsonElement projection)
                ? projection
                : root;

            if (projectionRoot.TryGetProperty("KanyushaItems", out JsonElement kanyushaItems) &&
                kanyushaItems.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in kanyushaItems.EnumerateObject())
                {
                    if (int.TryParse(property.Name, out int kanyushaNo))
                    {
                        _kanyushaListKnownKanyushaNos.Add(kanyushaNo);
                    }
                }
            }

            if (root.TryGetProperty("ReverseIndex", out JsonElement reverseIndex) &&
                reverseIndex.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in reverseIndex.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Number &&
                        property.Value.TryGetInt32(out int kanyushaNo))
                    {
                        _kanyushaListKnownNendoIndex[property.Name] = kanyushaNo;
                    }
                }
            }
        }
        catch
        {
            _kanyushaListKnownKanyushaNos.Clear();
            _kanyushaListKnownNendoIndex.Clear();
        }
    }

    private static bool TryReadNendoScopedReference(string payloadJson, out string? nendoKanyuId, out int? kanyushaNo)
    {
        nendoKanyuId = null;
        kanyushaNo = null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(payloadJson);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("nendoKanyuId", out JsonElement nendoKanyuIdElement) &&
                nendoKanyuIdElement.ValueKind == JsonValueKind.Object &&
                nendoKanyuIdElement.TryGetProperty("value", out JsonElement nendoKanyuIdValue) &&
                nendoKanyuIdValue.ValueKind == JsonValueKind.String)
            {
                nendoKanyuId = nendoKanyuIdValue.GetString();
            }

            if (root.TryGetProperty("kanyushaNo", out JsonElement kanyushaNoElement) &&
                kanyushaNoElement.ValueKind == JsonValueKind.Object &&
                kanyushaNoElement.TryGetProperty("value", out JsonElement kanyushaNoValue) &&
                kanyushaNoValue.TryGetInt32(out int parsedKanyushaNo))
            {
                kanyushaNo = parsedKanyushaNo;
            }

            return !string.IsNullOrWhiteSpace(nendoKanyuId) || kanyushaNo is not null;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<byte[]> CompressStringAsync(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        await using var output = new MemoryStream();
        await using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            await gzip.WriteAsync(bytes);
        }

        return output.ToArray();
    }

    private static async Task<string> DecompressToStringAsync(byte[] compressedData)
    {
        if (compressedData.Length == 0)
        {
            return "{}";
        }

        await using var input = new MemoryStream(compressedData);
        await using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private void InvalidateSerializedStateCache()
    {
        _cachedSerializedState = null;
        _cachedSerializedStateVersion = -1;
    }

    private void SetSerializedStateCache(string stateJson)
    {
        _cachedSerializedState = stateJson;
        _cachedSerializedStateVersion = _version;
    }

    private async Task<SerializableMultiProjectionStateEnvelope?> DeserializeEnvelopeAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        var data = await StreamReadHelper.ReadAllBytesAsync(source, cancellationToken);
        if (data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b)
        {
            var jsonBytes = GzipCompression.Decompress(data);
            return JsonSerializer.Deserialize<SerializableMultiProjectionStateEnvelope>(jsonBytes, _jsonOptions);
        }

        return JsonSerializer.Deserialize<SerializableMultiProjectionStateEnvelope>(data, _jsonOptions);
    }

    internal static WasmListQueryResult ParseListQueryResult(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return new WasmListQueryResult(document.RootElement.GetRawText(), null, null, null, null);
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("List query result must be a JSON array or object.");
        }

        var itemsElement = document.RootElement.TryGetProperty("items", out var camelItems)
            ? camelItems
            : document.RootElement.TryGetProperty("Items", out var pascalItems)
                ? pascalItems
                : default;

        if (itemsElement.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException("List query result object must contain an items property.");
        }

        return new WasmListQueryResult(
            ItemsJson: itemsElement.GetRawText(),
            TotalCount: TryReadInt(document.RootElement, "totalCount", "TotalCount"),
            TotalPages: TryReadInt(document.RootElement, "totalPages", "TotalPages"),
            CurrentPage: TryReadInt(document.RootElement, "currentPage", "CurrentPage"),
            PageSize: TryReadInt(document.RootElement, "pageSize", "PageSize"));
    }

    private static int? TryReadInt(JsonElement element, string camelName, string pascalName)
    {
        if (!element.TryGetProperty(camelName, out var property) &&
            !element.TryGetProperty(pascalName, out property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static void Trace(string message)
    {
        if (!TraceLifecycle)
        {
            return;
        }

        string line =
            $"[wasm-host-trace] {DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} {message}";
        try
        {
            Console.WriteLine(line);
        }
        catch
        {
            // Debug tracing must not affect runtime behavior.
        }

        try
        {
            lock (TraceFileLock)
            {
                File.AppendAllText(TraceFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Debug tracing must not affect runtime behavior.
        }
    }

    private static void WriteHokenTraceLine(string message)
    {
        string line =
            $"[wasm-hoken-trace] {DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} {message}";
        try
        {
            lock (TraceFileLock)
            {
                File.AppendAllText(HokenTraceFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Debug tracing must not affect runtime behavior.
        }
    }

    internal sealed record WasmProjectionPayload(string ProjectorName, string StateJson) : IMultiProjectionPayload;

    internal sealed record WasmListQueryResult(
        string ItemsJson,
        int? TotalCount,
        int? TotalPages,
        int? CurrentPage,
        int? PageSize);
}
