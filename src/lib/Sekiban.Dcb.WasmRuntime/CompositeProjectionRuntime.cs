using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Runtime;

namespace Sekiban.Dcb.WasmRuntime;

public class CompositeProjectionRuntime : IProjectionRuntime
{
    private readonly IProjectorRuntimeResolver _resolver;

    public CompositeProjectionRuntime(IProjectorRuntimeResolver resolver)
    {
        _resolver = resolver;
    }

    public ResultBox<IProjectionState> GenerateInitialState(string projectorName)
    {
        return _resolver.Resolve(projectorName).GenerateInitialState(projectorName);
    }

    public ResultBox<string> GetProjectorVersion(string projectorName)
    {
        return _resolver.Resolve(projectorName).GetProjectorVersion(projectorName);
    }

    public IReadOnlyList<string> GetAllProjectorNames()
    {
        return _resolver.GetAllRuntimes()
            .SelectMany(r => r.GetAllProjectorNames())
            .Distinct()
            .ToList();
    }

    public ResultBox<IProjectionState> ApplyEvent(
        string projectorName,
        IProjectionState currentState,
        Event ev,
        string safeWindowThreshold)
    {
        return _resolver.Resolve(projectorName)
            .ApplyEvent(projectorName, currentState, ev, safeWindowThreshold);
    }

    public ResultBox<IProjectionState> ApplyEvents(
        string projectorName,
        IProjectionState currentState,
        IReadOnlyList<Event> events,
        string safeWindowThreshold)
    {
        return _resolver.Resolve(projectorName)
            .ApplyEvents(projectorName, currentState, events, safeWindowThreshold);
    }

    public Task<ResultBox<SerializableQueryResult>> ExecuteQueryAsync(
        string projectorName,
        IProjectionState state,
        SerializableQueryParameter query,
        IServiceProvider serviceProvider)
    {
        return _resolver.Resolve(projectorName)
            .ExecuteQueryAsync(projectorName, state, query, serviceProvider);
    }

    public Task<ResultBox<SerializableListQueryResult>> ExecuteListQueryAsync(
        string projectorName,
        IProjectionState state,
        SerializableQueryParameter query,
        IServiceProvider serviceProvider)
    {
        return _resolver.Resolve(projectorName)
            .ExecuteListQueryAsync(projectorName, state, query, serviceProvider);
    }

    public ResultBox<byte[]> SerializeState(string projectorName, IProjectionState state)
    {
        return _resolver.Resolve(projectorName).SerializeState(projectorName, state);
    }

    public ResultBox<IProjectionState> DeserializeState(
        string projectorName,
        byte[] data,
        string safeWindowThreshold)
    {
        return _resolver.Resolve(projectorName)
            .DeserializeState(projectorName, data, safeWindowThreshold);
    }

    public ResultBox<string> ResolveProjectorName(IQueryCommon query)
    {
        foreach (var runtime in _resolver.GetAllRuntimes())
        {
            var result = runtime.ResolveProjectorName(query);
            if (result.IsSuccess)
            {
                return result;
            }
        }
        var queryTypeName = query.GetType().FullName ?? query.GetType().Name;
        return ResultBox<string>.FromException(
            new InvalidOperationException($"No projector found for query: {queryTypeName}"));
    }

    public ResultBox<string> ResolveProjectorName(IListQueryCommon query)
    {
        foreach (var runtime in _resolver.GetAllRuntimes())
        {
            var result = runtime.ResolveProjectorName(query);
            if (result.IsSuccess)
            {
                return result;
            }
        }
        var queryTypeName = query.GetType().FullName ?? query.GetType().Name;
        return ResultBox<string>.FromException(
            new InvalidOperationException($"No projector found for list query: {queryTypeName}"));
    }
}
