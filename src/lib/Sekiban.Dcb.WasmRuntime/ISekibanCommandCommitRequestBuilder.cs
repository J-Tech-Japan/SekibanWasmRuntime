using Sekiban.Dcb.Commands;

namespace Sekiban.Dcb.WasmRuntime;

public interface ISekibanCommandCommitRequestBuilder
{
    Task<SerializedCommitRequest> BuildCommitRequestAsync(
        string commandName,
        object command,
        CancellationToken cancellationToken = default);
}
