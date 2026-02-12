namespace SekibanWasm.Cs.Domain.Weather;

public record WeatherForecastItem(
    string ForecastId,
    string Location,
    int TemperatureC,
    string Summary,
    DateTimeOffset CreatedAt,
    bool IsDeleted = false,
    DateTimeOffset? DeletedAt = null);
