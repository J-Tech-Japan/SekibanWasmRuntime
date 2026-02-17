using ResultBoxes;

namespace Sekiban.Dcb.WasmRuntime;

/// <summary>
///     Abstraction for executing serialized commands in-process.
///     Implemented by the host application (WasmServer) which has access to
///     ISekibanExecutor and command type resolution.
/// </summary>
public interface ISerializedCommandExecutor
{
    Task<ResultBox<SerializedCommandExecuteResponse>> ExecuteAsync(
        SerializedCommandExecuteRequest request,
        CancellationToken cancellationToken);
}
