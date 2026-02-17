using System.Net.Http.Json;
using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.WasmRuntime;

namespace Sekiban.Dcb.WasmRuntime.Remote;

/// <summary>
///     HTTP transport implementation of <see cref="ISerializedDcbClient"/>.
///     Calls the remote API service's serialized endpoints.
/// </summary>
public class HttpSerializedDcbClient : ISerializedDcbClient
{
    private readonly HttpClient _httpClient;
    private readonly SerializedDcbClientOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;

    public HttpSerializedDcbClient(
        HttpClient httpClient,
        SerializedDcbClientOptions options,
        JsonSerializerOptions jsonOptions)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new ArgumentException("BaseUrl is required", nameof(options));
        }
        _httpClient = httpClient;
        _options = options;
        _jsonOptions = jsonOptions;
    }

    public async Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(
        TagStateId tagStateId)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var request = new TagStateRequest(tagStateId.GetTagStateId());

        var response = await _httpClient.PostAsJsonAsync(
            $"{baseUrl}/api/sekiban/serialized/tag-state",
            request,
            _jsonOptions);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            return ResultBox<SerializableTagState>.FromException(
                new HttpRequestException(
                    $"GetSerializableTagState failed with {response.StatusCode}: {errorBody}"));
        }

        var result = await response.Content.ReadFromJsonAsync<SerializableTagState>(_jsonOptions);
        if (result is null)
        {
            return ResultBox<SerializableTagState>.FromException(
                new InvalidOperationException("Null response from tag-state endpoint"));
        }

        return ResultBox<SerializableTagState>.FromValue(result);
    }

    public async Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
        SerializedCommitRequest request,
        CancellationToken cancellationToken)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');

        var response = await _httpClient.PostAsJsonAsync(
            $"{baseUrl}/api/sekiban/serialized/commit",
            request,
            _jsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            return ResultBox<SerializedCommitResult>.FromException(
                new HttpRequestException(
                    $"CommitSerializableEvents failed with {response.StatusCode}: {errorBody}"));
        }

        var result = await response.Content.ReadFromJsonAsync<SerializedCommitResult>(
            _jsonOptions, cancellationToken);
        if (result is null)
        {
            return ResultBox<SerializedCommitResult>.FromException(
                new InvalidOperationException("Null response from commit endpoint"));
        }

        return ResultBox<SerializedCommitResult>.FromValue(result);
    }

    public async Task<ResultBox<SerializedCommandExecuteResponse>> ExecuteSerializedCommandAsync(
        SerializedCommandExecuteRequest request,
        CancellationToken cancellationToken)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');

        var response = await _httpClient.PostAsJsonAsync(
            $"{baseUrl}/api/sekiban/serialized/command/execute",
            request,
            _jsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            return ResultBox<SerializedCommandExecuteResponse>.FromException(
                new HttpRequestException(
                    $"ExecuteSerializedCommand failed with {response.StatusCode}: {errorBody}"));
        }

        var result = await response.Content.ReadFromJsonAsync<SerializedCommandExecuteResponse>(
            _jsonOptions, cancellationToken);
        if (result is null)
        {
            return ResultBox<SerializedCommandExecuteResponse>.FromException(
                new InvalidOperationException("Null response from command/execute endpoint"));
        }

        return ResultBox<SerializedCommandExecuteResponse>.FromValue(result);
    }

    private record TagStateRequest(string TagStateId);
}
