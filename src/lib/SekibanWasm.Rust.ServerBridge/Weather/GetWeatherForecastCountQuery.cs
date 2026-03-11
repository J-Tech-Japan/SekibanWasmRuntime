using Sekiban.Dcb.Queries;

namespace SekibanWasm.Rust.Domain.Weather;

public record GetWeatherForecastCountQuery :
    IMultiProjectionQuery<WeatherForecastMultiProjection, GetWeatherForecastCountQuery, CountResult>
{
    public string? LocationFilter { get; init; }

    public static CountResult HandleQuery(
        WeatherForecastMultiProjection projector,
        GetWeatherForecastCountQuery query,
        IQueryContext context)
    {
        var items = projector.Forecasts.Values.Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(query.LocationFilter))
        {
            items = items.Where(x => x.Location.Contains(query.LocationFilter, StringComparison.OrdinalIgnoreCase));
        }

        return new CountResult(items.Count());
    }
}

public record CountResult(int Count);
