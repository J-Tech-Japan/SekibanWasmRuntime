using System.Net.Http.Json;
using System.Text.Json;
using ResultBoxes;

namespace Sekiban.Dcb.WasmRuntime.Remote;

public class HttpSerializedQueryClient : ISerializedQueryClient
{
    private readonly HttpClient _httpClient;
    private readonly SerializedDcbClientOptions _options;
    private readonly JsonSerializerOptions _transportJsonOptions;
    private readonly JsonSerializerOptions _payloadJsonOptions;

    public HttpSerializedQueryClient(
        HttpClient httpClient,
        SerializedDcbClientOptions options,
        JsonSerializerOptions transportJsonOptions,
        JsonSerializerOptions payloadJsonOptions)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new ArgumentException("BaseUrl is required", nameof(options));
        }

        _httpClient = httpClient;
        _options = options;
        _transportJsonOptions = transportJsonOptions;
        _payloadJsonOptions = payloadJsonOptions;
    }

    public Task<ResultBox<TResult>> ExecuteQueryAsync<TResult>(
        SerializedQueryRequest request,
        CancellationToken cancellationToken)
        where TResult : notnull =>
        ExecuteAsync<TResult, SerializedQueryResponse>(
            "/api/sekiban/serialized/query",
            request,
            static response => response.ResultJson,
            cancellationToken);

    public Task<ResultBox<TResult>> ExecuteListQueryAsync<TResult>(
        SerializedQueryRequest request,
        CancellationToken cancellationToken)
        where TResult : notnull =>
        ExecuteAsync<TResult, SerializedListQueryResponse>(
            "/api/sekiban/serialized/list-query",
            request,
            static response => response.ItemsJson,
            cancellationToken);

    private async Task<ResultBox<TResult>> ExecuteAsync<TResult, TResponse>(
        string relativePath,
        SerializedQueryRequest request,
        Func<TResponse, string> payloadSelector,
        CancellationToken cancellationToken)
        where TResult : notnull
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');

        var response = await _httpClient.PostAsJsonAsync(
            $"{baseUrl}{relativePath}",
            request,
            _transportJsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            return ResultBox<TResult>.FromException(
                new HttpRequestException(
                    $"Serialized query failed with {response.StatusCode}: {errorBody}"));
        }

        var body = await response.Content.ReadFromJsonAsync<TResponse>(
            _transportJsonOptions,
            cancellationToken);
        if (body is null)
        {
            return ResultBox<TResult>.FromException(
                new InvalidOperationException("Null response from serialized query endpoint"));
        }

        var result = JsonSerializer.Deserialize<TResult>(payloadSelector(body), _payloadJsonOptions);
        if (result is null)
        {
            return ResultBox<TResult>.FromException(
                new InvalidOperationException("Failed to deserialize query result payload"));
        }

        return ResultBox<TResult>.FromValue(result);
    }
}
