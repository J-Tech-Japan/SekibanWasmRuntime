using SekibanWasm.Cs.Domain.Weather;

namespace SekibanWasm.Cs.Web;

public class WeatherApiClient(HttpClient httpClient)
{
    public async Task<WeatherForecastItem[]> GetWeatherAsync(
        CancellationToken cancellationToken = default)
    {
        var forecasts = await httpClient.GetFromJsonAsync<List<WeatherForecastItem>>(
            "/api/weatherforecast",
            cancellationToken);

        return forecasts?.ToArray() ?? [];
    }

    public async Task<CommandResponse> CreateWeatherAsync(
        CreateWeatherForecast command,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/weatherforecast", command, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CommandResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Failed to deserialize CommandResponse");
    }

    public async Task<CommandResponse> DeleteWeatherAsync(
        string forecastId,
        CancellationToken cancellationToken = default)
    {
        var request = new { ForecastId = forecastId };
        var response = await httpClient.PostAsJsonAsync("/api/weatherforecast/delete", request, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CommandResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Failed to deserialize CommandResponse");
    }

    public async Task<CommandResponse> UpdateLocationAsync(
        string forecastId,
        string newLocation,
        CancellationToken cancellationToken = default)
    {
        var request = new { ForecastId = forecastId, NewLocation = newLocation };
        var response = await httpClient.PostAsJsonAsync("/api/weatherforecast/update-location", request, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CommandResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Failed to deserialize CommandResponse");
    }
}

public record CommandResponse(bool Success, string? Error, string? SortableUniqueId);
