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

public sealed class WasmProjectionActorHost : IProjectionActorHost
{
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
            var payloadJson = Encoding.UTF8.GetString(serializedEvent.Payload);
            instance.ApplyEvent(
                serializedEvent.EventPayloadName,
                payloadJson,
                serializedEvent.Tags,
                serializedEvent.SortableUniqueIdValue);

            _version++;
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
            var snapshot = CreateSnapshot();
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

            EnsureInstance().RestoreState(snapshot.StateJson);
            _version = snapshot.UnsafeVersion;
            _lastSortableUniqueId = snapshot.LastSortableUniqueId;
            _lastEventId = snapshot.LastEventId;
            _isCatchedUp = true;

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

    private string PeekSerializedState() =>
        _instance is null ? "{}" : _instance.SerializeState();

    private WasmStateSnapshot CreateSnapshot() =>
        new(
            StateJson: EnsureInstance().SerializeState(),
            SafeVersion: _version,
            UnsafeVersion: _version,
            SafeLastSortableUniqueId: _lastSortableUniqueId,
            LastSortableUniqueId: _lastSortableUniqueId,
            LastEventId: _lastEventId,
            ProjectorVersion: _projectorVersion,
            TagProjector: _projectorName);

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

    internal sealed record WasmProjectionPayload(string ProjectorName, string StateJson) : IMultiProjectionPayload;

    internal sealed record WasmListQueryResult(
        string ItemsJson,
        int? TotalCount,
        int? TotalPages,
        int? CurrentPage,
        int? PageSize);
}
