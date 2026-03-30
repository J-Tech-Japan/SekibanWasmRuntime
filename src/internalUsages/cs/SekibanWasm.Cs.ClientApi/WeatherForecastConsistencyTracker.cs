using System.Collections.Concurrent;

namespace SekibanWasm.Cs.ClientApi;

public interface IWeatherForecastConsistencyTracker
{
    string? GetWaitForSortableUniqueId(string forecastId);
    void Record(string forecastId, string? sortableUniqueId);
}

public sealed class WeatherForecastConsistencyTracker : IWeatherForecastConsistencyTracker
{
    private readonly ConcurrentDictionary<string, string> _latestSortables = new(StringComparer.Ordinal);

    public string? GetWaitForSortableUniqueId(string forecastId) =>
        _latestSortables.TryGetValue(forecastId, out var sortableUniqueId)
            ? sortableUniqueId
            : null;

    public void Record(string forecastId, string? sortableUniqueId)
    {
        if (string.IsNullOrWhiteSpace(forecastId) || string.IsNullOrWhiteSpace(sortableUniqueId))
        {
            return;
        }

        _latestSortables[forecastId] = sortableUniqueId;
    }
}
