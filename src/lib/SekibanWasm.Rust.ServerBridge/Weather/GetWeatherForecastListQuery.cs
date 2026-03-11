using Sekiban.Dcb.Queries;

namespace SekibanWasm.Rust.Domain.Weather;

public record GetWeatherForecastListQuery :
    IMultiProjectionListQuery<WeatherForecastMultiProjection, GetWeatherForecastListQuery, WeatherForecastItem>,
    IQueryPagingParameter
{
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }

    public static IEnumerable<WeatherForecastItem> HandleFilter(
        WeatherForecastMultiProjection projector,
        GetWeatherForecastListQuery query,
        IQueryContext context) =>
        projector.Forecasts.Values.Where(x => !x.IsDeleted);

    public static IEnumerable<WeatherForecastItem> HandleSort(
        IEnumerable<WeatherForecastItem> filteredList,
        GetWeatherForecastListQuery query,
        IQueryContext context) =>
        filteredList.OrderByDescending(x => x.CreatedAt);
}
