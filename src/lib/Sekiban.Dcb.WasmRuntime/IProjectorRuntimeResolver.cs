using Sekiban.Dcb.Runtime;

namespace Sekiban.Dcb.WasmRuntime;

public interface IProjectorRuntimeResolver
{
    IProjectionRuntime Resolve(string projectorName);
    IReadOnlyList<IProjectionRuntime> GetAllRuntimes();
}
