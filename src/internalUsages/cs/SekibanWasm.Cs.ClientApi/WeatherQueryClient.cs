using System.Text.Json;
using Sekiban.Dcb.WasmRuntime;
using SekibanWasm.Cs.Domain.Weather;

namespace SekibanWasm.Cs.ClientApi;

public interface IWeatherQueryClient
{
    Task<WeatherForecastItem?> GetForecastAsync(string forecastId, string? waitForSortableUniqueId, CancellationToken ct);
}

public sealed class WeatherQueryClient : IWeatherQueryClient
{
    private readonly ISerializedQueryClient _queryClient;
    private readonly JsonSerializerOptions _domainJsonOptions;

    public WeatherQueryClient(
        ISerializedQueryClient queryClient,
        DomainSerializerOptions domainJsonOptions)
    {
        _queryClient = queryClient;
        _domainJsonOptions = domainJsonOptions.Value;
    }

    public async Task<WeatherForecastItem?> GetForecastAsync(
        string forecastId,
        string? waitForSortableUniqueId,
        CancellationToken ct)
    {
        var request = new SerializedQueryRequest(
            QueryType: nameof(GetWeatherForecastListQuery),
            QueryParamsJson: JsonSerializer.Serialize(
                new GetWeatherForecastListQuery
                {
                    ForecastId = forecastId,
                    IncludeDeleted = true,
                    PageSize = 1,
                    WaitForSortableUniqueId = waitForSortableUniqueId
                },
                _domainJsonOptions));

        var result = await _queryClient.ExecuteListQueryAsync<List<WeatherForecastItem>>(request, ct);
        if (!result.IsSuccess)
        {
            throw result.GetException();
        }

        return result.GetValue().SingleOrDefault();
    }
}
