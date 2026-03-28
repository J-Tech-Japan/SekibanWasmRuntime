using Sekiban.Dcb.Primitives;

namespace Sekiban.Dcb.WasmRuntime;

public interface IFreshPrimitiveProjectionHost
{
    IPrimitiveProjectionInstance CreateFreshInstance(string projectorName);
}

public interface IPooledPrimitiveProjectionLeaseControl
{
    void MarkDoNotPool();
}
