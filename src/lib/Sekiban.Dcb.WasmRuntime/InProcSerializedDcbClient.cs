using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.WasmRuntime;

/// <summary>
///     In-process adapter that delegates to <see cref="ISerializedSekibanDcbExecutor"/>
///     and <see cref="ISerializedCommandExecutor"/> for local/test use without HTTP overhead.
/// </summary>
public class InProcSerializedDcbClient : ISerializedDcbClient
{
    private readonly ISerializedSekibanDcbExecutor _executor;
    private readonly ISerializedCommandExecutor _commandExecutor;

    public InProcSerializedDcbClient(
        ISerializedSekibanDcbExecutor executor,
        ISerializedCommandExecutor commandExecutor)
    {
        _executor = executor;
        _commandExecutor = commandExecutor;
    }

    public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId) =>
        _executor.GetSerializableTagStateAsync(tagStateId);

    public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
        SerializedCommitRequest request,
        CancellationToken cancellationToken) =>
        _executor.CommitSerializableEventsAsync(request, cancellationToken);

    public Task<ResultBox<SerializedCommandExecuteResponse>> ExecuteSerializedCommandAsync(
        SerializedCommandExecuteRequest request,
        CancellationToken cancellationToken) =>
        _commandExecutor.ExecuteAsync(request, cancellationToken);
}
