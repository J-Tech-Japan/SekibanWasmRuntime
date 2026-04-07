using Dcb.EventSource.Projections;
using Orleans;
using Sekiban.Dcb.Queries;
namespace Dcb.EventSource.Queries;

public record GetWeatherForecastListQuery :
    IMultiProjectionListQuery<WeatherForecastProjection, GetWeatherForecastListQuery, WeatherForecastItem>,
    IWaitForSortableUniqueId,
    IQueryPagingParameter
{
    // Paging parameters (from IQueryPagingParameter)
    [Id(0)]
    public int? PageNumber { get; init; }
    [Id(1)]
    public int? PageSize { get; init; }

    // Required static methods for IMultiProjectionListQuery
    public static IEnumerable<WeatherForecastItem> HandleFilter(
        WeatherForecastProjection projector,
        GetWeatherForecastListQuery query,
        IQueryContext context)
    {
        var forecasts = projector.GetCurrentForecasts();
        return forecasts.Values.AsEnumerable();
    }

    public static IEnumerable<WeatherForecastItem> HandleSort(
        IEnumerable<WeatherForecastItem> filteredList,
        GetWeatherForecastListQuery query,
        IQueryContext context) =>
        filteredList.OrderByDescending(f => f.Date);

    // Wait for sortable unique ID (from IWaitForSortableUniqueId)
    [Id(2)]
    public string? WaitForSortableUniqueId { get; init; }
}
