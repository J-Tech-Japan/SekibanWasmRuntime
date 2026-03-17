using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.WasmRuntime;

public class SekibanWasmExecutor : ISekibanWasmExecutor
{
    private readonly ISerializedDcbClient _client;
    private readonly ISerializedQueryClient _queryClient;
    private readonly ISekibanCommandCommitRequestBuilder _commandRequestBuilder;
    private readonly JsonSerializerOptions _jsonOptions;

    public SekibanWasmExecutor(
        ISerializedDcbClient client,
        ISerializedQueryClient queryClient,
        ISekibanCommandCommitRequestBuilder commandRequestBuilder,
        JsonSerializerOptions jsonOptions)
    {
        _client = client;
        _queryClient = queryClient;
        _commandRequestBuilder = commandRequestBuilder;
        _jsonOptions = jsonOptions;
    }

    public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId) =>
        _client.GetSerializableTagStateAsync(tagStateId);

    public async Task<ResultBox<SerializedCommitResult>> ExecuteCommandAsync(
        string commandName,
        object command,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = await _commandRequestBuilder.BuildCommitRequestAsync(
                commandName,
                command,
                cancellationToken);
            return await _client.CommitSerializableEventsAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            return ResultBox<SerializedCommitResult>.FromException(ex);
        }
    }

    public Task<ResultBox<TResult>> ExecuteQueryAsync<TResult>(
        string queryType,
        object query,
        string? waitForSortableUniqueId,
        CancellationToken cancellationToken)
        where TResult : notnull =>
        _queryClient.ExecuteQueryAsync<TResult>(
            BuildQueryRequest(queryType, query, waitForSortableUniqueId),
            cancellationToken);

    public Task<ResultBox<TResult>> ExecuteListQueryAsync<TResult>(
        string queryType,
        object query,
        string? waitForSortableUniqueId,
        CancellationToken cancellationToken)
        where TResult : notnull =>
        _queryClient.ExecuteListQueryAsync<TResult>(
            BuildQueryRequest(queryType, query, waitForSortableUniqueId),
            cancellationToken);

    private SerializedQueryRequest BuildQueryRequest(
        string queryType,
        object query,
        string? waitForSortableUniqueId) =>
        new(
            QueryType: queryType,
            QueryParamsJson: JsonSerializer.Serialize(query, query.GetType(), _jsonOptions),
            WaitForSortableUniqueId: waitForSortableUniqueId);
}
