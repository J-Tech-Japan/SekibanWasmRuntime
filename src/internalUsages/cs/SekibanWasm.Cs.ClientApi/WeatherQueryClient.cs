using System.Net.Http.Json;
using System.Text.Json;
using SekibanWasm.Cs.Domain.Weather;

namespace SekibanWasm.Cs.ClientApi;

public interface IWeatherQueryClient
{
    Task<WeatherForecastItem?> GetForecastAsync(string forecastId, CancellationToken ct);
}

public sealed class WeatherQueryClient : IWeatherQueryClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly JsonSerializerOptions _domainJsonOptions;
    private readonly JsonSerializerOptions _transportJsonOptions;

    public WeatherQueryClient(
        IHttpClientFactory httpClientFactory,
        DomainSerializerOptions domainJsonOptions,
        TransportSerializerOptions transportJsonOptions)
    {
        _httpClientFactory = httpClientFactory;
        _domainJsonOptions = domainJsonOptions.Value;
        _transportJsonOptions = transportJsonOptions.Value;
    }

    public async Task<WeatherForecastItem?> GetForecastAsync(string forecastId, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("wasmserver");
        var request = new SerializedListQueryRequest(
            QueryType: nameof(GetWeatherForecastListQuery),
            QueryParamsJson: JsonSerializer.Serialize(
                new GetWeatherForecastListQuery
                {
                    ForecastId = forecastId,
                    IncludeDeleted = true,
                    PageSize = 1
                },
                _domainJsonOptions));

        var response = await client.PostAsJsonAsync(
            "/api/sekiban/serialized/list-query",
            request,
            _transportJsonOptions,
            ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SerializedListQueryResponse>(
            _transportJsonOptions,
            ct);
        if (result is null)
        {
            throw new InvalidOperationException("Failed to parse serialized list-query response.");
        }

        var items = JsonSerializer.Deserialize<List<WeatherForecastItem>>(result.ItemsJson, _domainJsonOptions);
        return items?.SingleOrDefault();
    }

    private sealed record SerializedListQueryRequest(
        string QueryType,
        string QueryParamsJson,
        string? WaitForSortableUniqueId = null);

    private sealed record SerializedListQueryResponse(
        string ItemsJson,
        int? TotalCount,
        int? TotalPages,
        int? CurrentPage,
        int? PageSize);
}
