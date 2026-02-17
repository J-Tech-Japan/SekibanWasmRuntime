using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.WasmRuntime;

/// <summary>
///     Client abstraction for serialized DCB operations.
///     Mirrors <see cref="Sekiban.Dcb.Actors.ISerializedSekibanDcbExecutor"/> so that
///     both InProc and HTTP transports can be used interchangeably.
/// </summary>
public interface ISerializedDcbClient
{
    Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId);

    Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
        SerializedCommitRequest request,
        CancellationToken cancellationToken = default);

    Task<ResultBox<SerializedCommandExecuteResponse>> ExecuteSerializedCommandAsync(
        SerializedCommandExecuteRequest request,
        CancellationToken cancellationToken = default);
}
