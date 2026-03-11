namespace SekibanWasm.Rust.Web;

public record WeatherForecastListItem(
    string ForecastId,
    string Location,
    int TemperatureC,
    string Summary);

public record CreateWeatherForecastRequest(
    string ForecastId,
    string Location,
    int TemperatureC,
    string Summary);

public record CommandResponse(
    bool Success,
    string? Error,
    string? SortableUniqueId,
    string? ForecastId = null);
