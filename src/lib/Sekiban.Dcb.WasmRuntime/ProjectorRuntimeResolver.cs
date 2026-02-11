using Sekiban.Dcb.Runtime;

namespace Sekiban.Dcb.WasmRuntime;

public class ProjectorRuntimeResolver : IProjectorRuntimeResolver
{
    private readonly IProjectionRuntime _defaultRuntime;
    private readonly IReadOnlyDictionary<string, IProjectionRuntime> _runtimeMap;

    public ProjectorRuntimeResolver(
        IProjectionRuntime defaultRuntime,
        IReadOnlyDictionary<string, IProjectionRuntime> runtimeMap)
    {
        _defaultRuntime = defaultRuntime;
        _runtimeMap = runtimeMap;
    }

    public IProjectionRuntime Resolve(string projectorName)
    {
        return _runtimeMap.TryGetValue(projectorName, out var runtime)
            ? runtime
            : _defaultRuntime;
    }

    public IReadOnlyList<IProjectionRuntime> GetAllRuntimes()
    {
        var runtimes = new HashSet<IProjectionRuntime>(_runtimeMap.Values) { _defaultRuntime };
        return runtimes.ToList();
    }
}
