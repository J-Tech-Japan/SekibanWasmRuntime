using Sekiban.Dcb.Queries;

namespace SekibanDcbDecider.Domain.Weather;

// Query — read responsibility, separated from command/event/projection.
public record GetWeatherForecastListQuery :
    IMultiProjectionListQuery<WeatherForecastMultiProjection, GetWeatherForecastListQuery, WeatherForecastItem>,
    IQueryPagingParameter
{
    public string? ForecastId { get; init; }
    public bool IncludeDeleted { get; init; }
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }
    public string? WaitForSortableUniqueId { get; init; }

    public static IEnumerable<WeatherForecastItem> HandleFilter(
        WeatherForecastMultiProjection projector,
        GetWeatherForecastListQuery query,
        IQueryContext context)
    {
        var items = projector.Forecasts.Values.AsEnumerable();
        if (!query.IncludeDeleted)
        {
            items = items.Where(x => !x.IsDeleted);
        }

        if (!string.IsNullOrWhiteSpace(query.ForecastId))
        {
            items = items.Where(x => x.ForecastId == query.ForecastId);
        }

        return items;
    }

    public static IEnumerable<WeatherForecastItem> HandleSort(
        IEnumerable<WeatherForecastItem> filteredList,
        GetWeatherForecastListQuery query,
        IQueryContext context) =>
        filteredList.OrderByDescending(x => x.CreatedAt);
}
