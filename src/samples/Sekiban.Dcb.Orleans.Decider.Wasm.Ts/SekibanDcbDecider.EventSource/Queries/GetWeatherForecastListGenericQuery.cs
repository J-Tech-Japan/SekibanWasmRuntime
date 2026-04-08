using Dcb.EventSource.Projections;
using Orleans;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;

namespace Dcb.EventSource.Queries;

[GenerateSerializer]
public record GetWeatherForecastListGenericQuery :
    IMultiProjectionListQuery<WeatherForecastProjection, GetWeatherForecastListGenericQuery, WeatherForecastItem>,
    IWaitForSortableUniqueId,
    IQueryPagingParameter
{
    // Paging parameters
    [Id(0)] public int? PageNumber { get; init; }
    [Id(1)] public int? PageSize { get; init; }

    public static IEnumerable<WeatherForecastItem> HandleFilter(
        WeatherForecastProjection projector,
        GetWeatherForecastListGenericQuery query,
        IQueryContext context)
    {
        return projector.GetCurrentForecasts().Values;
    }

    public static IEnumerable<WeatherForecastItem> HandleSort(
        IEnumerable<WeatherForecastItem> filteredList,
        GetWeatherForecastListGenericQuery query,
        IQueryContext context) =>
        filteredList.OrderByDescending(f => f.Date);

    // Wait for sortable unique ID
    [Id(2)]
    public string? WaitForSortableUniqueId { get; init; }
}
