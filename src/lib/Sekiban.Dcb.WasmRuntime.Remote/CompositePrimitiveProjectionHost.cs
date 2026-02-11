using Sekiban.Dcb.Primitives;

namespace Sekiban.Dcb.WasmRuntime.Remote;

public class CompositePrimitiveProjectionHost : IPrimitiveProjectionHost
{
    private readonly IPrimitiveProjectionHost _inProcHost;
    private readonly IPrimitiveProjectionHost _remoteHost;
    private readonly HashSet<string> _remoteProjectors;

    public CompositePrimitiveProjectionHost(
        IPrimitiveProjectionHost inProcHost,
        IPrimitiveProjectionHost remoteHost,
        IEnumerable<string> remoteProjectorNames)
    {
        _inProcHost = inProcHost;
        _remoteHost = remoteHost;
        _remoteProjectors = new HashSet<string>(remoteProjectorNames);
    }

    public IPrimitiveProjectionInstance CreateInstance(string projectorName)
    {
        return _remoteProjectors.Contains(projectorName)
            ? _remoteHost.CreateInstance(projectorName)
            : _inProcHost.CreateInstance(projectorName);
    }
}
