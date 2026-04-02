using System.Collections;
using System.IO.Compression;
using System.Runtime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
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
    DcbDomainTypes domainTypes,
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
            domainTypes,
            jsonOptions,
            projectorName,
            logger ?? loggerFactory.CreateLogger<WasmProjectionActorHost>());
}

public sealed class WasmProjectionActorHost : IProjectionActorHost, IDisposable
{
    private const string CompressedSnapshotPayloadType =
        "Sekiban.Dcb.WasmRuntime.WasmCompressedProjectionState";

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
    private static readonly bool EnableLegacyEventFiltering =
        string.Equals(
            Environment.GetEnvironmentVariable("WASM_RUNTIME_ENABLE_LEGACY_EVENT_FILTERING"),
            "1",
            StringComparison.Ordinal);
    private static readonly bool EnableLegacyOrphanSkipping =
        string.Equals(
            Environment.GetEnvironmentVariable("WASM_RUNTIME_ENABLE_LEGACY_ORPHAN_SKIPPING"),
            "1",
            StringComparison.Ordinal);
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
    private static readonly SemaphoreSlim? SharedCatchUpGate = CreateConfiguredCatchUpGate();
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
    private readonly DcbDomainTypes _domainTypes;
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
    private byte[]? _pendingCompactionStateUtf8;
    private int _pendingCompactionStateVersion = -1;
    private int _eventsSinceLastCompaction;
    private int _compactionStaggerOffset = -1;
    /// <summary>
    /// Number of events to process before triggering automatic compaction.
    /// Compaction serializes state, creates a fresh WASM instance with minimal linear memory,
    /// and restores the state. This prevents unbounded linear memory growth.
    /// Set to 0 to disable automatic compaction.
    /// </summary>
    private readonly SemaphoreSlim? _catchUpGate;
    private readonly int _autoCompactionIntervalEvents;
    private readonly bool _forceCompactingGcAfterCompaction;
    /// <summary>
    /// Stagger offset between projectors to avoid simultaneous compaction of all 9+ projectors.
    /// Each projector gets a unique offset based on its name hash, spreading compactions over
    /// the interval to reduce peak memory from 9×2×36.6MB=659MB to 1×2×36.6MB=73MB.
    /// </summary>
    private const int StaggerSlotSize = 1_000;
    private HashSet<int> _kanyushaListKnownKanyushaNos = [];
    private Dictionary<string, int> _kanyushaListKnownNendoIndex = new(StringComparer.Ordinal);

    public WasmProjectionActorHost(
        IPrimitiveProjectionHost host,
        WasmProjectorRegistry registry,
        DcbDomainTypes domainTypes,
        JsonSerializerOptions jsonOptions,
        string projectorName,
        ILogger logger,
        SemaphoreSlim? catchUpGate = null,
        int? autoCompactionIntervalEvents = null,
        bool? forceCompactingGcAfterCompaction = null)
    {
        _host = host;
        _registry = registry;
        _domainTypes = domainTypes;
        _jsonOptions = jsonOptions;
        _projectorName = projectorName;
        _logger = logger;
        _catchUpGate = catchUpGate ?? SharedCatchUpGate;
        _autoCompactionIntervalEvents =
            autoCompactionIntervalEvents ?? ResolveConfiguredNonNegativeInt(
                "SEKIBAN_WASM_AUTO_COMPACTION_INTERVAL_EVENTS",
                defaultValue: 50_000);
        _forceCompactingGcAfterCompaction =
            forceCompactingGcAfterCompaction ?? ResolveConfiguredBoolean(
                "SEKIBAN_WASM_FORCE_COMPACTING_GC_AFTER_COMPACTION",
                defaultValue: false);

        _projectorVersion = _registry.TryGet(projectorName)?.ProjectorVersion
            ?? throw new InvalidOperationException($"Projector not found: {projectorName}");
    }

    public async Task AddSerializableEventsAsync(
        IReadOnlyList<SerializableEvent> events,
        bool finishedCatchUp = true)
    {
        SemaphoreSlim? catchUpGate = null;
        if (!finishedCatchUp && events.Count > 0 && _catchUpGate is not null)
        {
            catchUpGate = _catchUpGate;
            Trace($"host_add_events:catchup_wait projector={_projectorName} eventCount={events.Count}");
            await catchUpGate.WaitAsync();
        }

        try
        {
            var instance = EnsureInstance();
            bool useBatchApply = !finishedCatchUp && events.Count > 1;
            List<SerializableEvent>? batchEvents = useBatchApply
                ? new List<SerializableEvent>(events.Count)
                : null;
            foreach (var serializedEvent in events)
            {
                bool traceHokenProjection = string.Equals(
                    _projectorName,
                    "HokenNendoShosaiListProjection",
                    StringComparison.Ordinal);
                bool needsPayloadJsonInspection = ShouldInspectPayloadJson(serializedEvent.EventPayloadName);
                Trace(
                    $"host_add_events:start projector={_projectorName} eventId={serializedEvent.Id} eventType={serializedEvent.EventPayloadName} sortableUniqueId={serializedEvent.SortableUniqueIdValue} tagCount={serializedEvent.Tags.Count}");
                if (ShouldSkipEvent(serializedEvent.EventPayloadName, serializedEvent.Tags))
                {
                    Trace(
                        $"host_add_events:skipped projector={_projectorName} eventId={serializedEvent.Id} eventType={serializedEvent.EventPayloadName} sortableUniqueId={serializedEvent.SortableUniqueIdValue}");
                    continue;
                }

                string? payloadJson = needsPayloadJsonInspection || !useBatchApply
                    ? Encoding.UTF8.GetString(serializedEvent.Payload)
                    : null;
                if (payloadJson is not null &&
                    ShouldSkipOrphanKanyushaListEvent(serializedEvent.EventPayloadName, payloadJson))
                {
                    Trace(
                        $"host_add_events:skipped_orphan projector={_projectorName} eventId={serializedEvent.Id} eventType={serializedEvent.EventPayloadName} sortableUniqueId={serializedEvent.SortableUniqueIdValue}");
                    continue;
                }

                if (traceHokenProjection)
                {
                    WriteHokenTraceLine(
                        $"start projector={_projectorName} eventType={serializedEvent.EventPayloadName} eventId={serializedEvent.Id} sortableUniqueId={serializedEvent.SortableUniqueIdValue} tagCount={serializedEvent.Tags.Count}");
                    _logger.LogInformation(
                        "WASM HokenNendo event apply start: Projector={ProjectorName}, EventType={EventType}, EventId={EventId}, SortableUniqueId={SortableUniqueId}, TagCount={TagCount}",
                        _projectorName,
                        serializedEvent.EventPayloadName,
                        serializedEvent.Id,
                        serializedEvent.SortableUniqueIdValue,
                        serializedEvent.Tags.Count);
                }

                if (useBatchApply)
                {
                    batchEvents!.Add(serializedEvent);
                    continue;
                }

                instance.ApplyEvent(
                    serializedEvent.EventPayloadName,
                    payloadJson ?? Encoding.UTF8.GetString(serializedEvent.Payload),
                    serializedEvent.Tags,
                    serializedEvent.SortableUniqueIdValue);

                if (traceHokenProjection)
                {
                    WriteHokenTraceLine(
                        $"completed projector={_projectorName} eventType={serializedEvent.EventPayloadName} eventId={serializedEvent.Id} sortableUniqueId={serializedEvent.SortableUniqueIdValue}");
                    _logger.LogInformation(
                        "WASM HokenNendo event apply completed: Projector={ProjectorName}, EventType={EventType}, EventId={EventId}, SortableUniqueId={SortableUniqueId}",
                        _projectorName,
                        serializedEvent.EventPayloadName,
                        serializedEvent.Id,
                        serializedEvent.SortableUniqueIdValue);
                }

                Trace(
                    $"host_add_events:completed projector={_projectorName} eventId={serializedEvent.Id} eventType={serializedEvent.EventPayloadName} sortableUniqueId={serializedEvent.SortableUniqueIdValue}");
                AdvanceEventVersion(serializedEvent);
            }

            if (batchEvents is { Count: > 0 })
            {
                Trace($"host_add_events:batch_start projector={_projectorName} eventCount={batchEvents.Count}");
                if (instance is ISerializableEventBatchProjectionInstance serializableBatchInstance)
                {
                    serializableBatchInstance.ApplySerializableEvents(batchEvents);
                }
                else
                {
                    var envelopes = new List<PrimitiveProjectionEventEnvelope>(batchEvents.Count);
                    foreach (SerializableEvent serializedEvent in batchEvents)
                    {
                        envelopes.Add(new PrimitiveProjectionEventEnvelope(
                            serializedEvent.EventPayloadName,
                            Encoding.UTF8.GetString(serializedEvent.Payload),
                            serializedEvent.Tags,
                            serializedEvent.SortableUniqueIdValue));
                    }

                    instance.ApplyEvents(envelopes);
                }
                Trace($"host_add_events:batch_completed projector={_projectorName} eventCount={batchEvents.Count}");

                foreach (var serializedEvent in batchEvents)
                {
                    AdvanceEventVersion(serializedEvent);
                    Trace(
                        $"host_add_events:completed projector={_projectorName} eventId={serializedEvent.Id} eventType={serializedEvent.EventPayloadName} sortableUniqueId={serializedEvent.SortableUniqueIdValue}");
                }
            }

            _isCatchedUp = finishedCatchUp;

            // Auto-compaction: periodically reset WASM linear memory to prevent unbounded growth.
            // Staggered across projectors to avoid 9 simultaneous compactions (which spike 659MB).
            if (_autoCompactionIntervalEvents > 0)
            {
                _eventsSinceLastCompaction += events.Count;

                if (_compactionStaggerOffset < 0)
                {
                    _compactionStaggerOffset = Math.Abs(_projectorName.GetHashCode()) % _autoCompactionIntervalEvents;
                }

                if (_eventsSinceLastCompaction >= _autoCompactionIntervalEvents)
                {
                    var sinceCompaction = _eventsSinceLastCompaction - _autoCompactionIntervalEvents;
                    if (sinceCompaction >= (_compactionStaggerOffset % StaggerSlotSize))
                    {
                        _eventsSinceLastCompaction = 0;
                        try
                        {
                            CompactSafeHistory();
                            _logger.LogWarning(
                                "Auto-compaction: {ProjectorName} v{Version}",
                                _projectorName, _version);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "Auto-compaction failed for {ProjectorName} at version {Version}",
                                _projectorName, _version);
                        }
                    }
                }
            }
        }
        finally
        {
            catchUpGate?.Release();
        }
    }

    private bool ShouldInspectPayloadJson(string eventType) =>
        string.Equals(_projectorName, "KanyushaListProjection", StringComparison.Ordinal) &&
        KanyushaListOrphanSkippableEventTypes.Contains(eventType);

    private void AdvanceEventVersion(SerializableEvent serializedEvent)
    {
        _version++;
        InvalidateSerializedStateCache();
        _lastSortableUniqueId = serializedEvent.SortableUniqueIdValue;
        _lastEventId = serializedEvent.Id;
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
        byte[] stateJsonUtf8 = EnsureInstance().SerializeStateUtf8();
        var payload = DeserializeGuestStateToPayload(stateJsonUtf8);
        if (!payload.IsSuccess)
        {
            return Task.FromResult(ResultBox.Error<MultiProjectionState>(payload.GetException()));
        }

        return Task.FromResult(ResultBox.FromValue(new MultiProjectionState(
            Payload: payload.GetValue(),
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
            byte[] stateJsonUtf8 = EnsureInstance().SerializeStateUtf8();
            CapturePendingCompactionState(stateJsonUtf8);
            byte[] compressedStateJson = await CompressUtf8Async(stateJsonUtf8);
            await WriteCompressedSnapshotEnvelopeAsync(
                target,
                compressedStateJson,
                stateJsonUtf8.LongLength,
                cancellationToken);
            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            ClearPendingCompactionState();
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
            byte[] payloadBytes = inlineState.GetPayloadBytes();
            byte[] stateJsonUtf8 = await ReadStateJsonUtf8FromSnapshotPayloadAsync(
                inlineState,
                payloadBytes);
            EnsureInstance().RestoreStateUtf8(stateJsonUtf8);
            _version = inlineState.Version;
            _lastSortableUniqueId = inlineState.LastSortableUniqueId;
            _lastEventId = inlineState.LastEventId;
            _isCatchedUp = inlineState.IsCatchedUp;
            string stateJson = Encoding.UTF8.GetString(stateJsonUtf8);
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
        if (_instance is null)
        {
            return;
        }

        var preLinearMem = GetLinearMemoryBytes();
        byte[] stateJsonUtf8 = TakePendingCompactionState() ?? _instance.SerializeStateUtf8();
        _logger.LogWarning(
            "CompactSafeHistory: {ProjectorName} stateSize={StateSizeKB}KB linearMem={LinearMemMB}MB version={Version}",
            _projectorName, stateJsonUtf8.Length / 1024, preLinearMem / 1024 / 1024, _version);
        IPrimitiveProjectionInstance previous = _instance;
        IPrimitiveProjectionInstance? replacement = null;

        try
        {
            replacement = _host is IFreshPrimitiveProjectionHost freshHost
                ? freshHost.CreateFreshInstance(_projectorName)
                : _host.CreateInstance(_projectorName);
            replacement.RestoreStateUtf8(stateJsonUtf8);

            _instance = replacement;
            InvalidateSerializedStateCache();
            RefreshKanyushaListShadowState(stateJsonUtf8);

            if (previous is IPooledPrimitiveProjectionLeaseControl pooledLeaseControl)
            {
                pooledLeaseControl.MarkDoNotPool();
            }

            previous.Dispose();
            if (_forceCompactingGcAfterCompaction)
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            }
            else
            {
                GC.Collect(0, GCCollectionMode.Default, blocking: false);
            }
            var postLinearMem = GetLinearMemoryBytes();
            _logger.LogWarning(
                "CompactSafeHistory DONE: {ProjectorName} newLinearMem={LinearMemMB}MB (saved {SavedMB}MB)",
                _projectorName, postLinearMem / 1024 / 1024,
                (preLinearMem - postLinearMem) / 1024 / 1024);
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(previous, _instance))
            {
                _instance?.Dispose();
                _instance = previous;
            }

            if (replacement is not null && ReferenceEquals(replacement, _instance) is false)
            {
                replacement.Dispose();
            }

            _logger.LogWarning(ex, "Failed to compact WASM projection instance for {ProjectorName}", _projectorName);
        }
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
        EnsureInstance().SerializeStateUtf8().LongLength;

    public string PeekCurrentSafeWindowThreshold() => _lastSortableUniqueId ?? string.Empty;

    public string GetProjectorVersion() => _projectorVersion;

    /// <summary>
    /// Returns the current WASM linear memory size in bytes for the underlying instance.
    /// Returns 0 if no instance is active.
    /// </summary>
    public long GetLinearMemoryBytes()
    {
        try
        {
            var instance = _instance;
            if (instance == null) return 0;
            // Use reflection to call GetLinearMemoryBytes on the underlying instance
            var method = instance.GetType().GetMethod("GetLinearMemoryBytes");
            if (method != null)
                return (long)(method.Invoke(instance, null) ?? 0);
            return -1;
        }
        catch
        {
            return -1;
        }
    }

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
            var payloadBytes = inlineState.GetPayloadBytes();
            SerializableMultiProjectionState rewrittenInlineState;
            if (string.Equals(
                    inlineState.MultiProjectionPayloadType,
                    CompressedSnapshotPayloadType,
                    StringComparison.Ordinal))
            {
                rewrittenInlineState = SerializableMultiProjectionState.FromBytes(
                    payload: payloadBytes,
                    multiProjectionPayloadType: inlineState.MultiProjectionPayloadType,
                    projectorName: inlineState.ProjectorName,
                    projectorVersion: newVersion,
                    lastSortableUniqueId: inlineState.LastSortableUniqueId,
                    lastEventId: inlineState.LastEventId,
                    version: inlineState.Version,
                    isCatchedUp: inlineState.IsCatchedUp,
                    isSafeState: inlineState.IsSafeState,
                    originalSizeBytes: inlineState.OriginalSizeBytes,
                    compressedSizeBytes: inlineState.CompressedSizeBytes);
            }
            else
            {
                var snapshot = WasmRuntimeJsonContext.DeserializeSnapshot(payloadBytes)
                    ?? throw new InvalidOperationException("Snapshot stream did not contain a valid WASM state snapshot.");
                var rewritten = snapshot with { ProjectorVersion = newVersion };
                var rewrittenBytes = WasmRuntimeJsonContext.SerializeSnapshotToUtf8Bytes(rewritten);
                rewrittenInlineState = SerializableMultiProjectionState.FromBytes(
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
            }

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
        if (!EnableLegacyEventFiltering)
        {
            return false;
        }

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
            "NendoKanyuMoushikomiSaikaied" => true,
            "KeiyakuKaiyakuChutoed" => true,
            "TadantaiKeizokuJohoKoshin" => true,
            "TaDantaiKeizokuSeted" => true,
            "KanyushaLoginRecorded" => true,
            "KanyushaSakujoed" => true,
            "NendoKanyuImported" => true,
            "NendoKanyuReplaced" => true,
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
            "KeiyakuReplaced" => true,
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

    private async Task<SerializableMultiProjectionState> CreateCompressedInlineSnapshotStateAsync()
    {
        byte[] stateJsonUtf8 = EnsureInstance().SerializeStateUtf8();
        byte[] compressedStateJson = await CompressUtf8Async(stateJsonUtf8);
        return SerializableMultiProjectionState.FromBytes(
            payload: compressedStateJson,
            multiProjectionPayloadType: CompressedSnapshotPayloadType,
            projectorName: _projectorName,
            projectorVersion: _projectorVersion,
            lastSortableUniqueId: _lastSortableUniqueId ?? string.Empty,
            lastEventId: _lastEventId ?? Guid.Empty,
            version: _version,
            isCatchedUp: _isCatchedUp,
            isSafeState: true,
            originalSizeBytes: stateJsonUtf8.LongLength,
            compressedSizeBytes: compressedStateJson.LongLength);
    }

    private static async Task<byte[]> ReadSnapshotStateJsonUtf8Async(WasmStateSnapshot snapshot)
    {
        if (snapshot.CompressedStateJson is { Length: > 0 } compressedStateJson)
        {
            return await DecompressToUtf8Async(compressedStateJson);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.StateJson))
        {
            return Encoding.UTF8.GetBytes(snapshot.StateJson);
        }

        throw new InvalidOperationException("WASM snapshot does not contain any serialized state payload.");
    }

    private ResultBox<IMultiProjectionPayload> DeserializeGuestStateToPayload(byte[] stateJsonUtf8)
    {
        try
        {
            string safeWindowThreshold = !string.IsNullOrWhiteSpace(_lastSortableUniqueId)
                ? _lastSortableUniqueId
                : SortableUniqueId.MinValue.Value;
            return _domainTypes.MultiProjectorTypes.Deserialize(
                _projectorName,
                _domainTypes,
                safeWindowThreshold,
                stateJsonUtf8);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IMultiProjectionPayload>(ex);
        }
    }

    private async Task<byte[]> ReadStateJsonUtf8FromSnapshotPayloadAsync(
        SerializableMultiProjectionState inlineState,
        byte[] payloadBytes)
    {
        if (string.Equals(
                inlineState.MultiProjectionPayloadType,
                CompressedSnapshotPayloadType,
                StringComparison.Ordinal))
        {
            return await DecompressToUtf8Async(payloadBytes);
        }

        WasmStateSnapshot? legacySnapshot = WasmRuntimeJsonContext.DeserializeSnapshot(payloadBytes);
        if (legacySnapshot is not null)
        {
            return await ReadSnapshotStateJsonUtf8Async(legacySnapshot);
        }

        string safeWindowThreshold = !string.IsNullOrWhiteSpace(inlineState.LastSortableUniqueId)
            ? inlineState.LastSortableUniqueId
            : SortableUniqueId.MinValue.Value;
        ResultBox<IMultiProjectionPayload> payloadResult =
            _domainTypes.MultiProjectorTypes.Deserialize(
                _projectorName,
                _domainTypes,
                safeWindowThreshold,
                payloadBytes);
        if (!payloadResult.IsSuccess)
        {
            throw payloadResult.GetException();
        }

        IMultiProjectionPayload payload = payloadResult.GetValue();
        return JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType(), _jsonOptions);
    }

    private string GetSnapshotSafeWindowThreshold() =>
        !string.IsNullOrWhiteSpace(_lastSortableUniqueId)
            ? _lastSortableUniqueId
            : SortableUniqueId.MinValue.Value;

    private bool ShouldSkipOrphanKanyushaListEvent(string eventType, string payloadJson)
    {
        if (!EnableLegacyOrphanSkipping)
        {
            return false;
        }

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

    private void RefreshKanyushaListShadowState(string stateJson) =>
        RefreshKanyushaListShadowState(Encoding.UTF8.GetBytes(stateJson));

    private void RefreshKanyushaListShadowState(byte[] stateJsonUtf8)
    {
        _kanyushaListKnownKanyushaNos.Clear();
        _kanyushaListKnownNendoIndex.Clear();

        if (!string.Equals(_projectorName, "KanyushaListProjection", StringComparison.Ordinal) ||
            !EnableLegacyOrphanSkipping ||
            stateJsonUtf8.Length == 0 ||
            (stateJsonUtf8.Length == 2 && stateJsonUtf8[0] == (byte)'{' && stateJsonUtf8[1] == (byte)'}'))
        {
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(stateJsonUtf8);
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
        return await CompressUtf8Async(bytes);
    }

    private static async Task<byte[]> CompressUtf8Async(byte[] utf8Bytes)
    {
        await using var output = new MemoryStream();
        await using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            await gzip.WriteAsync(utf8Bytes);
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

    private static async Task<byte[]> DecompressToUtf8Async(byte[] compressedData)
    {
        if (compressedData.Length == 0)
        {
            return "{}"u8.ToArray();
        }

        await using var input = new MemoryStream(compressedData);
        await using var gzip = new GZipStream(input, CompressionMode.Decompress);
        await using var output = new MemoryStream();
        await gzip.CopyToAsync(output);
        return output.ToArray();
    }

    private void InvalidateSerializedStateCache()
    {
        _cachedSerializedState = null;
        _cachedSerializedStateVersion = -1;
        ClearPendingCompactionState();
    }

    private void SetSerializedStateCache(string stateJson)
    {
        _cachedSerializedState = stateJson;
        _cachedSerializedStateVersion = _version;
    }

    private void CapturePendingCompactionState(byte[] stateJsonUtf8)
    {
        _pendingCompactionStateUtf8 = stateJsonUtf8;
        _pendingCompactionStateVersion = _version;
    }

    private byte[]? TakePendingCompactionState()
    {
        if (_pendingCompactionStateUtf8 is null || _pendingCompactionStateVersion != _version)
        {
            ClearPendingCompactionState();
            return null;
        }

        byte[] stateJsonUtf8 = _pendingCompactionStateUtf8;
        ClearPendingCompactionState();
        return stateJsonUtf8;
    }

    private void ClearPendingCompactionState()
    {
        _pendingCompactionStateUtf8 = null;
        _pendingCompactionStateVersion = -1;
    }

    private async Task WriteCompressedSnapshotEnvelopeAsync(
        Stream target,
        byte[] compressedStateJson,
        long originalSizeBytes,
        CancellationToken cancellationToken)
    {
        await using var writer = new Utf8JsonWriter(target);
        writer.WriteStartObject();
        writer.WriteBoolean(GetJsonPropertyName(nameof(SerializableMultiProjectionStateEnvelope.IsOffloaded)), false);
        writer.WritePropertyName(GetJsonPropertyName(nameof(SerializableMultiProjectionStateEnvelope.InlineState)));
        writer.WriteStartObject();
        writer.WriteNull(GetJsonPropertyName(nameof(SerializableMultiProjectionState.PayloadJson)));
        writer.WriteBase64String(
            GetJsonPropertyName(nameof(SerializableMultiProjectionState.PayloadBase64)),
            compressedStateJson);
        writer.WriteString(
            GetJsonPropertyName(nameof(SerializableMultiProjectionState.MultiProjectionPayloadType)),
            CompressedSnapshotPayloadType);
        writer.WriteString(GetJsonPropertyName(nameof(SerializableMultiProjectionState.ProjectorName)), _projectorName);
        writer.WriteString(
            GetJsonPropertyName(nameof(SerializableMultiProjectionState.ProjectorVersion)),
            _projectorVersion);
        writer.WriteString(
            GetJsonPropertyName(nameof(SerializableMultiProjectionState.LastSortableUniqueId)),
            _lastSortableUniqueId ?? string.Empty);
        writer.WriteString(
            GetJsonPropertyName(nameof(SerializableMultiProjectionState.LastEventId)),
            _lastEventId ?? Guid.Empty);
        writer.WriteNumber(GetJsonPropertyName(nameof(SerializableMultiProjectionState.Version)), _version);
        writer.WriteBoolean(GetJsonPropertyName(nameof(SerializableMultiProjectionState.IsCatchedUp)), _isCatchedUp);
        writer.WriteBoolean(GetJsonPropertyName(nameof(SerializableMultiProjectionState.IsSafeState)), true);
        writer.WriteNumber(
            GetJsonPropertyName(nameof(SerializableMultiProjectionState.OriginalSizeBytes)),
            originalSizeBytes);
        writer.WriteNumber(
            GetJsonPropertyName(nameof(SerializableMultiProjectionState.CompressedSizeBytes)),
            compressedStateJson.LongLength);
        writer.WriteEndObject();
        writer.WriteNull(GetJsonPropertyName(nameof(SerializableMultiProjectionStateEnvelope.OffloadedState)));
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);
    }

    private string GetJsonPropertyName(string clrPropertyName) =>
        _jsonOptions.PropertyNamingPolicy?.ConvertName(clrPropertyName) ?? clrPropertyName;

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

    private static SemaphoreSlim? CreateConfiguredCatchUpGate()
    {
        var concurrency = ResolveConfiguredNonNegativeInt(
            "SEKIBAN_WASM_CATCHUP_CONCURRENCY",
            defaultValue: 0);
        return concurrency <= 0
            ? null
            : new SemaphoreSlim(concurrency, concurrency);
    }

    private static int ResolveConfiguredNonNegativeInt(string environmentVariableName, int defaultValue)
    {
        string? raw = Environment.GetEnvironmentVariable(environmentVariableName);
        return int.TryParse(raw, out int value) && value >= 0
            ? value
            : defaultValue;
    }

    private static bool ResolveConfiguredBoolean(string environmentVariableName, bool defaultValue)
    {
        string? raw = Environment.GetEnvironmentVariable(environmentVariableName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => defaultValue
        };
    }

    internal sealed record WasmProjectionPayload(string ProjectorName, string StateJson) : IMultiProjectionPayload;

    internal sealed record WasmListQueryResult(
        string ItemsJson,
        int? TotalCount,
        int? TotalPages,
        int? CurrentPage,
        int? PageSize);
}
