using ResultBoxes;

namespace Sekiban.Dcb.WasmRuntime;

public interface ISerializedQueryClient
{
    Task<ResultBox<TResult>> ExecuteQueryAsync<TResult>(
        SerializedQueryRequest request,
        CancellationToken cancellationToken = default)
        where TResult : notnull;

    Task<ResultBox<TResult>> ExecuteListQueryAsync<TResult>(
        SerializedQueryRequest request,
        CancellationToken cancellationToken = default)
        where TResult : notnull;
}
