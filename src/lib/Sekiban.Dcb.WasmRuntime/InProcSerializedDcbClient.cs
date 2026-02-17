using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.WasmRuntime;

/// <summary>
///     In-process adapter that delegates to <see cref="ISerializedSekibanDcbExecutor"/>
///     for local/test use without HTTP overhead.
/// </summary>
public class InProcSerializedDcbClient : ISerializedDcbClient
{
    private readonly ISerializedSekibanDcbExecutor _executor;

    public InProcSerializedDcbClient(ISerializedSekibanDcbExecutor executor)
    {
        _executor = executor;
    }

    public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId) =>
        _executor.GetSerializableTagStateAsync(tagStateId);

    public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
        SerializedCommitRequest request,
        CancellationToken cancellationToken) =>
        _executor.CommitSerializableEventsAsync(request, cancellationToken);
}
