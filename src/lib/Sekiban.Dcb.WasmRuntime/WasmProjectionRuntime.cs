using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Primitives;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Runtime;

namespace Sekiban.Dcb.WasmRuntime;

public class WasmProjectionRuntime : IProjectionRuntime
{
    private readonly IPrimitiveProjectionHost _host;
    private readonly WasmProjectorRegistry _registry;
    private readonly JsonSerializerOptions _jsonOptions;

    public WasmProjectionRuntime(
        IPrimitiveProjectionHost host,
        WasmProjectorRegistry registry,
        JsonSerializerOptions jsonOptions)
    {
        _host = host;
        _registry = registry;
        _jsonOptions = jsonOptions;
    }

    public ResultBox<IProjectionState> GenerateInitialState(string projectorName)
    {
        try
        {
            var instance = _host.CreateInstance(projectorName);
            return ResultBox<IProjectionState>.FromValue(
                new WasmProjectionState(instance, projectorName));
        }
        catch (Exception ex)
        {
            return ResultBox<IProjectionState>.FromException(ex);
        }
    }

    public ResultBox<string> GetProjectorVersion(string projectorName)
    {
        var moduleRef = _registry.TryGet(projectorName);
        if (moduleRef is null)
        {
            return ResultBox<string>.FromException(
                new InvalidOperationException($"Projector not found: {projectorName}"));
        }
        return ResultBox<string>.FromValue(moduleRef.ProjectorVersion);
    }

    public IReadOnlyList<string> GetAllProjectorNames()
    {
        return _registry.GetAllProjectorNames();
    }

    public ResultBox<IProjectionState> ApplyEvent(
        string projectorName,
        IProjectionState currentState,
        Event ev,
        string safeWindowThreshold)
    {
        try
        {
            var wasmState = (WasmProjectionState)currentState;
            var payloadJson = JsonSerializer.Serialize(
                ev.Payload, ev.Payload.GetType(), _jsonOptions);

            wasmState.Instance.ApplyEvent(
                ev.EventType, payloadJson, ev.Tags, ev.SortableUniqueIdValue);

            wasmState.UpdateMetadata(ev);
            return ResultBox<IProjectionState>.FromValue(currentState);
        }
        catch (Exception ex)
        {
            return ResultBox<IProjectionState>.FromException(ex);
        }
    }

    public ResultBox<IProjectionState> ApplyEvents(
        string projectorName,
        IProjectionState currentState,
        IReadOnlyList<Event> events,
        string safeWindowThreshold)
    {
        try
        {
            foreach (var ev in events)
            {
                var result = ApplyEvent(projectorName, currentState, ev, safeWindowThreshold);
                if (!result.IsSuccess)
                {
                    return result;
                }
            }
            return ResultBox<IProjectionState>.FromValue(currentState);
        }
        catch (Exception ex)
        {
            return ResultBox<IProjectionState>.FromException(ex);
        }
    }

    public async Task<ResultBox<SerializableQueryResult>> ExecuteQueryAsync(
        string projectorName,
        IProjectionState state,
        SerializableQueryParameter query,
        IServiceProvider serviceProvider)
    {
        try
        {
            var wasmState = (WasmProjectionState)state;
            var queryParamsJson = await DecompressToStringAsync(query.CompressedQueryJson);
            var resultJson = wasmState.Instance.ExecuteQuery(
                query.QueryTypeName, queryParamsJson);

            var compressedResult = await CompressStringAsync(resultJson);
            var compressedQuery = query.CompressedQueryJson;

            return ResultBox<SerializableQueryResult>.FromValue(
                new SerializableQueryResult
                {
                    ResultTypeName = query.QueryTypeName,
                    QueryTypeName = query.QueryTypeName,
                    CompressedResultJson = compressedResult,
                    CompressedQueryJson = compressedQuery,
                });
        }
        catch (Exception ex)
        {
            return ResultBox<SerializableQueryResult>.FromException(ex);
        }
    }

    public async Task<ResultBox<SerializableListQueryResult>> ExecuteListQueryAsync(
        string projectorName,
        IProjectionState state,
        SerializableQueryParameter query,
        IServiceProvider serviceProvider)
    {
        try
        {
            var wasmState = (WasmProjectionState)state;
            var queryParamsJson = await DecompressToStringAsync(query.CompressedQueryJson);
            var resultJson = wasmState.Instance.ExecuteListQuery(
                query.QueryTypeName, queryParamsJson);

            var compressedItems = await CompressStringAsync(resultJson);
            var compressedQuery = query.CompressedQueryJson;

            return ResultBox<SerializableListQueryResult>.FromValue(
                new SerializableListQueryResult
                {
                    QueryTypeName = query.QueryTypeName,
                    CompressedItemsJson = compressedItems,
                    CompressedQueryJson = compressedQuery,
                });
        }
        catch (Exception ex)
        {
            return ResultBox<SerializableListQueryResult>.FromException(ex);
        }
    }

    public ResultBox<byte[]> SerializeState(string projectorName, IProjectionState state)
    {
        try
        {
            var wasmState = (WasmProjectionState)state;
            var json = wasmState.Instance.SerializeState();
            var moduleRef = _registry.TryGet(projectorName);
            var snapshot = new WasmStateSnapshot(
                json,
                wasmState.SafeVersion,
                wasmState.UnsafeVersion,
                wasmState.SafeLastSortableUniqueId,
                wasmState.LastSortableUniqueId,
                wasmState.LastEventId,
                ProjectorVersion: moduleRef?.ProjectorVersion,
                TagProjector: projectorName);
            return ResultBox<byte[]>.FromValue(
                JsonSerializer.SerializeToUtf8Bytes(snapshot, _jsonOptions));
        }
        catch (Exception ex)
        {
            return ResultBox<byte[]>.FromException(ex);
        }
    }

    public ResultBox<IProjectionState> DeserializeState(
        string projectorName,
        byte[] data,
        string safeWindowThreshold)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<WasmStateSnapshot>(data, _jsonOptions);
            if (snapshot is null)
            {
                return ResultBox<IProjectionState>.FromException(
                    new InvalidOperationException("Failed to deserialize WasmStateSnapshot"));
            }

            // Version guard: projector version mismatch resets to initial state
            var moduleRef = _registry.TryGet(projectorName);
            if (moduleRef is not null &&
                snapshot.ProjectorVersion is not null &&
                snapshot.ProjectorVersion != moduleRef.ProjectorVersion)
            {
                return GenerateInitialState(projectorName);
            }

            // Identity guard: tag identity mismatch resets to initial state
            if (snapshot.TagProjector is not null &&
                snapshot.TagProjector != projectorName)
            {
                return GenerateInitialState(projectorName);
            }

            var instance = _host.CreateInstance(projectorName);
            instance.RestoreState(snapshot.StateJson);
            return ResultBox<IProjectionState>.FromValue(
                new WasmProjectionState(instance, projectorName, snapshot));
        }
        catch (Exception ex)
        {
            return ResultBox<IProjectionState>.FromException(ex);
        }
    }

    public ResultBox<string> ResolveProjectorName(IQueryCommon query)
    {
        var queryTypeName = query.GetType().FullName ?? query.GetType().Name;
        var projectorName = _registry.ResolveProjectorForQuery(queryTypeName);
        if (projectorName is null)
        {
            return ResultBox<string>.FromException(
                new InvalidOperationException($"No projector found for query: {queryTypeName}"));
        }
        return ResultBox<string>.FromValue(projectorName);
    }

    public ResultBox<string> ResolveProjectorName(IListQueryCommon query)
    {
        var queryTypeName = query.GetType().FullName ?? query.GetType().Name;
        var projectorName = _registry.ResolveProjectorForQuery(queryTypeName);
        if (projectorName is null)
        {
            return ResultBox<string>.FromException(
                new InvalidOperationException($"No projector found for list query: {queryTypeName}"));
        }
        return ResultBox<string>.FromValue(projectorName);
    }

    private static async Task<string> DecompressToStringAsync(byte[] compressedData)
    {
        if (compressedData.Length == 0)
        {
            return string.Empty;
        }
        using var compressedStream = new MemoryStream(compressedData);
        using var decompressedStream = new MemoryStream();
        using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
        {
            await gzipStream.CopyToAsync(decompressedStream);
        }
        return Encoding.UTF8.GetString(decompressedStream.ToArray());
    }

    private static async Task<byte[]> CompressStringAsync(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return [];
        }
        var bytes = Encoding.UTF8.GetBytes(data);
        using var memoryStream = new MemoryStream();
        using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Fastest))
        {
            await gzipStream.WriteAsync(bytes);
        }
        return memoryStream.ToArray();
    }
}
