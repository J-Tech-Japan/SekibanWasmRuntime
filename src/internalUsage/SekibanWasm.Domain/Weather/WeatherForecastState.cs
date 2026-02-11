using Sekiban.Dcb.Tags;

namespace SekibanWasm.Domain.Weather;

public record WeatherForecastState(
    string ForecastId,
    string Location,
    int TemperatureC,
    string Summary,
    DateTimeOffset CreatedAt,
    bool IsDeleted = false,
    DateTimeOffset? DeletedAt = null) : ITagStatePayload
{
    public static WeatherForecastState Empty =>
        new("", "", 0, "", DateTimeOffset.MinValue);
}
