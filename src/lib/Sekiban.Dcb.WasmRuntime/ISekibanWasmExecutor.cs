using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.WasmRuntime;

public interface ISekibanWasmExecutor
{
    Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(
        TagStateId tagStateId);

    Task<ResultBox<SerializedCommitResult>> ExecuteCommandAsync(
        string commandName,
        object command,
        CancellationToken cancellationToken = default);

    Task<ResultBox<TResult>> ExecuteQueryAsync<TResult>(
        string queryType,
        object query,
        string? waitForSortableUniqueId = null,
        CancellationToken cancellationToken = default)
        where TResult : notnull;

    Task<ResultBox<TResult>> ExecuteListQueryAsync<TResult>(
        string queryType,
        object query,
        string? waitForSortableUniqueId = null,
        CancellationToken cancellationToken = default)
        where TResult : notnull;
}
