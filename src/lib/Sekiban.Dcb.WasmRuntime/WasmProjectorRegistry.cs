namespace Sekiban.Dcb.WasmRuntime;

public class WasmProjectorRegistry
{
    private readonly Dictionary<string, WasmModuleRef> _projectors = new();
    private readonly Dictionary<string, string> _queryToProjectorMap = new();

    public void Register(WasmModuleRef moduleRef)
    {
        _projectors[moduleRef.ProjectorName] = moduleRef;
    }

    public void MapQueryToProjector(string queryTypeName, string projectorName)
    {
        _queryToProjectorMap[queryTypeName] = projectorName;
    }

    public WasmModuleRef? TryGet(string projectorName)
    {
        return _projectors.TryGetValue(projectorName, out var moduleRef) ? moduleRef : null;
    }

    public IReadOnlyList<string> GetAllProjectorNames()
    {
        return _projectors.Keys.ToList();
    }

    public string? ResolveProjectorForQuery(string queryTypeName)
    {
        return _queryToProjectorMap.TryGetValue(queryTypeName, out var projectorName)
            ? projectorName
            : null;
    }
}
